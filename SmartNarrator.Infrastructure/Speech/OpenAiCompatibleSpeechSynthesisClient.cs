using System.Net.Http.Json;
using Microsoft.Extensions.Options;
using SmartNarrator.Application.Ports;
using SmartNarrator.Infrastructure.Options;

namespace SmartNarrator.Infrastructure.Speech;

/// <summary>Calls an OpenAI-compatible <c>/v1/audio/speech</c> endpoint (often used by Ollama-adjacent bridges).</summary>
public sealed class OpenAiCompatibleSpeechSynthesisClient(
    HttpClient httpClient,
    IOptions<SpeechSynthesisOptions> options)
    : ISpeechSynthesisClient
{
    public async Task<byte[]> SynthesizeAsync(SpeechSynthesisRequestDto request, CancellationToken cancellationToken)
    {
        var opts = options.Value;

        var model = string.IsNullOrWhiteSpace(request.VoiceModel) ? opts.DefaultModel : request.VoiceModel!;
        var voice = string.IsNullOrWhiteSpace(request.VoiceId) ? opts.DefaultVoiceId : request.VoiceId!;
        var body = new Dictionary<string, object?>
        {
            ["model"] = model,
            ["voice"] = voice,
            ["input"] = request.Text,
            ["response_format"] = "wav",
        };

        foreach (var kv in request.ExtraParameters)
            body[kv.Key] = kv.Value;

        using var payload = JsonContent.Create(body);
        using var resp = await httpClient.PostAsync(opts.RelativePath.TrimStart('/'), payload, cancellationToken);
        resp.EnsureSuccessStatusCode();
        return await resp.Content.ReadAsByteArrayAsync(cancellationToken);
    }
}
