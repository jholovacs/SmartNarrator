using SmartNarrator.Domain.Enums;

namespace SmartNarrator.Domain.Entities;

/// <summary>
/// Dialogue or quoted-speech span detected inside a chapter (global canonical offsets).
/// Populated before speaker / character attribution; AI analyzes merge rows by <see cref="ChapterId"/>
/// + <see cref="StartOffset"/>/<see cref="EndOffset"/> so IDs stay stable when rerunning analyze.
/// </summary>
public class DialogueSpanEntity
{
    public Guid Id { get; set; }
    public Guid WorkId { get; set; }
    public Guid ChapterId { get; set; }

    /// <summary>Order within the chapter (0-based).</summary>
    public int OrderIndexInChapter { get; set; }

    /// <summary>UTF-16 inclusive start (global).</summary>
    public int StartOffset { get; set; }

    /// <summary>UTF-16 exclusive end (global).</summary>
    public int EndOffset { get; set; }

    /// <summary><see cref="SpeakerKind.Character"/> for spoken dialogue; <see cref="SpeakerKind.Narrator"/> rarely.</summary>
    public SpeakerKind SpeakerKind { get; set; }

    public double Confidence { get; set; }

    public bool IsAiSuggested { get; set; } = true;

    public WorkEntity? Work { get; set; }
    public WorkChapterEntity? Chapter { get; set; }
}
