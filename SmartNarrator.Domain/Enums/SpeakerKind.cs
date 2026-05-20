namespace SmartNarrator.Domain.Enums;

public enum SpeakerKind
{
    Character,
    Narrator,

    /// <summary>
    /// Quotation marks present but not spoken dialogue (emphasis, scare quotes, titles, irony).
    /// </summary>
    QuotedNonSpeech,
}
