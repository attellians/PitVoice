using PitVoice.Core.Pit;

namespace PitVoice.IRacing.Telemetry;

/// <summary>
/// Turns raw SDK values into a snapshot (design decision 9): no memory or no connected bit means "not running",
/// a tick counter that has not changed for more than <see cref="StaleAfter"/> means "not running" too, otherwise
/// <c>IsOnTrack</c> tells "in car" from "connected". Pure apart from remembering the last tick.
/// </summary>
public sealed class ConnectionStateTracker
{
    public static readonly TimeSpan StaleAfter = TimeSpan.FromSeconds(1);

    private int? _lastTick;
    private TimeSpan _lastTickChange;

    /// <param name="raw">Values read from the SDK memory, or <c>null</c> when there is no memory or it is unreadable.</param>
    /// <param name="now">Monotonic time of the reading.</param>
    public TelemetrySnapshot Evaluate(RawTelemetry? raw, TimeSpan now)
    {
        if (raw is not { IsConnected: true } value)
        {
            _lastTick = null;
            return TelemetrySnapshot.NotRunning;
        }

        if (_lastTick != value.Tick)
        {
            _lastTick = value.Tick;
            _lastTickChange = now;
        }
        else if (now - _lastTickChange > StaleAfter)
        {
            return TelemetrySnapshot.NotRunning;
        }

        var state = value.IsOnTrack ? ConnectionState.InCar : ConnectionState.Connected;
        var pit = new PitServiceState((PitServiceFlags)value.PitSvFlags, value.PitSvFuel, value.FuelLevel) { TireCompound = value.PitSvTireCompound };
        return new TelemetrySnapshot(state, pit, value.Tick) { FuelCapacity = value.FuelCapacity, TireCompounds = value.TireCompounds };
    }
}
