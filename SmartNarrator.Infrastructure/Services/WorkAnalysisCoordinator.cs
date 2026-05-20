using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SmartNarrator.Application.Ports;
using SmartNarrator.Domain.Entities;
using SmartNarrator.Domain.Enums;
using SmartNarrator.Infrastructure.Persistence;

namespace SmartNarrator.Infrastructure.Services;

public sealed class WorkAnalysisCoordinator(
    SmartNarratorDbContext db,
    IJobQueuePublisher jobQueuePublisher,
    ILogger<WorkAnalysisCoordinator> logger) : IWorkAnalysisCoordinator
{
    public async Task<Guid> EnqueueAnalyzeAsync(Guid workId, CancellationToken cancellationToken)
    {
        var work = await db.Works.AsNoTracking()
            .FirstOrDefaultAsync(w => w.Id == workId, cancellationToken);

        if (work is null)
            throw new InvalidOperationException($"Work {workId} not found.");

        if (string.IsNullOrWhiteSpace(work.CanonicalText))
            throw new InvalidOperationException("No canonical prose available; run ingestion first.");

        var job = new BackgroundJobEntity
        {
            Id = Guid.NewGuid(),
            WorkId = workId,
            Type = BackgroundJobType.Analyze,
            Status = BackgroundJobStatus.Pending,
            ProgressPercent = 0,
            CreatedUtc = DateTimeOffset.UtcNow,
            PayloadJson = JsonSerializer.Serialize(new { workId }),
        };

        db.BackgroundJobs.Add(job);
        await db.SaveChangesAsync(cancellationToken);

        await jobQueuePublisher.PublishPendingJobAsync(job.Id, job.Type, cancellationToken).ConfigureAwait(false);

        logger.LogInformation("Queued analyze job {JobId} for work {WorkId}", job.Id, workId);
        return job.Id;
    }
}
