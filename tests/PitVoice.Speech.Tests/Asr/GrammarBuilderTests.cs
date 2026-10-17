using PitVoice.Core.Asr;
using PitVoice.Speech.Asr;
using Xunit;

namespace PitVoice.Speech.Tests.Asr;

public sealed class GrammarBuilderTests
{
    [Theory]
    [InlineData("ru")]
    [InlineData("en")]
    public void Grammar_SamePack_Deterministic(string language)
    {
        var pack = TestEnvironment.Pack(language);

        Assert.Equal(GrammarBuilder.Build(pack), GrammarBuilder.Build(pack));
    }

    [Theory]
    [InlineData("ru")]
    [InlineData("en")]
    public void Grammar_HasUnknownMarkerAndNoSlots(string language)
    {
        var grammar = GrammarBuilder.Build(TestEnvironment.Pack(language));

        Assert.Contains(SpeechRecognizer.UnknownWord, grammar);
        Assert.DoesNotContain(grammar, e => e.Contains('{', StringComparison.Ordinal) || e.Contains('}', StringComparison.Ordinal));
        Assert.DoesNotContain(grammar, string.IsNullOrWhiteSpace);
        Assert.Equal(grammar.Distinct(StringComparer.Ordinal).Count(), grammar.Count);
    }

    [Theory]
    [InlineData("ru")]
    [InlineData("en")]
    public void Grammar_Words_EqualsVocabularyPlusUnknown(string language)
    {
        var pack = TestEnvironment.Pack(language);

        var grammar = GrammarBuilder.Build(pack);

        Assert.Equal(VocabularyBuilder.Build(pack).Append(SpeechRecognizer.UnknownWord).Order(StringComparer.Ordinal), grammar);
    }

    [Fact]
    public void Grammar_ModelVocabulary_UsesYoSpellingAndDropsMissingWords()
    {
        var pack = TestEnvironment.Pack("ru");
        var plain = GrammarBuilder.Build(pack);
        var model = FakeModelVocabulary(pack, spelledWithYo: [("еще", "ещё")], absent: ["долей"]);

        var grammar = GrammarBuilder.Build(pack, model.Contains);

        Assert.Contains(plain, e => e.Split(' ').Contains("еще"));
        Assert.Contains(plain, e => e.Split(' ').Contains("долей"));
        Assert.Contains(grammar, e => e.Split(' ').Contains("ещё"));
        Assert.DoesNotContain(grammar, e => e.Split(' ').Contains("еще"));
        Assert.DoesNotContain(grammar, e => e.Split(' ').Contains("долей"));
        Assert.Contains(SpeechRecognizer.UnknownWord, grammar);
        Assert.Equal(grammar, GrammarBuilder.Build(pack, model.Contains));
    }

    [Fact]
    public void Grammar_CheckVocabulary_ReportsSubstitutionsAndMissing()
    {
        var pack = TestEnvironment.Pack("ru");
        var model = FakeModelVocabulary(pack, spelledWithYo: [("еще", "ещё")], absent: ["заправить", "галлона"]);

        var report = GrammarBuilder.CheckVocabulary(pack, model.Contains);

        Assert.Equal(new Dictionary<string, string> { ["еще"] = "ещё" }, report.Substituted);
        Assert.Equal(["галлона", "заправить"], report.Missing.Order(StringComparer.Ordinal));
    }

    [Theory]
    [InlineData("еще", "ещё")]
    [InlineData("пленку", "плёнку")]
    [InlineData("литров", "литров")]
    public void Grammar_ModelSpelling_PrefersFewestReplacements(string word, string expected)
    {
        var known = new HashSet<string>(StringComparer.Ordinal) { "ещё", "ёщё", "плёнку", "литров" };

        Assert.Equal(expected, GrammarBuilder.ModelSpelling(word, known.Contains));
    }

    [Fact]
    public void Grammar_YoSpellingFromRecognizer_StillParses()
    {
        var pack = TestEnvironment.Pack("ru");
        var parser = new Core.Parsing.CommandParser(pack);

        var withYo = parser.Parse("меняй шины ещё сорви плёнку");

        Assert.True(withYo.IsAccepted, withYo.ToString());
        Assert.Equal(parser.Parse("меняй шины еще сорви пленку"), withYo);
    }

    /// <summary>A model that knows every pack word, except <paramref name="absent"/> and <paramref name="spelledWithYo"/> (known only with ё).</summary>
    private static HashSet<string> FakeModelVocabulary(Core.Languages.LanguagePack pack, (string Pack, string Model)[] spelledWithYo, string[] absent)
    {
        var words = new HashSet<string>(VocabularyBuilder.Build(pack), StringComparer.Ordinal);
        words.ExceptWith(absent);
        foreach (var (word, spelled) in spelledWithYo)
        {
            words.Remove(word);
            words.Add(spelled);
        }

        return words;
    }

    [Fact]
    public void Grammar_Json_KeepsCyrillicAndApostrophesLiteral()
    {
        var json = GrammarBuilder.ToJson(["залей", "don't", "[unk]"]);

        Assert.Equal("""["залей","don't","[unk]"]""", json);
    }
}
