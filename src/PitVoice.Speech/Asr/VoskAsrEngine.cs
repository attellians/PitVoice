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
    private readonly GrammarMode _mode;
    private readonly Func<string, VoskModelHandle> _loadModel;
    private readonly Lock _lock = new();
    private VoskModelHandle? _model;
    private string? _grammar;

    static VoskAsrEngine()
    {
        VoskNative.SetLogLevel(-1);
    }

    public VoskAsrEngine(GrammarMode mode = GrammarBuilder.DefaultMode)
        : this(mode, VoskModelHandle.Load)
    {
    }

    /// <param name="mode">Grammar shape.</param>
    /// <param name="loadModel">Loads a model from disk; replaceable so tests can count loads.</param>
    internal VoskAsrEngine(GrammarMode mode, Func<string, VoskModelHandle> loadModel)
    {
        ArgumentNullException.ThrowIfNull(loadModel);
        _mode = mode;
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

    public GrammarMode Mode => _mode;

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
        var grammar = GrammarBuilder.ToJson(GrammarBuilder.Build(pack, _mode, isKnown));
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
