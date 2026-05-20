namespace SmartNarrator.Domain.Entities;

/// <summary>One narrative work (novel, story import).</summary>
public class WorkEntity
{
    public Guid Id { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Language { get; set; } = "en";
    /// <summary>Normalized UTF-8 prose used for segmentation and highlighting.</summary>
    public string CanonicalText { get; set; } = string.Empty;
    public DateTimeOffset CreatedUtc { get; set; }

    public ICollection<SourceDocumentEntity> SourceDocuments { get; set; } = new List<SourceDocumentEntity>();
    public ICollection<TextSegmentEntity> Segments { get; set; } = new List<TextSegmentEntity>();
    public ICollection<CharacterProfileEntity> Characters { get; set; } = new List<CharacterProfileEntity>();
    public ICollection<UtteranceEntity> Utterances { get; set; } = new List<UtteranceEntity>();
    public ICollection<NarrativePassageEntity> NarrativePassages { get; set; } = new List<NarrativePassageEntity>();
    public ICollection<StoryStructureSectionEntity> StoryStructureSections { get; set; } =
        new List<StoryStructureSectionEntity>();
    public ICollection<WorkChapterEntity> WorkChapters { get; set; } = new List<WorkChapterEntity>();
    public ICollection<DialogueSpanEntity> DialogueSpans { get; set; } = new List<DialogueSpanEntity>();
    public ICollection<BackgroundJobEntity> BackgroundJobs { get; set; } = new List<BackgroundJobEntity>();
    public ICollection<AudioArtifactEntity> AudioArtifacts { get; set; } = new List<AudioArtifactEntity>();
}
