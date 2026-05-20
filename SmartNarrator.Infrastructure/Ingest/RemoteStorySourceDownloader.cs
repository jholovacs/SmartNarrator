using System.Net;
using System.Net.Http.Headers;
using System.Text;
using Microsoft.Extensions.Logging;
using SmartNarrator.Application.Ports;

namespace SmartNarrator.Infrastructure.Ingest;

public sealed class RemoteStorySourceDownloader(
    ILogger<RemoteStorySourceDownloader> logger,
    IHttpClientFactory httpClientFactory) : IRemoteStorySourceDownloader
{
    public async Task<RemoteStoryFetchResult> FetchAsync(Uri url, CancellationToken cancellationToken = default)
    {
        var client = httpClientFactory.CreateClient("StoryUrlImport");
        Uri current = url.NormalizeMinimal();

        var redirectsSeen = 0;
        while (true)
        {
            await StoryUrlDownloadSafety.UriMustBeSafelyResolvableAsync(current, cancellationToken)
                .ConfigureAwait(false);

            using var req = new HttpRequestMessage(HttpMethod.Get, current);
            using var response =
                await client.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
                    .ConfigureAwait(false);

            if (IsRedirect(response.StatusCode))
            {
                if (redirectsSeen >= StoryUrlDownloadSafety.MaxRedirects)
                    throw new InvalidOperationException(
                        $"Too many HTTP redirects ({StoryUrlDownloadSafety.MaxRedirects} max).");

                redirectsSeen++;
                current = StoryUrlDownloadSafety.ResolveRedirect(current, response.Headers.Location);
                logger.LogInformation("Story URL redirect hop {Hop} → {Uri}", redirectsSeen,
                    current.ToString());

                await response.Content.CopyToAsync(Stream.Null, cancellationToken).ConfigureAwait(false);
                continue;
            }

            if (!response.IsSuccessStatusCode)
            {
                var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
                logger.LogWarning("Story URL GET failed ({Code}): {Body}", (int)response.StatusCode,
                    body.Length > 200 ? body[..200] + "…" : body);
                throw new InvalidOperationException(
                    $"Failed to fetch URL (HTTP {(int)response.StatusCode}).");
            }

            var filename = PickFileName(current, response);
            var payload = await ReadBodyCappedAsync(response.Content, cancellationToken).ConfigureAwait(false);

            logger.LogInformation("Story URL fetched: {Filename}, {ByteCount} bytes", filename,
                payload.Length);

            string? htmlTitle = null;
            if (LooksHtmlLike(payload, filename))
            {
                payload.Position = 0;
                htmlTitle =
                    await HtmlDocumentTitlePeek.TryPeekTitleAsync(payload, cancellationToken)
                        .ConfigureAwait(false);
                payload.Position = 0;
            }

            return new RemoteStoryFetchResult(payload, filename, htmlTitle);
        }
    }

    private static bool LooksHtmlLike(MemoryStream payload, string fileName)
    {
        if (fileName.EndsWith(".html", StringComparison.OrdinalIgnoreCase) ||
            fileName.EndsWith(".htm", StringComparison.OrdinalIgnoreCase) ||
            fileName.EndsWith(".xhtml", StringComparison.OrdinalIgnoreCase))
            return true;

        payload.Position = 0;
        Span<byte> head = stackalloc byte[256];
        var n = payload.Read(head);
        payload.Position = 0;
        if (n == 0)
            return false;

        var span = head[..n];
        if (span.Length >= 3 && span[0] == 0xEF && span[1] == 0xBB && span[2] == 0xBF)
            span = span[3..];

        try
        {
            var prefix = Encoding.UTF8.GetString(span).TrimStart();
            return prefix.StartsWith("<!", StringComparison.OrdinalIgnoreCase) ||
                   prefix.StartsWith("<html", StringComparison.OrdinalIgnoreCase) ||
                   prefix.StartsWith("<head", StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    private static bool IsRedirect(HttpStatusCode code) =>
        code is HttpStatusCode.MovedPermanently or HttpStatusCode.Redirect or HttpStatusCode.SeeOther
            or HttpStatusCode.TemporaryRedirect or HttpStatusCode.PermanentRedirect;

    /// <remarks>Prefer Content-Disposition, then last URI path segment.</remarks>
    private static string PickFileName(Uri resolvedUri, HttpResponseMessage response)
    {
        var cd = response.Content.Headers.ContentDisposition;
        string? cand = null;
        if (cd is not null)
        {
            if (!string.IsNullOrWhiteSpace(cd.FileNameStar))
                cand = NormalizeStar(cd.FileNameStar.Trim());
            cand ??= !string.IsNullOrWhiteSpace(cd.FileName) ? NormalizeQuotes(cd.FileName.Trim()) : null;
        }

        var name =
            (!string.IsNullOrWhiteSpace(cand) ? Path.GetFileName(cand.AsSpan()).ToString().Trim() : null);
        return !string.IsNullOrWhiteSpace(name) ? name : FallbackFromUriPath(resolvedUri);
    }

    private static string NormalizeStar(string starred)
    {
        var value = starred;
        var lastQuote = starred.LastIndexOf('\'');
        var firstQuote = starred.IndexOf('\'');
        if (firstQuote >= 0 && lastQuote > firstQuote)
            value = starred[(lastQuote + 1)..];

        try
        {
            return Uri.UnescapeDataString(value);
        }
        catch
        {
            return starred;
        }
    }

    private static string NormalizeQuotes(string fileName)
    {
        fileName = fileName.Trim().Trim(';');
        if (fileName.Length >= 2 && fileName.StartsWith("\"", StringComparison.Ordinal) &&
            fileName.EndsWith("\"", StringComparison.Ordinal))
            return fileName[1..^1];
        return fileName.Trim();
    }

    private static string FallbackFromUriPath(Uri u)
    {
        var trimmed = Uri.UnescapeDataString(u.AbsolutePath.TrimEnd('/'));
        var seg = trimmed.Split('/');
        var last = seg.Length > 0 ? seg[^1] : trimmed;
        if (string.IsNullOrWhiteSpace(last))
            last = "imported-story.html";
        if (!Path.HasExtension(last))
            last += ".html";
        return last;
    }

    private static async Task<MemoryStream> ReadBodyCappedAsync(HttpContent content,
        CancellationToken cancellationToken)
    {
        await using var network = await content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        var ms = new MemoryStream(capacity: 1024 * 512);
        var buffer = new byte[8192];
        long total = 0;
        while (true)
        {
            var read =
                await network.ReadAsync(buffer.AsMemory(0, buffer.Length), cancellationToken)
                    .ConfigureAwait(false);
            if (read == 0)
                break;
            checked
            {
                total += read;
            }

            if (total > ImportLimits.MaxUrlDownloadBytes)
                throw new InvalidOperationException(
                    $"Remote content exceeds {ImportLimits.MaxUrlDownloadBytes} bytes.");

            await ms.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
        }

        ms.Position = 0;
        return ms;
    }
}

internal static class UriNormalizeMinimalExtensions
{
    /// <summary>Normalize empty URI path edge cases.</summary>
    internal static Uri NormalizeMinimal(this Uri url) =>
        url.AbsolutePath.Length == 0 ? new UriBuilder(url) { Path = "/" }.Uri : url;
}
