using SmartNarrator.Application.Ports;
using SmartNarrator.Domain.Enums;

namespace SmartNarrator.Infrastructure.Jobs;

internal sealed class NullJobQueuePublisher : IJobQueuePublisher
{
    public static readonly NullJobQueuePublisher Instance = new();

    private NullJobQueuePublisher()
    {
    }

    public Task PublishPendingJobAsync(Guid jobId, BackgroundJobType type, CancellationToken cancellationToken = default) =>
        Task.CompletedTask;
}
