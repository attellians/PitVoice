using System.Diagnostics;
using PitVoice.Core.Audio;
using PitVoice.Core.Parsing;

namespace PitVoice.Core.Asr;

/// <summary>Outcome of recognizing one buffer (FR-ASR-5).</summary>
/// <param name="Phrase">Recognized words with per-word confidence; empty on silence or error.</param>
/// <param name="AudioDuration">Length of the recognized audio.</param>
/// <param name="RecognitionTime">Time spent in the engine.</param>
/// <param name="Error">Engine failure, or null.</param>
public sealed record RecognitionResult(RecognizedPhrase Phrase, TimeSpan AudioDuration, TimeSpan RecognitionTime, string? Error)
{
    public bool IsSuccess => Error is null;

    public bool IsEmpty => Phrase.Words.Count == 0;
}

/// <summary>
/// Runs an <see cref="IAsrEngine"/> safely: an empty buffer never reaches the engine, the engine call is timed, engine
/// exceptions become error results, and the unknown-word marker gets confidence 0 so the parser rejects the phrase.
/// </summary>
public sealed class SpeechRecognizer
{
    /// <summary>Marker the grammar uses for out-of-vocabulary speech.</summary>
    public const string UnknownWord = "[unk]";

    public SpeechRecognizer(IAsrEngine engine)
    {
        ArgumentNullException.ThrowIfNull(engine);
        Engine = engine;
    }

    public IAsrEngine Engine { get; }

    public RecognitionResult Recognize(AudioBuffer buffer)
    {
        ArgumentNullException.ThrowIfNull(buffer);
        if (buffer.IsEmpty)
        {
            return new RecognitionResult(RecognizedPhrase.Empty, TimeSpan.Zero, TimeSpan.Zero, null);
        }

        if (!Engine.IsLoaded)
        {
            return new RecognitionResult(RecognizedPhrase.Empty, buffer.Duration, TimeSpan.Zero, "recognition model is not loaded");
        }

        var started = Stopwatch.GetTimestamp();
        try
        {
            var phrase = Engine.Recognize(buffer);
            return new RecognitionResult(MarkUnknownWords(phrase), buffer.Duration, Stopwatch.GetElapsedTime(started), null);
        }
#pragma warning disable CA1031 // Any engine failure must turn into a result; the next request has to work.
        catch (Exception e)
#pragma warning restore CA1031
        {
            return new RecognitionResult(RecognizedPhrase.Empty, buffer.Duration, Stopwatch.GetElapsedTime(started), $"recognition failed: {e.Message}");
        }
    }

    /// <summary>Forces confidence 0 on every <see cref="UnknownWord"/>.</summary>
    public static RecognizedPhrase MarkUnknownWords(RecognizedPhrase phrase)
    {
        ArgumentNullException.ThrowIfNull(phrase);
        if (!phrase.Words.Any(IsUnknown))
        {
            return phrase;
        }

        return new RecognizedPhrase(phrase.Words.Select(w => IsUnknown(w) ? w with { Confidence = 0.0 } : w).ToArray());
    }

    private static bool IsUnknown(RecognizedWord word) => string.Equals(word.Text, UnknownWord, StringComparison.Ordinal);
}
