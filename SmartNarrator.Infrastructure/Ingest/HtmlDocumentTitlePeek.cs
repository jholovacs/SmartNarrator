using System.Net;
using System.Text;
using System.Text.RegularExpressions;

namespace SmartNarrator.Infrastructure.Ingest;

/// <summary>
/// Lightweight extraction of HTML document titles from an early prefix (imports only).
/// </summary>
public static partial class HtmlDocumentTitlePeek
{
    private const int MaxPeekBytes = 512 * 1024;

    [GeneratedRegex(@"<title\s*[^>]*>\s*(?<inner>.*?)\s*</title>",
        RegexOptions.IgnoreCase | RegexOptions.Singleline)]
    private static partial Regex TitleRegex();

    private static readonly Regex InnerTagStrip = new(@"<[^>]+>", RegexOptions.Singleline | RegexOptions.Compiled);

    public static async Task<string?> TryPeekTitleAsync(Stream stream, CancellationToken cancellationToken)
    {
        if (stream.CanSeek)
        {
            var original = stream.Position;
            try
            {
                stream.Position = 0;
                var buffer = new byte[MaxPeekBytes];
                var read = await stream.ReadAsync(buffer.AsMemory(0, buffer.Length), cancellationToken)
                    .ConfigureAwait(false);
                return ExtractFromUtf8Bytes(buffer.AsSpan(0, read));
            }
            finally
            {
                stream.Position = original;
            }
        }

        var copy = new MemoryStream(capacity: Math.Min(MaxPeekBytes, 64 * 1024));
        var buf = new byte[8192];
        long total = 0;
        while (total < MaxPeekBytes)
        {
            var want = (int)Math.Min(buf.Length, MaxPeekBytes - total);
            var n = await stream.ReadAsync(buf.AsMemory(0, want), cancellationToken).ConfigureAwait(false);
            if (n == 0)
                break;
            await copy.WriteAsync(buf.AsMemory(0, n), cancellationToken).ConfigureAwait(false);
            total += n;
        }

        return ExtractFromUtf8Bytes(copy.GetBuffer().AsSpan(0, (int)copy.Length));
    }

    private static string? ExtractFromUtf8Bytes(ReadOnlySpan<byte> utf8Prefix)
    {
        if (utf8Prefix.Length == 0)
            return null;

        string head;
        try
        {
            head = Encoding.UTF8.GetString(utf8Prefix);
        }
        catch
        {
            return null;
        }

        if (!head.Contains('<', StringComparison.Ordinal))
            return null;

        var m = TitleRegex().Match(head);
        if (!m.Success)
            return null;

        var raw = InnerTagStrip.Replace(m.Groups["inner"].Value, "");
        raw = WebUtility.HtmlDecode(raw).Trim();
        raw = Regex.Replace(raw, @"\s+", " ").Trim();
        return raw.Length > 0 ? raw : null;
    }
}
