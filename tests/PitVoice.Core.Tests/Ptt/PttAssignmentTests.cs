using PitVoice.Core.Ptt;
using Xunit;

namespace PitVoice.Core.Tests.Ptt;

public sealed class PttAssignmentTests
{
    private const string Wheel = "030000006d0400001bc2000000000000";

    private readonly SteppedTimeProvider _time = new();

    [Fact]
    public async Task WheelButton_BecomesBinding()
    {
        using var assignment = new PttAssignment(_time);

        assignment.OnInput(new PttInput(PttKey.JoystickButton(Wheel, 5), true, "Fanatec Wheel"));

        var result = await assignment.Result;
        Assert.Equal(PttAssignmentStatus.Assigned, result.Status);
        Assert.Equal(new PttBinding(PttKey.JoystickButton(Wheel, 5), "Fanatec Wheel"), result.Binding);
        Assert.Equal("Fanatec Wheel, button 5", result.Binding!.Describe());
    }

    [Fact]
    public async Task KeyboardKey_BecomesBinding()
    {
        using var assignment = new PttAssignment(_time);

        assignment.OnInput(new PttInput(PttKey.KeyboardKey(0x7C), true, "F13"));

        var result = await assignment.Result;
        Assert.Equal(PttDeviceKind.Keyboard, result.Binding!.Kind);
        Assert.Equal("key F13", result.Binding.Describe());
    }

    [Fact]
    public async Task Release_IsIgnored_FirstPressWins()
    {
        using var assignment = new PttAssignment(_time);

        assignment.OnInput(new PttInput(PttKey.JoystickButton(Wheel, 3), false, "Wheel"));
        assignment.OnInput(new PttInput(PttKey.JoystickButton(Wheel, 5), true, "Wheel"));
        assignment.OnInput(new PttInput(PttKey.JoystickButton(Wheel, 6), true, "Wheel"));

        Assert.Equal(5, (await assignment.Result).Binding!.Key.Code);
    }

    [Fact]
    public async Task Escape_Cancels()
    {
        using var assignment = new PttAssignment(_time);

        assignment.OnInput(new PttInput(PttKey.KeyboardKey(PttAssignment.EscapeVirtualKey), true, "Esc"));

        var result = await assignment.Result;
        Assert.Equal(PttAssignmentStatus.Cancelled, result.Status);
        Assert.Null(result.Binding);
    }

    [Fact]
    public async Task NothingPressed_TimesOutAfterFifteenSeconds()
    {
        using var assignment = new PttAssignment(_time);

        _time.Advance(PttAssignment.Timeout - TimeSpan.FromMilliseconds(1));
        Assert.False(assignment.Result.IsCompleted);

        _time.Advance(TimeSpan.FromMilliseconds(1));

        var result = await assignment.Result;
        Assert.Equal(PttAssignmentStatus.TimedOut, result.Status);
        Assert.Null(result.Binding);
    }

    [Fact]
    public async Task PressAfterTimeout_DoesNotChangeResult()
    {
        using var assignment = new PttAssignment(_time);

        _time.Advance(PttAssignment.Timeout);
        assignment.OnInput(new PttInput(PttKey.JoystickButton(Wheel, 5), true, "Wheel"));

        Assert.Equal(PttAssignmentStatus.TimedOut, (await assignment.Result).Status);
    }
}
