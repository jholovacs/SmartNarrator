using System.Text;

namespace SmartNarrator.Infrastructure.Ingest;

/// <summary>Optional phase label helps explain long-running steps where percent moves slowly.</summary>
public readonly record struct StoryExtractProgress(int Percent, string? Phase = null);

public static class StoryTextExtractors
{
    /// <summary>Ingest timeout for a single document (PDFs can be slow; still fails fast if PdfPig hangs on open).</summary>
    public static readonly TimeSpan ExtractTimeout = TimeSpan.FromMinutes(35);

    internal static Task EmitProgress(Func<StoryExtractProgress, Task>? report, int percent, string? phase = null) =>
        report?.Invoke(new StoryExtractProgress(percent, phase)) ?? Task.CompletedTask;

    public static string NormalizeLineEndings(ReadOnlySpan<char> input)
    {
        var sb = new StringBuilder(input.Length);
        for (var i = 0; i < input.Length; i++)
        {
            var c = input[i];
            if (c == '\r')
            {
                if (i + 1 < input.Length && input[i + 1] == '\n')
                {
                    sb.Append('\n');
                    i++;
                }
                else
                {
                    sb.Append('\n');
                }
            }
            else
            {
                sb.Append(c);
            }
        }

        return sb.ToString();
    }

    /// <summary>
    /// LF normalization matching <see cref="NormalizeLineEndings"/> plus a UTF-16 prefix map:
    /// output index where content beginning at input index <paramref name="k"/> starts is <c>Utf16PrefixMap[k]</c>
    /// (half-open spans use <c>[map[start], map[end])</c>).
    /// </summary>
    public static (string Normalized, int[] Utf16PrefixMap) NormalizeLineEndingsWithPrefixMap(string input)
    {
        if (string.IsNullOrEmpty(input))
            return (string.Empty, [0]);

        var n = input.Length;
        var prefixMap = new int[n + 1];
        prefixMap[0] = 0;
        var sb = new StringBuilder(n);

        for (var i = 0; i < n; i++)
        {
            var c = input[i];
            if (c == '\r')
            {
                if (i + 1 < n && input[i + 1] == '\n')
                {
                    sb.Append('\n');
                    i++;
                }
                else
                    sb.Append('\n');
            }
            else
                sb.Append(c);

            prefixMap[i + 1] = sb.Length;
        }

        return (sb.ToString(), prefixMap);
    }

    /// <summary>
    /// Collapses horizontal ASCII whitespace runs (space and tab only) to a single space without touching newlines.
    /// </summary>
    public static (string Collapsed, int[] Utf16PrefixMap) CollapseHorizontalWhitespaceRunsWithPrefixMap(string input)
    {
        if (string.IsNullOrEmpty(input))
            return (string.Empty, [0]);

        var n = input.Length;
        var prefixMap = new int[n + 1];
        prefixMap[0] = 0;
        var sb = new StringBuilder(n);

        var prevHoriz = false;
        for (var i = 0; i < n; i++)
        {
            var c = input[i];
            var horiz = c is ' ' or '\t';
            if (horiz)
            {
                if (!prevHoriz)
                    sb.Append(' ');
                prevHoriz = true;
            }
            else
            {
                sb.Append(c);
                prevHoriz = false;
            }

            prefixMap[i + 1] = sb.Length;
        }

        return (sb.ToString(), prefixMap);
    }

    /// <summary>
    /// Collapses runs of three or more consecutive newlines to exactly two (paragraph gap); single/double runs unchanged.
    /// </summary>
    public static (string Collapsed, int[] Utf16PrefixMap) CollapseExcessiveNewlinesWithPrefixMap(string input)
    {
        if (string.IsNullOrEmpty(input))
            return (string.Empty, [0]);

        var n = input.Length;
        var prefixMap = new int[n + 1];
        prefixMap[0] = 0;
        var sb = new StringBuilder(n);

        var i = 0;
        while (i < n)
        {
            var c = input[i];
            if (c != '\n')
            {
                sb.Append(c);
                prefixMap[i + 1] = sb.Length;
                i++;
                continue;
            }

            var runStart = i;
            while (i < n && input[i] == '\n')
                i++;

            var runLen = i - runStart;
            var beforeLen = sb.Length;
            var add = runLen >= 3 ? 2 : runLen;
            sb.Append('\n', add);

            for (var k = runStart; k < i - 1; k++)
                prefixMap[k + 1] = beforeLen;

            prefixMap[i] = beforeLen + add;
        }

        return (sb.ToString(), prefixMap);
    }

    public static string ExtractFromPlainOrMarkdown(ReadOnlySpan<char> text) =>
        NormalizeLineEndings(text).Trim('\uFEFF', ' ', '\t', '\n', '\r');

    /// <summary>
    /// PdfPig / PDF font bugs can emit lone UTF-16 surrogates (invalid Unicode); UTF-8 encoding for Npgsql then throws.
    /// PostgreSQL <c>text</c> also rejects NUL bytes—replace both with U+FFFD so ingest completes.
    /// </summary>
    public static string SanitizeForPostgreSqlText(string input)
    {
        if (string.IsNullOrEmpty(input))
            return input;

        var needsFix = false;
        for (var i = 0; i < input.Length; i++)
        {
            var c = input[i];
            if (c == '\0')
            {
                needsFix = true;
                break;
            }

            if (char.IsHighSurrogate(c))
            {
                if (i + 1 >= input.Length || !char.IsLowSurrogate(input[i + 1]))
                {
                    needsFix = true;
                    break;
                }

                i++;
                continue;
            }

            if (char.IsLowSurrogate(c))
            {
                needsFix = true;
                break;
            }
        }

        if (!needsFix)
            return input;

        var sb = new StringBuilder(input.Length);
        for (var i = 0; i < input.Length;)
        {
            var c = input[i];
            if (c == '\0')
            {
                sb.Append('\uFFFD');
                i++;
                continue;
            }

            if (char.IsHighSurrogate(c))
            {
                if (i + 1 < input.Length && char.IsLowSurrogate(input[i + 1]))
                {
                    sb.Append(c);
                    sb.Append(input[i + 1]);
                    i += 2;
                    continue;
                }

                sb.Append('\uFFFD');
                i++;
                continue;
            }

            if (char.IsLowSurrogate(c))
            {
                sb.Append('\uFFFD');
                i++;
                continue;
            }

            sb.Append(c);
            i++;
        }

        return sb.ToString();
    }

    internal static async Task<string> NormalizeLineEndingsWithProgressAsync(
        string input,
        CancellationToken cancellationToken,
        Func<int, Task>? reportMappedPercentAsync,
        int percentLowInclusive,
        int percentHighInclusive)
    {
        var n = input.Length;
        if (n == 0)
            return string.Empty;

        var sb = new StringBuilder(n);
        const int yieldStride = 65536;
        var counter = 0;
        var lastReportedMapped = percentLowInclusive - 1;

        for (var i = 0; i < n; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var c = input[i];
            if (c == '\r')
            {
                if (i + 1 < n && input[i + 1] == '\n')
                {
                    sb.Append('\n');
                    i++;
                }
                else
                {
                    sb.Append('\n');
                }
            }
            else
            {
                sb.Append(c);
            }

            counter++;
            if (reportMappedPercentAsync is null || counter < yieldStride)
                continue;

            counter = 0;
            var mapped = percentLowInclusive +
                         (int)Math.Round((double)(i + 1) / n * (percentHighInclusive - percentLowInclusive));
            mapped = Math.Clamp(mapped, percentLowInclusive, percentHighInclusive);
            if (mapped > lastReportedMapped)
            {
                lastReportedMapped = mapped;
                await reportMappedPercentAsync.Invoke(mapped).ConfigureAwait(false);
            }

            await Task.Yield();
        }

        return sb.ToString();
    }

    /// <summary>Produces paragraph-aligned segments stored as offsets against <paramref name="canonicalText"/>.</summary>
    public static List<(int Start, int End)> BuildParagraphOffsets(string canonicalText)
    {
        var list = new List<(int, int)>();
        if (string.IsNullOrWhiteSpace(canonicalText))
            return list;

        var t = canonicalText;
        var n = t.Length;
        var i = 0;

        while (i < n)
        {
            while (i < n && char.IsWhiteSpace(t[i]))
                i++;

            if (i >= n)
                break;

            var start = i;

            while (i < n)
            {
                if (t[i] == '\n' && i + 1 < n && t[i + 1] == '\n')
                    break;

                i++;
            }

            var end = i;

            while (end > start && char.IsWhiteSpace(t[end - 1]))
                end--;

            if (end > start)
                list.Add((start, end));

            while (i < n && char.IsWhiteSpace(t[i]))
                i++;
        }

        return list;
    }

    /// <inheritdoc cref="BuildParagraphOffsets"/>
    public static async Task<List<(int Start, int End)>> BuildParagraphOffsetsAsync(string canonicalText,
        CancellationToken cancellationToken,
        Func<int, Task>? reportPercentMappedAsync,
        int percentLowInclusive,
        int percentHighInclusive)
    {
        var list = new List<(int, int)>();
        if (string.IsNullOrWhiteSpace(canonicalText))
            return list;

        var t = canonicalText;
        var n = t.Length;
        var i = 0;
        const int yieldStride = 65536;
        var lastYieldIndex = 0;
        var lastReportedMapped = percentLowInclusive - 1;

        while (i < n)
        {
            cancellationToken.ThrowIfCancellationRequested();

            while (i < n && char.IsWhiteSpace(t[i]))
                i++;

            if (i >= n)
                break;

            var start = i;

            while (i < n)
            {
                if (t[i] == '\n' && i + 1 < n && t[i + 1] == '\n')
                    break;

                i++;
            }

            var end = i;

            while (end > start && char.IsWhiteSpace(t[end - 1]))
                end--;

            if (end > start)
                list.Add((start, end));

            while (i < n && char.IsWhiteSpace(t[i]))
                i++;

            if (reportPercentMappedAsync is null || i - lastYieldIndex < yieldStride)
                continue;

            lastYieldIndex = i;
            var mapped = percentLowInclusive +
                         (int)Math.Round((double)i / n * (percentHighInclusive - percentLowInclusive));
            mapped = Math.Clamp(mapped, percentLowInclusive, percentHighInclusive);
            if (mapped > lastReportedMapped)
            {
                lastReportedMapped = mapped;
                await reportPercentMappedAsync.Invoke(mapped).ConfigureAwait(false);
            }

            await Task.Yield();
        }

        return list;
    }
}
