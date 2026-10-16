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

/// <summary>Templates and response of one intent in one language.</summary>
public sealed record IntentPhrases(IntentId Id, IReadOnlyList<Template> Templates, string Response);

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

/// <summary>A validated, compiled language pack (TZ §6).</summary>
public sealed class LanguagePack
{
    internal LanguagePack(
        string sourceFile,
        string language,
        string name,
        int version,
        string? asrModel,
        string? defaultVoice,
        INumberParser numberParser,
        IPluralRules pluralRules,
        IReadOnlyDictionary<string, UnitDefinition> units,
        IReadOnlyList<IReadOnlyList<string>> conjunctions,
        IReadOnlySet<string> fillers,
        IReadOnlySet<string> negations,
        IReadOnlyDictionary<IntentId, IntentPhrases> intents,
        IReadOnlyDictionary<string, string> responses,
        IReadOnlyList<PackTest> tests,
        Normalizer normalizer)
    {
        SourceFile = sourceFile;
        Language = language;
        Name = name;
        Version = version;
        AsrModel = asrModel;
        DefaultVoice = defaultVoice;
        NumberParser = numberParser;
        PluralRules = pluralRules;
        Units = units;
        Conjunctions = conjunctions;
        Fillers = fillers;
        Negations = negations;
        Intents = intents;
        Responses = responses;
        Tests = tests;
        Normalizer = normalizer;
        Templates = intents.Values.SelectMany(i => i.Templates).ToArray();
    }

    public string SourceFile { get; }

    /// <summary>Language code, e.g. <c>ru</c>.</summary>
    public string Language { get; }

    /// <summary>Display name, e.g. <c>Русский</c>.</summary>
    public string Name { get; }

    public int Version { get; }

    /// <summary>ASR model id from the models manifest (used from stage 2 on).</summary>
    public string? AsrModel { get; }

    /// <summary>Default TTS voice id (used from stage 4 on).</summary>
    public string? DefaultVoice { get; }

    public INumberParser NumberParser { get; }

    public IPluralRules PluralRules { get; }

    public IReadOnlyDictionary<string, UnitDefinition> Units { get; }

    /// <summary>Normalized conjunction word sequences, longest first.</summary>
    public IReadOnlyList<IReadOnlyList<string>> Conjunctions { get; }

    public IReadOnlySet<string> Fillers { get; }

    public IReadOnlySet<string> Negations { get; }

    public IReadOnlyDictionary<IntentId, IntentPhrases> Intents { get; }

    /// <summary>All templates of all intents.</summary>
    public IReadOnlyList<Template> Templates { get; }

    /// <summary>Generic responses: <c>not_understood</c>, <c>not_connected</c>, ...</summary>
    public IReadOnlyDictionary<string, string> Responses { get; }

    public IReadOnlyList<PackTest> Tests { get; }

    public Normalizer Normalizer { get; }

    /// <summary>Catalog intents this pack does not describe; phrases for them are rejected in this language.</summary>
    public IReadOnlyList<IntentId> MissingIntents =>
        IntentCatalog.All.Select(d => d.Id).Where(id => !Intents.ContainsKey(id)).ToArray();

    /// <summary>User additions file merged into this pack (FR-LANG-5), or null for the built-in pack alone.</summary>
    public string? AdditionsFile { get; private init; }

    /// <summary>A copy with <paramref name="extra"/> templates appended to their intents (already validated).</summary>
    internal LanguagePack WithTemplates(IReadOnlyDictionary<IntentId, List<Template>> extra, string additionsFile)
    {
        var intents = Intents.ToDictionary(
            p => p.Key,
            p => extra.TryGetValue(p.Key, out var added) ? p.Value with { Templates = [.. p.Value.Templates, .. added] } : p.Value);
        return new LanguagePack(SourceFile, Language, Name, Version, AsrModel, DefaultVoice, NumberParser, PluralRules, Units,
            Conjunctions, Fillers, Negations, intents, Responses, Tests, Normalizer)
        {
            AdditionsFile = additionsFile,
        };
    }
}
