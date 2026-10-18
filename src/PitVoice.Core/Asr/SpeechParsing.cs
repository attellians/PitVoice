using PitVoice.Core.Audio;
using PitVoice.Core.Parsing;

namespace PitVoice.Core.Asr;

/// <summary>How a spoken phrase was parsed.</summary>
/// <param name="Result">The parse result.</param>
/// <param name="Phrase">The words it was parsed from: the recognized ones, or with forms joined.</param>
/// <param name="FormsJoined">True when the second look at the forms (design D13) was taken.</param>
public sealed record SpeechParse(ParseResult Result, RecognizedPhrase Phrase, bool FormsJoined);

/// <summary>
/// Parses recognized speech (design D13). When the phrase is rejected for low confidence and a word below the threshold
/// has another form ("левые" heard as "левое"), the recognizer is asked once more, with its alternatives: a word split
/// between its own forms is certain of the word. Any other low confidence, or a word unsure between different words
/// ("левые" or "правые"), keeps the rejection. The threshold itself does not change.
/// </summary>
public static class SpeechParsing
{
    public static SpeechParse Parse(
        CommandParser parser, SpeechRecognizer recognizer, AudioBuffer audio, RecognizedPhrase phrase, ParseOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(parser);
        ArgumentNullException.ThrowIfNull(recognizer);
        ArgumentNullException.ThrowIfNull(audio);
        ArgumentNullException.ThrowIfNull(phrase);
        options ??= ParseOptions.Default;

        var first = parser.Parse(phrase, options);
        var unsureForm = phrase.Words.Any(w => w.Confidence < options.ConfidenceThreshold && parser.HasForms(w.Text));
        if (first is not ParseResult.Rejected { Reason: RejectReason.LowConfidence } || !unsureForm)
        {
            return new SpeechParse(first, phrase, FormsJoined: false);
        }

        if (recognizer.RecognizeForms(audio, phrase, parser.ReadAs) is not { } joined)
        {
            return new SpeechParse(first, phrase, FormsJoined: false);
        }

        return new SpeechParse(parser.Parse(joined, options), joined, FormsJoined: true);
    }
}
