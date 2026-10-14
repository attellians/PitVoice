namespace PitVoice.Core.Pit;

/// <summary>Connection to the game.</summary>
public enum ConnectionState
{
    /// <summary>No SDK data, the game is not connected, or the telemetry is stale.</summary>
    NotRunning,

    /// <summary>The game runs, the driver is not in the car (menu, garage).</summary>
    Connected,

    /// <summary>The driver is in the car; pit commands are accepted.</summary>
    InCar,
}

/// <summary>Latest telemetry the application knows about.</summary>
/// <param name="State">Connection state.</param>
/// <param name="Pit">Black box state, or <c>null</c> when there is no data (the game is not running).</param>
/// <param name="Tick">The game's update counter (<c>tickCount</c>), 0 without data.</param>
public sealed record TelemetrySnapshot(ConnectionState State, PitServiceState? Pit, int Tick)
{
    public static TelemetrySnapshot NotRunning { get; } = new(ConnectionState.NotRunning, null, 0);

    /// <summary>
    /// Usable fuel tank of the car in liters (session info), or <c>null</c> when unknown. The game caps a fuel request
    /// to this amount.
    /// </summary>
    public double? FuelCapacity { get; init; }
}

/// <summary>Source of telemetry snapshots: the game, a fake or a replayed recording. Polled by the caller.</summary>
public interface ITelemetrySource
{
    /// <summary>The most recent snapshot. Never blocks for long and never throws because the game is missing.</summary>
    TelemetrySnapshot Current { get; }
}

/// <summary>Delivers pit commands to the game.</summary>
public interface IPitCommandSink
{
    void Send(PitCommand command, int parameter);
}
