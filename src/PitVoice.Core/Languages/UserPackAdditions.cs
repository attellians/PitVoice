using System.Globalization;
using YamlDotNet.Core;
using YamlDotNet.RepresentationModel;

namespace PitVoice.Core.Languages;

/// <summary>A word the user added to a class of the built-in lexicon.</summary>
/// <param name="Class">Class the word is added to.</param>
/// <param name="Text">Word as written in the file.</param>
/// <param name="Line">1-based line of the word in the file, when known.</param>
public sealed record AdditionWord(WordClass Class, string Text, int? Line);

/// <summary>
/// Contents of a user additions file <c>&lt;language&gt;.yaml</c> (FR-LANG-5, design D7): only words for classes of the
/// lexicon format. An empty file (for example the template with comments only) adds nothing.
/// </summary>
/// <param name="File">Path of the file.</param>
/// <param name="Language">Language named in the file; null when the file does not name one.</param>
/// <param name="Words">Words in file order.</param>
public sealed record UserPackAdditions(string File, string? Language, IReadOnlyList<AdditionWord> Words)
{
    public bool IsEmpty => Words.Count == 0;
}

/// <summary>Result of reading an additions file: the additions when the format is valid, plus every problem found.</summary>
public sealed record UserAdditionsLoadResult(string File, UserPackAdditions? Additions, IReadOnlyList<PackIssue> Issues)
{
    public bool IsSuccess => Additions is not null;
}

/// <summary>
/// Reads user additions files. The directory is an explicit parameter, else <see cref="EnvironmentVariable"/>, else
/// <c>%AppData%\PitVoice\languages</c>. Only <c>language</c> and <c>lexicon</c> are allowed; anything else is an error
/// with the file and line, so a typo never silently changes nothing.
/// </summary>
public static class UserAdditionsLoader
{
    public const string EnvironmentVariable = "PITVOICE_USER_LANGUAGES";

    public static string DefaultDirectory =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "PitVoice", "languages");

    public static string ResolveDirectory(string? directory)
    {
        if (!string.IsNullOrWhiteSpace(directory))
        {
            return Path.GetFullPath(directory);
        }

        var fromEnvironment = Environment.GetEnvironmentVariable(EnvironmentVariable);
        return Path.GetFullPath(string.IsNullOrWhiteSpace(fromEnvironment) ? DefaultDirectory : fromEnvironment);
    }

    /// <summary>Path of the additions file of <paramref name="language"/> in <paramref name="directory"/>.</summary>
    public static string GetFile(string directory, string language)
    {
        ArgumentNullException.ThrowIfNull(directory);
        ArgumentException.ThrowIfNullOrWhiteSpace(language);
        return Path.Combine(directory, language + ".yaml");
    }

    /// <summary>
    /// Creates the additions file of <paramref name="language"/> with a comment about the format, unless it exists. The
    /// template adds nothing until the user writes words (spec: No additions file).
    /// </summary>
    public static string CreateTemplate(string directory, string language)
    {
        var path = GetFile(directory, language);
        if (File.Exists(path))
        {
            return path;
        }

        Directory.CreateDirectory(directory);
        var (verb, target) = language == "ru" ? ("плесни", "перёд") : ("pour", "noses");
        File.WriteAllText(path, $$"""
            # PitVoice: your own words for the '{{language}}' language pack.
            # Words are added to classes of the built-in lexicon and work with all its other words:
            # a new fuel verb works with every amount, a new tire target with every tire verb.
            # Groups: fuel, tires, windshield, repair, clear. Classes: action, object, keep (tires, windshield),
            # fast (repair), targets (tires: all, left, right, front, rear, lf, rf, lr, rr).
            # Plain words only: no {slots} and no digits. One word never changes the pit stop by itself.
            # After saving, press "Reload". See docs/adding-a-language.md.
            #
            # lexicon:
            #   fuel: { action: [{{verb}}] }
            #   tires: { targets: { front: [{{target}}] } }

            """);
        return path;
    }

    /// <summary>Reads the file; null when it does not exist.</summary>
    public static UserAdditionsLoadResult? LoadFile(string path)
    {
        ArgumentNullException.ThrowIfNull(path);
        if (!File.Exists(path))
        {
            return null;
        }

        string yaml;
        try
        {
            yaml = File.ReadAllText(path);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return Fail(path, null, $"cannot read the file: {e.Message}");
        }

        return LoadText(yaml, path);
    }

    public static UserAdditionsLoadResult LoadText(string yaml, string file)
    {
        ArgumentNullException.ThrowIfNull(yaml);
        ArgumentNullException.ThrowIfNull(file);

        var stream = new YamlStream();
        try
        {
            using var reader = new StringReader(yaml);
            stream.Load(reader);
        }
        catch (YamlException e)
        {
            return Fail(file, (int)e.Start.Line, $"invalid YAML: {e.Message}");
        }

        if (stream.Documents.Count == 0 || stream.Documents[0].RootNode is YamlScalarNode { Value: null or "" })
        {
            return new UserAdditionsLoadResult(file, new UserPackAdditions(file, null, []), []);
        }

        var issues = new List<PackIssue>();
        void Error(YamlNode node, string path, string message) =>
            issues.Add(new PackIssue(IssueSeverity.Error, file, (int)node.Start.Line, path, message));

        if (stream.Documents[0].RootNode is not YamlMappingNode root)
        {
            return Fail(file, (int)stream.Documents[0].RootNode.Start.Line, "the file must be a mapping with 'lexicon'");
        }

        string? language = null;
        var words = new List<AdditionWord>();
        foreach (var (keyNode, value) in root.Children)
        {
            var key = (keyNode as YamlScalarNode)?.Value;
            switch (key)
            {
                case "language":
                    language = (value as YamlScalarNode)?.Value?.Trim();
                    if (string.IsNullOrEmpty(language))
                    {
                        Error(value, "language", "'language' must be a language code, e.g. ru");
                    }

                    break;

                case "lexicon":
                    ReadLexicon(value, words, Error);
                    break;

                case "intents":
                    Error(keyNode, "intents",
                        "the old format with phrases in 'intents' is not supported: words are added to classes of 'lexicon', e.g. lexicon: { fuel: { action: [pour] } } (docs/adding-a-language.md)");
                    break;

                default:
                    Error(keyNode, key ?? string.Empty, $"unknown field '{key}': an additions file may only have 'language' and 'lexicon'");
                    break;
            }
        }

        return issues.Count > 0
            ? new UserAdditionsLoadResult(file, null, issues)
            : new UserAdditionsLoadResult(file, new UserPackAdditions(file, language, words), issues);
    }

    private static void ReadLexicon(YamlNode node, List<AdditionWord> words, Action<YamlNode, string, string> error)
    {
        if (node is not YamlMappingNode groups)
        {
            error(node, "lexicon", "'lexicon' must map groups (fuel, tires, ...) to their classes");
            return;
        }

        foreach (var (groupKey, rolesNode) in groups.Children)
        {
            var groupName = (groupKey as YamlScalarNode)?.Value?.Trim() ?? string.Empty;
            var groupPath = $"lexicon/{groupName}";
            if (!LexiconSchema.TryGetGroup(groupName, out var group))
            {
                error(groupKey, groupPath, $"unknown word class group '{groupName}'; the format has: {string.Join(", ", LexiconSchema.GroupNames)}");
                continue;
            }

            if (rolesNode is not YamlMappingNode roles)
            {
                error(rolesNode, groupPath, $"group '{groupName}' must map classes ({string.Join(", ", LexiconSchema.RoleNames(group))}) to words");
                continue;
            }

            foreach (var (roleKey, value) in roles.Children)
            {
                var roleName = (roleKey as YamlScalarNode)?.Value?.Trim() ?? string.Empty;
                var rolePath = $"{groupPath}/{roleName}";
                if (roleName == LexiconSchema.TargetsKey && LexiconSchema.HasRole(group, WordRole.Target))
                {
                    if (value is not YamlMappingNode targets)
                    {
                        error(value, rolePath, $"'{groupName}.targets' must map targets ({string.Join(", ", LexiconSchema.TargetNames)}) to words");
                        continue;
                    }

                    foreach (var (targetKey, targetWords) in targets.Children)
                    {
                        var targetName = (targetKey as YamlScalarNode)?.Value?.Trim() ?? string.Empty;
                        if (!LexiconSchema.TryGetTarget(targetName, out var target))
                        {
                            error(targetKey, $"{rolePath}/{targetName}", $"unknown tire target '{targetName}'; targets: {string.Join(", ", LexiconSchema.TargetNames)}");
                            continue;
                        }

                        ReadWords(new WordClass(group, WordRole.Target, target), targetWords, $"{rolePath}/{targetName}", words, error);
                    }

                    continue;
                }

                if (roleName == LexiconSchema.CompoundsKey && LexiconSchema.HasRole(group, WordRole.Compound))
                {
                    if (value is not YamlMappingNode compounds)
                    {
                        error(value, rolePath, $"'{groupName}.compounds' must map compounds ({string.Join(", ", LexiconSchema.CompoundNames)}) to words");
                        continue;
                    }

                    foreach (var (compoundKey, compoundWords) in compounds.Children)
                    {
                        var compoundName = (compoundKey as YamlScalarNode)?.Value?.Trim() ?? string.Empty;
                        if (!LexiconSchema.TryGetCompound(compoundName, out var compound))
                        {
                            error(compoundKey, $"{rolePath}/{compoundName}", $"unknown tire compound '{compoundName}'; compounds: {string.Join(", ", LexiconSchema.CompoundNames)}");
                            continue;
                        }

                        ReadWords(new WordClass(group, WordRole.Compound, Compound: compound), compoundWords, $"{rolePath}/{compoundName}", words, error);
                    }

                    continue;
                }

                if (!LexiconSchema.TryGetRole(group, roleName, out var role))
                {
                    error(roleKey, rolePath, $"unknown word class '{groupName}.{roleName}'; group '{groupName}' has: {string.Join(", ", LexiconSchema.RoleNames(group))}");
                    continue;
                }

                ReadWords(new WordClass(group, role), value, rolePath, words, error);
            }
        }
    }

    private static void ReadWords(WordClass wordClass, YamlNode node, string path, List<AdditionWord> words, Action<YamlNode, string, string> error)
    {
        if (node is not YamlSequenceNode list)
        {
            error(node, path, $"'{wordClass}' must be a list of words");
            return;
        }

        for (var i = 0; i < list.Children.Count; i++)
        {
            var item = list.Children[i];
            if (item is not YamlScalarNode scalar)
            {
                error(item, $"{path}/{i.ToString(CultureInfo.InvariantCulture)}", "a word must be a word or words in quotes");
                continue;
            }

            words.Add(new AdditionWord(wordClass, scalar.Value ?? string.Empty, (int)item.Start.Line));
        }
    }

    private static UserAdditionsLoadResult Fail(string file, int? line, string message) =>
        new(file, null, [new PackIssue(IssueSeverity.Error, file, line, string.Empty, message)]);
}
