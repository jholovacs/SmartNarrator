using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using SmartNarrator.Application.Ports;
using SmartNarrator.Domain.Entities;
using SmartNarrator.Domain.Enums;
using SmartNarrator.Infrastructure.Persistence;

namespace SmartNarrator.Infrastructure.Jobs;

/// <summary>
/// Claims a single pending job row (when possible) and runs <see cref="JobExecutors"/>.
/// Shared by in-process polling and RabbitMQ consumers.
/// </summary>
internal static class BackgroundJobRunner
{
    internal static async Task<bool> TryClaimAndExecuteAsync(IServiceProvider scopedServices, Guid jobId,
        CancellationToken hostStoppingToken, ILogger logger)
    {
        var db = scopedServices.GetRequiredService<SmartNarratorDbContext>();
        var notifier = scopedServices.GetRequiredService<IJobRealtimeNotifier>();
        var scopeFactory = scopedServices.GetRequiredService<IServiceScopeFactory>();

        var claimed = await db.BackgroundJobs
            .Where(j => j.Id == jobId && j.Status == BackgroundJobStatus.Pending)
            .ExecuteUpdateAsync(setters => setters
                    .SetProperty(j => j.Status, BackgroundJobStatus.Running)
                    .SetProperty(j => j.ErrorMessage, _ => null)
                    .SetProperty(j => j.ProgressPhase, _ => null)
                    .SetProperty(j => j.UpdatedUtc, _ => DateTimeOffset.UtcNow),
                hostStoppingToken)
            .ConfigureAwait(false);

        if (claimed == 0)
        {
            logger.LogDebug("Skipped job {JobId}: not pending (already handled or cancelled).", jobId);
            return false;
        }

        var jobLock = await db.BackgroundJobs.Include(j => j.Work)
            .FirstAsync(j => j.Id == jobId, hostStoppingToken).ConfigureAwait(false);

        if (jobLock.StartedUtc is null)
        {
            jobLock.StartedUtc = DateTimeOffset.UtcNow;
            await db.SaveChangesAsync(hostStoppingToken).ConfigureAwait(false);
        }

        await notifier.NotifyJobUpdatedAsync(jobLock.Id, hostStoppingToken).ConfigureAwait(false);

        using var jobRunCts = CancellationTokenSource.CreateLinkedTokenSource(hostStoppingToken);
        using var pollDelayCts = CancellationTokenSource.CreateLinkedTokenSource(hostStoppingToken, jobRunCts.Token);

        var pollingTask = PollCancellationRequestedAsync(jobLock.Id, jobRunCts, pollDelayCts.Token, scopeFactory, logger);

        var persistJobOutcome = true;
        try
        {
            await JobExecutors.RunAsync(scopedServices, jobLock, jobRunCts.Token).ConfigureAwait(false);

            jobLock.Status = BackgroundJobStatus.Succeeded;
            jobLock.ProgressPercent = 100;
            jobLock.CompletedUtc = DateTimeOffset.UtcNow;
            jobLock.CancellationRequested = false;
            jobLock.ProgressPhase = null;
        }
        catch (OperationCanceledException)
        {
            var hostStopping = hostStoppingToken.IsCancellationRequested;

            var existsNow = await db.BackgroundJobs.AsNoTracking()
                .AnyAsync(j => j.Id == jobLock.Id, hostStoppingToken)
                .ConfigureAwait(false);

            // Matches GET /jobs/{id}: row gone ⇒ deleted while running — nothing left to persist.
            if (!hostStopping && !existsNow)
            {
                logger.LogInformation(
                    "Job {JobId} stopped cooperatively — database row was deleted mid-run.", jobLock.Id);
                db.Entry(jobLock).State = EntityState.Detached;
                persistJobOutcome = false;
            }
            else
            {
                try
                {
                    await db.Entry(jobLock).ReloadAsync(hostStoppingToken).ConfigureAwait(false);
                }
                catch (Exception reloadEx)
                {
                    logger.LogDebug(reloadEx, "Could not reload job {JobId} after cancellation.", jobLock.Id);
                }

                if (!hostStopping && jobLock.CancellationRequested)
                {
                    jobLock.Status = BackgroundJobStatus.Cancelled;
                    jobLock.ErrorMessage = null;
                    jobLock.ProgressPhase ??= "Cancelled.";
                    jobLock.CompletedUtc = DateTimeOffset.UtcNow;
                    jobLock.CancellationRequested = false;
                }
                else
                {
                    jobLock.Status = BackgroundJobStatus.Failed;
                    jobLock.ErrorMessage = hostStopping
                        ? "Interrupted (service shutting down)."
                        : "Operation was canceled.";
                    jobLock.CompletedUtc = DateTimeOffset.UtcNow;
                    jobLock.CancellationRequested = false;
                }
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Job {JobId} failed.", jobLock.Id);
            jobLock.Status = BackgroundJobStatus.Failed;
            jobLock.ErrorMessage = ex.Message.Length > 8000 ? ex.Message[..8000] : ex.Message;
            jobLock.CompletedUtc = DateTimeOffset.UtcNow;
            jobLock.CancellationRequested = false;
        }
        finally
        {
            jobRunCts.Cancel();
            try
            {
                await pollingTask.ConfigureAwait(false);
            }
            catch (Exception pollEndEx)
            {
                logger.LogTrace(pollEndEx, "Polling task ended for job {JobId}.", jobLock.Id);
            }

            if (persistJobOutcome)
            {
                await db.SaveChangesAsync(hostStoppingToken).ConfigureAwait(false);
                await notifier.NotifyJobUpdatedAsync(jobLock.Id, hostStoppingToken).ConfigureAwait(false);
            }
        }

        return true;
    }

    private static async Task PollCancellationRequestedAsync(
        Guid jobId,
        CancellationTokenSource jobRunCts,
        CancellationToken pollDelayToken,
        IServiceScopeFactory scopeFactory,
        ILogger logger)
    {
        try
        {
            while (!pollDelayToken.IsCancellationRequested)
            {
                await Task.Delay(400, pollDelayToken).ConfigureAwait(false);

                await using var scope = scopeFactory.CreateAsyncScope();
                var db = scope.ServiceProvider.GetRequiredService<SmartNarratorDbContext>();

                var snap = await db.BackgroundJobs.AsNoTracking()
                    .Where(j => j.Id == jobId)
                    .Select(j => new { j.CancellationRequested })
                    .SingleOrDefaultAsync(pollDelayToken)
                    .ConfigureAwait(false);

                // Mirrors GET /jobs/{id}: missing row means deleted — stop cooperatively.
                if (snap is null)
                {
                    logger.LogInformation(
                        "Job {JobId} row no longer exists (deleted); cancelling worker run.", jobId);
                    jobRunCts.Cancel();
                    return;
                }

                if (snap.CancellationRequested)
                {
                    jobRunCts.Cancel();
                    return;
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Job finished or host stopped — normal.
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Cancellation poll failed for job {JobId}.", jobId);
        }
    }
}
