using System.Text.Json;
using PitVoice.Core.Asr;
using PitVoice.Core.Audio;
using PitVoice.Core.Languages;
using PitVoice.Core.Parsing;

namespace PitVoice.Speech.Asr;

/// <summary>
/// Vosk recognizer (design D4–D5). The model and grammar are loaded once per language; every <see cref="Recognize"/>
/// creates a fresh recognizer over the loaded model with per-word confidence enabled and feeds it the whole buffer.
/// </summary>
public sealed class VoskAsrEngine : IAsrEngine, IDisposable
{
    private readonly Func<string, VoskModelHandle> _loadModel;
    private readonly Lock _lock = new();
    private VoskModelHandle? _model;
    private string? _grammar;

    static VoskAsrEngine()
    {
        VoskNative.SetLogLevel(-1);
    }

    public VoskAsrEngine()
        : this(VoskModelHandle.Load)
    {
    }

    /// <param name="loadModel">Loads a model from disk; replaceable so tests can count loads.</param>
    internal VoskAsrEngine(Func<string, VoskModelHandle> loadModel)
    {
        ArgumentNullException.ThrowIfNull(loadModel);
        _loadModel = loadModel;
    }

    public bool IsLoaded
    {
        get
        {
            lock (_lock)
            {
                return _model is not null;
            }
        }
    }

    public string? Language { get; private set; }

    /// <summary>Directory of the loaded model.</summary>
    public string? ModelPath { get; private set; }

    public async Task<AsrLoadResult> LoadAsync(LanguagePack pack, string? modelPath, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(pack);
        var modelId = pack.AsrModel ?? "(no asr_model in pack)";

        if (IsLoaded && Language == pack.Language && ModelPath == modelPath)
        {
            return AsrLoadResult.Loaded;
        }

        Unload();
        if (string.IsNullOrWhiteSpace(modelPath) || !Directory.Exists(modelPath))
        {
            return AsrLoadResult.Failed($"model not installed: {modelId}");
        }

        var (model, error) = await OpenModelAsync(modelPath, _loadModel, cancellationToken).ConfigureAwait(false);
        if (model is null)
        {
            return AsrLoadResult.Failed(error!);
        }

        var isKnown = IsKnownWord(model);
        var vocabulary = GrammarBuilder.CheckVocabulary(pack, isKnown);
        var grammar = GrammarBuilder.ToJson(GrammarBuilder.Build(pack, isKnown));
        lock (_lock)
        {
            _model = model;
            _grammar = grammar;
            Vocabulary = vocabulary;
            Language = pack.Language;
            ModelPath = modelPath;
        }

        return AsrLoadResult.Loaded;
    }

    /// <summary>How the loaded pack fits the loaded model: ё-spellings used and words left out of the grammar.</summary>
    public VocabularyReport? Vocabulary { get; private set; }

    /// <summary>Loads a model only to compare its vocabulary with <paramref name="pack"/> (used by pack validation).</summary>
    public static async Task<(VocabularyReport? Report, string? Error)> CheckVocabularyAsync(
        LanguagePack pack,
        string modelPath,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(pack);
        var (model, error) = await OpenModelAsync(modelPath, VoskModelHandle.Load, cancellationToken).ConfigureAwait(false);
        if (model is null)
        {
            return (null, error);
        }

        using (model)
        {
            return (GrammarBuilder.CheckVocabulary(pack, IsKnownWord(model)), null);
        }
    }

    private static Func<string, bool> IsKnownWord(VoskModelHandle model) => model.Contains;

    /// <summary>Opens a model directory; failures are returned as messages, the process never crashes on a bad model.</summary>
    private static async Task<(VoskModelHandle? Model, string? Error)> OpenModelAsync(
        string modelPath,
        Func<string, VoskModelHandle> loadModel,
        CancellationToken cancellationToken)
    {
        var fullPath = Path.GetFullPath(modelPath);

        // Vosk does not throw for a directory without a model; a null native handle would crash the next call.
        var acousticModel = Path.Combine(fullPath, "am", "final.mdl");
        if (!File.Exists(acousticModel))
        {
            return (null, $"model directory corrupted: {fullPath}: {Path.Combine("am", "final.mdl")} is missing");
        }

        VoskModelHandle? model = null;
        try
        {
            model = await Task.Run(() => loadModel(fullPath), cancellationToken).ConfigureAwait(false);
            if (model.IsInvalid)
            {
                model.Dispose();
                return (null, $"model directory corrupted: {fullPath}: the files are missing or unreadable");
            }

            return (model, null);
        }
        catch (OperationCanceledException)
        {
            model?.Dispose();
            throw;
        }
        catch (Exception e) when (e is DllNotFoundException or BadImageFormatException or EntryPointNotFoundException)
        {
            return (null, $"speech recognition library failed to load: {e.Message}");
        }
#pragma warning disable CA1031 // Any other native failure is reported as a corrupted model; the app keeps running.
        catch (Exception e)
#pragma warning restore CA1031
        {
            model?.Dispose();
            return (null, $"model directory corrupted: {fullPath}: {e.Message}");
        }
    }

    public RecognizedPhrase Recognize(AudioBuffer buffer)
    {
        ArgumentNullException.ThrowIfNull(buffer);
        VoskModelHandle model;
        string grammar;
        lock (_lock)
        {
            model = _model ?? throw new InvalidOperationException("recognition model is not loaded");
            grammar = _grammar!;
        }

        if (buffer.IsEmpty)
        {
            return RecognizedPhrase.Empty;
        }

        using var recognizer = VoskNative.RecognizerNewGrammar(model, AudioBuffer.SampleRate, grammar);
        if (recognizer.IsInvalid)
        {
            throw new InvalidOperationException("Vosk could not create a recognizer for the grammar");
        }

        VoskNative.RecognizerSetWords(recognizer, 1);
        return ParseResult(recognizer.Recognize(buffer.ToArray()));
    }

    /// <summary>Alternatives asked for when a word may be only unsure of its form.</summary>
    internal const int FormAlternatives = 10;

    /// <summary>How far below the best score an alternative of forms may be (design D2 of read-wheel-names-from-alternatives).</summary>
    internal const double FormScoreMargin = 0.5;

    public RecognizedPhrase? RecognizeForms(AudioBuffer buffer, RecognizedPhrase phrase, Func<string, string> readAs)
    {
        ArgumentNullException.ThrowIfNull(buffer);
        ArgumentNullException.ThrowIfNull(phrase);
        ArgumentNullException.ThrowIfNull(readAs);
        if (buffer.IsEmpty || phrase.Words.Count == 0)
        {
            EnsureLoaded();
            return phrase;
        }

        return JoinForms(phrase, RecognizeAlternatives(buffer), readAs);
    }

    public IReadOnlyList<RecognizedPhrase> RecognizeFormAlternatives(AudioBuffer buffer, RecognizedPhrase phrase, Func<string, string> readAs)
    {
        ArgumentNullException.ThrowIfNull(buffer);
        ArgumentNullException.ThrowIfNull(phrase);
        ArgumentNullException.ThrowIfNull(readAs);
        if (buffer.IsEmpty || phrase.Words.Count == 0)
        {
            EnsureLoaded();
            return [];
        }

        return SelectFormAlternatives(phrase, RecognizeAlternatives(buffer), readAs);
    }

    private void EnsureLoaded()
    {
        lock (_lock)
        {
            _ = _model ?? throw new InvalidOperationException("recognition model is not loaded");
        }
    }

    /// <summary>Recognizes the buffer once more with up to <see cref="FormAlternatives"/> alternatives.</summary>
    private IReadOnlyList<(double Score, IReadOnlyList<string> Words)> RecognizeAlternatives(AudioBuffer buffer)
    {
        VoskModelHandle model;
        string grammar;
        lock (_lock)
        {
            model = _model ?? throw new InvalidOperationException("recognition model is not loaded");
            grammar = _grammar!;
        }

        using var recognizer = VoskNative.RecognizerNewGrammar(model, AudioBuffer.SampleRate, grammar);
        if (recognizer.IsInvalid)
        {
            throw new InvalidOperationException("Vosk could not create a recognizer for the grammar");
        }

        VoskNative.RecognizerSetWords(recognizer, 1);
        VoskNative.RecognizerSetMaxAlternatives(recognizer, FormAlternatives);
        return ParseAlternatives(recognizer.Recognize(buffer.ToArray()));
    }

    /// <summary>
    /// Alternatives with as many words as <paramref name="phrase"/>, each read as the same word in the same place, that
    /// differ from it in at least one form and score no lower than the best minus <see cref="FormScoreMargin"/>. An
    /// alternative with any other word ("и", "не", "меня") is left out. Each word's confidence is its share of all
    /// alternatives, as in <see cref="JoinForms"/>; an unknown word keeps 0.
    /// </summary>
    internal static IReadOnlyList<RecognizedPhrase> SelectFormAlternatives(
        RecognizedPhrase phrase, IReadOnlyList<(double Score, IReadOnlyList<string> Words)> alternatives, Func<string, string> readAs)
    {
        if (alternatives.Count == 0)
        {
            return [];
        }

        var best = alternatives.Max(a => a.Score);
        var selected = new List<RecognizedPhrase>();
        foreach (var (score, words) in alternatives)
        {
            if (score < best - FormScoreMargin || words.Count != phrase.Words.Count)
            {
                continue;
            }

            var sameWords = words.Select((word, i) => readAs(word) == readAs(phrase.Words[i].Text)).All(same => same);
            var sameForms = words.Select((word, i) => string.Equals(word, phrase.Words[i].Text, StringComparison.Ordinal)).All(same => same);
            if (!sameWords || sameForms || selected.Any(s => s.Words.Select(w => w.Text).SequenceEqual(words, StringComparer.Ordinal)))
            {
                continue;
            }

            var unsure = new RecognizedPhrase(words.Select(word => new RecognizedWord(word, 0.0)).ToArray());
            selected.Add(JoinForms(unsure, alternatives, readAs));
        }

        return selected;
    }

    /// <summary>
    /// Each word gets the share of the alternatives that have the same word in the same place once forms are read as
    /// one word; the share comes from the alternative scores (log scale, so a softmax). Alternatives with a different
    /// number of words add nothing, so a confidence only rises when the alternatives agree word by word.
    /// </summary>
    internal static RecognizedPhrase JoinForms(
        RecognizedPhrase phrase, IReadOnlyList<(double Score, IReadOnlyList<string> Words)> alternatives, Func<string, string> readAs)
    {
        if (alternatives.Count == 0)
        {
            return phrase;
        }

        var best = alternatives.Max(a => a.Score);
        var weights = alternatives.Select(a => Math.Exp(a.Score - best)).ToArray();
        var total = weights.Sum();
        var words = phrase.Words.Select((word, i) =>
        {
            if (string.Equals(word.Text, SpeechRecognizer.UnknownWord, StringComparison.Ordinal))
            {
                return word;
            }

            var target = readAs(word.Text);
            var share = 0.0;
            for (var k = 0; k < alternatives.Count; k++)
            {
                var alternative = alternatives[k].Words;
                if (alternative.Count == phrase.Words.Count && readAs(alternative[i]) == target)
                {
                    share += weights[k];
                }
            }

            var joined = share / total;
            return word.Confidence is { } confidence && joined > confidence ? word with { Confidence = joined } : word;
        });

        return new RecognizedPhrase(words.ToArray());
    }

    /// <summary>Vosk's JSON with alternatives: <c>{"alternatives":[{"confidence","result":[{"word"}...],"text"}...]}</c>.</summary>
    internal static IReadOnlyList<(double Score, IReadOnlyList<string> Words)> ParseAlternatives(string json)
    {
        using var document = JsonDocument.Parse(json);
        if (!document.RootElement.TryGetProperty("alternatives", out var alternatives) || alternatives.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        return alternatives.EnumerateArray()
            .Select(a => (
                a.GetProperty("confidence").GetDouble(),
                (IReadOnlyList<string>)(a.TryGetProperty("result", out var result) && result.ValueKind == JsonValueKind.Array
                    ? result.EnumerateArray().Select(w => w.GetProperty("word").GetString() ?? string.Empty).ToArray()
                    : [])))
            .ToArray();
    }

    public void Unload()
    {
        lock (_lock)
        {
            _model?.Dispose();
            _model = null;
            _grammar = null;
            Vocabulary = null;
            Language = null;
            ModelPath = null;
        }
    }

    public void Dispose() => Unload();

    /// <summary>Turns Vosk's final JSON (<c>{"result":[{"word","conf"}...],"text"}</c>) into a phrase.</summary>
    internal static RecognizedPhrase ParseResult(string json)
    {
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        if (root.TryGetProperty("result", out var result) && result.ValueKind == JsonValueKind.Array)
        {
            var words = new List<RecognizedWord>(result.GetArrayLength());
            foreach (var item in result.EnumerateArray())
            {
                var word = item.GetProperty("word").GetString() ?? string.Empty;
                double? confidence = item.TryGetProperty("conf", out var conf) ? conf.GetDouble() : null;
                if (string.Equals(word, SpeechRecognizer.UnknownWord, StringComparison.Ordinal))
                {
                    confidence = 0.0;
                }

                words.Add(new RecognizedWord(word, confidence));
            }

            return new RecognizedPhrase(words);
        }

        // No word list: silence, or a result without per-word data.
        var text = root.TryGetProperty("text", out var t) ? t.GetString() : null;
        return string.IsNullOrWhiteSpace(text) ? RecognizedPhrase.Empty : RecognizedPhrase.FromText(text);
    }
}
