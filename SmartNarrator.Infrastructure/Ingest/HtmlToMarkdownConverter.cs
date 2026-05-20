using System.Text;
using AngleSharp.Dom;
namespace SmartNarrator.Infrastructure.Ingest;

/// <summary>Lightweight HTML → Markdown for fiction imports (headings, paragraphs, emphasis, lists, quotes).</summary>
internal static class HtmlToMarkdownConverter
{
    internal static string ConvertHtmlDocumentSource(string htmlSource)
    {
        var parser = new AngleSharp.Html.Parser.HtmlParser();
        var doc = parser.ParseDocument(htmlSource);
        var root = doc.Body ?? doc.DocumentElement;
        return ConvertSubtree(root).Trim();
    }

    internal static string ConvertElementRoots(IEnumerable<IElement> roots)
    {
        var sb = new StringBuilder();
        foreach (var r in roots)
        {
            var piece = ConvertSubtree(r);
            if (piece.Length == 0)
                continue;
            if (sb.Length > 0)
                sb.Append("\n\n");
            sb.Append(piece);
        }

        return sb.ToString().Trim();
    }

    internal static string ConvertHeadingLine(IElement heading)
    {
        if (!TryHeadingLevel(heading.LocalName, out var level))
            return ConvertSubtree(heading).Trim();

        var text = TrimSingleLine(ConvertInlineChildren(heading));
        return $"{new string('#', level)} {text}";
    }

    private static string ConvertSubtree(IElement root)
    {
        var sb = new StringBuilder();
        foreach (var child in root.ChildNodes)
            AppendFlowNode(sb, child);
        return sb.ToString().TrimEnd();
    }

    private static void AppendFlowNode(StringBuilder sb, INode node)
    {
        switch (node)
        {
            case IText text:
                sb.Append(EscapePlainText(text.Data));
                break;
            case IElement el:
                AppendFlowElement(sb, el);
                break;
        }
    }

    private static void AppendFlowElement(StringBuilder sb, IElement el)
    {
        var tag = el.LocalName.ToLowerInvariant();
        switch (tag)
        {
            case "script":
            case "style":
            case "noscript":
            case "template":
                return;
            case "br":
                sb.Append('\n');
                return;
            case "hr":
                sb.Append("\n\n---\n\n");
                return;
        }

        if (TryHeadingLevel(tag, out var hLevel))
        {
            sb.Append('\n').Append('#', hLevel).Append(' ')
                .Append(TrimSingleLine(ConvertInlineChildren(el)))
                .Append("\n\n");
            return;
        }

        switch (tag)
        {
            case "p":
                sb.Append(ConvertInlineChildren(el).TrimEnd());
                sb.Append("\n\n");
                return;
            case "blockquote":
                foreach (var line in ConvertInlineChildren(el).Split('\n', StringSplitOptions.RemoveEmptyEntries))
                    sb.Append("> ").Append(line.Trim()).Append('\n');
                sb.Append('\n');
                return;
            case "ul":
                AppendList(sb, el, ordered: false);
                return;
            case "ol":
                AppendList(sb, el, ordered: true);
                return;
            case "pre":
                sb.Append("\n```\n");
                sb.Append(el.TextContent.Replace("```", "`\u200b``", StringComparison.Ordinal));
                sb.Append("\n```\n\n");
                return;
            case "div":
            case "section":
            case "article":
            case "main":
            case "header":
            case "footer":
            case "aside":
            case "figure":
            case "figcaption":
                foreach (var child in el.ChildNodes)
                    AppendFlowNode(sb, child);
                return;
            case "li" when el.Parent is IElement pel &&
                             pel.LocalName.Equals("ul", StringComparison.OrdinalIgnoreCase):
                sb.Append("- ").Append(ConvertInlineChildren(el).Trim()).Append('\n');
                return;
            case "li" when el.Parent is IElement pel2 &&
                             pel2.LocalName.Equals("ol", StringComparison.OrdinalIgnoreCase):
                sb.Append("1. ").Append(ConvertInlineChildren(el).Trim()).Append('\n');
                return;
            default:
                foreach (var child in el.ChildNodes)
                    AppendFlowNode(sb, child);
                break;
        }
    }

    private static void AppendList(StringBuilder sb, IElement listEl, bool ordered)
    {
        var i = 1;
        foreach (var li in listEl.Children.Where(c =>
                     string.Equals(c.LocalName, "li", StringComparison.OrdinalIgnoreCase)))
        {
            if (ordered)
                sb.Append(i++).Append(". ");
            else
                sb.Append("- ");

            sb.Append(ConvertInlineChildren(li).Trim());
            sb.Append('\n');
        }

        sb.Append('\n');
    }

    private static string ConvertInlineChildren(IElement el)
    {
        var sb = new StringBuilder();
        foreach (var child in el.ChildNodes)
            AppendInlineNode(sb, child);
        return sb.ToString();
    }

    private static void AppendInlineNode(StringBuilder sb, INode node)
    {
        switch (node)
        {
            case IText text:
                sb.Append(EscapePlainText(text.Data));
                break;
            case IElement el:
                AppendInlineElement(sb, el);
                break;
        }
    }

    private static void AppendInlineElement(StringBuilder sb, IElement el)
    {
        var tag = el.LocalName.ToLowerInvariant();
        switch (tag)
        {
            case "br":
                sb.Append('\n');
                return;
            case "strong":
            case "b":
                sb.Append("**").Append(ConvertInlineChildren(el).Trim()).Append("**");
                return;
            case "em":
            case "i":
                sb.Append('*').Append(ConvertInlineChildren(el).Trim()).Append('*');
                return;
            case "code":
                sb.Append('`').Append(el.TextContent.Trim().Replace("`", "`\u200b`", StringComparison.Ordinal)).Append('`');
                return;
            case "a":
                var href = el.GetAttribute("href")?.Trim();
                var label = ConvertInlineChildren(el).Trim();
                if (string.IsNullOrWhiteSpace(href))
                {
                    sb.Append(label);
                    return;
                }

                sb.Append('[').Append(string.IsNullOrEmpty(label) ? href : label).Append("](").Append(href)
                    .Append(')');
                return;
            default:
                foreach (var child in el.ChildNodes)
                    AppendInlineNode(sb, child);
                break;
        }
    }

    private static bool TryHeadingLevel(string localName, out int level)
    {
        level = 0;
        if (localName.Length != 2 || char.ToLowerInvariant(localName[0]) != 'h')
            return false;

        if (!char.IsAsciiDigit(localName[1]))
            return false;

        level = localName[1] - '0';
        return level is >= 1 and <= 6;
    }

    private static string TrimSingleLine(string s)
    {
        var t = s.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n').Trim();
        var lines = t.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        return lines.Length == 0 ? "" : string.Join(' ', lines.Select(x => x.Trim()));
    }

    private static string EscapePlainText(string? data)
    {
        if (string.IsNullOrEmpty(data))
            return string.Empty;

        return data.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n');
    }
}
