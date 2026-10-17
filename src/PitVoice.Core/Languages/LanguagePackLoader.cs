using PitVoice.Core.Languages.Dto;
using YamlDotNet.Core;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace PitVoice.Core.Languages;

/// <summary>Result of loading one pack file.</summary>
/// <param name="File">File name or path.</param>
/// <param name="Pack">The pack when it loaded and validated, otherwise <c>null</c>.</param>
/// <param name="Issues">Errors and warnings; a pack with errors is not available.</param>
public sealed record LanguagePackLoadResult(string File, LanguagePack? Pack, IReadOnlyList<PackIssue> Issues)
{
    public bool IsSuccess => Pack is not null;

    public IEnumerable<PackIssue> Errors => Issues.Where(i => i.Severity == IssueSeverity.Error);

    public IEnumerable<PackIssue> Warnings => Issues.Where(i => i.Severity == IssueSeverity.Warning);
}

/// <summary>Loads YAML language packs from text, files or a directory.</summary>
public static class LanguagePackLoader
{
    public const string FilePattern = "*.yaml";

    private static readonly IDeserializer Deserializer = new DeserializerBuilder()
        .WithNamingConvention(UnderscoredNamingConvention.Instance)
        .Build();

    public static LanguagePackLoadResult LoadText(string yaml, string fileName)
    {
        ArgumentNullException.ThrowIfNull(yaml);
        ArgumentNullException.ThrowIfNull(fileName);

        LanguagePackDto? dto;
        YamlLineIndex lines;
        try
        {
            lines = YamlLineIndex.Build(yaml);
            dto = Deserializer.Deserialize<LanguagePackDto>(yaml);
        }
        catch (YamlException ex)
        {
            var issue = new PackIssue(IssueSeverity.Error, fileName, (int)ex.Start.Line, string.Empty, Describe(ex));
            return new LanguagePackLoadResult(fileName, null, [issue]);
        }
        catch (Exception ex) when (ex is InvalidOperationException or ArgumentException or FormatException)
        {
            // YamlDotNet occasionally surfaces scanner failures as plain exceptions without a position.
            var issue = new PackIssue(IssueSeverity.Error, fileName, null, string.Empty, $"invalid YAML: {ex.Message}");
            return new LanguagePackLoadResult(fileName, null, [issue]);
        }

        if (dto is null)
        {
            var issue = new PackIssue(IssueSeverity.Error, fileName, null, string.Empty, "the file is empty");
            return new LanguagePackLoadResult(fileName, null, [issue]);
        }

        var validation = LanguagePackValidator.Validate(dto, fileName, lines);
        return new LanguagePackLoadResult(fileName, validation.Pack, validation.Issues);
    }

    /// <summary>
    /// The YAML error in words of the pack format: a field the format does not have is named, with a hint for the fields
    /// removed in format 2.
    /// </summary>
    private static string Describe(YamlException ex)
    {
        var unknown = UnknownProperty.Match(ex.Message);
        if (!unknown.Success)
        {
            return $"invalid YAML: {ex.Message}";
        }

        var field = unknown.Groups[1].Value;
        return field switch
        {
            "default_voice" => "unknown field 'default_voice': packs have no voice, the result is a sound cue (TZ §4.7); remove the field",
            "intents" => "unknown field 'intents': format 2 lists words in 'lexicon' classes and intent responses in 'responses' (docs/adding-a-language.md)",
            _ => $"unknown field '{field}'",
        };
    }

    private static readonly System.Text.RegularExpressions.Regex UnknownProperty =
        new("Property '([^']+)' not found on type", System.Text.RegularExpressions.RegexOptions.CultureInvariant);

    public static LanguagePackLoadResult LoadFile(string path)
    {
        ArgumentNullException.ThrowIfNull(path);
        string yaml;
        try
        {
            yaml = File.ReadAllText(path);
        }
        catch (IOException ex)
        {
            return new LanguagePackLoadResult(path, null, [new PackIssue(IssueSeverity.Error, path, null, string.Empty, ex.Message)]);
        }
        catch (UnauthorizedAccessException ex)
        {
            return new LanguagePackLoadResult(path, null, [new PackIssue(IssueSeverity.Error, path, null, string.Empty, ex.Message)]);
        }

        return LoadText(yaml, path);
    }

    /// <summary>Loads every <c>*.yaml</c> in <paramref name="directory"/>; a broken file does not stop the others.</summary>
    public static IReadOnlyList<LanguagePackLoadResult> LoadDirectory(string directory)
    {
        ArgumentNullException.ThrowIfNull(directory);
        if (!Directory.Exists(directory))
        {
            var issue = new PackIssue(IssueSeverity.Error, directory, null, string.Empty, "languages directory not found");
            return [new LanguagePackLoadResult(directory, null, [issue])];
        }

        return Directory.GetFiles(directory, FilePattern)
            .Order(StringComparer.Ordinal)
            .Select(LoadFile)
            .ToArray();
    }
}
