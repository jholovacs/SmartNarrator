namespace SmartNarrator.Infrastructure.Ai;

internal static class StoryPhasedAnalysisPrompts
{
    internal static string CharacterDialoguePrompt(
        string? chapterTitle,
        int chapterOrderIndex,
        string dialogueCatalogJson,
        string? priorCharacterRegistryJson)
    {
        var nl = Environment.NewLine;
        var prior = string.IsNullOrWhiteSpace(priorCharacterRegistryJson)
            ? ""
            : string.Concat(
                "PRIOR_CHARACTER_REGISTRY (reuse SAME external_key for the same identity; aliases are alternate surface spellings spoken in prose):",
                nl,
                priorCharacterRegistryJson.Trim(),
                nl,
                nl);

        var title = string.IsNullOrWhiteSpace(chapterTitle) ? "(untitled chapter)" : chapterTitle.Trim();

        return string.Concat(
            prior,
            $"Chapter #{chapterOrderIndex + 1}: {title}",
            nl,
            nl,
            "You receive FIXED quoted spans as JSON (order_index MUST match immutable input order_index; one row per quoted span). ",
            "Your job—together—is (a) classify each span as spoken aloud vs quoted emphasis/non-dialogue, ",
            "(b) infer voice profiles ONLY for identifiable speakers referenced by those classifications, ",
            "(c) assign every spoken span to exactly one speakers profile external_key — never leave spoken dialogue orphaned.",
            nl,
            "Return ONLY compact JSON with nested \"characters\" and \"assignments\" arrays (no prose).",
            nl,
            nl,
            "**characters[]**: one row per persona that actually speaks aloud in SOME span labeled not_speech=false below. "
            +
            "\"external_key\", \"name\", optional \"aliases[]\" (alternate surface forms readers see in tags or narration), "
            +
            "\"personality_summary?\", \"speech_style_notes?\", \"gender_presentation\", \"tone\", \"accent\", \"breathiness\", \"speaking_pace\". "
            +
            "When PRIOR_REGISTRY already tracks someone, reuse that external_key; when a NEW speaker first appears IN THESE SPANS, "
            +
            "invent stable snake_case external_key NOW and emit the SAME key in BOTH characters[] AND every assignments[] row that quotes them."
            +
            nl,
            "Do NOT add characters[] solely for figures who are addressed, overheard-without-quote, or only mentioned narratively "
            +
            "(unless narration itself is wrongly inside a quoted span you must annotate). Omit profiles for mute names.",
            nl,
            nl,
            "**assignments[]** (same length/order as catalogue rows; order_index 0-based, matching DIALOGUE_SPANS_JSON):"
            +
            nl,
            "- \"order_index\" (integer, REQUIRED; must match catalogue).",
            nl,
            "- \"not_speech\" (boolean, REQUIRED): true only when punctuation wraps emphasis irony titles non-spoken excerpts etc; "
            +
            "false means in-world dialogue / monologue attributable to someone.",
            nl,
            "- \"character_external_key\" (string, REQUIRED ALWAYS): "
            +
            "If not_speech=true, MUST be literal \"__not_speech__\" (used only as sentinel). "
            +
            "If not_speech=false, MUST be a non-empty key that IDENTICALLY matches one characters[].external_key OR a PRIOR_CHARACTER_REGISTRY "
            +
            "external_key for the attributable speaker—even if you flag uncertainty."
            +
            nl,
            "- NEVER output null omitted or empty character_external_key for not_speech=false. "
            +
            "Best effort: invent a provisional profile + matching key rather than omitting linkage. "
            +
            "When torn between overlapping speakers infer the single MOST LIKELY key and compensate with "
            +
            "speaker_uncertain=true plus lower confidence.",
            nl,
            "- \"speaker_uncertain\" + \"confidence\" (0–1): express doubt on WHO spoke; NEVER express doubt by wiping the key.",
            nl,
            nl,
            "DIALOGUE_SPANS_JSON:",
            nl,
            dialogueCatalogJson);
    }
}
