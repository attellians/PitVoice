using PitVoice.Core.Intents;
using PitVoice.Core.Numbers;
using PitVoice.Core.Parsing;
using PitVoice.Core.Plural;

namespace PitVoice.Core.Languages;

/// <summary>A fuel unit as described by a pack.</summary>
/// <param name="Id">Canonical id (<see cref="FuelUnits"/>).</param>
/// <param name="Synonyms">Normalized words that mean this unit in speech.</param>
/// <param name="Forms">Word form per plural category, used in responses.</param>
public sealed record UnitDefinition(
    string Id,
    IReadOnlyList<string> Synonyms,
    IReadOnlyDictionary<PluralCategory, string> Forms);

/// <summary>A command the pack expects a test phrase to produce; <c>null</c> fields are not checked.</summary>
public sealed record ExpectedCommand(IntentId Intent, int? Amount, string? Unit)
{
    public override string ToString()
    {
        var parts = new List<string> { Intent.Value };
        if (Amount is { } amount)
        {
            parts.Add($"amount={amount}");
        }

        if (Unit is { } unit)
        {
            parts.Add($"unit={unit}");
        }

        return string.Join(' ', parts);
    }
}

/// <summary>A self-test phrase of a pack.</summary>
/// <param name="Say">Phrase to parse.</param>
/// <param name="Expect">Expected commands in order, or <c>null</c> when the phrase must be rejected.</param>
/// <param name="Line">Line of the test in the pack file.</param>
public sealed record PackTest(string Say, IReadOnlyList<ExpectedCommand>? Expect, int? Line)
{
    public bool ExpectsRejection => Expect is null;
}

/// <summary>A validated language pack (TZ §6): word classes, responses and self-tests.</summary>
public sealed class LanguagePack
{
    internal LanguagePack(
        string sourceFile,
        string language,
        string name,
        int version,
        string? asrModel,
        INumberParser numberParser,
        IPluralRules pluralRules,
        IReadOnlyDictionary<string, UnitDefinition> units,
        IReadOnlyList<IReadOnlyList<string>> conjunctions,
        IReadOnlySet<string> fillers,
        IReadOnlySet<string> negations,
        Lexicon lexicon,
        IReadOnlyDictionary<string, string> responses,
        IReadOnlyList<PackTest> tests,
        Normalizer normalizer,
        IReadOnlyList<IntentId>? intents = null)
    {
        SourceFile = sourceFile;
        Language = language;
        Name = name;
        Version = version;
        AsrModel = asrModel;
        NumberParser = numberParser;
        PluralRules = pluralRules;
        Units = units;
        Conjunctions = conjunctions;
        Fillers = fillers;
        Negations = negations;
        Lexicon = lexicon;
        Responses = responses;
        Tests = tests;
        Normalizer = normalizer;
        // Additions never make new intents available: their responses and tests come from the built-in pack.
        Intents = intents ?? IntentCatalog.All.Select(d => d.Id).Where(id => WordRules.IsAvailable(id, lexicon, negations.Count > 0)).ToArray();
    }

    public string SourceFile { get; }

    /// <summary>Language code, e.g. <c>ru</c>.</summary>
    public string Language { get; }

    /// <summary>Display name, e.g. <c>Русский</c>.</summary>
    public string Name { get; }

    public int Version { get; }

    /// <summary>ASR model id from the models manifest.</summary>
    public string? AsrModel { get; }

    public INumberParser NumberParser { get; }

    public IPluralRules PluralRules { get; }

    public IReadOnlyDictionary<string, UnitDefinition> Units { get; }

    /// <summary>Normalized conjunction word sequences, longest first.</summary>
    public IReadOnlyList<IReadOnlyList<string>> Conjunctions { get; }

    public IReadOnlySet<string> Fillers { get; }

    public IReadOnlySet<string> Negations { get; }

    /// <summary>
    /// Words that reject the whole phrase (design D12). They are in the recognizer grammar so that the recognizer hears
    /// them instead of dropping a phrase tail it cannot write ("меняем резину на слики" → "меняем резину на").
    /// </summary>
    public IReadOnlySet<string> RejectWords { get; internal init; } = new HashSet<string>(StringComparer.Ordinal);

    /// <summary>Word classes of the language, built-in and the user's own.</summary>
    public Lexicon Lexicon { get; }

    /// <summary>Catalog intents the word classes of this pack can produce, in catalog order.</summary>
    public IReadOnlyList<IntentId> Intents { get; }

    /// <summary>Responses of intents (by intent id) and generic ones: <c>not_understood</c>, <c>not_connected</c>, ...</summary>
    public IReadOnlyDictionary<string, string> Responses { get; }

    public IReadOnlyList<PackTest> Tests { get; }

    public Normalizer Normalizer { get; }

    public bool Supports(IntentId intent) => Intents.Contains(intent);

    /// <summary>The response of <paramref name="intent"/>.</summary>
    public string ResponseOf(IntentId intent) => Responses[intent.Value];

    /// <summary>Catalog intents this pack cannot produce; phrases for them are rejected in this language.</summary>
    public IReadOnlyList<IntentId> MissingIntents =>
        IntentCatalog.All.Select(d => d.Id).Where(id => !Supports(id)).ToArray();

    /// <summary>User additions file merged into this pack (FR-LANG-5), or null for the built-in pack alone.</summary>
    public string? AdditionsFile { get; private init; }

    /// <summary>Entries from the user's additions.</summary>
    public IReadOnlyList<LexiconEntry> UserEntries => Lexicon.Entries.Where(e => e.IsUserAddition).ToArray();

    /// <summary>A copy with <paramref name="extra"/> entries added to the lexicon (already validated).</summary>
    internal LanguagePack WithEntries(IReadOnlyList<LexiconEntry> extra, string additionsFile) =>
        new(SourceFile, Language, Name, Version, AsrModel, NumberParser, PluralRules, Units, Conjunctions, Fillers, Negations,
            Lexicon.With(extra), Responses, Tests, Normalizer, Intents)
        {
            AdditionsFile = additionsFile,
            RejectWords = RejectWords,
        };
}
