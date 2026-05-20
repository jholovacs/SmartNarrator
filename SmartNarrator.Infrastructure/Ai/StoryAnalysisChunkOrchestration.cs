using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using SmartNarrator.Application.Analysis;

namespace SmartNarrator.Infrastructure.Ai;

internal static class StoryAnalysisChunkPlanner
{
    internal static List<(int StartUtf16, int LengthUtf16)> Plan(int totalUtf16Length, int chunkSizeUtf16, int overlapUtf16)
    {
        var chunk = Math.Max(2048, chunkSizeUtf16);
        var overlap = Math.Clamp(overlapUtf16, 0, chunk / 2);
        if (totalUtf16Length <= 0)
            return [];

        if (totalUtf16Length <= chunk)
            return [(0, totalUtf16Length)];

        var ranges = new List<(int StartUtf16, int LengthUtf16)>();
        var pos = 0;
        while (pos < totalUtf16Length)
        {
            var len = Math.Min(chunk, totalUtf16Length - pos);
            ranges.Add((pos, len));
            if (pos + len >= totalUtf16Length)
                break;

            pos += chunk - overlap;
        }

        return ranges;
    }

    internal static List<SegmentBoundaryDto> ClipSegmentsToChunk(
        IReadOnlyList<SegmentBoundaryDto> segments,
        int chunkGlobalStart,
        int excerptLen)
    {
        var chunkEnd = chunkGlobalStart + excerptLen;
        var list = new List<SegmentBoundaryDto>();
        foreach (var s in segments)
        {
            if (s.EndOffset <= chunkGlobalStart || s.StartOffset >= chunkEnd)
                continue;

            var rs = Math.Max(0, s.StartOffset - chunkGlobalStart);
            var re = Math.Min(excerptLen, s.EndOffset - chunkGlobalStart);
            if (re > rs)
                list.Add(new SegmentBoundaryDto(s.Index, rs, re));
        }

        return list;
    }

    /// <summary>
    /// UTF-16 start of the paragraph immediately before <paramref name="chunkStartUtf16"/> using blank-line boundaries (<c>\n\n</c>),
    /// or <c>null</c> if no prior paragraph boundary exists (caller should not extend context).
    /// </summary>
    internal static int? TryStartOfPreviousParagraphNewlines(string text, int chunkStartUtf16)
    {
        if (chunkStartUtf16 <= 0 || chunkStartUtf16 > text.Length)
            return null;

        ReadOnlySpan<char> before = text.AsSpan(0, chunkStartUtf16);
        var lastSep = before.LastIndexOf("\n\n".AsSpan(), StringComparison.Ordinal);
        if (lastSep < 0)
            return null;

        ReadOnlySpan<char> prevSpan = before[..lastSep];
        var prevSep = prevSpan.LastIndexOf("\n\n".AsSpan(), StringComparison.Ordinal);
        var paraStart = prevSep < 0 ? 0 : prevSep + 2;
        return paraStart < chunkStartUtf16 ? paraStart : null;
    }

    /// <summary>
    /// Pulls excerpt start backward to include the previous paragraph (newline blocks) and/or previous ingest segment when possible.
    /// </summary>
    internal static int ExcerptStartWithPriorParagraphContext(
        string canonicalUtf16,
        IReadOnlyList<SegmentBoundaryDto> segments,
        int plannedChunkStartUtf16,
        int maxBackUtf16)
    {
        if (plannedChunkStartUtf16 <= 0)
            return 0;

        var floor = Math.Max(0, plannedChunkStartUtf16 - Math.Max(512, maxBackUtf16));
        var excerptStart = plannedChunkStartUtf16;

        if (TryStartOfPreviousParagraphNewlines(canonicalUtf16, plannedChunkStartUtf16) is { } nlStart &&
            nlStart >= floor &&
            nlStart < excerptStart)
        {
            excerptStart = nlStart;
        }

        var orderedSegs = segments.OrderBy(s => s.StartOffset).ToList();
        var idx = orderedSegs.FindIndex(s =>
            plannedChunkStartUtf16 >= s.StartOffset && plannedChunkStartUtf16 < s.EndOffset);
        if (idx > 0)
        {
            var prevSegStart = orderedSegs[idx - 1].StartOffset;
            if (prevSegStart >= floor && prevSegStart < excerptStart)
                excerptStart = prevSegStart;
        }

        return Math.Max(floor, excerptStart);
    }
}

internal static class StoryAnalysisPriorRegistryFormatter
{
    private static readonly JsonSerializerOptions Options =
        new()
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = false,
        };

    internal static string ToJson(IReadOnlyCollection<CharacterSuggestionDto> characters)
    {
        var ordered = characters.OrderBy(c => c.ExternalKey, StringComparer.OrdinalIgnoreCase).ToList();
        return JsonSerializer.Serialize(ordered, Options);
    }
}

internal static class StoryAnalysisAccumulator
{
    internal static void MergeCharacters(
        IDictionary<string, CharacterSuggestionDto> acc,
        IEnumerable<CharacterSuggestionDto> incoming)
    {
        foreach (var c in incoming)
        {
            var rawKey = c.ExternalKey.Trim();
            if (string.IsNullOrWhiteSpace(rawKey))
                continue;

            if (!acc.TryGetValue(rawKey, out var existing))
            {
                acc[rawKey] = new CharacterSuggestionDto
                {
                    ExternalKey = rawKey,
                    Name = c.Name,
                    Aliases = c.Aliases,
                    PersonalitySummary = c.PersonalitySummary,
                    SpeechStyleSummary = c.SpeechStyleSummary,
                    GenderPresentation = c.GenderPresentation,
                    Tone = c.Tone,
                    Accent = c.Accent,
                    Breathiness = c.Breathiness,
                    SpeakingPace = c.SpeakingPace,
                };
                continue;
            }

            acc[existing.ExternalKey] = MergeCharacter(existing, c);
        }
    }

    internal static CharacterSuggestionDto MergeCharacter(CharacterSuggestionDto a, CharacterSuggestionDto b)
    {
        var aliases = MergeAliases(a.Aliases, b.Aliases);
        return new CharacterSuggestionDto
        {
            ExternalKey = a.ExternalKey,
            Name = PreferNonEmpty(b.Name, a.Name),
            Aliases = aliases,
            PersonalitySummary = CombineNotes(a.PersonalitySummary, b.PersonalitySummary),
            SpeechStyleSummary = CombineNotes(a.SpeechStyleSummary, b.SpeechStyleSummary),
            GenderPresentation = PreferConcreteGender(b.GenderPresentation, a.GenderPresentation),
            Tone = PreferInterestingVoiceLabel(b.Tone, a.Tone),
            Accent = PreferInterestingVoiceLabel(b.Accent, a.Accent),
            Breathiness = PreferInterestingVoiceLabel(b.Breathiness, a.Breathiness),
            SpeakingPace = PreferInterestingVoiceLabel(b.SpeakingPace, a.SpeakingPace),
        };
    }

    internal static List<UtteranceSuggestionDto> ShiftUtterances(
        IEnumerable<UtteranceSuggestionDto> chunk,
        int globalDeltaUtf16)
    {
        var list = new List<UtteranceSuggestionDto>();
        foreach (var u in chunk)
        {
            list.Add(new UtteranceSuggestionDto
            {
                StartOffset = u.StartOffset + globalDeltaUtf16,
                EndOffset = u.EndOffset + globalDeltaUtf16,
                SpeakerKind = u.SpeakerKind,
                CharacterExternalKey = u.CharacterExternalKey,
                Confidence = u.Confidence,
            });
        }

        return list;
    }

    internal static List<NarrativePassageSuggestionDto> ShiftNarratives(
        IEnumerable<NarrativePassageSuggestionDto> chunk,
        int globalDeltaUtf16)
    {
        var list = new List<NarrativePassageSuggestionDto>();
        foreach (var p in chunk)
        {
            list.Add(new NarrativePassageSuggestionDto
            {
                StartOffset = p.StartOffset + globalDeltaUtf16,
                EndOffset = p.EndOffset + globalDeltaUtf16,
                NarratorCharacterExternalKey = p.NarratorCharacterExternalKey,
                PerspectiveNotes = p.PerspectiveNotes,
                GenderPresentation = p.GenderPresentation,
                Tone = p.Tone,
                Accent = p.Accent,
                Breathiness = p.Breathiness,
                SpeakingPace = p.SpeakingPace,
            });
        }

        return list;
    }

    internal static List<StoryStructureSectionSuggestionDto> ShiftSections(
        IEnumerable<StoryStructureSectionSuggestionDto> chunk,
        int globalDeltaUtf16)
    {
        var list = new List<StoryStructureSectionSuggestionDto>();
        foreach (var s in chunk)
        {
            list.Add(new StoryStructureSectionSuggestionDto
            {
                StartOffset = s.StartOffset + globalDeltaUtf16,
                EndOffset = s.EndOffset + globalDeltaUtf16,
                Kind = s.Kind,
                Title = s.Title,
                Notes = s.Notes,
            });
        }

        return list;
    }

    internal static List<UtteranceSuggestionDto> DedupeUtterances(List<UtteranceSuggestionDto> raw)
    {
        var sorted = raw.OrderBy(u => u.StartOffset).ThenBy(u => u.EndOffset).ToList();
        var result = new List<UtteranceSuggestionDto>();
        foreach (var u in sorted)
        {
            var dupIdx = result.FindIndex(r => r.StartOffset == u.StartOffset && r.EndOffset == u.EndOffset);
            if (dupIdx >= 0)
            {
                if (u.Confidence > result[dupIdx].Confidence)
                    result[dupIdx] = u;

                continue;
            }

            result.Add(u);
        }

        return result;
    }

    internal static List<NarrativePassageSuggestionDto> DedupeNarratives(List<NarrativePassageSuggestionDto> raw)
    {
        var sorted = raw.OrderBy(p => p.StartOffset).ThenBy(p => p.EndOffset).ToList();
        var result = new List<NarrativePassageSuggestionDto>();
        foreach (var p in sorted)
        {
            var dupIdx = result.FindIndex(r =>
                r.StartOffset == p.StartOffset && r.EndOffset == p.EndOffset);
            if (dupIdx >= 0)
            {
                if ((p.PerspectiveNotes?.Length ?? 0) > (result[dupIdx].PerspectiveNotes?.Length ?? 0))
                    result[dupIdx] = p;

                continue;
            }

            result.Add(p);
        }

        return result;
    }

    internal static List<StoryStructureSectionSuggestionDto> DedupeSections(List<StoryStructureSectionSuggestionDto> raw)
    {
        var sorted = raw.OrderBy(s => s.StartOffset).ThenBy(s => s.EndOffset).ToList();
        var result = new List<StoryStructureSectionSuggestionDto>();
        foreach (var s in sorted)
        {
            var dupIdx = result.FindIndex(r =>
                r.StartOffset == s.StartOffset &&
                r.EndOffset == s.EndOffset &&
                r.Kind == s.Kind);
            if (dupIdx >= 0)
            {
                if ((s.Notes?.Length ?? 0) > (result[dupIdx].Notes?.Length ?? 0))
                    result[dupIdx] = s;

                continue;
            }

            result.Add(s);
        }

        return result;
    }

    private static string PreferNonEmpty(string candidate, string fallback) =>
        string.IsNullOrWhiteSpace(candidate) ? fallback : candidate.Trim();

    private static string PreferConcreteGender(string candidate, string fallback)
    {
        var c = (candidate ?? "").Trim();
        var f = (fallback ?? "").Trim();
        if (string.Equals(c, "unspecified", StringComparison.OrdinalIgnoreCase))
            return string.IsNullOrWhiteSpace(f) ? "unspecified" : f;

        return string.IsNullOrWhiteSpace(c) ? (string.IsNullOrWhiteSpace(f) ? "unspecified" : f) : c;
    }

    private static string PreferInterestingVoiceLabel(string candidate, string fallback)
    {
        var c = (candidate ?? "").Trim();
        var f = (fallback ?? "").Trim();
        var bland = string.Equals(c, "neutral", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(c, "normal", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(c, "none", StringComparison.OrdinalIgnoreCase);

        if (string.IsNullOrWhiteSpace(c))
            return string.IsNullOrWhiteSpace(f) ? "neutral" : f;

        if (bland && !string.IsNullOrWhiteSpace(f) &&
            !string.Equals(f, "neutral", StringComparison.OrdinalIgnoreCase))
            return f;

        return c;
    }

    private static string? CombineNotes(string? a, string? b, int maxLen = 12000)
    {
        var x = string.IsNullOrWhiteSpace(a) ? "" : a.Trim();
        var y = string.IsNullOrWhiteSpace(b) ? "" : b.Trim();
        if (x.Length == 0)
            return y.Length == 0 ? null : y.Length <= maxLen ? y : y[..maxLen];

        if (y.Length == 0)
            return x.Length <= maxLen ? x : x[..maxLen];

        if (string.Equals(x, y, StringComparison.Ordinal))
            return x.Length <= maxLen ? x : x[..maxLen];

        var sep = $"{Environment.NewLine}{Environment.NewLine}";
        var merged = x + sep + y;
        return merged.Length <= maxLen ? merged : merged[..maxLen];
    }

    private static IReadOnlyList<string>? MergeAliases(IReadOnlyList<string>? a, IReadOnlyList<string>? b)
    {
        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        void AddRange(IReadOnlyList<string>? xs)
        {
            if (xs is null)
                return;
            foreach (var s in xs)
            {
                var t = (s ?? "").Trim();
                if (t.Length > 0)
                    set.Add(t);
            }
        }

        AddRange(a);
        AddRange(b);
        return set.Count == 0 ? null : set.OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToList();
    }
}
