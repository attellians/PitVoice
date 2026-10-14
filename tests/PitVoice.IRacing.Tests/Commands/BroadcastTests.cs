using PitVoice.Core.Pit;
using PitVoice.IRacing.Commands;
using Xunit;

namespace PitVoice.IRacing.Tests.Commands;

public class BroadcastTests
{
    // Codes of irsdk_PitCommandMode as checked against irsdk_defines.h (design decision 3).
    [Theory]
    [InlineData(PitCommand.Clear, 0)]
    [InlineData(PitCommand.WS, 1)]
    [InlineData(PitCommand.Fuel, 2)]
    [InlineData(PitCommand.LF, 3)]
    [InlineData(PitCommand.RF, 4)]
    [InlineData(PitCommand.LR, 5)]
    [InlineData(PitCommand.RR, 6)]
    [InlineData(PitCommand.ClearTires, 7)]
    [InlineData(PitCommand.FR, 8)]
    [InlineData(PitCommand.ClearWS, 9)]
    [InlineData(PitCommand.ClearFR, 10)]
    [InlineData(PitCommand.ClearFuel, 11)]
    public void Command_codes_and_packing(PitCommand command, int code)
    {
        Assert.Equal(code, (int)command);

        var (wParam, lParam) = BroadcastPitCommandSink.Pack(command, 0);

        Assert.Equal((nuint)(uint)(9 | (code << 16)), wParam);
        Assert.Equal(0, lParam);
    }

    [Theory]
    [InlineData(PitCommand.Fuel, 38)]
    [InlineData(PitCommand.Fuel, 200)]
    [InlineData(PitCommand.LF, 170)]
    public void Parameter_goes_to_lparam(PitCommand command, int parameter)
    {
        var (wParam, lParam) = BroadcastPitCommandSink.Pack(command, parameter);

        Assert.Equal(BroadcastPitCommandSink.MakeLong(9, (int)command), wParam);
        Assert.Equal(parameter, lParam);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(65536)]
    public void Parameter_out_of_range(int parameter)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => BroadcastPitCommandSink.Pack(PitCommand.Fuel, parameter));
    }

    [Fact]
    public void Message_id_is_registered_only_on_windows()
    {
        if (OperatingSystem.IsWindows())
        {
            // Registering a message name is harmless; nothing is sent here.
            _ = new BroadcastPitCommandSink();
        }
        else
        {
            Assert.Throws<PlatformNotSupportedException>(() => new BroadcastPitCommandSink());
        }
    }
}
