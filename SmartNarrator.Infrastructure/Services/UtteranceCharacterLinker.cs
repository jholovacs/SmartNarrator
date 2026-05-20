using SmartNarrator.Domain.Entities;

namespace SmartNarrator.Infrastructure.Services;

/// <summary>
/// Links utterance <c>character_external_key</c> strings from analysis JSON to persisted character rows.
/// Exact AI-key match first; when confidence is high enough, falls back to unique name/alias token match.
/// </summary>
internal static class UtteranceCharacterLinker
{
    internal static Guid? ResolveCharacterId(
        string? externalKeyRaw,
        double confidence,
        double autoTrustMinConfidence,
        IReadOnlyDictionary<string, Guid> aiExternalKeyToId,
        IReadOnlyList<CharacterProfileEntity> profiles)
    {
        var key = externalKeyRaw?.Trim();
        if (string.IsNullOrEmpty(key))
            return null;

        foreach (var variant in KeyLookupVariants(key))
        {
            if (aiExternalKeyToId.TryGetValue(variant, out var id))
                return id;
        }

        if (confidence < autoTrustMinConfidence || !double.IsFinite(confidence))
            return null;

        Guid? nameMatch = null;

        foreach (var p in profiles)
        {
            if (AnalysisCharacterResolution.IsAnonymousCharacterProfile(p))
                continue;

            var display = (p.Name ?? "").Trim();
            if (display.Length == 0)
                continue;

            foreach (var variant in KeyLookupVariants(key))
            {
                if (!string.Equals(display, variant, StringComparison.OrdinalIgnoreCase))
                    continue;
                if (nameMatch is not null && nameMatch.Value != p.Id)
                    return null;
                nameMatch = p.Id;
                break;
            }
        }

        if (nameMatch is not null)
            return nameMatch;

        Guid? lone = null;
        foreach (var p in profiles)
        {
            if (AnalysisCharacterResolution.IsAnonymousCharacterProfile(p))
                continue;

            var tokens = ExpandedProfileTokens(p);
            var matched = false;
            foreach (var variant in KeyLookupVariants(key))
            {
                if (!tokens.Contains(variant))
                    continue;
                matched = true;
                break;
            }

            if (!matched)
                continue;

            if (lone is not null && lone.Value != p.Id)
                return null;

            lone = p.Id;
        }

        return lone;
    }

    /// <summary>Include individual words from multi-word names/aliases so keys like <c>mary</c> match profile <c>Mary Jane</c>.</summary>
    private static HashSet<string> ExpandedProfileTokens(CharacterProfileEntity p)
    {
        var baseTokens = AnalysisCharacterResolution.TokensFromProfile(p);
        var set = new HashSet<string>(baseTokens, StringComparer.OrdinalIgnoreCase);
        foreach (var t in baseTokens)
        {
            foreach (var part in t.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                if (part.Length >= 2)
                    set.Add(part);
            }
        }

        return set;
    }

    private static IEnumerable<string> KeyLookupVariants(string key)
    {
        var k = key.Trim();
        yield return k;

        var spaced = k.Replace('_', ' ').Trim();
        if (spaced.Length > 0 && !string.Equals(spaced, k, StringComparison.Ordinal))
            yield return spaced;

        var compact = k.Replace("_", "", StringComparison.Ordinal).Replace(" ", "", StringComparison.Ordinal);
        if (compact.Length > 0 && !string.Equals(compact, k, StringComparison.OrdinalIgnoreCase))
            yield return compact;
    }
}
