namespace SmartNarrator.Application.Ports;

/// <summary>
/// Push current <see cref="Domain.Entities.BackgroundJobEntity"/> snapshot to subscribed clients (SignalR).
/// </summary>
public interface IJobRealtimeNotifier
{
    Task NotifyJobUpdatedAsync(Guid jobId, CancellationToken cancellationToken = default);

    /// <summary>Broadcast row removal (dashboard subscribers drop the id).</summary>
    Task NotifyJobRemovedAsync(Guid jobId, CancellationToken cancellationToken = default) =>
        Task.CompletedTask;
}
