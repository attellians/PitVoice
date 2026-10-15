using System.Collections.Concurrent;
using System.Diagnostics;
using PitVoice.Core.Events;
using Xunit;

namespace PitVoice.Core.Tests.Events;

public sealed class EventBusTests
{
    private static readonly DateTimeOffset At = new(2026, 10, 15, 12, 0, 0, TimeSpan.Zero);

    private static PttPressed Event(int id) => new(id, At);

    [Fact]
    public async Task Subscriber_ReceivesEventsInPublishOrder()
    {
        using var bus = new EventBus();
        var received = new ConcurrentQueue<int>();
        bus.Subscribe(e => received.Enqueue(e.PhraseId));

        for (var i = 1; i <= 200; i++)
        {
            bus.Publish(Event(i));
        }

        await bus.DrainAsync();
        Assert.Equal(Enumerable.Range(1, 200), received);
    }

    [Fact]
    public async Task FailingSubscriber_DoesNotStopOthersOrItself()
    {
        var errors = new ConcurrentQueue<Exception>();
        using var bus = new EventBus(errors.Enqueue);
        var good = new ConcurrentQueue<int>();
        var failingCalls = 0;
        bus.Subscribe(_ =>
        {
            Interlocked.Increment(ref failingCalls);
            throw new InvalidOperationException("subscriber broke");
        });
        bus.Subscribe(e => good.Enqueue(e.PhraseId));

        bus.Publish(Event(1));
        bus.Publish(Event(2));
        bus.Publish(Event(3));

        await bus.DrainAsync();
        Assert.Equal([1, 2, 3], good);
        Assert.Equal(3, failingCalls);
        Assert.Equal(3, errors.Count);
    }

    [Fact]
    public async Task SlowSubscriber_DoesNotBlockPublishOrOthers()
    {
        using var bus = new EventBus();
        using var release = new ManualResetEventSlim();
        using var fastDone = new SemaphoreSlim(0);
        bus.Subscribe(_ => release.Wait(TimeSpan.FromSeconds(10)));
        bus.Subscribe(_ => fastDone.Release());

        var watch = Stopwatch.StartNew();
        for (var i = 1; i <= 5; i++)
        {
            bus.Publish(Event(i));
        }

        Assert.True(watch.Elapsed < TimeSpan.FromSeconds(1), "publish waited for the slow subscriber");
        for (var i = 0; i < 5; i++)
        {
            Assert.True(await fastDone.WaitAsync(TimeSpan.FromSeconds(5)), "the fast subscriber waited for the slow one");
        }

        release.Set();
        await bus.DrainAsync();
    }

    [Fact]
    public async Task Unsubscribed_ReceivesNothingAfterwards()
    {
        using var bus = new EventBus();
        var received = new ConcurrentQueue<int>();
        var other = new ConcurrentQueue<int>();
        var subscription = bus.Subscribe(e => received.Enqueue(e.PhraseId));
        bus.Subscribe(e => other.Enqueue(e.PhraseId));

        bus.Publish(Event(1));
        subscription.Dispose();
        bus.Publish(Event(2));

        await bus.DrainAsync();
        Assert.DoesNotContain(2, received);
        Assert.Equal([1, 2], other);
    }
}
