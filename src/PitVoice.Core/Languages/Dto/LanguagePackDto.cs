namespace PitVoice.Core.Languages.Dto;

/// <summary>Raw YAML shape of a language pack (TZ §6). Validation and compilation happen afterwards.</summary>
public sealed class LanguagePackDto
{
    public string? Language { get; set; }

    public string? Name { get; set; }

    public int? Version { get; set; }

    public string? AsrModel { get; set; }

    public string? DefaultVoice { get; set; }

    public string? PluralRules { get; set; }

    public Dictionary<string, UnitDto?>? Units { get; set; }

    public List<string?>? Conjunctions { get; set; }

    public List<string?>? Fillers { get; set; }

    public List<string?>? Negations { get; set; }

    public Dictionary<string, IntentDto?>? Intents { get; set; }

    public Dictionary<string, string?>? Responses { get; set; }

    public List<TestDto?>? Tests { get; set; }
}

public sealed class UnitDto
{
    public List<string?>? Synonyms { get; set; }

    public Dictionary<string, string?>? Forms { get; set; }
}

public sealed class IntentDto
{
    public List<string?>? Patterns { get; set; }

    public string? Response { get; set; }
}

public sealed class TestDto
{
    public string? Say { get; set; }

    /// <summary>Either the scalar <c>reject</c> or a list of expected commands.</summary>
    public object? Expect { get; set; }
}
