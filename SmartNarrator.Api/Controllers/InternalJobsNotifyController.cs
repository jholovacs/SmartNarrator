using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using SmartNarrator.Application.Ports;
using SmartNarrator.Infrastructure.Options;

namespace SmartNarrator.Api.Controllers;

/// <summary>Worker containers POST here so SignalR pushes reach SPA subscribers.</summary>
[ApiController]
[Route("internal/jobs")]
public sealed class InternalJobsNotifyController(
    IJobRealtimeNotifier notifier,
    IOptions<JobsOptions> jobsOptions,
    ILogger<InternalJobsNotifyController> logger) : ControllerBase
{
    [HttpPost("notify")]
    public async Task<IActionResult> Notify(
        [FromHeader(Name = "X-Internal-Key")] string? apiKey,
        [FromBody] NotifyJobBody body,
        CancellationToken cancellationToken)
    {
        var expected = jobsOptions.Value.InternalNotifyApiKey;
        if (string.IsNullOrWhiteSpace(expected) || !string.Equals(apiKey, expected, StringComparison.Ordinal))
        {
            logger.LogWarning("Rejected internal job notify (missing or invalid API key).");
            return Unauthorized();
        }

        await notifier.NotifyJobUpdatedAsync(body.JobId, cancellationToken).ConfigureAwait(false);
        return NoContent();
    }

    public sealed record NotifyJobBody(Guid JobId);
}
