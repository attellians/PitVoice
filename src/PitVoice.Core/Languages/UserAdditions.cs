namespace PitVoice.Core.Languages;

/// <summary>Outcome of applying a user additions file to a built-in pack.</summary>
/// <param name="Pack">The pack with the additions, or the built-in pack when the file is missing or has errors.</param>
/// <param name="File">Path of the additions file of this language (it may not exist).</param>
/// <param name="Exists">True when the file exists.</param>
/// <param name="Applied">True when the file was read and merged; false when it is missing or has errors.</param>
/// <param name="Issues">Errors (the file was not applied) and warnings (skipped duplicates).</param>
public sealed record PackAdditionsResult(LanguagePack Pack, string File, bool Exists, bool Applied, IReadOnlyList<PackIssue> Issues)
{
    public IEnumerable<PackIssue> Errors => Issues.Where(i => i.Severity == IssueSeverity.Error);

    public IEnumerable<PackIssue> Warnings => Issues.Where(i => i.Severity == IssueSeverity.Warning);
}

/// <summary>
/// Merges user additions into a built-in pack (FR-LANG-5, design D7). Each word is checked by the rules of the built-in
/// lexicon (<see cref="LexiconWords"/>): no slots, digits, fillers, negations or conjunctions, and no word in both a
/// class and its opposite. Any error leaves the whole file out and the built-in pack in use. A word already in its class,
/// or repeated in the file, is skipped with a warning. One word never makes a command alone: every rule needs two.
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

    /// <summary>Merges already read additions into <paramref name="pack"/>.</summary>
    public static PackAdditionsResult Merge(LanguagePack pack, UserPackAdditions additions)
    {
        ArgumentNullException.ThrowIfNull(pack);
        ArgumentNullException.ThrowIfNull(additions);

        var issues = new List<PackIssue>();
        void Add(IssueSeverity severity, AdditionWord? word, string message) => issues.Add(new PackIssue(
            severity,
            additions.File,
            word?.Line,
            word is null ? string.Empty : $"lexicon/{word.Class}",
            message));

        if (additions.Language is { } language && !string.Equals(language, pack.Language, StringComparison.Ordinal))
        {
            Add(IssueSeverity.Error, null, $"the file is for language '{language}', but it extends the '{pack.Language}' pack; name it {language}.yaml");
            return new PackAdditionsResult(pack, additions.File, Exists: true, Applied: false, issues);
        }

        var checker = new LexiconWords(pack.Normalizer, pack.Conjunctions, pack.Negations);
        var added = new List<LexiconEntry>();
        foreach (var word in additions.Words)
        {
            var entry = checker.Check(word.Class, word.Text, isUserAddition: true, message => Add(IssueSeverity.Error, word, message));
            if (entry is null)
            {
                continue;
            }

            if (pack.Lexicon.Contains(entry.Class, entry.Text))
            {
                Add(IssueSeverity.Warning, word, $"'{entry.Text}' is already built into '{entry.Class}'; skipped");
                continue;
            }

            if (added.Any(e => e.Class == entry.Class && e.Text == entry.Text))
            {
                Add(IssueSeverity.Warning, word, $"'{entry.Text}' is repeated in the file; skipped");
                continue;
            }

            added.Add(entry);
        }

        foreach (var (action, keep) in LexiconWords.Opposites(pack.Lexicon.Entries.Concat(added)))
        {
            var own = action.IsUserAddition ? action : keep;
            if (!own.IsUserAddition)
            {
                continue;
            }

            var word = additions.Words.FirstOrDefault(w => w.Class == own.Class);
            Add(IssueSeverity.Error, word, $"'{own.Text}' cannot be in both '{action.Class}' and '{keep.Class}': the meanings are opposite");
        }

        if (issues.Any(i => i.Severity == IssueSeverity.Error))
        {
            return new PackAdditionsResult(pack, additions.File, Exists: true, Applied: false, issues);
        }

        return new PackAdditionsResult(pack.WithEntries(added, additions.File), additions.File, Exists: true, Applied: true, issues);
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
