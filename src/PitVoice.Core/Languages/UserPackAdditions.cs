using System.Globalization;
using System.Text;
using PitVoice.Core.Intents;
using YamlDotNet.Core;
using YamlDotNet.RepresentationModel;

namespace PitVoice.Core.Languages;

/// <summary>A word the user added to a class of the built-in lexicon; only <c>fuel.action</c> is allowed.</summary>
/// <param name="Class">Class the word is added to.</param>
/// <param name="Text">Word as written in the file.</param>
/// <param name="Line">1-based line of the word in the file, when known.</param>
public sealed record AdditionWord(WordClass Class, string Text, int? Line)
{
    /// <summary>Position of the entry among all entries of the additions (a row of the page).</summary>
    public int Entry { get; init; }
}

/// <summary>A ready phrase the user gave one command (design D3).</summary>
/// <param name="Text">Phrase as written.</param>
/// <param name="Intent">Command of the phrase, one of <see cref="BuilderCommands"/>.</param>
/// <param name="Line">1-based line of the phrase in the file, when known.</param>
public sealed record AdditionPhrase(string Text, IntentId Intent, int? Line)
{
    /// <summary>Position of the entry among all entries of the additions (a row of the page).</summary>
    public int Entry { get; init; }
}

/// <summary>
/// Contents of a user additions file <c>&lt;language&gt;.yaml</c> (FR-LANG-5, design D1): fuel verbs and ready phrases.
/// An empty file adds nothing.
/// </summary>
/// <param name="File">Path of the file.</param>
/// <param name="Language">Language named in the file; null when the file does not name one.</param>
/// <param name="Words">Fuel verbs in file order.</param>
/// <param name="Phrases">Ready phrases in file order.</param>
public sealed record UserPackAdditions(string File, string? Language, IReadOnlyList<AdditionWord> Words, IReadOnlyList<AdditionPhrase> Phrases)
{
    public bool IsEmpty => Words.Count == 0 && Phrases.Count == 0;

    /// <summary>Number of entries: verbs and phrases.</summary>
    public int Count => Words.Count + Phrases.Count;
}

/// <summary>Result of reading an additions file: the additions when the format is valid, plus every problem found.</summary>
public sealed record UserAdditionsLoadResult(string File, UserPackAdditions? Additions, IReadOnlyList<PackIssue> Issues)
{
    public bool IsSuccess => Additions is not null;

    /// <summary>
    /// True when the "Own phrases" page cannot show the file (design D10): invalid YAML, the old <c>intents</c> format,
    /// words in classes other than <c>fuel.action</c>, or a command the page does not have.
    /// </summary>
    public bool NotBuilderFormat => Additions is null;
}

/// <summary>
/// Reads user additions files. The directory is an explicit parameter, else <see cref="EnvironmentVariable"/>, else
/// <c>%AppData%\PitVoice\languages</c>. Only <c>language</c>, <c>lexicon</c> with <c>fuel.action</c>, and <c>phrases</c>
/// are allowed; anything else is an error with the file and line, and the file is not applied.
/// </summary>
public static class UserAdditionsLoader
{
    public const string EnvironmentVariable = "PITVOICE_USER_LANGUAGES";

    /// <summary>The only lexicon class an additions file may add words to.</summary>
    public static WordClass FuelVerbClass { get; } = new(LexiconGroup.Fuel, WordRole.Action);

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
            return new UserAdditionsLoadResult(file, new UserPackAdditions(file, null, [], []), []);
        }

        var issues = new List<PackIssue>();
        void Error(YamlNode node, string path, string message) =>
            issues.Add(new PackIssue(IssueSeverity.Error, file, (int)node.Start.Line, path, message));

        if (stream.Documents[0].RootNode is not YamlMappingNode root)
        {
            return Fail(file, (int)stream.Documents[0].RootNode.Start.Line, "the file must be a mapping with 'lexicon' and 'phrases'");
        }

        string? language = null;
        var words = new List<AdditionWord>();
        var phrases = new List<AdditionPhrase>();
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

                case "phrases":
                    ReadPhrases(value, phrases, Error);
                    break;

                case "intents":
                    Error(keyNode, "intents",
                        "the old format with phrases in 'intents' is not supported: own phrases are set on the page Commands → Own phrases");
                    break;

                default:
                    Error(keyNode, key ?? string.Empty, $"unknown field '{key}': an additions file may only have 'language', 'lexicon' and 'phrases'");
                    break;
            }
        }

        if (issues.Count > 0)
        {
            return new UserAdditionsLoadResult(file, null, issues);
        }

        // Entries are numbered in file order, verbs first: the order of the rows on the page.
        var numberedWords = words.Select((w, i) => w with { Entry = i }).ToArray();
        var numberedPhrases = phrases.Select((p, i) => p with { Entry = words.Count + i }).ToArray();
        return new UserAdditionsLoadResult(file, new UserPackAdditions(file, language, numberedWords, numberedPhrases), issues);
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

                    foreach (var (targetKey, _) in targets.Children)
                    {
                        var targetName = (targetKey as YamlScalarNode)?.Value?.Trim() ?? string.Empty;
                        if (!LexiconSchema.TryGetTarget(targetName, out var target))
                        {
                            error(targetKey, $"{rolePath}/{targetName}", $"unknown tire target '{targetName}'; targets: {string.Join(", ", LexiconSchema.TargetNames)}");
                            continue;
                        }

                        NotShown(new WordClass(group, WordRole.Target, target), targetKey, $"{rolePath}/{targetName}", error);
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

                    foreach (var (compoundKey, _) in compounds.Children)
                    {
                        var compoundName = (compoundKey as YamlScalarNode)?.Value?.Trim() ?? string.Empty;
                        if (!LexiconSchema.TryGetCompound(compoundName, out var compound))
                        {
                            error(compoundKey, $"{rolePath}/{compoundName}", $"unknown tire compound '{compoundName}'; compounds: {string.Join(", ", LexiconSchema.CompoundNames)}");
                            continue;
                        }

                        NotShown(new WordClass(group, WordRole.Compound, Compound: compound), compoundKey, $"{rolePath}/{compoundName}", error);
                    }

                    continue;
                }

                if (!LexiconSchema.TryGetRole(group, roleName, out var role))
                {
                    error(roleKey, rolePath, $"unknown word class '{groupName}.{roleName}'; group '{groupName}' has: {string.Join(", ", LexiconSchema.RoleNames(group))}");
                    continue;
                }

                var wordClass = new WordClass(group, role);
                if (wordClass != FuelVerbClass)
                {
                    NotShown(wordClass, roleKey, rolePath, error);
                    continue;
                }

                ReadWords(wordClass, value, rolePath, words, error);
            }
        }
    }

    /// <summary>Words of a class other than <c>fuel.action</c>: the page cannot show them (design D10).</summary>
    private static void NotShown(WordClass wordClass, YamlNode node, string path, Action<YamlNode, string, string> error) =>
        error(node, path, $"words in '{wordClass}' cannot be shown on the page Commands → Own phrases; only '{FuelVerbClass}' and ready phrases are allowed");

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

    private static void ReadPhrases(YamlNode node, List<AdditionPhrase> phrases, Action<YamlNode, string, string> error)
    {
        if (node is not YamlSequenceNode list)
        {
            error(node, "phrases", "'phrases' must be a list of { text, intent }");
            return;
        }

        for (var i = 0; i < list.Children.Count; i++)
        {
            var path = $"phrases/{i.ToString(CultureInfo.InvariantCulture)}";
            if (list.Children[i] is not YamlMappingNode item)
            {
                error(list.Children[i], path, "a phrase must be { text, intent }");
                continue;
            }

            string? text = null;
            string? intent = null;
            var valid = true;
            foreach (var (keyNode, value) in item.Children)
            {
                var key = (keyNode as YamlScalarNode)?.Value;
                var scalar = (value as YamlScalarNode)?.Value;
                switch (key)
                {
                    case "text":
                        text = scalar;
                        break;
                    case "intent":
                        intent = scalar?.Trim();
                        break;
                    default:
                        error(keyNode, $"{path}/{key}", $"unknown field '{key}': a phrase has only 'text' and 'intent'");
                        valid = false;
                        break;
                }
            }

            if (text is null || string.IsNullOrEmpty(intent))
            {
                error(item, path, "a phrase must have 'text' and 'intent'");
                continue;
            }

            if (BuilderCommands.PhraseOf(new IntentId(intent)) is null)
            {
                error(item, $"{path}/intent", $"command '{intent}' is not one of the page Commands → Own phrases");
                continue;
            }

            if (valid)
            {
                phrases.Add(new AdditionPhrase(text, new IntentId(intent), (int)item.Start.Line));
            }
        }
    }

    private static UserAdditionsLoadResult Fail(string file, int? line, string message) =>
        new(file, null, [new PackIssue(IssueSeverity.Error, file, line, string.Empty, message)]);
}

/// <summary>
/// Writes the additions file (design D1). Only the application writes it: first into a temporary file next to it, then
/// the temporary file replaces it, so a failure never leaves half a file.
/// </summary>
public static class UserAdditionsWriter
{
    public const string Header = "# Managed by PitVoice: Commands → Own phrases. Changes made here by hand may be lost.";

    /// <summary>The file text for <paramref name="words"/> and <paramref name="phrases"/>.</summary>
    public static string ToYaml(string language, IEnumerable<AdditionWord> words, IEnumerable<AdditionPhrase> phrases)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(language);
        ArgumentNullException.ThrowIfNull(words);
        ArgumentNullException.ThrowIfNull(phrases);

        var text = new StringBuilder();
        text.Append(Header).Append('\n');
        text.Append("language: ").Append(language).Append('\n');
        var verbs = words.Select(w => Quote(w.Text)).ToArray();
        if (verbs.Length > 0)
        {
            text.Append("lexicon:\n");
            text.Append("  fuel: { action: [").Append(string.Join(", ", verbs)).Append("] }\n");
        }

        var list = phrases.ToArray();
        if (list.Length > 0)
        {
            text.Append("phrases:\n");
            foreach (var phrase in list)
            {
                text.Append("  - { text: ").Append(Quote(phrase.Text)).Append(", intent: ").Append(phrase.Intent.Value).Append(" }\n");
            }
        }

        return text.ToString();
    }

    /// <summary>Writes the file; returns the error message when it cannot be written, and the old file stays as it was.</summary>
    public static string? TryWrite(string path, string language, IEnumerable<AdditionWord> words, IEnumerable<AdditionPhrase> phrases)
    {
        ArgumentNullException.ThrowIfNull(path);
        var yaml = ToYaml(language, words, phrases);
        var temporary = path + ".tmp";
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
            File.WriteAllText(temporary, yaml, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            File.Move(temporary, path, overwrite: true);
            return null;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            try
            {
                File.Delete(temporary);
            }
            catch (Exception cleanup) when (cleanup is IOException or UnauthorizedAccessException)
            {
                // The temporary file is harmless: the next save overwrites it.
            }

            return e.Message;
        }
    }

    /// <summary>A double-quoted YAML scalar: any text is safe in it.</summary>
    private static string Quote(string text)
    {
        var quoted = new StringBuilder("\"");
        foreach (var c in text.Trim())
        {
            switch (c)
            {
                case '"':
                    quoted.Append("\\\"");
                    break;
                case '\\':
                    quoted.Append("\\\\");
                    break;
                case '\n' or '\r' or '\t':
                    quoted.Append(' ');
                    break;
                default:
                    quoted.Append(c);
                    break;
            }
        }

        return quoted.Append('"').ToString();
    }
}
