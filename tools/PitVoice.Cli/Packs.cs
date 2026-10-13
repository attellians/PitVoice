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

        if (!match.IsSuccess)
        {
            errors.WriteLine($"Language pack '{language}' failed validation.");
        }

        return match.Pack;
    }
}
