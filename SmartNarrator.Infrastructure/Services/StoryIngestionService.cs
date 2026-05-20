using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SmartNarrator.Application.Ports;
using SmartNarrator.Domain.Entities;
using SmartNarrator.Domain.Enums;
using SmartNarrator.Infrastructure.Options;
using SmartNarrator.Infrastructure.Persistence;

namespace SmartNarrator.Infrastructure.Services;

public sealed class StoryIngestionService(
    SmartNarratorDbContext db,
    IHostEnvironment hostEnvironment,
    IOptions<StorageOptions> storageOptions,
    IJobQueuePublisher jobQueuePublisher,
    ILogger<StoryIngestionService> logger) : Application.Ports.IStoryIngestionService
{
    public async Task<Guid> EnqueueIngestAsync(
        Guid workId,
        Stream content,
        SourceFormat format,
        string originalFileName,
        CancellationToken cancellationToken)
    {
        var work = await db.Works.SingleOrDefaultAsync(w => w.Id == workId, cancellationToken)
                   ?? throw new InvalidOperationException($"Work {workId} not found.");

        var relativeRoot = Path.GetFullPath(
            Path.Combine(hostEnvironment.ContentRootPath, storageOptions.Value.RelativeRoot));
        Directory.CreateDirectory(relativeRoot);

        var workDir = Path.Combine(relativeRoot, workId.ToString("N"));
        Directory.CreateDirectory(workDir);

        var safeName = Sanitize(originalFileName);
        var docId = Guid.NewGuid();
        var fileName = $"{docId:N}-{safeName}";
        var diskPath = Path.Combine(workDir, fileName);

        await using (var fs = File.Create(diskPath))
        {
            if (content.CanSeek)
                content.Position = 0;

            await content.CopyToAsync(fs, cancellationToken);
        }

        var relativeStored = $"{workId:N}/{fileName}";
        var sourceDoc = new SourceDocumentEntity
        {
            Id = docId,
            WorkId = workId,
            Format = format,
            StoredRelativePath = relativeStored,
            OriginalFileName = originalFileName,
        };

        db.SourceDocuments.Add(sourceDoc);

        var job = new BackgroundJobEntity
        {
            Id = Guid.NewGuid(),
            WorkId = workId,
            Type = BackgroundJobType.Ingest,
            Status = BackgroundJobStatus.Pending,
            ProgressPercent = 0,
            CreatedUtc = DateTimeOffset.UtcNow,
            PayloadJson = JsonSerializer.Serialize(new IngestJobPayload(sourceDoc.Id)),
        };

        db.BackgroundJobs.Add(job);
        await db.SaveChangesAsync(cancellationToken);

        await jobQueuePublisher.PublishPendingJobAsync(job.Id, job.Type, cancellationToken).ConfigureAwait(false);

        logger.LogInformation("Queued ingest job {JobId} for work {WorkId}", job.Id, workId);
        return job.Id;
    }

    private sealed record IngestJobPayload(Guid SourceDocumentId);

    private static string Sanitize(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return "upload.bin";

        foreach (var c in Path.GetInvalidFileNameChars())
            name = name.Replace(c, '_');

        name = Path.GetFileName(name);
        return name.Length > 240 ? name[..240] : name;
    }
}
