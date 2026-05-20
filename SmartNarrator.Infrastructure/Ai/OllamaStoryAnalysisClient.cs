using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SmartNarrator.Application.Analysis;
using SmartNarrator.Application.Ports;
using SmartNarrator.Infrastructure.Options;

namespace SmartNarrator.Infrastructure.Ai;

public sealed class OllamaStoryAnalysisClient(
    HttpClient httpClient,
    IOptions<OllamaOptions> options,
    ILogger<OllamaStoryAnalysisClient> logger) : IStructuredStoryAnalysisClient
{
    public async Task<StoryAnalysisResultDto> AnalyzeExcerptAsync(
        string excerptUtf16,
        int excerptGlobalStartUtf16,
        int fullCanonicalUtf16Length,
        IReadOnlyList<SegmentBoundaryDto> segmentsRelativeToExcerpt,
        string? priorCharacterRegistryJson,
        int primaryAnnotationFocusStartRelativeUtf16,
        CancellationToken cancellationToken)
    {
        var o = options.Value;
        var clipped = segmentsRelativeToExcerpt
            .Where(s => s.StartOffset < excerptUtf16.Length)
            .Select(s => new SegmentBoundaryDto(
                s.Index,
                s.StartOffset,
                Math.Min(s.EndOffset, excerptUtf16.Length)))
            .ToList();

        var prompt = StoryAnalysisPromptBuilder.BuildForChunk(
            excerptUtf16,
            excerptGlobalStartUtf16,
            fullCanonicalUtf16Length,
            clipped,
            priorCharacterRegistryJson,
            primaryAnnotationFocusStartRelativeUtf16);

        var url = new Uri(o.BaseUri, "api/chat");

        var chatBody = new JsonObject
        {
            ["model"] = o.Model,
            ["stream"] = false,
            ["format"] = StoryAnalysisOllamaFormatSchema.CreateFormatRoot(),
            ["options"] = new JsonObject
            {
                ["temperature"] = Math.Clamp(o.AnalysisTemperature, 0d, 1d),
            },
            ["messages"] = new JsonArray
            {
                new JsonObject { ["role"] = "user", ["content"] = prompt },
            },
        };

        using var payload = new StringContent(chatBody.ToJsonString(), Encoding.UTF8, "application/json");

        using var resp = await httpClient.PostAsync(url, payload, cancellationToken);
        if (!resp.IsSuccessStatusCode)
        {
            var err = await resp.Content.ReadAsStringAsync(cancellationToken);
            logger.LogError("Ollama chat failed ({StatusCode}): {Body}", resp.StatusCode, err);
            throw new InvalidOperationException($"Ollama analysis failed ({resp.StatusCode})");
        }

        var responseJson = await resp.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        var content = ExtractAssistantJsonContent(responseJson, logger)
                      ?? throw new InvalidOperationException("Ollama message content missing");

        return StoryAnalysisEnvelopeParser.Parse(content, excerptUtf16.Length, logger);
    }

    public async Task<string> CompleteStructuredPromptAsync(JsonObject formatSchema, string userPrompt,
        CancellationToken cancellationToken)
    {
        var o = options.Value;
        var url = new Uri(o.BaseUri, "api/chat");

        var chatBody = new JsonObject
        {
            ["model"] = o.Model,
            ["stream"] = false,
            ["format"] = formatSchema,
            ["options"] = new JsonObject
            {
                ["temperature"] = Math.Clamp(o.AnalysisTemperature, 0d, 1d),
            },
            ["messages"] = new JsonArray
            {
                new JsonObject { ["role"] = "user", ["content"] = userPrompt },
            },
        };

        using var payload = new StringContent(chatBody.ToJsonString(), Encoding.UTF8, "application/json");

        using var resp = await httpClient.PostAsync(url, payload, cancellationToken);
        if (!resp.IsSuccessStatusCode)
        {
            var err = await resp.Content.ReadAsStringAsync(cancellationToken);
            logger.LogError("Ollama chat failed ({StatusCode}): {Body}", resp.StatusCode, err);
            throw new InvalidOperationException($"Ollama analysis failed ({resp.StatusCode})");
        }

        var responseJson = await resp.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        return ExtractAssistantJsonContent(responseJson, logger)
               ?? throw new InvalidOperationException("Ollama message content missing");
    }

    /// <summary>
    /// Ollama normally returns <c>message.content</c> as a string; some builds/models return JSON content as an object/array instead.
    /// </summary>
    private static string? ExtractAssistantJsonContent(string responseJson, ILogger<OllamaStoryAnalysisClient> logger)
    {
        try
        {
            using var doc = JsonDocument.Parse(responseJson);
            var root = doc.RootElement;

            if (root.TryGetProperty("message", out var msg) &&
                msg.TryGetProperty("content", out var contentEl))
            {
                var extracted = JsonElementToAssistantPayload(contentEl);
                if (!string.IsNullOrWhiteSpace(extracted))
                    return extracted.Trim();

                logger.LogWarning("Ollama message.content present but produced empty extracted payload.");
            }

            if (root.TryGetProperty("response", out var respEl))
            {
                var extracted = JsonElementToAssistantPayload(respEl);
                if (!string.IsNullOrWhiteSpace(extracted))
                    return extracted.Trim();
            }
        }
        catch (JsonException ex)
        {
            logger.LogError(ex, "Could not parse Ollama HTTP response as JSON.");
        }

        return null;
    }

    private static string? JsonElementToAssistantPayload(JsonElement el) =>
        el.ValueKind switch
        {
            JsonValueKind.String => el.GetString(),
            JsonValueKind.Object => el.GetRawText(),
            JsonValueKind.Array => el.GetRawText(),
            _ => null,
        };
}
