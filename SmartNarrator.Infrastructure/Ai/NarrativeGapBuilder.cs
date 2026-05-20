namespace SmartNarrator.Infrastructure.Ai;

internal static class NarrativeGapBuilder
{
    internal sealed record GapSlice(int StartOffset, int EndOffset);

    internal static List<GapSlice> GapsForChapter(int chapterStart, int chapterEnd,
        IReadOnlyList<(int Start, int End)> dialogueSpansSorted)
    {
        var gaps = new List<GapSlice>();
        var cursor = chapterStart;

        foreach (var (ds, de) in dialogueSpansSorted)
        {
            if (ds > chapterEnd)
                break;

            var segLo = Math.Max(ds, chapterStart);
            var segHi = Math.Min(de, chapterEnd);
            if (segHi <= segLo)
                continue;

            if (cursor < segLo)
                gaps.Add(new GapSlice(cursor, segLo));

            cursor = Math.Max(cursor, segHi);
            if (cursor >= chapterEnd)
                break;
        }

        if (cursor < chapterEnd)
            gaps.Add(new GapSlice(cursor, chapterEnd));

        return gaps.Where(g => g.EndOffset > g.StartOffset).ToList();
    }
}
