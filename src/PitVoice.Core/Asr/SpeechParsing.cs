using PitVoice.Core.Audio;
using PitVoice.Core.Parsing;

namespace PitVoice.Core.Asr;

/// <summary>How a spoken phrase was parsed.</summary>
/// <param name="Result">The parse result.</param>
/// <param name="Phrase">The words it was parsed from: the recognized ones, with forms joined, or an alternative of forms.</param>
/// <param name="FormsJoined">True when a second look at the forms was taken and gave <paramref name="Phrase"/>.</param>
public sealed record SpeechParse(ParseResult Result, RecognizedPhrase Phrase, bool FormsJoined);

/// <summary>
/// Parses recognized speech with a second look at word forms. When the phrase is rejected for low confidence and a
/// word below the threshold has another form ("левые" heard as "левое"), the recognizer is asked once more, with its
/// alternatives: a word split between its own forms is certain of the word (design D13). Any other low confidence, or
/// a word unsure between different words ("левые" or "правые"), keeps the rejection. The threshold itself does not
/// change. When the phrase is not understood and has a word with another form ("меняй передние левое"), the
/// alternatives that differ only by forms are parsed, and exactly one distinct accepted reading among them is taken
/// ("меняй переднее левое"); none or several keep the rejection.
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
        if (first is not ParseResult.Rejected rejected)
        {
            return new SpeechParse(first, phrase, FormsJoined: false);
        }

        if (rejected.Reason == RejectReason.LowConfidence)
        {
            var unsureForm = phrase.Words.Any(w => w.Confidence < options.ConfidenceThreshold && parser.HasForms(w.Text));
            if (unsureForm && recognizer.RecognizeForms(audio, phrase, parser.ReadAs) is { } joined)
            {
                return new SpeechParse(parser.Parse(joined, options), joined, FormsJoined: true);
            }
        }
        else if (rejected.Reason == RejectReason.NotUnderstood && phrase.Words.Any(w => parser.HasForms(w.Text)))
        {
            var readings = recognizer.RecognizeFormAlternatives(audio, phrase, parser.ReadAs)
                .Select(alternative => (Phrase: alternative, Result: parser.Parse(alternative, options)))
                .Where(reading => reading.Result.IsAccepted)
                .ToArray();
            if (readings.Length > 0 && readings.Select(r => Reading((ParseResult.Accepted)r.Result)).Distinct().Count() == 1)
            {
                return new SpeechParse(readings[0].Result, readings[0].Phrase, FormsJoined: true);
            }
        }

        return new SpeechParse(first, phrase, FormsJoined: false);
    }

    /// <summary>The commands of a reading, without their confidence: two forms giving the same wheel are one reading.</summary>
    private static string Reading(ParseResult.Accepted accepted) =>
        string.Join("; ", accepted.Commands.Select(c => (c with { Confidence = 0 }).ToString()));
}
