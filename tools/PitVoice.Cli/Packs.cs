using PitVoice.Core.Languages;

namespace PitVoice.Cli;

internal static class Packs
{
    public const string DefaultDirectoryName = "languages";

    /// <summary>Explicit directory, else <c>languages</c> next to the executable, else <c>languages</c> in the current directory.</summary>
    public static string ResolveDirectory(string? explicitDirectory)
    {
        if (!string.IsNullOrWhiteSpace(explicitDirectory))
        {
            return Path.GetFullPath(explicitDirectory);
        }

        var nextToExe = Path.Combine(AppContext.BaseDirectory, DefaultDirectoryName);
        if (Directory.Exists(nextToExe))
        {
            return nextToExe;
        }

        return Path.GetFullPath(DefaultDirectoryName);
    }

    public static LanguagePack? LoadLanguage(string directory, string language, TextWriter errors)
    {
        var results = LanguagePackLoader.LoadDirectory(directory);
        var match = results.FirstOrDefault(r =>
            r.Pack?.Language == language
            || string.Equals(Path.GetFileNameWithoutExtension(r.File), language, StringComparison.OrdinalIgnoreCase));

        if (match is null)
        {
            var available = results.Where(r => r.IsSuccess).Select(r => r.Pack!.Language).Order(StringComparer.Ordinal);
            errors.WriteLine($"No language pack '{language}' in {directory}. Available: {string.Join(", ", available)}");
            return null;
        }

        foreach (var issue in match.Issues)
        {
            errors.WriteLine(issue);
        }

        if (match.Pack is not { } pack)
        {
            errors.WriteLine($"Language pack '{language}' failed validation.");
            return null;
        }

        return ApplyAdditions(pack, errors);
    }

    /// <summary>
    /// Merges the user's additions file of the pack's language (FR-LANG-5) from
    /// <c>$PITVOICE_USER_LANGUAGES</c> or <c>%AppData%\PitVoice\languages</c>; a broken file is reported and the
    /// built-in pack is used.
    /// </summary>
    public static LanguagePack ApplyAdditions(LanguagePack pack, TextWriter errors)
    {
        var result = UserAdditions.Apply(pack, UserAdditionsLoader.ResolveDirectory(null));
        Print(result, errors);
        return result.Pack;
    }

    public static void Print(PackAdditionsResult result, TextWriter errors)
    {
        foreach (var issue in result.Issues)
        {
            errors.WriteLine(issue);
        }

        if (result.Exists && !result.Applied)
        {
            errors.WriteLine($"Additions {result.File} not applied; using the built-in '{result.Pack.Language}' pack.");
            if (result.NotBuilderFormat)
            {
                errors.WriteLine(NotBuilderFormatNote);
            }
        }
        else if (Count(result.Pack) is { Length: > 0 } count)
        {
            errors.WriteLine($"additions:  {count} from {result.File}");
        }
    }

    public const string NotBuilderFormatNote = "The file cannot be opened on the page Commands → Own phrases; saving there replaces it.";

    /// <summary>"1 fuel verb, 2 ready phrases", or empty when the pack has no additions.</summary>
    public static string Count(LanguagePack pack)
    {
        var parts = new List<string>();
        if (pack.UserEntries.Count is var verbs and > 0)
        {
            parts.Add($"{verbs} fuel verb(s)");
        }

        if (pack.UserPhrases.Count is var phrases and > 0)
        {
            parts.Add($"{phrases} ready phrase(s)");
        }

        return string.Join(", ", parts);
    }
}
