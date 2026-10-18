using System.Globalization;
using PitVoice.Core.Intents;
using PitVoice.Core.Languages.Dto;
using PitVoice.Core.Numbers;
using PitVoice.Core.Parsing;
using PitVoice.Core.Plural;

namespace PitVoice.Core.Languages;

/// <summary>Outcome of validation: the compiled pack when there are no errors, plus every issue found.</summary>
public sealed record ValidationResult(LanguagePack? Pack, IReadOnlyList<PackIssue> Issues)
{
    public bool IsValid => Pack is not null;
}

/// <summary>
/// Checks a raw pack against the format rules (FR-LANG-3, TZ §6) and compiles it. All problems are collected, not
/// only the first one, each with the file and line it refers to.
/// </summary>
public static class LanguagePackValidator
{
    public static ValidationResult Validate(LanguagePackDto dto, string fileName)
    {
        ArgumentNullException.ThrowIfNull(dto);
        return Validate(dto, fileName, YamlLineIndex.Empty);
    }

    internal static ValidationResult Validate(LanguagePackDto dto, string fileName, YamlLineIndex lines)
    {
        var issues = new List<PackIssue>();
        void Add(IssueSeverity severity, string message, string[] path)
        {
            var segments = path.Where(s => s.Length > 0).ToArray();
            issues.Add(new PackIssue(severity, fileName, lines.Get(segments), string.Join('/', segments), message));
        }

        void Error(string message, params string[] path) => Add(IssueSeverity.Error, message, path);
        void Warn(string message, params string[] path) => Add(IssueSeverity.Warning, message, path);

        // --- metadata and language-specific code
        var language = dto.Language?.Trim() ?? string.Empty;
        INumberParser? numberParser = null;
        if (language.Length == 0)
        {
            Error("'language' is required", "language");
        }
        else if (!LanguageSupport.TryGetNumberParser(language, out numberParser))
        {
            Error($"no number parser for language '{language}'; supported: {string.Join(", ", LanguageSupport.LanguagesWithNumbers)}", "language");
        }

        var pluralId = dto.PluralRules?.Trim();
        if (string.IsNullOrEmpty(pluralId))
        {
            pluralId = language;
        }

        IPluralRules? pluralRules = null;
        if (pluralId.Length > 0 && !LanguageSupport.TryGetPluralRules(pluralId, out pluralRules))
        {
            Error($"unknown plural rules '{pluralId}'; supported: {string.Join(", ", LanguageSupport.PluralRuleSets)}", "plural_rules");
        }

        var name = dto.Name?.Trim();
        if (string.IsNullOrEmpty(name))
        {
            Error("'name' is required", "name");
        }

        // --- units
        var units = new Dictionary<string, UnitDefinition>(StringComparer.Ordinal);
        var unitSynonyms = new Dictionary<string, string>(StringComparer.Ordinal);
        if (dto.Units is null || dto.Units.Count == 0)
        {
            Error("'units' must describe at least one fuel unit", "units");
        }
        else
        {
            foreach (var (unitId, unit) in dto.Units)
            {
                if (!FuelUnits.IsKnown(unitId))
                {
                    Error($"unknown unit '{unitId}'; known units: {string.Join(", ", FuelUnits.All)}", "units", unitId);
                    continue;
                }

                var synonyms = new List<string>();
                if (unit?.Synonyms is null || unit.Synonyms.Count == 0)
                {
                    Error($"unit '{unitId}' has no synonyms", "units", unitId, "synonyms");
                }
                else
                {
                    for (var i = 0; i < unit.Synonyms.Count; i++)
                    {
                        var synonym = Normalizer.NormalizeWord(unit.Synonyms[i] ?? string.Empty);
                        if (synonym.Length == 0)
                        {
                            Error("empty synonym", "units", unitId, "synonyms", i.ToString(CultureInfo.InvariantCulture));
                            continue;
                        }

                        if (synonym.Contains(' '))
                        {
                            Error($"synonym '{synonym}' must be a single word", "units", unitId, "synonyms", i.ToString(CultureInfo.InvariantCulture));
                            continue;
                        }

                        if (unitSynonyms.TryGetValue(synonym, out var other) && other != unitId)
                        {
                            Error($"synonym '{synonym}' is already used by unit '{other}'", "units", unitId, "synonyms", i.ToString(CultureInfo.InvariantCulture));
                            continue;
                        }

                        unitSynonyms[synonym] = unitId;
                        synonyms.Add(synonym);
                    }
                }

                var forms = new Dictionary<PluralCategory, string>();
                if (unit?.Forms is null || unit.Forms.Count == 0)
                {
                    Error($"unit '{unitId}' has no plural forms", "units", unitId, "forms");
                }
                else
                {
                    foreach (var (category, form) in unit.Forms)
                    {
                        if (!Enum.TryParse<PluralCategory>(category, ignoreCase: true, out var parsed))
                        {
                            Error($"unknown plural category '{category}'; use one, two, few, many, other, zero", "units", unitId, "forms", category);
                            continue;
                        }

                        if (string.IsNullOrWhiteSpace(form))
                        {
                            Error($"form '{category}' is empty", "units", unitId, "forms", category);
                            continue;
                        }

                        forms[parsed] = form.Trim();
                    }

                    if (pluralRules is not null)
                    {
                        foreach (var required in pluralRules.Categories)
                        {
                            if (!forms.ContainsKey(required))
                            {
                                Error($"unit '{unitId}' has no form for plural category '{required.ToString().ToLowerInvariant()}' required by plural rules '{pluralId}'", "units", unitId, "forms");
                            }
                        }
                    }
                }

                units[unitId] = new UnitDefinition(unitId, synonyms, forms);
            }
        }

        // --- word lists
        var fillers = NormalizeWordSet(dto.Fillers, "fillers", Error);
        var negations = NormalizeWordSet(dto.Negations, "negations", Error);
        var rejectWords = NormalizeWordSet(dto.RejectWords, "reject_words", Error);
        var conjunctions = new List<IReadOnlyList<string>>();
        if (dto.Conjunctions is not null)
        {
            for (var i = 0; i < dto.Conjunctions.Count; i++)
            {
                var words = Normalizer.SplitWords(dto.Conjunctions[i] ?? string.Empty);
                if (words.Count == 0)
                {
                    Error("empty conjunction", "conjunctions", i.ToString(CultureInfo.InvariantCulture));
                    continue;
                }

                conjunctions.Add(words);
            }
        }

        conjunctions.Sort((a, b) =>
        {
            var byLength = b.Count.CompareTo(a.Count);
            return byLength != 0 ? byLength : string.CompareOrdinal(string.Join(' ', a), string.Join(' ', b));
        });

        // --- word forms: variant → the word it is read as (design D13)
        var wordForms = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var (rawWord, rawForms) in dto.WordForms ?? [])
        {
            var word = Normalizer.NormalizeWord(rawWord);
            if (word.Length == 0 || word.Contains(' '))
            {
                Error($"'{rawWord}' in 'word_forms' must be a single word", "word_forms", rawWord);
                continue;
            }

            foreach (var rawForm in rawForms ?? [])
            {
                var form = Normalizer.NormalizeWord(rawForm ?? string.Empty);
                if (form.Length == 0 || form.Contains(' '))
                {
                    Error($"a form of '{word}' in 'word_forms' must be a single word", "word_forms", rawWord);
                }
                else if (form == word)
                {
                    Error($"'{form}' is listed as a form of itself", "word_forms", rawWord);
                }
                else if (!wordForms.TryAdd(form, word))
                {
                    Error($"'{form}' is a form of both '{wordForms[form]}' and '{word}'", "word_forms", rawWord);
                }
            }
        }

        foreach (var word in wordForms.Values.Distinct().Order(StringComparer.Ordinal))
        {
            if (wordForms.ContainsKey(word))
            {
                Error($"'{word}' is a form of another word and cannot have forms itself", "word_forms", word);
            }
        }

        // --- lexicon (needs the normalizer, which needs the number parser)
        Normalizer? normalizer = null;
        if (numberParser is not null)
        {
            normalizer = new Normalizer(new NormalizationRules(numberParser, fillers, unitSynonyms, wordForms));
        }

        var entries = new List<LexiconEntry>();
        if (dto.Lexicon is null || dto.Lexicon.Count == 0)
        {
            Error("'lexicon' must describe the words of at least one group", "lexicon");
        }
        else
        {
            // Without a number parser the words cannot be normalized, but the classes are still checked.
            var words = normalizer is null ? null : new LexiconWords(normalizer, conjunctions, negations);
            foreach (var raw in LexiconReader.Read(dto.Lexicon, (message, path) => Error(message, ["lexicon", .. path])))
            {
                string[] path = ["lexicon", .. raw.Path];
                var entry = words?.Check(raw.Class, raw.Text, isUserAddition: false, message => Error(message, path));
                if (entry is null)
                {
                    continue;
                }

                if (entries.Any(e => e.Class == entry.Class && e.Text == entry.Text))
                {
                    Warn($"'{entry.Text}' is repeated in '{entry.Class}'; skipped", path);
                    continue;
                }

                entries.Add(entry);
            }

            foreach (var (action, keep) in LexiconWords.Opposites(entries))
            {
                Error($"'{action.Text}' is in both '{action.Class}' and '{keep.Class}': a word cannot mean the command and keeping it",
                    "lexicon", LexiconSchema.Name(keep.Class.Group));
            }
        }

        // A reject word must not be a word of any command, or that command could never be said (design D12).
        var commandWords = new Dictionary<string, string>(StringComparer.Ordinal);
        void Claim(IEnumerable<string> words, string where)
        {
            foreach (var word in words)
            {
                commandWords.TryAdd(word, where);
            }
        }

        foreach (var entry in entries)
        {
            Claim(entry.Words, $"'{entry.Class}'");
        }

        Claim(fillers, "'fillers'");
        Claim(negations, "'negations'");
        Claim(conjunctions.SelectMany(c => c), "'conjunctions'");
        Claim(unitSynonyms.Keys, "'units'");
        if (numberParser is not null)
        {
            Claim(numberParser.Words, "number words");
        }

        foreach (var word in rejectWords.Order(StringComparer.Ordinal))
        {
            if (commandWords.TryGetValue(word, out var where))
            {
                Error($"reject word '{word}' is also in {where}: a phrase with it would always be rejected", "reject_words");
            }
        }

        var lexicon = new Lexicon(entries);
        var available = IntentCatalog.All
            .Select(d => d.Id)
            .Where(id => WordRules.IsAvailable(id, lexicon, negations.Count > 0))
            .ToHashSet();
        var unavailable = IntentCatalog.All.Select(d => d.Id).Where(id => !available.Contains(id)).ToArray();
        if (dto.Lexicon is { Count: > 0 } && unavailable.Length > 0)
        {
            Warn($"the pack is incomplete: the lexicon has no words for {string.Join(", ", unavailable)}; phrases for them will be rejected in this language", "lexicon");
        }

        // --- responses: of intents and of outcomes
        var responses = new Dictionary<string, string>(StringComparer.Ordinal);
        if (dto.Responses is not null)
        {
            foreach (var (key, value) in dto.Responses)
            {
                if (Responses.ResponseBuilder.UnusedKeys.Contains(key))
                {
                    Warn($"response '{key}' is not used: the intent or outcome it answered was removed", "responses", key);
                    continue;
                }

                var isIntent = IntentCatalog.TryGet(new IntentId(key), out var definition);
                if (!isIntent && !Responses.ResponseBuilder.RequiredKeys.Contains(key))
                {
                    Error($"unknown response '{key}': it is neither a catalog intent nor one of {string.Join(", ", Responses.ResponseBuilder.RequiredKeys)}", "responses", key);
                    continue;
                }

                if (string.IsNullOrWhiteSpace(value))
                {
                    Error($"response '{key}' is empty", "responses", key);
                    continue;
                }

                foreach (var placeholder in Placeholders(value))
                {
                    var allowed = isIntent && placeholder switch
                    {
                        "amount" => definition!.HasSlot(SlotKind.Amount),
                        "unit" => definition!.HasSlot(SlotKind.Unit),
                        _ => false,
                    };
                    if (!allowed)
                    {
                        Error($"response '{key}' uses {{{placeholder}}}, which it does not provide", "responses", key);
                    }
                }

                if (isIntent && !available.Contains(definition!.Id))
                {
                    Warn($"response '{key}' is not used: the lexicon has no words for this intent", "responses", key);
                }

                responses[key] = value.Trim();
            }
        }

        foreach (var required in Responses.ResponseBuilder.RequiredKeys)
        {
            if (!responses.ContainsKey(required))
            {
                Error($"'responses.{required}' is required", "responses");
            }
        }

        foreach (var intent in IntentCatalog.All.Select(d => d.Id).Where(available.Contains))
        {
            if (!responses.ContainsKey(intent.Value))
            {
                Error($"intent '{intent}' has no response", "responses");
            }
        }

        // --- tests
        var tests = new List<PackTest>();
        var testedIntents = new HashSet<IntentId>();
        if (dto.Tests is not null)
        {
            for (var i = 0; i < dto.Tests.Count; i++)
            {
                var index = i.ToString(CultureInfo.InvariantCulture);
                var test = dto.Tests[i];
                if (test is null || string.IsNullOrWhiteSpace(test.Say))
                {
                    Error("test has no 'say' phrase", "tests", index);
                    continue;
                }

                var expect = ParseExpect(test.Expect, (message, segment) => Error(message, "tests", index, "expect", segment));
                if (expect.Invalid)
                {
                    continue;
                }

                if (expect.Commands is not null)
                {
                    foreach (var command in expect.Commands)
                    {
                        if (!available.Contains(command.Intent))
                        {
                            Error($"expected intent '{command.Intent}' has no words in this pack", "tests", index, "expect");
                        }

                        testedIntents.Add(command.Intent);
                    }
                }

                tests.Add(new PackTest(test.Say.Trim(), expect.Commands, lines.Get("tests", index)));
            }
        }

        foreach (var intentId in available.Order())
        {
            if (!testedIntents.Contains(intentId))
            {
                Error($"intent '{intentId}' is not covered by any test phrase", "tests");
            }
        }

        if (issues.Any(i => i.Severity == IssueSeverity.Error))
        {
            return new ValidationResult(null, issues);
        }

        var pack = new LanguagePack(
            fileName,
            language,
            name!,
            dto.Version ?? 1,
            dto.AsrModel?.Trim(),
            numberParser!,
            pluralRules!,
            units,
            conjunctions,
            fillers,
            negations,
            lexicon,
            responses,
            tests,
            normalizer!)
        {
            RejectWords = rejectWords,
        };

        return new ValidationResult(pack, issues);
    }

    private static HashSet<string> NormalizeWordSet(List<string?>? source, string section, Action<string, string[]> error)
    {
        var result = new HashSet<string>(StringComparer.Ordinal);
        if (source is null)
        {
            return result;
        }

        for (var i = 0; i < source.Count; i++)
        {
            var word = Normalizer.NormalizeWord(source[i] ?? string.Empty);
            if (word.Length == 0)
            {
                error($"empty entry in '{section}'", [section, i.ToString(CultureInfo.InvariantCulture)]);
                continue;
            }

            if (word.Contains(' '))
            {
                error($"'{word}' must be a single word", [section, i.ToString(CultureInfo.InvariantCulture)]);
                continue;
            }

            result.Add(word);
        }

        return result;
    }

    private static IEnumerable<string> Placeholders(string text)
    {
        var start = text.IndexOf('{');
        while (start >= 0)
        {
            var end = text.IndexOf('}', start + 1);
            if (end < 0)
            {
                yield break;
            }

            yield return text[(start + 1)..end];
            start = text.IndexOf('{', end + 1);
        }
    }

    internal readonly record struct ExpectParse(IReadOnlyList<ExpectedCommand>? Commands, bool Invalid);

    /// <summary>Reads an <c>expect</c> value: <c>reject</c> or a list of commands (also used by the sample set format).</summary>
    internal static ExpectParse ParseExpect(object? expect, Action<string, string> error)
    {
        switch (expect)
        {
            case string scalar when scalar.Trim().Equals("reject", StringComparison.OrdinalIgnoreCase):
                return new ExpectParse(null, false);

            case string scalar:
                error($"'expect' must be 'reject' or a list of commands, got '{scalar}'", string.Empty);
                return new ExpectParse(null, true);

            case null:
                error("'expect' is required: 'reject' or a list of commands", string.Empty);
                return new ExpectParse(null, true);

            case IList<object> list:
                var commands = new List<ExpectedCommand>();
                var invalid = false;
                for (var i = 0; i < list.Count; i++)
                {
                    var index = i.ToString(CultureInfo.InvariantCulture);
                    if (list[i] is not IDictionary<object, object> map)
                    {
                        error("each expected command must be a mapping with 'intent' and optional 'amount', 'unit'", index);
                        invalid = true;
                        continue;
                    }

                    string? intent = null;
                    int? amount = null;
                    string? unit = null;
                    foreach (var (key, value) in map)
                    {
                        var text = value?.ToString()?.Trim();
                        switch (key?.ToString())
                        {
                            case "intent":
                                intent = text;
                                break;
                            case "amount":
                                if (int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out var parsed))
                                {
                                    amount = parsed;
                                }
                                else
                                {
                                    error($"'amount' must be an integer, got '{text}'", index);
                                    invalid = true;
                                }

                                break;
                            case "unit":
                                unit = text;
                                break;
                            default:
                                error($"unknown key '{key}' in expected command", index);
                                invalid = true;
                                break;
                        }
                    }

                    if (string.IsNullOrEmpty(intent))
                    {
                        error("expected command has no 'intent'", index);
                        invalid = true;
                        continue;
                    }

                    if (!IntentCatalog.Contains(new IntentId(intent)))
                    {
                        error($"unknown intent '{intent}'", index);
                        invalid = true;
                        continue;
                    }

                    commands.Add(new ExpectedCommand(new IntentId(intent), amount, unit));
                }

                return new ExpectParse(commands, invalid);

            default:
                error("'expect' must be 'reject' or a list of commands", string.Empty);
                return new ExpectParse(null, true);
        }
    }
}
