using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using SmartNarrator.Application.Analysis;
using SmartNarrator.Domain.Enums;

namespace SmartNarrator.Infrastructure.Ai;

internal static class StoryAnalysisEnvelopeParser
{
    private static readonly HashSet<string> WrapPropertyNames =
        new(StringComparer.OrdinalIgnoreCase)
        {
            "analysis",
            "result",
            "response",
            "data",
            "output",
            "story",
            "timeline",
            "choices",
            "message",
        };

    private static string Clamp(string? s, int max = 512) =>
        string.IsNullOrWhiteSpace(s) ? string.Empty : s.Trim().Length > max ? s.Trim()[..max] : s.Trim();

    private static string? ClampMultiline(string? s, int max = 12000)
    {
        if (string.IsNullOrWhiteSpace(s))
            return null;

        var t = s.Trim();
        return t.Length > max ? t[..max] : t;
    }

    private static string? FirstNonEmptyMultiline(params string?[] values)
    {
        foreach (var v in values)
        {
            if (!string.IsNullOrWhiteSpace(v))
                return v;
        }

        return null;
    }

    private static bool IsSpeakerUncertainFlag(UtteranceDto u)
    {
        if (u.SpeakerUncertain == true || u.UncertainSpeaker == true)
            return true;

        var ac = (u.AttributionConfidence ?? "").Trim().ToLowerInvariant();
        return ac is "low" or "uncertain" or "unknown" or "ambiguous";
    }
    private static JsonSerializerOptions CreateSerializerOptions(JsonNamingPolicy namingPolicy) =>
        new()
        {
            PropertyNameCaseInsensitive = true,
            PropertyNamingPolicy = namingPolicy,
            NumberHandling = JsonNumberHandling.AllowReadingFromString | JsonNumberHandling.AllowNamedFloatingPointLiterals,
            ReadCommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true,
        };

    private static readonly JsonSerializerOptions SnakeOptions = CreateSerializerOptions(JsonNamingPolicy.SnakeCaseLower);
    private static readonly JsonSerializerOptions CamelOptions = CreateSerializerOptions(JsonNamingPolicy.CamelCase);

    /// <summary>
    /// Naming-policy-neutral fallback — models often emit PascalCase (<c>Characters</c>) or mixed keys that snake/camel policies skip.
    /// </summary>
    private static readonly JsonSerializerOptions RelaxedOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        NumberHandling = JsonNumberHandling.AllowReadingFromString | JsonNumberHandling.AllowNamedFloatingPointLiterals,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    internal static StoryAnalysisResultDto Parse(string json, int excerptLength, ILogger logger)
    {
        var payload = TryPeelWrappedStoryAnalysis(TryUnwrapJsonStringValue(PrepareJsonPayload(json)), logger);

        var root = DeserializeRootFlexible(payload, logger);

        var characters = new List<CharacterSuggestionDto>();
        foreach (var c in root.Characters ?? [])
        {
            var externalKey = ResolveCharacterExternalKey(c);
            if (string.IsNullOrWhiteSpace(externalKey))
            {
                logger.LogWarning("Skipping character row with no usable external_key/name.");
                continue;
            }

            var name = string.IsNullOrWhiteSpace(c.Name) ? externalKey : Clamp(c.Name, 240);

            characters.Add(new CharacterSuggestionDto
            {
                ExternalKey = externalKey,
                Name = name,
                Aliases = c.Aliases,
                PersonalitySummary =
                    ClampMultiline(FirstNonEmptyMultiline(c.PersonalitySummary, c.Personality_profile)),
                SpeechStyleSummary =
                    ClampMultiline(FirstNonEmptyMultiline(c.SpeechStyleNotes, c.SpeechStyleSummary)),
                GenderPresentation =
                    Clamp(string.IsNullOrWhiteSpace(c.GenderPresentation) ? "unspecified" : c.GenderPresentation, 96),
                Tone = Clamp(string.IsNullOrWhiteSpace(c.Tone) ? "neutral" : c.Tone, 96),
                Accent = Clamp(string.IsNullOrWhiteSpace(c.Accent) ? "none" : c.Accent, 96),
                Breathiness = Clamp(string.IsNullOrWhiteSpace(c.Breathiness) ? "normal" : c.Breathiness, 96),
                SpeakingPace = Clamp(string.IsNullOrWhiteSpace(c.SpeakingPace) ? "normal" : c.SpeakingPace, 96),
            });
        }

        var utterances = new List<UtteranceSuggestionDto>();
        foreach (var u in root.Utterances ?? [])
        {
            var rawStart = u.Start ?? u.StartOffset;
            var rawEnd = u.End ?? u.EndOffset;
            if (!TryNormalizeSpan(rawStart, rawEnd, excerptLength, logger, "Utterance", out var start, out var end))
            {
                logger.LogWarning(
                    "Skipping utterance span raw {RawStart}-{RawEnd} (excerpt UTF-16 length {Len}).",
                    rawStart,
                    rawEnd,
                    excerptLength);
                continue;
            }

            var kindStr = ResolveUtteranceSpeakerKind(u.SpeakerKind);
            var isCharacterKind = kindStr == nameof(SpeakerKind.Character);

            var uncertain = IsSpeakerUncertainFlag(u);
            var charKey = ResolveUtteranceCharacterKey(u);
            if (isCharacterKind)
            {
                if (uncertain || string.IsNullOrWhiteSpace(charKey))
                {
                    if (!uncertain && string.IsNullOrWhiteSpace(charKey))
                    {
                        logger.LogWarning(
                            "Utterance Character missing character_external_key (model did not flag uncertainty); leaving unattributed for human review.");
                    }

                    charKey = null;
                }
            }
            else
            {
                charKey = null;
            }

            utterances.Add(new UtteranceSuggestionDto
            {
                StartOffset = start,
                EndOffset = end,
                SpeakerKind = kindStr,
                CharacterExternalKey = string.IsNullOrWhiteSpace(charKey) ? null : Clamp(charKey, 120),
                Confidence = NormalizeConfidence(u.Confidence),
            });
        }

        var passages = new List<NarrativePassageSuggestionDto>();
        foreach (var p in root.NarrativePassages ?? [])
        {
            var rawStart = p.Start ?? p.StartOffset;
            var rawEnd = p.End ?? p.EndOffset;
            if (!TryNormalizeSpan(rawStart, rawEnd, excerptLength, logger, "Narrative passage", out var start, out var end))
            {
                logger.LogWarning(
                    "Skipping narrative passage span raw {RawStart}-{RawEnd} (excerpt UTF-16 length {Len}).",
                    rawStart,
                    rawEnd,
                    excerptLength);
                continue;
            }

            var narratorKey = ResolveNarratorCharacterKey(p);
            passages.Add(new NarrativePassageSuggestionDto
            {
                StartOffset = start,
                EndOffset = end,
                NarratorCharacterExternalKey =
                    string.IsNullOrWhiteSpace(narratorKey) ? null : Clamp(narratorKey, 120),
                PerspectiveNotes =
                    Clamp(string.IsNullOrWhiteSpace(p.PerspectiveNotes) ? string.Empty : p.PerspectiveNotes, 1024),
                GenderPresentation =
                    Clamp(string.IsNullOrWhiteSpace(p.GenderPresentation) ? "unspecified" : p.GenderPresentation),
                Tone = Clamp(string.IsNullOrWhiteSpace(p.Tone) ? "neutral" : p.Tone),
                Accent = Clamp(string.IsNullOrWhiteSpace(p.Accent) ? "none" : p.Accent),
                Breathiness = Clamp(string.IsNullOrWhiteSpace(p.Breathiness) ? "normal" : p.Breathiness),
                SpeakingPace = Clamp(string.IsNullOrWhiteSpace(p.SpeakingPace) ? "normal" : p.SpeakingPace),
            });
        }

        var sections = new List<StoryStructureSectionSuggestionDto>();
        foreach (var s in root.Sections ?? [])
        {
            var rawStart = s.Start ?? s.StartOffset;
            var rawEnd = s.End ?? s.EndOffset;
            if (!TryNormalizeSpan(rawStart, rawEnd, excerptLength, logger, "Structure section", out var start, out var end))
            {
                logger.LogWarning(
                    "Skipping structure section span raw {RawStart}-{RawEnd} (excerpt UTF-16 length {Len}).",
                    rawStart,
                    rawEnd,
                    excerptLength);
                continue;
            }

            sections.Add(new StoryStructureSectionSuggestionDto
            {
                StartOffset = start,
                EndOffset = end,
                Kind = MapSectionKind(s.Kind),
                Title = string.IsNullOrWhiteSpace(s.Title) ? null : Clamp(s.Title, 512),
                Notes = Clamp(string.IsNullOrWhiteSpace(s.Notes) ? string.Empty : s.Notes, 2048),
            });
        }

        AddPlaceholderCharactersForReferencedKeys(characters, utterances, passages, logger);

        logger.LogInformation(
            "Parsed story analysis: characters={Ch}, utterances={Ut}, narratives={Na}, sections={Se}",
            characters.Count, utterances.Count, passages.Count, sections.Count);

        if (characters.Count == 0 && utterances.Count == 0 && passages.Count == 0 && sections.Count == 0)
        {
            logger.LogWarning(
                "Analysis JSON contained no usable rows after mapping (model raw arrays: characters≈{Rc}, utterances≈{Ru}, passages≈{Rp}, sections≈{Rs}).",
                root.Characters?.Count ?? 0,
                root.Utterances?.Count ?? 0,
                root.NarrativePassages?.Count ?? 0,
                root.Sections?.Count ?? 0);
            LogPayloadDiagnostics(payload, logger);
        }

        return new StoryAnalysisResultDto
        {
            Characters = characters,
            Utterances = utterances,
            NarrativePassages = passages,
            StructureSections = sections,
        };
    }

    private static double NormalizeConfidence(double c) =>
        double.IsFinite(c) ? Math.Clamp(c, 0, 1) : 0;

    private static RootDto DeserializeRootFlexible(string payload, ILogger logger)
    {
        RootDto? TryOpts(JsonSerializerOptions options, string label)
        {
            try
            {
                return JsonSerializer.Deserialize<RootDto>(payload, options);
            }
            catch (JsonException ex)
            {
                logger.LogTrace(ex, "Analysis JSON deserialize failed ({Label}).", label);
                return null;
            }
        }

        RootDto? best = null;
        foreach (var (opts, label) in new (JsonSerializerOptions opts, string label)[]
                 {
                     (SnakeOptions, "snake_case"),
                     (CamelOptions, "camelCase"),
                     (RelaxedOptions, "relaxed"),
                 })
        {
            var candidate = TryOpts(opts, label);
            if (candidate is null)
                continue;

            best ??= candidate;
            if (!IsEffectivelyEmpty(candidate))
            {
                logger.LogInformation("Parsed story analysis envelope using JSON naming mode '{Label}'.", label);
                return candidate;
            }
        }

        return best ?? throw new InvalidOperationException("Could not deserialize analysis JSON.");
    }

    /// <summary>
    /// Some stacks double-encode assistant JSON as a JSON string literal.
    /// </summary>
    private static string TryUnwrapJsonStringValue(string payload)
    {
        var t = payload.TrimStart();
        if (!t.StartsWith('"'))
            return payload;

        try
        {
            var inner = JsonSerializer.Deserialize<string>(t);
            return string.IsNullOrWhiteSpace(inner) ? payload : PrepareJsonPayload(inner);
        }
        catch (JsonException)
        {
            return payload;
        }
    }

    private static void LogPayloadDiagnostics(string payload, ILogger logger)
    {
        try
        {
            using var doc = JsonDocument.Parse(payload);
            var root = doc.RootElement;
            if (root.ValueKind == JsonValueKind.Object)
            {
                var keys = string.Join(", ", root.EnumerateObject().Select(p => p.Name));
                logger.LogWarning("Story analysis JSON root keys: {Keys}", keys);
            }
            else
                logger.LogWarning("Story analysis JSON root value kind: {Kind}", root.ValueKind);

            var excerpt = payload.Length > 1600 ? $"{payload[..1600]}…" : payload;
            logger.LogWarning("Story analysis payload excerpt:\n{Excerpt}", excerpt);
        }
        catch (JsonException ex)
        {
            logger.LogWarning(ex, "Story analysis payload is not valid JSON.");
        }
    }

    private static int ClampOffset(int value, int maxLen) =>
        maxLen <= 0 ? 0 : value < 0 ? 0 : value > maxLen ? maxLen : value;

    /// <summary>
    /// Swap inverted ranges; salvage zero-length spans as a single UTF-16 unit when possible (models often confuse offsets).
    /// </summary>
    private static bool TryNormalizeSpan(
        int rawStart,
        int rawEnd,
        int excerptLength,
        ILogger logger,
        string label,
        out int start,
        out int end)
    {
        var a = rawStart;
        var b = rawEnd;
        if (a > b)
            (a, b) = (b, a);

        start = ClampOffset(a, excerptLength);
        end = ClampOffset(b, excerptLength);

        if (end <= start && excerptLength > 0 && start < excerptLength)
        {
            logger.LogWarning(
                "{Label}: degenerate span after clamp ({Start}-{End}); extending end by one UTF-16 unit.",
                label,
                start,
                end);
            end = Math.Min(start + 1, excerptLength);
        }

        return end > start;
    }

    /// <summary>
    /// Models often omit <c>characters[]</c> or only list names; utterances still carry stable keys.
    /// Ensure every referenced key exists so <see cref="StoryAnalysisResultApplier"/> can persist profiles.
    /// </summary>
    private static void AddPlaceholderCharactersForReferencedKeys(List<CharacterSuggestionDto> characters,
        List<UtteranceSuggestionDto> utterances,
        List<NarrativePassageSuggestionDto> passages,
        ILogger logger)
    {
        var known =
            new Dictionary<string, CharacterSuggestionDto>(StringComparer.OrdinalIgnoreCase);
        foreach (var c in characters)
        {
            if (string.IsNullOrWhiteSpace(c.ExternalKey))
                continue;
            known[c.ExternalKey] = c;
        }

        void EnsureFromReference(string? keyRaw)
        {
            if (string.IsNullOrWhiteSpace(keyRaw))
                return;

            var key = Clamp(keyRaw, 120);
            if (known.ContainsKey(key))
                return;

            logger.LogInformation("Synthesizing character profile placeholder for referenced key '{Key}'.", key);
            var placeholder = new CharacterSuggestionDto
            {
                ExternalKey = key,
                Name = key,
                Aliases = null,
                PersonalitySummary = null,
                SpeechStyleSummary = null,
                GenderPresentation = "unspecified",
                Tone = "neutral",
                Accent = "none",
                Breathiness = "normal",
                SpeakingPace = "normal",
            };

            characters.Add(placeholder);
            known[key] = placeholder;
        }

        foreach (var u in utterances)
            EnsureFromReference(u.CharacterExternalKey);

        foreach (var p in passages)
            EnsureFromReference(p.NarratorCharacterExternalKey);
    }

    private static string? ResolveCharacterExternalKey(CharacterDto c)
    {
        foreach (var raw in new[]
                 {
                     c.ExternalKey,
                     c.Key,
                     c.Slug,
                     c.CharacterId,
                     c.Id,
                 })
        {
            var s = Clamp(raw, 120);
            if (!string.IsNullOrWhiteSpace(s))
                return s;
        }

        return string.IsNullOrWhiteSpace(c.Name) ? null : Clamp(c.Name, 120);
    }

    /// <summary>
    /// Maps model-specific speaker labels to persisted <see cref="SpeakerKind"/> names (PascalCase).
    /// </summary>
    private static string ResolveUtteranceSpeakerKind(string? raw)
    {
        var s = Clamp(raw, 96).Trim();
        if (string.IsNullOrEmpty(s))
            return nameof(SpeakerKind.Character);

        if (Enum.TryParse<SpeakerKind>(s, ignoreCase: true, out var parsed))
            return parsed.ToString();

        var compact = s.Replace("_", "", StringComparison.Ordinal).Replace("-", "", StringComparison.Ordinal);
        if (Enum.TryParse<SpeakerKind>(compact, ignoreCase: true, out parsed))
            return parsed.ToString();

        var su = s.ToUpperInvariant().Replace("_", "").Replace("-", "");
        if (su.Contains("NARR", StringComparison.Ordinal))
            return nameof(SpeakerKind.Narrator);

        if (su.Contains("QUOTEDNONSPEECH", StringComparison.Ordinal)
            || su.Contains("NONSPEECH", StringComparison.Ordinal)
            || su.Contains("NOTSPEECH", StringComparison.Ordinal)
            || su.Contains("EMPHASISONLY", StringComparison.Ordinal)
            || (su.Contains("EMPHASIS", StringComparison.Ordinal) && su.Contains("QUOTE", StringComparison.Ordinal)))
            return nameof(SpeakerKind.QuotedNonSpeech);

        return nameof(SpeakerKind.Character);
    }

    private static string? ResolveUtteranceCharacterKey(UtteranceDto u) =>
        FirstNonEmpty(
            u.CharacterExternalKey,
            u.CharacterKey,
            u.SpeakerExternalKey);

    private static string? ResolveNarratorCharacterKey(NarrativePassageDto p) =>
        FirstNonEmpty(
            p.NarratorCharacterExternalKey,
            p.NarratorKey,
            p.NarratorExternalKey);

    private static string? FirstNonEmpty(params string?[] values)
    {
        foreach (var v in values)
        {
            var s = Clamp(v, 120);
            if (!string.IsNullOrWhiteSpace(s))
                return s;
        }

        return null;
    }

    private static StoryStructureSectionKind MapSectionKind(string? raw)
    {
        var k = (raw ?? "").Trim().ToLowerInvariant().Replace('-', '_').Replace(' ', '_');
        return k switch
        {
            "chapter" or "chapter_heading" or "chapter_break" or "chapterheading" => StoryStructureSectionKind.Chapter,
            "perspective_shift" or "perspective" or "pov" or "perspectiveshift" => StoryStructureSectionKind.PerspectiveShift,
            "scene_break" or "scene" or "break" or "scenebreak" => StoryStructureSectionKind.SceneBreak,
            _ => StoryStructureSectionKind.Other,
        };
    }

    private static bool IsEffectivelyEmpty(RootDto? root) =>
        root is null
        || ((root.Characters is null || root.Characters.Count == 0)
            && (root.Utterances is null || root.Utterances.Count == 0)
            && (root.NarrativePassages is null || root.NarrativePassages.Count == 0)
            && (root.Sections is null || root.Sections.Count == 0));

    /// <summary>
    /// Some models wrap the schema in <c>{ "response": { ... } }</c>, a JSON string, or a single-element array.
    /// Peel those layers so we deserialize our <see cref="RootDto"/>.
    /// </summary>
    private static string TryPeelWrappedStoryAnalysis(string payload, ILogger logger)
    {
        try
        {
            using var doc = JsonDocument.Parse(payload);
            var root = doc.RootElement;
            if (JsonObjectLooksLikeStoryAnalysis(root))
                return payload;

            if (root.ValueKind == JsonValueKind.Array && root.GetArrayLength() == 1)
            {
                var only = root[0];
                if (only.ValueKind == JsonValueKind.Object && JsonObjectLooksLikeStoryAnalysis(only))
                {
                    logger.LogInformation("Unwrapped story analysis JSON from single-element root array.");
                    return only.GetRawText();
                }
            }

            if (root.ValueKind != JsonValueKind.Object)
                return payload;

            foreach (var prop in root.EnumerateObject())
            {
                var wrap = prop.Name;
                if (!WrapPropertyNames.Contains(wrap))
                    continue;

                var inner = prop.Value;

                if (inner.ValueKind == JsonValueKind.Object && JsonObjectLooksLikeStoryAnalysis(inner))
                {
                    logger.LogInformation("Unwrapped story analysis JSON from nested property '{Prop}'.", wrap);
                    return inner.GetRawText();
                }

                if (inner.ValueKind != JsonValueKind.String)
                    continue;

                var txt = inner.GetString();
                if (string.IsNullOrWhiteSpace(txt))
                    continue;

                var innerPayload = PrepareJsonPayload(txt);
                try
                {
                    using var innerDoc = JsonDocument.Parse(innerPayload);
                    if (JsonObjectLooksLikeStoryAnalysis(innerDoc.RootElement))
                    {
                        logger.LogInformation(
                            "Unwrapped story analysis JSON from nested string property '{Prop}'.", wrap);
                        return innerPayload;
                    }
                }
                catch (JsonException)
                {
                    /* try next wrapper */
                }
            }
        }
        catch (JsonException ex)
        {
            logger.LogTrace(ex, "Nested unwrap skipped; using flat payload.");
        }

        return payload;
    }

    private static bool JsonObjectLooksLikeStoryAnalysis(JsonElement obj)
    {
        if (obj.ValueKind != JsonValueKind.Object)
            return false;

        foreach (var prop in obj.EnumerateObject())
        {
            var n = prop.Name.Replace("_", "", StringComparison.Ordinal).ToLowerInvariant();
            if (n is "characters" or "utterances" or "narrativepassages" or "sections")
                return true;
        }

        return false;
    }

    /// <summary>Strips markdown fences and stray prose around the JSON object.</summary>
    private static string PrepareJsonPayload(string raw)
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

        // Root arrays like [{ "characters": [...] }] — slicing from first '{' leaves invalid JSON (`{...}]`).
        if (s.StartsWith('['))
        {
            try
            {
                using var doc = JsonDocument.Parse(s);
                if (doc.RootElement.ValueKind == JsonValueKind.Array && doc.RootElement.GetArrayLength() > 0)
                {
                    var first = doc.RootElement[0];
                    if (first.ValueKind == JsonValueKind.Object)
                        s = first.GetRawText();
                    else if (first.ValueKind == JsonValueKind.String)
                    {
                        var inner = first.GetString();
                        if (!string.IsNullOrWhiteSpace(inner))
                            return PrepareJsonPayload(inner);
                    }
                }
            }
            catch (JsonException)
            {
                /* fall through */
            }
        }

        var brace = s.IndexOf('{');
        if (brace > 0)
            s = s[brace..];

        var lastBrace = s.LastIndexOf('}');
        if (lastBrace >= 0 && lastBrace < s.Length - 1)
            s = s[..(lastBrace + 1)];

        return s.Trim();
    }

    private sealed class RootDto
    {
        public List<CharacterDto>? Characters { get; set; }
        public List<UtteranceDto>? Utterances { get; set; }
        public List<NarrativePassageDto>? NarrativePassages { get; set; }
        public List<SectionDto>? Sections { get; set; }
    }

    private sealed class CharacterDto
    {
        public string? ExternalKey { get; set; }
        /// <summary>Alternate slug many models emit instead of <c>external_key</c>.</summary>
        public string? Key { get; set; }
        public string? Slug { get; set; }
        public string? CharacterId { get; set; }
        public string? Id { get; set; }
        public string? Name { get; set; }
        public List<string>? Aliases { get; set; }
        public string? GenderPresentation { get; set; }
        public string? Tone { get; set; }
        public string? Accent { get; set; }
        public string? Breathiness { get; set; }
        public string? SpeakingPace { get; set; }
        public string? PersonalitySummary { get; set; }
        /// <summary>Alternate snake_case naming.</summary>
        public string? Personality_profile { get; set; }
        /// <summary>Maps JSON <c>speech_style_notes</c>; keep a single property so snake_case deserialization does not collide.</summary>
        public string? SpeechStyleNotes { get; set; }
        public string? SpeechStyleSummary { get; set; }
    }

    private sealed class UtteranceDto
    {
        public int StartOffset { get; set; }
        public int EndOffset { get; set; }
        /// <summary>Alternate keys (<c>start</c>/<c>startIdx</c>) used by some models.</summary>
        public int? Start { get; set; }
        public int? End { get; set; }
        public string? SpeakerKind { get; set; }
        public string? CharacterExternalKey { get; set; }
        public string? CharacterKey { get; set; }
        public string? SpeakerExternalKey { get; set; }
        public double Confidence { get; set; }
        public bool? SpeakerUncertain { get; set; }
        public bool? UncertainSpeaker { get; set; }
        /// <summary>Some models emit "low" instead of booleans.</summary>
        public string? AttributionConfidence { get; set; }
    }

    private sealed class NarrativePassageDto
    {
        public int StartOffset { get; set; }
        public int EndOffset { get; set; }
        public int? Start { get; set; }
        public int? End { get; set; }
        public string? NarratorCharacterExternalKey { get; set; }
        public string? NarratorKey { get; set; }
        public string? NarratorExternalKey { get; set; }

        public string? PerspectiveNotes { get; set; }
        public string? GenderPresentation { get; set; }
        public string? Tone { get; set; }
        public string? Accent { get; set; }
        public string? Breathiness { get; set; }
        public string? SpeakingPace { get; set; }
    }

    private sealed class SectionDto
    {
        public int StartOffset { get; set; }
        public int EndOffset { get; set; }
        public int? Start { get; set; }
        public int? End { get; set; }
        public string? Kind { get; set; }
        public string? Title { get; set; }
        public string? Notes { get; set; }
    }
}
