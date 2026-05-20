using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using SmartNarrator.Domain.Enums;
using SmartNarrator.Infrastructure.Persistence;

namespace SmartNarrator.Infrastructure.Jobs;

public sealed class BackgroundJobWorker(IServiceScopeFactory scopeFactory, ILogger<BackgroundJobWorker> logger)
    : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("Background job worker started (in-process polling).");
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await ProcessNextAsync(stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Background worker iteration failed.");
            }

            await Task.Delay(TimeSpan.FromSeconds(2), stoppingToken).ConfigureAwait(false);
        }
    }

    private async Task ProcessNextAsync(CancellationToken hostStoppingToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SmartNarratorDbContext>();

        var pending = await db.BackgroundJobs.Where(j => j.Status == BackgroundJobStatus.Pending).OrderBy(j => j.CreatedUtc)
            .Select(j => new { j.Id }).FirstOrDefaultAsync(hostStoppingToken).ConfigureAwait(false);

        if (pending is null)
            return;

        await BackgroundJobRunner.TryClaimAndExecuteAsync(scope.ServiceProvider, pending.Id, hostStoppingToken, logger)
            .ConfigureAwait(false);
    }
}
