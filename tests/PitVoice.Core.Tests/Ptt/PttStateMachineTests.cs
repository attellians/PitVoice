using PitVoice.Core.Ptt;
using Xunit;

namespace PitVoice.Core.Tests.Ptt;

public sealed class PttStateMachineTests
{
    private const string Wheel = "030000006d0400001bc2000000000000";
    private const string ButtonBox = "03000000ad1b00003500000000000000";
    private static readonly PttKey Button5 = PttKey.JoystickButton(Wheel, 5);
    private static readonly PttBinding WheelBinding = new(Button5, "Wheel");

    private readonly SteppedTimeProvider _time = new();
    private readonly List<PttTransition> _transitions = [];

    private PttStateMachine Create(PttMode mode, PttBinding? binding = null)
    {
        var machine = new PttStateMachine(binding ?? WheelBinding, mode, _time);
        machine.Transition += _transitions.Add;
        return machine;
    }

    private static PttInput Down(PttKey key) => new(key, true, "Wheel");

    private static PttInput Up(PttKey key) => new(key, false, "Wheel");

    private void Hold(PttStateMachine machine, TimeSpan duration, PttKey? key = null)
    {
        machine.OnInput(Down(key ?? Button5));
        _time.Advance(duration);
        machine.OnInput(Up(key ?? Button5));
    }

    [Fact]
    public void Hold_NormalPress_StartsOnPressAndStopsOnRelease()
    {
        using var machine = Create(PttMode.Hold);

        machine.OnInput(Down(Button5));
        Assert.IsType<PttTransition.Started>(Assert.Single(_transitions));
        Assert.True(machine.IsRecording);

        _time.Advance(TimeSpan.FromSeconds(2));
        machine.OnInput(Up(Button5));

        Assert.Equal(new PttTransition.Stopped(RecordingStopReason.Released), _transitions[1]);
        Assert.False(machine.IsRecording);
    }

    [Fact]
    public void Hold_TooShortPress_IsDiscarded()
    {
        using var machine = Create(PttMode.Hold);

        Hold(machine, TimeSpan.FromMilliseconds(150));

        Assert.Equal([new PttTransition.Started(), new PttTransition.Discarded()], _transitions);
    }

    [Fact]
    public void Hold_ExactlyMinHold_IsKept()
    {
        using var machine = Create(PttMode.Hold);

        Hold(machine, PttStateMachine.MinHold);

        Assert.IsType<PttTransition.Stopped>(_transitions[^1]);
    }

    [Fact]
    public void Hold_LongerThanLimit_StopsOnlyOnRelease()
    {
        using var machine = Create(PttMode.Hold);

        machine.OnInput(Down(Button5));
        _time.Advance(TimeSpan.FromSeconds(12));
        Assert.Single(_transitions);

        machine.OnInput(Up(Button5));
        Assert.Equal(new PttTransition.Stopped(RecordingStopReason.Released), _transitions[1]);
    }

    [Fact]
    public void AutoRepeat_RepeatedDownWithoutUp_IsIgnored()
    {
        var key = PttKey.KeyboardKey(0x7C);
        using var machine = Create(PttMode.Hold, new PttBinding(key, "F13"));

        machine.OnInput(Down(key));
        for (var i = 0; i < 10; i++)
        {
            _time.Advance(TimeSpan.FromMilliseconds(33));
            machine.OnInput(Down(key));
        }

        machine.OnInput(Up(key));

        Assert.Equal([new PttTransition.Started(), new PttTransition.Stopped(RecordingStopReason.Released)], _transitions);
    }

    [Fact]
    public void OtherButtons_AndSameButtonOfOtherDevice_AreIgnored()
    {
        using var machine = Create(PttMode.Hold);

        Hold(machine, TimeSpan.FromSeconds(1), PttKey.JoystickButton(Wheel, 6));
        Hold(machine, TimeSpan.FromSeconds(1), PttKey.JoystickButton(ButtonBox, 5));
        Hold(machine, TimeSpan.FromSeconds(1), PttKey.KeyboardKey(5));

        Assert.Empty(_transitions);
    }

    [Fact]
    public void Binding_MatchesGuidCaseInsensitively_RegardlessOfDeviceOrder()
    {
        using var machine = Create(PttMode.Hold);

        Hold(machine, TimeSpan.FromSeconds(1), PttKey.JoystickButton(Wheel.ToUpperInvariant(), 5));

        Assert.Equal(2, _transitions.Count);
    }

    [Fact]
    public void Hold_DeviceUnplugged_StopsAsRelease()
    {
        using var machine = Create(PttMode.Hold);

        machine.OnInput(Down(Button5));
        _time.Advance(TimeSpan.FromSeconds(1));
        machine.OnDeviceChanged(new PttDeviceChange(Wheel, "Wheel", Connected: false));

        Assert.Equal(new PttTransition.Stopped(RecordingStopReason.DeviceLost), _transitions[1]);
    }

    [Fact]
    public void Hold_DeviceUnpluggedRightAfterPress_IsDiscarded()
    {
        using var machine = Create(PttMode.Hold);

        machine.OnInput(Down(Button5));
        _time.Advance(TimeSpan.FromMilliseconds(100));
        machine.OnDeviceChanged(new PttDeviceChange(Wheel, "Wheel", Connected: false));

        Assert.IsType<PttTransition.Discarded>(_transitions[1]);
    }

    [Fact]
    public void DeviceReconnected_BindingWorksAgain()
    {
        using var machine = Create(PttMode.Hold);

        machine.OnInput(Down(Button5));
        machine.OnDeviceChanged(new PttDeviceChange(Wheel, "Wheel", Connected: false));
        machine.OnDeviceChanged(new PttDeviceChange(Wheel, "Wheel", Connected: true));
        _transitions.Clear();

        Hold(machine, TimeSpan.FromSeconds(1));

        Assert.Equal([new PttTransition.Started(), new PttTransition.Stopped(RecordingStopReason.Released)], _transitions);
    }

    [Fact]
    public void OtherDeviceUnplugged_DoesNotStop()
    {
        using var machine = Create(PttMode.Hold);

        machine.OnInput(Down(Button5));
        machine.OnDeviceChanged(new PttDeviceChange(ButtonBox, "Box", Connected: false));

        Assert.True(machine.IsRecording);
    }

    [Fact]
    public void Toggle_TwoPresses_StartAndStop()
    {
        using var machine = Create(PttMode.Toggle);

        Hold(machine, TimeSpan.FromMilliseconds(80));
        _time.Advance(TimeSpan.FromSeconds(2));
        Hold(machine, TimeSpan.FromMilliseconds(80));

        Assert.Equal([new PttTransition.Started(), new PttTransition.Stopped(RecordingStopReason.SecondPress)], _transitions);
    }

    [Fact]
    public void Toggle_ShortTap_DoesNotStop()
    {
        using var machine = Create(PttMode.Toggle);

        Hold(machine, TimeSpan.FromMilliseconds(100));

        Assert.Equal([new PttTransition.Started()], _transitions);
        Assert.True(machine.IsRecording);
    }

    [Fact]
    public void Toggle_BounceWithinDebounce_IsIgnored()
    {
        using var machine = Create(PttMode.Toggle);

        Hold(machine, TimeSpan.FromMilliseconds(60));
        _time.Advance(TimeSpan.FromMilliseconds(60));
        Hold(machine, TimeSpan.FromMilliseconds(30)); // second press 120 ms after the first
        Assert.True(machine.IsRecording);

        _time.Advance(TimeSpan.FromMilliseconds(200));
        Hold(machine, TimeSpan.FromMilliseconds(30)); // after 300 ms

        Assert.Equal(new PttTransition.Stopped(RecordingStopReason.SecondPress), _transitions[^1]);
    }

    [Fact]
    public void Toggle_NoSecondPress_StopsAfterEightSeconds()
    {
        using var machine = Create(PttMode.Toggle);

        Hold(machine, TimeSpan.FromMilliseconds(80));
        _time.Advance(PttStateMachine.ToggleAutoStop - TimeSpan.FromMilliseconds(81));
        Assert.True(machine.IsRecording);

        _time.Advance(TimeSpan.FromMilliseconds(1));

        Assert.Equal(new PttTransition.Stopped(RecordingStopReason.AutoStop), _transitions[^1]);
        Assert.False(machine.IsRecording);
    }

    [Fact]
    public void Toggle_PressRightAfterAutoStop_StartsNewRecording()
    {
        using var machine = Create(PttMode.Toggle);

        Hold(machine, TimeSpan.FromMilliseconds(80));
        _time.Advance(PttStateMachine.ToggleAutoStop);
        _time.Advance(TimeSpan.FromMilliseconds(50));
        Hold(machine, TimeSpan.FromMilliseconds(80));

        Assert.Equal(
            [
                new PttTransition.Started(),
                new PttTransition.Stopped(RecordingStopReason.AutoStop),
                new PttTransition.Started(),
            ],
            _transitions);
    }

    [Fact]
    public void Toggle_StoppedBySecondPress_OldTimerDoesNotStopNextRecording()
    {
        using var machine = Create(PttMode.Toggle);

        Hold(machine, TimeSpan.FromMilliseconds(80));
        _time.Advance(TimeSpan.FromSeconds(5));
        Hold(machine, TimeSpan.FromMilliseconds(80)); // stop at ~5 s
        _time.Advance(TimeSpan.FromSeconds(1));
        Hold(machine, TimeSpan.FromMilliseconds(80)); // new recording at ~6 s
        _time.Advance(TimeSpan.FromSeconds(4)); // the first timer would have fired at 8 s

        Assert.True(machine.IsRecording);
        Assert.Equal(3, _transitions.Count);
    }

    [Fact]
    public void Toggle_DeviceUnplugged_Stops()
    {
        using var machine = Create(PttMode.Toggle);

        Hold(machine, TimeSpan.FromMilliseconds(80));
        machine.OnDeviceChanged(new PttDeviceChange(Wheel, "Wheel", Connected: false));

        Assert.Equal(new PttTransition.Stopped(RecordingStopReason.DeviceLost), _transitions[^1]);
    }
}
