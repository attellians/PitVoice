using PitVoice.Host;

namespace PitVoice.App;

/// <summary>
/// One app per Windows user (design decision 4): the first instance holds <see cref="HostLock.AppName"/> and listens
/// for <see cref="ShowEventName"/>; a second launch sets that event and waits <see cref="AckTimeout"/> for
/// <see cref="AckEventName"/>, which the first instance sets after it showed its window.
/// </summary>
internal sealed class SingleInstance : IDisposable
{
    public const string ShowEventName = @"Local\PitVoice.ShowWindow";
    public const string AckEventName = @"Local\PitVoice.ShowWindowAck";
    public static readonly TimeSpan AckTimeout = TimeSpan.FromSeconds(3);

    private readonly HostLock _lock;
    private readonly EventWaitHandle _show = new(false, EventResetMode.AutoReset, ShowEventName);
    private readonly EventWaitHandle _ack = new(false, EventResetMode.AutoReset, AckEventName);
    private readonly ManualResetEvent _stop = new(false);
    private Thread? _listener;

    private SingleInstance(HostLock appLock)
    {
        _lock = appLock;
    }

    /// <summary>Becomes the first instance; null when another instance runs.</summary>
    public static SingleInstance? TryStart() => HostLock.TryAcquire(HostLock.AppName) is { } appLock ? new SingleInstance(appLock) : null;

    /// <summary>Asks the running instance to show its window; false when it did not answer in time (spec: First instance hung).</summary>
    public static bool AskFirstToShow()
    {
        using var show = new EventWaitHandle(false, EventResetMode.AutoReset, ShowEventName);
        using var ack = new EventWaitHandle(false, EventResetMode.AutoReset, AckEventName);
        ack.Reset();
        show.Set();
        return ack.WaitOne(AckTimeout);
    }

    /// <summary>Calls <paramref name="show"/> for every later launch; it must show the window and return when it is shown.</summary>
    public void Listen(Action show)
    {
        _listener = new Thread(() =>
        {
            var handles = new WaitHandle[] { _stop, _show };
            while (WaitHandle.WaitAny(handles) == 1)
            {
                show();
                _ack.Set();
            }
        })
        {
            IsBackground = true,
            Name = "PitVoice show window",
        };
        _listener.Start();
    }

    public void Dispose()
    {
        _stop.Set();
        _listener?.Join(TimeSpan.FromSeconds(1));
        _show.Dispose();
        _ack.Dispose();
        _stop.Dispose();
        _lock.Dispose();
    }
}
