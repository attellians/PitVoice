namespace PitVoice.Core.Ptt;

/// <summary>Why a recording stopped.</summary>
public enum RecordingStopReason
{
    /// <summary>Hold mode: the button was released.</summary>
    Released,

    /// <summary>Toggle mode: the button was pressed again.</summary>
    SecondPress,

    /// <summary>Toggle mode: no second press within <see cref="PttStateMachine.ToggleAutoStop"/>.</summary>
    AutoStop,

    /// <summary>The bound device was disconnected during the recording.</summary>
    DeviceLost,
}

/// <summary>What the PTT button did to the recording.</summary>
public abstract record PttTransition
{
    private PttTransition()
    {
    }

    /// <summary>Start recording.</summary>
    public sealed record Started : PttTransition;

    /// <summary>Stop recording and pass it on to recognition.</summary>
    public sealed record Stopped(RecordingStopReason Reason) : PttTransition;

    /// <summary>Hold mode: the press was shorter than <see cref="PttStateMachine.MinHold"/>; drop the recording.</summary>
    public sealed record Discarded : PttTransition;
}

/// <summary>
/// Turns raw edges of the bound control into recording starts and stops (FR-PTT-4/7, design decision 4).
/// Hold: press starts, release stops, presses shorter than <see cref="MinHold"/> are dropped. Toggle: press starts, the
/// next press stops (ignored within <see cref="ToggleDebounce"/>), releases do nothing, and the recording stops by itself
/// after <see cref="ToggleAutoStop"/>. Repeated presses without a release (keyboard auto-repeat) and other controls are
/// ignored. Losing the bound device stops the recording like a release. Thread-safe; <see cref="Transition"/> is raised
/// under a lock, so handlers see transitions in order and must not block for long.
/// </summary>
public sealed class PttStateMachine : IDisposable
{
    public static readonly TimeSpan MinHold = TimeSpan.FromMilliseconds(250);
    public static readonly TimeSpan ToggleDebounce = TimeSpan.FromMilliseconds(300);
    public static readonly TimeSpan ToggleAutoStop = TimeSpan.FromSeconds(8);

    private readonly TimeProvider _time;
    private readonly Lock _lock = new();
    private bool _keyDown;
    private bool _recording;
    private long _startedAt;
    private int _recordingNumber;
    private ITimer? _autoStop;

    public PttStateMachine(PttBinding binding, PttMode mode, TimeProvider? time = null)
    {
        ArgumentNullException.ThrowIfNull(binding);
        Binding = binding;
        Mode = mode;
        _time = time ?? TimeProvider.System;
    }

    public event Action<PttTransition>? Transition;

    public PttBinding Binding { get; }

    public PttMode Mode { get; }

    public bool IsRecording
    {
        get
        {
            lock (_lock)
            {
                return _recording;
            }
        }
    }

    public void OnInput(PttInput input)
    {
        ArgumentNullException.ThrowIfNull(input);
        if (!Binding.Matches(input.Key))
        {
            return;
        }

        lock (_lock)
        {
            if (input.Down)
            {
                if (_keyDown)
                {
                    return; // auto-repeat
                }

                _keyDown = true;
                OnPress();
            }
            else
            {
                if (!_keyDown)
                {
                    return;
                }

                _keyDown = false;
                if (Mode == PttMode.Hold && _recording)
                {
                    StopHold(RecordingStopReason.Released);
                }
            }
        }
    }

    public void OnDeviceChanged(PttDeviceChange change)
    {
        ArgumentNullException.ThrowIfNull(change);
        if (change.Connected || !Binding.IsDevice(change.Device))
        {
            return;
        }

        lock (_lock)
        {
            _keyDown = false;
            if (!_recording)
            {
                return;
            }

            if (Mode == PttMode.Hold)
            {
                StopHold(RecordingStopReason.DeviceLost);
            }
            else
            {
                Stop(RecordingStopReason.DeviceLost);
            }
        }
    }

    public void Dispose()
    {
        lock (_lock)
        {
            _autoStop?.Dispose();
            _autoStop = null;
        }
    }

    private void OnPress()
    {
        if (!_recording)
        {
            _recording = true;
            _startedAt = _time.GetTimestamp();
            _recordingNumber++;
            if (Mode == PttMode.Toggle)
            {
                var number = _recordingNumber;
                _autoStop = _time.CreateTimer(_ => OnAutoStop(number), null, ToggleAutoStop, Timeout.InfiniteTimeSpan);
            }

            Transition?.Invoke(new PttTransition.Started());
            return;
        }

        if (Mode == PttMode.Toggle && _time.GetElapsedTime(_startedAt) >= ToggleDebounce)
        {
            Stop(RecordingStopReason.SecondPress);
        }
    }

    private void OnAutoStop(int number)
    {
        lock (_lock)
        {
            if (_recording && _recordingNumber == number)
            {
                Stop(RecordingStopReason.AutoStop);
            }
        }
    }

    private void StopHold(RecordingStopReason reason)
    {
        if (_time.GetElapsedTime(_startedAt) < MinHold)
        {
            _recording = false;
            Transition?.Invoke(new PttTransition.Discarded());
            return;
        }

        Stop(reason);
    }

    private void Stop(RecordingStopReason reason)
    {
        _recording = false;
        _autoStop?.Dispose();
        _autoStop = null;
        Transition?.Invoke(new PttTransition.Stopped(reason));
    }
}
