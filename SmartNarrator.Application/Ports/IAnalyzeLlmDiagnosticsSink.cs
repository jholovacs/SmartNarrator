namespace SmartNarrator.Application.Ports;

/// <summary>
/// Opt-in persistence of phased-analysis LLM user prompts and assistant payloads (debugging parsers / characterization).
/// </summary>
public interface IAnalyzeLlmDiagnosticsSink
{
    Task RecordTurnAsync(Guid jobId, Guid? workId, string step, string model, string prompt, string response,
        CancellationToken cancellationToken);
}
