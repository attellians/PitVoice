using System.Collections.Concurrent;
using PitVoice.Core.Cues;
using PitVoice.Core.Events;
using PitVoice.Core.Settings;
using Xunit;

namespace PitVoice.Host.Tests;

/// <summary>The indicator model attached to the host must not change what the pipeline does (task 2.3).</summary>
public sealed class IndicatorHostTests
{
    [Fact]
    public async Task Indicator_follows_the_phrase_and_the_pipeline_is_the_same()
    {
        var without = await RunPhraseAsync(attach: false);
        var with = await RunPhraseAsync(attach: true);

        Assert.Equal(without.Events, with.Events);
        Assert.Equal([Cue.Start, Cue.Success], with.Cues);
        Assert.Equal([IndicatorVisual.Listening, IndicatorVisual.Recognizing, IndicatorVisual.Done], with.Visuals);
    }

    [Fact]
    public async Task Failing_indicator_is_reported_and_the_phrase_still_gets_its_cue()
    {
        await using var fixture = new HostFixture();
        var host = await fixture.StartAsync();
        var failures = new ConcurrentQueue<Exception>();
        host.SubscriberFailed += failures.Enqueue;
        using var indicator = host.CreateIndicator();
        indicator.Changed += _ => throw new InvalidOperationException("overlay failed");

        fixture.Say("залей тридцать литров");
        var signaled = await fixture.WaitForAsync<ResultSignaled>();

        Assert.Equal(Cue.Success, signaled.Cue);
        Assert.Equal([Cue.Start, Cue.Success], fixture.Platform.LastCues.Played);
        Assert.Contains(failures, e => e.Message == "overlay failed");
    }

    [Fact]
    public async Task Indicator_follows_overlay_settings_without_rebuilding_the_session()
    {
        await using var fixture = new HostFixture();
        var host = await fixture.StartAsync();
        using var indicator = host.CreateIndicator();

        host.Update(s => s with { Overlay = s.Overlay with { Enabled = false } });
        await host.WhenAppliedAsync().WaitAsync(HostFixture.Wait);

        Assert.False(indicator.Settings.Enabled);
        Assert.False(indicator.View.Visible);
        Assert.Single(fixture.Platform.Engines);
        Assert.Single(fixture.Platform.Sources);
    }

    [Fact]
    public async Task Disposed_indicator_no_longer_gets_events()
    {
        await using var fixture = new HostFixture();
        var host = await fixture.StartAsync();
        var indicator = host.CreateIndicator();
        indicator.Dispose();

        fixture.Say("залей тридцать литров");
        await fixture.WaitForAsync<ResultSignaled>();
        host.Update(s => s with { Overlay = s.Overlay with { Scale = 150 } });

        Assert.Equal(IndicatorVisual.Idle, indicator.View.Visual);
        Assert.Equal(OverlaySettings.DefaultScale, indicator.Settings.Scale);
    }

    private static async Task<(IReadOnlyList<string> Events, IReadOnlyList<Cue> Cues, IReadOnlyList<IndicatorVisual> Visuals)> RunPhraseAsync(bool attach)
    {
        await using var fixture = new HostFixture();
        var host = await fixture.StartAsync();
        var visuals = new ConcurrentQueue<IndicatorVisual>();
        using var indicator = attach ? host.CreateIndicator() : null;
        if (indicator is not null)
        {
            indicator.Changed += v => visuals.Enqueue(v.Visual);
        }

        fixture.Say("залей тридцать литров");
        await fixture.WaitForAsync<ResultSignaled>();
        if (indicator is not null)
        {
            var deadline = DateTime.UtcNow + HostFixture.Wait;
            while (indicator.View.Visual != IndicatorVisual.Done && DateTime.UtcNow < deadline)
            {
                await Task.Delay(5);
            }
        }

        var events = fixture.Events.Where(e => e.PhraseId == 1).Select(e => e.GetType().Name).ToArray();
        return (events, fixture.Platform.LastCues.Played, [.. visuals.Distinct()]);
    }
}
