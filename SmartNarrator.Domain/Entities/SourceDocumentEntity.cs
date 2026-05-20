using SmartNarrator.Domain.Enums;

namespace SmartNarrator.Domain.Entities;

public class SourceDocumentEntity
{
    public Guid Id { get; set; }
    public Guid WorkId { get; set; }
    public SourceFormat Format { get; set; }
    /// <summary>Relative path under API storage directory, if persisted on disk.</summary>
    public string? StoredRelativePath { get; set; }
    public string? OriginalFileName { get; set; }

    public WorkEntity? Work { get; set; }
}
