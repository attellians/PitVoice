using PitVoice.Core.Languages;

namespace PitVoice.Core.Tests;

/// <summary>Loads the shipped packs (copied next to the test assembly) once per test run.</summary>
internal static class TestPacks
{
    public static string Directory { get; } = Path.Combine(AppContext.BaseDirectory, "languages");

    public static string FixturesDirectory { get; } = Path.Combine(AppContext.BaseDirectory, "Fixtures");

    private static readonly Lazy<LanguagePack> RuPack = new(() => Load("ru"));
    private static readonly Lazy<LanguagePack> EnPack = new(() => Load("en"));

    public static LanguagePack Ru => RuPack.Value;

    public static LanguagePack En => EnPack.Value;

    public static LanguagePack Get(string language) => language switch
    {
        "ru" => Ru,
        "en" => En,
        _ => throw new ArgumentOutOfRangeException(nameof(language), language, "no such test pack"),
    };

    private static LanguagePack Load(string language)
    {
        var result = LanguagePackLoader.LoadFile(Path.Combine(Directory, $"{language}.yaml"));
        if (result.Pack is null)
        {
            throw new InvalidOperationException($"pack {language} failed to load:\n{string.Join('\n', result.Issues)}");
        }

        return result.Pack;
    }
}
