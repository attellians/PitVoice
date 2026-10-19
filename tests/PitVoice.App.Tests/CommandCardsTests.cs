using System.IO;
using PitVoice.App.Controls;
using PitVoice.App.Localization;
using PitVoice.App.ViewModels;
using PitVoice.Core.Languages;
using PitVoice.Host;
using Xunit;

namespace PitVoice.App.Tests;

/// <summary>The cards of the "Commands" page (design D5; spec: Command reference, User additions in the reference).</summary>
[Collection("Interface language")]
public sealed class CommandCardsTests : IDisposable
{
    private readonly string _language = Loc.Instance.Language;

    public CommandCardsTests() => Loc.Instance.SetLanguage("ru");

    public void Dispose() => Loc.Instance.SetLanguage(_language);

    private static LanguagePack Pack(string language)
    {
        var result = LanguagePackLoader.LoadFile(Path.Combine(AppContext.BaseDirectory, "languages", $"{language}.yaml"));
        Assert.True(result.Pack is not null, string.Join('\n', result.Issues));
        return result.Pack;
    }

    private static GroupEntry Group(IReadOnlyList<GroupEntry> cards, string name) => Assert.Single(cards, g => g.Name == name);

    [Fact]
    public void Fuel_card_shows_the_slot_example_and_no_fuel()
    {
        var cards = CommandCards.Build(CommandReference.Build(Pack("ru")));
        var fuel = Group(cards, "Топливо");

        Assert.Equal(Section.Commands, fuel.Section);
        Assert.Contains("залей ‹число› ‹единица›", fuel.Examples);
        Assert.Contains("без топлива", fuel.Examples);
        Assert.InRange(fuel.Examples.Count, 2, CommandCards.MaxExamples);
        Assert.Equal(fuel.Examples.Distinct().Count(), fuel.Examples.Count);
        Assert.NotEmpty(fuel.Description);
        Assert.Contains(fuel.Classes, c => c.Name == "Действия" && c.Words.Any(w => w.Text == "залей"));
    }

    [Fact]
    public void Tires_card_has_the_wet_compound_example_only_with_compounds()
    {
        var pack = Pack("ru");
        var tires = Group(CommandCards.Build(CommandReference.Build(pack)), "Шины");

        Assert.Equal(Section.Home, tires.Section);
        Assert.Contains(tires.Examples, e => e.EndsWith("резину на дождевые", StringComparison.Ordinal)); // the first wet self-test of the pack
        Assert.Contains(tires.Examples, e => e.StartsWith("не ", StringComparison.Ordinal) || e.Contains(" не ", StringComparison.Ordinal)); // a removal
        Assert.Equal(CommandCards.MaxExamples, tires.Examples.Count);
        Assert.Contains(tires.Classes, c => c.Name == "Дождевые");
        Assert.Contains(tires.Commands, c => c.Name == "Дождевые шины");
        Assert.NotEmpty(tires.ElidedAction);

        var withoutCompounds = LanguagePackLoader.LoadText(RemoveCompounds(File.ReadAllText(pack.SourceFile)), "ru.yaml");
        Assert.True(withoutCompounds.Pack is not null, string.Join('\n', withoutCompounds.Issues));
        var plain = Group(CommandCards.Build(CommandReference.Build(withoutCompounds.Pack)), "Шины");
        Assert.DoesNotContain(plain.Examples, e => e.Contains("дождевые", StringComparison.Ordinal));
        Assert.DoesNotContain(plain.Commands, c => c.Name == "Дождевые шины");
    }

    [Fact]
    public void Own_word_is_in_the_classes_but_not_in_the_examples()
    {
        var directory = Path.Combine(Path.GetTempPath(), "pitvoice-cards-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            File.WriteAllText(Path.Combine(directory, "ru.yaml"), "lexicon:\n  fuel: { action: [плесни] }\n");
            var additions = UserAdditions.Apply(Pack("ru"), directory);
            Assert.True(additions.Applied, string.Join('\n', additions.Issues));

            var fuel = Group(CommandCards.Build(CommandReference.Build(additions.Pack)), "Топливо");

            Assert.Contains(fuel.Classes, c => c.Words.Any(w => w is { Text: "плесни", IsOwn: true }));
            Assert.DoesNotContain(fuel.Examples, e => e.Contains("плесни", StringComparison.Ordinal));
            Assert.DoesNotContain(fuel.Commands.SelectMany(c => c.Examples), e => e.Contains("плесни", StringComparison.Ordinal));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void English_cards_use_the_english_pack()
    {
        Loc.Instance.SetLanguage("en");
        var fuel = Group(CommandCards.Build(CommandReference.Build(Pack("en"))), "Fuel");

        Assert.Contains(fuel.Examples, e => e.Contains("‹number›", StringComparison.Ordinal));
        Assert.Contains(fuel.Classes, c => c.Name == "Actions" && c.Words.Any(w => w.Text == "add"));
    }

    [Fact]
    public void Every_group_has_a_hue_and_a_symbol()
    {
        foreach (var group in Enum.GetValues<CommandGroup>())
        {
            Assert.NotEqual(Wpf.Ui.Controls.SymbolRegular.Empty, CommandCards.SymbolOf(group));
            _ = CommandCards.SectionOf(group);
        }

        Assert.Equal(Section.Alert, CommandCards.SectionOf(CommandGroup.Repair));
    }

    /// <summary>Drops the <c>compounds:</c> block of the tires lexicon (two indented lines under it).</summary>
    private static string RemoveCompounds(string yaml)
    {
        var lines = yaml.Split('\n').ToList();
        var start = lines.FindIndex(l => l.TrimStart().StartsWith("compounds:", StringComparison.Ordinal));
        Assert.True(start >= 0, "no compounds in ru.yaml");
        var indent = lines[start].Length - lines[start].TrimStart().Length;
        var end = start + 1;
        while (end < lines.Count && (lines[end].Trim().Length == 0 || lines[end].Length - lines[end].TrimStart().Length > indent))
        {
            end++;
        }

        lines.RemoveRange(start, end - start);

        // The self-tests of the compounds would expect intents the pack no longer has: drop each "- say" / "expect" pair.
        for (var i = lines.Count - 1; i > 0; i--)
        {
            if (lines[i].Contains("tires.wet", StringComparison.Ordinal) || lines[i].Contains("tires.dry", StringComparison.Ordinal))
            {
                if (lines[i].TrimStart().StartsWith("expect:", StringComparison.Ordinal) && lines[i - 1].TrimStart().StartsWith("- say:", StringComparison.Ordinal))
                {
                    lines.RemoveRange(i - 1, 2);
                    i--;
                }
            }
        }

        return string.Join('\n', lines);
    }
}
