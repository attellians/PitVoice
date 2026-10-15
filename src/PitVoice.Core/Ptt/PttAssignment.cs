namespace PitVoice.Core.Ptt;

/// <summary>How a button assignment ended.</summary>
public enum PttAssignmentStatus
{
    Assigned,

    /// <summary>Esc was pressed.</summary>
    Cancelled,

    /// <summary>Nothing was pressed within <see cref="PttAssignment.Timeout"/>.</summary>
    TimedOut,
}

/// <summary>Result of <see cref="PttAssignment"/>; <see cref="Binding"/> is set only when assigned.</summary>
public sealed record PttAssignmentResult(PttAssignmentStatus Status, PttBinding? Binding);

/// <summary>
/// Assignment mode (design decision 7): the first press of any game device button or any key except Esc becomes the
/// binding; Esc cancels; after <see cref="Timeout"/> the assignment ends with nothing changed. Feed it the edges of all
/// sources; the first result wins and later edges are ignored.
/// </summary>
public sealed class PttAssignment : IDisposable
{
    public static readonly TimeSpan Timeout = TimeSpan.FromSeconds(15);

    /// <summary>Virtual-key code of Esc.</summary>
    public const int EscapeVirtualKey = 0x1B;

    private readonly TaskCompletionSource<PttAssignmentResult> _result = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly ITimer _timer;

    public PttAssignment(TimeProvider? time = null)
    {
        _timer = (time ?? TimeProvider.System).CreateTimer(
            _ => _result.TrySetResult(new PttAssignmentResult(PttAssignmentStatus.TimedOut, null)),
            null,
            Timeout,
            System.Threading.Timeout.InfiniteTimeSpan);
    }

    public Task<PttAssignmentResult> Result => _result.Task;

    public void OnInput(PttInput input)
    {
        ArgumentNullException.ThrowIfNull(input);
        if (!input.Down)
        {
            return;
        }

        var result = input.Key is { Kind: PttDeviceKind.Keyboard, Code: EscapeVirtualKey }
            ? new PttAssignmentResult(PttAssignmentStatus.Cancelled, null)
            : new PttAssignmentResult(PttAssignmentStatus.Assigned, PttBinding.From(input));
        if (_result.TrySetResult(result))
        {
            _timer.Dispose();
        }
    }

    public void Dispose() => _timer.Dispose();
}
