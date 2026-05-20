namespace SmartNarrator.Domain.Entities;

/// <summary>
/// Logical chapter partition over canonical UTF-16 offsets (markers + segment bounds).
/// AI analyze merges rows by exact <see cref="StartOffset"/>/<see cref="EndOffset"/> for <see cref="IsAiSuggested"/>
/// chapters so stable IDs survive repeat runs; orphans are removed.
/// </summary>
public class WorkChapterEntity
{
    public Guid Id { get; set; }
    public Guid WorkId { get; set; }

    /// <summary>Reading order (0-based).</summary>
    public int OrderIndex { get; set; }

    /// <summary>UTF-16 inclusive start of chapter segment (usually heading or body).</summary>
    public int StartOffset { get; set; }

    /// <summary>UTF-16 exclusive end of chapter segment.</summary>
    public int EndOffset { get; set; }

    /// <summary>Optional UTF-16 heading marker span inside <see cref="StartOffset"/>…<see cref="EndOffset"/>.</summary>
    public int? HeadingStartOffset { get; set; }

    public int? HeadingEndOffset { get; set; }

    public string? Title { get; set; }

    public string Notes { get; set; } = string.Empty;

    public bool IsAiSuggested { get; set; } = true;

    public WorkEntity? Work { get; set; }

    public ICollection<DialogueSpanEntity> DialogueSpans { get; set; } = new List<DialogueSpanEntity>();
}
