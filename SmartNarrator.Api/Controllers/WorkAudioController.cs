using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using SmartNarrator.Api.Contracts;
using SmartNarrator.Infrastructure.Options;
using SmartNarrator.Infrastructure.Persistence;

namespace SmartNarrator.Api.Controllers;

[ApiController]
[Route("works/{workId:guid}/audio")]
public sealed class WorkAudioController(
    SmartNarratorDbContext db,
    IHostEnvironment hostEnvironment,
    IOptions<StorageOptions> storageOptions) : ControllerBase
{
    [HttpGet("artifacts")]
    public async Task<ActionResult<List<AudioArtifactDto>>> ListArtifacts(Guid workId,
        CancellationToken cancellationToken)
    {
        if (!await db.Works.AnyAsync(w => w.Id == workId, cancellationToken).ConfigureAwait(false))
            return NotFound();

        return await db.AudioArtifacts.Where(a => a.WorkId == workId).OrderBy(a => a.StartOffset)
            .Select(a => new AudioArtifactDto(a.Id, a.RelativePath, a.StartOffset, a.EndOffset, a.UtteranceId))
            .ToListAsync(cancellationToken).ConfigureAwait(false);
    }

    [HttpGet("artifacts/{artifactId:guid}/file")]
    public async Task<IActionResult> Download(Guid workId, Guid artifactId, CancellationToken cancellationToken)
    {
        var artifact = await db.AudioArtifacts.AsNoTracking()
            .SingleOrDefaultAsync(a => a.Id == artifactId && a.WorkId == workId, cancellationToken).ConfigureAwait(false);
        if (artifact is null)
            return NotFound();

        var root = Path.GetFullPath(Path.Combine(hostEnvironment.ContentRootPath, storageOptions.Value.RelativeRoot));
        var absolute = Path.GetFullPath(Path.Combine(root, artifact.RelativePath.Replace('/', Path.DirectorySeparatorChar)));
        if (!absolute.StartsWith(root, StringComparison.OrdinalIgnoreCase))
            return BadRequest("Invalid path.");

        if (!System.IO.File.Exists(absolute))
            return NotFound("Audio file missing on disk.");

        var stream = System.IO.File.OpenRead(absolute);
        return File(stream, artifact.MimeType, Path.GetFileName(absolute), enableRangeProcessing: true);
    }
}
