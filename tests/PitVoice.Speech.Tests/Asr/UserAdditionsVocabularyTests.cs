using PitVoice.Core.Asr;
using PitVoice.Core.Languages;
using PitVoice.Core.Parsing;
using PitVoice.Speech.Asr;
using Xunit;

namespace PitVoice.Speech.Tests.Asr;

/// <summary>Words of user pack additions in the recognizer vocabulary (FR-LANG-5, FR-ASR-2).</summary>
public sealed class UserAdditionsVocabularyTests
{
    private const string UnknownWord = "шмыгздрязь";

    private static LanguagePack RuWithAdditions()
    {
        var additions = UserAdditionsLoader.LoadText(
            $$"""
            language: ru
            intents:
              fuel.add:
                patterns:
                  - "плесни {amount} {unit?}"
                  - "{{UnknownWord}} {amount} {unit?}"
            """,
            "ru.yaml");
        var merged = UserAdditions.Merge(TestEnvironment.Pack("ru"), additions.Additions!);
        Assert.True(merged.Applied, string.Join('\n', merged.Issues));
        return merged.Pack;
    }

    [Fact]
    public void Addition_words_are_in_the_vocabulary_and_the_grammar()
    {
        var pack = RuWithAdditions();

        Assert.Contains("плесни", VocabularyBuilder.Build(pack));
        Assert.Contains("плесни", GrammarBuilder.Build(pack));
        Assert.Equal(["плесни", UnknownWord], VocabularyBuilder.BuildUserAdditionWords(pack));
        Assert.Empty(VocabularyBuilder.BuildUserAdditionWords(TestEnvironment.Pack("ru")));
    }

    [ModelFact("ru")]
    public async Task Addition_word_outside_the_model_is_reported_and_still_parses_as_text()
    {
        var pack = RuWithAdditions();
        using var engine = new VoskAsrEngine();

        Assert.True((await engine.LoadAsync(pack, TestEnvironment.RequireModel("ru"))).IsLoaded);

        var missing = GrammarBuilder.MissingAdditionWords(pack, engine.Vocabulary!);
        Assert.Contains(UnknownWord, missing);
        Assert.DoesNotContain("залей", missing);
        Assert.True(new CommandParser(pack).Parse($"{UnknownWord} тридцать литров").IsAccepted);
    }
}
