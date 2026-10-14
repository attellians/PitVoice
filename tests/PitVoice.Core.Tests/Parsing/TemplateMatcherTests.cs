using PitVoice.Core.Intents;
using PitVoice.Core.Parsing;
using Xunit;

namespace PitVoice.Core.Tests.Parsing;

public class TemplateMatcherTests
{
    private static MatchResult Match(string language, string phrase)
    {
        var pack = TestPacks.Get(language);
        var matcher = new TemplateMatcher(pack.Templates, pack.Negations);
        return matcher.Match(pack.Normalizer.Normalize(phrase));
    }

    private static TemplateMatch Best(MatchResult result)
    {
        Assert.NotNull(result.Best);
        return result.Best!;
    }

    [Fact]
    public void Exact_match_with_slots()
    {
        var result = Match("ru", "добавь двадцать литров топлива");

        var best = Best(result);
        Assert.Equal(IntentCatalog.FuelAdd, best.Template.Intent);
        Assert.Equal(0, best.Distance);
        Assert.Equal(1.0, best.Quality);
        Assert.Equal(20, best.Amount);
        Assert.Equal("liters", best.Unit);
    }

    [Fact]
    public void Optional_unit_may_be_absent()
    {
        var best = Best(Match("ru", "залей десять"));

        Assert.Equal(IntentCatalog.FuelAdd, best.Template.Intent);
        Assert.Equal(10, best.Amount);
        Assert.Null(best.Unit);
    }

    [Fact]
    public void Required_unit_must_be_present()
    {
        // "{amount} {unit} топлива" needs a unit: "двадцать топлива" does not match it, "двадцать литров топлива" does.
        var pack = TestPacks.Ru;
        var template = pack.Templates.Single(t => t.Source == "{amount} {unit} топлива");
        var matcher = new TemplateMatcher([template], pack.Negations);

        Assert.Null(matcher.Match(pack.Normalizer.Normalize("двадцать топлива")).Best);
        var best = Best(matcher.Match(pack.Normalizer.Normalize("двадцать литров топлива")));
        Assert.Equal(20, best.Amount);
        Assert.Equal("liters", best.Unit);
    }

    [Fact]
    public void Negation_wins_by_specificity()
    {
        var best = Best(Match("ru", "не меняй шины"));

        Assert.Equal(IntentCatalog.TiresNone, best.Template.Intent);
        Assert.Equal(0, best.Distance);
    }

    [Fact]
    public void Fuzzy_match_within_one_word()
    {
        var best = Best(Match("ru", "меняй все эти шины"));

        Assert.Equal(IntentCatalog.TiresAll, best.Template.Intent);
        Assert.Equal(1, best.Distance);
        Assert.Equal(TemplateMatcher.FuzzyQuality, best.Quality);
    }

    [Fact]
    public void Fuzzy_match_is_not_allowed_for_short_templates()
    {
        // "меняй шины" has two tokens: an extra word is not tolerated.
        Assert.Null(Match("ru", "меняй шины быстро").Best);
    }

    [Fact]
    public void Fuzzy_match_never_drops_a_negation_from_the_phrase()
    {
        // "не меняй эти шины" vs "меняй все шины": dropping "не" is forbidden, so only negating intents are candidates.
        foreach (var (language, phrase) in new[] { ("ru", "не меняй эти шины"), ("en", "don't change these tires") })
        {
            var candidates = Match(language, phrase).Candidates;
            Assert.NotEmpty(candidates);
            Assert.All(candidates, c => Assert.True(IntentCatalog.Get(c.Template.Intent).IsNegation, $"{phrase} -> {c.Template.Source}"));
        }
    }

    [Fact]
    public void Fuzzy_match_never_inserts_a_negation_from_the_template()
    {
        // "no fast repair" must not be reached from "the fast repair" by replacing "the" with "no".
        var best = Best(Match("en", "the fast repair"));
        Assert.Equal(IntentCatalog.RepairFast, best.Template.Intent);
    }

    [Fact]
    public void Fuzzy_match_never_touches_slots()
    {
        Assert.Null(Match("ru", "залей тысячу литров").Best);
        Assert.Null(Match("ru", "залей двадцать тридцать литров").Best);
    }

    [Fact]
    public void Too_distant_phrase_does_not_match()
    {
        Assert.Null(Match("ru", "какая сегодня погода").Best);
        Assert.Null(Match("en", "what is the weather today").Best);
    }

    [Fact]
    public void Equally_good_fuzzy_candidates_of_different_intents_are_ambiguous()
    {
        var result = Match("en", "change this side");

        Assert.Null(result.Best);
        Assert.True(result.IsAmbiguous);
        Assert.Contains(result.Candidates, c => c.Template.Intent == IntentCatalog.TiresLeft);
        Assert.Contains(result.Candidates, c => c.Template.Intent == IntentCatalog.TiresRight);
    }

    [Fact]
    public void Longer_template_wins_over_shorter_at_equal_distance()
    {
        var pack = TestPacks.Ru;
        var templates = new[]
        {
            new Template(IntentCatalog.TiresAll, "меняй шины", [new WordLiteral("меняй"), new WordLiteral("шины")]),
            new Template(IntentCatalog.TiresNone, "не меняй шины", [new WordLiteral("не"), new WordLiteral("меняй"), new WordLiteral("шины")]),
        };
        var matcher = new TemplateMatcher(templates, pack.Negations);

        var best = Best(matcher.Match(pack.Normalizer.Normalize("не меняй шины")));
        Assert.Equal(IntentCatalog.TiresNone, best.Template.Intent);
    }

    [Fact]
    public void Literal_number_in_template_matches_number_token()
    {
        var best = Best(Match("ru", "все 4"));
        Assert.Equal(IntentCatalog.TiresAll, best.Template.Intent);
        Assert.Equal(0, best.Distance);
    }

    [Fact]
    public void Empty_input_matches_nothing()
    {
        var pack = TestPacks.Ru;
        var matcher = new TemplateMatcher(pack.Templates, pack.Negations);
        Assert.Null(matcher.Match([]).Best);
    }
}
