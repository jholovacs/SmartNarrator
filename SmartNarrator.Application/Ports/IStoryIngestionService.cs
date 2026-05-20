using SmartNarrator.Domain.Enums;

namespace SmartNarrator.Application.Ports;

public interface IStoryIngestionService
{
    /// <returns>Background job id for async ingest pipeline.</returns>
    Task<Guid> EnqueueIngestAsync(
        Guid workId,
        Stream content,
        SourceFormat format,
        string originalFileName,
        CancellationToken cancellationToken);
}
