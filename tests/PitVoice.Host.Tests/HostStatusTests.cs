using PitVoice.Core.Pit;
using PitVoice.Core.Ptt;
using PitVoice.Core.Settings;
using PitVoice.IRacing.Diagnostics;
using Xunit;

namespace PitVoice.Host.Tests;

/// <summary>State of the tray icon (design decision 3, spec: Tray status).</summary>
public sealed class HostStatusTests
{
    private static readonly PttBinding Wheel = new(PttKey.JoystickButton("030000004f0400000a00000000000000", 1), "GT Neo");

    private static readonly HostFacts ReadyFacts = HostFacts.Initial with
    {
        Starting = false,
        ModelId = HostFixture.RuModel,
        ModelInstalled = true,
        Binding = Wheel,
        PttDeviceConnected = true,
        Game = ConnectionState.InCar,
    };

    [Fact]
    public void Ready_when_everything_works() =>
        Assert.Equal(new HostStatus(HostState.Ready, StatusReason.None, null, ReadyFacts), HostStatus.Evaluate(ReadyFacts));

    [Fact]
    public void No_model_wins_over_no_game()
    {
        var status = HostStatus.Evaluate(ReadyFacts with { ModelInstalled = false, Game = ConnectionState.NotRunning });

        Assert.Equal(HostState.NoModel, status.State);
        Assert.Equal(StatusReason.ModelNotInstalled, status.Reason);
        Assert.Equal(HostFixture.RuModel, status.Detail);
    }

    [Theory]
    [InlineData(StatusReason.ModelFailed)]
    [InlineData(StatusReason.ModelLoading)]
    public void Model_not_loaded_is_no_model(StatusReason reason)
    {
        var facts = reason == StatusReason.ModelFailed
            ? ReadyFacts with { ModelError = "model directory corrupted", PttDeviceConnected = false }
            : ReadyFacts with { ModelLoading = true, Binding = null };

        var status = HostStatus.Evaluate(facts);

        Assert.Equal(HostState.NoModel, status.State);
        Assert.Equal(reason, status.Reason);
    }

    [Fact]
    public void Error_wins_over_no_game()
    {
        var notAssigned = HostStatus.Evaluate(ReadyFacts with { Binding = null, Game = ConnectionState.NotRunning });
        var unplugged = HostStatus.Evaluate(ReadyFacts with { PttDeviceConnected = false, Game = ConnectionState.NotRunning });
        var packFailed = HostStatus.Evaluate(ReadyFacts with { PackError = "ru.yaml:3: error", ModelId = null });

        Assert.Equal((HostState.Error, StatusReason.PttNotAssigned), (notAssigned.State, notAssigned.Reason));
        Assert.Equal((HostState.Error, StatusReason.PttDeviceDisconnected, "GT Neo"), (unplugged.State, unplugged.Reason, unplugged.Detail));
        Assert.Equal((HostState.Error, StatusReason.PackFailed), (packFailed.State, packFailed.Reason));
    }

    [Fact]
    public void Keyboard_binding_has_no_device_to_lose()
    {
        var status = HostStatus.Evaluate(ReadyFacts with { Binding = HostFixture.KeyBinding, PttDeviceConnected = false });

        Assert.Equal(HostState.Ready, status.State);
    }

    [Fact]
    public void Game_not_running_is_no_connection() =>
        Assert.Equal(HostState.NoGame, HostStatus.Evaluate(ReadyFacts with { Game = ConnectionState.NotRunning }).State);

    [Fact]
    public async Task Game_closed_is_seen_at_the_next_poll()
    {
        await using var fixture = new HostFixture();
        var host = await fixture.StartAsync();
        Assert.Equal(HostState.Ready, host.Status.State);
        var changes = new List<HostStatus>();
        host.StatusChanged += changes.Add;

        fixture.Platform.Game.State = ConnectionState.NotRunning;
        fixture.Time.Advance(TimeSpan.FromSeconds(1));
        Assert.Equal(HostState.Ready, host.Status.State);
        fixture.Time.Advance(TimeSpan.FromSeconds(1));

        await LiveApplyTests.WaitUntilAsync(() => host.Status.State == HostState.NoGame);
        Assert.Equal(StatusReason.GameNotRunning, Assert.Single(changes).Reason);
    }

    [Fact]
    public async Task Game_elevation_is_checked_when_the_game_starts()
    {
        await using var fixture = new HostFixture();
        fixture.Platform.Game.State = ConnectionState.NotRunning;
        var host = await fixture.StartAsync();
        Assert.False(host.Status.Facts.GameElevated);

        fixture.Platform.Elevation = GameElevation.Elevated;
        fixture.Platform.Game.State = ConnectionState.Connected;
        fixture.Time.Advance(TimeSpan.FromSeconds(2));

        await LiveApplyTests.WaitUntilAsync(() => host.Status.Facts.GameElevated);
        Assert.Equal(HostState.Ready, host.Status.State);
    }

    [Fact]
    public async Task Unplugged_wheel_is_an_error_naming_it_and_plugging_it_back_restores_the_state()
    {
        await using var fixture = new HostFixture(AppSettings.Defaults("ru") with { PttBinding = Wheel });
        fixture.Platform.ConnectedDevices.Add((Wheel.Key.Device, "GT Neo"));
        var host = await fixture.StartAsync();
        fixture.Time.Advance(AppHost.DeviceCheckDelay);
        Assert.Equal(HostState.Ready, host.Status.State);

        fixture.Platform.LastSource.Device(Wheel.Key.Device, "GT Neo", connected: false);
        Assert.Equal((HostState.Error, StatusReason.PttDeviceDisconnected, "GT Neo"), (host.Status.State, host.Status.Reason, host.Status.Detail));

        fixture.Platform.LastSource.Device(Wheel.Key.Device, "GT Neo", connected: true);
        Assert.Equal(HostState.Ready, host.Status.State);
    }

    [Fact]
    public async Task Wheel_absent_at_start_is_found_after_the_device_check()
    {
        await using var fixture = new HostFixture(AppSettings.Defaults("ru") with { PttBinding = Wheel });
        var host = await fixture.StartAsync();

        fixture.Time.Advance(AppHost.DeviceCheckDelay);

        await LiveApplyTests.WaitUntilAsync(() => host.Status.Reason == StatusReason.PttDeviceDisconnected);
    }

    [Fact]
    public async Task Device_state_is_read_again_after_the_source_is_rebuilt()
    {
        var other = new PttBinding(PttKey.JoystickButton("03000000aaaa0000bbbb000000000000", 2), "Button Box");
        await using var fixture = new HostFixture(AppSettings.Defaults("ru") with { PttBinding = Wheel });
        fixture.Platform.ConnectedDevices.Add((Wheel.Key.Device, "GT Neo"));
        var host = await fixture.StartAsync();
        fixture.Time.Advance(AppHost.DeviceCheckDelay);
        Assert.Equal(HostState.Ready, host.Status.State);

        host.Update(s => s with { PttBinding = other }); // the button box is not connected
        await host.WhenAppliedAsync().WaitAsync(HostFixture.Wait);
        fixture.Time.Advance(AppHost.DeviceCheckDelay);

        await LiveApplyTests.WaitUntilAsync(() => host.Status.Reason == StatusReason.PttDeviceDisconnected);
        Assert.Equal("Button Box", host.Status.Detail);
        Assert.True(fixture.Platform.Sources.First().Disposed);
    }

    [Fact]
    public async Task Source_failure_is_an_error()
    {
        await using var fixture = new HostFixture();
        fixture.Platform.SourceError = "keyboard hook failed";
        var host = await fixture.StartAsync();

        Assert.Equal((HostState.Error, StatusReason.PttSourceFailed), (host.Status.State, host.Status.Reason));
    }
}
