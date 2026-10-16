using PitVoice.Core.Events;
using PitVoice.Core.Ptt;
using PitVoice.Core.Settings;
using Xunit;

namespace PitVoice.Host.Tests;

/// <summary>Button assignment from the window (design decision 12, spec: PTT assignment in the window).</summary>
public sealed class PttAssignmentTests
{
    private const string WheelGuid = "030000004f0400000a00000000000000";
    private static readonly PttBinding Button1 = new(PttKey.JoystickButton(WheelGuid, 1), "GT Neo");

    private static async Task<FakePttSource> AssignmentSourceAsync(HostFixture fixture)
    {
        await LiveApplyTests.WaitUntilAsync(() => fixture.Platform.Sources.Any(s => s.IsAssignment && s.Started));
        return fixture.Platform.Sources.Last(s => s.IsAssignment);
    }

    [Fact]
    public async Task Wheel_button_becomes_the_binding_and_starts_the_next_recording()
    {
        await using var fixture = new HostFixture();
        var host = await fixture.StartAsync();

        var assigning = host.AssignPttAsync();
        var all = await AssignmentSourceAsync(fixture);
        Assert.True(fixture.Platform.Sources.First().Disposed); // the bound source is closed meanwhile
        all.Press(PttKey.JoystickButton(WheelGuid, 3), "GT Neo");
        var result = await assigning.WaitAsync(HostFixture.Wait);
        await host.WhenAppliedAsync().WaitAsync(HostFixture.Wait);

        Assert.Equal(PttAssignmentStatus.Assigned, result.Status);
        Assert.Equal("GT Neo, button 3", result.Binding!.Describe());
        Assert.True(result.Save!.Saved);
        Assert.Equal(result.Binding, fixture.Store.Current.PttBinding);
        Assert.True(all.Disposed);

        var bound = fixture.Platform.LastSource;
        Assert.False(bound.IsAssignment);
        bound.Press(PttKey.JoystickButton(WheelGuid, 3), "GT Neo");
        await fixture.WaitForAsync<PttPressed>(pumpTime: false);
    }

    [Fact]
    public async Task Current_ptt_pressed_during_assignment_does_not_record_and_stays_the_binding()
    {
        await using var fixture = new HostFixture(AppSettings.Defaults("ru") with { PttBinding = Button1 });
        fixture.Platform.ConnectedDevices.Add((WheelGuid, "GT Neo"));
        var host = await fixture.StartAsync();
        var bound = fixture.Platform.LastSource;

        var assigning = host.AssignPttAsync();
        var all = await AssignmentSourceAsync(fixture);
        bound.Press(Button1.Key, "GT Neo"); // the old source is closed: nothing reaches the pipeline
        all.Press(Button1.Key, "GT Neo");
        var result = await assigning.WaitAsync(HostFixture.Wait);
        await host.WhenAppliedAsync().WaitAsync(HostFixture.Wait);

        Assert.Equal(Button1.Key, result.Binding!.Key);
        Assert.Equal(Button1.Key, fixture.Store.Current.PttBinding!.Key);
        Assert.DoesNotContain(fixture.Events, e => e is PttPressed);
        Assert.Empty(fixture.Platform.LastCues.Played);
    }

    [Fact]
    public async Task Timeout_leaves_the_binding_unchanged()
    {
        await using var fixture = new HostFixture();
        var host = await fixture.StartAsync();

        var assigning = host.AssignPttAsync();
        await AssignmentSourceAsync(fixture);
        fixture.Time.Advance(PttAssignment.Timeout);
        var result = await assigning.WaitAsync(HostFixture.Wait);
        await host.WhenAppliedAsync().WaitAsync(HostFixture.Wait);

        Assert.Equal(PttAssignmentStatus.TimedOut, result.Status);
        Assert.Null(result.Binding);
        Assert.Equal(HostFixture.KeyBinding, host.Settings.PttBinding);
        Assert.Empty(fixture.Store.Saved);
        fixture.Say("залей тридцать литров"); // the old binding works again
        await fixture.WaitForAsync<ResultSignaled>();
    }

    [Fact]
    public async Task Escape_cancels()
    {
        await using var fixture = new HostFixture();
        var host = await fixture.StartAsync();

        var assigning = host.AssignPttAsync();
        var all = await AssignmentSourceAsync(fixture);
        all.Press(PttKey.KeyboardKey(PttAssignment.EscapeVirtualKey), "Esc");

        Assert.Equal(PttAssignmentStatus.Cancelled, (await assigning.WaitAsync(HostFixture.Wait)).Status);
        Assert.Equal(HostFixture.KeyBinding, host.Settings.PttBinding);
    }

    [Fact]
    public async Task First_assignment_without_a_previous_binding()
    {
        await using var fixture = new HostFixture(AppSettings.Defaults("ru"));
        var host = await fixture.StartAsync();
        Assert.Equal(StatusReason.PttNotAssigned, host.Status.Reason);

        var assigning = host.AssignPttAsync();
        var all = await AssignmentSourceAsync(fixture);
        all.Press(HostFixture.F13);
        await assigning.WaitAsync(HostFixture.Wait);
        await host.WhenAppliedAsync().WaitAsync(HostFixture.Wait);

        Assert.NotEqual(StatusReason.PttNotAssigned, host.Status.Reason);
        fixture.Say("залей тридцать литров");
        await fixture.WaitForAsync<ResultSignaled>();
    }
}
