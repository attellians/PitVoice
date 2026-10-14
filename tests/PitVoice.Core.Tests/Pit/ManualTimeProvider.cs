namespace PitVoice.Core.Tests.Pit;

/// <summary>
/// Virtual clock. Every timer jumps the clock forward by its due time and fires at once, so code that waits with
/// <c>Task.Delay(…, timeProvider)</c> runs through its timeouts without real waiting.
/// </summary>
internal sealed class ManualTimeProvider : TimeProvider
{
    private static readonly DateTimeOffset Start = new(2026, 10, 14, 12, 0, 0, TimeSpan.Zero);
    private long _ticks;

    public TimeSpan Elapsed => TimeSpan.FromTicks(Interlocked.Read(ref _ticks));

    public override long TimestampFrequency => TimeSpan.TicksPerSecond;

    public override long GetTimestamp() => Interlocked.Read(ref _ticks);

    public override DateTimeOffset GetUtcNow() => Start + Elapsed;

    public void Advance(TimeSpan delta) => Interlocked.Add(ref _ticks, delta.Ticks);

    public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
    {
        if (dueTime != Timeout.InfiniteTimeSpan)
        {
            Advance(dueTime);
            ThreadPool.UnsafeQueueUserWorkItem(_ => callback(state), null);
        }

        return new NoTimer();
    }

    private sealed class NoTimer : ITimer
    {
        public bool Change(TimeSpan dueTime, TimeSpan period) => true;

        public void Dispose()
        {
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
