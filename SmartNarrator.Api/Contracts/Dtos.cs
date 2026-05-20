using SmartNarrator.Domain.Enums;

namespace SmartNarrator.Api.Contracts;

public sealed record WorkCreateRequest(string Title, string Language = "en");

/// <summary>POST /works/import-from-url payload.</summary>
public sealed record WorkImportFromUrlRequest(
    string Url,
    SourceFormat? Format = null,
    string Language = "en",
    string? Title = null);

public sealed record ImportWorkQueuedResponse(Guid WorkId, Guid JobId);

public sealed record WorkSummaryDto(
    Guid Id,
    string Title,
    string Language,
    DateTimeOffset CreatedUtc,
    int CanonicalTextLength,
    bool HasArtifacts);

public sealed record WorkDetailDto(
    Guid Id,
    string Title,
    string Language,
    DateTimeOffset CreatedUtc,
    string CanonicalText);

public sealed record JobDto(
    Guid Id,
    BackgroundJobType Type,
    BackgroundJobStatus Status,
    int ProgressPercent,
    string? ProgressPhase,
    Guid? WorkId,
    string? PayloadJson,
    string? ErrorMessage,
    bool CancellationRequested,
    DateTimeOffset CreatedUtc,
    DateTimeOffset UpdatedUtc,
    DateTimeOffset? StartedUtc,
    DateTimeOffset? CompletedUtc);

public sealed record CharacterDto(
    Guid Id,
    string? AiExternalKey,
    string Name,
    IReadOnlyList<string>? Aliases,
    string? PersonalitySummary,
    string? SpeechStyleSummary,
    string GenderPresentation,
    string Tone,
    string Accent,
    string Breathiness,
    string SpeakingPace,
    bool IsAiSuggested,
    bool UserApproved);

public sealed record CharacterCreateDto(string Name);

/// <summary>Merges duplicate profiles into <see cref="TargetCharacterId"/>; sources are deleted after FK repoint.</summary>
public sealed record CharacterMergeDto(Guid TargetCharacterId, IReadOnlyList<Guid> SourceCharacterIds);

public sealed record CharacterUpsertDto(
    Guid Id,
    string Name,
    string? AiExternalKey,
    IReadOnlyList<string>? Aliases,
    string? PersonalitySummary,
    string? SpeechStyleSummary,
    string GenderPresentation,
    string Tone,
    string Accent,
    string Breathiness,
    string SpeakingPace,
    bool UserApproved,
    bool PatchAiExternalKey = false);

public sealed record UtteranceDto(
    Guid Id,
    int StartOffset,
    int EndOffset,
    SpeakerKind SpeakerKind,
    Guid? CharacterId,
    double Confidence,
    bool SpeakerNeedsReview,
    bool IsAiSuggested,
    bool UserApproved);

public sealed record UtteranceUpsertDto(
    Guid Id,
    SpeakerKind SpeakerKind,
    Guid? CharacterId,
    bool UserApproved);

public sealed record TextSegmentDto(Guid Id, int OrderIndex, int StartOffset, int EndOffset);

public sealed record NarrativePassageDto(
    Guid Id,
    int StartOffset,
    int EndOffset,
    Guid? NarratorCharacterId,
    string PerspectiveNotes,
    string GenderPresentation,
    string Tone,
    string Accent,
    string Breathiness,
    string SpeakingPace,
    bool IsAiSuggested);

public sealed record NarrativePassageUpsertDto(
    Guid Id,
    Guid? NarratorCharacterId,
    string PerspectiveNotes,
    string GenderPresentation,
    string Tone,
    string Accent,
    string Breathiness,
    string SpeakingPace);

public sealed record StoryStructureSectionDto(
    Guid Id,
    int StartOffset,
    int EndOffset,
    StoryStructureSectionKind Kind,
    string? Title,
    string Notes,
    bool IsAiSuggested);

public sealed record AudioArtifactDto(Guid Id, string RelativePath, int? StartOffset, int? EndOffset, Guid? UtteranceId);

public sealed record WorkChapterDto(
    Guid Id,
    int OrderIndex,
    int StartOffset,
    int EndOffset,
    int? HeadingStartOffset,
    int? HeadingEndOffset,
    string? Title,
    string Notes,
    bool IsAiSuggested);

public sealed record DialogueSpanDto(
    Guid Id,
    Guid ChapterId,
    int OrderIndexInChapter,
    int StartOffset,
    int EndOffset,
    SpeakerKind SpeakerKind,
    double Confidence,
    bool IsAiSuggested);

public sealed record TimelineDto(
    string CanonicalText,
    IReadOnlyList<TextSegmentDto> Segments,
    IReadOnlyList<UtteranceDto> Utterances,
    IReadOnlyList<NarrativePassageDto> Narratives,
    IReadOnlyList<StoryStructureSectionDto> StructureSections,
    IReadOnlyList<WorkChapterDto> WorkChapters,
    IReadOnlyList<DialogueSpanDto> DialogueSpans);

/// <summary>POST .../timeline/bulk-merge and bulk-delete body.</summary>
public sealed record TimelineBulkDto(string EntityKind, IReadOnlyList<Guid> Ids);
