using System.Net.Http.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SmartNarrator.Application.Ports;
using SmartNarrator.Infrastructure.Options;

namespace SmartNarrator.Infrastructure.Services;

/// <summary>
/// Calls this API's POST /internal/jobs/notify so SignalR pushes propagate from worker containers.
/// </summary>
public sealed class HttpJobRealtimeNotifier(HttpClient http, IOptions<JobsOptions> opts, ILogger<HttpJobRealtimeNotifier> logger)
    : IJobRealtimeNotifier
{
    public async Task NotifyJobUpdatedAsync(Guid jobId, CancellationToken cancellationToken = default)
    {
        var key = opts.Value.InternalNotifyApiKey;
        if (string.IsNullOrWhiteSpace(key))
        {
            logger.LogWarning("Jobs:InternalNotifyApiKey empty; cannot notify UI for job {JobId}.", jobId);
            return;
        }

        using var req = new HttpRequestMessage(HttpMethod.Post, "internal/jobs/notify")
        {
            Content = JsonContent.Create(new { jobId }),
        };
        req.Headers.TryAddWithoutValidation("X-Internal-Key", key);

        try
        {
            var resp = await http.SendAsync(req, cancellationToken).ConfigureAwait(false);
            resp.EnsureSuccessStatusCode();
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Notify API failed for job {JobId}.", jobId);
        }
    }
}
