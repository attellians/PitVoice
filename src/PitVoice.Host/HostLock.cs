namespace PitVoice.Host;

/// <summary>
/// A named mutex held for the whole life of a host (design decision 4). A mutex belongs to the thread that took it, and
/// hosts stop on whatever thread runs their disposal, so a dedicated thread takes, holds and releases it. A mutex
/// abandoned by a crashed process counts as taken successfully (spec: Host crashed).
/// </summary>
public sealed class HostLock : IDisposable
{
    /// <summary>Held by the tray app and by <c>pitvoice-cli run</c>: one pipeline host per Windows user.</summary>
    public const string PipelineHostName = @"Local\PitVoice.Host";

    /// <summary>Held only by the tray app: one app instance per Windows user.</summary>
    public const string AppName = @"Local\PitVoice.App";

    private readonly ManualResetEventSlim _release = new();
    private readonly Thread _thread;
    private int _disposed;

    private HostLock(Thread thread)
    {
        _thread = thread;
    }

    /// <summary>Takes the mutex <paramref name="name"/>; null when another holder has it.</summary>
    public static HostLock? TryAcquire(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        var acquired = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        HostLock? holder = null;
        var thread = new Thread(() =>
        {
            using var mutex = new Mutex(initiallyOwned: false, name);
            bool owned;
            try
            {
                owned = mutex.WaitOne(0);
            }
            catch (AbandonedMutexException)
            {
                owned = true;
            }

            acquired.SetResult(owned);
            if (!owned)
            {
                return;
            }

            holder!._release.Wait();
            mutex.ReleaseMutex();
        })
        {
            IsBackground = true,
            Name = $"PitVoice lock {name}",
        };

        holder = new HostLock(thread);
        thread.Start();
        if (acquired.Task.GetAwaiter().GetResult())
        {
            return holder;
        }

        thread.Join();
        holder._release.Dispose();
        return null;
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        _release.Set();
        _thread.Join();
        _release.Dispose();
    }
}
