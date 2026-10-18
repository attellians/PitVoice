using PitVoice.Core.Audio;
using PitVoice.Core.Languages;
using PitVoice.Core.Parsing;

namespace PitVoice.Core.Asr;

/// <summary>Result of <see cref="IAsrEngine.LoadAsync"/>; failures are values, not exceptions.</summary>
public sealed record AsrLoadResult(bool IsLoaded, string? Error)
{
    public static AsrLoadResult Loaded { get; } = new(true, null);

    public static AsrLoadResult Failed(string error) => new(false, error);
}

/// <summary>
/// A speech recognizer for one language at a time (FR-ASR-1). The model is loaded once and kept in memory; switching
/// language means <see cref="Unload"/> and <see cref="LoadAsync"/> again. Use it through <see cref="SpeechRecognizer"/>,
/// which handles empty buffers, timing, unknown words and engine failures.
/// </summary>
public interface IAsrEngine
{
    bool IsLoaded { get; }

    /// <summary>Language of the loaded model, or null.</summary>
    string? Language { get; }

    /// <summary>Loads the model and builds the grammar from <paramref name="pack"/>.</summary>
    /// <param name="pack">Language pack (its <see cref="LanguagePack.AsrModel"/> names the model in messages).</param>
    /// <param name="modelPath">Model directory, or null when the model is not installed.</param>
    /// <param name="cancellationToken">Cancels loading.</param>
    Task<AsrLoadResult> LoadAsync(LanguagePack pack, string? modelPath, CancellationToken cancellationToken = default);

    /// <summary>Recognizes a whole, non-empty buffer. May throw; <see cref="SpeechRecognizer"/> contains that.</summary>
    RecognizedPhrase Recognize(AudioBuffer buffer);

    /// <summary>
    /// Gives each word of <paramref name="phrase"/> (the result of <see cref="Recognize"/> for the same buffer) the
    /// probability that the recognizer heard that word in any of its forms, from its list of alternatives (design D13):
    /// a word split between "левые" and "левое" is certain of the word, not of the form. A confidence is never lowered.
    /// Null when the engine has no alternatives. May throw; <see cref="SpeechRecognizer"/> contains that.
    /// </summary>
    /// <param name="buffer">The same buffer.</param>
    /// <param name="phrase">The words recognized for it.</param>
    /// <param name="readAs">The word a form is read as ("левое" → "левые"); other words are returned as they are.</param>
    RecognizedPhrase? RecognizeForms(AudioBuffer buffer, RecognizedPhrase phrase, Func<string, string> readAs) => null;

    void Unload();
}
