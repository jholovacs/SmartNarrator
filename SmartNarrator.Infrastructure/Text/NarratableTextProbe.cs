using System.Globalization;
using System.Text;

namespace SmartNarrator.Infrastructure.Text;

/// <summary>
/// Detects whether a canonical UTF‑16 slice contains prose worth narrating (letters, digits, symbols),
/// versus whitespace/punctuation‑only fillers between structural markers.
/// </summary>
internal static class NarratableTextProbe
{
    /// <summary>Returns true when at least one code point is not whitespace/punctuation/control/format noise.</summary>
    internal static bool SliceHasSpeakableContent(string canonicalUtf16, int startInclusive, int endExclusive)
    {
        if (string.IsNullOrEmpty(canonicalUtf16) || endExclusive <= startInclusive)
            return false;

        startInclusive = Math.Clamp(startInclusive, 0, canonicalUtf16.Length);
        endExclusive = Math.Clamp(endExclusive, startInclusive, canonicalUtf16.Length);

        foreach (var rune in canonicalUtf16.AsSpan(startInclusive, endExclusive - startInclusive).EnumerateRunes())
        {
            if (Rune.IsWhiteSpace(rune))
                continue;

            var cat = GetUnicodeCategory(rune);
            if (IsWhitespaceOrPunctuationNoise(cat))
                continue;

            return true;
        }

        return false;
    }

    private static UnicodeCategory GetUnicodeCategory(Rune rune)
    {
        var v = rune.Value;
        return v <= char.MaxValue
            ? char.GetUnicodeCategory((char)v)
            : CharUnicodeInfo.GetUnicodeCategory(char.ConvertFromUtf32(v), 0);
    }

    private static bool IsWhitespaceOrPunctuationNoise(UnicodeCategory cat) =>
        cat switch
        {
            UnicodeCategory.SpaceSeparator => true,
            UnicodeCategory.LineSeparator => true,
            UnicodeCategory.ParagraphSeparator => true,
            UnicodeCategory.Control => true,
            UnicodeCategory.Format => true,
            UnicodeCategory.DashPunctuation => true,
            UnicodeCategory.OpenPunctuation => true,
            UnicodeCategory.ClosePunctuation => true,
            UnicodeCategory.ConnectorPunctuation => true,
            UnicodeCategory.InitialQuotePunctuation => true,
            UnicodeCategory.FinalQuotePunctuation => true,
            UnicodeCategory.OtherPunctuation => true,
            _ => false,
        };
}
