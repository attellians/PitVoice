using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using PitVoice.Core.Pit;

namespace PitVoice.IRacing.Commands;

/// <summary>
/// Sends pit commands the way the SDK documents it: a registered window message broadcast to all top-level windows
/// (design decision 3). No process access, no key emulation.
/// </summary>
public sealed partial class BroadcastPitCommandSink : IPitCommandSink
{
    public const string MessageName = "IRSDK_BROADCASTMSG";

    /// <summary><c>irsdk_BroadcastPitCommand</c>.</summary>
    public const int PitCommandMessage = 9;

    private const nint HwndBroadcast = 0xFFFF;

    private readonly uint _message;

    /// <exception cref="PlatformNotSupportedException">Not on Windows.</exception>
    public BroadcastPitCommandSink()
    {
        if (!OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException("iRacing pit commands are only available on Windows.");
        }

        _message = RegisterWindowMessage(MessageName);
        if (_message == 0)
        {
            throw new Win32Exception(Marshal.GetLastPInvokeError(), "RegisterWindowMessage failed.");
        }
    }

    public void Send(PitCommand command, int parameter)
    {
        if (!OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException("iRacing pit commands are only available on Windows.");
        }

        var (wParam, lParam) = Pack(command, parameter);
        if (!SendNotifyMessage(HwndBroadcast, _message, wParam, lParam))
        {
            throw new Win32Exception(Marshal.GetLastPInvokeError(), "SendNotifyMessage failed.");
        }
    }

    /// <summary>
    /// <c>wParam = MAKELONG(irsdk_BroadcastPitCommand, command)</c>; <c>lParam</c> carries the parameter in its low word.
    /// </summary>
    internal static (nuint WParam, nint LParam) Pack(PitCommand command, int parameter)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(parameter);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(parameter, ushort.MaxValue);
        return (MakeLong(PitCommandMessage, (int)command), parameter);
    }

    internal static nuint MakeLong(int low, int high) => (nuint)(uint)((low & 0xFFFF) | ((high & 0xFFFF) << 16));

    [SupportedOSPlatform("windows")]
    [LibraryImport("user32.dll", EntryPoint = "RegisterWindowMessageW", StringMarshalling = StringMarshalling.Utf16, SetLastError = true)]
    private static partial uint RegisterWindowMessage(string name);

    [SupportedOSPlatform("windows")]
    [LibraryImport("user32.dll", EntryPoint = "SendNotifyMessageW", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SendNotifyMessage(nint hWnd, uint msg, nuint wParam, nint lParam);
}
