using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SmartNarrator.Application.Ports;
using SmartNarrator.Infrastructure.Persistence;

namespace SmartNarrator.Api.Controllers;

[ApiController]
[Route("works/{workId:guid}")]
public sealed class WorkPipelineController(
    SmartNarratorDbContext db,
    IWorkAnalysisCoordinator analysisCoordinator,
    ISpeechRenderingCoordinator renderCoordinator) : ControllerBase
{
    [HttpPost("analyze")]
    public async Task<ActionResult<object>> Analyze(Guid workId, CancellationToken cancellationToken)
    {
        if (!await db.Works.AnyAsync(w => w.Id == workId, cancellationToken).ConfigureAwait(false))
            return NotFound();

        try
        {
            var jobId = await analysisCoordinator.EnqueueAnalyzeAsync(workId, cancellationToken).ConfigureAwait(false);
            return Accepted(new { jobId });
        }
        catch (Exception ex)
        {
            return BadRequest(ex.Message);
        }
    }

    [HttpPost("render")]
    public async Task<ActionResult<object>> Render(Guid workId, CancellationToken cancellationToken)
    {
        if (!await db.Works.AnyAsync(w => w.Id == workId, cancellationToken).ConfigureAwait(false))
            return NotFound();

        try
        {
            var jobId = await renderCoordinator.EnqueueRenderAsync(workId, cancellationToken).ConfigureAwait(false);
            return Accepted(new { jobId });
        }
        catch (Exception ex)
        {
            return BadRequest(ex.Message);
        }
    }
}
