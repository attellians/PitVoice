using PitVoice.Core.Pit;
using Xunit;

namespace PitVoice.Core.Tests.Pit;

public class FakeGameTests
{
    [Fact]
    public void Command_shows_in_telemetry_after_the_delay()
    {
        var time = new ManualTimeProvider();
        var game = new FakeTelemetrySource(time) { Delay = TimeSpan.FromMilliseconds(100) };
        var sink = new RecordingPitCommandSink(game);

        sink.Send(PitCommand.LF, 0);

        Assert.Equal(Wheels.None, game.Current.Pit!.Wheels);
        time.Advance(TimeSpan.FromMilliseconds(99));
        Assert.Equal(Wheels.None, game.Current.Pit!.Wheels);
        time.Advance(TimeSpan.FromMilliseconds(1));
        Assert.Equal(Wheels.LF, game.Current.Pit!.Wheels);
        Assert.Equal([new SentCommand(PitCommand.LF, 0)], sink.Sent);
    }

    [Fact]
    public void Ignored_command_never_shows()
    {
        var time = new ManualTimeProvider();
        var game = new FakeTelemetrySource(time);
        game.Ignored.Add(PitCommand.FR);

        game.Receive(PitCommand.FR, 0);
        time.Advance(TimeSpan.FromSeconds(5));

        Assert.False(game.Current.Pit!.FastRepair);
    }

    [Fact]
    public void Closed_game_reports_no_data()
    {
        var time = new ManualTimeProvider();
        var game = new FakeTelemetrySource(time) { CloseAt = TimeSpan.FromSeconds(1) };

        Assert.Equal(ConnectionState.InCar, game.Current.State);
        time.Advance(TimeSpan.FromSeconds(1));

        Assert.Equal(TelemetrySnapshot.NotRunning, game.Current);
    }
}
