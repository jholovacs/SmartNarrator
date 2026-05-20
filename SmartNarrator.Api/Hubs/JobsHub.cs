using Microsoft.AspNetCore.SignalR;

namespace SmartNarrator.Api.Hubs;

public sealed class JobsHub : Hub
{
    public const string JobUpdateEvent = "jobUpdate";
    public const string JobRemovedEvent = "jobRemoved";

    /// <summary>Clients subscribed via <see cref="WatchRecentJobs"/> receive every job row update (jobs dashboard).</summary>
    public const string RecentJobsGroupName = "recent-jobs";

    public static string GroupName(Guid jobId) => $"job-{jobId:N}";

    /// <summary>Subscribe the connection to live updates for a single job.</summary>
    public async Task WatchJob(string jobId)
    {
        if (!Guid.TryParse(jobId, out var id))
            throw new HubException("Invalid job id.");

        await Groups.AddToGroupAsync(Context.ConnectionId, GroupName(id)).ConfigureAwait(false);
    }

    /// <summary>Leave the job group (optional; groups are cleaned up on disconnect).</summary>
    public async Task UnwatchJob(string jobId)
    {
        if (!Guid.TryParse(jobId, out var id))
            return;

        await Groups.RemoveFromGroupAsync(Context.ConnectionId, GroupName(id)).ConfigureAwait(false);
    }

    /// <summary>Subscribe to updates for any job (progress/status), newest payloads merged client-side.</summary>
    public Task WatchRecentJobs() =>
        Groups.AddToGroupAsync(Context.ConnectionId, RecentJobsGroupName);

    public Task UnwatchRecentJobs() =>
        Groups.RemoveFromGroupAsync(Context.ConnectionId, RecentJobsGroupName);
}
