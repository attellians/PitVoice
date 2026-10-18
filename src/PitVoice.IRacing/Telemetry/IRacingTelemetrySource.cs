using System.IO.MemoryMappedFiles;
using System.Runtime.Versioning;
using PitVoice.Core.Pit;

namespace PitVoice.IRacing.Telemetry;

/// <summary>
/// Telemetry of the running game, read from the SDK shared memory on every request. Survives the game being absent,
/// closed and started again: the memory is reopened when the game is not seen.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed unsafe class IRacingTelemetrySource : ITelemetrySource, IDisposable
{
    /// <summary>How often to try opening the shared memory while the game is not running.</summary>
    public static readonly TimeSpan ReopenInterval = TimeSpan.FromMilliseconds(250);

    private readonly Lock _lock = new();
    private readonly TimeProvider _time;
    private readonly long _start;
    private readonly ConnectionStateTracker _tracker = new();
    private MemoryMappedFile? _file;
    private MemoryMappedViewAccessor? _view;
    private byte* _pointer;
    private int _length;
    private TimeSpan? _lastOpenAttempt;
    private int _sessionInfoUpdate = -1;
    private double? _fuelCapacity;
    private IReadOnlyList<TireCompound>? _tireCompounds;
    private bool _disposed;

    public IRacingTelemetrySource(TimeProvider? time = null)
    {
        _time = time ?? TimeProvider.System;
        _start = _time.GetTimestamp();
    }

    public TelemetrySnapshot Current
    {
        get
        {
            lock (_lock)
            {
                ObjectDisposedException.ThrowIf(_disposed, this);
                var now = _time.GetElapsedTime(_start);
                RawTelemetry? raw = null;
                if (EnsureOpen(now))
                {
                    var memory = new ReadOnlySpan<byte>(_pointer, _length);
                    if (IRacingMemoryLayout.TryRead(memory, out var value))
                    {
                        ReadSessionInfo(memory);
                        raw = value with { FuelCapacity = _fuelCapacity, TireCompounds = _tireCompounds };
                    }
                }

                if (raw is not { IsConnected: true })
                {
                    // The game left: let go of the memory so a restarted game is picked up afresh. Frozen telemetry
                    // keeps the memory open; the tracker reports it as not running until the tick moves again.
                    Close();
                }

                return _tracker.Evaluate(raw, now);
            }
        }
    }

    public void Dispose()
    {
        lock (_lock)
        {
            Close();
            _disposed = true;
        }
    }

    /// <summary>Tank capacity and tire list from session info, parsed again only when the session info changes.</summary>
    private void ReadSessionInfo(ReadOnlySpan<byte> memory)
    {
        var update = IRacingMemoryLayout.SessionInfoUpdate(memory);
        if (update != _sessionInfoUpdate)
        {
            _sessionInfoUpdate = update;
            _fuelCapacity = IRacingMemoryLayout.TryReadFuelCapacity(memory, out var liters) ? liters : null;
            _tireCompounds = IRacingMemoryLayout.ReadTireCompounds(memory);
        }
    }

    private bool EnsureOpen(TimeSpan now)
    {
        if (_view is not null)
        {
            return true;
        }

        if (_lastOpenAttempt is { } last && now - last < ReopenInterval)
        {
            return false;
        }

        _lastOpenAttempt = now;
        try
        {
            _file = MemoryMappedFile.OpenExisting(IRacingMemoryLayout.MapName, MemoryMappedFileRights.Read);
            _view = _file.CreateViewAccessor(0, 0, MemoryMappedFileAccess.Read);
            byte* pointer = null;
            _view.SafeMemoryMappedViewHandle.AcquirePointer(ref pointer);
            _pointer = pointer + _view.PointerOffset;
            _length = (int)Math.Min(_view.Capacity, int.MaxValue);
            return true;
        }
        catch (Exception e) when (e is FileNotFoundException or IOException or UnauthorizedAccessException)
        {
            Close();
            return false;
        }
    }

    private void Close()
    {
        if (_view is not null)
        {
            if (_pointer is not null)
            {
                _view.SafeMemoryMappedViewHandle.ReleasePointer();
            }

            _view.Dispose();
        }

        _file?.Dispose();
        _view = null;
        _file = null;
        _pointer = null;
        _length = 0;
        _sessionInfoUpdate = -1;
        _fuelCapacity = null;
        _tireCompounds = null;
    }
}
