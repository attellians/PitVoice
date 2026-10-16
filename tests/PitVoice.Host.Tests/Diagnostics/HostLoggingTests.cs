using PitVoice.Core.Events;
using PitVoice.Core.Pit;
using PitVoice.Host.Diagnostics;
using PitVoice.IRacing.Diagnostics;
using Xunit;

namespace PitVoice.Host.Tests.Diagnostics;

/// <summary>Application events in the log (design decision 2, spec: Application events in the log).</summary>
public sealed class HostLoggingTests
{
    [Fact]
    public async Task Start_writes_version_settings_pack_and_model()
    {
        var capture = new LogCapture();
        await using var fixture = new HostFixture { Log = capture.CreateLogger() };
        await fixture.StartAsync();

        var lines = capture.Lines;
        Assert.Contains(lines, l => l.StartsWith("INF PitVoice ", StringComparison.Ordinal) && l.Contains(" started on ", StringComparison.Ordinal));
        Assert.Contains(lines, l => l.StartsWith("INF Settings at start: ", StringComparison.Ordinal) && l.Contains("language ru", StringComparison.Ordinal));
        Assert.Contains(lines, l => l.Contains("Language pack \"ru\" loaded", StringComparison.Ordinal));
        Assert.Contains(lines, l => l.Contains($"Model \"{HostFixture.RuModel}\" loaded in", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Model_failed_to_load()
    {
        var capture = new LogCapture();
        await using var fixture = new HostFixture { Log = capture.CreateLogger() };
        fixture.Platform.LoadError = "model files are damaged";
        await fixture.StartAsync();

        Assert.Contains(capture.Lines, l => l.StartsWith("ERR ", StringComparison.Ordinal)
            && l.Contains(HostFixture.RuModel, StringComparison.Ordinal)
            && l.Contains("model files are damaged", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Settings_changed_gives_one_record_after_apply()
    {
        var capture = new LogCapture();
        await using var fixture = new HostFixture { Log = capture.CreateLogger() };
        var host = await fixture.StartAsync();
        var before = capture.Count;

        host.Update(s => s with { Microphone = "USB Headset" });
        await host.WhenAppliedAsync().WaitAsync(HostFixture.Wait);

        var line = Assert.Single(capture.LinesAfter(before));
        Assert.StartsWith("INF Settings applied: ", line, StringComparison.Ordinal);
        Assert.Contains("microphone USB Headset", line, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Idle_game_polling_writes_nothing()
    {
        var capture = new LogCapture();
        await using var fixture = new HostFixture { Log = capture.CreateLogger() };
        fixture.Platform.Game.State = ConnectionState.NotRunning;
        await fixture.StartAsync();
        var before = capture.Count;

        for (var i = 0; i < 60; i++)
        {
            fixture.Time.Advance(TimeSpan.FromSeconds(2));
        }

        Assert.Empty(capture.LinesAfter(before));
    }

    [Fact]
    public async Task Game_connection_changes_are_written_once_each()
    {
        var capture = new LogCapture();
        await using var fixture = new HostFixture { Log = capture.CreateLogger() };
        fixture.Platform.Game.State = ConnectionState.NotRunning;
        await fixture.StartAsync();
        var before = capture.Count;

        fixture.Platform.Game.State = ConnectionState.InCar;
        fixture.Platform.Elevation = GameElevation.Elevated;
        fixture.Time.Advance(TimeSpan.FromSeconds(2));
        fixture.Time.Advance(TimeSpan.FromSeconds(2));
        fixture.Platform.Game.State = ConnectionState.NotRunning;
        fixture.Time.Advance(TimeSpan.FromSeconds(2));

        Assert.Equal(
            ["INF iRacing: \"in the car\"", "WRN iRacing runs as administrator while PitVoice does not", "INF iRacing: \"not running\""],
            capture.LinesAfter(before));
    }

    [Fact]
    public async Task Stage_failure_is_written_with_stage_phrase_id_and_error_and_the_next_one_too()
    {
        var capture = new LogCapture();
        await using var fixture = new HostFixture { Log = capture.CreateLogger() };
        await fixture.StartAsync();

        fixture.Platform.Script.Throw("engine crashed");
        fixture.PressAndRelease();
        var first = await fixture.WaitForAsync<PipelineFailed>();
        fixture.Platform.Script.Throw("engine crashed again");
        fixture.PressAndRelease();
        var second = await fixture.WaitForAsync<PipelineFailed>(f => f.PhraseId != first.PhraseId);
        await fixture.Host.Events.DrainAsync();

        var errors = capture.Lines.Where(l => l.StartsWith("ERR Stage", StringComparison.Ordinal)).ToList();
        Assert.Equal(2, errors.Count);
        Assert.Contains($"Recognition failed, phrase {first.PhraseId}: \"recognition failed: engine crashed\"", errors[0], StringComparison.Ordinal);
        Assert.Contains($"phrase {second.PhraseId}", errors[1], StringComparison.Ordinal);
    }

    [Fact]
    public async Task Pit_commands_not_applied_has_the_outcome_and_the_rights_hint_but_no_phrase_or_commands()
    {
        var capture = new LogCapture();
        await using var fixture = new HostFixture { Log = capture.CreateLogger() };
        fixture.Platform.ExecutionLog = new ElevationHintLog(new SerilogExecutionLog(fixture.Log), new ElevatedGame());
        fixture.Platform.Game.Ignored.Add(PitCommand.Fuel);
        await fixture.StartAsync();
        var before = capture.Count;

        fixture.Say("залей тридцать литров");
        await fixture.WaitForAsync<ResultSignaled>();

        var lines = capture.LinesAfter(before);
        Assert.Equal(2, lines.Count);
        Assert.StartsWith("WRN Pit commands \"not applied\": ", lines[0], StringComparison.Ordinal);
        Assert.Contains("nothing confirmed", lines[0], StringComparison.Ordinal);
        Assert.Contains(ElevationHintLog.Hint, lines[1], StringComparison.Ordinal);
        Assert.All(lines, l =>
        {
            Assert.DoesNotContain("тридцать", l, StringComparison.Ordinal);
            Assert.DoesNotContain("30", l, StringComparison.Ordinal);
            Assert.DoesNotContain("Fuel", l, StringComparison.Ordinal);
        });
    }

    [Fact]
    public async Task Event_subscriber_failure_is_written()
    {
        var capture = new LogCapture();
        await using var fixture = new HostFixture { Log = capture.CreateLogger() };
        var host = await fixture.StartAsync();
        host.Events.Subscribe(_ => throw new InvalidOperationException("subscriber is broken"));

        fixture.Platform.Script.Throw("x");
        fixture.PressAndRelease();
        await fixture.WaitForAsync<PipelineFailed>();
        await host.Events.DrainAsync();

        Assert.Contains(capture.Lines, l => l.StartsWith("ERR Event subscriber failed", StringComparison.Ordinal)
            && l.Contains("subscriber is broken", StringComparison.Ordinal));
    }

    [Fact]
    public void Execution_report_without_phrase_commands_and_states()
    {
        var capture = new LogCapture();
        using var logger = capture.CreateLogger();
        var log = new SerilogExecutionLog(logger);

        log.ApplyFailed(new ApplyFailureReport(
            "меняй левые и залей тридцать",
            ExecutionOutcome.PartiallyApplied,
            [new SentCommand(PitCommand.LF, 0), new SentCommand(PitCommand.Fuel, 30)],
            ["fuel 30 l"],
            PitServiceState.Empty,
            NothingConfirmed: false));

        var line = Assert.Single(capture.Lines);
        Assert.Contains("partially applied", line, StringComparison.Ordinal);
        Assert.Contains("2 sent, 1 not confirmed", line, StringComparison.Ordinal);
        Assert.DoesNotContain("30", line, StringComparison.Ordinal);
        Assert.DoesNotContain("LF", line, StringComparison.Ordinal);
        Assert.DoesNotContain("меняй", line, StringComparison.Ordinal);
    }

    private sealed class ElevatedGame : IGameElevationProbe
    {
        public GameElevation Check() => GameElevation.Elevated;
    }
}
