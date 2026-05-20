using System.Text;
using AngleSharp.Dom;
using AngleSharp.Html.Parser;
using SmartNarrator.Domain.Enums;
using UglyToad.PdfPig;
using VersOne.Epub;
using VersOne.Epub.Options;

namespace SmartNarrator.Infrastructure.Ingest;

/// <summary>Canonical ingest produces Markdown plus optional structural chapter UTF‑16 spans (EPUB/HTML).</summary>
public sealed record MarkdownStructuralChapter(
    int StartOffset,
    int EndOffset,
    string? Title,
    int? HeadingStartOffset,
    int? HeadingEndOffset);

public sealed record MarkdownIngestExtract(string Markdown, IReadOnlyList<MarkdownStructuralChapter> StructuralChapters);

public static class StoryMarkdownImporter
{
    public static async Task<MarkdownIngestExtract> ExtractAsync(
        Stream stream,
        SourceFormat format,
        CancellationToken cancellationToken = default,
        Func<StoryExtractProgress, Task>? reportProgressAsync = null)
    {
        await StoryTextExtractors.EmitProgress(reportProgressAsync, 6, "Reading file…").ConfigureAwait(false);

        await using var ms = new MemoryStream();
        await stream.CopyToAsync(ms, cancellationToken).ConfigureAwait(false);
        var bytes = ms.ToArray();

        MarkdownIngestExtract raw = format switch
        {
            SourceFormat.PlainText => await ExtractPlainAsync(bytes, cancellationToken, reportProgressAsync)
                .ConfigureAwait(false),
            SourceFormat.Markdown => await ExtractMarkdownAsync(bytes, cancellationToken, reportProgressAsync)
                .ConfigureAwait(false),
            SourceFormat.Html => await ExtractHtmlAsync(bytes, cancellationToken, reportProgressAsync)
                .ConfigureAwait(false),
            SourceFormat.Pdf => await ExtractPdfAsync(bytes, cancellationToken, reportProgressAsync)
                .ConfigureAwait(false),
            SourceFormat.Epub => await ExtractEpubAsync(bytes, cancellationToken, reportProgressAsync)
                .ConfigureAwait(false),
            _ => await ExtractPlainAsync(bytes, cancellationToken, reportProgressAsync).ConfigureAwait(false),
        };

        List<MarkdownStructuralChapter>? chaptersMut =
            raw.StructuralChapters.Count > 0 ? raw.StructuralChapters.ToList() : null;

        var (normalized, mapNl) = StoryTextExtractors.NormalizeLineEndingsWithPrefixMap(raw.Markdown);
        if (chaptersMut is not null)
            RemapStructuralOffsets(chaptersMut, mapNl);

        var (collapsedH, mapH) = StoryTextExtractors.CollapseHorizontalWhitespaceRunsWithPrefixMap(normalized);
        if (chaptersMut is not null)
            RemapStructuralOffsets(chaptersMut, mapH);

        var (collapsed, mapNl2) = StoryTextExtractors.CollapseExcessiveNewlinesWithPrefixMap(collapsedH);
        if (chaptersMut is not null)
            RemapStructuralOffsets(chaptersMut, mapNl2);

        var md = TrimCanonicalMarkdown(collapsed);
        IReadOnlyList<MarkdownStructuralChapter> chaptersSource =
            chaptersMut is not null ? chaptersMut : raw.StructuralChapters;
        var chapters = AdjustChapterEndsForTrim(chaptersSource, md.Length);
        return new MarkdownIngestExtract(md, chapters);
    }

    private static string TrimCanonicalMarkdown(string md) => md.Trim('\uFEFF', ' ', '\t', '\n', '\r');

    /// <remarks>
    /// <paramref name="prefixMap"/> maps UTF-16 indices in the <strong>current</strong> string (length map.Length − 1).
    /// </remarks>
    private static void RemapStructuralOffsets(List<MarkdownStructuralChapter> chapters, int[] prefixMap)
    {
        var srcLen = prefixMap.Length - 1;
        for (var i = 0; i < chapters.Count; i++)
        {
            var ch = chapters[i];
            var lo = Math.Clamp(ch.StartOffset, 0, srcLen);
            var hi = Math.Clamp(ch.EndOffset, lo, srcLen);
            var ns = prefixMap[lo];
            var ne = prefixMap[hi];

            int? hs = null;
            int? he = null;
            if (ch.HeadingStartOffset is { } hss && ch.HeadingEndOffset is { } hee)
            {
                var hLo = Math.Clamp(hss, lo, hi);
                var hHi = Math.Clamp(hee, lo, hi);
                var hsOut = prefixMap[hLo];
                var heOut = prefixMap[hHi];
                if (heOut < hsOut)
                    heOut = hsOut;
                hsOut = Math.Clamp(hsOut, ns, ne);
                heOut = Math.Clamp(heOut, ns, ne);
                if (heOut < hsOut)
                    heOut = hsOut;
                hs = hsOut;
                he = heOut;
            }

            chapters[i] = new MarkdownStructuralChapter(ns, ne, ch.Title, hs, he);
        }
    }

    private static IReadOnlyList<MarkdownStructuralChapter> AdjustChapterEndsForTrim(
        IReadOnlyList<MarkdownStructuralChapter> chapters,
        int newLength)
    {
        if (chapters.Count == 0)
            return chapters;

        var arr = chapters.ToArray();
        var last = arr[^1];
        if (last.EndOffset > newLength)
            arr[^1] = last with { EndOffset = newLength };

        return arr;
    }

    private static async Task<MarkdownIngestExtract> ExtractPlainAsync(byte[] bytes,
        CancellationToken cancellationToken,
        Func<StoryExtractProgress, Task>? report)
    {
        cancellationToken.ThrowIfCancellationRequested();
        await StoryTextExtractors.EmitProgress(report, 42, "Decoding text…").ConfigureAwait(false);
        var raw = Encoding.UTF8.GetString(bytes);
        string text;
        if (raw.Length < 120_000)
        {
            text = StoryTextExtractors.ExtractFromPlainOrMarkdown(raw.AsSpan());
        }
        else
        {
            await StoryTextExtractors.EmitProgress(report, 43, "Normalizing line endings…").ConfigureAwait(false);
            text = await StoryTextExtractors.NormalizeLineEndingsWithProgressAsync(raw, cancellationToken,
                    async mapped => await StoryTextExtractors.EmitProgress(report, mapped).ConfigureAwait(false),
                    43, 47)
                .ConfigureAwait(false);
            text = text.Trim('\uFEFF', ' ', '\t', '\n', '\r');
        }

        await StoryTextExtractors.EmitProgress(report, 48).ConfigureAwait(false);
        return new MarkdownIngestExtract(text, []);
    }

    private static Task<MarkdownIngestExtract> ExtractMarkdownAsync(byte[] bytes,
        CancellationToken cancellationToken,
        Func<StoryExtractProgress, Task>? report) =>
        ExtractPlainAsync(bytes, cancellationToken, report);

    private static async Task<MarkdownIngestExtract> ExtractHtmlAsync(byte[] bytes,
        CancellationToken cancellationToken,
        Func<StoryExtractProgress, Task>? report)
    {
        cancellationToken.ThrowIfCancellationRequested();
        await StoryTextExtractors.EmitProgress(report, 40, "Parsing HTML…").ConfigureAwait(false);

        var htmlString = Encoding.UTF8.GetString(bytes);
        var parser = new HtmlParser();
        var doc = parser.ParseDocument(htmlString);
        var body = doc.Body ?? doc.DocumentElement;
        body = UnwrapOuterDivs(body);

        await StoryTextExtractors.EmitProgress(report, 44, "Converting HTML to Markdown…").ConfigureAwait(false);

        var headingTag = PickHeadingTag(body);
        List<List<IElement>> sections;
        if (headingTag is null)
            sections = [[body]];
        else
            sections = SplitBodyByHeading(body, headingTag);

        var built = BuildMarkdownFromHtmlSections(sections);
        await StoryTextExtractors.EmitProgress(report, 48).ConfigureAwait(false);
        return built;
    }

    private static async Task<MarkdownIngestExtract> ExtractPdfAsync(byte[] bytes,
        CancellationToken cancellationToken,
        Func<StoryExtractProgress, Task>? report)
    {
        cancellationToken.ThrowIfCancellationRequested();
        await StoryTextExtractors.EmitProgress(report, 8, "Loading PDF…").ConfigureAwait(false);
        await Task.Yield();
        cancellationToken.ThrowIfCancellationRequested();
        await StoryTextExtractors.EmitProgress(report, 9, "Parsing PDF structure…").ConfigureAwait(false);

        using var pdfStream = new MemoryStream(bytes, writable: false);
        using var document = PdfDocument.Open(pdfStream);
        var pageCount = document.NumberOfPages;
        var sb = new StringBuilder();

        if (pageCount <= 0)
        {
            await StoryTextExtractors.EmitProgress(report, 38, "Normalizing extracted text…").ConfigureAwait(false);
            var empty = StoryTextExtractors.ExtractFromPlainOrMarkdown(sb.ToString().AsSpan());
            await StoryTextExtractors.EmitProgress(report, 48).ConfigureAwait(false);
            return new MarkdownIngestExtract(empty, []);
        }

        await StoryTextExtractors.EmitProgress(report, 10, $"Reading PDF pages (0/{pageCount})…").ConfigureAwait(false);

        for (var pageNumber = 1; pageNumber <= pageCount; pageNumber++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var page = document.GetPage(pageNumber);
            var t = page.Text;
            if (!string.IsNullOrWhiteSpace(t))
            {
                sb.Append(t.Trim());
                sb.Append("\n\n---\n\n");
            }

            var pct = 10 + (int)Math.Round(27.0 * pageNumber / pageCount);
            pct = Math.Clamp(pct, 10, 37);
            await StoryTextExtractors.EmitProgress(report, pct, $"Reading PDF pages ({pageNumber}/{pageCount})…")
                .ConfigureAwait(false);
        }

        var core = sb.ToString();
        const string tail = "\n\n---\n\n";
        if (core.EndsWith(tail, StringComparison.Ordinal))
            core = core[..^tail.Length];

        await StoryTextExtractors.EmitProgress(report, 38, "Normalizing extracted text…").ConfigureAwait(false);
        await Task.Yield();

        var normalizedCore = await StoryTextExtractors.NormalizeLineEndingsWithProgressAsync(core, cancellationToken,
                async mapped => await StoryTextExtractors.EmitProgress(report, mapped).ConfigureAwait(false),
                39, 46)
            .ConfigureAwait(false);

        await StoryTextExtractors.EmitProgress(report, 48).ConfigureAwait(false);
        return new MarkdownIngestExtract(normalizedCore, []);
    }

    private static async Task<MarkdownIngestExtract> ExtractEpubAsync(byte[] bytes,
        CancellationToken cancellationToken,
        Func<StoryExtractProgress, Task>? report)
    {
        await StoryTextExtractors.EmitProgress(report, 12, "Reading EPUB…").ConfigureAwait(false);
        await using var ms = new MemoryStream(bytes, writable: false);
        var book = await EpubReader.ReadBookAsync(ms, new EpubReaderOptions()).ConfigureAwait(false);

        var order = book.ReadingOrder ?? [];
        var spineFiles = order.Where(IsEpubReadableDocument).ToList();
        if (spineFiles.Count == 0)
            return new MarkdownIngestExtract(string.Empty, []);

        var sb = new StringBuilder();
        var chapters = new List<MarkdownStructuralChapter>();
        const string sep = "\n\n---\n\n";

        for (var i = 0; i < spineFiles.Count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var file = spineFiles[i];
            if (i > 0)
                sb.Append(sep);

            var start = sb.Length;
            var html = file.Content ?? string.Empty;

            await StoryTextExtractors.EmitProgress(report,
                16 + (int)Math.Round(30.0 * i / spineFiles.Count),
                $"Converting EPUB part ({i + 1}/{spineFiles.Count})…").ConfigureAwait(false);

            var navTitle = NavTitleForFile(book.Navigation, file);
            var htmlTitle = FirstHeadingTitleFromHtml(html);
            var title = !string.IsNullOrWhiteSpace(navTitle) ? navTitle : htmlTitle;

            int? hs = null, he = null;
            if (!string.IsNullOrWhiteSpace(title))
            {
                hs = sb.Length;
                sb.Append("# ").AppendLine(title.Trim());
                he = sb.Length;
                sb.AppendLine();
            }

            var mdBody = HtmlToMarkdownConverter.ConvertHtmlDocumentSource(html).Trim();
            if (mdBody.Length > 0)
                sb.Append(mdBody);

            var end = sb.Length;
            chapters.Add(new MarkdownStructuralChapter(start, end, title, hs, he));
        }

        await StoryTextExtractors.EmitProgress(report, 48).ConfigureAwait(false);
        return new MarkdownIngestExtract(sb.ToString(), chapters);
    }

    private static bool IsEpubReadableDocument(EpubLocalTextContentFile file) =>
        file.ContentType is EpubContentType.XHTML_1_1 or EpubContentType.OEB1_DOCUMENT or EpubContentType.XML
            or EpubContentType.DTBOOK;

    private static IEnumerable<EpubNavigationItem> FlattenNav(IEnumerable<EpubNavigationItem>? items)
    {
        if (items is null)
            yield break;

        foreach (var i in items)
        {
            yield return i;
            foreach (var c in FlattenNav(i.NestedItems))
                yield return c;
        }
    }

    private static string? NavTitleForFile(IEnumerable<EpubNavigationItem>? nav, EpubLocalTextContentFile file)
    {
        foreach (var item in FlattenNav(nav))
        {
            if (item.HtmlContentFile?.Key == file.Key && !string.IsNullOrWhiteSpace(item.Title))
                return item.Title.Trim();
        }

        return null;
    }

    private static string? FirstHeadingTitleFromHtml(string htmlFragment)
    {
        var parser = new HtmlParser();
        var doc = parser.ParseDocument(htmlFragment);
        var h = doc.QuerySelector("h1") ?? doc.QuerySelector("h2") ?? doc.QuerySelector("h3");
        var t = h?.TextContent?.Trim();
        return string.IsNullOrWhiteSpace(t) ? null : t;
    }

    private static IElement UnwrapOuterDivs(IElement body)
    {
        var cur = body;
        while (cur.ChildElementCount == 1 &&
               cur.FirstElementChild!.LocalName.Equals("div", StringComparison.OrdinalIgnoreCase))
            cur = cur.FirstElementChild!;

        return cur;
    }

    private static string? PickHeadingTag(IElement root)
    {
        if (root.QuerySelector("h1") != null)
            return "h1";
        if (root.QuerySelector("h2") != null)
            return "h2";
        if (root.QuerySelector("h3") != null)
            return "h3";
        return null;
    }

    private static List<List<IElement>> SplitBodyByHeading(IElement body, string headingTag)
    {
        var chapters = new List<List<IElement>>();
        foreach (var child in body.Children)
        {
            if (child.LocalName.Equals(headingTag, StringComparison.OrdinalIgnoreCase))
                chapters.Add(CollectHeadingSection(child, headingTag).ToList());
        }

        return chapters.Count > 0 ? chapters : [[body]];
    }

    private static IEnumerable<IElement> CollectHeadingSection(IElement heading, string headingTag)
    {
        yield return heading;
        var n = heading.NextSibling;
        while (n != null)
        {
            if (n is IElement el && el.LocalName.Equals(headingTag, StringComparison.OrdinalIgnoreCase))
                yield break;

            if (n is IElement el2)
                yield return el2;

            n = n.NextSibling;
        }
    }

    private static MarkdownIngestExtract BuildMarkdownFromHtmlSections(List<List<IElement>> sections)
    {
        var sb = new StringBuilder();
        var chapters = new List<MarkdownStructuralChapter>();
        const string sep = "\n\n---\n\n";

        for (var i = 0; i < sections.Count; i++)
        {
            if (i > 0)
                sb.Append(sep);

            var start = sb.Length;
            var sec = sections[i];
            int? hs = null, he = null;
            string? title = null;

            var first = sec.Count > 0 ? sec[0] : null;
            if (first is not null && IsHeadingElement(first))
            {
                hs = sb.Length;
                sb.AppendLine(HtmlToMarkdownConverter.ConvertHeadingLine(first));
                he = sb.Length;
                title = string.IsNullOrWhiteSpace(first.TextContent) ? null : first.TextContent.Trim();
                sb.AppendLine();
                var tail = sec.Count > 1 ? HtmlToMarkdownConverter.ConvertElementRoots(sec.Skip(1)) : "";
                if (!string.IsNullOrWhiteSpace(tail))
                    sb.AppendLine(tail.Trim());
            }
            else
            {
                sb.Append(HtmlToMarkdownConverter.ConvertElementRoots(sec).Trim());
            }

            var end = sb.Length;
            chapters.Add(new MarkdownStructuralChapter(start, end, title, hs, he));
        }

        return new MarkdownIngestExtract(sb.ToString(), chapters);
    }

    private static bool IsHeadingElement(IElement el)
    {
        if (el.LocalName.Length != 2 || char.ToLowerInvariant(el.LocalName[0]) != 'h')
            return false;

        return el.LocalName[1] is >= '1' and <= '6';
    }
}
