using SmartNarrator.Infrastructure.Text;

namespace SmartNarrator.Infrastructure.Ai;

internal static class ChapterPartitionNormalizer
{
    internal sealed record GlobalChapterBoundary(int Start, int End, string? Title, int? HeadingStart, int? HeadingEnd);

    /// <summary>Merges overlapping chunk proposals then fills gaps so [0, storyLen) is partitioned.</summary>
    internal static List<GlobalChapterBoundary> NormalizePartition(int storyLenUtf16,
        List<GlobalChapterBoundary> raw)
    {
        if (storyLenUtf16 <= 0)
            return [];

        var cleaned = raw
            .Where(x => x.End > x.Start && x.Start >= 0 && x.End <= storyLenUtf16)
            .OrderBy(x => x.Start)
            .ToList();

        if (cleaned.Count == 0)
            return [new GlobalChapterBoundary(0, storyLenUtf16, null, null, null)];

        var merged = new List<GlobalChapterBoundary>();
        foreach (var x in cleaned)
        {
            if (merged.Count == 0)
            {
                merged.Add(x);
                continue;
            }

            var last = merged[^1];
            if (x.Start <= last.End)
            {
                merged[^1] = new GlobalChapterBoundary(
                    last.Start,
                    Math.Max(last.End, x.End),
                    CoalesceTitle(last.Title, x.Title),
                    last.HeadingStart ?? x.HeadingStart,
                    last.HeadingEnd ?? x.HeadingEnd);
            }
            else
            {
                merged.Add(x);
            }
        }

        var filled = new List<GlobalChapterBoundary>();
        var cursor = 0;
        foreach (var m in merged)
        {
            if (cursor < m.Start)
                filled.Add(new GlobalChapterBoundary(cursor, m.Start, null, null, null));

            filled.Add(m);
            cursor = Math.Max(cursor, m.End);
        }

        if (cursor < storyLenUtf16)
            filled.Add(new GlobalChapterBoundary(cursor, storyLenUtf16, null, null, null));

        return filled;
    }

    /// <summary>
    /// Merges whitespace/punctuation‑only partitions into neighboring chapters so ingest never keeps filler‑only
    /// chapters or parallel structural chapter markers.
    /// </summary>
    internal static List<GlobalChapterBoundary> CollapseWhitespaceOnlyChapterSlices(string canonicalUtf16,
        IReadOnlyList<GlobalChapterBoundary> filledPartitions)
    {
        var storyLen = canonicalUtf16.Length;
        if (storyLen <= 0)
            return [];

        var parts = filledPartitions.Where(x => x.End > x.Start && x.Start >= 0 && x.End <= storyLen)
            .OrderBy(x => x.Start)
            .ToList();

        if (parts.Count == 0)
            return [new GlobalChapterBoundary(0, storyLen, null, null, null)];

        var chapters = new List<GlobalChapterBoundary>();
        var idx = 0;

        while (idx < parts.Count)
        {
            while (idx < parts.Count &&
                   !NarratableTextProbe.SliceHasSpeakableContent(canonicalUtf16, parts[idx].Start, parts[idx].End))
                idx++;

            if (idx >= parts.Count)
                break;

            var chapStart = chapters.Count == 0 ? 0 : parts[idx].Start;
            var seg = parts[idx];
            var chapEnd = seg.End;
            var title = seg.Title;
            var hs = seg.HeadingStart;
            var he = seg.HeadingEnd;
            idx++;

            while (idx < parts.Count &&
                   !NarratableTextProbe.SliceHasSpeakableContent(canonicalUtf16, parts[idx].Start, parts[idx].End))
            {
                chapEnd = parts[idx].End;
                idx++;
            }

            chapters.Add(new GlobalChapterBoundary(chapStart, chapEnd, title, hs, he));
        }

        if (chapters.Count == 0)
            chapters.Add(new GlobalChapterBoundary(0, storyLen, null, null, null));
        else if (chapters[^1].End < storyLen)
            chapters[^1] = chapters[^1] with { End = storyLen };

        return chapters;
    }

    private static string? CoalesceTitle(string? a, string? b)
    {
        if (!string.IsNullOrWhiteSpace(a))
            return a;
        return string.IsNullOrWhiteSpace(b) ? null : b;
    }
}
