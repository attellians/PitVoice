using PitVoice.Core.Pit;
using PitVoice.IRacing.Telemetry;
using Xunit;

namespace PitVoice.IRacing.Tests.Telemetry;

public class MemoryLayoutTests
{
    [Fact]
    public void Reads_the_newest_buffer()
    {
        var image = new MemoryImage()
            .Buffer(0, tick: 100, isOnTrack: false, pitSvFlags: 0, pitSvFuel: 0, fuelLevel: 10)
            .Buffer(1, tick: 102, isOnTrack: true, pitSvFlags: 0x05 | 0x10, pitSvFuel: 20, fuelLevel: 41.5f)
            .Buffer(2, tick: 101, isOnTrack: false, pitSvFlags: 0, pitSvFuel: 0, fuelLevel: 10);

        Assert.True(IRacingMemoryLayout.TryRead(image.Bytes, out var raw));

        Assert.Equal(new RawTelemetry(IRacingMemoryLayout.StatusConnected, 102, true, 0x15, 20f, 41.5f), raw);
    }

    [Fact]
    public void Disconnected_header_gives_status_only()
    {
        var image = new MemoryImage(status: 0).Buffer(0, 5, true, 0x0F, 30, 30);

        Assert.True(IRacingMemoryLayout.TryRead(image.Bytes, out var raw));
        Assert.False(raw.IsConnected);
        Assert.Equal(0, raw.PitSvFlags);
    }

    // Lines as the game writes them (Test Drive, 2026-10-14), with a driver block around them.
    private const string SessionYaml = """
        ---
        WeekendInfo:
         TrackName: some track
        DriverInfo:
         DriverCarIdx: 0
         DriverCarFuelKgPerLtr: 0.750
         DriverCarFuelMaxLtr: 96.000
         DriverCarMaxFuelPct: 1.000
         Drivers:
         - CarIdx: 0
           UserName: Jörg Müller
           CarClassMaxFuelPct: 1.000 %
        ...
        """;

    [Fact]
    public void Fuel_capacity_from_session_info()
    {
        var image = new MemoryImage().SessionInfo(SessionYaml, update: 28);

        Assert.True(IRacingMemoryLayout.TryReadFuelCapacity(image.Bytes, out var liters));
        Assert.Equal(96.0, liters, 3);
        Assert.Equal(28, IRacingMemoryLayout.SessionInfoUpdate(image.Bytes));
    }

    [Theory]
    [InlineData("0.900", 86.4)]   // restricted car, written as a fraction
    [InlineData("90.0 %", 86.4)]  // the same written as a percentage
    [InlineData("0.000", 96.0)]   // nonsense restriction is ignored
    public void Fuel_capacity_applies_the_max_fuel_restriction(string pct, double expected)
    {
        var image = new MemoryImage().SessionInfo(SessionYaml.Replace("DriverCarMaxFuelPct: 1.000", $"DriverCarMaxFuelPct: {pct}", StringComparison.Ordinal));

        Assert.True(IRacingMemoryLayout.TryReadFuelCapacity(image.Bytes, out var liters));
        Assert.Equal(expected, liters, 3);
    }

    [Theory]
    [InlineData(" DriverCarFuelMaxLtr: 96.000", "")]
    [InlineData(" DriverCarFuelMaxLtr: 96.000", " DriverCarFuelMaxLtr: n/a")]
    public void Fuel_capacity_missing_or_unreadable(string line, string replacement)
    {
        var image = new MemoryImage().SessionInfo(SessionYaml.Replace(line, replacement, StringComparison.Ordinal));

        Assert.False(IRacingMemoryLayout.TryReadFuelCapacity(image.Bytes, out _));
        Assert.False(IRacingMemoryLayout.TryReadFuelCapacity(new MemoryImage().Bytes, out _));
    }

    [Fact]
    public void Too_small_image_is_unreadable()
    {
        Assert.False(IRacingMemoryLayout.TryRead(new byte[16], out _));
    }

    [Fact]
    public void Variable_headers_outside_the_image_are_unreadable()
    {
        var image = new MemoryImage();
        BitConverter.GetBytes(int.MaxValue / 2).CopyTo(image.Bytes, 28);

        Assert.False(IRacingMemoryLayout.TryRead(image.Bytes, out _));
    }
}

public class ConnectionStateTrackerTests
{
    private static readonly TimeSpan Ms = TimeSpan.FromMilliseconds(1);

    private static RawTelemetry Raw(int tick, bool onTrack = true, int flags = 0x03, float fuel = 20, float level = 40) =>
        new(IRacingMemoryLayout.StatusConnected, tick, onTrack, flags, fuel, level);

    [Fact]
    public void Game_not_running()
    {
        Assert.Equal(TelemetrySnapshot.NotRunning, new ConnectionStateTracker().Evaluate(null, TimeSpan.Zero));
    }

    [Fact]
    public void Connected_bit_missing_means_not_running()
    {
        var raw = Raw(5) with { Status = 0 };

        Assert.Equal(ConnectionState.NotRunning, new ConnectionStateTracker().Evaluate(raw, TimeSpan.Zero).State);
    }

    [Fact]
    public void In_the_garage()
    {
        var snapshot = new ConnectionStateTracker().Evaluate(Raw(1, onTrack: false), TimeSpan.Zero);

        Assert.Equal(ConnectionState.Connected, snapshot.State);
        Assert.NotNull(snapshot.Pit);
    }

    [Fact]
    public void Driving()
    {
        var snapshot = new ConnectionStateTracker().Evaluate(Raw(1, flags: 0x05 | 0x10, fuel: 20, level: 33), TimeSpan.Zero);

        Assert.Equal(ConnectionState.InCar, snapshot.State);
        Assert.Equal(Wheels.LF | Wheels.LR, snapshot.Pit!.Wheels);
        Assert.True(snapshot.Pit.FuelFill);
        Assert.Equal(20, snapshot.Pit.FuelToAdd);
        Assert.Equal(33, snapshot.Pit.FuelLevel);
        Assert.Equal(1, snapshot.Tick);
        Assert.Null(snapshot.FuelCapacity);

        var withTank = new ConnectionStateTracker().Evaluate(Raw(1) with { FuelCapacity = 96 }, TimeSpan.Zero);
        Assert.Equal(96, withTank.FuelCapacity);
    }

    [Fact]
    public void Game_closed_while_connected_and_started_again()
    {
        var tracker = new ConnectionStateTracker();
        Assert.Equal(ConnectionState.InCar, tracker.Evaluate(Raw(10), TimeSpan.Zero).State);

        Assert.Equal(ConnectionState.NotRunning, tracker.Evaluate(null, 100 * Ms).State);
        Assert.Equal(ConnectionState.Connected, tracker.Evaluate(Raw(1, onTrack: false), 5000 * Ms).State);
    }

    [Fact]
    public void Frozen_telemetry_is_lost_connection()
    {
        var tracker = new ConnectionStateTracker();
        tracker.Evaluate(Raw(7), TimeSpan.Zero);

        Assert.Equal(ConnectionState.InCar, tracker.Evaluate(Raw(7), 1000 * Ms).State);
        var frozen = tracker.Evaluate(Raw(7), 1500 * Ms);

        Assert.Equal(TelemetrySnapshot.NotRunning, frozen);
    }

    [Fact]
    public void Telemetry_resumes()
    {
        var tracker = new ConnectionStateTracker();
        tracker.Evaluate(Raw(7), TimeSpan.Zero);
        Assert.Equal(ConnectionState.NotRunning, tracker.Evaluate(Raw(7), 1500 * Ms).State);

        Assert.Equal(ConnectionState.Connected, tracker.Evaluate(Raw(8, onTrack: false), 1516 * Ms).State);
    }
}
