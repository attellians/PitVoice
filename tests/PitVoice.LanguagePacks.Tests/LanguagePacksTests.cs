using PitVoice.Core.Intents;
using PitVoice.Core.Languages;
using PitVoice.Core.Parsing;
using Xunit;

namespace PitVoice.LanguagePacks.Tests;

/// <summary>
/// Data-driven run of the <c>tests:</c> section of every shipped pack plus completeness checks. A failing phrase
/// names the language, the phrase, the expected and the actual result.
/// </summary>
public class LanguagePacksTests
{
    private static readonly string Directory = Path.Combine(AppContext.BaseDirectory, "languages");

    private static readonly Lazy<IReadOnlyDictionary<string, LanguagePackLoadResult>> Loaded = new(() =>
        LanguagePackLoader.LoadDirectory(Directory)
            .ToDictionary(r => Path.GetFileNameWithoutExtension(r.File), StringComparer.Ordinal));

    public static TheoryData<string> Languages
    {
        get
        {
            var data = new TheoryData<string>();
            foreach (var language in Loaded.Value.Keys.Order(StringComparer.Ordinal))
            {
                data.Add(language);
            }

            return data;
        }
    }

    public static TheoryData<string, int, string> Phrases
    {
        get
        {
            var data = new TheoryData<string, int, string>();
            foreach (var (language, result) in Loaded.Value.OrderBy(p => p.Key, StringComparer.Ordinal))
            {
                if (result.Pack is null)
                {
                    continue;
                }

                for (var i = 0; i < result.Pack.Tests.Count; i++)
                {
                    data.Add(language, i, result.Pack.Tests[i].Say);
                }
            }

            return data;
        }
    }

    private static LanguagePack Pack(string language)
    {
        var result = Loaded.Value[language];
        Assert.True(result.IsSuccess, $"{language}: {string.Join('\n', result.Issues)}");
        return result.Pack!;
    }

    [Fact]
    public void Both_languages_ship()
    {
        Assert.Contains("ru", Loaded.Value.Keys);
        Assert.Contains("en", Loaded.Value.Keys);
    }

    [Theory]
    [MemberData(nameof(Languages))]
    public void Pack_loads_without_issues(string language)
    {
        var result = Loaded.Value[language];
        Assert.True(result.IsSuccess, string.Join('\n', result.Issues));
        Assert.Empty(result.Issues);
    }

    [Theory]
    [MemberData(nameof(Languages))]
    public void Pack_describes_every_catalog_intent(string language)
    {
        var pack = Pack(language);
        Assert.Empty(pack.MissingIntents);
        Assert.Equal(IntentCatalog.All.Count, pack.Intents.Count);
    }

    [Theory]
    [MemberData(nameof(Languages))]
    public void Every_intent_has_at_least_two_tests(string language)
    {
        var pack = Pack(language);
        foreach (var id in pack.Intents)
        {
            var tests = pack.Tests.Count(t => t.Expect is not null && t.Expect.Any(e => e.Intent == id));
            Assert.True(tests >= 2, $"{language}: intent {id} has {tests} test(s), expected at least 2");
        }
    }

    [Theory]
    [MemberData(nameof(Languages))]
    public void Pack_has_negative_tests(string language)
    {
        var pack = Pack(language);
        Assert.True(pack.Tests.Count(t => t.ExpectsRejection) >= 5, $"{language}: expected at least 5 rejection tests");
    }

    [Theory]
    [MemberData(nameof(Languages))]
    public void Pack_has_every_generic_response(string language)
    {
        var pack = Pack(language);
        foreach (var key in new[] { "not_understood", "not_connected", "not_in_car", "apply_failed" })
        {
            Assert.True(pack.Responses.ContainsKey(key), $"{language}: responses.{key} is missing");
        }
    }

    [Theory]
    [MemberData(nameof(Phrases))]
    public void Test_phrase(string language, int index, string say)
    {
        var pack = Pack(language);
        var test = pack.Tests[index];
        Assert.Equal(say, test.Say);

        var failure = LanguagePackTestRunner.RunOne(new CommandParser(pack), test);

        Assert.True(failure is null, failure?.ToString());
    }

    [Theory]
    [MemberData(nameof(Languages))]
    public void Runner_reports_all_failures_of_a_pack(string language)
    {
        var pack = Pack(language);
        Assert.Empty(LanguagePackTestRunner.Run(pack));
    }
}
