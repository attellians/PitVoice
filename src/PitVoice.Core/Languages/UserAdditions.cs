using PitVoice.Core.Intents;
using PitVoice.Core.Parsing;

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
/// Merges user additions into a built-in pack (FR-LANG-5, design decision 9). Each pattern is checked by the rules of
/// <see cref="LanguagePackValidator"/>: the intent must be in the pack, slots must exist, the pattern must not be empty and
/// a pattern of a pit command needs at least two words. Any error leaves the whole file out and the built-in pack in
/// use. A pattern equal to a built-in pattern of the same intent, or repeated in the file, is skipped with a warning.
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
        void Add(IssueSeverity severity, AdditionPattern? pattern, string message) => issues.Add(new PackIssue(
            severity,
            additions.File,
            pattern?.Line,
            pattern is null ? string.Empty : $"intents/{pattern.Intent}/patterns",
            message));

        if (additions.Language is { } language && !string.Equals(language, pack.Language, StringComparison.Ordinal))
        {
            Add(IssueSeverity.Error, null, $"the file is for language '{language}', but it extends the '{pack.Language}' pack; name it {language}.yaml");
            return new PackAdditionsResult(pack, additions.File, Exists: true, Applied: false, issues);
        }

        var compiler = new TemplateCompiler(pack.Normalizer);
        var builtIn = new Dictionary<string, IntentId>(StringComparer.Ordinal);
        foreach (var template in pack.Templates)
        {
            builtIn.TryAdd(Key(template), template.Intent);
        }

        var added = new Dictionary<IntentId, List<Template>>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var pattern in additions.Patterns)
        {
            if (!IntentCatalog.TryGet(pattern.Intent, out var definition))
            {
                Add(IssueSeverity.Error, pattern, $"unknown intent '{pattern.Intent}'; additions can only extend intents of the pack");
                continue;
            }

            if (!pack.Intents.ContainsKey(pattern.Intent))
            {
                Add(IssueSeverity.Error, pattern, $"intent '{pattern.Intent}' is not described in the '{pack.Language}' pack, so it cannot get extra patterns");
                continue;
            }

            if (string.IsNullOrWhiteSpace(pattern.Pattern))
            {
                Add(IssueSeverity.Error, pattern, "empty pattern");
                continue;
            }

            var errors = new List<string>();
            var compiled = compiler.Compile(pattern.Pattern, definition, errors);
            foreach (var error in errors)
            {
                Add(IssueSeverity.Error, pattern, $"pattern \"{pattern.Pattern}\": {error}");
            }

            if (compiled is null)
            {
                continue;
            }

            if (definition.ChangesPitSetup && compiled.RequiredLength < LanguagePackValidator.MinPitPatternWords)
            {
                Add(IssueSeverity.Error, pattern,
                    $"pattern \"{pattern.Pattern}\" has a single word; intent '{pattern.Intent}' changes the pit stop and needs at least {LanguagePackValidator.MinPitPatternWords} words");
                continue;
            }

            var key = Key(compiled);
            if (builtIn.TryGetValue(key, out var owner))
            {
                if (owner == pattern.Intent)
                {
                    Add(IssueSeverity.Warning, pattern, $"pattern \"{pattern.Pattern}\" is already built in; skipped");
                }
                else
                {
                    Add(IssueSeverity.Error, pattern, $"pattern \"{pattern.Pattern}\" is a built-in pattern of intent '{owner}'");
                }

                continue;
            }

            if (!seen.Add(key))
            {
                Add(IssueSeverity.Warning, pattern, $"pattern \"{pattern.Pattern}\" is repeated in the file; skipped");
                continue;
            }

            if (!added.TryGetValue(pattern.Intent, out var list))
            {
                added[pattern.Intent] = list = [];
            }

            list.Add(compiled with { IsUserAddition = true });
        }

        if (issues.Any(i => i.Severity == IssueSeverity.Error))
        {
            return new PackAdditionsResult(pack, additions.File, Exists: true, Applied: false, issues);
        }

        return new PackAdditionsResult(pack.WithTemplates(added, additions.File), additions.File, Exists: true, Applied: true, issues);
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

    private static string Key(Template template) => string.Join(' ', template.Tokens.Select(t => t.ToString()));
}
