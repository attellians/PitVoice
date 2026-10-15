using System.Threading.Channels;

namespace PitVoice.Core.Events;

/// <summary>Fan-out of pipeline events to logs, the console and later the UI, overlay and local API (TZ §7).</summary>
public interface IEventBus
{
    /// <summary>Queues the event for every subscriber and returns at once.</summary>
    void Publish(PipelineEvent pipelineEvent);

    /// <summary>Starts delivering events to <paramref name="handler"/>; dispose the result to stop.</summary>
    IDisposable Subscribe(Action<PipelineEvent> handler);
}

/// <summary>
/// Every subscriber has its own queue and reader task (design decision 8): <see cref="Publish"/> never waits for a
/// subscriber, a slow subscriber delays only itself, and an exception in one subscriber is reported to
/// <c>onSubscriberError</c> and does not stop delivery. Each subscriber sees events in publish order.
/// </summary>
public sealed class EventBus : IEventBus, IDisposable
{
    private readonly Lock _lock = new();
    private readonly List<Subscription> _subscriptions = [];
    private readonly Action<Exception>? _onSubscriberError;

    public EventBus(Action<Exception>? onSubscriberError = null)
    {
        _onSubscriberError = onSubscriberError;
    }

    public void Publish(PipelineEvent pipelineEvent)
    {
        ArgumentNullException.ThrowIfNull(pipelineEvent);
        lock (_lock)
        {
            foreach (var subscription in _subscriptions)
            {
                subscription.Queue.Writer.TryWrite(pipelineEvent);
            }
        }
    }

    public IDisposable Subscribe(Action<PipelineEvent> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        var subscription = new Subscription(this, handler);
        lock (_lock)
        {
            _subscriptions.Add(subscription);
        }

        subscription.Reader = Task.Run(subscription.RunAsync);
        return subscription;
    }

    /// <summary>Stops accepting events and waits until every subscriber has handled what was already published.</summary>
    public async Task DrainAsync()
    {
        Subscription[] subscriptions;
        lock (_lock)
        {
            subscriptions = [.. _subscriptions];
            _subscriptions.Clear();
        }

        foreach (var subscription in subscriptions)
        {
            subscription.Queue.Writer.TryComplete();
        }

        await Task.WhenAll(subscriptions.Select(s => s.Reader)).ConfigureAwait(false);
    }

    public void Dispose()
    {
        lock (_lock)
        {
            foreach (var subscription in _subscriptions)
            {
                subscription.Queue.Writer.TryComplete();
            }

            _subscriptions.Clear();
        }
    }

    private void Remove(Subscription subscription)
    {
        lock (_lock)
        {
            _subscriptions.Remove(subscription);
        }

        subscription.Queue.Writer.TryComplete();
    }

    private sealed class Subscription(EventBus bus, Action<PipelineEvent> handler) : IDisposable
    {
        public Channel<PipelineEvent> Queue { get; } =
            Channel.CreateUnbounded<PipelineEvent>(new UnboundedChannelOptions { SingleReader = true });

        public Task Reader { get; set; } = Task.CompletedTask;

        public async Task RunAsync()
        {
            await foreach (var pipelineEvent in Queue.Reader.ReadAllAsync().ConfigureAwait(false))
            {
                try
                {
                    handler(pipelineEvent);
                }
#pragma warning disable CA1031 // A failing subscriber must not stop delivery to itself or to others.
                catch (Exception e)
#pragma warning restore CA1031
                {
                    bus._onSubscriberError?.Invoke(e);
                }
            }
        }

        public void Dispose() => bus.Remove(this);
    }
}
