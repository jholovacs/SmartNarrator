using System.Diagnostics;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SmartNarrator.Application.Analysis;
using SmartNarrator.Application.Ports;
using SmartNarrator.Domain.Entities;
using SmartNarrator.Domain.Enums;
using SmartNarrator.Infrastructure.Ai;
using SmartNarrator.Infrastructure.Options;
using SmartNarrator.Infrastructure.Persistence;
using SmartNarrator.Infrastructure.Text;

namespace SmartNarrator.Infrastructure.Services;

/// <summary>
/// Analyze pipeline: quoted dialogue via punctuation rules, then LLM character attribution + narrator gaps.
/// Dialogue spans merge by canonical UTF‑16 span identity (<c>(StartOffset, EndOffset)</c>, plus chapter scope)
/// so repeat analyzes preserve stable entity IDs where spans match.
/// </summary>
public sealed class StoryPhasedAnalysisOrchestrator(
    SmartNarratorDbContext db,
    OllamaStoryAnalysisClient ollama,
    IOptions<OllamaOptions> ollamaOptions,
    IAnalyzeLlmDiagnosticsSink llmDiagnostics,
    ILogger<StoryPhasedAnalysisOrchestrator> logger)
{
    private readonly double _speakerReviewThreshold =
        Math.Clamp(ollamaOptions.Value.SpeakerConfidenceNeedsReviewThreshold, 0d, 1d);
    private readonly double _speakerAutoTrustThreshold =
        Math.Clamp(ollamaOptions.Value.SpeakerConfidenceAutoTrustThreshold, 0d, 1d);

    private readonly OllamaOptions _ollamaOptsSnapshot = ollamaOptions.Value;

    public async Task RunAsync(BackgroundJobEntity job, IJobRealtimeNotifier notifier, CancellationToken cancellationToken)
    {
        Task BumpAsync() => notifier.NotifyJobUpdatedAsync(job.Id, cancellationToken);

        if (job.WorkId is null)
            throw new InvalidOperationException("Analyze job missing WorkId.");

        var workId = job.WorkId.Value;
        var work = await db.Works.AsNoTracking().SingleAsync(w => w.Id == workId, cancellationToken)
            .ConfigureAwait(false);

        var canonical = work.CanonicalText ?? "";
        if (canonical.Length == 0)
            throw new InvalidOperationException("Canonical text is empty; nothing to analyze.");

        job.ProgressPhase = "Phase 1 — loading chapters…";
        job.ProgressPercent = 12;
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        await BumpAsync().ConfigureAwait(false);

        var chapters = await db.WorkChapters.Where(c => c.WorkId == workId).OrderBy(c => c.OrderIndex)
            .ToListAsync(cancellationToken).ConfigureAwait(false);

        if (chapters.Count == 0)
        {
            await InsertFallbackChapterAsync(workId, canonical.Length, cancellationToken).ConfigureAwait(false);
            chapters = await db.WorkChapters.Where(c => c.WorkId == workId).OrderBy(c => c.OrderIndex)
                .ToListAsync(cancellationToken).ConfigureAwait(false);
        }

        job.ProgressPhase = $"Phase 2 — detecting quoted dialogue ({chapters.Count} chapters)…";
        job.ProgressPercent = 18;
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        await BumpAsync().ConfigureAwait(false);

        const double quotedConfidence = 0.92;
        var dialogueChapterTotal = Math.Max(chapters.Count, 1);

        for (var chIdx = 0; chIdx < chapters.Count; chIdx++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await ThrowIfAnalyzeJobStoppedAsync(job.Id, cancellationToken).ConfigureAwait(false);

            var ch = chapters[chIdx];
            var chLabel = $"{chIdx + 1}/{dialogueChapterTotal} (#{ch.OrderIndex + 1})";

            job.ProgressPhase = $"Phase 2 · chapter {chLabel} · scanning quoted spans…";
            job.ProgressPercent = Phase2DialoguePercent(chIdx, dialogueChapterTotal, 0.12);
            await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            await BumpAsync().ConfigureAwait(false);

            var len = ch.EndOffset - ch.StartOffset;
            if (len <= 0)
            {
                job.ProgressPhase = $"Phase 2 · chapter {chLabel} · empty slice, skipped";
                job.ProgressPercent = Phase2DialoguePercent(chIdx, dialogueChapterTotal, 0.92);
                await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
                await BumpAsync().ConfigureAwait(false);
                continue;
            }

            var sw = Stopwatch.StartNew();
            var relative = QuotedSpeechSpanDetector.Detect(canonical.AsSpan(ch.StartOffset, len), cancellationToken);
            sw.Stop();
            logger.LogInformation(
                "Phase 2 dialogue scan: chapter {Chapter} ({ChapterId}), sliceLen {SliceLen}, spans {SpanCount}, elapsed {ElapsedMs}ms",
                chLabel, ch.Id, len, relative.Count, sw.ElapsedMilliseconds);

            job.ProgressPhase =
                $"Phase 2 · chapter {chLabel} · merging {relative.Count} dialogue spans…";
            job.ProgressPercent = Phase2DialoguePercent(chIdx, dialogueChapterTotal, 0.55);
            await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            await BumpAsync().ConfigureAwait(false);

            var proposals = relative
                .Select(r => (
                    Start: ch.StartOffset + r.Start,
                    End: ch.StartOffset + r.End,
                    Kind: SpeakerKind.Character,
                    Confidence: quotedConfidence))
                .OrderBy(p => p.Start)
                .ToList();

            sw.Restart();
            await MergeAiDialogueSpansAsync(workId, ch.Id, proposals, cancellationToken).ConfigureAwait(false);
            sw.Stop();
            logger.LogInformation(
                "Phase 2 dialogue merge: chapter {Chapter}, elapsed {ElapsedMs}ms",
                chLabel, sw.ElapsedMilliseconds);

            job.ProgressPhase = $"Phase 2 · chapter {chLabel} · done";
            job.ProgressPercent = Phase2DialoguePercent(chIdx, dialogueChapterTotal, 0.92);
            await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            await BumpAsync().ConfigureAwait(false);
        }

        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        var accChars = new Dictionary<string, CharacterSuggestionDto>(StringComparer.OrdinalIgnoreCase);
        var pendingUtters = new List<PendingUtterance>();

        var chapterTotalP3 = Math.Max(chapters.Count, 1);
        const string phase3Prefix = "Phase 3 · infer characters & link dialogue · chapter ";

        for (var chIdx = 0; chIdx < chapters.Count; chIdx++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await ThrowIfAnalyzeJobStoppedAsync(job.Id, cancellationToken).ConfigureAwait(false);

            accChars.Clear();
            await SeedCharacterRegistryFromDbAsync(workId, accChars, cancellationToken).ConfigureAwait(false);

            var ch = chapters[chIdx];
            var chLabel = $"{chIdx + 1}/{chapterTotalP3} (#{ch.OrderIndex + 1})";

            job.ProgressPhase = $"{phase3Prefix}{chLabel} · loading dialogue spans…";
            job.ProgressPercent = Phase3AiPercent(chIdx, chapterTotalP3, 0.06);
            await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            await BumpAsync().ConfigureAwait(false);

            var spans = await db.DialogueSpans.Where(d => d.ChapterId == ch.Id).OrderBy(d => d.OrderIndexInChapter)
                .ToListAsync(cancellationToken).ConfigureAwait(false);
            if (spans.Count == 0)
            {
                job.ProgressPhase = $"{phase3Prefix}{chLabel} · no quoted dialogue — skipped";
                job.ProgressPercent = Phase3AiPercent(chIdx, chapterTotalP3, 0.98);
                await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
                await BumpAsync().ConfigureAwait(false);
                continue;
            }

            job.ProgressPhase = $"{phase3Prefix}{chLabel} · building model prompt…";
            job.ProgressPercent = Phase3AiPercent(chIdx, chapterTotalP3, 0.2);
            await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            await BumpAsync().ConfigureAwait(false);

            var catalogRows = spans.Select(s => new DialogueCatalogRow(
                s.OrderIndexInChapter,
                s.StartOffset,
                s.EndOffset,
                TrimCanon(canonical, s.StartOffset, s.EndOffset, 420))).ToList();

            var catalogJson = JsonSerializer.Serialize(catalogRows,
                new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower });

            var priorJson = accChars.Count > 0 ? StoryAnalysisPriorRegistryFormatter.ToJson(accChars.Values) : null;

            var prompt = StoryPhasedAnalysisPrompts.CharacterDialoguePrompt(ch.Title, ch.OrderIndex, catalogJson,
                priorJson);

            job.ProgressPhase = $"{phase3Prefix}{chLabel} · inferring via Ollama…";
            job.ProgressPercent = Phase3AiPercent(chIdx, chapterTotalP3, 0.48);
            await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            await BumpAsync().ConfigureAwait(false);

            var json =
                await ollama.CompleteStructuredPromptAsync(PhasedAnalysisSchemas.CharacterAssignmentRoot(), prompt,
                    cancellationToken).ConfigureAwait(false);

            await llmDiagnostics.RecordTurnAsync(job.Id, work.Id, $"{phase3Prefix}{chLabel}",
                string.IsNullOrWhiteSpace(_ollamaOptsSnapshot.Model) ? "(model)" : _ollamaOptsSnapshot.Model.Trim(), prompt,
                json, cancellationToken).ConfigureAwait(false);

            job.ProgressPhase = $"{phase3Prefix}{chLabel} · merging assignments…";
            job.ProgressPercent = Phase3AiPercent(chIdx, chapterTotalP3, 0.72);
            await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            await BumpAsync().ConfigureAwait(false);

            var parsedRaw = PhasedAnalysisJsonParser.ParseCharacterAssignmentPhase(json, logger);
            var parsed = PhasedAnalysisJsonParser.CoalesceBrokenSpeakerAssignments(parsedRaw, logger);

            foreach (var c in parsed.Characters)
                StoryAnalysisAccumulator.MergeCharacters(accChars,
                    [MapAssignmentCharacter(c)]);

            EnsurePlaceholderKeys(accChars, parsed.Assignments);

            var assignByOrder = parsed.Assignments
                .GroupBy(a => a.OrderIndex)
                .ToDictionary(g => g.Key, g => g.Last());
            var orderBias = DetectAssignmentOrderBias(spans, parsed.Assignments);
            var degenerateOrder = AssignmentsUseDegenerateOrderIndex(parsed.Assignments);

            var chapterPending = new List<PendingUtterance>(spans.Count);
            for (var spanIx = 0; spanIx < spans.Count; spanIx++)
            {
                var span = spans[spanIx];
                PhasedAnalysisJsonParser.CharacterAssignmentRow? ais = null;

                var posOk = parsed.Assignments.Count == spans.Count && spanIx < parsed.Assignments.Count;
                if (degenerateOrder && posOk)
                    ais = parsed.Assignments[spanIx];
                else if (assignByOrder.TryGetValue(span.OrderIndexInChapter + orderBias, out var fromDict))
                    ais = fromDict;
                if (ais is null && posOk)
                    ais = parsed.Assignments[spanIx];

                var notSpeech = ais?.NotSpeech == true;
                var uncertain = !notSpeech && ais?.Uncertain == true;
                var conf = ais?.Confidence ?? span.Confidence;

                string? key = null;
                if (!notSpeech)
                {
                    key = string.IsNullOrWhiteSpace(ais?.CharacterExternalKey)
                        ? null
                        : ais!.CharacterExternalKey!.Trim();
                }

                if (!notSpeech && string.IsNullOrWhiteSpace(key) && conf >= 0.75
                    && parsed.Characters.Count == 1)
                {
                    var sole = parsed.Characters[0].ExternalKey.Trim();
                    if (!string.IsNullOrWhiteSpace(sole))
                        key = sole;
                }

                var utterKind = notSpeech ? SpeakerKind.QuotedNonSpeech : SpeakerKind.Character;

                chapterPending.Add(new PendingUtterance(
                    span.StartOffset,
                    span.EndOffset,
                    utterKind,
                    key,
                    conf,
                    uncertain));
            }

            pendingUtters.AddRange(chapterPending);

            job.ProgressPhase = $"{phase3Prefix}{chLabel} · committing profiles & utterances…";
            job.ProgressPercent = Phase3AiPercent(chIdx, chapterTotalP3, 0.86);
            await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            await BumpAsync().ConfigureAwait(false);

            var supplementalCharacterKeys = new Dictionary<string, Guid>(StringComparer.OrdinalIgnoreCase);
            await MergeAiCharactersAsync(workId, accChars, supplementalCharacterKeys, cancellationToken)
                .ConfigureAwait(false);

            var chapterKeyMap =
                await LoadAiCharacterKeyMapAsync(workId, cancellationToken).ConfigureAwait(false);
            MergeSupplementalCharacterKeys(chapterKeyMap, supplementalCharacterKeys);
            var chapterProfiles =
                await db.Characters.Where(c => c.WorkId == workId).AsNoTracking()
                    .ToListAsync(cancellationToken).ConfigureAwait(false);
            await UpsertPendingUtterancesAsync(workId, chapterPending, chapterKeyMap, chapterProfiles,
                    cancellationToken)
                .ConfigureAwait(false);

            job.ProgressPhase = $"{phase3Prefix}{chLabel} · chapter saved";
            job.ProgressPercent = Phase3AiPercent(chIdx, chapterTotalP3, 0.97);
            await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            await BumpAsync().ConfigureAwait(false);
        }

        accChars.Clear();
        await SeedCharacterRegistryFromDbAsync(workId, accChars, cancellationToken).ConfigureAwait(false);

        EnsurePlaceholderKeys(accChars,
            pendingUtters.Where(p =>
                    p.SpeakerKind == SpeakerKind.Character &&
                    !string.IsNullOrWhiteSpace(p.CharacterExternalKey))
                .Select(p =>
                    new PhasedAnalysisJsonParser.CharacterAssignmentRow(0, p.CharacterExternalKey, false, 1)));

        job.ProgressPhase = "Phase 3 · applying cross-chapter speaker placeholders…";
        job.ProgressPercent = 80;
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        await BumpAsync().ConfigureAwait(false);

        var supplementalCharacterKeysFinal =
            new Dictionary<string, Guid>(StringComparer.OrdinalIgnoreCase);
        await MergeAiCharactersAsync(workId, accChars, supplementalCharacterKeysFinal, cancellationToken)
            .ConfigureAwait(false);

        job.ProgressPhase = "Phase 3 · reconciling utterance rows & pruning superseded lines…";
        job.ProgressPercent = 82;
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        await BumpAsync().ConfigureAwait(false);

        var keyToCharacterId = await LoadAiCharacterKeyMapAsync(workId, cancellationToken).ConfigureAwait(false);
        MergeSupplementalCharacterKeys(keyToCharacterId, supplementalCharacterKeysFinal);

        await MergeAiUtterancesAsync(workId, pendingUtters, keyToCharacterId, cancellationToken)
            .ConfigureAwait(false);

        job.ProgressPhase = "Phase 3 · building narrator passages…";
        job.ProgressPercent = 87;
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        await BumpAsync().ConfigureAwait(false);

        await MergeAiNarrativeGapsAsync(workId, chapters, cancellationToken).ConfigureAwait(false);

        job.ProgressPhase = "Phase 3 · cleaning unused profiles…";
        job.ProgressPercent = 89;
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        await BumpAsync().ConfigureAwait(false);

        var retainedCharacterKeys = accChars.Keys.ToHashSet(StringComparer.OrdinalIgnoreCase);
        await MergeAiCharacterCleanupAsync(workId, retainedCharacterKeys, cancellationToken).ConfigureAwait(false);

        if (canonical.Length >= 200 && pendingUtters.Count == 0)
        {
            logger.LogWarning(
                "Analyze produced no utterances for work {WorkId} (no quoted spans detected or catalog empty).",
                workId);
        }

        job.ProgressPercent = 90;
        job.ProgressPhase = "Analysis phases complete.";
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        await BumpAsync().ConfigureAwait(false);

        logger.LogInformation(
            "Phased analyze finished work {WorkId}: chapters={Ch}, utterances={U}, characters={C}",
            workId, chapters.Count, pendingUtters.Count, accChars.Count);
    }

    /// <summary>
    /// Same decision as GET /jobs/{id}: row missing ⇒ deleted; cooperatively stop like cooperative cancel.
    /// </summary>
    private async Task ThrowIfAnalyzeJobStoppedAsync(Guid jobId, CancellationToken cancellationToken)
    {
        var snap = await db.BackgroundJobs.AsNoTracking()
            .Where(j => j.Id == jobId)
            .Select(j => new { j.CancellationRequested })
            .SingleOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);

        if (snap is null)
            throw new OperationCanceledException("Analyze job was deleted.");

        if (snap.CancellationRequested)
            throw new OperationCanceledException("Analyze job cancellation was requested.");
    }

    /// <summary>
    /// Hydrates Phase 3 prior-registry from Postgres so user-edited aliases / summaries survive re-analyze.
    /// </summary>
    private async Task SeedCharacterRegistryFromDbAsync(Guid workId,
        Dictionary<string, CharacterSuggestionDto> accChars,
        CancellationToken cancellationToken)
    {
        var rows = await db.Characters.AsNoTracking()
            .Where(c => c.WorkId == workId && c.AiExternalKey != null)
            .ToListAsync(cancellationToken).ConfigureAwait(false);

        foreach (var row in rows)
        {
            var key = row.AiExternalKey!.Trim();
            if (string.IsNullOrEmpty(key))
                continue;

            List<string>? aliases = null;
            if (!string.IsNullOrWhiteSpace(row.AliasesJson))
            {
                try
                {
                    aliases = JsonSerializer.Deserialize<List<string>>(row.AliasesJson);
                }
                catch (JsonException ex)
                {
                    logger.LogWarning(ex, "Ignored invalid AliasesJson for character row {CharacterId}", row.Id);
                }
            }

            StoryAnalysisAccumulator.MergeCharacters(accChars,
            [
                new CharacterSuggestionDto
                {
                    ExternalKey = key,
                    Name = string.IsNullOrWhiteSpace(row.Name) ? key : row.Name.Trim(),
                    Aliases = aliases,
                    PersonalitySummary = row.PersonalitySummary,
                    SpeechStyleSummary = row.SpeechStyleSummary,
                    GenderPresentation = row.GenderPresentation,
                    Tone = row.Tone,
                    Accent = row.Accent,
                    Breathiness = row.Breathiness,
                    SpeakingPace = row.SpeakingPace,
                },
            ]);
        }
    }

    /// <summary>Ollama often emits duplicate order_index (e.g. all 0) — treat array order as span order instead.</summary>
    private static bool AssignmentsUseDegenerateOrderIndex(
        IReadOnlyList<PhasedAnalysisJsonParser.CharacterAssignmentRow> assignments)
    {
        if (assignments.Count <= 1)
            return false;
        var first = assignments[0].OrderIndex;
        return assignments.All(a => a.OrderIndex == first);
    }

    /// <summary>Models often emit 1-based order_index; dialogue spans use 0-based OrderIndexInChapter.</summary>
    private static int DetectAssignmentOrderBias(IReadOnlyList<DialogueSpanEntity> spans,
        IReadOnlyList<PhasedAnalysisJsonParser.CharacterAssignmentRow> assignments)
    {
        if (assignments.Count == 0 || spans.Count == 0)
            return 0;

        var keys = assignments.Select(a => a.OrderIndex).ToHashSet();
        bool Covers(int delta) => spans.All(s => keys.Contains(s.OrderIndexInChapter + delta));
        if (Covers(0))
            return 0;
        if (Covers(1))
            return 1;
        return 0;
    }

    private async Task InsertFallbackChapterAsync(Guid workId, int canonicalLength,
        CancellationToken cancellationToken)
    {
        db.WorkChapters.Add(new WorkChapterEntity
        {
            Id = Guid.NewGuid(),
            WorkId = workId,
            OrderIndex = 0,
            StartOffset = 0,
            EndOffset = canonicalLength,
            Title = "Full text",
            Notes = string.Empty,
            IsAiSuggested = false,
        });

        db.StoryStructureSections.Add(new StoryStructureSectionEntity
        {
            Id = Guid.NewGuid(),
            WorkId = workId,
            Kind = StoryStructureSectionKind.Chapter,
            StartOffset = 0,
            EndOffset = canonicalLength,
            Title = "Full text",
            Notes = string.Empty,
            IsAiSuggested = false,
        });

        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Maps Phase 3 LLM chapter passes into ~62–79% before persistence substeps (~81–89%).</summary>
    private static int Phase3AiPercent(int chapterIndex, int chapterTotal, double phaseWithinChapter01)
    {
        chapterTotal = Math.Max(chapterTotal, 1);
        var t = Math.Clamp(phaseWithinChapter01, 0d, 1d);
        var f = (chapterIndex + t) / chapterTotal;
        return Math.Min(79, 62 + (int)Math.Round(17.0 * f));
    }

    /// <summary>Maps Phase 2 progress into ~18–58% before Phase 3 starts at 62%.</summary>
    private static int Phase2DialoguePercent(int chapterIndex, int chapterTotal, double phaseWithinChapter01)
    {
        chapterTotal = Math.Max(chapterTotal, 1);
        var t = Math.Clamp(phaseWithinChapter01, 0d, 1d);
        var f = (chapterIndex + t) / chapterTotal;
        return Math.Min(58, 18 + (int)Math.Round(40.0 * f));
    }

    private async Task MergeAiDialogueSpansAsync(Guid workId, Guid chapterId,
        IReadOnlyList<(int Start, int End, SpeakerKind Kind, double Confidence)> proposalsSorted,
        CancellationToken cancellationToken)
    {
        if (proposalsSorted.Count == 0)
        {
            await db.DialogueSpans.Where(d => d.ChapterId == chapterId && d.IsAiSuggested)
                .ExecuteDeleteAsync(cancellationToken).ConfigureAwait(false);
            return;
        }

        var proposalKeys = proposalsSorted.Select(p => (p.Start, p.End)).ToHashSet();

        var survivors =
            await db.DialogueSpans.Where(d => d.ChapterId == chapterId && d.IsAiSuggested)
                .ToListAsync(cancellationToken).ConfigureAwait(false);

        foreach (var dead in survivors.Where(e => !proposalKeys.Contains((e.StartOffset, e.EndOffset))))
            db.DialogueSpans.Remove(dead);

        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        survivors =
            await db.DialogueSpans.Where(d => d.ChapterId == chapterId && d.IsAiSuggested)
                .ToListAsync(cancellationToken).ConfigureAwait(false);

        var byOffsets = survivors.GroupBy(e => (e.StartOffset, e.EndOffset))
            .ToDictionary(g => g.Key, g => g.First());

        for (var i = 0; i < proposalsSorted.Count; i++)
        {
            var p = proposalsSorted[i];
            if (byOffsets.TryGetValue((p.Start, p.End), out var hit))
            {
                hit.SpeakerKind = p.Kind;
                hit.Confidence = p.Confidence;
                hit.WorkId = workId;
                hit.OrderIndexInChapter = i;
            }
            else
            {
                var neo = new DialogueSpanEntity
                {
                    Id = Guid.NewGuid(),
                    WorkId = workId,
                    ChapterId = chapterId,
                    OrderIndexInChapter = i,
                    StartOffset = p.Start,
                    EndOffset = p.End,
                    SpeakerKind = p.Kind,
                    Confidence = p.Confidence,
                    IsAiSuggested = true,
                };
                db.DialogueSpans.Add(neo);
                byOffsets[(p.Start, p.End)] = neo;
            }
        }

        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    private async Task MergeAiCharactersAsync(Guid workId, Dictionary<string, CharacterSuggestionDto> accChars,
        Dictionary<string, Guid> supplementalExternalKeyToCharacterId,
        CancellationToken cancellationToken)
    {
        var allRows =
            await db.Characters.Where(c => c.WorkId == workId).ToListAsync(cancellationToken).ConfigureAwait(false);

        var byKey = allRows
            .Where(c => !string.IsNullOrWhiteSpace(c.AiExternalKey))
            .ToDictionary(c => c.AiExternalKey!.Trim(), StringComparer.OrdinalIgnoreCase);

        foreach (var c in accChars.Values.OrderBy(x => x.ExternalKey, StringComparer.OrdinalIgnoreCase))
        {
            var extKey = c.ExternalKey.Trim();

            if (byKey.TryGetValue(extKey, out var row))
            {
                if (row.UserApproved || !row.IsAiSuggested)
                    AnalysisCharacterResolution.MergeCumulativeSuggestionIntoEstablished(row, c);
                else
                    AnalysisCharacterResolution.ApplyCumulativeSuggestion(row, c);

                continue;
            }

            var match = AnalysisCharacterResolution.TryFindUniqueEstablishedMatch(allRows, c);
            if (match is not null)
            {
                supplementalExternalKeyToCharacterId[extKey] = match.Id;

                if (match.UserApproved || !match.IsAiSuggested)
                    AnalysisCharacterResolution.MergeCumulativeSuggestionIntoEstablished(match, c);
                else
                    AnalysisCharacterResolution.ApplyCumulativeSuggestion(match, c);

                if (string.IsNullOrWhiteSpace(match.AiExternalKey))
                    match.AiExternalKey = extKey;

                byKey[extKey] = match;
                continue;
            }

            var created = AnalysisCharacterResolution.CreateProfileFromSuggestion(workId, c);
            db.Characters.Add(created);
            allRows.Add(created);
            byKey[extKey] = created;
        }

        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    private static void MergeSupplementalCharacterKeys(Dictionary<string, Guid> primary,
        Dictionary<string, Guid> supplemental)
    {
        foreach (var kv in supplemental)
            primary[kv.Key] = kv.Value;
    }

    private Task<Dictionary<string, Guid>> LoadAiCharacterKeyMapAsync(Guid workId,
        CancellationToken cancellationToken) =>
        db.Characters.Where(x => x.WorkId == workId && x.AiExternalKey != null)
            .ToDictionaryAsync(x => x.AiExternalKey!, x => x.Id, StringComparer.OrdinalIgnoreCase, cancellationToken);

    /// <summary>
    /// Upserts AI utterances for the given slice (Phase 3 commits per chapter). Does not delete stale rows —
    /// see <see cref="RemoveStaleAiUtterancesAsync"/>.
    /// </summary>
    private async Task UpsertPendingUtterancesAsync(Guid workId, IReadOnlyList<PendingUtterance> slice,
        IReadOnlyDictionary<string, Guid> keyToCharacterId,
        IReadOnlyList<CharacterProfileEntity> workCharacters,
        CancellationToken cancellationToken)
    {
        if (slice.Count == 0)
            return;

        var offsetKeys = slice.Select(p => $"{p.StartOffset}\u001f{p.EndOffset}").ToHashSet(StringComparer.Ordinal);

        var utterBucket =
            (await db.Utterances.Where(u => u.WorkId == workId && u.IsAiSuggested).ToListAsync(cancellationToken)
                .ConfigureAwait(false))
            .Where(u => offsetKeys.Contains($"{u.StartOffset}\u001f{u.EndOffset}"))
            .ToList();

        var byOffsets = utterBucket.GroupBy(u => (u.StartOffset, u.EndOffset))
            .ToDictionary(g => g.Key, g => g.First());

        foreach (var pu in slice.OrderBy(p => p.StartOffset))
        {
            Guid? charId = null;
            if (pu.SpeakerKind == SpeakerKind.Character)
            {
                charId = UtteranceCharacterLinker.ResolveCharacterId(
                    pu.CharacterExternalKey,
                    pu.Confidence,
                    _speakerAutoTrustThreshold,
                    keyToCharacterId,
                    workCharacters);
                if (charId is null && byOffsets.TryGetValue((pu.StartOffset, pu.EndOffset), out var preserve))
                    charId = preserve.CharacterId;
            }

            var highTrust = pu.Confidence >= _speakerAutoTrustThreshold;
            var needsReview = pu.SpeakerKind == SpeakerKind.Character &&
                              (charId is null ||
                               (!highTrust && (pu.Uncertain || pu.Confidence < _speakerReviewThreshold)));

            if (pu.SpeakerKind == SpeakerKind.QuotedNonSpeech)
            {
                charId = null;
                needsReview = false;
            }

            if (byOffsets.TryGetValue((pu.StartOffset, pu.EndOffset), out var row))
            {
                row.SpeakerKind = pu.SpeakerKind;
                row.CharacterId = charId;
                row.Confidence = pu.Confidence;
                row.SpeakerNeedsReview = needsReview;
                row.IsAiSuggested = true;
            }
            else
            {
                var neo = new UtteranceEntity
                {
                    Id = Guid.NewGuid(),
                    WorkId = workId,
                    StartOffset = pu.StartOffset,
                    EndOffset = pu.EndOffset,
                    SpeakerKind = pu.SpeakerKind,
                    CharacterId = charId,
                    Confidence = pu.Confidence,
                    SpeakerNeedsReview = needsReview,
                    IsAiSuggested = true,
                    UserApproved = false,
                };
                db.Utterances.Add(neo);
                byOffsets[(pu.StartOffset, pu.EndOffset)] = neo;
            }
        }

        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Removes AI-suggested utterances whose spans are no longer part of the latest merged catalogue.</summary>
    private async Task RemoveStaleAiUtterancesAsync(Guid workId,
        HashSet<(int StartOffset, int EndOffset)> keepKeys,
        CancellationToken cancellationToken)
    {
        var utterBucket =
            await db.Utterances.Where(u => u.WorkId == workId && u.IsAiSuggested && !u.UserApproved)
                .ToListAsync(cancellationToken).ConfigureAwait(false);

        foreach (var u in utterBucket.Where(u => !keepKeys.Contains((u.StartOffset, u.EndOffset))))
            db.Utterances.Remove(u);

        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Re-upserts all pending utterances (placeholder FK fixes), then deletes superseded AI rows.</summary>
    private async Task MergeAiUtterancesAsync(Guid workId, IReadOnlyList<PendingUtterance> pendingUtters,
        IReadOnlyDictionary<string, Guid> keyToCharacterId, CancellationToken cancellationToken)
    {
        var keepKeys = pendingUtters.Select(p => (p.StartOffset, p.EndOffset)).ToHashSet();

        var profiles =
            await db.Characters.Where(c => c.WorkId == workId).AsNoTracking()
                .ToListAsync(cancellationToken).ConfigureAwait(false);
        await UpsertPendingUtterancesAsync(workId, pendingUtters, keyToCharacterId, profiles, cancellationToken)
            .ConfigureAwait(false);
        await RemoveStaleAiUtterancesAsync(workId, keepKeys, cancellationToken).ConfigureAwait(false);
    }

    private async Task MergeAiNarrativeGapsAsync(Guid workId, IReadOnlyList<WorkChapterEntity> chapters,
        CancellationToken cancellationToken)
    {
        var gapTuples = new List<(int Start, int End)>();

        foreach (var ch in chapters)
        {
            var pairRows =
                await db.DialogueSpans.AsNoTracking().Where(d => d.ChapterId == ch.Id).OrderBy(d => d.StartOffset)
                    .Select(d => new { d.StartOffset, d.EndOffset }).ToListAsync(cancellationToken)
                    .ConfigureAwait(false);
            var inChapter = pairRows.Select(p => (p.StartOffset, p.EndOffset)).ToList();

            foreach (var gap in NarrativeGapBuilder.GapsForChapter(ch.StartOffset, ch.EndOffset, inChapter))
                gapTuples.Add((gap.StartOffset, gap.EndOffset));
        }

        var gapKeys = gapTuples.ToHashSet();

        var narrBucket =
            await db.NarrativePassages.Where(p => p.WorkId == workId && p.IsAiSuggested)
                .ToListAsync(cancellationToken).ConfigureAwait(false);

        foreach (var n in narrBucket.Where(n => !gapKeys.Contains((n.StartOffset, n.EndOffset))))
            db.NarrativePassages.Remove(n);

        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        narrBucket =
            await db.NarrativePassages.Where(p => p.WorkId == workId && p.IsAiSuggested)
                .ToListAsync(cancellationToken).ConfigureAwait(false);

        var byOffsets = narrBucket.GroupBy(n => (n.StartOffset, n.EndOffset))
            .ToDictionary(g => g.Key, g => g.First());

        foreach (var (start, end) in gapTuples.OrderBy(g => g.Start))
        {
            if (byOffsets.TryGetValue((start, end), out var row))
            {
                row.NarratorCharacterId = null;
                row.PerspectiveNotes = string.Empty;
                row.GenderPresentation = "unspecified";
                row.Tone = "neutral";
                row.Accent = "none";
                row.Breathiness = "normal";
                row.SpeakingPace = "normal";
                row.IsAiSuggested = true;
            }
            else
            {
                db.NarrativePassages.Add(new NarrativePassageEntity
                {
                    Id = Guid.NewGuid(),
                    WorkId = workId,
                    StartOffset = start,
                    EndOffset = end,
                    NarratorCharacterId = null,
                    PerspectiveNotes = string.Empty,
                    GenderPresentation = "unspecified",
                    Tone = "neutral",
                    Accent = "none",
                    Breathiness = "normal",
                    SpeakingPace = "normal",
                    IsAiSuggested = true,
                });
            }
        }

        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    private async Task MergeAiCharacterCleanupAsync(Guid workId, HashSet<string> retainedExternalKeysIgnoreCase,
        CancellationToken cancellationToken)
    {
        var aiPool =
            await db.Characters.Where(c => c.WorkId == workId && c.IsAiSuggested && !c.UserApproved &&
                                           c.AiExternalKey != null)
                .ToListAsync(cancellationToken).ConfigureAwait(false);

        foreach (var row in aiPool)
        {
            if (retainedExternalKeysIgnoreCase.Contains(row.AiExternalKey!))
                continue;

            var utterRefs =
                await db.Utterances.AnyAsync(u => u.CharacterId == row.Id, cancellationToken).ConfigureAwait(false);
            var narrRefs =
                await db.NarrativePassages.AnyAsync(p => p.NarratorCharacterId == row.Id, cancellationToken)
                    .ConfigureAwait(false);

            if (!utterRefs && !narrRefs)
                db.Characters.Remove(row);
        }

        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    private static CharacterSuggestionDto MapAssignmentCharacter(PhasedAnalysisJsonParser.CharacterAssignmentCharacterRow r) =>
        new()
        {
            ExternalKey = r.ExternalKey,
            Name = string.IsNullOrWhiteSpace(r.Name) ? r.ExternalKey : r.Name,
            Aliases = r.Aliases is { Count: > 0 } ? r.Aliases : null,
            PersonalitySummary = r.PersonalitySummary,
            SpeechStyleSummary = r.SpeechStyleNotes,
            GenderPresentation = string.IsNullOrWhiteSpace(r.GenderPresentation) ? "unspecified" : r.GenderPresentation,
            Tone = string.IsNullOrWhiteSpace(r.Tone) ? "neutral" : r.Tone,
            Accent = string.IsNullOrWhiteSpace(r.Accent) ? "none" : r.Accent,
            Breathiness = string.IsNullOrWhiteSpace(r.Breathiness) ? "normal" : r.Breathiness,
            SpeakingPace = string.IsNullOrWhiteSpace(r.SpeakingPace) ? "normal" : r.SpeakingPace,
        };

    private static void EnsurePlaceholderKeys(Dictionary<string, CharacterSuggestionDto> accChars,
        IEnumerable<PhasedAnalysisJsonParser.CharacterAssignmentRow> refs)
    {
        foreach (var a in refs)
        {
            var key = a.CharacterExternalKey?.Trim();
            if (string.IsNullOrWhiteSpace(key))
                continue;

            if (a.NotSpeech)
                continue;

            if (accChars.ContainsKey(key))
                continue;

            StoryAnalysisAccumulator.MergeCharacters(accChars,
            [
                new CharacterSuggestionDto
                {
                    ExternalKey = key,
                    Name = key,
                    Aliases = null,
                    PersonalitySummary = null,
                    SpeechStyleSummary = null,
                    GenderPresentation = "unspecified",
                    Tone = "neutral",
                    Accent = "none",
                    Breathiness = "normal",
                    SpeakingPace = "normal",
                },
            ]);
        }
    }

    private static string TrimCanon(string canon, int lo, int hi, int maxChars)
    {
        if (lo < 0 || hi > canon.Length || hi <= lo)
            return string.Empty;

        var spanLen = hi - lo;
        if (spanLen <= maxChars)
            return canon.Substring(lo, spanLen);

        return canon.Substring(lo, maxChars) + "…";
    }

    private sealed record DialogueCatalogRow(int OrderIndex, int StartOffset, int EndOffset, string Text);

    private sealed record PendingUtterance(
        int StartOffset,
        int EndOffset,
        SpeakerKind SpeakerKind,
        string? CharacterExternalKey,
        double Confidence,
        bool Uncertain);
}
