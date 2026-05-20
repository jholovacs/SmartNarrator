using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace SmartNarrator.Infrastructure.Ai;

internal static class PhasedAnalysisJsonParser
{
    internal const string NotSpeechExternalKeySentinel = "__not_speech__";
    internal static List<ChapterPhaseRow> ParseChapterPhase(string json, int excerptUtf16Len, ILogger logger)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (!root.TryGetProperty("chapters", out var arr) || arr.ValueKind != JsonValueKind.Array)
                return [];

            var list = new List<ChapterPhaseRow>();
            foreach (var el in arr.EnumerateArray())
            {
                var start = ReadInt(el, "start_offset", "start");
                var end = ReadInt(el, "end_offset", "end");
                if (!TryNormalizeSpan(start, end, excerptUtf16Len, out var a, out var b))
                    continue;

                list.Add(new ChapterPhaseRow(
                    a,
                    b,
                    ReadString(el, "title"),
                    ReadNullableInt(el, "heading_start_offset", "heading_start"),
                    ReadNullableInt(el, "heading_end_offset", "heading_end")));
            }

            return list;
        }
        catch (JsonException ex)
        {
            logger.LogWarning(ex, "Chapter phase JSON parse failed.");
            return [];
        }
    }

    internal static CharacterAssignmentPhaseParseResult ParseCharacterAssignmentPhase(string json, ILogger logger)
    {
        var chars = new List<CharacterAssignmentCharacterRow>();
        var assigns = new List<CharacterAssignmentRow>();
        try
        {
            var normalized = PrepareCharacterAssignmentJson(json.Trim());
            using var doc = JsonDocument.Parse(normalized);
            var rootCursor = doc.RootElement;
            // Arrays may appear on different wrappers (structured-output quirks): search the whole subtree.
            if (!TryFindAssignmentsArrayRecursive(rootCursor, out var aarr) ||
                aarr.ValueKind != JsonValueKind.Array)
            {
                logger.LogWarning(
                    "Character assignment JSON: missing assignments/dialogue-assignments array after flexible parse.");
                return new CharacterAssignmentPhaseParseResult(chars, assigns);
            }

            _ = TryFindCharactersArrayRecursive(rootCursor, out var carr);
            if (carr.ValueKind != JsonValueKind.Array)
            {
                logger.LogWarning(
                    "Character assignment JSON: optional characters[] absent (will rely on placeholders from assignment keys).");
            }

            if (carr.ValueKind == JsonValueKind.Array)
            {
                foreach (var el in carr.EnumerateArray())
                {
                    var key = FirstNonEmpty(
                        ReadString(el, "external_key"),
                        ReadString(el, "externalKey"),
                        ReadString(el, "key"),
                        ReadString(el, "slug"),
                        ReadString(el, "name"));
                    if (string.IsNullOrWhiteSpace(key))
                        continue;

                    chars.Add(new CharacterAssignmentCharacterRow(
                        key.Trim(),
                        FirstNonEmpty(ReadString(el, "name"), ReadString(el, "Name")) ?? key.Trim(),
                        ReadStringList(el, "aliases") ?? ReadStringList(el, "Aliases"),
                        FirstNonEmpty(ReadString(el, "personality_summary"), ReadString(el, "personalitySummary")),
                        FirstNonEmpty(ReadString(el, "speech_style_notes"), ReadString(el, "speechStyleNotes")),
                        FirstNonEmpty(ReadString(el, "gender_presentation"), ReadString(el, "genderPresentation")) ??
                        "unspecified",
                        FirstNonEmpty(ReadString(el, "tone"), ReadString(el, "Tone")) ?? "neutral",
                        FirstNonEmpty(ReadString(el, "accent"), ReadString(el, "Accent")) ?? "none",
                        FirstNonEmpty(ReadString(el, "breathiness"), ReadString(el, "Breathiness")) ?? "normal",
                        FirstNonEmpty(ReadString(el, "speaking_pace"), ReadString(el, "speakingPace")) ?? "normal"));
                }
            }

            foreach (var el in aarr.EnumerateArray())
            {
                var idx = ReadInt(el,
                    "order_index",
                    "OrderIndex",
                    "orderIndex",
                    "span_order_index",
                    "spanOrderIndex",
                    "dialogue_span_order_index",
                    "dialogueSpanOrderIndex",
                    "dialogue_span_index",
                    "dialogueSpanIndex",
                    "index");
                var ck = FirstNonEmpty(
                    ReadString(el, "character_external_key"),
                    ReadString(el, "characterExternalKey"),
                    ReadString(el, "CharacterExternalKey"),
                    ReadString(el, "external_key"),
                    ReadString(el, "externalKey"),
                    ReadString(el, "speaker_external_key"),
                    ReadString(el, "speakerExternalKey"),
                    ReadString(el, "character_key"),
                    ReadString(el, "characterKey"),
                    ReadString(el, "speaker_key"),
                    ReadString(el, "speakerKey"),
                    ReadNestedSpeakerOrCharacterExternalKey(el));
                var uncertain = ReadBool(el,
                    "speaker_uncertain",
                    "speakerUncertain",
                    "SpeakerUncertain",
                    "uncertain_speaker",
                    "uncertainSpeaker");
                var conf = ReadDoubleFirst(el, 0, "confidence", "Confidence");
                var notSpeech = ReadBool(el,
                    "not_speech",
                    "notSpeech",
                    "non_spoken_quote",
                    "nonSpokenQuote",
                    "quoted_emphasis_only",
                    "quotedEmphasisOnly");

                if (string.Equals(ck?.Trim(), NotSpeechExternalKeySentinel, StringComparison.Ordinal))
                {
                    notSpeech = true;
                    ck = null;
                }

                assigns.Add(new CharacterAssignmentRow(idx, ck, uncertain, conf, notSpeech));
            }

            return new CharacterAssignmentPhaseParseResult(chars, assigns);
        }
        catch (JsonException ex)
        {
            logger.LogWarning(ex, "Character assignment phase JSON parse failed.");
            return new CharacterAssignmentPhaseParseResult([], []);
        }
    }

    /// <summary>
    /// When the model adds a character profile but forgets to key exactly one spoken line, attach the orphan profile.
    /// Only runs for the unambiguous 1↔1 case.
    /// </summary>
    internal static CharacterAssignmentPhaseParseResult CoalesceBrokenSpeakerAssignments(
        CharacterAssignmentPhaseParseResult parsed,
        ILogger logger)
    {
        var chars = parsed.Characters;
        if (chars.Count == 0 || parsed.Assignments.Count == 0)
            return parsed;

        var rebuilt = parsed.Assignments.ToList();

        var referenced = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var a in rebuilt)
        {
            if (AssignmentHasSpeechSpeakerKey(a))
                referenced.Add(a.CharacterExternalKey!.Trim());
        }

        var unreferencedProfiles = chars
            .Where(c => !string.IsNullOrWhiteSpace(c.ExternalKey) && !referenced.Contains(c.ExternalKey.Trim()))
            .ToList();

        var vacantIndices = new List<int>();
        for (var i = 0; i < rebuilt.Count; i++)
        {
            if (AssignmentSpeechMissingKey(rebuilt[i]))
                vacantIndices.Add(i);
        }

        if (vacantIndices.Count == 1 && unreferencedProfiles.Count == 1)
        {
            var fixKey = unreferencedProfiles[0].ExternalKey.Trim();
            var ix = vacantIndices[0];
            var row = rebuilt[ix];
            rebuilt[ix] = row with { CharacterExternalKey = fixKey };
            logger.LogInformation(
                "Phase 3 repair: assignment order_index {OrderIndex} had empty character_external_key; linked sole unreferenced profile {Key}.",
                row.OrderIndex, fixKey);
        }

        return new CharacterAssignmentPhaseParseResult(chars, rebuilt);
    }

    private static bool AssignmentHasSpeechSpeakerKey(CharacterAssignmentRow a) =>
        !a.NotSpeech && !string.IsNullOrWhiteSpace(a.CharacterExternalKey);

    private static bool AssignmentSpeechMissingKey(CharacterAssignmentRow a) =>
        !a.NotSpeech && string.IsNullOrWhiteSpace(a.CharacterExternalKey);

    internal sealed record ChapterPhaseRow(int Start, int End, string? Title, int? HeadingStart, int? HeadingEnd);

    internal sealed record CharacterAssignmentCharacterRow(
        string ExternalKey,
        string Name,
        IReadOnlyList<string>? Aliases,
        string? PersonalitySummary,
        string? SpeechStyleNotes,
        string GenderPresentation,
        string Tone,
        string Accent,
        string Breathiness,
        string SpeakingPace);

    internal sealed record CharacterAssignmentRow(int OrderIndex, string? CharacterExternalKey, bool Uncertain,
        double Confidence, bool NotSpeech = false);

    internal sealed record CharacterAssignmentPhaseParseResult(
        IReadOnlyList<CharacterAssignmentCharacterRow> Characters,
        IReadOnlyList<CharacterAssignmentRow> Assignments);

    private static readonly HashSet<string> AssignmentArrayPropertyTokens = BuildNormalizedPropertyTokens(
        "assignments",
        "dialogue_assignments",
        "speaker_assignments",
        "utterance_assignments",
        "utterances",
        "speaker_spans",
        "lines",
        "quoted_lines",
        "assignment",
        "speaker_lines",
        "dialogue_spans");

    private static readonly HashSet<string> CharactersArrayPropertyTokens = BuildNormalizedPropertyTokens(
        "characters",
        "inferred_characters",
        "character_profiles",
        "profiles",
        "speakers",
        "speaker_registry",
        "voices");

    private static HashSet<string> BuildNormalizedPropertyTokens(params string[] raw)
    {
        var h = new HashSet<string>(StringComparer.Ordinal);
        foreach (var r in raw)
            h.Add(NormalizeJsonPropertyToken(r));
        return h;
    }

    private static string NormalizeJsonPropertyToken(string name)
    {
        var sb = new System.Text.StringBuilder(name.Length);
        foreach (var c in name)
        {
            if (c is '_' or '-' or ' ') continue;
            sb.Append(char.ToLowerInvariant(c));
        }

        return sb.Length > 0 ? sb.ToString() : name.ToLowerInvariant();
    }

    private static string PrepareCharacterAssignmentJson(string raw)
    {
        var s = raw.Trim();
        if (s.StartsWith("```", StringComparison.Ordinal))
        {
            var nl = s.IndexOf('\n');
            if (nl >= 0)
                s = s[(nl + 1)..].TrimEnd();
            if (s.EndsWith("```", StringComparison.Ordinal))
                s = s[..^3].Trim();
        }

        s = s.Trim();
        for (var depth = 0; depth < 4; depth++)
        {
            try
            {
                using var doc = JsonDocument.Parse(s);
                var r = doc.RootElement;
                if (r.ValueKind == JsonValueKind.String)
                {
                    var inner = r.GetString()?.Trim();
                    if (string.IsNullOrEmpty(inner))
                        break;
                    s = inner;
                    continue;
                }
            }
            catch (JsonException)
            {
                break;
            }

            break;
        }

        var brace = s.IndexOf('{');
        if (brace > 0)
            s = s[brace..];
        var last = s.LastIndexOf('}');
        if (last >= 0 && last < s.Length - 1)
            s = s[..(last + 1)];

        return s.Trim();
    }

    private static bool TryFindAssignmentsArrayRecursive(JsonElement el, out JsonElement arr)
    {
        arr = default;
        if (el.ValueKind == JsonValueKind.Object)
        {
            if (TryPickArrayByPropertyTokens(el, AssignmentArrayPropertyTokens, out arr))
                return true;
            foreach (var p in el.EnumerateObject())
            {
                if (TryFindAssignmentsArrayRecursive(p.Value, out arr))
                    return true;
            }

            return false;
        }

        if (el.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in el.EnumerateArray())
            {
                if (TryFindAssignmentsArrayRecursive(item, out arr))
                    return true;
            }
        }

        return false;
    }

    private static bool TryFindCharactersArrayRecursive(JsonElement el, out JsonElement arr)
    {
        arr = default;
        if (el.ValueKind == JsonValueKind.Object)
        {
            if (TryPickArrayByPropertyTokens(el, CharactersArrayPropertyTokens, out arr))
                return true;
            foreach (var p in el.EnumerateObject())
            {
                if (TryFindCharactersArrayRecursive(p.Value, out arr))
                    return true;
            }

            return false;
        }

        if (el.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in el.EnumerateArray())
            {
                if (TryFindCharactersArrayRecursive(item, out arr))
                    return true;
            }
        }

        return false;
    }

    private static bool TryPickArrayByPropertyTokens(JsonElement obj, HashSet<string> tokens, out JsonElement arr)
    {
        arr = default;
        if (obj.ValueKind != JsonValueKind.Object)
            return false;

        foreach (var p in obj.EnumerateObject())
        {
            if (p.Value.ValueKind != JsonValueKind.Array)
                continue;
            if (!tokens.Contains(NormalizeJsonPropertyToken(p.Name)))
                continue;
            arr = p.Value;
            return true;
        }

        return false;
    }

    private static string? ReadNestedSpeakerOrCharacterExternalKey(JsonElement el)
    {
        if (el.ValueKind != JsonValueKind.Object)
            return null;

        foreach (var nestName in new[] { "speaker", "character", "speaker_profile", "character_profile" })
        {
            foreach (var p in el.EnumerateObject())
            {
                if (!p.Name.Equals(nestName, StringComparison.OrdinalIgnoreCase))
                    continue;
                var nested = p.Value;
                if (nested.ValueKind != JsonValueKind.Object)
                    continue;
                return FirstNonEmpty(
                    ReadString(nested, "external_key"),
                    ReadString(nested, "externalKey"),
                    ReadString(nested, "character_external_key"),
                    ReadString(nested, "characterExternalKey"),
                    ReadString(nested, "slug"),
                    ReadString(nested, "key"),
                    ReadString(nested, "name"),
                    ReadString(nested, "Name"));
            }
        }

        return null;
    }

    private static int ReadInt(JsonElement el, params string[] names)
    {
        foreach (var n in names)
        {
            if (!el.TryGetProperty(n, out var p))
                continue;
            return CoerceInt(p);
        }

        return 0;
    }

    private static int? ReadNullableInt(JsonElement el, params string[] names)
    {
        foreach (var n in names)
        {
            if (!el.TryGetProperty(n, out var p))
                continue;
            return CoerceInt(p);
        }

        return null;
    }

    private static int CoerceInt(JsonElement p) =>
        p.ValueKind switch
        {
            JsonValueKind.Number => p.TryGetInt32(out var i) ? i : (int)Math.Round(p.GetDouble()),
            JsonValueKind.String when int.TryParse(p.GetString(), out var j) => j,
            _ => 0,
        };

    private static double ReadDoubleFirst(JsonElement el, double defaultWhenMissing, params string[] names)
    {
        foreach (var n in names)
        {
            if (!el.TryGetProperty(n, out var p))
                continue;

            switch (p.ValueKind)
            {
                case JsonValueKind.Number when p.TryGetDouble(out var d) && double.IsFinite(d):
                    return d;
                case JsonValueKind.String when double.TryParse(p.GetString(), out var x) && double.IsFinite(x):
                    return x;
            }
        }

        return defaultWhenMissing;
    }

    private static double ReadDouble(JsonElement el, string name, double defaultWhenMissing = 0)
    {
        if (!el.TryGetProperty(name, out var p))
            return defaultWhenMissing;
        return p.ValueKind switch
        {
            JsonValueKind.Number => p.TryGetDouble(out var d) ? d : 0,
            JsonValueKind.String when double.TryParse(p.GetString(), out var x) => x,
            _ => 0,
        };
    }

    private static bool ReadBool(JsonElement el, params string[] names)
    {
        foreach (var n in names)
        {
            if (!el.TryGetProperty(n, out var p))
                continue;
            return p.ValueKind switch
            {
                JsonValueKind.True => true,
                JsonValueKind.False => false,
                JsonValueKind.String => bool.TryParse(p.GetString(), out var b) && b,
                JsonValueKind.Number => p.GetInt32() != 0,
                _ => false,
            };
        }

        return false;
    }

    private static string? ReadString(JsonElement el, string name) =>
        el.TryGetProperty(name, out var p) && p.ValueKind == JsonValueKind.String ? p.GetString() : null;

    private static List<string>? ReadStringList(JsonElement el, string name)
    {
        if (!el.TryGetProperty(name, out var p) || p.ValueKind != JsonValueKind.Array)
            return null;

        var list = new List<string>();
        foreach (var item in p.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.String)
                continue;
            var s = item.GetString()?.Trim();
            if (!string.IsNullOrEmpty(s))
                list.Add(s);
        }

        return list.Count > 0 ? list : null;
    }

    private static string? FirstNonEmpty(params string?[] values)
    {
        foreach (var v in values)
        {
            if (!string.IsNullOrWhiteSpace(v))
                return v.Trim();
        }

        return null;
    }

    private static bool TryNormalizeSpan(int rawStart, int rawEnd, int maxLen, out int start, out int end)
    {
        var a = rawStart;
        var b = rawEnd;
        if (a > b)
            (a, b) = (b, a);

        start = Math.Clamp(a, 0, Math.Max(0, maxLen));
        end = Math.Clamp(b, 0, Math.Max(0, maxLen));
        if (end <= start && maxLen > 0 && start < maxLen)
            end = Math.Min(start + 1, maxLen);

        return end > start;
    }
}
