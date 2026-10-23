using PitVoice.Core.Intents;
using PitVoice.Core.Parsing;

namespace PitVoice.Core.Languages;

/// <summary>Outcome of applying a user additions file to a built-in pack.</summary>
/// <param name="Pack">The pack with the additions, or the built-in pack when the file is missing or has errors.</param>
/// <param name="File">Path of the additions file of this language (it may not exist).</param>
/// <param name="Exists">True when the file exists.</param>
/// <param name="Applied">True when the file was read and merged; false when it is missing or has errors.</param>
/// <param name="Issues">Errors (the file was not applied) and warnings (skipped entries).</param>
public sealed record PackAdditionsResult(LanguagePack Pack, string File, bool Exists, bool Applied, IReadOnlyList<PackIssue> Issues)
{
    public IEnumerable<PackIssue> Errors => Issues.Where(i => i.Severity == IssueSeverity.Error);

    public IEnumerable<PackIssue> Warnings => Issues.Where(i => i.Severity == IssueSeverity.Warning);

    /// <summary>
    /// True when the "Own phrases" page cannot show the file (design D10): it was not read, or it is for another language.
    /// Errors of single entries do not count: the page shows those entries with their errors.
    /// </summary>
    public bool NotBuilderFormat => Exists && !Applied && Errors.Any(e => e.Entry is null);

    /// <summary>The entries that are applied, without the skipped ones: what a save writes (design D7). Null when not applied.</summary>
    public UserPackAdditions? Kept { get; init; }
}

/// <summary>
/// Merges user additions into a built-in pack (FR-LANG-5, design D4). The same check serves the file at load time, the
/// draft of the "Own phrases" page and <c>pitvoice-cli validate</c>. A fuel verb is checked by the rules of the built-in
/// lexicon (<see cref="LexiconWords"/>) and must give "‹verb› twenty liters" as a fuel amount. A ready phrase must have
/// two words or more, no conjunctions or reject words, and must not mean another command with the built-in words. Any
/// error leaves the whole file out and the built-in pack in use; an entry that already works or is repeated is skipped
/// with a warning.
/// </summary>
public static class UserAdditions
{
    /// <summary>Loads the additions file of <paramref name="pack"/>'s language from <paramref name="directory"/> and merges it.</summary>
    public static PackAdditionsResult Apply(LanguagePack pack, string directory)
    {
        ArgumentNullException.ThrowIfNull(pack);
        var file = UserAdditionsLoader.GetFile(directory, pack.Language);
        if (UserAdditionsLoader.LoadFile(file) is not { } loaded)
        {
            return new PackAdditionsResult(pack, file, Exists: false, Applied: false, []);
        }

        if (loaded.Additions is not { } additions)
        {
            return new PackAdditionsResult(pack, file, Exists: true, Applied: false, loaded.Issues);
        }

        return Merge(pack, additions);
    }

    /// <summary>Merges already read additions (a file or a draft) into the built-in <paramref name="pack"/>.</summary>
    public static PackAdditionsResult Merge(LanguagePack pack, UserPackAdditions additions)
    {
        ArgumentNullException.ThrowIfNull(pack);
        ArgumentNullException.ThrowIfNull(additions);

        var issues = new List<PackIssue>();
        void Add(IssueSeverity severity, string path, int? line, int? entry, AdditionProblem? problem, string? argument, string message) =>
            issues.Add(new PackIssue(severity, additions.File, line, path, message) { Entry = entry, Problem = problem, Argument = argument });
        void AddWord(IssueSeverity severity, AdditionWord word, AdditionProblem problem, string? argument, string message) =>
            Add(severity, $"lexicon/{word.Class}", word.Line, word.Entry, problem, argument, message);
        void AddPhrase(IssueSeverity severity, AdditionPhrase phrase, AdditionProblem problem, string? argument, string message) =>
            Add(severity, $"phrases/{phrase.Entry - additions.Words.Count}", phrase.Line, phrase.Entry, problem, argument, message);

        if (additions.Language is { } language && !string.Equals(language, pack.Language, StringComparison.Ordinal))
        {
            Add(IssueSeverity.Error, string.Empty, null, null, null, null,
                $"the file is for language '{language}', but it extends the '{pack.Language}' pack; name it {language}.yaml");
            return new PackAdditionsResult(pack, additions.File, Exists: true, Applied: false, issues);
        }

        var keptWords = CheckWords(pack, additions.Words, AddWord);
        var keptPhrases = CheckPhrases(pack, additions.Phrases, AddPhrase);

        if (issues.Any(i => i.Severity == IssueSeverity.Error))
        {
            return new PackAdditionsResult(pack, additions.File, Exists: true, Applied: false, issues);
        }

        var merged = pack.WithAdditions(keptWords.Select(k => k.Entry).ToArray(), keptPhrases.Select(k => k.Phrase).ToArray(), additions.File);
        var kept = additions with
        {
            Words = keptWords.Select(k => k.Word).ToArray(),
            Phrases = keptPhrases.Select(k => k.Source).ToArray(),
        };
        return new PackAdditionsResult(merged, additions.File, Exists: true, Applied: true, issues) { Kept = kept };
    }

    private delegate void Report<in T>(IssueSeverity severity, T entry, AdditionProblem problem, string? argument, string message);

    private static List<(AdditionWord Word, LexiconEntry Entry)> CheckWords(LanguagePack pack, IReadOnlyList<AdditionWord> words, Report<AdditionWord> report)
    {
        var checker = new LexiconWords(pack.Normalizer, pack.Conjunctions, pack.Negations);
        var added = new List<(AdditionWord Word, LexiconEntry Entry)>();
        foreach (var word in words)
        {
            var problem = Classify(pack, word.Text);
            var entry = checker.Check(word.Class, word.Text, isUserAddition: true,
                message => report(IssueSeverity.Error, word, problem.Problem, problem.Argument, message));
            if (entry is null)
            {
                continue;
            }

            if (pack.Lexicon.Contains(entry.Class, entry.Text))
            {
                report(IssueSeverity.Warning, word, AdditionProblem.AlreadyWorks, null, $"'{entry.Text}' is already built into '{entry.Class}'; skipped");
                continue;
            }

            if (added.Any(a => a.Entry.Class == entry.Class && a.Entry.Text == entry.Text))
            {
                report(IssueSeverity.Warning, word, AdditionProblem.Repeated, null, $"'{entry.Text}' is repeated in the file; skipped");
                continue;
            }

            added.Add((word, entry));
        }

        var rejected = new HashSet<AdditionWord>();
        foreach (var (action, keep) in LexiconWords.Opposites(pack.Lexicon.Entries.Concat(added.Select(a => a.Entry))))
        {
            var own = action.IsUserAddition ? action : keep;
            if (!own.IsUserAddition)
            {
                continue;
            }

            var word = added.First(a => a.Entry == own).Word;
            rejected.Add(word);
            report(IssueSeverity.Error, word, AdditionProblem.Negation, own.Text,
                $"'{own.Text}' cannot be in both '{action.Class}' and '{keep.Class}': the meanings are opposite");
        }

        // A verb that merges with the built-in words ("двадцать" alone, a unit) does not give an amount (design D4).
        foreach (var (word, entry) in added.Where(a => !rejected.Contains(a.Word) && a.Entry.Class == UserAdditionsLoader.FuelVerbClass))
        {
            if (FuelSample(pack, entry.Text) is not { } sample)
            {
                continue;
            }

            var parsed = new CommandParser(pack.WithAdditions([entry], [], string.Empty)).Parse(sample);
            if (parsed is not ParseResult.Accepted { Commands: [{ Amount: SampleAmount, Unit: FuelUnits.Liters } command] }
                || command.Intent != IntentCatalog.FuelAdd)
            {
                rejected.Add(word);
                report(IssueSeverity.Error, word, AdditionProblem.NotFuelVerb, entry.Text,
                    $"'{sample}' does not parse as adding {SampleAmount} liters: '{entry.Text}' does not work as a fuel verb");
            }
        }

        return added.Where(a => !rejected.Contains(a.Word)).ToList();
    }

    private const int SampleAmount = 20;

    /// <summary>"‹verb› двадцать литров" in the words of the pack; null when the pack has no word for 20 or for liters.</summary>
    private static string? FuelSample(LanguagePack pack, string verb)
    {
        var twenty = pack.NumberParser.Words
            .Order(StringComparer.Ordinal)
            .FirstOrDefault(w => pack.NumberParser.TryParse([w], out var value, out var consumed) && value == SampleAmount && consumed == 1);
        var liters = pack.Units.TryGetValue(FuelUnits.Liters, out var unit) ? unit.Synonyms.FirstOrDefault() : null;
        return twenty is null || liters is null ? null : $"{verb} {twenty} {liters}";
    }

    /// <summary>The kind of problem of a fuel verb that <see cref="LexiconWords.Check"/> rejects, for the page.</summary>
    private static (AdditionProblem Problem, string? Argument) Classify(LanguagePack pack, string raw)
    {
        var text = raw.Trim();
        if (text.Length == 0)
        {
            return (AdditionProblem.TooShort, null);
        }

        if (text.Any(c => c is '{' or '}' || char.IsDigit(c)))
        {
            return (AdditionProblem.Symbols, null);
        }

        var words = Normalizer.SplitWords(text);
        if (words.FirstOrDefault(pack.Negations.Contains) is { } negation)
        {
            return (AdditionProblem.Negation, negation);
        }

        return FindConjunction(pack, words) is { } conjunction
            ? (AdditionProblem.Conjunction, conjunction)
            : (AdditionProblem.Invalid, null);
    }

    private static List<(AdditionPhrase Source, ReadyPhrase Phrase)> CheckPhrases(LanguagePack pack, IReadOnlyList<AdditionPhrase> phrases, Report<AdditionPhrase> report)
    {
        var builtIn = new CommandParser(pack);
        var seen = new Dictionary<string, IntentId>(StringComparer.Ordinal);
        var kept = new List<(AdditionPhrase Source, ReadyPhrase Phrase)>();
        foreach (var phrase in phrases)
        {
            var text = phrase.Text.Trim();
            if (text.Any(c => c is '{' or '}' || char.IsDigit(c)))
            {
                report(IssueSeverity.Error, phrase, AdditionProblem.Symbols, null, $"'{text}' has digits or braces; write words only");
                continue;
            }

            if (BuilderCommands.PhraseOf(phrase.Intent) is null || !pack.Supports(phrase.Intent))
            {
                report(IssueSeverity.Error, phrase, AdditionProblem.UnknownCommand, phrase.Intent.Value,
                    $"command '{phrase.Intent}' is not available for a ready phrase in the '{pack.Language}' pack");
                continue;
            }

            var words = Normalizer.SplitWords(text);
            if (FindConjunction(pack, words) is { } conjunction)
            {
                report(IssueSeverity.Error, phrase, AdditionProblem.Conjunction, conjunction,
                    $"'{text}' contains the conjunction '{conjunction}', which splits the phrase before parsing");
                continue;
            }

            if (words.FirstOrDefault(pack.RejectWords.Contains) is { } reject)
            {
                report(IssueSeverity.Error, phrase, AdditionProblem.RejectWord, reject,
                    $"'{text}' contains the reject word '{reject}', which rejects the whole phrase");
                continue;
            }

            var tokens = pack.Normalizer.Normalize(text);
            if (tokens.Count < 2)
            {
                report(IssueSeverity.Error, phrase, AdditionProblem.TooShort, null,
                    $"'{text}' is shorter than two words without fillers; one word never changes the pit stop");
                continue;
            }

            var key = ReadyPhrase.KeyOf(tokens);
            if (seen.TryGetValue(key, out var earlier))
            {
                if (earlier == phrase.Intent)
                {
                    report(IssueSeverity.Warning, phrase, AdditionProblem.Repeated, null, $"'{text}' is repeated in the file; skipped");
                }
                else
                {
                    report(IssueSeverity.Error, phrase, AdditionProblem.AssignedElsewhere, earlier.Value,
                        $"'{text}' is already given the command '{earlier}' earlier in the file");
                }

                continue;
            }

            seen[key] = phrase.Intent;
            if (builtIn.Parse(text) is ParseResult.Accepted accepted)
            {
                if (accepted.Commands is [var only] && only.Intent == phrase.Intent)
                {
                    report(IssueSeverity.Warning, phrase, AdditionProblem.AlreadyWorks, null,
                        $"'{text}' already gives '{phrase.Intent}' with the built-in words; skipped");
                }
                else
                {
                    var meaning = string.Join(", ", accepted.Commands.Select(c => c.Intent.Value));
                    report(IssueSeverity.Error, phrase, AdditionProblem.MeansOther, meaning,
                        $"'{text}' already means '{meaning}' with the built-in words");
                }

                continue;
            }

            kept.Add((phrase, new ReadyPhrase(string.Join(' ', words), key, phrase.Intent)));
        }

        return kept;
    }

    private static string? FindConjunction(LanguagePack pack, IReadOnlyList<string> words)
    {
        foreach (var conjunction in pack.Conjunctions)
        {
            for (var start = 0; start + conjunction.Count <= words.Count; start++)
            {
                if (words.Skip(start).Take(conjunction.Count).SequenceEqual(conjunction, StringComparer.Ordinal))
                {
                    return string.Join(' ', conjunction);
                }
            }
        }

        return null;
    }

    /// <summary>
    /// Warnings for additions files in <paramref name="directory"/> whose language has no built-in pack among
    /// <paramref name="languages"/>; such files are ignored and never create a language.
    /// </summary>
    public static IReadOnlyList<PackIssue> FindOrphans(string directory, IEnumerable<string> languages)
    {
        ArgumentNullException.ThrowIfNull(directory);
        ArgumentNullException.ThrowIfNull(languages);
        if (!Directory.Exists(directory))
        {
            return [];
        }

        var known = new HashSet<string>(languages, StringComparer.OrdinalIgnoreCase);
        return Directory.GetFiles(directory, LanguagePackLoader.FilePattern)
            .Order(StringComparer.Ordinal)
            .Where(f => !known.Contains(Path.GetFileNameWithoutExtension(f)))
            .Select(f => new PackIssue(IssueSeverity.Warning, f, null, string.Empty,
                $"no built-in language pack '{Path.GetFileNameWithoutExtension(f)}'; the additions file is ignored"))
            .ToArray();
    }
}
