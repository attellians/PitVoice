using PitVoice.Core.Numbers;

namespace PitVoice.Core.Parsing;

/// <summary>Language data the normalizer needs; extracted from a language pack.</summary>
/// <param name="NumberParser">Number parser of the language.</param>
/// <param name="Fillers">Normalized filler words to drop.</param>
/// <param name="UnitSynonyms">Normalized unit synonym → canonical unit id.</param>
public sealed record NormalizationRules(
    INumberParser NumberParser,
    IReadOnlySet<string> Fillers,
    IReadOnlyDictionary<string, string> UnitSynonyms);
