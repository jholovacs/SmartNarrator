using SmartNarrator.Domain.Enums;

namespace SmartNarrator.Domain.Entities;

/// <summary>Structural spans such as chapters or POV shifts (distinct from narrator voice passages).</summary>
public class StoryStructureSectionEntity
{
    public Guid Id { get; set; }
    public Guid WorkId { get; set; }
    public int StartOffset { get; set; }
    public int EndOffset { get; set; }
    public StoryStructureSectionKind Kind { get; set; }
    /// <summary>Optional detected heading or chapter title.</summary>
    public string? Title { get; set; }
    /// <summary>Short AI rationale (perspective character, scene context).</summary>
    public string Notes { get; set; } = string.Empty;
    public bool IsAiSuggested { get; set; } = true;

    public WorkEntity? Work { get; set; }
}
