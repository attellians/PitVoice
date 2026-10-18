using PitVoice.Core.Pit;

namespace PitVoice.Core.Tests.Pit;

/// <summary>
/// A fake game: keeps a black box state, applies received commands after <see cref="Delay"/> of virtual time, can
/// ignore commands, cap fuel to the tank and close in the middle of a test.
/// </summary>
internal sealed class FakeTelemetrySource : ITelemetrySource
{
    private readonly TimeProvider _time;
    private readonly long _start;
    private readonly List<(TimeSpan At, PitCommand Command, int Parameter)> _pending = [];
    private readonly Dictionary<TimeSpan, PitServiceFlags> _reverts = [];
    private int _tick;

    public FakeTelemetrySource(TimeProvider time)
    {
        _time = time;
        _start = time.GetTimestamp();
    }

    public ConnectionState State { get; set; } = ConnectionState.InCar;

    public PitServiceState Pit { get; set; } = PitServiceState.Empty;

    /// <summary>How long the game takes to show a received command in telemetry.</summary>
    public TimeSpan Delay { get; set; } = TimeSpan.FromMilliseconds(50);

    /// <summary>Commands the game accepts but never applies (e.g. FR without fast repairs left).</summary>
    public HashSet<PitCommand> Ignored { get; } = [];

    /// <summary>
    /// Commands the game shows for <see cref="FlashDuration"/> and then drops, as iRacing does with fast repair when none
    /// are left.
    /// </summary>
    public HashSet<PitCommand> Flashing { get; } = [];

    public TimeSpan FlashDuration { get; set; } = TimeSpan.FromMilliseconds(31);

    /// <summary>
    /// Usable tank in liters. When set, the snapshot reports it and a fuel request above it is reduced to it, as
    /// iRacing does (the request is capped to the tank, not to the free volume).
    /// </summary>
    public double? TankCapacity { get; set; }

    /// <summary>The car's tire list as session info gives it; null when the game gave none.</summary>
    public IReadOnlyList<TireCompound>? TireCompounds { get; set; }

    /// <summary>When set, the game closes at this point of virtual time.</summary>
    public TimeSpan? CloseAt { get; set; }

    public TimeSpan Elapsed => _time.GetElapsedTime(_start);

    public TelemetrySnapshot Current
    {
        get
        {
            lock (_pending)
            {
                var now = Elapsed;
                if (CloseAt is { } close && now >= close)
                {
                    State = ConnectionState.NotRunning;
                    _pending.Clear();
                }

                if (State == ConnectionState.NotRunning)
                {
                    return TelemetrySnapshot.NotRunning;
                }

                while (_pending.Count > 0 && _pending[0].At <= now)
                {
                    var (at, command, parameter) = _pending[0];
                    _pending.RemoveAt(0);
                    if (_reverts.Remove(at, out var flags))
                    {
                        Pit = Pit with { Flags = flags };
                        continue;
                    }

                    if (Ignored.Contains(command))
                    {
                        continue;
                    }

                    if (Flashing.Contains(command))
                    {
                        var revertAt = at + FlashDuration;
                        _reverts[revertAt] = Pit.Flags;
                        var index = _pending.FindLastIndex(p => p.At <= revertAt) + 1;
                        _pending.Insert(index, (revertAt, command, parameter));
                    }

                    Pit = Apply(Pit, command, parameter);
                }

                return new TelemetrySnapshot(State, Pit, ++_tick) { FuelCapacity = TankCapacity, TireCompounds = TireCompounds };
            }
        }
    }

    public void Receive(PitCommand command, int parameter)
    {
        lock (_pending)
        {
            _pending.Add((Elapsed + Delay, command, parameter));
        }
    }

    private PitServiceState Apply(PitServiceState pit, PitCommand command, int parameter) => command switch
    {
        PitCommand.Clear => pit with { Flags = PitServiceFlags.None },
        PitCommand.WS => pit.With(PitServiceFlags.WindshieldTearoff, true),
        PitCommand.ClearWS => pit.With(PitServiceFlags.WindshieldTearoff, false),
        PitCommand.FR => pit.With(PitServiceFlags.FastRepair, true),
        PitCommand.ClearFR => pit.With(PitServiceFlags.FastRepair, false),
        PitCommand.ClearFuel => pit.With(PitServiceFlags.FuelFill, false),
        PitCommand.Fuel => pit.With(PitServiceFlags.FuelFill, true) with
        {
            FuelToAdd = parameter == 0 ? pit.FuelToAdd : Math.Min(parameter, TankCapacity ?? parameter),
        },
        PitCommand.LF => pit.WithWheels(pit.Wheels | Wheels.LF),
        PitCommand.RF => pit.WithWheels(pit.Wheels | Wheels.RF),
        PitCommand.LR => pit.WithWheels(pit.Wheels | Wheels.LR),
        PitCommand.RR => pit.WithWheels(pit.Wheels | Wheels.RR),
        PitCommand.ClearTires => pit.WithWheels(Wheels.None),
        PitCommand.TC => pit with { TireCompound = parameter },
        _ => pit,
    };
}

/// <summary>Remembers what was sent and forwards it to the fake game, if any.</summary>
internal sealed class RecordingPitCommandSink(FakeTelemetrySource? game = null) : IPitCommandSink
{
    public List<SentCommand> Sent { get; } = [];

    public void Send(PitCommand command, int parameter)
    {
        Sent.Add(new SentCommand(command, parameter));
        game?.Receive(command, parameter);
    }
}
