using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SmartNarrator.Application.Ports;
using SmartNarrator.Infrastructure.Persistence;

namespace SmartNarrator.Api.Controllers;

[ApiController]
[Route("works/{workId:guid}/profiles")]
public sealed class WorkProfilesController(
    SmartNarratorDbContext db,
    IProfileImportExportService profiles) : ControllerBase
{
    [HttpGet("export")]
    public async Task<IActionResult> Export(Guid workId, CancellationToken cancellationToken)
    {
        if (!await db.Works.AnyAsync(w => w.Id == workId, cancellationToken).ConfigureAwait(false))
            return NotFound();

        var bytes = await profiles.ExportJsonAsync(workId, cancellationToken).ConfigureAwait(false);
        return File(bytes, "application/json", $"smartnarrator-profiles-{workId:N}.json");
    }

    [HttpPost("import")]
    [DisableRequestSizeLimit]
    public async Task<IActionResult> Import(Guid workId, IFormFile? file, CancellationToken cancellationToken)
    {
        if (file is null || file.Length == 0)
            return BadRequest("Attach JSON bundle.");

        if (!await db.Works.AnyAsync(w => w.Id == workId, cancellationToken).ConfigureAwait(false))
            return NotFound();

        await using var stream = file.OpenReadStream();
        await profiles.ImportIntoWorkAsync(workId, stream, cancellationToken).ConfigureAwait(false);
        return NoContent();
    }
}
