using System.Text.Json.Nodes;

namespace SmartNarrator.Infrastructure.Ai;

internal static class PhasedAnalysisSchemas
{
    internal static JsonObject ChapterDetectionRoot() =>
        (JsonNode.Parse(
            """
            {
              "type": "object",
              "properties": {
                "chapters": { "type": "array", "items": { "type": "object", "additionalProperties": true } }
              },
              "required": ["chapters"]
            }
            """)?.AsObject())
        ?? throw new InvalidOperationException("Chapter schema parse failed.");

    /// <remarks>
    /// Assignments enforce non-empty sentinel for every row so models cannot omit linkage on spoken spans.
    /// </remarks>
    internal static JsonObject CharacterAssignmentRoot() =>
        (JsonNode.Parse(
            """
            {
              "type": "object",
              "properties": {
                "characters": {
                  "type": "array",
                  "items": {
                    "type": "object",
                    "properties": {
                      "external_key": { "type": "string", "minLength": 1 },
                      "name": { "type": "string", "minLength": 1 },
                      "aliases": { "type": "array", "items": { "type": "string" } },
                      "personality_summary": { "type": "string" },
                      "speech_style_notes": { "type": "string" },
                      "gender_presentation": { "type": "string" },
                      "tone": { "type": "string" },
                      "accent": { "type": "string" },
                      "breathiness": { "type": "string" },
                      "speaking_pace": { "type": "string" }
                    },
                    "required": ["external_key", "name"],
                    "additionalProperties": true
                  }
                },
                "assignments": {
                  "type": "array",
                  "items": {
                    "type": "object",
                    "properties": {
                      "order_index": { "type": "integer" },
                      "not_speech": { "type": "boolean" },
                      "speaker_uncertain": { "type": "boolean" },
                      "confidence": { "type": "number", "minimum": 0, "maximum": 1 },
                      "character_external_key": { "type": "string", "minLength": 1 }
                    },
                    "required": [
                      "order_index",
                      "not_speech",
                      "speaker_uncertain",
                      "confidence",
                      "character_external_key"
                    ],
                    "additionalProperties": true
                  }
                }
              },
              "required": ["characters", "assignments"]
            }
            """)?.AsObject())
        ?? throw new InvalidOperationException("Character assignment schema parse failed.");
}
