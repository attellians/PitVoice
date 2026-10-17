using PitVoice.Core.Languages;

namespace PitVoice.Speech.Tests.Samples;

/// <summary>
/// <c>Samples/&lt;lang&gt;/samples.yaml</c> of the local acceptance set and <c>Samples/script.&lt;lang&gt;.yaml</c>, the list
/// of phrases it is recorded from. The format is read by <see cref="SampleFile"/>, shared with <c>listen --script</c>.
/// </summary>
internal static class SampleSet
{
    public const string FileName = SampleFile.FileName;

    public static string Directory(string language) => Path.Combine(TestEnvironment.BaseDirectory, "Samples", language);

    public static string ScriptPath(string language) => Path.Combine(TestEnvironment.BaseDirectory, "Samples", $"script.{language}.yaml");

    public static bool Exists(string language) => File.Exists(Path.Combine(Directory(language), FileName));

    public static IReadOnlyList<SampleEntry> Load(string language) =>
        SampleFile.Load(Path.Combine(Directory(language), FileName), requireFile: true);
}
