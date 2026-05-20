using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SmartNarrator.Api.Contracts;
using SmartNarrator.Domain.Entities;

namespace SmartNarrator.Api.Controllers;

public sealed partial class WorkStoryElementsController
{
    private async Task<TimelineDto?> BuildTimelineDto(Guid workId, CancellationToken cancellationToken)
    {
        var work = await db.Works.AsNoTracking().SingleOrDefaultAsync(w => w.Id == workId, cancellationToken)
            .ConfigureAwait(false);
        if (work is null)
            return null;

        var segments = await db.Segments.Where(s => s.WorkId == workId).OrderBy(s => s.OrderIndex)
            .Select(s => new TextSegmentDto(s.Id, s.OrderIndex, s.StartOffset, s.EndOffset))
            .ToListAsync(cancellationToken).ConfigureAwait(false);

        var utterances = await db.Utterances.Where(u => u.WorkId == workId).OrderBy(u => u.StartOffset)
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

        var narratives = await db.NarrativePassages.Where(p => p.WorkId == workId).OrderBy(p => p.StartOffset)
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

        var structureSections = await db.StoryStructureSections.Where(s => s.WorkId == workId).OrderBy(s => s.StartOffset)
            .Select(s => new StoryStructureSectionDto(
                s.Id,
                s.StartOffset,
                s.EndOffset,
                s.Kind,
                s.Title,
                s.Notes,
                s.IsAiSuggested)).ToListAsync(cancellationToken).ConfigureAwait(false);

        var workChapters = await db.WorkChapters.Where(c => c.WorkId == workId).OrderBy(c => c.OrderIndex)
            .Select(c => new WorkChapterDto(
                c.Id,
                c.OrderIndex,
                c.StartOffset,
                c.EndOffset,
                c.HeadingStartOffset,
                c.HeadingEndOffset,
                c.Title,
                c.Notes,
                c.IsAiSuggested)).ToListAsync(cancellationToken).ConfigureAwait(false);

        var dialogueSpans = await db.DialogueSpans.Where(d => d.WorkId == workId).OrderBy(d => d.StartOffset)
            .Select(d => new DialogueSpanDto(
                d.Id,
                d.ChapterId,
                d.OrderIndexInChapter,
                d.StartOffset,
                d.EndOffset,
                d.SpeakerKind,
                d.Confidence,
                d.IsAiSuggested)).ToListAsync(cancellationToken).ConfigureAwait(false);

        return new TimelineDto(work.CanonicalText, segments, utterances, narratives, structureSections, workChapters,
            dialogueSpans);
    }

    private static bool TryParseTimelineKind(string? raw, out TimelineBulkKind kind)
    {
        kind = default;
        if (string.IsNullOrWhiteSpace(raw))
            return false;

        switch (raw.Trim().ToLowerInvariant())
        {
            case "utterance":
                kind = TimelineBulkKind.Utterance;
                return true;
            case "narrative":
            case "narrativepassage":
                kind = TimelineBulkKind.NarrativePassage;
                return true;
            case "dialoguespan":
                kind = TimelineBulkKind.DialogueSpan;
                return true;
            case "workchapter":
            case "chapter":
                kind = TimelineBulkKind.WorkChapter;
                return true;
            case "structuresection":
            case "structure":
                kind = TimelineBulkKind.StructureSection;
                return true;
            default:
                return false;
        }
    }

    private static string TruncateField(string? value, int maxLen)
    {
        if (string.IsNullOrWhiteSpace(value))
            return string.Empty;

        var t = value.Trim();
        return t.Length <= maxLen ? t : t[..maxLen];
    }

    private static string CombineDistinctTitles(IEnumerable<string?> titles, int maxLen)
    {
        var parts = titles.Where(t => !string.IsNullOrWhiteSpace(t)).Select(t => t!.Trim()).Distinct().ToList();
        return parts.Count == 0 ? string.Empty : TruncateField(string.Join(" · ", parts), maxLen);
    }

    private static string CombineDistinctNotes(IEnumerable<string> notes, int maxLen)
    {
        var parts = notes.Select(n => n.Trim()).Where(s => s.Length > 0).Distinct().ToList();
        return parts.Count == 0
            ? string.Empty
            : TruncateField(string.Join($"{Environment.NewLine}{Environment.NewLine}", parts), maxLen);
    }

    private async Task RenumberDialogueSpansAsync(Guid chapterId, CancellationToken cancellationToken)
    {
        var spans = await db.DialogueSpans.Where(d => d.ChapterId == chapterId).OrderBy(d => d.StartOffset)
            .ToListAsync(cancellationToken).ConfigureAwait(false);
        for (var i = 0; i < spans.Count; i++)
            spans[i].OrderIndexInChapter = i;
    }

    private async Task<ActionResult?> BulkMergeUtterances(Guid workId, IReadOnlyList<Guid> ids,
        CancellationToken cancellationToken)
    {
        var idList = ids.Distinct().ToList();
        var rows = await db.Utterances.Where(u => u.WorkId == workId && idList.Contains(u.Id))
            .ToListAsync(cancellationToken).ConfigureAwait(false);
        if (rows.Count != idList.Count)
            return BadRequest("One or more utterances were not found for this work.");
        if (rows.Count < 2)
            return BadRequest("Merge requires at least two utterances.");

        rows.Sort((a, b) => a.StartOffset.CompareTo(b.StartOffset));
        var survivor = rows[0];
        var deadIds = rows.Skip(1).Select(r => r.Id).ToList();

        await db.AudioArtifacts.Where(a =>
                a.WorkId == workId && a.UtteranceId != null && deadIds.Contains(a.UtteranceId.Value))
            .ExecuteUpdateAsync(s => s.SetProperty(a => a.UtteranceId, _ => null), cancellationToken)
            .ConfigureAwait(false);

        db.Utterances.RemoveRange(rows.Skip(1));
        survivor.StartOffset = rows.Min(r => r.StartOffset);
        survivor.EndOffset = rows.Max(r => r.EndOffset);
        survivor.Confidence = rows.Average(r => r.Confidence);
        survivor.IsAiSuggested = false;

        var mismatch = rows.Skip(1).Any(r =>
            r.SpeakerKind != survivor.SpeakerKind || r.CharacterId != survivor.CharacterId);
        if (mismatch)
            survivor.SpeakerNeedsReview = true;

        survivor.UserApproved = rows.All(r => r.UserApproved);

        return null;
    }

    private async Task<ActionResult?> BulkDeleteUtterances(Guid workId, IReadOnlyList<Guid> ids,
        CancellationToken cancellationToken)
    {
        var idList = ids.Distinct().ToList();
        var rows = await db.Utterances.Where(u => u.WorkId == workId && idList.Contains(u.Id))
            .ToListAsync(cancellationToken).ConfigureAwait(false);
        if (rows.Count != idList.Count)
            return BadRequest("One or more utterances were not found for this work.");

        var deadIds = rows.Select(r => r.Id).ToList();
        await db.AudioArtifacts.Where(a =>
                a.WorkId == workId && a.UtteranceId != null && deadIds.Contains(a.UtteranceId.Value))
            .ExecuteUpdateAsync(s => s.SetProperty(a => a.UtteranceId, _ => null), cancellationToken)
            .ConfigureAwait(false);

        db.Utterances.RemoveRange(rows);
        return null;
    }

    private async Task<ActionResult?> BulkMergeNarratives(Guid workId, IReadOnlyList<Guid> ids,
        CancellationToken cancellationToken)
    {
        var idList = ids.Distinct().ToList();
        var rows = await db.NarrativePassages.Where(p => p.WorkId == workId && idList.Contains(p.Id))
            .ToListAsync(cancellationToken).ConfigureAwait(false);
        if (rows.Count != idList.Count)
            return BadRequest("One or more narrator passages were not found for this work.");
        if (rows.Count < 2)
            return BadRequest("Merge requires at least two narrator passages.");

        rows.Sort((a, b) => a.StartOffset.CompareTo(b.StartOffset));
        var survivor = rows[0];

        var narratorChars = rows.Select(r => r.NarratorCharacterId).Distinct().ToList();
        survivor.NarratorCharacterId = narratorChars.Count == 1 ? narratorChars[0] : null;

        survivor.StartOffset = rows.Min(r => r.StartOffset);
        survivor.EndOffset = rows.Max(r => r.EndOffset);
        survivor.PerspectiveNotes = CombineDistinctNotes(rows.Select(r => r.PerspectiveNotes), 1024);
        survivor.IsAiSuggested = false;

        db.NarrativePassages.RemoveRange(rows.Skip(1));
        return null;
    }

    private async Task<ActionResult?> BulkDeleteNarratives(Guid workId, IReadOnlyList<Guid> ids,
        CancellationToken cancellationToken)
    {
        var idList = ids.Distinct().ToList();
        var rows = await db.NarrativePassages.Where(p => p.WorkId == workId && idList.Contains(p.Id))
            .ToListAsync(cancellationToken).ConfigureAwait(false);
        if (rows.Count != idList.Count)
            return BadRequest("One or more narrator passages were not found for this work.");

        db.NarrativePassages.RemoveRange(rows);
        return null;
    }

    private async Task<ActionResult?> BulkMergeDialogueSpans(Guid workId, IReadOnlyList<Guid> ids,
        CancellationToken cancellationToken)
    {
        var idList = ids.Distinct().ToList();
        var rows = await db.DialogueSpans.Where(d => d.WorkId == workId && idList.Contains(d.Id))
            .ToListAsync(cancellationToken).ConfigureAwait(false);
        if (rows.Count != idList.Count)
            return BadRequest("One or more dialogue spans were not found for this work.");
        if (rows.Count < 2)
            return BadRequest("Merge requires at least two dialogue spans.");

        var chapterIds = rows.Select(r => r.ChapterId).Distinct().ToList();
        if (chapterIds.Count != 1)
            return BadRequest("Merge dialogue spans only within the same chapter.");

        rows.Sort((a, b) => a.StartOffset.CompareTo(b.StartOffset));
        var survivor = rows[0];
        var chapterId = survivor.ChapterId;

        survivor.StartOffset = rows.Min(r => r.StartOffset);
        survivor.EndOffset = rows.Max(r => r.EndOffset);
        survivor.Confidence = rows.Average(r => r.Confidence);
        survivor.IsAiSuggested = false;

        db.DialogueSpans.RemoveRange(rows.Skip(1));

        await RenumberDialogueSpansAsync(chapterId, cancellationToken).ConfigureAwait(false);
        return null;
    }

    private async Task<ActionResult?> BulkDeleteDialogueSpans(Guid workId, IReadOnlyList<Guid> ids,
        CancellationToken cancellationToken)
    {
        var idList = ids.Distinct().ToList();
        var rows = await db.DialogueSpans.Where(d => d.WorkId == workId && idList.Contains(d.Id))
            .ToListAsync(cancellationToken).ConfigureAwait(false);
        if (rows.Count != idList.Count)
            return BadRequest("One or more dialogue spans were not found for this work.");

        var chapterIds = rows.Select(r => r.ChapterId).Distinct().ToList();
        db.DialogueSpans.RemoveRange(rows);

        foreach (var chId in chapterIds)
            await RenumberDialogueSpansAsync(chId, cancellationToken).ConfigureAwait(false);

        return null;
    }

    private async Task<ActionResult?> BulkMergeWorkChapters(Guid workId, IReadOnlyList<Guid> ids,
        CancellationToken cancellationToken)
    {
        var idList = ids.Distinct().ToList();
        var rows = await db.WorkChapters.Where(c => c.WorkId == workId && idList.Contains(c.Id))
            .ToListAsync(cancellationToken).ConfigureAwait(false);
        if (rows.Count != idList.Count)
            return BadRequest("One or more chapters were not found for this work.");
        if (rows.Count < 2)
            return BadRequest("Merge requires at least two chapters.");

        rows.Sort((a, b) => a.OrderIndex.CompareTo(b.OrderIndex));
        var survivor = rows[0];

        foreach (var victim in rows.Skip(1))
        {
            await db.DialogueSpans.Where(d => d.ChapterId == victim.Id)
                .ExecuteUpdateAsync(s => s.SetProperty(d => d.ChapterId, _ => survivor.Id), cancellationToken)
                .ConfigureAwait(false);
        }

        survivor.StartOffset = rows.Min(r => r.StartOffset);
        survivor.EndOffset = rows.Max(r => r.EndOffset);
        survivor.Title = CombineDistinctTitles(rows.Select(r => r.Title), 512);
        survivor.Notes = CombineDistinctNotes(rows.Select(r => r.Notes), 2048);
        survivor.HeadingStartOffset = null;
        survivor.HeadingEndOffset = null;
        survivor.IsAiSuggested = false;

        db.WorkChapters.RemoveRange(rows.Skip(1));

        var chaptersLeft = await db.WorkChapters.Where(c => c.WorkId == workId).OrderBy(c => c.StartOffset)
            .ToListAsync(cancellationToken).ConfigureAwait(false);
        for (var i = 0; i < chaptersLeft.Count; i++)
            chaptersLeft[i].OrderIndex = i;

        foreach (var ch in chaptersLeft)
            await RenumberDialogueSpansAsync(ch.Id, cancellationToken).ConfigureAwait(false);

        return null;
    }

    private async Task<ActionResult?> BulkDeleteWorkChapters(Guid workId, IReadOnlyList<Guid> ids,
        CancellationToken cancellationToken)
    {
        var idList = ids.Distinct().ToList();
        var victims = await db.WorkChapters.Where(c => c.WorkId == workId && idList.Contains(c.Id))
            .ToListAsync(cancellationToken).ConfigureAwait(false);
        if (victims.Count != idList.Count)
            return BadRequest("One or more chapters were not found for this work.");

        var survivor = await db.WorkChapters.Where(c => c.WorkId == workId && !idList.Contains(c.Id))
            .OrderBy(c => c.OrderIndex).FirstOrDefaultAsync(cancellationToken).ConfigureAwait(false);

        if (survivor is null)
            return BadRequest("Cannot delete every chapter.");

        foreach (var victim in victims)
        {
            await db.DialogueSpans.Where(d => d.ChapterId == victim.Id)
                .ExecuteUpdateAsync(s => s.SetProperty(d => d.ChapterId, _ => survivor.Id), cancellationToken)
                .ConfigureAwait(false);
        }

        db.WorkChapters.RemoveRange(victims);

        var chaptersLeft = await db.WorkChapters.Where(c => c.WorkId == workId).OrderBy(c => c.StartOffset)
            .ToListAsync(cancellationToken).ConfigureAwait(false);
        for (var i = 0; i < chaptersLeft.Count; i++)
            chaptersLeft[i].OrderIndex = i;

        foreach (var ch in chaptersLeft)
            await RenumberDialogueSpansAsync(ch.Id, cancellationToken).ConfigureAwait(false);

        return null;
    }

    private async Task<ActionResult?> BulkMergeStructureSections(Guid workId, IReadOnlyList<Guid> ids,
        CancellationToken cancellationToken)
    {
        var idList = ids.Distinct().ToList();
        var rows = await db.StoryStructureSections.Where(s => s.WorkId == workId && idList.Contains(s.Id))
            .ToListAsync(cancellationToken).ConfigureAwait(false);
        if (rows.Count != idList.Count)
            return BadRequest("One or more structural sections were not found for this work.");
        if (rows.Count < 2)
            return BadRequest("Merge requires at least two structural sections.");

        rows.Sort((a, b) => a.StartOffset.CompareTo(b.StartOffset));
        var survivor = rows[0];

        survivor.StartOffset = rows.Min(r => r.StartOffset);
        survivor.EndOffset = rows.Max(r => r.EndOffset);
        survivor.Title = CombineDistinctTitles(rows.Select(r => r.Title), 512);
        survivor.Notes = CombineDistinctNotes(rows.Select(r => r.Notes), 2048);
        survivor.IsAiSuggested = false;

        db.StoryStructureSections.RemoveRange(rows.Skip(1));
        return null;
    }

    private async Task<ActionResult?> BulkDeleteStructureSections(Guid workId, IReadOnlyList<Guid> ids,
        CancellationToken cancellationToken)
    {
        var idList = ids.Distinct().ToList();
        var rows = await db.StoryStructureSections.Where(s => s.WorkId == workId && idList.Contains(s.Id))
            .ToListAsync(cancellationToken).ConfigureAwait(false);
        if (rows.Count != idList.Count)
            return BadRequest("One or more structural sections were not found for this work.");

        db.StoryStructureSections.RemoveRange(rows);
        return null;
    }
}
