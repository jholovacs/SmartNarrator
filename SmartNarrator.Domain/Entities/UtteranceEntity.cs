using SmartNarrator.Domain.Enums;

namespace SmartNarrator.Domain.Entities;

public class UtteranceEntity
{
    public Guid Id { get; set; }
    public Guid WorkId { get; set; }
    public int StartOffset { get; set; }
    public int EndOffset { get; set; }
    public SpeakerKind SpeakerKind { get; set; }
    public Guid? CharacterId { get; set; }

    /// <summary>Model confidence 0–1 when produced by AI.</summary>
    public double Confidence { get; set; }

    /// <summary>True when the speaker must be confirmed or assigned in the UI.</summary>
    public bool SpeakerNeedsReview { get; set; }

    public bool IsAiSuggested { get; set; } = true;
    public bool UserApproved { get; set; }

    public WorkEntity? Work { get; set; }
    public CharacterProfileEntity? Character { get; set; }
}
