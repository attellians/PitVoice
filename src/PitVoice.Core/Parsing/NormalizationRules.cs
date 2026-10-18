using PitVoice.Core.Numbers;

namespace PitVoice.Core.Parsing;

/// <summary>Language data the normalizer needs; extracted from a language pack.</summary>
/// <param name="NumberParser">Number parser of the language.</param>
/// <param name="Fillers">Normalized filler words to drop.</param>
/// <param name="UnitSynonyms">Normalized unit synonym → canonical unit id.</param>
/// <param name="WordForms">
/// Normalized word → the word it is read as when it stands alone (design D13): forms the recognizer swaps freely
/// ("переднее" for "передние"). Inside an entry of several words the words must be as written. Null when the pack
/// has none.
/// </param>
public sealed record NormalizationRules(
    INumberParser NumberParser,
    IReadOnlySet<string> Fillers,
    IReadOnlyDictionary<string, string> UnitSynonyms,
    IReadOnlyDictionary<string, string>? WordForms = null);
