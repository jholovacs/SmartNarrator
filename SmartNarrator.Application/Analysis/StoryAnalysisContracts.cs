using SmartNarrator.Domain.Enums;

namespace SmartNarrator.Application.Analysis;
/// <summary>Chunk boundary when sending excerpts to Ollama (optional slicing).</summary>
public sealed record SegmentBoundaryDto(int Index, int StartOffset, int EndOffset);

/// <summary>Validated model output persisted as proposals.</summary>
public sealed class StoryAnalysisResultDto
{
    public IReadOnlyList<CharacterSuggestionDto> Characters { get; init; } = Array.Empty<CharacterSuggestionDto>();
    public IReadOnlyList<UtteranceSuggestionDto> Utterances { get; init; } = Array.Empty<UtteranceSuggestionDto>();
    public IReadOnlyList<NarrativePassageSuggestionDto> NarrativePassages { get; init; } = Array.Empty<NarrativePassageSuggestionDto>();

    /// <summary>Chapters, POV shifts, scene breaks — offsets relative to the excerpt sent to the model.</summary>
    public IReadOnlyList<StoryStructureSectionSuggestionDto> StructureSections { get; init; } =
        Array.Empty<StoryStructureSectionSuggestionDto>();
}

public sealed class CharacterSuggestionDto
{
    public string ExternalKey { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public IReadOnlyList<string>? Aliases { get; init; }

    /// <summary>Warmth, temperament, motivations — refined across story chunks.</summary>
    public string? PersonalitySummary { get; init; }

    /// <summary>Register (formal↔informal), directness, interpersonal attitude, dialect markers.</summary>
    public string? SpeechStyleSummary { get; init; }

    public string GenderPresentation { get; init; } = "unspecified";
    public string Tone { get; init; } = "neutral";
    public string Accent { get; init; } = "none";
    public string Breathiness { get; init; } = "normal";
    public string SpeakingPace { get; init; } = "normal";
}

public sealed class UtteranceSuggestionDto
{
    public int StartOffset { get; init; }
    public int EndOffset { get; init; }
    public string SpeakerKind { get; init; } = "Character";
    public string? CharacterExternalKey { get; init; }
    public double Confidence { get; init; }
}

public sealed class NarrativePassageSuggestionDto
{
    public int StartOffset { get; init; }
    public int EndOffset { get; init; }
    public string? NarratorCharacterExternalKey { get; init; }
    public string PerspectiveNotes { get; init; } = string.Empty;
    public string GenderPresentation { get; init; } = "unspecified";
    public string Tone { get; init; } = "neutral";
    public string Accent { get; init; } = "none";
    public string Breathiness { get; init; } = "normal";
    public string SpeakingPace { get; init; } = "normal";
}

public sealed class StoryStructureSectionSuggestionDto
{
    public int StartOffset { get; init; }
    public int EndOffset { get; init; }
    public StoryStructureSectionKind Kind { get; init; }
    public string? Title { get; init; }
    public string Notes { get; init; } = string.Empty;
}
