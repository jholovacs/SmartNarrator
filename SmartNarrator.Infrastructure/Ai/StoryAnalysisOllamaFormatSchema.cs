using System.Text.Json.Nodes;

namespace SmartNarrator.Infrastructure.Ai;

/// <summary>
/// JSON Schema passed to Ollama <c>/api/chat</c> <c>format</c> so the model cannot substitute random object shapes
/// (e.g. <c>title</c>/<c>text</c>/<c>author</c>) when <c>format: "json"</c> alone only enforces “valid JSON”.
/// See https://ollama.com/blog/structured-outputs
/// </summary>
internal static class StoryAnalysisOllamaFormatSchema
{
    /// <summary>Deep-clone per request so callers never mutate shared state.</summary>
    internal static JsonObject CreateFormatRoot() =>
        (JsonNode.Parse(SchemaJson)?.AsObject())
        ?? throw new InvalidOperationException("Built-in story-analysis JSON schema failed to parse.");

    private const string SchemaJson =
        """
        {
          "type": "object",
          "properties": {
            "characters": {
              "type": "array",
              "items": { "type": "object", "additionalProperties": true }
            },
            "utterances": {
              "type": "array",
              "items": { "type": "object", "additionalProperties": true }
            },
            "narrative_passages": {
              "type": "array",
              "items": { "type": "object", "additionalProperties": true }
            },
            "sections": {
              "type": "array",
              "items": { "type": "object", "additionalProperties": true }
            }
          },
          "required": ["characters", "utterances", "narrative_passages", "sections"]
        }
        """;
}
