namespace SmartNarrator.Domain.Entities;

public class CharacterProfileEntity
{
    public Guid Id { get; set; }
    public Guid WorkId { get; set; }
    /// <summary>Stable slug from structured analysis linking utterances.</summary>
    public string? AiExternalKey { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? AliasesJson { get; set; }

    public string GenderPresentation { get; set; } = "unspecified";
    public string Tone { get; set; } = "neutral";
    public string Accent { get; set; } = "none";
    public string Breathiness { get; set; } = "normal";
    public string SpeakingPace { get; set; } = "normal";

    /// <summary>Accumulated personality traits, motivations, temperament (from chunked analysis).</summary>
    public string? PersonalitySummary { get; set; }

    /// <summary>Formal/informal, direct/subtle, attitude toward others — guides realistic speech synthesis.</summary>
    public string? SpeechStyleSummary { get; set; }

    public bool IsAiSuggested { get; set; } = true;
    public bool UserApproved { get; set; }

    public WorkEntity? Work { get; set; }
}
