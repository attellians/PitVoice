namespace PitVoice.Core.Parsing;

/// <summary>A word as produced by the recognizer, with its per-word confidence when the recognizer provides one.</summary>
/// <param name="Text">Raw text of the word.</param>
/// <param name="Confidence">0..1, or <c>null</c> when unknown (typed text). Unknown counts as 1.0.</param>
public sealed record RecognizedWord(string Text, double? Confidence = null);

/// <summary>Input of the parser: a sequence of recognized words.</summary>
public sealed record RecognizedPhrase(IReadOnlyList<RecognizedWord> Words)
{
    public static RecognizedPhrase Empty { get; } = new([]);

    /// <summary>Builds a phrase from typed text; every word gets no confidence (treated as 1.0).</summary>
    public static RecognizedPhrase FromText(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        var words = text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return new RecognizedPhrase(words.Select(w => new RecognizedWord(w)).ToArray());
    }

    public string Text => string.Join(' ', Words.Select(w => w.Text));
}
