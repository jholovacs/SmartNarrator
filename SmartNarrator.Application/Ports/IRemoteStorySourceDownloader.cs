namespace SmartNarrator.Application.Ports;

/// <summary>
/// Downloads public HTTP(S) prose sources with basic SSRF mitigations for URL import.
/// </summary>
public interface IRemoteStorySourceDownloader
{
    /// <exception cref="InvalidOperationException">
    /// Unsafe URL / redirect, DNS resolves to prohibited addresses, or HTTP failure.
    /// </exception>
    Task<RemoteStoryFetchResult> FetchAsync(Uri url, CancellationToken cancellationToken = default);
}

/// <summary>
/// Holds response bytes in-memory; callers must dispose.
/// </summary>
public sealed class RemoteStoryFetchResult : IDisposable
{
    public RemoteStoryFetchResult(MemoryStream stream, string suggestedOriginalFileName,
        string? htmlDocumentTitle = null)
    {
        Content = stream;
        SuggestedOriginalFileName = suggestedOriginalFileName;
        HtmlDocumentTitle = htmlDocumentTitle;
    }

    public MemoryStream Content { get; }
    public string SuggestedOriginalFileName { get; }
    /// <summary>Best-effort &lt;title&gt; when the payload looks like HTML.</summary>
    public string? HtmlDocumentTitle { get; }

    public void Dispose() => Content.Dispose();
}
