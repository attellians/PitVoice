using System.Globalization;
using PitVoice.Core.Intents;
using PitVoice.Core.Languages;
using PitVoice.Core.Parsing;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace PitVoice.Speech.Tests.Samples;

/// <summary>One recorded command: file, what was said, expected commands or null for "must be rejected".</summary>
internal sealed record Sample(string File, string Say, IReadOnlyList<ExpectedCommand>? Expect);

/// <summary>
/// <c>Samples/&lt;lang&gt;/samples.yaml</c>: <c>samples:</c> list of <c>file</c>, <c>say</c>, <c>expect</c>, where
/// <c>expect</c> uses the format of the <c>tests:</c> section of a language pack (a list of commands or <c>reject</c>).
/// </summary>
internal static class SampleSet
{
    public const string FileName = "samples.yaml";

    private static readonly IDeserializer Deserializer = new DeserializerBuilder()
        .WithNamingConvention(UnderscoredNamingConvention.Instance)
        .Build();

    public static string Directory(string language) => Path.Combine(TestEnvironment.BaseDirectory, "Samples", language);

    public static bool Exists(string language) => System.IO.File.Exists(Path.Combine(Directory(language), FileName));

    public static IReadOnlyList<Sample> Load(string language)
    {
        var path = Path.Combine(Directory(language), FileName);
        var dto = Deserializer.Deserialize<SamplesDto>(System.IO.File.ReadAllText(path))
            ?? throw new InvalidDataException($"{path}: empty");
        var samples = new List<Sample>();
        foreach (var (item, index) in (dto.Samples ?? []).Select((s, i) => (s, i)))
        {
            var where = $"{path}: samples[{index}]";
            if (item?.File is not { Length: > 0 } file || item.Say is not { Length: > 0 } say)
            {
                throw new InvalidDataException($"{where}: 'file' and 'say' are required");
            }

            samples.Add(new Sample(file, say, ParseExpect(item.Expect, where)));
        }

        return samples;
    }

    /// <summary>True when <paramref name="actual"/> has the expected intents in order (amount and unit when given).</summary>
    public static bool Matches(IReadOnlyList<ExpectedCommand> expected, IReadOnlyList<ParsedCommand> actual) =>
        expected.Count == actual.Count && expected.Zip(actual).All(p =>
            p.First.Intent == p.Second.Intent
            && (p.First.Amount is null || p.First.Amount == p.Second.Amount)
            && (p.First.Unit is null || p.First.Unit == p.Second.Unit));

    private static List<ExpectedCommand>? ParseExpect(object? expect, string where)
    {
        if (expect is string text && text.Trim() == "reject")
        {
            return null;
        }

        if (expect is not List<object> list || list.Count == 0)
        {
            throw new InvalidDataException($"{where}: 'expect' must be 'reject' or a list of commands");
        }

        var commands = new List<ExpectedCommand>();
        foreach (var entry in list)
        {
            if (entry is not Dictionary<object, object> map || !map.TryGetValue("intent", out var intent))
            {
                throw new InvalidDataException($"{where}: every expected command needs 'intent'");
            }

            int? amount = map.TryGetValue("amount", out var a) ? int.Parse(a.ToString()!, CultureInfo.InvariantCulture) : null;
            var unit = map.TryGetValue("unit", out var u) ? u.ToString() : null;
            commands.Add(new ExpectedCommand(new IntentId(intent.ToString()!), amount, unit));
        }

        return commands;
    }

    private sealed class SamplesDto
    {
        public List<SampleDto?>? Samples { get; set; }
    }

    private sealed class SampleDto
    {
        public string? File { get; set; }

        public string? Say { get; set; }

        public object? Expect { get; set; }
    }
}
