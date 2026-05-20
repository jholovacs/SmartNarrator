namespace SmartNarrator.Infrastructure.Ai;

internal static class StoryIngestPrompts
{
    /// <summary>
    /// Plain text / Markdown / PDF imports lack spine markup—ask for salient narrative divisions instead of labeled chapters.
    /// </summary>
    internal static string MajorContextShiftPrompt(string excerptUtf16, int excerptGlobalStartUtf16,
        int fullCanonicalUtf16Length)
    {
        var len = excerptUtf16.Length;
        var nl = Environment.NewLine;
        return string.Concat(
            "Identify MAJOR NARRATIVE DIVISIONS where context shifts strongly (scene/time/POV arc jumps suitable as audiobook sections). ",
            "Do NOT rely on the literal word \"chapter\"; cues may be whitespace breaks, asterisk rules, blank stretches, ",
            "or implicit pivots.",
            nl,
            $"Full story UTF-16 length {fullCanonicalUtf16Length:N0}; excerpt covers [{excerptGlobalStartUtf16:N0}, {excerptGlobalStartUtf16 + len:N0}).",
            nl,
            "Return ONLY compact JSON with root \"chapters\" as an array (machine schema expects this key).",
            nl,
            "Each entry MUST include:",
            nl,
            "- start_offset / end_offset (UTF-16 indices RELATIVE TO THIS excerpt only, 0…",
            $"{len:N0}).",
            nl,
            "- optional title (short scene label).",
            nl,
            "- optional heading_start_offset / heading_end_offset RELATIVE TO excerpt.",
            nl,
            "If this excerpt has only one coherent arc, return ONE entry spanning the entire excerpt.",
            nl,
            nl,
            "EXCERPT START<<<",
            nl,
            excerptUtf16,
            nl,
            ">>>EXCERPT END");
    }
}
