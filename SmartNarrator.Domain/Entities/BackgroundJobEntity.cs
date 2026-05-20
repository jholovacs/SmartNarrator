using SmartNarrator.Domain.Enums;

namespace SmartNarrator.Domain.Entities;

public class BackgroundJobEntity
{
    public Guid Id { get; set; }
    public BackgroundJobType Type { get; set; }
    public BackgroundJobStatus Status { get; set; }
    /// <summary>0–100 for UI progress.</summary>
    public int ProgressPercent { get; set; }

    /// <summary>Human-readable step label while running (cleared when job completes).</summary>
    public string? ProgressPhase { get; set; }

    public Guid? WorkId { get; set; }
    /// <summary>Optional opaque JSON describing job invocation.</summary>
    public string? PayloadJson { get; set; }
    public string? ErrorMessage { get; set; }

    /// <summary>
    /// When <see cref="Status"/> is <see cref="BackgroundJobStatus.Running"/>, worker observes this flag and stops work cooperatively.
    /// Cleared when the job reaches a terminal status.
    /// </summary>
    public bool CancellationRequested { get; set; }

    public DateTimeOffset CreatedUtc { get; set; }
    /// <summary>Last progress/status mutation (worker bumps, cancellations, completions).</summary>
    public DateTimeOffset UpdatedUtc { get; set; }
    public DateTimeOffset? StartedUtc { get; set; }
    public DateTimeOffset? CompletedUtc { get; set; }

    public WorkEntity? Work { get; set; }
}
