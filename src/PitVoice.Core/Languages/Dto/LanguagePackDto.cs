namespace PitVoice.Core.Languages.Dto;

/// <summary>
/// Raw YAML shape of a language pack (TZ §6, format 2). Loading is strict: a field that is not here, such as the removed
/// <c>default_voice</c> or the old <c>intents</c>, makes the pack invalid. Validation happens afterwards.
/// </summary>
public sealed class LanguagePackDto
{
    public string? Language { get; set; }

    public string? Name { get; set; }

    public int? Version { get; set; }

    public string? AsrModel { get; set; }

    public string? PluralRules { get; set; }

    public Dictionary<string, UnitDto?>? Units { get; set; }

    public List<string?>? Conjunctions { get; set; }

    public List<string?>? Fillers { get; set; }

    public List<string?>? Negations { get; set; }

    /// <summary>Words that make a phrase rejected (design D12): "на" of "меняем резину на слики".</summary>
    public List<string?>? RejectWords { get; set; }

    /// <summary>
    /// Word classes: group → role → words, and for <c>tires.targets</c> target → words. Kept untyped so that an
    /// unknown group, role or target gets an error with its name and line instead of a generic YAML error.
    /// </summary>
    public Dictionary<string, Dictionary<string, object?>?>? Lexicon { get; set; }

    public Dictionary<string, string?>? Responses { get; set; }

    public List<TestDto?>? Tests { get; set; }
}

public sealed class UnitDto
{
    public List<string?>? Synonyms { get; set; }

    public Dictionary<string, string?>? Forms { get; set; }
}

public sealed class TestDto
{
    public string? Say { get; set; }

    /// <summary>Either the scalar <c>reject</c> or a list of expected commands.</summary>
    public object? Expect { get; set; }
}
