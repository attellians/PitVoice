using System.Globalization;
using PitVoice.Core.Intents;
using YamlDotNet.Core;
using YamlDotNet.RepresentationModel;

namespace PitVoice.Core.Languages;

/// <summary>A pattern the user added to an intent of a built-in pack.</summary>
/// <param name="Intent">Intent the pattern is added to.</param>
/// <param name="Pattern">Pattern text as written in the file.</param>
/// <param name="Line">1-based line of the pattern in the file, when known.</param>
public sealed record AdditionPattern(IntentId Intent, string Pattern, int? Line);

/// <summary>
/// Contents of a user additions file <c>&lt;language&gt;.yaml</c> (FR-LANG-5, design decision 9): only extra patterns for
/// intents of the built-in pack. An empty file (for example the template with comments only) adds nothing.
/// </summary>
/// <param name="File">Path of the file.</param>
/// <param name="Language">Language named in the file; null when the file is empty.</param>
/// <param name="Patterns">Patterns in file order.</param>
public sealed record UserPackAdditions(string File, string? Language, IReadOnlyList<AdditionPattern> Patterns)
{
    public bool IsEmpty => Patterns.Count == 0;
}

/// <summary>Result of reading an additions file: the additions when the format is valid, plus every problem found.</summary>
public sealed record UserAdditionsLoadResult(string File, UserPackAdditions? Additions, IReadOnlyList<PackIssue> Issues)
{
    public bool IsSuccess => Additions is not null;
}

/// <summary>
/// Reads user additions files. The directory is an explicit parameter, else <see cref="EnvironmentVariable"/>, else
/// <c>%AppData%\PitVoice\languages</c>. Only <c>language</c> and <c>intents.&lt;id&gt;.patterns</c> are allowed; anything
/// else is an error with the file and line, so a typo never silently changes nothing.
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
    /// template adds nothing until the user writes patterns (spec: No additions file).
    /// </summary>
    public static string CreateTemplate(string directory, string language)
    {
        var path = GetFile(directory, language);
        if (File.Exists(path))
        {
            return path;
        }

        Directory.CreateDirectory(directory);
        File.WriteAllText(path, $$"""
            # PitVoice: your own phrases for the '{{language}}' language pack.
            # Add patterns to intents of the built-in pack; they work together with the built-in ones.
            # Only 'language' and 'intents.<id>.patterns' are allowed. Slots: {amount}, {unit}, {unit?}.
            # Commands that change the pit stop need at least two words. After saving, press "Reload".
            #
            # language: {{language}}
            # intents:
            #   fuel.add:
            #     patterns: ["pour {amount} {unit?}"]

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
            return Fail(file, (int)stream.Documents[0].RootNode.Start.Line, "the file must be a mapping with 'language' and 'intents'");
        }

        string? language = null;
        var patterns = new List<AdditionPattern>();
        YamlNode? intentsNode = null;
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

                case "intents":
                    intentsNode = value;
                    ReadIntents(value, patterns, Error);
                    break;

                default:
                    Error(keyNode, key ?? string.Empty,
                        $"unknown field '{key}': an additions file may only have 'language' and 'intents' with 'patterns'");
                    break;
            }
        }

        if (language is null && !issues.Any(i => i.Path == "language"))
        {
            Error(root, "language", "'language' is required");
        }

        if (intentsNode is null)
        {
            Error(root, "intents", "'intents' is required");
        }

        return issues.Count > 0
            ? new UserAdditionsLoadResult(file, null, issues)
            : new UserAdditionsLoadResult(file, new UserPackAdditions(file, language, patterns), issues);
    }

    private static void ReadIntents(YamlNode node, List<AdditionPattern> patterns, Action<YamlNode, string, string> error)
    {
        if (node is not YamlMappingNode intents)
        {
            error(node, "intents", "'intents' must map intent ids to their 'patterns'");
            return;
        }

        foreach (var (keyNode, value) in intents.Children)
        {
            var intent = (keyNode as YamlScalarNode)?.Value?.Trim() ?? string.Empty;
            var path = $"intents/{intent}";
            if (value is not YamlMappingNode entry)
            {
                error(value, path, $"intent '{intent}' must contain 'patterns'");
                continue;
            }

            var hasPatterns = false;
            foreach (var (fieldNode, fieldValue) in entry.Children)
            {
                var field = (fieldNode as YamlScalarNode)?.Value;
                if (field != "patterns")
                {
                    error(fieldNode, $"{path}/{field}",
                        $"unknown field '{field}': additions can only add 'patterns' to an intent; responses and other fields come from the built-in pack");
                    continue;
                }

                hasPatterns = true;
                if (fieldValue is not YamlSequenceNode list)
                {
                    error(fieldValue, $"{path}/patterns", "'patterns' must be a list of phrases");
                    continue;
                }

                for (var i = 0; i < list.Children.Count; i++)
                {
                    var item = list.Children[i];
                    var itemPath = $"{path}/patterns/{i.ToString(CultureInfo.InvariantCulture)}";
                    if (item is not YamlScalarNode scalar)
                    {
                        error(item, itemPath, "a pattern must be a phrase in quotes");
                        continue;
                    }

                    patterns.Add(new AdditionPattern(new IntentId(intent), scalar.Value ?? string.Empty, (int)item.Start.Line));
                }
            }

            if (!hasPatterns)
            {
                error(entry, path, $"intent '{intent}' must contain 'patterns'");
            }
        }
    }

    private static UserAdditionsLoadResult Fail(string file, int? line, string message) =>
        new(file, null, [new PackIssue(IssueSeverity.Error, file, line, string.Empty, message)]);
}
