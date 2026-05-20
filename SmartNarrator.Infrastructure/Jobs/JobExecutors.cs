using System.Diagnostics;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SmartNarrator.Application.Analysis;
using SmartNarrator.Application.Ports;
using SmartNarrator.Domain.Entities;
using SmartNarrator.Domain.Enums;
using SmartNarrator.Infrastructure.Ai;
using SmartNarrator.Infrastructure.Ingest;
using SmartNarrator.Infrastructure.Options;
using SmartNarrator.Infrastructure.Persistence;
using SmartNarrator.Infrastructure.Services;
using SmartNarrator.Infrastructure.Speech;

namespace SmartNarrator.Infrastructure.Jobs;

public static class JobExecutors
{
    public static async Task RunAsync(IServiceProvider services, BackgroundJobEntity job,
        CancellationToken cancellationToken)
    {
        var notifier = services.GetRequiredService<IJobRealtimeNotifier>();
        switch (job.Type)
        {
            case BackgroundJobType.Ingest:
                await ExecuteIngestAsync(services, job, notifier, cancellationToken).ConfigureAwait(false);
                break;
            case BackgroundJobType.Analyze:
                await ExecuteAnalyzeAsync(services, job, notifier, cancellationToken).ConfigureAwait(false);
                break;
            case BackgroundJobType.RenderSpeech:
                await ExecuteRenderAsync(services, job, notifier, cancellationToken).ConfigureAwait(false);
                break;
            default:
                throw new InvalidOperationException($"Unknown job type {job.Type}");
        }
    }

    private sealed record SourceDocumentPayload(Guid SourceDocumentId);

    private static async Task ExecuteIngestAsync(IServiceProvider services, BackgroundJobEntity job,
        IJobRealtimeNotifier notifier, CancellationToken cancellationToken)
    {
        var db = services.GetRequiredService<SmartNarratorDbContext>();
        var logger = services.GetRequiredService<ILoggerFactory>().CreateLogger(nameof(JobExecutors));
        var ollamaClient = services.GetRequiredService<OllamaStoryAnalysisClient>();
        var ollamaOpts = services.GetRequiredService<IOptions<OllamaOptions>>().Value;
        var llmDiag = services.GetRequiredService<IAnalyzeLlmDiagnosticsSink>();
        var hostEnv = services.GetRequiredService<IHostEnvironment>();
        var storageOpts = services.GetRequiredService<IOptions<StorageOptions>>().Value;

        Task BumpAsync() => notifier.NotifyJobUpdatedAsync(job.Id, cancellationToken);

        async Task PulseAsync(int progressPercent, string? phase)
        {
            job.ProgressPercent = progressPercent;
            if (phase is not null)
                job.ProgressPhase = phase;
            await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            await BumpAsync().ConfigureAwait(false);
        }

        var payload =
            JsonSerializer.Deserialize<SourceDocumentPayload>(job.PayloadJson ??
                throw new InvalidOperationException("Missing ingest payload"))
            ?? throw new InvalidOperationException("Invalid ingest payload.");

        await PulseAsync(5, "Starting ingest…").ConfigureAwait(false);

        var sourceDoc = await db.SourceDocuments.Include(s => s.Work)
                .SingleOrDefaultAsync(s => s.Id == payload.SourceDocumentId, cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidOperationException("Source document was removed");

        if (sourceDoc.Work is null)
            throw new InvalidOperationException("Work missing");

        var workId = sourceDoc.WorkId;

        var basePath = Path.GetFullPath(Path.Combine(hostEnv.ContentRootPath, storageOpts.RelativeRoot));
        var fullPath = Path.Combine(basePath,
            sourceDoc.StoredRelativePath ?? throw new InvalidOperationException("Missing StoredRelativePath"));

        await using (var fs = File.OpenRead(fullPath))
        {
            using var extractTimeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            extractTimeout.CancelAfter(StoryTextExtractors.ExtractTimeout);

            var lastPercent = -1;
            string? phaseSticky = null;

            async Task PulseExtractAsync(StoryExtractProgress snap)
            {
                var pctAdvanced = snap.Percent > lastPercent;
                var phaseFresh = snap.Phase is not null && snap.Phase != phaseSticky;
                if (!pctAdvanced && !phaseFresh)
                    return;

                lastPercent = Math.Max(lastPercent, snap.Percent);
                if (snap.Phase is not null)
                    phaseSticky = snap.Phase;

                job.ProgressPercent = snap.Percent;
                job.ProgressPhase = phaseSticky;
                await db.SaveChangesAsync(extractTimeout.Token).ConfigureAwait(false);
                await BumpAsync().ConfigureAwait(false);
            }

            MarkdownIngestExtract ingestExtract;
            try
            {
                ingestExtract = await StoryMarkdownImporter.ExtractAsync(fs, sourceDoc.Format, extractTimeout.Token,
                    PulseExtractAsync).ConfigureAwait(false);
            }
            catch (OperationCanceledException ex)
            {
                cancellationToken.ThrowIfCancellationRequested();
                throw new InvalidOperationException(
                    "Extracting or structuring this document exceeded the time limit (PDF parsing, EPUB/HTML conversion, or AI division detection). Try a smaller file, split it, or check server logs.",
                    ex);
            }

            var text = StoryTextExtractors.SanitizeForPostgreSqlText(ingestExtract.Markdown);

            await PulseAsync(52, "Removing prior AI suggestions and segments…").ConfigureAwait(false);

            await db.AudioArtifacts.Where(a => a.WorkId == workId).ExecuteDeleteAsync(cancellationToken)
                .ConfigureAwait(false);
            await db.Utterances.Where(u => u.WorkId == workId).ExecuteDeleteAsync(cancellationToken)
                .ConfigureAwait(false);
            await db.NarrativePassages.Where(p => p.WorkId == workId).ExecuteDeleteAsync(cancellationToken)
                .ConfigureAwait(false);
            await db.StoryStructureSections.Where(s => s.WorkId == workId).ExecuteDeleteAsync(cancellationToken)
                .ConfigureAwait(false);
            await db.WorkChapters.Where(w => w.WorkId == workId).ExecuteDeleteAsync(cancellationToken)
                .ConfigureAwait(false);
            await db.Characters.Where(c => c.WorkId == workId).ExecuteDeleteAsync(cancellationToken)
                .ConfigureAwait(false);
            await db.Segments.Where(s => s.WorkId == workId).ExecuteDeleteAsync(cancellationToken)
                .ConfigureAwait(false);

            await PulseAsync(58, "Preparing to save story text…").ConfigureAwait(false);

            await PulseAsync(60, "Writing story text to PostgreSQL…").ConfigureAwait(false);

            var connectionString = ResolveNpgsqlConnectionString(db);
            using var heartbeatCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            var heartbeatTask =
                RunCanonicalSaveHeartbeatAsync(connectionString, job.Id, notifier, logger, heartbeatCts.Token);

            var sw = Stopwatch.StartNew();
            try
            {
                var prevTimeout = db.Database.GetCommandTimeout();
                db.Database.SetCommandTimeout(900);
                try
                {
                    await db.Database.ExecuteSqlInterpolatedAsync(
                            $"""UPDATE works SET "CanonicalText" = {text} WHERE "Id" = {workId}""",
                            cancellationToken)
                        .ConfigureAwait(false);
                }
                finally
                {
                    db.Database.SetCommandTimeout(prevTimeout);
                }

                logger.LogInformation(
                    "Persisted canonical text for work {WorkId}: characterLength={Chars}, elapsedMs={ElapsedMs}",
                    workId, text.Length, sw.ElapsedMilliseconds);
            }
            finally
            {
                heartbeatCts.Cancel();
                try
                {
                    await heartbeatTask.ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    logger.LogTrace(ex, "Canonical save heartbeat ended for job {JobId}.", job.Id);
                }
            }

            await PulseAsync(62,
                    ingestExtract.StructuralChapters.Count > 0
                        ? "Saving spine chapters…"
                        : "Detecting major divisions (AI)…")
                .ConfigureAwait(false);

            List<ChapterPartitionNormalizer.GlobalChapterBoundary> partitions;
            var structuralIngest = ingestExtract.StructuralChapters.Count > 0;

            if (structuralIngest)
            {
                var raw = ingestExtract.StructuralChapters.Select(s =>
                        new ChapterPartitionNormalizer.GlobalChapterBoundary(s.StartOffset, s.EndOffset, s.Title,
                            s.HeadingStartOffset, s.HeadingEndOffset))
                    .ToList();
                partitions = ChapterPartitionNormalizer.NormalizePartition(text.Length, raw);
            }
            else
            {
                var provisionalSegs = StoryTextExtractors.BuildParagraphOffsets(text)
                    .Select((p, i) => new SegmentBoundaryDto(i, p.Start, p.End)).ToList();

                partitions = await IngestChapterShiftDetector.DetectNormalizedAsync(
                        ollamaClient,
                        logger,
                        text,
                        provisionalSegs,
                        ollamaOpts,
                        null,
                        job.Id,
                        workId,
                        llmDiag,
                        extractTimeout.Token)
                    .ConfigureAwait(false);
            }

            partitions = ChapterPartitionNormalizer.CollapseWhitespaceOnlyChapterSlices(text, partitions);

            await PersistIngestChaptersAsync(db, workId, partitions, structuralIngest, cancellationToken)
                .ConfigureAwait(false);

            await PulseAsync(66, "Finding paragraph boundaries…").ConfigureAwait(false);

            var offsets = await StoryTextExtractors.BuildParagraphOffsetsAsync(text, cancellationToken,
                    async mapped =>
                    {
                        job.ProgressPercent = mapped;
                        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
                        await BumpAsync().ConfigureAwait(false);
                    }, 66, 69)
                .ConfigureAwait(false);

            await PulseAsync(70, "Saving paragraph markers…").ConfigureAwait(false);

            var totalSegs = Math.Max(offsets.Count, 1);
            var order = 0;
            var segmentBatch = Math.Max(15, Math.Min(120, totalSegs / 8));
            foreach (var span in offsets)
            {
                db.Segments.Add(new TextSegmentEntity
                {
                    Id = Guid.NewGuid(),
                    WorkId = workId,
                    OrderIndex = order++,
                    StartOffset = span.Start,
                    EndOffset = span.End,
                });

                if (order % segmentBatch == 0)
                {
                    job.ProgressPercent =
                        Math.Min(92, 70 + (int)Math.Round(22.0 * order / totalSegs));
                    await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
                    await BumpAsync().ConfigureAwait(false);
                }
            }

            await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            job.ProgressPercent = 95;
            job.ProgressPhase = "Finalizing ingest…";
            await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            await BumpAsync().ConfigureAwait(false);
        }

        logger.LogInformation("Ingest processed document {DocId}", sourceDoc.Id);
    }

    private static async Task PersistIngestChaptersAsync(SmartNarratorDbContext db,
        Guid workId,
        IReadOnlyList<ChapterPartitionNormalizer.GlobalChapterBoundary> partitions,
        bool structuralIngest,
        CancellationToken cancellationToken)
    {
        var aiSuggested = !structuralIngest;

        // Unique index IX_work_chapters_WorkId_OrderIndex — each row needs a distinct OrderIndex on first save.
        for (var i = 0; i < partitions.Count; i++)
        {
            var p = partitions[i];
            db.WorkChapters.Add(new WorkChapterEntity
            {
                Id = Guid.NewGuid(),
                WorkId = workId,
                OrderIndex = i,
                StartOffset = p.Start,
                EndOffset = p.End,
                Title = p.Title,
                Notes = string.Empty,
                HeadingStartOffset = ClampHeadingCoordForChapter(p.HeadingStart, p.Start, p.End),
                HeadingEndOffset = ClampHeadingCoordForChapter(p.HeadingEnd, p.Start, p.End),
                IsAiSuggested = aiSuggested,
            });

            db.StoryStructureSections.Add(new StoryStructureSectionEntity
            {
                Id = Guid.NewGuid(),
                WorkId = workId,
                Kind = StoryStructureSectionKind.Chapter,
                StartOffset = p.Start,
                EndOffset = p.End,
                Title = p.Title,
                Notes = string.Empty,
                IsAiSuggested = aiSuggested,
            });
        }

        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Heading markers must lie inside <c>[chapterLo, chapterHi)</c> (UTF‑16; exclusive end).</summary>
    private static int? ClampHeadingCoordForChapter(int? headingCoord, int chapterLo, int chapterHiExclusive)
    {
        if (headingCoord is null)
            return null;

        var v = headingCoord.Value;
        if (v < chapterLo || v >= chapterHiExclusive)
            return null;

        return v;
    }

    private static async Task ExecuteAnalyzeAsync(IServiceProvider services, BackgroundJobEntity job,
        IJobRealtimeNotifier notifier, CancellationToken cancellationToken)
    {
        var db = services.GetRequiredService<SmartNarratorDbContext>();
        var logger = services.GetRequiredService<ILoggerFactory>().CreateLogger(nameof(JobExecutors));

        Task BumpAsync() => notifier.NotifyJobUpdatedAsync(job.Id, cancellationToken);
        if (job.WorkId is null)
            throw new InvalidOperationException("Analyze job missing WorkId.");

        job.ProgressPercent = 8;
        job.ProgressPhase = "Starting phased story analysis…";
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        await BumpAsync().ConfigureAwait(false);

        var orchestrator = services.GetRequiredService<StoryPhasedAnalysisOrchestrator>();
        await orchestrator.RunAsync(job, notifier, cancellationToken).ConfigureAwait(false);

        logger.LogInformation("Analyze completed for job {JobId}", job.Id);
    }

    private static async Task ExecuteRenderAsync(IServiceProvider services, BackgroundJobEntity job,
        IJobRealtimeNotifier notifier, CancellationToken cancellationToken)
    {
        var db = services.GetRequiredService<SmartNarratorDbContext>();
        var synth = services.GetRequiredService<ISpeechSynthesisClient>();
        var hostEnv = services.GetRequiredService<IHostEnvironment>();
        var logger = services.GetRequiredService<ILoggerFactory>().CreateLogger(nameof(JobExecutors));
        var storageOpts = services.GetRequiredService<IOptions<StorageOptions>>().Value;
        var speechOpts = services.GetRequiredService<IOptions<SpeechSynthesisOptions>>().Value;

        Task BumpAsync() => notifier.NotifyJobUpdatedAsync(job.Id, cancellationToken);
        if (job.WorkId is null)
            throw new InvalidOperationException("Render job missing WorkId.");

        var workId = job.WorkId.Value;

        await db.AudioArtifacts.Where(a => a.WorkId == workId).ExecuteDeleteAsync(cancellationToken).ConfigureAwait(false);

        var storageRootPhysical = Path.GetFullPath(Path.Combine(hostEnv.ContentRootPath, storageOpts.RelativeRoot));
        var workAudioPhysical = Path.Combine(storageRootPhysical, "audio", workId.ToString("N"));
        if (Directory.Exists(workAudioPhysical))
            Directory.Delete(workAudioPhysical, recursive: true);
        Directory.CreateDirectory(workAudioPhysical);

        var work = await db.Works.AsNoTracking().SingleAsync(w => w.Id == workId, cancellationToken).ConfigureAwait(false);
        var utterances = await db.Utterances.AsNoTracking().Where(u => u.WorkId == workId).ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        var passages = await db.NarrativePassages.AsNoTracking().Where(p => p.WorkId == workId)
                .OrderBy(p => p.StartOffset).ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        var characters =
            await db.Characters.AsNoTracking().Where(c => c.WorkId == workId).ToListAsync(cancellationToken)
                .ConfigureAwait(false);

        var charLookup = characters.ToDictionary(c => c.Id);

        job.ProgressPercent = 10;
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        await BumpAsync().ConfigureAwait(false);

        var plan = SpeechTimelinePlanner.Plan(work, utterances, passages).ToList();
        var total = Math.Max(plan.Count, 1);
        var ordinal = 0;

        foreach (var slice in plan)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var len = slice.EndOffset - slice.StartOffset;
            if (len <= 0 || slice.StartOffset < 0 || slice.EndOffset > work.CanonicalText.Length)
                continue;

            var excerpt = work.CanonicalText.Substring(slice.StartOffset, len);

            CharacterProfileEntity? resolved = null;
            if (slice.CharacterOrNarratorLinkId is { } cid && charLookup.TryGetValue(cid, out var rc))
                resolved = rc;

            var dto = SpeechTimelinePlanner.ToRequest(speechOpts.DefaultModel, slice, excerpt, resolved);
            byte[] wav;
            try
            {
                wav = await synth.SynthesizeAsync(dto, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Skipping chunk {Ordinal} synthesis failure.", ordinal);
                continue;
            }

            var relative = $"audio/{workId:N}/{ordinal:0000}.wav".Replace('\\', '/');
            var diskPath = Path.Combine(workAudioPhysical, $"{ordinal:0000}.wav");
            await File.WriteAllBytesAsync(diskPath, wav, cancellationToken).ConfigureAwait(false);

            db.AudioArtifacts.Add(new AudioArtifactEntity
            {
                Id = Guid.NewGuid(),
                WorkId = workId,
                UtteranceId = utterances.FirstOrDefault(u =>
                    u.StartOffset <= slice.StartOffset && u.EndOffset >= slice.EndOffset)?.Id,
                StartOffset = slice.StartOffset,
                EndOffset = slice.EndOffset,
                RelativePath = relative,
                MimeType = "audio/wav",
            });

            ordinal++;
            job.ProgressPercent = Math.Min(99, (int)Math.Round(ordinal * 100.0 / total));

            if (ordinal % 2 == 0)
            {
                await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
                await BumpAsync().ConfigureAwait(false);
            }
        }

        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        await BumpAsync().ConfigureAwait(false);

        logger.LogInformation("Rendered {Chunks} clips for work {WorkId}", ordinal, workId);
    }

    private static string ResolveNpgsqlConnectionString(SmartNarratorDbContext db)
    {
        var cs = db.Database.GetConnectionString();
        if (!string.IsNullOrWhiteSpace(cs))
            return cs;

        var fallback = db.Database.GetDbConnection().ConnectionString;
        if (!string.IsNullOrWhiteSpace(fallback))
            return fallback;

        throw new InvalidOperationException(
            "Cannot resolve PostgreSQL connection string for ingest DB heartbeat.");
    }

    /// <summary>
    /// Updates job phase text on a separate connection while the canonical UPDATE runs so the UI stays responsive.
    /// </summary>
    private static async Task RunCanonicalSaveHeartbeatAsync(string connectionString, Guid jobId,
        IJobRealtimeNotifier notifier, ILogger logger, CancellationToken cancellationToken)
    {
        var sw = Stopwatch.StartNew();
        var warnedMissingRow = false;
        while (true)
        {
            try
            {
                await Task.Delay(600, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                break;
            }

            try
            {
                await using var conn = new NpgsqlConnection(connectionString);
                await conn.OpenAsync(CancellationToken.None).ConfigureAwait(false);
                await using var cmd = new NpgsqlCommand(
                    """
                    UPDATE background_jobs
                    SET "ProgressPhase" = @phase
                    WHERE "Id" = @id
                    """,
                    conn);
                cmd.Parameters.AddWithValue("phase",
                    $"Writing story text to PostgreSQL… ({sw.Elapsed.TotalSeconds:F1}s elapsed)");
                cmd.Parameters.AddWithValue("id", jobId);
                var affected = await cmd.ExecuteNonQueryAsync(CancellationToken.None).ConfigureAwait(false);
                if (affected == 0 && !warnedMissingRow)
                {
                    warnedMissingRow = true;
                    logger.LogWarning(
                        "Heartbeat did not update background_jobs row (PostgreSQL identifier mismatch?). JobId={JobId}",
                        jobId);
                }

                await notifier.NotifyJobUpdatedAsync(jobId, CancellationToken.None).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                logger.LogTrace(ex, "Canonical save heartbeat iteration failed for job {JobId}.", jobId);
            }
        }
    }
}
