using System.Linq;
using SmartNarrator.Application.Analysis;

namespace SmartNarrator.Infrastructure.Ai;

/// <summary>System/user instructions for structured story analysis (chunked over full canon).</summary>
internal static class StoryAnalysisPromptBuilder
{
    internal static string BuildForChunk(
        string excerptUtf16,
        int excerptGlobalStartUtf16,
        int fullCanonicalUtf16Length,
        IReadOnlyList<SegmentBoundaryDto> clippedSegments,
        string? priorCharacterRegistryJson,
        int primaryAnnotationFocusStartRelativeUtf16)
    {
        var excerptLen = excerptUtf16.Length;
        var segmentLines =
            string.Join(Environment.NewLine, clippedSegments.Select(s => $"{s.Index}:{s.StartOffset}-{s.EndOffset}"));

        var spanNote =
            $"This excerpt spans UTF-16 indices [{excerptGlobalStartUtf16:N0}, {excerptGlobalStartUtf16 + excerptLen:N0}) " +
            $"within the full story ({fullCanonicalUtf16Length:N0} UTF-16 units total). " +
            $"ALL JSON offsets MUST be relative to THIS excerpt only (0…{excerptLen:N0}), not the full story.{Environment.NewLine}{Environment.NewLine}";

        var continuityNote = primaryAnnotationFocusStartRelativeUtf16 > 0
            ? string.Concat(
                "CONTINUITY PREFIX: The first ",
                $"{primaryAnnotationFocusStartRelativeUtf16:N0}",
                " UTF-16 units may repeat the prior paragraph or ingest segment so dialogue stays grounded — ",
                "you may annotate them if needed, but prioritize NEW spans from offset ",
                $"{primaryAnnotationFocusStartRelativeUtf16:N0}",
                " onward (still expressed relative to THIS excerpt starting at 0).",
                Environment.NewLine,
                Environment.NewLine)
            : "";

        var priorBlock = string.IsNullOrWhiteSpace(priorCharacterRegistryJson)
            ? ""
            : string.Concat(
                "PRIOR_CHARACTER_REGISTRY (already discovered earlier in this story — reuse THE SAME external_key for the same person; refine personality_summary and speech_style_notes when new evidence appears):",
                Environment.NewLine,
                priorCharacterRegistryJson.Trim(),
                Environment.NewLine,
                Environment.NewLine);

        return string.Concat(
            $"You annotate fiction prose so each speaking identity becomes a reusable voice profile for speech synthesis.{Environment.NewLine}",
            "The API enforces JSON shape: root keys MUST be exactly characters, utterances, narrative_passages, and sections. ",
            "Do NOT return title, author, publisher, genre, rating, publication_date, or a standalone prose \\\"text\\\" field.",
            $"{Environment.NewLine}{Environment.NewLine}",
            spanNote,
            continuityNote,
            priorBlock,
            CoreGuidanceChunked(),
            $"{Environment.NewLine}",
            SchemaReminderChunked(excerptLen),
            $"{Environment.NewLine}",
            SegmentHint(segmentLines),
            $"{Environment.NewLine}EXCERPT START<<<{Environment.NewLine}",
            excerptUtf16,
            $"{Environment.NewLine}>>>EXCERPT END");
    }

    private static string SchemaReminderChunked(int excerptUtf16Length) =>
        string.Concat(
            $"Return ONLY compact JSON matching this schema (no markdown fences):{{",
            "\\\"characters\\\":[{\\\"external_key\\\":string,\\\"name\\\":string,\\\"aliases\\\"?:string[],",
            "\\\"personality_summary\\\"?:string,\\\"speech_style_notes\\\"?:string,",
            "\\\"gender_presentation\\\":string,\\\"tone\\\":string," +
            "\\\"accent\\\":string,\\\"breathiness\\\":string,\\\"speaking_pace\\\":string}]," +
            "\\\"utterances\\\":[{\\\"start_offset\\\":int,\\\"end_offset\\\":int,\\\"speaker_kind\\\":\\\"Character\\\"|\\\"Narrator\\\"," +
            "\\\"character_external_key\\\"?:string,\\\"confidence\\\":number,\\\"speaker_uncertain\\\"?:boolean}]," +
            "\\\"narrative_passages\\\":[{" +
            "\\\"start_offset\\\":int,\\\"end_offset\\\":int,\\\"narrator_character_external_key\\\"?:string," +
            "\\\"perspective_notes\\\":string,\\\"gender_presentation\\\":string,\\\"tone\\\":string,\\\"accent\\\":string,\\\"breathiness\\\":string,\\\"speaking_pace\\\":string}]," +
            "\\\"sections\\\":[{" +
            "\\\"start_offset\\\":int,\\\"end_offset\\\":int,\\\"kind\\\":\\\"chapter\\\"|\\\"perspective_shift\\\"|\\\"scene_break\\\"|\\\"other\\\"," +
            "\\\"title\\\"?:string,\\\"notes\\\"?:string}]}." +
            $"{Environment.NewLine}",
            $"Offsets MUST be UTF-16 code unit indices into THIS excerpt only (length {excerptUtf16Length}); surrogate pairs count as two units. ",
            "Normalize CRLF to \\\\n ONLY when reasoning about indices (the excerpt already uses \\\\n).",
            $"{Environment.NewLine}",
            "Populate \\\"characters\\\" ONLY with identities who actually SPEAK (utterances / voiced dialogue in this excerpt) or whose voice is needed as an attributed NARRATOR (narrative_passages.narrator_character_external_key). ",
            "Every distinct speaker/narrator external_key referenced there MUST appear in characters[]. Do NOT add profiles for characters who are only mentioned in narration without speaking.",
            $"{Environment.NewLine}");

    private static string SegmentHint(string segmentLines) =>
        $"Known paragraph segments (approximate; still judge dialogue and narration yourself):{Environment.NewLine}{segmentLines}";

    private static string CoreGuidanceChunked() =>
        """
VOICE PROFILES — characters[]:
• One row per DISTINCT speaker or narrator voice that needs synthesis — not for background figures who never speak or narrate in THIS excerpt. external_key is a stable slug (e.g. ashe_blackwood, narrator_close_third).
• name = the clearest display name readers see for that speaker/narrator.
• aliases = alternate surface forms that refer to THE SAME speaking identity in THIS excerpt: nicknames, titles ("Mrs. Blackwood"), shortened names, occupational epithets ("the trainer"), affectionate forms — merge duplicates under ONE external_key.
• personality_summary = temperament, motivations, emotional baseline, moral shading — concise but concrete (several sentences).
• speech_style_notes = how they talk for synthesis: formal↔informal, blunt↔subtle/sarcastic, vocabulary level, recurring fillers, dialect/markers, how they address specific other characters (warm, hostile, deferential).
• gender_presentation = infer from grammar (he/she/they), honorifics, explicit cues; prefer feminine | masculine | neutral when justified — use unspecified only when truly impossible.
• tone, accent, breathiness, speaking_pace = infer from dialogue tags ("she whispered"), dialect/markers, narrative framing — avoid bland defaults when prose signals emotion or pace.

DIALOGUE — utterances[]:
• Character = quoted/spoken lines tied to a character’s voice; Narrator = exposition / narration unless clearly voiced aloud by a named role.
• When speaker_kind is Character you MUST set character_external_key matching characters[].external_key whenever attribution is reasonably confident.
• If you CANNOT confidently identify the speaker, set speaker_uncertain=true AND omit character_external_key — never invent fake keys for uncertainty (humans will assign speakers later).
• NEVER collapse unrelated anonymous speakers into one bucket key.

NARRATION — narrative_passages[]:
• Cover stretches dominated by narrator exposition between dialogue beats; mirror narrator voice metadata.
• narrator_character_external_key links when the narrator voice maps to a named profile.

SECTIONS — sections[]:
• Mark chapters/headings (chapter), POV shifts (perspective_shift), scene/time jumps or decorative breaks (scene_break).

CONFIDENCE:
• Lower confidence when attribution or alias-merge is uncertain; combine with speaker_uncertain when appropriate.

""".TrimEnd().Replace("\r\n", "\n").Replace("\n", Environment.NewLine);
}
