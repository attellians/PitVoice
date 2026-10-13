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

        // --- intents (needs the normalizer, which needs the number parser)
        var intents = new Dictionary<IntentId, IntentPhrases>();
        Normalizer? normalizer = null;
        if (numberParser is not null)
        {
            normalizer = new Normalizer(new NormalizationRules(numberParser, fillers, unitSynonyms));
        }

        if (dto.Intents is null || dto.Intents.Count == 0)
        {
            Error("'intents' must describe at least one intent", "intents");
        }
        else
        {
            var compiler = normalizer is null ? null : new TemplateCompiler(normalizer);
            var seenPatterns = new Dictionary<string, (IntentId Intent, string Source)>(StringComparer.Ordinal);

            foreach (var (intentKey, intentDto) in dto.Intents)
            {
                var intentId = new IntentId(intentKey);
                if (!IntentCatalog.TryGet(intentId, out var definition))
                {
                    Error($"unknown intent '{intentKey}'; the catalog has: {string.Join(", ", IntentCatalog.All.Select(d => d.Id))}", "intents", intentKey);
                    continue;
                }

                var templates = new List<Template>();
                if (intentDto?.Patterns is null || intentDto.Patterns.Count == 0)
                {
                    Error($"intent '{intentKey}' has no patterns", "intents", intentKey, "patterns");
                }
                else if (compiler is not null)
                {
                    for (var i = 0; i < intentDto.Patterns.Count; i++)
                    {
                        var index = i.ToString(CultureInfo.InvariantCulture);
                        var pattern = intentDto.Patterns[i];
                        if (string.IsNullOrWhiteSpace(pattern))
                        {
                            Error("empty pattern", "intents", intentKey, "patterns", index);
                            continue;
                        }

                        var errors = new List<string>();
                        var template = compiler.Compile(pattern, definition, errors);
                        foreach (var error in errors)
                        {
                            Error($"pattern \"{pattern}\": {error}", "intents", intentKey, "patterns", index);
                        }

                        if (template is null)
                        {
                            continue;
                        }

                        var key = string.Join(' ', template.Tokens.Select(t => t.ToString()));
                        if (seenPatterns.TryGetValue(key, out var previous))
                        {
                            Error($"pattern \"{pattern}\" duplicates \"{previous.Source}\" of intent '{previous.Intent}'", "intents", intentKey, "patterns", index);
                            continue;
                        }

                        seenPatterns[key] = (intentId, pattern);
                        templates.Add(template);
                    }
                }

                var response = intentDto?.Response?.Trim();
                if (string.IsNullOrEmpty(response))
                {
                    Error($"intent '{intentKey}' has no response", "intents", intentKey, "response");
                    response = string.Empty;
                }
                else
                {
                    foreach (var placeholder in Placeholders(response))
                    {
                        var allowed = placeholder switch
                        {
                            "amount" => definition.HasSlot(SlotKind.Amount),
                            "unit" => definition.HasSlot(SlotKind.Unit),
                            _ => false,
                        };
                        if (!allowed)
                        {
                            Error($"response uses {{{placeholder}}}, which intent '{intentKey}' does not provide", "intents", intentKey, "response");
                        }
                    }
                }

                intents[intentId] = new IntentPhrases(intentId, templates, response);
            }
        }

        foreach (var definition in IntentCatalog.All)
        {
            if (!intents.ContainsKey(definition.Id))
            {
                Warn($"intent '{definition.Id}' is not described; phrases for it will be rejected in this language", "intents");
            }
        }

        // --- responses
        var responses = new Dictionary<string, string>(StringComparer.Ordinal);
        if (dto.Responses is not null)
        {
            foreach (var (key, value) in dto.Responses)
            {
                if (string.IsNullOrWhiteSpace(value))
                {
                    Error($"response '{key}' is empty", "responses", key);
                    continue;
                }

                responses[key] = value.Trim();
            }
        }

        if (!responses.ContainsKey(Responses.ResponseBuilder.NotUnderstoodKey))
        {
            Error($"'responses.{Responses.ResponseBuilder.NotUnderstoodKey}' is required", "responses");
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
                        if (!intents.ContainsKey(command.Intent))
                        {
                            Error($"expected intent '{command.Intent}' is not described in this pack", "tests", index, "expect");
                        }

                        testedIntents.Add(command.Intent);
                    }
                }

                tests.Add(new PackTest(test.Say.Trim(), expect.Commands, lines.Get("tests", index)));
            }
        }

        foreach (var intentId in intents.Keys.Order())
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
            dto.DefaultVoice?.Trim(),
            numberParser!,
            pluralRules!,
            units,
            conjunctions,
            fillers,
            negations,
            intents,
            responses,
            tests,
            normalizer!);

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

    private readonly record struct ExpectParse(IReadOnlyList<ExpectedCommand>? Commands, bool Invalid);

    private static ExpectParse ParseExpect(object? expect, Action<string, string> error)
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
