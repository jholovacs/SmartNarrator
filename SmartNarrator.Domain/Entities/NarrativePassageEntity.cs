namespace SmartNarrator.Domain.Entities;

/// <summary>Non-dialogue narration block with narrator voice guidance.</summary>
public class NarrativePassageEntity
{
    public Guid Id { get; set; }
    public Guid WorkId { get; set; }
    public int StartOffset { get; set; }
    public int EndOffset { get; set; }
    /// <summary>When first-person or limited narrator matches a named character voice.</summary>
    public Guid? NarratorCharacterId { get; set; }
    public string PerspectiveNotes { get; set; } = string.Empty;

    public string GenderPresentation { get; set; } = "unspecified";
    public string Tone { get; set; } = "neutral";
    public string Accent { get; set; } = "none";
    public string Breathiness { get; set; } = "normal";
    public string SpeakingPace { get; set; } = "normal";

    public bool IsAiSuggested { get; set; } = true;

    public WorkEntity? Work { get; set; }
    public CharacterProfileEntity? NarratorCharacter { get; set; }
}
