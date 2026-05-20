namespace SmartNarrator.Domain.Entities;

public class AudioArtifactEntity
{
    public Guid Id { get; set; }
    public Guid WorkId { get; set; }
    public Guid? UtteranceId { get; set; }
    public int? StartOffset { get; set; }
    public int? EndOffset { get; set; }
    /// <summary>Relative path within storage directory.</summary>
    public string RelativePath { get; set; } = string.Empty;
    public string MimeType { get; set; } = "audio/wav";

    public WorkEntity? Work { get; set; }
}
