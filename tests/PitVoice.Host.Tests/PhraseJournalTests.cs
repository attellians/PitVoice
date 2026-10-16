using PitVoice.Core.Cues;
using PitVoice.Core.Events;
using PitVoice.Core.Intents;
using PitVoice.Core.Parsing;
using PitVoice.Core.Pit;
using PitVoice.Core.Ptt;
using Xunit;

namespace PitVoice.Host.Tests;

/// <summary>Recent phrases of the main page (design decision 11, spec: Recent phrases).</summary>
public sealed class PhraseJournalTests
{
    private static readonly DateTimeOffset T0 = new(2026, 10, 15, 18, 0, 0, TimeSpan.Zero);

    private static RecognizedPhrase Words(string text) => RecognizedPhrase.FromText(text);

    private static PhraseJournal Feed(params PipelineEvent[] events)
    {
        var journal = new PhraseJournal();
        foreach (var e in events)
        {
            journal.OnEvent(e);
        }

        return journal;
    }

    [Fact]
    public void Applied_phrase_has_text_outcome_and_response()
    {
        var journal = Feed(
            new PttPressed(1, T0),
            new PttReleased(1, T0, false, RecordingStopReason.Released),
            new SpeechRecognized(1, T0, Words("меняй шины и залей двадцать пять литров"), TimeSpan.Zero),
            new CommandsParsed(1, T0, [new ParsedCommand(IntentCatalog.TiresAll, null, null, 1), new ParsedCommand(IntentCatalog.FuelAdd, 25, "liters", 1)]),
            new CommandsApplied(1, T0, [new SentCommand(PitCommand.Fuel, 25)]),
            new ResultSignaled(1, T0, Cue.Success, "Меняем все шины, заливаем 25 литров", false, TimeSpan.Zero));

        var entry = Assert.Single(journal.Entries);
        Assert.Equal(1, entry.PhraseId);
        Assert.Equal(T0, entry.PressedAt);
        Assert.Equal("меняй шины и залей двадцать пять литров", entry.Text);
        Assert.Equal(PhraseOutcome.Applied, entry.Outcome);
        Assert.Equal("Меняем все шины, заливаем 25 литров", entry.Response);
        Assert.True(entry.Finished);
    }

    [Fact]
    public void Not_understood_phrase_shows_the_rejection()
    {
        var journal = Feed(
            new PttPressed(1, T0),
            new SpeechRecognized(1, T0, Words("залей"), TimeSpan.Zero),
            new CommandsRejected(1, T0, RejectReason.LowConfidence, "confidence 0.41 < 0.60"),
            new ResultSignaled(1, T0, Cue.Error, "не понял, повтори", false, TimeSpan.Zero));

        var entry = Assert.Single(journal.Entries);
        Assert.Equal(PhraseOutcome.NotUnderstood, entry.Outcome);
        Assert.Equal(RejectReason.LowConfidence, entry.Rejected);
        Assert.Equal("не понял, повтори", entry.Response);
    }

    [Fact]
    public void Failed_stage_is_shown_with_the_reason()
    {
        var journal = Feed(
            new PttPressed(1, T0),
            new PipelineFailed(1, T0, PipelineStage.Capture, "no microphone"),
            new ResultSignaled(1, T0, Cue.Error, string.Empty, false, TimeSpan.Zero));

        var entry = Assert.Single(journal.Entries);
        Assert.Equal(PhraseOutcome.Failed, entry.Outcome);
        Assert.Equal(PipelineStage.Capture, entry.FailedStage);
        Assert.Equal("no microphone", entry.Error);
        Assert.Null(entry.Text);
    }

    [Fact]
    public void Execution_outcome_other_than_applied_is_kept()
    {
        var journal = Feed(
            new PttPressed(1, T0),
            new SpeechRecognized(1, T0, Words("залей тридцать"), TimeSpan.Zero),
            new ApplyFailed(1, T0, ExecutionOutcome.NotConnected, []),
            new ResultSignaled(1, T0, Cue.Error, "нет связи с игрой", false, TimeSpan.Zero));

        Assert.Equal(PhraseOutcome.NotConnected, Assert.Single(journal.Entries).Outcome);
    }

    [Fact]
    public void Ignored_short_press_is_not_shown()
    {
        var journal = Feed(new PttPressed(1, T0), new PttReleased(1, T0, true, null));

        Assert.Empty(journal.Entries);
    }

    [Fact]
    public void Unavailable_press_is_shown_with_its_reason()
    {
        var journal = Feed(new PttPressed(1, T0), new PttUnavailable(1, T0, StatusReason.ModelLoading, "vosk-model-small-en-us-0.15"));

        var entry = Assert.Single(journal.Entries);
        Assert.Equal(PhraseOutcome.Unavailable, entry.Outcome);
        Assert.Equal(StatusReason.ModelLoading, entry.Unavailable);
    }

    [Fact]
    public void Twenty_first_phrase_drops_the_oldest_and_newest_comes_first()
    {
        var journal = new PhraseJournal();
        var changes = 0;
        journal.Changed += () => changes++;
        for (var id = 1; id <= 21; id++)
        {
            journal.OnEvent(new PttPressed(id, T0.AddSeconds(id)));
        }

        Assert.Equal(PhraseJournal.Capacity, journal.Entries.Count);
        Assert.Equal(21, journal.Entries[0].PhraseId);
        Assert.Equal(2, journal.Entries[^1].PhraseId);
        Assert.Equal(21, changes);
    }

    [Fact]
    public async Task Journal_survives_a_session_rebuild()
    {
        await using var fixture = new HostFixture(installedModels: [HostFixture.RuModel, HostFixture.EnModel]);
        var host = await fixture.StartAsync();
        fixture.Say("залей тридцать литров");
        await fixture.WaitForAsync<ResultSignaled>();

        host.Update(s => s with { Language = "en" });
        await host.WhenAppliedAsync().WaitAsync(HostFixture.Wait);
        fixture.Say("change all tires");
        await fixture.WaitForAsync<ResultSignaled>(e => e.PhraseId == 2);
        await LiveApplyTests.WaitUntilAsync(() => host.Journal.Entries is [{ Finished: true }, { Finished: true }]);

        Assert.Equal(["change all tires", "залей тридцать литров"], host.Journal.Entries.Select(e => e.Text));
        Assert.Equal("заливаем 30 литров", host.Journal.Entries[1].Response);
    }
}
