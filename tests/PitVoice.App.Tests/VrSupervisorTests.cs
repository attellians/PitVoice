using System.IO;
using Microsoft.Extensions.Time.Testing;
using PitVoice.App.Overlay.Vr;
using PitVoice.Core.Settings;
using PitVoice.Core.Vr;
using Xunit;

namespace PitVoice.App.Tests;

/// <summary>
/// The supervisor of the VR helper on fakes (add-vr-overlay D3, D4, D8; spec: SteamVR lifecycle, Fault isolation, Nothing
/// extra without VR mode).
/// </summary>
public sealed class VrSupervisorTests
{
    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 10, 23, 12, 0, 0, TimeSpan.Zero));
    private readonly FakeProbe _probe = new();
    private readonly FakeLauncher _launcher = new();
    private readonly List<string> _log = [];

    /// <summary>Spec: SteamVR not running — no helper for 10 minutes, the state stays "waiting".</summary>
    [Fact]
    public void Without_SteamVR_nothing_is_started()
    {
        using var supervisor = Start();

        _time.Advance(TimeSpan.FromMinutes(10));

        Assert.Equal(VrState.WaitingSteamVr, supervisor.State);
        Assert.Empty(_launcher.Started);
        Assert.InRange(_probe.Calls, 290, 310); // every 2 s
    }

    /// <summary>Spec: SteamVR started later — "in the headset" within 5 s, the kept messages go to the helper.</summary>
    [Fact]
    public void SteamVR_started_later_gets_the_helper_and_the_kept_messages()
    {
        using var supervisor = Start();
        supervisor.Send(new VrPlace(20, 14, 4));
        supervisor.Send(new VrFrame(1, 1, new byte[4]));
        supervisor.Send(new VrVisible(true));
        _time.Advance(TimeSpan.FromSeconds(30));

        _probe.Pid = 4242;
        _time.Advance(TimeSpan.FromSeconds(2));
        Assert.Equal(VrState.Starting, supervisor.State);
        var helper = Assert.Single(_launcher.Started);

        helper.Say("ready");

        Assert.Equal(VrState.InHeadset, supervisor.State);
        Assert.Collection(
            helper.Received,
            m => Assert.IsType<VrPlace>(m),
            m => Assert.IsType<VrFrame>(m),
            m => Assert.Equal(new VrVisible(true), m));
    }

    [Fact]
    public void Messages_go_straight_to_a_ready_helper_and_pings_go_every_second()
    {
        using var supervisor = Start();
        var helper = Ready();

        supervisor.Send(new VrVisible(false));
        _time.Advance(TimeSpan.FromSeconds(1));

        Assert.Equal(new VrVisible(false), helper.Received[0]);
        Assert.IsType<VrPing>(helper.Received[1]);
    }

    /// <summary>Spec: SteamVR closed — no failure, back to waiting, no new helper while SteamVR is gone.</summary>
    [Fact]
    public void SteamVR_closed_is_not_a_failure()
    {
        using var supervisor = Start();
        var helper = Ready();

        helper.Say("quit");
        _probe.Pid = null;
        helper.Exit(VrExitCode.SteamVrClosed);
        _time.Advance(TimeSpan.FromMinutes(1));

        Assert.Equal(VrState.WaitingSteamVr, supervisor.State);
        Assert.Single(_launcher.Started);
        Assert.DoesNotContain(_log, l => l.Contains("failed", StringComparison.Ordinal));
    }

    /// <summary>D4: SteamVR may end the helper itself after "quit".</summary>
    [Fact]
    public void Helper_killed_by_SteamVR_after_quit_is_not_a_failure()
    {
        using var supervisor = Start();
        var helper = Ready();

        helper.Say("quit");
        helper.Exit(1);

        Assert.Equal(VrState.WaitingSteamVr, supervisor.State);
        Assert.DoesNotContain(_log, l => l.Contains("failed", StringComparison.Ordinal));
    }

    /// <summary>Spec: One failure — the helper is back within 5 s.</summary>
    [Fact]
    public void One_crash_is_retried_after_two_seconds()
    {
        using var supervisor = Start();
        var helper = Ready();

        helper.Exit(-1073741819);
        Assert.Equal(VrState.WaitingSteamVr, supervisor.State);
        Assert.Contains(_log, l => l.Contains("exited with code -1073741819", StringComparison.Ordinal) && l.Contains("ran", StringComparison.Ordinal));

        _time.Advance(TimeSpan.FromSeconds(2));
        Assert.Equal(2, _launcher.Started.Count);
        _launcher.Started[1].Say("ready");
        Assert.Equal(VrState.InHeadset, supervisor.State);
    }

    /// <summary>Spec: Repeated failures — three attempts, then "VR error" and no more attempts.</summary>
    [Fact]
    public void Three_failures_within_two_minutes_stop_the_attempts()
    {
        _launcher.OnStart = helper =>
        {
            helper.Say("error init-108");
            helper.Exit(VrExitCode.InitFailed);
        };
        using var supervisor = Start();
        _probe.Pid = 100;

        Run(TimeSpan.FromSeconds(30));

        Assert.Equal(VrState.Failed, supervisor.State);
        Assert.Equal(3, _launcher.Started.Count);
        Assert.Equal(3, _log.Count(l => l.Contains("helper failed", StringComparison.Ordinal) && l.Contains("error init-108", StringComparison.Ordinal)));

        _time.Advance(TimeSpan.FromMinutes(10));
        Assert.Equal(3, _launcher.Started.Count);
    }

    /// <summary>Spec: Recovering after an error — a new vrserver process means SteamVR was restarted.</summary>
    [Fact]
    public void Failed_state_ends_when_SteamVR_restarts()
    {
        _launcher.OnStart = helper => helper.Exit(VrExitCode.InitFailed);
        using var supervisor = Start();
        _probe.Pid = 100;
        Run(TimeSpan.FromSeconds(30));
        Assert.Equal(VrState.Failed, supervisor.State);

        _launcher.OnStart = null;
        _probe.Pid = null;
        _time.Advance(TimeSpan.FromSeconds(4));
        Assert.Equal(VrState.Failed, supervisor.State);

        _probe.Pid = 200;
        _time.Advance(TimeSpan.FromSeconds(2));
        _launcher.Started[^1].Say("ready");

        Assert.Equal(4, _launcher.Started.Count);
        Assert.Equal(VrState.InHeadset, supervisor.State);
    }

    [Fact]
    public void Failures_spread_over_more_than_two_minutes_keep_retrying()
    {
        using var supervisor = Start();
        for (var i = 0; i < 5; i++)
        {
            // Each helper works for a minute, answering the pings, then crashes.
            var helper = Ready();
            for (var second = 0; second < 61; second++)
            {
                _time.Advance(TimeSpan.FromSeconds(1));
                helper.Say("pong");
            }

            helper.Exit(1);
        }

        Assert.NotEqual(VrState.Failed, supervisor.State);
        Ready();
        Assert.Equal(6, _launcher.Started.Count);
    }

    /// <summary>Spec: Hung connection — no pong for 5 s is a failure; the helper is killed.</summary>
    [Fact]
    public void Missing_pong_for_five_seconds_kills_the_helper_and_retries()
    {
        using var supervisor = Start();
        var helper = Ready();

        _time.Advance(TimeSpan.FromSeconds(3));
        helper.Say("pong");
        _time.Advance(TimeSpan.FromSeconds(4));
        Assert.Equal(VrState.InHeadset, supervisor.State);

        _time.Advance(TimeSpan.FromSeconds(1));
        Assert.True(helper.Killed);
        Assert.Contains(_log, l => l.Contains("no pong", StringComparison.Ordinal));

        _time.Advance(TimeSpan.FromSeconds(2));
        Assert.Equal(2, _launcher.Started.Count);
    }

    [Fact]
    public void No_ready_within_ten_seconds_is_a_failure()
    {
        using var supervisor = Start();
        _probe.Pid = 1;
        _time.Advance(VrSupervisor.ProbeInterval);
        var helper = Assert.Single(_launcher.Started);

        _time.Advance(TimeSpan.FromSeconds(10));

        Assert.True(helper.Killed);
        Assert.Contains(_log, l => l.Contains("no ready", StringComparison.Ordinal));
    }

    [Fact]
    public void Helper_that_cannot_start_is_a_failure_and_the_app_goes_on()
    {
        _launcher.Throws = true;
        using var supervisor = Start();
        _probe.Pid = 1;

        Run(TimeSpan.FromSeconds(10));

        Assert.Equal(VrState.Failed, supervisor.State);
        Assert.Contains(_log, l => l.Contains("could not start the helper", StringComparison.Ordinal));
    }

    /// <summary>Spec: Application exit — the helper is ended, nothing more is started.</summary>
    [Fact]
    public void Dispose_ends_the_helper_and_stops_looking_for_SteamVR()
    {
        var supervisor = Start();
        var helper = Ready();
        var calls = _probe.Calls;

        supervisor.Dispose();
        _time.Advance(TimeSpan.FromMinutes(1));

        Assert.True(helper.Disposed);
        Assert.Equal(calls, _probe.Calls);
        Assert.Single(_launcher.Started);
    }

    /// <summary>Spec: VR mode off — no SteamVR check and no helper at all (task 4.4).</summary>
    [Fact]
    public void Mode_off_creates_nothing()
    {
        var created = 0;
        _probe.Pid = 1;
        using var mode = new VrMode(
            () =>
            {
                created++;
                return new VrSupervisor(_launcher, _probe, _time, _log.Add);
            },
            _ => { });

        mode.Apply(OverlaySettings.Default);
        Assert.Equal(VrState.Off, mode.State);
        mode.Apply(OverlaySettings.Default with { Vr = true, Enabled = false });
        Assert.Equal(VrState.OverlayOff, mode.State);
        _time.Advance(TimeSpan.FromMinutes(1));

        Assert.Equal(0, created);
        Assert.Equal(0, _probe.Calls);
        Assert.Empty(_launcher.Started);
    }

    /// <summary>Spec: Status line, VR mode turned off, Overlay turned off.</summary>
    [Fact]
    public void Mode_follows_both_switches_at_once()
    {
        VrSupervisor? attached = null;
        using var mode = new VrMode(() => new VrSupervisor(_launcher, _probe, _time, _log.Add), s => attached = s);
        var changes = 0;
        mode.Changed += () => changes++;

        mode.Apply(OverlaySettings.Default with { Vr = true });
        Assert.Equal(VrState.WaitingSteamVr, mode.State);
        Assert.NotNull(attached);
        Assert.Equal(1, _probe.Calls); // looked for SteamVR at once

        _probe.Pid = 7;
        _time.Advance(TimeSpan.FromSeconds(2));
        var helper = _launcher.Started.Single();
        helper.Say("ready");
        Assert.Equal(VrState.InHeadset, mode.State);

        mode.Apply(OverlaySettings.Default with { Vr = true, Enabled = false });
        Assert.Equal(VrState.OverlayOff, mode.State);
        Assert.Null(attached);
        Assert.True(helper.Disposed);

        mode.Apply(OverlaySettings.Default with { Vr = true });
        _time.Advance(TimeSpan.Zero);
        Assert.Equal(2, _launcher.Started.Count);

        mode.Apply(OverlaySettings.Default);
        Assert.Equal(VrState.Off, mode.State);
        Assert.True(changes >= 5);
    }

    /// <summary>Switching the mode off and on again leaves the failed state (D8).</summary>
    [Fact]
    public void Switching_the_mode_again_leaves_the_failed_state()
    {
        _launcher.OnStart = helper => helper.Exit(VrExitCode.InitFailed);
        _probe.Pid = 1;
        using var mode = new VrMode(() => new VrSupervisor(_launcher, _probe, _time, _log.Add), _ => { });
        mode.Apply(OverlaySettings.Default with { Vr = true });
        _launcher.Settle();
        Run(TimeSpan.FromSeconds(30));
        Assert.Equal(VrState.Failed, mode.State);

        _launcher.OnStart = null;
        mode.Apply(OverlaySettings.Default);
        mode.Apply(OverlaySettings.Default with { Vr = true });
        _launcher.Started[^1].Say("ready");

        Assert.Equal(VrState.InHeadset, mode.State);
    }

    /// <summary>Lets the time pass second by second, with the output of the helpers handled in between.</summary>
    private void Run(TimeSpan time)
    {
        for (var passed = TimeSpan.Zero; passed < time; passed += TimeSpan.FromSeconds(1))
        {
            _time.Advance(TimeSpan.FromSeconds(1));
            _launcher.Settle();
        }
    }

    private VrSupervisor Start()
    {
        var supervisor = new VrSupervisor(_launcher, _probe, _time, _log.Add);
        supervisor.Start();
        return supervisor;
    }

    /// <summary>SteamVR runs and a new helper says "ready".</summary>
    private FakeHelper Ready(bool advance = true)
    {
        _probe.Pid ??= 4242;
        var count = _launcher.Started.Count;
        if (advance)
        {
            _time.Advance(TimeSpan.FromSeconds(2));
        }

        for (var second = 0; _launcher.Started.Count == count; second++)
        {
            Assert.True(second < 30, "no helper was started");
            _time.Advance(TimeSpan.FromSeconds(1));
        }

        var helper = _launcher.Started[^1];
        helper.Say("ready");
        return helper;
    }

    private sealed class FakeProbe : ISteamVrProbe
    {
        public int? Pid { get; set; }

        public int Calls { get; private set; }

        public int? ServerProcessId()
        {
            Calls++;
            return Pid;
        }
    }

    private sealed class FakeLauncher : IVrHelperLauncher
    {
        public List<FakeHelper> Started { get; } = [];

        public Action<FakeHelper>? OnStart { get; set; }

        public bool Throws { get; set; }

        public List<Task> Pending { get; } = [];

        public void Settle() => Task.WaitAll([.. Pending]);

        public IVrHelperProcess Start(Action<string> line, Action<int> exited)
        {
            if (Throws)
            {
                throw new FileNotFoundException("the VR helper is not installed");
            }

            var helper = new FakeHelper(line, exited);
            Started.Add(helper);

            // Like a real process: its output comes on another thread, after the start returned.
            if (OnStart is { } onStart)
            {
                Pending.Add(Task.Run(() => onStart(helper)));
            }

            return helper;
        }
    }

    private sealed class FakeHelper(Action<string> line, Action<int> exited) : IVrHelperProcess
    {
        public List<VrMessage> Received { get; } = [];

        public bool Killed { get; private set; }

        public bool Disposed { get; private set; }

        public void Say(string text) => line(text);

        public void Exit(int code) => exited(code);

        public void Send(VrMessage message) => Received.Add(message);

        public void Kill() => Killed = true;

        public void Dispose() => Disposed = true;
    }
}
