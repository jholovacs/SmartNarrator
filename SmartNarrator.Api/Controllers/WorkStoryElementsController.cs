using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SmartNarrator.Api.Contracts;
using SmartNarrator.Domain.Entities;
using SmartNarrator.Domain.Enums;
using SmartNarrator.Infrastructure.Persistence;

namespace SmartNarrator.Api.Controllers;

[ApiController]
[Route("works/{workId:guid}")]
public sealed partial class WorkStoryElementsController(SmartNarratorDbContext db) : ControllerBase
{
    private enum TimelineBulkKind
    {
        Utterance,
        NarrativePassage,
        DialogueSpan,
        WorkChapter,
        StructureSection,
    }

    [HttpGet("timeline")]
    public async Task<ActionResult<TimelineDto>> Timeline(Guid workId, CancellationToken cancellationToken)
    {
        var dto = await BuildTimelineDto(workId, cancellationToken).ConfigureAwait(false);
        return dto is null ? NotFound() : dto;
    }

    [HttpPost("timeline/bulk-merge")]
    public async Task<ActionResult<TimelineDto>> TimelineBulkMerge(Guid workId, [FromBody] TimelineBulkDto body,
        CancellationToken cancellationToken)
    {
        if (body.Ids.Count < 2)
            return BadRequest("Merge requires at least two rows.");
        if (!TryParseTimelineKind(body.EntityKind, out var kind))
            return BadRequest("Unknown entity kind.");

        await using var tx = await db.Database.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        ActionResult? problem = kind switch
        {
            TimelineBulkKind.Utterance => await BulkMergeUtterances(workId, body.Ids, cancellationToken)
                .ConfigureAwait(false),
            TimelineBulkKind.NarrativePassage => await BulkMergeNarratives(workId, body.Ids, cancellationToken)
                .ConfigureAwait(false),
            TimelineBulkKind.DialogueSpan => await BulkMergeDialogueSpans(workId, body.Ids, cancellationToken)
                .ConfigureAwait(false),
            TimelineBulkKind.WorkChapter => await BulkMergeWorkChapters(workId, body.Ids, cancellationToken)
                .ConfigureAwait(false),
            TimelineBulkKind.StructureSection => await BulkMergeStructureSections(workId, body.Ids, cancellationToken)
                .ConfigureAwait(false),
            _ => BadRequest("Unsupported merge kind."),
        };

        if (problem is not null)
        {
            await tx.RollbackAsync(cancellationToken).ConfigureAwait(false);
            return problem;
        }

        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        await tx.CommitAsync(cancellationToken).ConfigureAwait(false);

        var refreshed = await BuildTimelineDto(workId, cancellationToken).ConfigureAwait(false);
        return refreshed is null ? NotFound() : refreshed;
    }

    [HttpPost("timeline/bulk-delete")]
    public async Task<ActionResult<TimelineDto>> TimelineBulkDelete(Guid workId, [FromBody] TimelineBulkDto body,
        CancellationToken cancellationToken)
    {
        if (body.Ids.Count == 0)
            return BadRequest("Select at least one row to delete.");
        if (!TryParseTimelineKind(body.EntityKind, out var kind))
            return BadRequest("Unknown entity kind.");

        await using var tx = await db.Database.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        ActionResult? problem = kind switch
        {
            TimelineBulkKind.Utterance => await BulkDeleteUtterances(workId, body.Ids, cancellationToken)
                .ConfigureAwait(false),
            TimelineBulkKind.NarrativePassage => await BulkDeleteNarratives(workId, body.Ids, cancellationToken)
                .ConfigureAwait(false),
            TimelineBulkKind.DialogueSpan => await BulkDeleteDialogueSpans(workId, body.Ids, cancellationToken)
                .ConfigureAwait(false),
            TimelineBulkKind.WorkChapter => await BulkDeleteWorkChapters(workId, body.Ids, cancellationToken)
                .ConfigureAwait(false),
            TimelineBulkKind.StructureSection => await BulkDeleteStructureSections(workId, body.Ids, cancellationToken)
                .ConfigureAwait(false),
            _ => BadRequest("Unsupported delete kind."),
        };

        if (problem is not null)
        {
            await tx.RollbackAsync(cancellationToken).ConfigureAwait(false);
            return problem;
        }

        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        await tx.CommitAsync(cancellationToken).ConfigureAwait(false);

        var refreshed = await BuildTimelineDto(workId, cancellationToken).ConfigureAwait(false);
        return refreshed is null ? NotFound() : refreshed;
    }

    [HttpGet("segments")]
    public async Task<ActionResult<List<TextSegmentDto>>> Segments(Guid workId, CancellationToken cancellationToken)
    {
        var exists = await db.Works.AnyAsync(w => w.Id == workId, cancellationToken).ConfigureAwait(false);
        if (!exists)
            return NotFound();

        return await db.Segments.Where(s => s.WorkId == workId).OrderBy(s => s.OrderIndex)
            .Select(s => new TextSegmentDto(s.Id, s.OrderIndex, s.StartOffset, s.EndOffset)).ToListAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    [HttpGet("characters")]
    public async Task<ActionResult<List<CharacterDto>>> CharactersGet(Guid workId, CancellationToken cancellationToken)
    {
        var exists = await db.Works.AnyAsync(w => w.Id == workId, cancellationToken).ConfigureAwait(false);
        if (!exists)
            return NotFound();

        var rows =
            await db.Characters.Where(c => c.WorkId == workId).OrderBy(c => c.Name).ToListAsync(cancellationToken)
                .ConfigureAwait(false);

        return rows.Select(MapCharacter).ToList();
    }

    [HttpPost("characters")]
    public async Task<ActionResult<CharacterDto>> CharactersPost(Guid workId, [FromBody] CharacterCreateDto body,
        CancellationToken cancellationToken)
    {
        var exists = await db.Works.AnyAsync(w => w.Id == workId, cancellationToken).ConfigureAwait(false);
        if (!exists)
            return NotFound();

        var name = string.IsNullOrWhiteSpace(body.Name) ? "Unnamed" : body.Name.Trim();
        var entity = new CharacterProfileEntity
        {
            Id = Guid.NewGuid(),
            WorkId = workId,
            AiExternalKey = null,
            Name = name,
            AliasesJson = null,
            PersonalitySummary = null,
            SpeechStyleSummary = null,
            GenderPresentation = "unspecified",
            Tone = "neutral",
            Accent = "none",
            Breathiness = "normal",
            SpeakingPace = "normal",
            IsAiSuggested = false,
            UserApproved = false,
        };

        db.Characters.Add(entity);
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return MapCharacter(entity);
    }

    [HttpPut("characters")]
    public async Task<ActionResult<List<CharacterDto>>> CharactersPut(Guid workId,
        [FromBody] List<CharacterUpsertDto> body,
        CancellationToken cancellationToken)
    {
        var exists = await db.Works.AnyAsync(w => w.Id == workId, cancellationToken).ConfigureAwait(false);
        if (!exists)
            return NotFound();

        foreach (var dto in body)
        {
            var entity = await db.Characters.SingleOrDefaultAsync(c => c.Id == dto.Id && c.WorkId == workId,
                cancellationToken).ConfigureAwait(false);
            if (entity is null)
                continue;

            entity.Name = string.IsNullOrWhiteSpace(dto.Name) ? entity.Name : dto.Name.Trim();

            var rawKey = dto.AiExternalKey?.Trim();
            var keyToSet = string.IsNullOrWhiteSpace(rawKey) ? null : rawKey;
            if (dto.PatchAiExternalKey)
            {
                if (keyToSet is not null)
                {
                    var keyNorm = keyToSet.ToLowerInvariant();
                    var collision = await db.Characters.AnyAsync(
                            c => c.WorkId == workId && c.Id != entity.Id && c.AiExternalKey != null &&
                                 c.AiExternalKey.ToLower() == keyNorm,
                            cancellationToken)
                        .ConfigureAwait(false);
                    if (collision)
                        return Conflict(
                            $"Another profile already uses AI external key '{keyToSet}'. Clear or change it first.");

                    entity.AiExternalKey = keyToSet;
                }
                else
                    entity.AiExternalKey = null;
            }

            entity.PersonalitySummary = string.IsNullOrWhiteSpace(dto.PersonalitySummary)
                ? null
                : dto.PersonalitySummary.Trim();
            entity.SpeechStyleSummary = string.IsNullOrWhiteSpace(dto.SpeechStyleSummary)
                ? null
                : dto.SpeechStyleSummary.Trim();
            entity.GenderPresentation = dto.GenderPresentation;
            entity.Tone = dto.Tone;
            entity.Accent = dto.Accent;
            entity.Breathiness = dto.Breathiness;
            entity.SpeakingPace = dto.SpeakingPace;
            entity.UserApproved = dto.UserApproved;
            entity.IsAiSuggested = false;
            entity.AliasesJson = dto.Aliases is { Count: > 0 } ? JsonSerializer.Serialize(dto.Aliases) : null;
        }

        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        var rows = await db.Characters.Where(c => c.WorkId == workId).OrderBy(c => c.Name).ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        return rows.Select(MapCharacter).ToList();
    }

    /// <summary>
    /// Deletes a character profile. Utterances / narrator passages referencing this profile clear their FK (SetNull).
    /// </summary>
    [HttpDelete("characters/{characterId:guid}")]
    public async Task<IActionResult> CharactersDelete(Guid workId, Guid characterId,
        CancellationToken cancellationToken)
    {
        var exists = await db.Works.AnyAsync(w => w.Id == workId, cancellationToken).ConfigureAwait(false);
        if (!exists)
            return NotFound();

        var entity =
            await db.Characters.SingleOrDefaultAsync(c => c.Id == characterId && c.WorkId == workId,
                cancellationToken).ConfigureAwait(false);
        if (entity is null)
            return NotFound();

        db.Characters.Remove(entity);
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return NoContent();
    }

    /// <summary>Unifies duplicate AI/user profiles: repoints utterances &amp; narrator passages, merges aliases &amp; notes.</summary>
    [HttpPost("characters/merge")]
    public async Task<ActionResult<List<CharacterDto>>> CharactersMerge(Guid workId, [FromBody] CharacterMergeDto body,
        CancellationToken cancellationToken)
    {
        var exists = await db.Works.AnyAsync(w => w.Id == workId, cancellationToken).ConfigureAwait(false);
        if (!exists)
            return NotFound();

        var sources = body.SourceCharacterIds.Where(id => id != body.TargetCharacterId).Distinct().ToList();
        if (sources.Count == 0)
            return BadRequest("Select at least one source profile to merge away.");

        await using var tx = await db.Database.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);

        var target =
            await db.Characters.SingleOrDefaultAsync(c => c.Id == body.TargetCharacterId && c.WorkId == workId,
                cancellationToken).ConfigureAwait(false);
        if (target is null)
        {
            await tx.RollbackAsync(cancellationToken).ConfigureAwait(false);
            return BadRequest("Keep-profile character was not found on this work.");
        }

        var sourceEntities =
            await db.Characters.Where(c => c.WorkId == workId && sources.Contains(c.Id)).ToListAsync(cancellationToken)
                .ConfigureAwait(false);

        if (sourceEntities.Count != sources.Count)
        {
            await tx.RollbackAsync(cancellationToken).ConfigureAwait(false);
            return BadRequest("One or more source characters were not found on this work.");
        }

        var mergedAliases = MergeCharacterAliasBag(target, sourceEntities);

        target.PersonalitySummary = CombineMergedNotes(target.PersonalitySummary,
            sourceEntities.Select(s => s.PersonalitySummary));
        target.SpeechStyleSummary = CombineMergedNotes(target.SpeechStyleSummary,
            sourceEntities.Select(s => s.SpeechStyleSummary));
        target.UserApproved |= sourceEntities.Any(s => s.UserApproved);
        target.IsAiSuggested = false;

        foreach (var key in sourceEntities.Select(s => s.AiExternalKey).Where(k => !string.IsNullOrWhiteSpace(k)))
            mergedAliases.Add(key!.Trim());

        if (!string.IsNullOrWhiteSpace(target.Name))
            mergedAliases.Remove(target.Name.Trim());

        if (!string.IsNullOrWhiteSpace(target.AiExternalKey))
            mergedAliases.Remove(target.AiExternalKey.Trim());

        foreach (var s in sourceEntities)
        {
            if (!string.IsNullOrWhiteSpace(s.Name))
                mergedAliases.Remove(s.Name.Trim());
        }

        target.AliasesJson = mergedAliases.Count > 0 ? JsonSerializer.Serialize(mergedAliases.OrderBy(a => a, StringComparer.OrdinalIgnoreCase).ToList()) : null;

        await db.Utterances.Where(u => u.WorkId == workId && u.CharacterId != null && sources.Contains(u.CharacterId.Value))
            .ExecuteUpdateAsync(setters => setters.SetProperty(u => u.CharacterId, body.TargetCharacterId),
                cancellationToken).ConfigureAwait(false);

        await db.NarrativePassages.Where(p =>
                p.WorkId == workId && p.NarratorCharacterId != null && sources.Contains(p.NarratorCharacterId.Value))
            .ExecuteUpdateAsync(
                setters => setters.SetProperty(p => p.NarratorCharacterId, body.TargetCharacterId),
                cancellationToken).ConfigureAwait(false);

        db.Characters.RemoveRange(sourceEntities);
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        await tx.CommitAsync(cancellationToken).ConfigureAwait(false);

        var rows = await db.Characters.Where(c => c.WorkId == workId).OrderBy(c => c.Name).ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        return rows.Select(MapCharacter).ToList();
    }

    private static HashSet<string> MergeCharacterAliasBag(CharacterProfileEntity target,
        List<CharacterProfileEntity> sources)
    {
        var bag = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var a in DeserializeAliases(target.AliasesJson) ?? [])
            bag.Add(a.Trim());

        foreach (var s in sources)
        {
            foreach (var a in DeserializeAliases(s.AliasesJson) ?? [])
                bag.Add(a.Trim());

            if (!string.IsNullOrWhiteSpace(s.Name))
                bag.Add(s.Name.Trim());
        }

        return bag;
    }

    private static string? CombineMergedNotes(string? primary, IEnumerable<string?> extras)
    {
        var parts = new List<string>();
        if (!string.IsNullOrWhiteSpace(primary))
            parts.Add(primary.Trim());

        foreach (var e in extras)
        {
            if (string.IsNullOrWhiteSpace(e))
                continue;
            var t = e.Trim();
            if (parts.Any(p => string.Equals(p, t, StringComparison.OrdinalIgnoreCase)))
                continue;

            parts.Add(t);
        }

        return parts.Count == 0 ? null : string.Join("\n\n--- merged ---\n\n", parts);
    }

    [HttpGet("utterances")]
    public async Task<ActionResult<List<UtteranceDto>>> UtterancesGet(Guid workId, CancellationToken cancellationToken)
    {
        var exists = await db.Works.AnyAsync(w => w.Id == workId, cancellationToken).ConfigureAwait(false);
        if (!exists)
            return NotFound();

        return await db.Utterances.Where(u => u.WorkId == workId).OrderBy(u => u.StartOffset)
            .Select(u => new UtteranceDto(
                u.Id,
                u.StartOffset,
                u.EndOffset,
                u.SpeakerKind,
                u.CharacterId,
                u.Confidence,
                u.SpeakerNeedsReview,
                u.IsAiSuggested,
                u.UserApproved)).ToListAsync(cancellationToken).ConfigureAwait(false);
    }

    [HttpPut("utterances")]
    public async Task<ActionResult<List<UtteranceDto>>> UtterancesPut(Guid workId,
        [FromBody] List<UtteranceUpsertDto> body,
        CancellationToken cancellationToken)
    {
        var exists = await db.Works.AnyAsync(w => w.Id == workId, cancellationToken).ConfigureAwait(false);
        if (!exists)
            return NotFound();

        foreach (var dto in body)
        {
            var entity = await db.Utterances.SingleOrDefaultAsync(u => u.Id == dto.Id && u.WorkId == workId,
                cancellationToken).ConfigureAwait(false);
            if (entity is null)
                continue;

            entity.SpeakerKind = dto.SpeakerKind;
            if (dto.SpeakerKind == SpeakerKind.Character)
            {
                entity.CharacterId = dto.CharacterId;
                entity.SpeakerNeedsReview = !dto.CharacterId.HasValue;
            }
            else
            {
                entity.CharacterId = null;
                entity.SpeakerNeedsReview = false;
            }

            entity.UserApproved = dto.UserApproved;
            entity.IsAiSuggested = false;
        }

        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return await UtterancesGet(workId, cancellationToken);
    }

    [HttpPut("narratives")]
    public async Task<ActionResult<List<NarrativePassageDto>>> NarrativesPut(Guid workId,
        [FromBody] List<NarrativePassageUpsertDto> body,
        CancellationToken cancellationToken)
    {
        var exists = await db.Works.AnyAsync(w => w.Id == workId, cancellationToken).ConfigureAwait(false);
        if (!exists)
            return NotFound();

        foreach (var dto in body)
        {
            var entity = await db.NarrativePassages.SingleOrDefaultAsync(p => p.Id == dto.Id && p.WorkId == workId,
                cancellationToken).ConfigureAwait(false);
            if (entity is null)
                continue;

            entity.NarratorCharacterId = dto.NarratorCharacterId;
            entity.PerspectiveNotes = dto.PerspectiveNotes?.Trim() ?? string.Empty;
            entity.GenderPresentation = dto.GenderPresentation;
            entity.Tone = dto.Tone;
            entity.Accent = dto.Accent;
            entity.Breathiness = dto.Breathiness;
            entity.SpeakingPace = dto.SpeakingPace;
            entity.IsAiSuggested = false;
        }

        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return await db.NarrativePassages.Where(p => p.WorkId == workId).OrderBy(p => p.StartOffset)
            .Select(p => new NarrativePassageDto(
                p.Id,
                p.StartOffset,
                p.EndOffset,
                p.NarratorCharacterId,
                p.PerspectiveNotes,
                p.GenderPresentation,
                p.Tone,
                p.Accent,
                p.Breathiness,
                p.SpeakingPace,
                p.IsAiSuggested)).ToListAsync(cancellationToken).ConfigureAwait(false);
    }

    private static CharacterDto MapCharacter(CharacterProfileEntity c) =>
        new(
            c.Id,
            c.AiExternalKey,
            c.Name,
            DeserializeAliases(c.AliasesJson),
            c.PersonalitySummary,
            c.SpeechStyleSummary,
            c.GenderPresentation,
            c.Tone,
            c.Accent,
            c.Breathiness,
            c.SpeakingPace,
            c.IsAiSuggested,
            c.UserApproved);

    private static List<string>? DeserializeAliases(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return null;

        try
        {
            return JsonSerializer.Deserialize<List<string>>(json);
        }
        catch
        {
            return null;
        }
    }
}
