using SmartNarrator.Domain.Enums;

namespace SmartNarrator.Application.Ports;

/// <summary>
/// Notify an external worker process that a background job row has been persisted as <see cref="BackgroundJobStatus.Pending"/>.
/// </summary>
public interface IJobQueuePublisher
{
    Task PublishPendingJobAsync(Guid jobId, BackgroundJobType type, CancellationToken cancellationToken = default);
}
