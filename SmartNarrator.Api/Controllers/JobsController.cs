using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using SmartNarrator.Api.Contracts;
using SmartNarrator.Api.Mapping;
using SmartNarrator.Application.Ports;
using SmartNarrator.Domain.Enums;
using SmartNarrator.Infrastructure.Ai;
using SmartNarrator.Infrastructure.Options;
using SmartNarrator.Infrastructure.Persistence;
using System.Text.Json;

namespace SmartNarrator.Api.Controllers;

[ApiController]
[Route("jobs")]
public sealed class JobsController(
    SmartNarratorDbContext db,
    IJobRealtimeNotifier notifier,
    IHostEnvironment hostEnvironment,
    IOptions<StorageOptions> storageOptions,
    IOptions<OllamaOptions> ollamaOptions) : ControllerBase
{
    /// <summary>Recent jobs for dashboard (newest first).</summary>
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<JobDto>>> ListRecent([FromQuery] int take = 150,
        CancellationToken cancellationToken = default)
    {
        take = Math.Clamp(take, 1, 500);
        var rows = await db.BackgroundJobs.AsNoTracking()
            .OrderByDescending(j => j.CreatedUtc)
            .Take(take)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return rows.ConvertAll(JobDtoMapping.FromEntity);
    }

    [HttpGet("{jobId:guid}")]
    public async Task<ActionResult<JobDto>> Get(Guid jobId, CancellationToken cancellationToken)
    {
        var j = await db.BackgroundJobs.AsNoTracking().SingleOrDefaultAsync(x => x.Id == jobId, cancellationToken)
            .ConfigureAwait(false);
        if (j is null)
            return NotFound();

        return JobDtoMapping.FromEntity(j);
    }

    /// <summary>
    /// Structured LLM prompts and assistant payloads captured for this job (<c>Ollama:CaptureAnalyzeLlmTurns</c>).
    /// Returns NDJSON lines parsed into a JSON array; empty or missing capture yields 404.
    /// </summary>
    [HttpGet("{jobId:guid}/llm-diagnostics")]
    public async Task<ActionResult<LlmDiagnosticsResponseDto>> GetLlmDiagnostics(Guid jobId,
        CancellationToken cancellationToken)
    {
        var path =
            AnalyzeLlmDiagnosticsPaths.TurnsFilePhysical(hostEnvironment, storageOptions.Value, jobId);

        if (!System.IO.File.Exists(path))
        {
            var rel = Path.GetRelativePath(hostEnvironment.ContentRootPath, Path.GetFullPath(path));
            var captureNow = ollamaOptions.Value.CaptureAnalyzeLlmTurns;
            return NotFound(new LlmDiagnosticsNotFoundDto(
                JobId: jobId.ToString("D"),
                Detail:
                "There is no llm-diagnostics capture for this job id. Diagnostics are optional and only appear when CaptureAnalyzeLlmTurns was enabled during the job that called the LLM.",
                CaptureAnalyzeLlmTurnsInThisApiConfiguration: captureNow,
                ExpectedRelativePathUnderApiContentRoot: rel,
                Hints:
                [
                    "Set Ollama:CaptureAnalyzeLlmTurns to true on the machine that EXECUTES ingest/analyze (API when Jobs:DispatchMode is InProcess, or the Worker process when DispatchMode is RabbitMq), redeploy/restart if needed.",
                    "Re-run ingest or analyze after enabling capture — existing jobs finished before enabling will never have turns.ndjson.",
                    "Analyze and Ingest shifts write under {Storage:RelativeRoot}/llm-diagnostics/<jobId>/turns.ndjson on that host. With RabbitMQ locally, the Worker and API ContentRoot/App_Data folders are often different unless you mount the same path — use Docker Compose’s shared volume, or symlink/point both apps at identical Storage:RelativeRoot.",
                    "Speech render jobs never write LLM conversation captures; use this link only for Analyze or Ingest jobs when capture is enabled.",
                ]));
        }

        const long maxFileBytes = 20 * 1024 * 1024;
        var info = new FileInfo(path);
        if (info.Length > maxFileBytes)
            return StatusCode(StatusCodes.Status413PayloadTooLarge,
                new LlmDiagTooLargeDto(
                    Detail: "LLM diagnostics file exceeds the 20 MiB download cap — open turns.ndjson on disk."));

        var turns = new List<JsonElement>();
        await foreach (var line in System.IO.File.ReadLinesAsync(path, cancellationToken).ConfigureAwait(false))
        {
            if (string.IsNullOrWhiteSpace(line))
                continue;
            try
            {
                var el = JsonSerializer.Deserialize<JsonElement>(line);
                turns.Add(el);
            }
            catch (JsonException)
            {
                var wrapped = JsonSerializer.Serialize(new Dictionary<string, string>
                {
                    ["parseError"] = "Line is not valid JSON; stored raw in rawLine.",
                    ["rawLine"] = line.Length > 20_000 ? line[..20000] + "… [truncated]" : line,
                });
                turns.Add(JsonSerializer.Deserialize<JsonElement>(wrapped));
            }
        }

        var rowExists =
            await db.BackgroundJobs.AsNoTracking().AnyAsync(x => x.Id == jobId, cancellationToken).ConfigureAwait(false);

        var dto = new LlmDiagnosticsResponseDto(
            JobId: jobId.ToString("D"),
            RelativePathUnderContentRoot: Path.GetRelativePath(hostEnvironment.ContentRootPath,
                Path.GetFullPath(path)),
            JobRowStillPresent: rowExists,
            Turns: turns);

        return Ok(dto);
    }

    public sealed record LlmDiagnosticsResponseDto(
        string JobId,
        string RelativePathUnderContentRoot,
        bool JobRowStillPresent,
        IReadOnlyList<JsonElement> Turns);

    private sealed record LlmDiagTooLargeDto(string Detail);

    /// <remarks>Returned as JSON body when <c>turns.ndjson</c> does not exist (404).</remarks>
    public sealed record LlmDiagnosticsNotFoundDto(
        string JobId,
        string Detail,
        bool CaptureAnalyzeLlmTurnsInThisApiConfiguration,
        string ExpectedRelativePathUnderApiContentRoot,
        IReadOnlyList<string> Hints);

    /// <summary>Cancels a queued job immediately, or requests cooperative cancellation while running.</summary>
    [HttpPost("{jobId:guid}/cancel")]
    public async Task<IActionResult> Cancel(Guid jobId, CancellationToken cancellationToken)
    {
        var terminal = new[]
        {
            BackgroundJobStatus.Succeeded,
            BackgroundJobStatus.Failed,
            BackgroundJobStatus.Cancelled,
        };

        var cancelledPending = await db.BackgroundJobs
            .Where(j => j.Id == jobId && j.Status == BackgroundJobStatus.Pending)
            .ExecuteUpdateAsync(setters => setters
                    .SetProperty(j => j.Status, BackgroundJobStatus.Cancelled)
                    .SetProperty(j => j.CompletedUtc, DateTimeOffset.UtcNow)
                    .SetProperty(j => j.ProgressPhase, "Cancelled before start.")
                    .SetProperty(j => j.CancellationRequested, false)
                    .SetProperty(j => j.UpdatedUtc, DateTimeOffset.UtcNow),
                cancellationToken)
            .ConfigureAwait(false);

        if (cancelledPending > 0)
        {
            await notifier.NotifyJobUpdatedAsync(jobId, cancellationToken).ConfigureAwait(false);
            return NoContent();
        }

        var job = await db.BackgroundJobs.SingleOrDefaultAsync(j => j.Id == jobId, cancellationToken)
            .ConfigureAwait(false);

        if (job is null)
            return NotFound();

        if (terminal.Contains(job.Status))
            return Conflict("Job already finished.");

        if (job.Status != BackgroundJobStatus.Running)
            return Conflict($"Cannot cancel job in status {job.Status}.");

        job.CancellationRequested = true;
        job.ProgressPhase ??= "Cancellation requested…";
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        await notifier.NotifyJobUpdatedAsync(jobId, cancellationToken).ConfigureAwait(false);

        return NoContent();
    }

    /// <summary>
    /// Deletes a job row (terminal or pending cleanup). Running jobs require <paramref name="force"/> for stuck zombies.
    /// </summary>
    /// <remarks>
    /// Rows are removed only when not concurrently <see cref="BackgroundJobStatus.Running"/> (unless <paramref name="force"/>).
    /// Any RabbitMQ message still in flight for that job id is acknowledged without work: execution requires a Pending row.
    /// </remarks>
    [HttpDelete("{jobId:guid}")]
    public async Task<IActionResult> Delete(Guid jobId, [FromQuery] bool force = false,
        CancellationToken cancellationToken = default)
    {
        var snap = await db.BackgroundJobs.AsNoTracking()
            .Select(j => new { j.Id, j.Status })
            .SingleOrDefaultAsync(j => j.Id == jobId, cancellationToken).ConfigureAwait(false);
        if (snap is null)
            return NotFound();

        if (snap.Status == BackgroundJobStatus.Running && !force)
        {
            return Conflict(
                "Job is still marked Running — cancel it first, or delete with ?force=true only if the worker is dead (zombie).");
        }

        var deleted = await db.BackgroundJobs
            .Where(j => j.Id == jobId && (force || j.Status != BackgroundJobStatus.Running))
            .ExecuteDeleteAsync(cancellationToken).ConfigureAwait(false);

        if (deleted == 0)
        {
            var stillThere =
                await db.BackgroundJobs.AsNoTracking().AnyAsync(j => j.Id == jobId, cancellationToken)
                    .ConfigureAwait(false);
            if (!stillThere)
                return NotFound();

            return Conflict(
                "Could not delete this job — it may have started running. Refresh the list; cancel a running job before removing it, or use ?force=true only for zombies.");
        }

        await notifier.NotifyJobRemovedAsync(jobId, cancellationToken).ConfigureAwait(false);

        return NoContent();
    }
}
