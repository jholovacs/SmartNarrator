using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using SmartNarrator.Api.Hubs;
using SmartNarrator.Api.Mapping;
using SmartNarrator.Application.Ports;
using SmartNarrator.Infrastructure.Persistence;

namespace SmartNarrator.Api.Services;

public sealed class JobRealtimeNotifier(
    IHubContext<JobsHub> hub,
    IServiceScopeFactory scopeFactory,
    ILogger<JobRealtimeNotifier> logger) : IJobRealtimeNotifier
{
    public async Task NotifyJobUpdatedAsync(Guid jobId, CancellationToken cancellationToken = default)
    {
        try
        {
            await using var scope = scopeFactory.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<SmartNarratorDbContext>();
            var entity = await db.BackgroundJobs.AsNoTracking()
                .SingleOrDefaultAsync(j => j.Id == jobId, cancellationToken).ConfigureAwait(false);
            if (entity is null)
                return;

            var dto = JobDtoMapping.FromEntity(entity);
            await hub.Clients.Group(JobsHub.GroupName(jobId))
                .SendAsync(JobsHub.JobUpdateEvent, dto, cancellationToken).ConfigureAwait(false);
            await hub.Clients.Group(JobsHub.RecentJobsGroupName)
                .SendAsync(JobsHub.JobUpdateEvent, dto, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to push job {JobId} update over SignalR.", jobId);
        }
    }

    public async Task NotifyJobRemovedAsync(Guid jobId, CancellationToken cancellationToken = default)
    {
        try
        {
            var payload = new { id = jobId };
            await hub.Clients.Group(JobsHub.GroupName(jobId))
                .SendAsync(JobsHub.JobRemovedEvent, payload, cancellationToken).ConfigureAwait(false);
            await hub.Clients.Group(JobsHub.RecentJobsGroupName)
                .SendAsync(JobsHub.JobRemovedEvent, payload, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to push job {JobId} removal over SignalR.", jobId);
        }
    }
}
