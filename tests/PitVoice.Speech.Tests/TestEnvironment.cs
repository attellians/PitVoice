using PitVoice.Core.Languages;
using PitVoice.Models;
using Xunit;

namespace PitVoice.Speech.Tests;

/// <summary>Shipped packs, fixtures and installed models for tests.</summary>
internal static class TestEnvironment
{
    /// <summary>When set (CI), a missing model is a failure instead of a skip.</summary>
    public const string RequireModelsVariable = "PITVOICE_REQUIRE_MODELS";

    private static readonly Lazy<LanguagePack> RuPack = new(() => LoadPack("ru"));
    private static readonly Lazy<LanguagePack> EnPack = new(() => LoadPack("en"));

    public static string BaseDirectory => AppContext.BaseDirectory;

    public static bool ModelsRequired => !string.IsNullOrEmpty(Environment.GetEnvironmentVariable(RequireModelsVariable));

    public static LanguagePack Pack(string language) => language switch
    {
        "ru" => RuPack.Value,
        "en" => EnPack.Value,
        _ => throw new ArgumentOutOfRangeException(nameof(language), language, "no such pack"),
    };

    public static string Fixture(string name) => Path.Combine(BaseDirectory, "Fixtures", name);

    /// <summary>Directory of the installed model of <paramref name="language"/>, or null.</summary>
    public static string? ModelPath(string language) => new ModelStore().GetInstalledPath(Pack(language).AsrModel!);

    /// <summary>Message for a skipped test that needs the model of <paramref name="language"/>.</summary>
    public static string? SkipReason(string language)
    {
        if (ModelsRequired || ModelPath(language) is not null)
        {
            return null;
        }

        var id = Pack(language).AsrModel;
        return $"model {id} is not installed; run 'pitvoice-cli models download {id}' or set {ModelStore.EnvironmentVariable}";
    }

    /// <summary>Model path, failing the test with the model id when it is missing and models are required.</summary>
    public static string RequireModel(string language) =>
        ModelPath(language) ?? throw new InvalidOperationException(
            $"model {Pack(language).AsrModel} is required ({RequireModelsVariable} is set) but not installed in {new ModelStore().Root}");

    private static LanguagePack LoadPack(string language)
    {
        var result = LanguagePackLoader.LoadFile(Path.Combine(BaseDirectory, "languages", $"{language}.yaml"));
        return result.Pack ?? throw new InvalidOperationException($"pack {language} failed:\n{string.Join('\n', result.Issues)}");
    }
}

/// <summary>A fact that needs the installed model of one language; skipped without it unless models are required.</summary>
internal sealed class ModelFactAttribute : FactAttribute
{
    public ModelFactAttribute(string language)
    {
        Skip = TestEnvironment.SkipReason(language);
    }
}

/// <summary>A theory over both languages' samples; skipped when a needed model is absent (see <see cref="ModelFactAttribute"/>).</summary>
internal sealed class ModelTheoryAttribute : TheoryAttribute
{
    public ModelTheoryAttribute(params string[] languages)
    {
        Skip = languages.Select(TestEnvironment.SkipReason).FirstOrDefault(r => r is not null);
    }
}
