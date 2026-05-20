using SmartNarrator.Application.Ports;
using SmartNarrator.Domain.Entities;
using SmartNarrator.Domain.Enums;

namespace SmartNarrator.Infrastructure.Speech;

public sealed record SpeechTimelineSlice(
    int StartOffset,
    int EndOffset,
    SpeakerKind Kind,
    Guid? CharacterOrNarratorLinkId,
    NarrativePassageEntity? NarrPassageFallback);

/// <summary>Builds narration + dialogue excerpts in document order.</summary>
public static class SpeechTimelinePlanner
{
    public static IEnumerable<SpeechTimelineSlice> Plan(
        WorkEntity work,
        IReadOnlyList<UtteranceEntity> utterances,
        IReadOnlyList<NarrativePassageEntity> passagesSorted)
    {
        var text = work.CanonicalText;
        if (string.IsNullOrEmpty(text))
            yield break;

        var orderedUtterances = utterances.OrderBy(u => u.StartOffset).ToList();
        var passages = passagesSorted.OrderBy(p => p.StartOffset).ToList();

        var cursor = 0;
        foreach (var utterance in orderedUtterances)
        {
            if (utterance.StartOffset > cursor)
            {
                foreach (var s in NarratorSlices(cursor, utterance.StartOffset, text, passages))
                    yield return s;
            }

            if (utterance.StartOffset < utterance.EndOffset)
            {
                var mid = (utterance.StartOffset + utterance.EndOffset) / 2;
                var passage = LocatePassage(mid, passages);

                var linkCharacter = utterance.SpeakerKind == SpeakerKind.Character
                    ? utterance.CharacterId
                    : passage?.NarratorCharacterId;

                yield return new SpeechTimelineSlice(
                    utterance.StartOffset,
                    utterance.EndOffset,
                    utterance.SpeakerKind == SpeakerKind.QuotedNonSpeech ? SpeakerKind.Narrator : utterance.SpeakerKind,
                    linkCharacter,
                    passage);
            }

            cursor = Math.Max(cursor, utterance.EndOffset);
        }

        foreach (var s in NarratorSlices(cursor, text.Length, text, passages))
            yield return s;
    }

    public static SpeechSynthesisRequestDto ToRequest(
        string voiceModel,
        SpeechTimelineSlice slice,
        string excerpt,
        CharacterProfileEntity? speakerCharacterOrNull)
    {
        var passage = slice.NarrPassageFallback;

        var extras = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["speaking_pace"] = passage?.SpeakingPace ?? speakerCharacterOrNull?.SpeakingPace ?? "normal",
            ["accent"] = passage?.Accent ?? speakerCharacterOrNull?.Accent ?? "neutral",
            ["tone"] = passage?.Tone ?? speakerCharacterOrNull?.Tone ?? "neutral",
            ["breathiness"] = passage?.Breathiness ?? speakerCharacterOrNull?.Breathiness ?? "normal",
            ["gender_presentation"] =
                passage?.GenderPresentation ?? speakerCharacterOrNull?.GenderPresentation ?? "unspecified",
        };

        var genderBasis = passage?.GenderPresentation ?? speakerCharacterOrNull?.GenderPresentation ??
                          "unspecified";

        var voiceAlias = GenderToVoiceStub(genderBasis);

        return new SpeechSynthesisRequestDto
        {
            VoiceModel = voiceModel,
            VoiceId = voiceAlias,
            Text = excerpt.Trim(),
            ExtraParameters = extras,
        };
    }

    private static NarrativePassageEntity? LocatePassage(int midpoint, IEnumerable<NarrativePassageEntity> passages) =>
        passages.Where(x => midpoint >= x.StartOffset && midpoint < x.EndOffset)
            .MinBy(x => x.EndOffset - x.StartOffset);

    private static IEnumerable<SpeechTimelineSlice> NarratorSlices(int startExclusive, int endExclusive, string text,
        IReadOnlyList<NarrativePassageEntity> passages)
    {
        var start = startExclusive;
        var end = endExclusive;
        while (start < end && char.IsWhiteSpace(text[start]))
            start++;
        while (end > start && char.IsWhiteSpace(text[end - 1]))
            end--;
        if (start >= end || start < 0 || end > text.Length)
            yield break;

        var mid = (start + end) / 2;
        var passage = LocatePassage(mid, passages);

        yield return new SpeechTimelineSlice(start, end, SpeakerKind.Narrator, passage?.NarratorCharacterId, passage);
    }

    private static string GenderToVoiceStub(string genderPresentation)
    {
        var g = genderPresentation.Trim().ToLowerInvariant();
        if (g.StartsWith('f'))
            return "verse";
        if (g.StartsWith('m'))
            return "echo";
        return "alloy";
    }
}
