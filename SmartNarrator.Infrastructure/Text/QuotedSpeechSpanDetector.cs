namespace SmartNarrator.Infrastructure.Text;

/// <summary>
/// Rule-based quoted dialogue using paired quotation marks (ASCII doubles and common curly doubles/singles).
/// Supports continuation paragraphs where the closing mark is omitted until the utterance ends (same speaker).
/// Half-open UTF‑16 spans <c>[start, end)</c> aligned with canonical text / PostgreSQL offsets.
/// </summary>
internal static class QuotedSpeechSpanDetector
{
    private const char AsciiDoubleQuote = '"';
    private const char LeftDoubleQuote = '\u201C';
    private const char RightDoubleQuote = '\u201D';
    private const char LeftSingleQuote = '\u2018';
    private const char RightSingleQuote = '\u2019';

    /// <summary>
    /// Maximum UTF‑16 code units walked forward after an opening quote without seeing the closing delimiter.
    /// Beyond this we treat the opener as stray punctuation so pathological texts cannot stall for multiple full scans.
    /// </summary>
    private const int MaxUtf16UnitsWithoutCloser = 524_288;

    internal static List<(int Start, int End)> Detect(ReadOnlySpan<char> slice,
        CancellationToken cancellationToken = default)
    {
        var results = new List<(int Start, int End)>();
        if (slice.Length == 0)
            return results;

        var cursor = 0;
        while (cursor < slice.Length)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (!TryFindOpeningQuote(slice, cursor, out var openIdx, out var closingDelimiter))
                break;

            var endExclusive = FindClosingExclusive(slice, openIdx, closingDelimiter, cancellationToken);
            if (endExclusive <= openIdx)
            {
                cursor = openIdx + 1;
                continue;
            }

            results.Add((openIdx, endExclusive));
            cursor = endExclusive;
        }

        return results;
    }

    private static bool TryFindOpeningQuote(ReadOnlySpan<char> text, int from, out int openIdx,
        out char closingDelimiter)
    {
        openIdx = -1;
        closingDelimiter = AsciiDoubleQuote;

        for (var k = from; k < text.Length; k++)
        {
            switch (text[k])
            {
                case AsciiDoubleQuote:
                    openIdx = k;
                    closingDelimiter = AsciiDoubleQuote;
                    return true;
                case LeftDoubleQuote:
                    openIdx = k;
                    closingDelimiter = RightDoubleQuote;
                    return true;
                case LeftSingleQuote:
                    openIdx = k;
                    closingDelimiter = RightSingleQuote;
                    return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Walk forward after <paramref name="openIdx"/> until <paramref name="closingDelimiter"/>.
    /// Paragraph gaps (<see cref="TryConsumeParagraphGap"/>) without an intervening closer extend the utterance.
    /// </summary>
    private static int FindClosingExclusive(ReadOnlySpan<char> text, int openIdx, char closingDelimiter,
        CancellationToken cancellationToken)
    {
        var i = openIdx + 1;
        var utf16Advanced = 0;
        var cancelStride = 0;

        while (i < text.Length)
        {
            if ((cancelStride++ & 4095) == 0)
                cancellationToken.ThrowIfCancellationRequested();

            if (utf16Advanced > MaxUtf16UnitsWithoutCloser)
                return -1;

            var c = text[i];

            if (c == closingDelimiter)
                return i + 1;

            var walkBefore = i;
            if (TryConsumeParagraphGap(text, ref i))
            {
                utf16Advanced += i - walkBefore;
                continue;
            }

            var next = AdvanceUtf16(text, i);
            utf16Advanced += next - i;
            i = next;
        }

        return -1;
    }

    /// <summary>Returns true when position starts a blank-line paragraph boundary (<c>\n\n</c>-style).</summary>
    private static bool TryConsumeParagraphGap(ReadOnlySpan<char> text, ref int i)
    {
        var saved = i;
        var terminators = 0;

        while (i < text.Length)
        {
            if (text[i] == '\r')
            {
                i++;
                if (i < text.Length && text[i] == '\n')
                    i++;
                terminators++;
                continue;
            }

            if (text[i] == '\n')
            {
                i++;
                terminators++;
                continue;
            }

            break;
        }

        if (terminators < 2)
        {
            i = saved;
            return false;
        }

        while (i < text.Length && text[i] is ' ' or '\t')
            i++;

        return true;
    }

    private static int AdvanceUtf16(ReadOnlySpan<char> text, int i)
    {
        if (i + 1 < text.Length && char.IsSurrogatePair(text[i], text[i + 1]))
            return i + 2;
        return i + 1;
    }
}
