namespace SmartNarrator.Domain.Entities;

/// <summary>Paragraph-level chunk referencing offsets in <see cref="WorkEntity.CanonicalText"/>.</summary>
public class TextSegmentEntity
{
    public Guid Id { get; set; }
    public Guid WorkId { get; set; }
    public int OrderIndex { get; set; }
    public int StartOffset { get; set; }
    public int EndOffset { get; set; }

    public WorkEntity? Work { get; set; }
}
