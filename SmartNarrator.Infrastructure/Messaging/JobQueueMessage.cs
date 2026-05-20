using System.Text.Json;

namespace SmartNarrator.Infrastructure.Messaging;

internal static class JobQueueMessage
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    internal static byte[] Serialize(Guid jobId) =>
        JsonSerializer.SerializeToUtf8Bytes(new Payload(jobId), SerializerOptions);

    internal static Guid Deserialize(ReadOnlyMemory<byte> body)
    {
        var dto = JsonSerializer.Deserialize<Payload>(body.Span, SerializerOptions)
                  ?? throw new InvalidOperationException("Invalid job queue payload.");
        if (dto.JobId == Guid.Empty)
            throw new InvalidOperationException("Invalid job queue payload.");
        return dto.JobId;
    }

    private sealed record Payload(Guid JobId);

}
