namespace PitVoice.Core.Tests.Ptt;

/// <summary>
/// Virtual clock that moves only on <see cref="Advance"/>; timers that come due fire synchronously inside it, in due
/// order. Unlike <c>ManualTimeProvider</c>, a timer never moves the clock by itself, so tests decide when 8 s pass.
/// </summary>
internal sealed class SteppedTimeProvider : TimeProvider
{
    private static readonly DateTimeOffset Start = new(2026, 10, 15, 12, 0, 0, TimeSpan.Zero);
    private readonly Lock _lock = new();
    private readonly List<SteppedTimer> _timers = [];
    private long _ticks;

    public override long TimestampFrequency => TimeSpan.TicksPerSecond;

    public override long GetTimestamp()
    {
        lock (_lock)
        {
            return _ticks;
        }
    }

    public override DateTimeOffset GetUtcNow() => Start + TimeSpan.FromTicks(GetTimestamp());

    public void Advance(TimeSpan delta)
    {
        long target;
        lock (_lock)
        {
            target = _ticks + delta.Ticks;
        }

        while (true)
        {
            SteppedTimer? next;
            lock (_lock)
            {
                next = _timers.Where(t => t.DueAt <= target).MinBy(t => t.DueAt);
                if (next is null)
                {
                    _ticks = target;
                    return;
                }

                _ticks = next.DueAt;
                _timers.Remove(next);
            }

            next.Fire();
        }
    }

    public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
    {
        var timer = new SteppedTimer(this, callback, state);
        timer.Change(dueTime, period);
        return timer;
    }

    private sealed class SteppedTimer(SteppedTimeProvider owner, TimerCallback callback, object? state) : ITimer
    {
        public long DueAt { get; private set; }

        public void Fire() => callback(state);

        public bool Change(TimeSpan dueTime, TimeSpan period)
        {
            lock (owner._lock)
            {
                owner._timers.Remove(this);
                if (dueTime != Timeout.InfiniteTimeSpan)
                {
                    DueAt = owner._ticks + dueTime.Ticks;
                    owner._timers.Add(this);
                }
            }

            return true;
        }

        public void Dispose() => Change(Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);

        public ValueTask DisposeAsync()
        {
            Dispose();
            return ValueTask.CompletedTask;
        }
    }
}
