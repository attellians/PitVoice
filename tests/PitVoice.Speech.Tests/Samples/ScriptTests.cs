using PitVoice.Core.Languages;
using PitVoice.Core.Parsing;
using Xunit;

namespace PitVoice.Speech.Tests.Samples;

/// <summary>
/// The lists the acceptance set is recorded from (design D9): enough phrases, every intent of the pack, and every phrase
/// parses as text into its own expected result, so a recording can only fail on recognition.
/// </summary>
public sealed class ScriptTests
{
    public const int MinPhrases = 55;
    public const int MinRejections = 8;

    [Theory]
    [InlineData("ru")]
    [InlineData("en")]
    public void Script_phrases_parse_as_text_into_their_expect(string language)
    {
        var path = SampleSet.ScriptPath(language);
        var script = SampleFile.Load(path, requireFile: false);
        var pack = TestEnvironment.Pack(language);
        var parser = new CommandParser(pack);

        Assert.Empty(SampleFile.CheckIntents(script, pack, path));
        var failures = new List<string>();
        foreach (var phrase in script)
        {
            var result = parser.Parse(phrase.Say);
            var ok = (result, phrase.Expect) switch
            {
                (ParseResult.Rejected, null) => true,
                (ParseResult.Accepted accepted, { } expect) => SampleFile.Matches(expect, accepted.Commands),
                _ => false,
            };
            if (!ok)
            {
                failures.Add($"{path}:{phrase.Line}: \"{phrase.Say}\": expected {SampleFile.FormatExpect(phrase.Expect)}, got {result}");
            }
        }

        Assert.True(failures.Count == 0, string.Join('\n', failures));
    }

    [Theory]
    [InlineData("ru")]
    [InlineData("en")]
    public void Script_is_large_enough_and_covers_every_intent(string language)
    {
        var script = SampleFile.Load(SampleSet.ScriptPath(language), requireFile: false);
        var pack = TestEnvironment.Pack(language);

        Assert.True(script.Count >= MinPhrases, $"{script.Count} phrases, need at least {MinPhrases}");
        Assert.True(script.Count(p => p.Expect is null) >= MinRejections, $"need at least {MinRejections} phrases that must be rejected");
        var said = script.SelectMany(p => p.Expect ?? []).Select(c => c.Intent).ToHashSet();
        Assert.All(pack.Intents, i => Assert.Contains(i, said));
    }
}
