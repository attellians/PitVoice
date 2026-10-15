using PitVoice.Core.Ptt;
using Xunit;

namespace PitVoice.Input.Tests;

/// <summary>A fact that needs Windows (native input APIs); skipped elsewhere.</summary>
internal sealed class WindowsFactAttribute : FactAttribute
{
    public WindowsFactAttribute()
    {
        if (!OperatingSystem.IsWindows())
        {
            Skip = "needs Windows";
        }
    }
}

public sealed class SystemLanguageTests
{
    [Theory]
    [InlineData(0x0419, "ru")] // ru-RU
    [InlineData(0x0819, "ru")] // ru-MD
    [InlineData(0x0409, "en")] // en-US
    [InlineData(0x0809, "en")] // en-GB
    [InlineData(0x0407, "en")] // de-DE: no pack, English
    [InlineData(0x0422, "en")] // uk-UA
    public void LangId_MapsToPackLanguage(int langId, string expected) =>
        Assert.Equal(expected, SystemLanguage.FromLangId((ushort)langId));

    [Fact]
    public void Detect_ReturnsAPackLanguage() => Assert.Contains(SystemLanguage.Detect(), new[] { "ru", "en" });
}

public sealed class SourceSmokeTests
{
    [WindowsFact]
    public void SdlJoystickSource_StartsAndStops()
    {
        var connected = new List<PttDeviceChange>();
        using (var source = new SdlJoystickSource())
        {
            source.DeviceChanged += connected.Add;
            Assert.Null(source.Start());
            Thread.Sleep(200);
        }

        Assert.All(connected, c => Assert.False(string.IsNullOrEmpty(c.Device)));
    }

    [WindowsFact]
    public void SdlJoystickSource_DisposeWithoutStart_DoesNotThrow()
    {
        using var source = new SdlJoystickSource();
    }

    [WindowsFact]
    public void SdlJoystickSource_ListDevices_DoesNotThrow()
    {
        var devices = SdlJoystickSource.ListDevices(out var error);

        Assert.Null(error);
        Assert.NotNull(devices);
    }

    [WindowsFact]
    public void KeyboardHook_InstallsAndRemoves()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        using (var source = new KeyboardHookSource())
        {
            Assert.Null(source.Start());
        }

        // A second hook after the first one was removed must install again.
        using var again = new KeyboardHookSource();
        Assert.Null(again.Start());
    }
}
