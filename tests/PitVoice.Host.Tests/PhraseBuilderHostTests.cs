using PitVoice.Core.Intents;
using PitVoice.Core.Languages;
using PitVoice.Core.Settings;
using Xunit;

namespace PitVoice.Host.Tests;

/// <summary>Draft check, phrase check and save of own phrases (design D4–D7 of replace-additions-with-phrase-builder).</summary>
public sealed class PhraseBuilderHostTests
{
    private static UserPackAdditions Draft(params (string Text, IntentId Intent)[] phrases) =>
        new("draft", "ru", [], phrases.Select((p, i) => new AdditionPhrase(p.Text, p.Intent, null) { Entry = i }).ToArray());

    [Fact]
    public async Task Save_writes_the_file_and_applies_the_phrase_at_once()
    {
        await using var fixture = new HostFixture();
        var host = await fixture.StartAsync();
        Assert.Empty(host.Pack!.UserPhrases);

        var error = await host.SaveAdditionsAsync(Draft(("меняй все шины", IntentCatalog.TiresAll), ("идет дождь", IntentCatalog.TiresWet)));

        Assert.Null(error);
        var file = UserAdditionsLoader.GetFile(fixture.AdditionsDirectory, "ru");
        var text = File.ReadAllText(file);
        Assert.Contains("идет дождь", text, StringComparison.Ordinal);
        Assert.DoesNotContain("меняй все шины", text, StringComparison.Ordinal);
        Assert.True(host.Additions!.Applied);
        Assert.Equal(["идет дождь"], host.Pack!.UserPhrases.Select(p => p.Text));
        Assert.NotNull(host.Engine);

        fixture.Say("идет дождь");
        var parsed = await fixture.WaitForAsync<Core.Events.CommandsParsed>();
        Assert.Equal(IntentCatalog.TiresWet, Assert.Single(parsed.Commands).Intent);
    }

    [Fact]
    public async Task Write_error_keeps_the_previous_phrases()
    {
        await using var fixture = new HostFixture();
        var host = await fixture.StartAsync();
        Assert.Null(await host.SaveAdditionsAsync(Draft(("идет дождь", IntentCatalog.TiresWet))));
        var file = UserAdditionsLoader.GetFile(fixture.AdditionsDirectory, "ru");
        var before = File.ReadAllText(file);

        // A directory where the temporary file goes fails the write on every OS (a lock blocks a replace only on Windows).
        Directory.CreateDirectory(file + ".tmp");
        var error = await host.SaveAdditionsAsync(Draft(("начнем заново", IntentCatalog.PitClear)));

        Assert.NotNull(error);
        Assert.Equal(before, File.ReadAllText(file));
        Assert.Equal(["идет дождь"], host.Pack!.UserPhrases.Select(p => p.Text));
    }

    [Fact]
    public async Task Draft_with_errors_is_not_saved()
    {
        await using var fixture = new HostFixture();
        var host = await fixture.StartAsync();

        var error = await host.SaveAdditionsAsync(Draft(("почисти", IntentCatalog.WindshieldTearoff)));

        Assert.NotNull(error);
        Assert.False(File.Exists(UserAdditionsLoader.GetFile(fixture.AdditionsDirectory, "ru")));
    }

    [Fact]
    public async Task Checks_publish_no_events_and_do_not_count_phrases()
    {
        await using var fixture = new HostFixture();
        var host = await fixture.StartAsync();
        var draft = Draft(("идет дождь", IntentCatalog.TiresWet), ("почисти", IntentCatalog.WindshieldTearoff));
        var events = fixture.Events.Count;

        var check = host.CheckDraft(draft)!;
        var response = host.TryPhrase(draft, "идет дождь и залей двадцать литров");
        var notUnderstood = host.TryPhrase(draft, "кто впереди меня");

        Assert.True(check.HasErrors);
        Assert.Equal(1, Assert.Single(check.Issues).Entry);
        Assert.Equal("ставим дождевые, заливаем 20 литров", response);
        Assert.Null(notUnderstood);
        Assert.Equal(events, fixture.Events.Count);
        Assert.Equal(0, host.Journal.TotalCount);
        Assert.Empty(host.Pack!.UserPhrases);
        Assert.False(File.Exists(UserAdditionsLoader.GetFile(fixture.AdditionsDirectory, "ru")));
    }

    [Fact]
    public async Task Words_outside_the_model_are_errors_of_their_row()
    {
        await using var fixture = new HostFixture();
        fixture.Platform.UnknownWords.Add("дождь");
        var host = await fixture.StartAsync();

        var check = host.CheckDraft(Draft(("начнем заново", IntentCatalog.PitClear), ("идёт дождь", IntentCatalog.TiresWet)))!;

        Assert.True(check.ModelLoaded);
        var error = Assert.Single(check.Issues);
        Assert.Equal((1, AdditionProblem.NotInModel, "дождь"), (error.Entry, error.Problem, error.Argument));
    }

    [Fact]
    public async Task Without_a_model_words_are_not_checked()
    {
        await using var fixture = new HostFixture(AppSettings.Defaults("ru") with { PttBinding = HostFixture.KeyBinding }, HostFixture.EnModel);
        fixture.Platform.UnknownWords.Add("дождь");
        var host = await fixture.StartAsync();

        var check = host.CheckDraft(Draft(("идёт дождь", IntentCatalog.TiresWet)))!;

        Assert.Null(host.Engine);
        Assert.False(check.ModelLoaded);
        Assert.Empty(check.Issues);
    }
}
