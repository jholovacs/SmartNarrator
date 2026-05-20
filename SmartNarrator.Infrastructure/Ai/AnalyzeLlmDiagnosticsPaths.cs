using Microsoft.Extensions.Hosting;
using SmartNarrator.Infrastructure.Options;

namespace SmartNarrator.Infrastructure.Ai;

public static class AnalyzeLlmDiagnosticsPaths
{
    public static string StorageRootPhysical(IHostEnvironment host, StorageOptions storage) =>
        Path.GetFullPath(Path.Combine(host.ContentRootPath, storage.RelativeRoot));

    /// <summary>NDJSON log: one structured JSON object per line per LLM exchange.</summary>
    public static string TurnsFilePhysical(IHostEnvironment host, StorageOptions storage, Guid jobId) =>
        Path.Combine(StorageRootPhysical(host, storage), "llm-diagnostics", jobId.ToString("D"), "turns.ndjson");
}
