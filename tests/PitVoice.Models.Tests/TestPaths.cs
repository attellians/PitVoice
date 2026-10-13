namespace PitVoice.Models.Tests;

internal static class TestPaths
{
    public static string Manifest { get; } = Path.Combine(AppContext.BaseDirectory, ModelManifest.DefaultDirectoryName, ModelManifest.FileName);

    public static string Fixture(string name) => Path.Combine(AppContext.BaseDirectory, "Fixtures", name);
}
