using System.Text.Json;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SmartNarrator.Application.Ports;
using SmartNarrator.Infrastructure.Options;

namespace SmartNarrator.Infrastructure.Ai;

public sealed class FileAnalyzeLlmDiagnosticsSink(
    IOptions<OllamaOptions> ollamaOptions,
    IOptions<StorageOptions> storageOptions,
    IHostEnvironment hostEnvironment,
    ILogger<FileAnalyzeLlmDiagnosticsSink> logger) : IAnalyzeLlmDiagnosticsSink
{
    private readonly object _appendLock = new();

    /// <inheritdoc />
    public Task RecordTurnAsync(Guid jobId, Guid? workId, string step, string model, string prompt, string response,
        CancellationToken cancellationToken)
    {
        if (!ollamaOptions.Value.CaptureAnalyzeLlmTurns)
            return Task.CompletedTask;

        if (jobId == Guid.Empty)
            return Task.CompletedTask;

        var maxField = Math.Max(4096, ollamaOptions.Value.AnalyzeDiagnosticsMaxCharsPerField);
        try
        {
            var dto = new LlmDiagTurnDto(
                DateTimeOffset.UtcNow,
                jobId,
                workId,
                step,
                model,
                MaybeTruncate(prompt, maxField),
                MaybeTruncate(response, maxField));

            var json = JsonSerializer.Serialize(dto, SerializerOptions.Instance);
            var path = AnalyzeLlmDiagnosticsPaths.TurnsFilePhysical(hostEnvironment, storageOptions.Value, jobId);

            cancellationToken.ThrowIfCancellationRequested();

            lock (_appendLock)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var dir = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(dir))
                    Directory.CreateDirectory(dir);
                File.AppendAllText(path, json + Environment.NewLine);
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to append LLM diagnostics for job {JobId} step {Step}", jobId, step);
        }

        return Task.CompletedTask;
    }

    private static string MaybeTruncate(string? value, int maxChars)
    {
        if (string.IsNullOrEmpty(value))
            return string.Empty;
        return value.Length <= maxChars ? value : value[..maxChars] + "\n…[truncated]";
    }

    private sealed record LlmDiagTurnDto(
        DateTimeOffset Utc,
        Guid JobId,
        Guid? WorkId,
        string Step,
        string Model,
        string Prompt,
        string Response);

    private static class SerializerOptions
    {
        internal static readonly JsonSerializerOptions Instance = new()
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = false,
        };
    }
}
