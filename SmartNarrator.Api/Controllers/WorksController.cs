using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using SmartNarrator.Api.Contracts;
using SmartNarrator.Api.Options;
using SmartNarrator.Application.Ports;
using SmartNarrator.Domain.Entities;
using SmartNarrator.Domain.Enums;
using SmartNarrator.Infrastructure.Ingest;
using SmartNarrator.Infrastructure.Persistence;

namespace SmartNarrator.Api.Controllers;

[ApiController]
[Route("works")]
public sealed class WorksController(
    SmartNarratorDbContext db,
    ILogger<WorksController> logger,
    IHostEnvironment environment,
    IOptions<ApiOptions> apiOptions) : ControllerBase
{
    [HttpPost]
    public async Task<ActionResult<WorkSummaryDto>> Create([FromBody] WorkCreateRequest request,
        CancellationToken cancellationToken)
    {
        var work = new WorkEntity
        {
            Id = Guid.NewGuid(),
            Title = request.Title.Trim(),
            Language = string.IsNullOrWhiteSpace(request.Language) ? "en" : request.Language.Trim(),
            CreatedUtc = DateTimeOffset.UtcNow,
        };

        db.Works.Add(work);
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return CreatedAtAction(nameof(GetSummary), new { id = work.Id }, MapSummaryCached(work, false));
    }

    /// <summary>Creates a work and queues ingestion from an uploaded file (disk pick in the SPA).</summary>
    [HttpPost("import")]
    [Consumes("multipart/form-data")]
    [DisableRequestSizeLimit]
    [RequestFormLimits(MultipartBodyLengthLimit = 104857600)]
    public async Task<IActionResult> ImportFromDisk(
        [FromServices] IStoryIngestionService ingestionService,
        [FromForm] SourceFormat format,
        IFormFile? file,
        [FromForm] string? title,
        [FromForm] string? language,
        CancellationToken cancellationToken)
    {
        if (file is null || file.Length == 0)
            return BadRequest(ApiClientErrorDto.Validation("Attach a non-empty file."));

        var originalFileName =
            string.IsNullOrWhiteSpace(file.FileName) ? "upload" : Path.GetFileName(file.FileName.Trim());

        try
        {
            string? htmlTitle = null;
            if (format == SourceFormat.Html || LooksHtmlExtension(originalFileName))
            {
                await using (var peekStream = file.OpenReadStream())
                    htmlTitle = await HtmlDocumentTitlePeek.TryPeekTitleAsync(peekStream, cancellationToken)
                        .ConfigureAwait(false);
            }

            await using var stream = file.OpenReadStream();
            var workTitle = ResolveImportedTitle(title, originalFileName, htmlTitle);
            var lang = NormalizeLanguage(language);
            var (workId, jobId) =
                await CreateWorkThenEnqueueIngestAsync(stream, format, originalFileName, workTitle, lang,
                    ingestionService, cancellationToken).ConfigureAwait(false);

            return Accepted(new ImportWorkQueuedResponse(workId, jobId));
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Import from disk failed.");
            return Failure(ex, "Import from disk failed", StatusCodes.Status500InternalServerError);
        }
    }

    /// <summary>
    /// Creates a work and queues ingestion after fetching HTTP(S) content on the server (basic SSRF protection).
    /// </summary>
    [HttpPost("import-from-url")]
    public async Task<IActionResult> ImportFromUrl(
        [FromServices] IRemoteStorySourceDownloader downloader,
        [FromServices] IStoryIngestionService ingestionService,
        [FromBody] WorkImportFromUrlRequest request,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Url) ||
            !Uri.TryCreate(request.Url.Trim(), UriKind.Absolute, out var uriParsed))
            return BadRequest(ApiClientErrorDto.Validation("Provide an absolute HTTP or HTTPS URL."));

        try
        {
            using var fetched =
                await downloader.FetchAsync(uriParsed, cancellationToken).ConfigureAwait(false);
            var resolvedFormat =
                request.Format ?? InferSourceFormatFromFileName(fetched.SuggestedOriginalFileName);
            var workTitle = ResolveImportedTitle(request.Title, fetched.SuggestedOriginalFileName,
                fetched.HtmlDocumentTitle);
            var lang = NormalizeLanguage(request.Language);
            var (workId, jobId) = await CreateWorkThenEnqueueIngestAsync(fetched.Content, resolvedFormat,
                fetched.SuggestedOriginalFileName, workTitle, lang, ingestionService, cancellationToken)
                .ConfigureAwait(false);

            return Accepted(new ImportWorkQueuedResponse(workId, jobId));
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Import from URL failed for {Uri}.", uriParsed.ToString());
            return Failure(ex, "Import from URL failed", StatusCodes.Status500InternalServerError);
        }
    }

    [HttpGet]
    public async Task<ActionResult<List<WorkSummaryDto>>> List(CancellationToken cancellationToken)
    {
        var rows = await db.Works.OrderByDescending(w => w.CreatedUtc).ToListAsync(cancellationToken).ConfigureAwait(false);

        var artifactWorks = await db.AudioArtifacts.AsNoTracking()
            .Select(a => a.WorkId).Distinct().ToListAsync(cancellationToken).ConfigureAwait(false);

        var set = artifactWorks.ToHashSet();

        return rows.Select(w => MapSummaryCached(w, set.Contains(w.Id))).ToList();
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<WorkSummaryDto>> GetSummary(Guid id, CancellationToken cancellationToken)
    {
        var w = await db.Works.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id, cancellationToken)
                .ConfigureAwait(false);
        if (w is null)
            return NotFound();

        var has = await db.AudioArtifacts.AsNoTracking().AnyAsync(a => a.WorkId == id, cancellationToken)
            .ConfigureAwait(false);

        return MapSummaryCached(w, has);
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken)
    {
        var work = await db.Works.SingleOrDefaultAsync(w => w.Id == id, cancellationToken).ConfigureAwait(false);
        if (work is null)
            return NotFound();

        db.Works.Remove(work);
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return NoContent();
    }

    [HttpGet("{id:guid}/detail")]
    public async Task<ActionResult<WorkDetailDto>> GetDetail(Guid id, CancellationToken cancellationToken)
    {
        var w = await db.Works.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id, cancellationToken)
                .ConfigureAwait(false);

        return w is null ? NotFound() : new WorkDetailDto(w.Id, w.Title, w.Language, w.CreatedUtc, w.CanonicalText);
    }

    [HttpPost("{id:guid}/ingest")]
    [DisableRequestSizeLimit]
    public async Task<IActionResult> Ingest(Guid id,
        [FromServices] SmartNarrator.Application.Ports.IStoryIngestionService ingestionService,
        [FromQuery] SourceFormat format,
        IFormFile? file,
        CancellationToken cancellationToken)
    {
        if (file is null || file.Length == 0)
            return BadRequest(ApiClientErrorDto.Validation("Attach a non-empty file."));

        await using var stream = file.OpenReadStream();
        try
        {
            var jobId = await ingestionService.EnqueueIngestAsync(id, stream, format, file.FileName, cancellationToken)
                .ConfigureAwait(false);
            return Accepted(new { jobId });
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Ingest enqueue failed.");
            return Failure(ex, "Ingest enqueue failed", StatusCodes.Status500InternalServerError);
        }
    }

    private async Task<(Guid WorkId, Guid JobId)> CreateWorkThenEnqueueIngestAsync(
        Stream payload,
        SourceFormat format,
        string originalFileName,
        string resolvedTitle,
        string language,
        IStoryIngestionService ingestionService,
        CancellationToken cancellationToken)
    {
        var workId = Guid.NewGuid();
        var work = new WorkEntity
        {
            Id = workId,
            Title = resolvedTitle,
            Language = language,
            CreatedUtc = DateTimeOffset.UtcNow,
        };
        db.Works.Add(work);
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        var jobId =
            await ingestionService.EnqueueIngestAsync(workId, payload, format, originalFileName, cancellationToken)
                .ConfigureAwait(false);

        return (workId, jobId);
    }

    private static string NormalizeLanguage(string? language) =>
        string.IsNullOrWhiteSpace(language) ? "en" : language.Trim();

    private static bool LooksHtmlExtension(string fileName)
    {
        var ext = Path.GetExtension(fileName).Trim('.').ToLowerInvariant();
        return ext is "html" or "htm" or "xhtml";
    }

    private static SourceFormat InferSourceFormatFromFileName(string fileName)
    {
        var ext = Path.GetExtension(fileName).Trim('.').ToLowerInvariant();
        return ext switch
        {
            "txt" or "text" => SourceFormat.PlainText,
            "md" or "markdown" or "mdown" or "mkd" => SourceFormat.Markdown,
            "html" or "htm" or "xhtml" => SourceFormat.Html,
            "pdf" => SourceFormat.Pdf,
            "epub" => SourceFormat.Epub,
            _ => SourceFormat.PlainText,
        };
    }

    private static string ResolveImportedTitle(string? requestedTitle, string referenceNameFromSource,
        string? htmlDocumentTitle = null)
    {
        var t = requestedTitle?.Trim() ?? "";
        if (t.Length > 0)
            return t;

        var ht = htmlDocumentTitle?.Trim() ?? "";
        if (ht.Length > 0)
            return ht;

        var baseName = Path.GetFileName(
            string.IsNullOrWhiteSpace(referenceNameFromSource) ? "story" : referenceNameFromSource.Trim());
        t = Path.GetFileNameWithoutExtension(baseName).Trim();
        return t.Length > 0 ? t : "Imported work";
    }

    private static WorkSummaryDto MapSummaryCached(WorkEntity w, bool artifacts) =>
        new(w.Id, w.Title, w.Language, w.CreatedUtc, w.CanonicalText.Length, artifacts);

    private bool ExposeTechnicalDetails =>
        environment.IsDevelopment() || apiOptions.Value.ExposeExceptionDetails;

    private IActionResult Failure(Exception ex, string title, int statusCode)
    {
        var dto = ApiClientErrorDto.From(ex, ExposeTechnicalDetails, title);
        return StatusCode(statusCode, dto);
    }
}
