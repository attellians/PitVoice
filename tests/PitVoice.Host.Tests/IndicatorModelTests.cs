using Microsoft.Extensions.Time.Testing;
using PitVoice.Core.Cues;
using PitVoice.Core.Events;
using PitVoice.Core.Parsing;
using PitVoice.Core.Pit;
using PitVoice.Core.Ptt;
using PitVoice.Core.Settings;
using Xunit;

namespace PitVoice.Host.Tests;

/// <summary>Indicator state without WPF (design D1, D2; spec: Phrase states, Visibility by game state).</summary>
public sealed class IndicatorModelTests : IDisposable
{
    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 10, 18, 12, 0, 0, TimeSpan.Zero));
    private readonly List<IndicatorView> _changes = [];
    private IndicatorModel? _model;

    private IndicatorModel Model => _model ??= Create(OverlaySettings.Default, ConnectionState.InCar);

    public void Dispose() => _model?.Dispose();

    private IndicatorModel Create(OverlaySettings settings, ConnectionState game)
    {
        _model = new IndicatorModel(_time, settings, game);
        _model.Changed += _changes.Add;
        return _model;
    }

    private DateTimeOffset Now => _time.GetUtcNow();

    private IndicatorVisual Visual => Model.View.Visual;

    private IReadOnlyList<IndicatorVisual> Visuals => [.. _changes.Select(c => c.Visual).Distinct()];

    private void Feed(params PipelineEvent[] events)
    {
        foreach (var e in events)
        {
            Model.OnEvent(e);
        }
    }

    private void Record(int phrase = 1, RecordingStopReason reason = RecordingStopReason.Released) =>
        Feed(new PttPressed(phrase, Now), new PttReleased(phrase, Now, Ignored: false, reason));

    private ResultSignaled Signal(int phrase = 1, Cue cue = Cue.Error, bool cancelled = false) =>
        new(phrase, Now, cue, "response", cancelled, TimeSpan.FromMilliseconds(150));

    private static ParsedCommand Fuel => new(new Core.Intents.IntentId("fuel.add"), 30, "liters", 1);

    [Fact]
    public void Applied_phrase_goes_listening_recognizing_done_with_the_success_cue()
    {
        Model.OnEvent(new PttPressed(1, Now));
        Assert.Equal(IndicatorVisual.Listening, Visual);

        Feed(new PttReleased(1, Now, Ignored: false, RecordingStopReason.Released));
        Assert.Equal(IndicatorVisual.Recognizing, Visual);

        Feed(new CommandsParsed(1, Now, [Fuel]), new CommandsApplied(1, Now, []));
        Assert.Equal(IndicatorVisual.Recognizing, Visual); // the result comes with the cue (design D2)

        Feed(Signal(cue: Cue.Success));
        Assert.Equal(IndicatorVisual.Done, Visual);
        Assert.Equal([IndicatorVisual.Listening, IndicatorVisual.Recognizing, IndicatorVisual.Done], Visuals);
    }

    [Fact]
    public void Result_is_held_for_two_and_a_half_seconds_then_idle()
    {
        Record();
        Feed(new CommandsApplied(1, Now, []), Signal(cue: Cue.Success));

        _time.Advance(IndicatorModel.ResultHold - TimeSpan.FromMilliseconds(10));
        Assert.Equal(IndicatorVisual.Done, Visual);

        _time.Advance(TimeSpan.FromMilliseconds(10));
        Assert.Equal(IndicatorVisual.Idle, Visual);
        Assert.True(Model.View.Visible); // in the car
    }

    [Theory]
    [InlineData(RejectReason.NotUnderstood)]
    [InlineData(RejectReason.Conflict)]
    [InlineData(RejectReason.LowConfidence)]
    [InlineData(RejectReason.Empty)]
    public void Rejected_phrase_is_not_understood(RejectReason reason)
    {
        Record();
        Feed(new CommandsRejected(1, Now, reason, "details"), Signal());

        Assert.Equal(IndicatorVisual.NotUnderstood, Visual);
    }

    [Fact]
    public void Not_connected_is_no_game()
    {
        Record();
        Feed(new CommandsParsed(1, Now, [Fuel]), new ApplyFailed(1, Now, ExecutionOutcome.NotConnected, [Fuel]), Signal());

        Assert.Equal(IndicatorVisual.NoGame, Visual);
    }

    [Theory]
    [InlineData(ExecutionOutcome.NotInCar)]
    [InlineData(ExecutionOutcome.NotApplied)]
    [InlineData(ExecutionOutcome.PartiallyApplied)]
    [InlineData(ExecutionOutcome.CompoundUnavailable)]
    public void Other_outcomes_are_error(ExecutionOutcome outcome)
    {
        Record();
        Feed(new ApplyFailed(1, Now, outcome, []), Signal());

        Assert.Equal(IndicatorVisual.Error, Visual);
    }

    [Theory]
    [InlineData(PipelineStage.Capture)]
    [InlineData(PipelineStage.Recognition)]
    [InlineData(PipelineStage.Parsing)]
    [InlineData(PipelineStage.Execution)]
    public void Stage_failure_is_error(PipelineStage stage)
    {
        Record();
        Feed(new PipelineFailed(1, Now, stage, "boom"), Signal());

        Assert.Equal(IndicatorVisual.Error, Visual);
    }

    [Fact]
    public void Host_not_ready_is_error_at_once()
    {
        Feed(new PttPressed(1, Now), new PttUnavailable(1, Now, StatusReason.ModelLoading, "model"));

        Assert.Equal(IndicatorVisual.Error, Visual);
        _time.Advance(IndicatorModel.ResultHold);
        Assert.Equal(IndicatorVisual.Idle, Visual);
    }

    [Fact]
    public void Result_cue_that_did_not_play_still_shows_the_result()
    {
        Record();
        Feed(new CommandsRejected(1, Now, RejectReason.NotUnderstood, "x"), new PipelineFailed(1, Now, PipelineStage.Cue, "no device"));

        Assert.Equal(IndicatorVisual.NotUnderstood, Visual);
    }

    [Fact]
    public void Failed_start_cue_does_not_end_the_phrase()
    {
        Feed(new PttPressed(1, Now), new PipelineFailed(1, Now, PipelineStage.Cue, "no device"));
        Assert.Equal(IndicatorVisual.Listening, Visual);

        Feed(new PttReleased(1, Now, Ignored: false, RecordingStopReason.Released), new CommandsApplied(1, Now, []), Signal(cue: Cue.Success));
        Assert.Equal(IndicatorVisual.Done, Visual);
    }

    [Fact]
    public void Ignored_short_press_returns_to_idle_without_recognizing_or_result()
    {
        Feed(new PttPressed(1, Now), new PttReleased(1, Now, Ignored: true, Reason: null));

        Assert.Equal(IndicatorVisual.Idle, Visual);
        Assert.Equal([IndicatorVisual.Listening, IndicatorVisual.Idle], Visuals);
    }

    [Fact]
    public void Radio_auto_stop_recognizes_and_shows_the_result()
    {
        Record(reason: RecordingStopReason.AutoStop);
        Assert.Equal(IndicatorVisual.Recognizing, Visual);

        Feed(new CommandsApplied(1, Now, []), Signal(cue: Cue.Success));
        Assert.Equal(IndicatorVisual.Done, Visual);
    }

    [Fact]
    public void New_press_during_processing_shows_listening_and_drops_the_old_result()
    {
        Record(phrase: 1);
        Feed(new PttPressed(2, Now));
        Assert.Equal(IndicatorVisual.Listening, Visual);

        Feed(new CommandsApplied(1, Now, []), Signal(phrase: 1, cue: Cue.Success, cancelled: true));
        Assert.Equal(IndicatorVisual.Listening, Visual);

        Feed(new PttReleased(2, Now, Ignored: false, RecordingStopReason.Released), new CommandsRejected(2, Now, RejectReason.Empty, ""), Signal(phrase: 2));
        Assert.Equal(IndicatorVisual.NotUnderstood, Visual);
    }

    [Fact]
    public void Press_during_the_hold_shows_listening_and_the_old_hold_does_not_end_it()
    {
        Record(phrase: 1);
        Feed(new CommandsApplied(1, Now, []), Signal(phrase: 1, cue: Cue.Success));
        _time.Advance(TimeSpan.FromSeconds(1));

        Feed(new PttPressed(2, Now));
        Assert.Equal(IndicatorVisual.Listening, Visual);

        _time.Advance(TimeSpan.FromSeconds(5));
        Assert.Equal(IndicatorVisual.Listening, Visual);
    }

    [Fact]
    public void Without_the_game_a_phrase_is_not_shown()
    {
        var model = Create(OverlaySettings.Default, ConnectionState.NotRunning);
        Assert.False(model.View.Visible);

        Record();
        Assert.False(model.View.Visible);
        Feed(new ApplyFailed(1, Now, ExecutionOutcome.NotConnected, []), Signal());
        Assert.Equal(new IndicatorView(IndicatorVisual.NoGame, Visible: false, Moving: false), model.View);
    }

    [Fact]
    public void Outside_the_car_idle_and_phrases_are_hidden()
    {
        var model = Create(OverlaySettings.Default, ConnectionState.Connected);
        Assert.False(model.View.Visible);

        Record();
        Feed(new ApplyFailed(1, Now, ExecutionOutcome.NotInCar, []), Signal());
        Assert.False(model.View.Visible);
        Assert.DoesNotContain(_changes, c => c.Visible);
    }

    [Fact]
    public void Getting_in_the_car_shows_the_idle_icon_and_getting_out_hides_it()
    {
        var model = Create(OverlaySettings.Default, ConnectionState.Connected);

        model.OnGame(ConnectionState.InCar);
        Assert.Equal(new IndicatorView(IndicatorVisual.Idle, Visible: true, Moving: false), model.View);

        model.OnGame(ConnectionState.Connected);
        Assert.False(model.View.Visible);
    }

    [Fact]
    public void Phrase_started_in_the_car_is_shown_to_its_end_after_getting_out()
    {
        Record();
        Model.OnGame(ConnectionState.Connected);
        Assert.True(Model.View.Visible);

        Feed(new ApplyFailed(1, Now, ExecutionOutcome.NotInCar, []), Signal());
        Assert.Equal(new IndicatorView(IndicatorVisual.Error, Visible: true, Moving: false), Model.View);

        _time.Advance(IndicatorModel.ResultHold);
        Assert.False(Model.View.Visible);
    }

    [Fact]
    public void Game_closed_during_a_result_holds_it_then_hides()
    {
        Record();
        Feed(new CommandsApplied(1, Now, []), Signal(cue: Cue.Success));

        Model.OnGame(ConnectionState.NotRunning);
        Assert.Equal(new IndicatorView(IndicatorVisual.Done, Visible: true, Moving: false), Model.View);

        _time.Advance(IndicatorModel.ResultHold);
        Assert.False(Model.View.Visible);
    }

    [Fact]
    public void Turned_off_indicator_is_never_shown()
    {
        var model = Create(OverlaySettings.Default with { Enabled = false }, ConnectionState.InCar);
        model.Moving = true;
        Record();

        Assert.False(model.View.Visible);
        Assert.False(model.Moving);
    }

    [Fact]
    public void Turn_off_while_showing_a_phrase_hides_at_once_and_ends_move_mode()
    {
        Model.Moving = true;
        Record();
        Assert.True(Model.View.Visible);

        Model.Apply(OverlaySettings.Default with { Enabled = false });

        Assert.False(Model.View.Visible);
        Assert.False(Model.Moving);
        Feed(new CommandsApplied(1, Now, []), Signal(cue: Cue.Success));
        Assert.False(Model.View.Visible);
    }

    [Fact]
    public void Move_mode_is_visible_without_the_game()
    {
        var model = Create(OverlaySettings.Default, ConnectionState.NotRunning);

        model.Moving = true;
        Assert.Equal(new IndicatorView(IndicatorVisual.Idle, Visible: true, Moving: true), model.View);

        Record();
        Assert.Equal(new IndicatorView(IndicatorVisual.Recognizing, Visible: true, Moving: true), model.View);

        model.Moving = false;
        Assert.False(model.View.Moving);
    }

    [Fact]
    public void Handler_error_goes_to_the_callback_when_given()
    {
        var errors = new List<Exception>();
        using var model = new IndicatorModel(_time, OverlaySettings.Default, ConnectionState.InCar, errors.Add);
        model.Changed += _ => throw new InvalidOperationException("window failed");

        model.OnEvent(new PttPressed(1, Now));

        Assert.Equal(IndicatorVisual.Listening, model.View.Visual);
        Assert.Equal("window failed", Assert.Single(errors).Message);
    }
}
