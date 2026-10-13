namespace PitVoice.Core.Numbers;

/// <summary>
/// Turns a run of spoken number words (or a digit token) into an integer 0..999. One implementation per language.
/// </summary>
public interface INumberParser
{
    /// <summary>Smallest value this parser can produce.</summary>
    const int MinValue = 0;

    /// <summary>Largest value this parser can produce.</summary>
    const int MaxValue = 999;

    /// <summary>
    /// Tries to read a number that starts at <paramref name="words"/>[0]. Reads greedily: as many words as still form
    /// a valid number. Returns <c>false</c> if the first word does not start a number.
    /// </summary>
    /// <param name="words">Normalized (lower-case) words.</param>
    /// <param name="value">Parsed value.</param>
    /// <param name="consumed">How many words were used; at least 1 when the method returns <c>true</c>.</param>
    bool TryParse(ReadOnlySpan<string> words, out int value, out int consumed);

    /// <summary>
    /// Every word this parser understands, so that the recognizer grammar can contain all of them. Does not include
    /// digit strings.
    /// </summary>
    IReadOnlyCollection<string> Words { get; }
}
