using System.Text.Json;
using PitVoice.Core.Pit;
using PitVoice.Core.Tests.Pit;
using PitVoice.IRacing.Recording;
using Xunit;

namespace PitVoice.IRacing.Tests.Recording;

public class RecordingTests
{
    /// <summary>A scripted game: garage, getting in the car, picking left tires and 20 l, then closing.</summary>
    private sealed class ScriptedGame(TimeProvider time, CancellationTokenSource stopAt2s) : ITelemetrySource
    {
        private readonly long _start = time.GetTimestamp();
        private int _tick;

        public TelemetrySnapshot Current
        {
            get
            {
                var t = time.GetElapsedTime(_start).TotalMilliseconds;
                if (t >= 2000)
                {
                    stopAt2s.Cancel();
                }

                _tick++;
                return t switch
                {
                    < 200 => TelemetrySnapshot.NotRunning,
                    < 500 => new TelemetrySnapshot(ConnectionState.Connected, new PitServiceState(PitServiceFlags.None, 0, 50.25), _tick),
                    < 900 => new TelemetrySnapshot(ConnectionState.InCar, new PitServiceState(PitServiceFlags.None, 0, 50.25 - (t / 10000)), _tick),
                    < 1600 => new TelemetrySnapshot(ConnectionState.InCar, new PitServiceState(PitServiceFlags.LFTireChange | PitServiceFlags.LRTireChange | PitServiceFlags.FuelFill, 20, 50.1f), _tick),
                    _ => TelemetrySnapshot.NotRunning,
                };
            }
        }
    }

    private static async Task<(string Text, ScriptedGame Game)> RecordScript()
    {
        var time = new ManualTimeProvider();
        using var stop = new CancellationTokenSource();
        var game = new ScriptedGame(time, stop);
        var writer = new StringWriter();
        await new TelemetryRecorder(writer).RunAsync(game, time, cancellationToken: stop.Token);
        return (writer.ToString(), game);
    }

    [Fact]
    public async Task Record_and_replay_give_the_same_snapshots()
    {
        var (text, _) = await RecordScript();
        var records = TelemetryRecordFormat.Read(new StringReader(text), "memory");

        Assert.True(records.Count >= 20, $"only {records.Count} records in 2 s");
        var time = new ManualTimeProvider();
        var replay = new ReplayTelemetrySource(records, time);

        foreach (var (at, snapshot) in records)
        {
            Assert.Equal(snapshot, replay.At(at));
        }

        // Record and replay: after the selection the replay shows LF and LR.
        time.Advance(TimeSpan.FromMilliseconds(1000));
        Assert.Equal(Wheels.LF | Wheels.LR, replay.Current.Pit!.Wheels);
        time.Advance(TimeSpan.FromMilliseconds(800));
        Assert.Equal(TelemetrySnapshot.NotRunning, replay.Current);
    }

    [Fact]
    public void Recording_is_written_at_10_hz_and_on_every_change()
    {
        var writer = new StringWriter();
        var recorder = new TelemetryRecorder(writer);
        var calm = new TelemetrySnapshot(ConnectionState.InCar, PitServiceState.Empty, 1);
        var picked = new TelemetrySnapshot(ConnectionState.InCar, PitServiceState.Empty.WithWheels(Wheels.LF), 2);

        Assert.True(recorder.Sample(calm, TimeSpan.Zero));
        Assert.False(recorder.Sample(calm, TimeSpan.FromMilliseconds(50)));
        Assert.True(recorder.Sample(picked, TimeSpan.FromMilliseconds(60)));          // flags changed
        Assert.False(recorder.Sample(picked, TimeSpan.FromMilliseconds(150)));
        Assert.True(recorder.Sample(picked, TimeSpan.FromMilliseconds(160)));         // 100 ms since the last line
        Assert.True(recorder.Sample(TelemetrySnapshot.NotRunning, TimeSpan.FromMilliseconds(170))); // state changed
        Assert.Equal(4, recorder.Written);
    }

    [Fact]
    public void Recording_without_game_is_valid()
    {
        var line = TelemetryRecordFormat.ToLine(new RecordedSnapshot(TimeSpan.FromMilliseconds(42), TelemetrySnapshot.NotRunning));

        Assert.Equal("""{"t":42,"state":"NotRunning","tick":0,"pitSvFlags":null,"pitSvFuel":null,"fuelLevel":null,"fuelCapacity":null}""", line);
        Assert.Equal(TelemetrySnapshot.NotRunning, TelemetryRecordFormat.FromLine(line, "x", 1).Snapshot);
    }

    [Fact]
    public void Tank_capacity_round_trips_and_old_recordings_without_it_still_read()
    {
        var snapshot = new TelemetrySnapshot(ConnectionState.InCar, new PitServiceState(PitServiceFlags.FuelFill, 96, 61.5), 7) { FuelCapacity = 96 };
        var line = TelemetryRecordFormat.ToLine(new RecordedSnapshot(TimeSpan.FromMilliseconds(5), snapshot));

        Assert.Equal(snapshot, TelemetryRecordFormat.FromLine(line, "x", 1).Snapshot);

        var old = TelemetryRecordFormat.FromLine("""{"t":5,"state":"InCar","tick":7,"pitSvFlags":16,"pitSvFuel":96,"fuelLevel":61.5}""", "old", 1);
        Assert.Null(old.Snapshot.FuelCapacity);
        Assert.Equal(96, old.Snapshot.Pit!.FuelToAdd);
    }

    [Fact]
    public async Task Every_line_has_exactly_the_documented_keys()
    {
        var (text, _) = await RecordScript();

        foreach (var line in text.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            using var json = JsonDocument.Parse(line);
            var keys = json.RootElement.EnumerateObject().Select(p => p.Name).ToArray();
            Assert.Equal(TelemetryRecordFormat.Keys, keys);
        }
    }

    [Fact]
    public void Truncated_recording_names_the_damaged_line()
    {
        var good = TelemetryRecordFormat.ToLine(new RecordedSnapshot(TimeSpan.Zero, TelemetrySnapshot.NotRunning));
        var text = good + "\n" + good + "\n" + good[..(good.Length / 2)];

        var error = Assert.Throws<InvalidDataException>(() => TelemetryRecordFormat.Read(new StringReader(text), "cut.jsonl"));

        Assert.StartsWith("cut.jsonl:3:", error.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("""{"t":1,"state":"InCar","tick":1,"pitSvFlags":0,"pitSvFuel":0}""")]                                   // key missing
    [InlineData("""{"t":1,"state":"InCar","tick":1,"pitSvFlags":0,"pitSvFuel":0,"fuelLevel":0,"driver":"x"}""")]      // extra data
    [InlineData("""{"t":1,"state":"Flying","tick":1,"pitSvFlags":0,"pitSvFuel":0,"fuelLevel":0}""")]                   // unknown state
    public void Unexpected_records_are_rejected(string line)
    {
        Assert.Throws<InvalidDataException>(() => TelemetryRecordFormat.FromLine(line, "x", 7));
    }
}
