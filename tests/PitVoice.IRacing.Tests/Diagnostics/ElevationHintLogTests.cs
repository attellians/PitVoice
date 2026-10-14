using PitVoice.Core.Pit;
using PitVoice.IRacing.Diagnostics;
using Xunit;

namespace PitVoice.IRacing.Tests.Diagnostics;

public class ElevationHintLogTests
{
    private static ApplyFailureReport Report(ExecutionOutcome outcome = ExecutionOutcome.NotApplied, bool nothingConfirmed = true, bool connected = true) =>
        new("меняй шины", outcome, [new SentCommand(PitCommand.LF, 0)], ["wheels include LF"], connected ? PitServiceState.Empty : null, nothingConfirmed);

    [Fact]
    public void Elevated_game_gets_a_hint()
    {
        var inner = new ListLog();
        var log = new ElevationHintLog(inner, new Probe(() => GameElevation.Elevated));

        log.ApplyFailed(Report());

        Assert.Single(inner.Failures);
        Assert.Equal(ElevationHintLog.Hint, Assert.Single(inner.Warnings));
    }

    [Theory]
    [InlineData(GameElevation.NotElevated)]
    [InlineData(GameElevation.Unknown)]
    public void No_hint_otherwise(GameElevation elevation)
    {
        var inner = new ListLog();

        new ElevationHintLog(inner, new Probe(() => elevation)).ApplyFailed(Report());

        Assert.Empty(inner.Warnings);
    }

    [Fact]
    public void Not_checked_when_something_was_confirmed_or_connection_lost()
    {
        var checks = 0;
        var log = new ElevationHintLog(new ListLog(), new Probe(() =>
        {
            checks++;
            return GameElevation.Elevated;
        }));

        log.ApplyFailed(Report(ExecutionOutcome.PartiallyApplied, nothingConfirmed: false));
        log.ApplyFailed(Report(connected: false));

        Assert.Equal(0, checks);
    }

    [Fact]
    public void Inaccessible_process_does_not_throw()
    {
        var inner = new ListLog();
        var log = new ElevationHintLog(inner, new Probe(() => throw new InvalidOperationException("process exited")));

        log.ApplyFailed(Report());

        Assert.Single(inner.Failures);
        Assert.Empty(inner.Warnings);
    }

    [Fact]
    public void Real_probe_without_game_is_unknown()
    {
        if (OperatingSystem.IsWindows() && !System.Diagnostics.Process.GetProcessesByName("iRacingSim64DX11").Any())
        {
            Assert.Equal(GameElevation.Unknown, new WindowsGameElevationProbe().Check());
        }
    }

    private sealed class Probe(Func<GameElevation> check) : IGameElevationProbe
    {
        public GameElevation Check() => check();
    }

    private sealed class ListLog : IExecutionLog
    {
        public List<ApplyFailureReport> Failures { get; } = [];

        public List<string> Warnings { get; } = [];

        public void ApplyFailed(ApplyFailureReport report) => Failures.Add(report);

        public void Warning(string message) => Warnings.Add(message);
    }
}
