using System.Text.Json;
using SmartNarrator.Application.Analysis;
using SmartNarrator.Domain.Entities;

namespace SmartNarrator.Infrastructure.Services;

/// <summary>
/// Maps newly inferred characters onto existing profiles when display names / aliases overlap,
/// except for anonymous placeholders where each inference must stay isolated.
/// </summary>
internal static class AnalysisCharacterResolution
{
    private static readonly HashSet<string> AnonymousDisplayLabels = new(StringComparer.OrdinalIgnoreCase)
    {
        "unknown",
        "unnamed",
        "anonymous",
        "anon",
        "?",
    };

    internal static bool IsAnonymousCharacterSuggestion(CharacterSuggestionDto c)
    {
        var name = string.IsNullOrWhiteSpace(c.Name) ? string.Empty : c.Name.Trim();
        if (name.Length == 0)
            return true;

        if (AnonymousDisplayLabels.Contains(name))
            return true;

        return false;
    }

    internal static bool IsAnonymousCharacterProfile(CharacterProfileEntity row)
    {
        var name = string.IsNullOrWhiteSpace(row.Name) ? string.Empty : row.Name.Trim();
        if (name.Length == 0)
            return true;

        if (AnonymousDisplayLabels.Contains(name))
            return true;

        if (string.Equals(name, "Unnamed", StringComparison.OrdinalIgnoreCase))
            return true;

        return false;
    }

    /// <summary>
    /// Finds exactly one established (non-placeholder) profile whose name or aliases intersect the suggestion.
    /// Does not match when the suggestion is anonymous or carries no comparable tokens.
    /// </summary>
    internal static CharacterProfileEntity? TryFindUniqueEstablishedMatch(
        IReadOnlyCollection<CharacterProfileEntity> existing,
        CharacterSuggestionDto suggestion)
    {
        if (IsAnonymousCharacterSuggestion(suggestion))
            return null;

        var want = NonAnonymousTokensFromSuggestion(suggestion);
        if (want.Count == 0)
            return null;

        CharacterProfileEntity? hit = null;
        foreach (var row in existing)
        {
            if (IsAnonymousCharacterProfile(row))
                continue;

            var got = TokensFromProfile(row);
            if (!want.Overlaps(got))
                continue;

            if (hit is not null && !ReferenceEquals(hit, row))
                return null;

            hit = row;
        }

        return hit;
    }

    internal static HashSet<string> NonAnonymousTokensFromSuggestion(CharacterSuggestionDto suggestion)
    {
        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var name = string.IsNullOrWhiteSpace(suggestion.Name) ? string.Empty : suggestion.Name.Trim();
        if (name.Length > 0 && !AnonymousDisplayLabels.Contains(name))
            set.Add(name);

        if (suggestion.Aliases is null)
            return set;

        foreach (var a in suggestion.Aliases)
        {
            var t = (a ?? "").Trim();
            if (t.Length > 0 && !AnonymousDisplayLabels.Contains(t))
                set.Add(t);
        }

        return set;
    }

    internal static HashSet<string> TokensFromProfile(CharacterProfileEntity row)
    {
        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var name = string.IsNullOrWhiteSpace(row.Name) ? string.Empty : row.Name.Trim();
        if (name.Length > 0)
            set.Add(name);

        foreach (var a in DeserializeAliases(row.AliasesJson) ?? [])
        {
            var t = a.Trim();
            if (t.Length > 0)
                set.Add(t);
        }

        return set;
    }

    /// <summary>
    /// Overwrite semantics for cumulative Phase 3 registry rows keyed by <see cref="CharacterSuggestionDto.ExternalKey"/>.
    /// </summary>
    internal static void ApplyCumulativeSuggestion(CharacterProfileEntity row, CharacterSuggestionDto c)
    {
        row.Name = string.IsNullOrWhiteSpace(c.Name) ? row.Name : c.Name.Trim();
        row.AliasesJson = c.Aliases is { Count: > 0 } ? JsonSerializer.Serialize(c.Aliases) : null;
        row.PersonalitySummary = string.IsNullOrWhiteSpace(c.PersonalitySummary) ? null : c.PersonalitySummary.Trim();
        row.SpeechStyleSummary =
            string.IsNullOrWhiteSpace(c.SpeechStyleSummary) ? null : c.SpeechStyleSummary.Trim();
        row.GenderPresentation = c.GenderPresentation;
        row.Tone = c.Tone;
        row.Accent = c.Accent;
        row.Breathiness = c.Breathiness;
        row.SpeakingPace = c.SpeakingPace;
    }

    /// <summary>
    /// Merge cumulative AI output into user-maintained rows without duplicating prior accumulator content.
    /// </summary>
    internal static void MergeCumulativeSuggestionIntoEstablished(CharacterProfileEntity row, CharacterSuggestionDto c)
    {
        if (IsAnonymousCharacterProfile(row) && !IsAnonymousCharacterSuggestion(c))
            row.Name = c.Name.Trim();

        row.PersonalitySummary =
            MergeCumulativeAiNote(row.PersonalitySummary, c.PersonalitySummary);

        row.SpeechStyleSummary =
            MergeCumulativeAiNote(row.SpeechStyleSummary, c.SpeechStyleSummary);

        row.GenderPresentation =
            PreferConcreteGender(c.GenderPresentation, row.GenderPresentation);

        row.Tone = PreferInterestingVoiceLabel(c.Tone, row.Tone);
        row.Accent = PreferInterestingVoiceLabel(c.Accent, row.Accent);
        row.Breathiness = PreferInterestingVoiceLabel(c.Breathiness, row.Breathiness);
        row.SpeakingPace = PreferInterestingVoiceLabel(c.SpeakingPace, row.SpeakingPace);

        row.AliasesJson = SerializeMergedAliases(row.AliasesJson, c.Aliases, row.Name, c.Name, c.ExternalKey);

        if (string.IsNullOrWhiteSpace(row.AiExternalKey))
            row.AiExternalKey = c.ExternalKey.Trim();
    }

    internal static CharacterProfileEntity CreateProfileFromSuggestion(Guid workId, CharacterSuggestionDto c) =>
        new()
        {
            Id = Guid.NewGuid(),
            WorkId = workId,
            AiExternalKey = c.ExternalKey.Trim(),
            Name = string.IsNullOrWhiteSpace(c.Name) ? c.ExternalKey.Trim() : c.Name.Trim(),
            AliasesJson = c.Aliases is { Count: > 0 } ? JsonSerializer.Serialize(c.Aliases) : null,
            PersonalitySummary = string.IsNullOrWhiteSpace(c.PersonalitySummary) ? null : c.PersonalitySummary.Trim(),
            SpeechStyleSummary =
                string.IsNullOrWhiteSpace(c.SpeechStyleSummary) ? null : c.SpeechStyleSummary.Trim(),
            GenderPresentation = c.GenderPresentation,
            Tone = c.Tone,
            Accent = c.Accent,
            Breathiness = c.Breathiness,
            SpeakingPace = c.SpeakingPace,
            IsAiSuggested = true,
            UserApproved = false,
        };

    private static string? MergeCumulativeAiNote(string? existingRow, string? cumulativeFromModel)
    {
        var r = string.IsNullOrWhiteSpace(existingRow) ? string.Empty : existingRow.Trim();
        var m = string.IsNullOrWhiteSpace(cumulativeFromModel) ? string.Empty : cumulativeFromModel.Trim();
        if (m.Length == 0)
            return r.Length == 0 ? null : r;

        if (r.Length == 0)
            return m;

        if (string.Equals(r, m, StringComparison.Ordinal))
            return r;

        if (m.AsSpan().Contains(r.AsSpan(), StringComparison.OrdinalIgnoreCase))
            return m;

        var sep = $"{Environment.NewLine}{Environment.NewLine}";
        var merged = r + sep + m;
        const int maxLen = 12000;
        return merged.Length <= maxLen ? merged : merged[..maxLen];
    }

    private static string? SerializeMergedAliases(string? existingJson, IReadOnlyList<string>? incomingAliases,
        string? rowName, string? suggestionName, string suggestionExternalKey)
    {
        var bag = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var a in DeserializeAliases(existingJson) ?? [])
            bag.Add(a.Trim());

        if (incomingAliases is not null)
        {
            foreach (var a in incomingAliases)
            {
                var t = (a ?? "").Trim();
                if (t.Length > 0)
                    bag.Add(t);
            }
        }

        void MaybeAlias(string? label)
        {
            var t = (label ?? "").Trim();
            if (t.Length == 0)
                return;

            var rn = (rowName ?? "").Trim();
            if (rn.Length > 0 && string.Equals(t, rn, StringComparison.OrdinalIgnoreCase))
                return;

            bag.Add(t);
        }

        MaybeAlias(suggestionName);
        MaybeAlias(suggestionExternalKey);

        return bag.Count > 0 ? JsonSerializer.Serialize(bag.OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToList()) : null;
    }

    private static List<string>? DeserializeAliases(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return null;

        try
        {
            return JsonSerializer.Deserialize<List<string>>(json);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static string PreferConcreteGender(string candidate, string fallback)
    {
        var c = (candidate ?? "").Trim();
        var f = (fallback ?? "").Trim();
        if (string.Equals(c, "unspecified", StringComparison.OrdinalIgnoreCase))
            return string.IsNullOrWhiteSpace(f) ? "unspecified" : f;

        return string.IsNullOrWhiteSpace(c)
            ? (string.IsNullOrWhiteSpace(f) ? "unspecified" : f)
            : c;
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
            !string.Equals(f, "neutral", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(f, "normal", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(f, "none", StringComparison.OrdinalIgnoreCase))
            return f;

        return c;
    }
}
