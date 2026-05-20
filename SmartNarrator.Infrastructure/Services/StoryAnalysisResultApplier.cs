using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using SmartNarrator.Application.Analysis;
using SmartNarrator.Application.Ports;
using SmartNarrator.Domain.Entities;
using SmartNarrator.Domain.Enums;
using SmartNarrator.Infrastructure.Options;
using SmartNarrator.Infrastructure.Persistence;

namespace SmartNarrator.Infrastructure.Services;

public sealed class StoryAnalysisResultApplier(
    SmartNarratorDbContext db,
    IOptions<OllamaOptions> ollamaOptions) : IStoryAnalysisResultApplier
{
    public async Task ApplyAsync(Guid workId, StoryAnalysisResultDto result, CancellationToken cancellationToken)
    {
        var workExists = await db.Works.AsNoTracking().AnyAsync(w => w.Id == workId, cancellationToken);
        if (!workExists)
            throw new InvalidOperationException($"Work {workId} not found.");

        await db.Characters.Where(c => c.WorkId == workId && c.IsAiSuggested && !c.UserApproved)
            .ExecuteDeleteAsync(cancellationToken);
        await db.Utterances.Where(u => u.WorkId == workId && u.IsAiSuggested && !u.UserApproved)
            .ExecuteDeleteAsync(cancellationToken);
        await db.NarrativePassages.Where(p => p.WorkId == workId && p.IsAiSuggested)
            .ExecuteDeleteAsync(cancellationToken);
        await db.StoryStructureSections.Where(s => s.WorkId == workId && s.IsAiSuggested)
            .ExecuteDeleteAsync(cancellationToken);

        var speakerReviewThreshold =
            Math.Clamp(ollamaOptions.Value.SpeakerConfidenceNeedsReviewThreshold, 0d, 1d);
        var speakerAutoTrustThreshold =
            Math.Clamp(ollamaOptions.Value.SpeakerConfidenceAutoTrustThreshold, 0d, 1d);

        var supplementalKeyRedirects = new Dictionary<string, Guid>(StringComparer.OrdinalIgnoreCase);
        var trackedCharacters =
            await db.Characters.Where(c => c.WorkId == workId).ToListAsync(cancellationToken);

        foreach (var c in result.Characters.GroupBy(x => x.ExternalKey, StringComparer.OrdinalIgnoreCase).Select(g =>
                     g.Last()))
        {
            var extKey = string.IsNullOrWhiteSpace(c.ExternalKey) ? string.Empty : c.ExternalKey.Trim();
            if (extKey.Length == 0)
                continue;

            var existingByKey = trackedCharacters.FirstOrDefault(x =>
                x.AiExternalKey != null &&
                string.Equals(x.AiExternalKey.Trim(), extKey, StringComparison.OrdinalIgnoreCase));

            if (existingByKey is not null)
            {
                if (existingByKey.UserApproved || !existingByKey.IsAiSuggested)
                    AnalysisCharacterResolution.MergeCumulativeSuggestionIntoEstablished(existingByKey, c);
                else
                    AnalysisCharacterResolution.ApplyCumulativeSuggestion(existingByKey, c);

                continue;
            }

            var match = AnalysisCharacterResolution.TryFindUniqueEstablishedMatch(trackedCharacters, c);
            if (match is not null)
            {
                supplementalKeyRedirects[extKey] = match.Id;

                if (match.UserApproved || !match.IsAiSuggested)
                    AnalysisCharacterResolution.MergeCumulativeSuggestionIntoEstablished(match, c);
                else
                    AnalysisCharacterResolution.ApplyCumulativeSuggestion(match, c);

                if (string.IsNullOrWhiteSpace(match.AiExternalKey))
                    match.AiExternalKey = extKey;

                continue;
            }

            var neo = AnalysisCharacterResolution.CreateProfileFromSuggestion(workId, c);
            db.Characters.Add(neo);
            trackedCharacters.Add(neo);
        }

        await db.SaveChangesAsync(cancellationToken);

        var keyToCharacterId = await db.Characters
            .Where(x => x.WorkId == workId && x.AiExternalKey != null)
            .ToDictionaryAsync(
                x => x.AiExternalKey!,
                x => x.Id,
                StringComparer.OrdinalIgnoreCase,
                cancellationToken);

        foreach (var kv in supplementalKeyRedirects)
            keyToCharacterId[kv.Key] = kv.Value;

        foreach (var u in result.Utterances)
        {
            var kind = Enum.TryParse<SpeakerKind>(u.SpeakerKind, true, out var sk)
                ? sk
                : SpeakerKind.Character;

            Guid? charId = null;
            if (kind == SpeakerKind.Character && !string.IsNullOrWhiteSpace(u.CharacterExternalKey))
            {
                charId = UtteranceCharacterLinker.ResolveCharacterId(
                    u.CharacterExternalKey,
                    u.Confidence,
                    speakerAutoTrustThreshold,
                    keyToCharacterId,
                    trackedCharacters);
            }

            var highTrust = u.Confidence >= speakerAutoTrustThreshold;

            db.Utterances.Add(new UtteranceEntity
            {
                Id = Guid.NewGuid(),
                WorkId = workId,
                StartOffset = u.StartOffset,
                EndOffset = u.EndOffset,
                SpeakerKind = kind,
                CharacterId = charId,
                Confidence = u.Confidence,
                SpeakerNeedsReview =
                    kind == SpeakerKind.Character &&
                    (charId is null || (!highTrust && u.Confidence < speakerReviewThreshold)),
                IsAiSuggested = true,
                UserApproved = false,
            });
        }

        foreach (var p in result.NarrativePassages)
        {
            Guid? narratorCharacterId = null;
            if (!string.IsNullOrWhiteSpace(p.NarratorCharacterExternalKey) &&
                keyToCharacterId.TryGetValue(p.NarratorCharacterExternalKey, out var resolvedNarratorId))
            {
                narratorCharacterId = resolvedNarratorId;
            }

            db.NarrativePassages.Add(new NarrativePassageEntity
            {
                Id = Guid.NewGuid(),
                WorkId = workId,
                StartOffset = p.StartOffset,
                EndOffset = p.EndOffset,
                NarratorCharacterId = narratorCharacterId,
                PerspectiveNotes = p.PerspectiveNotes,
                GenderPresentation = p.GenderPresentation,
                Tone = p.Tone,
                Accent = p.Accent,
                Breathiness = p.Breathiness,
                SpeakingPace = p.SpeakingPace,
                IsAiSuggested = true,
            });
        }

        foreach (var s in result.StructureSections)
        {
            db.StoryStructureSections.Add(new StoryStructureSectionEntity
            {
                Id = Guid.NewGuid(),
                WorkId = workId,
                StartOffset = s.StartOffset,
                EndOffset = s.EndOffset,
                Kind = s.Kind,
                Title = s.Title,
                Notes = s.Notes,
                IsAiSuggested = true,
            });
        }

        await db.SaveChangesAsync(cancellationToken);
    }
}
