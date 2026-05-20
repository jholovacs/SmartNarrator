namespace SmartNarrator.Application.Ports;

public sealed class SpeechSynthesisRequestDto
{
    public string VoiceModel { get; init; } = string.Empty;
    public string Text { get; init; } = string.Empty;
    /// <summary>OpenAI-style voice id when supported.</summary>
    public string VoiceId { get; init; } = "default";
    public Dictionary<string, string> ExtraParameters { get; init; } = new(StringComparer.OrdinalIgnoreCase);
}

public interface ISpeechSynthesisClient
{
    Task<byte[]> SynthesizeAsync(SpeechSynthesisRequestDto request, CancellationToken cancellationToken);
}
