using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using PitVoice.Core.Settings;

namespace PitVoice.App.Overlay;

/// <summary>Window styles, z-order and monitors for the indicator (design D3, D6).</summary>
internal static unsafe partial class OverlayNative
{
    public const int WmMouseActivate = 0x0021;
    public const int MaNoActivate = 3;

    private const int GwlExStyle = -20;
    private const long WsExTransparent = 0x00000020;
    private const long WsExToolWindow = 0x00000080;
    private const long WsExLayered = 0x00080000;
    private const long WsExNoActivate = 0x08000000;
    private const uint SwpNoSize = 0x0001;
    private const uint SwpNoMove = 0x0002;
    private const uint SwpNoActivate = 0x0010;
    private const uint MonitorInfoPrimary = 1;
    private const int MdtEffectiveDpi = 0;
    private const int DwmwaSystemBackdropType = 38;
    private const int DwmsbtNone = 1;
    private static readonly nint HwndTopmost = -1;

    /// <summary>No taskbar button, no activation, per-pixel alpha; clicks pass through unless <paramref name="clickThrough"/> is false.</summary>
    public static void SetOverlayStyles(nint window, bool clickThrough)
    {
        var style = (long)GetWindowLongPtr(window, GwlExStyle) | WsExToolWindow | WsExNoActivate | WsExLayered;
        style = clickThrough ? style | WsExTransparent : style & ~WsExTransparent;
        SetWindowLongPtr(window, GwlExStyle, (nint)style);
    }

    /// <summary>
    /// Removes the system backdrop (Mica) and the extended frame WPF-UI gives every window of the app when it applies a
    /// theme; DWM would draw it as a grey rectangle under the transparent indicator.
    /// </summary>
    public static void ClearBackdrop(nint window)
    {
        var none = DwmsbtNone;
        DwmSetWindowAttribute(window, DwmwaSystemBackdropType, &none, sizeof(int));
        var margins = default(Margins);
        DwmExtendFrameIntoClientArea(window, &margins);
    }

    /// <summary>Puts the window on top again without activating it: a borderless game is topmost too.</summary>
    public static void BringToTop(nint window) => SetWindowPos(window, HwndTopmost, 0, 0, 0, 0, SwpNoMove | SwpNoSize | SwpNoActivate);

    /// <summary>Moves the window (top left corner in pixels), keeps it on top and inactive.</summary>
    public static void MoveTo(nint window, int left, int top) => SetWindowPos(window, HwndTopmost, left, top, 0, 0, SwpNoSize | SwpNoActivate);

    public static PixelRect GetRect(nint window) =>
        GetWindowRect(window, out var rect) ? new PixelRect(rect.Left, rect.Top, rect.Right, rect.Bottom) : default;

    public static ScreenPoint CursorPosition() => GetCursorPos(out var point) ? new ScreenPoint(point.X, point.Y) : default;

    /// <summary>Every monitor with its work area and effective DPI.</summary>
    public static IReadOnlyList<MonitorArea> GetMonitors()
    {
        var monitors = new List<nint>();
        var handle = GCHandle.Alloc(monitors);
        try
        {
            EnumDisplayMonitors(0, 0, &Collect, GCHandle.ToIntPtr(handle));
        }
        finally
        {
            handle.Free();
        }

        var result = new List<MonitorArea>();
        foreach (var monitor in monitors)
        {
            var info = new MonitorInfo { Size = sizeof(MonitorInfo) };
            if (!GetMonitorInfo(monitor, ref info))
            {
                continue;
            }

            var scale = GetDpiForMonitor(monitor, MdtEffectiveDpi, out var dpi, out _) == 0 && dpi > 0 ? dpi / 96.0 : 1.0;
            result.Add(new MonitorArea(
                new PixelRect(info.Monitor.Left, info.Monitor.Top, info.Monitor.Right, info.Monitor.Bottom),
                new PixelRect(info.Work.Left, info.Work.Top, info.Work.Right, info.Work.Bottom),
                scale,
                (info.Flags & MonitorInfoPrimary) != 0));
        }

        return result;
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
    private static int Collect(nint monitor, nint hdc, Rect* clip, nint data)
    {
        ((List<nint>)GCHandle.FromIntPtr(data).Target!).Add(monitor);
        return 1;
    }

    [LibraryImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
    private static partial nint GetWindowLongPtr(nint window, int index);

    [LibraryImport("user32.dll", EntryPoint = "SetWindowLongPtrW")]
    private static partial nint SetWindowLongPtr(nint window, int index, nint value);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SetWindowPos(nint window, nint insertAfter, int x, int y, int width, int height, uint flags);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetWindowRect(nint window, out Rect rect);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetCursorPos(out Point point);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool EnumDisplayMonitors(nint hdc, nint clip, delegate* unmanaged[Stdcall]<nint, nint, Rect*, nint, int> callback, nint data);

    [LibraryImport("user32.dll", EntryPoint = "GetMonitorInfoW")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetMonitorInfo(nint monitor, ref MonitorInfo info);

    [LibraryImport("dwmapi.dll")]
    private static partial int DwmSetWindowAttribute(nint window, int attribute, void* value, int size);

    [LibraryImport("dwmapi.dll")]
    private static partial int DwmExtendFrameIntoClientArea(nint window, Margins* margins);

    [LibraryImport("shcore.dll")]
    private static partial int GetDpiForMonitor(nint monitor, int type, out uint dpiX, out uint dpiY);

    [StructLayout(LayoutKind.Sequential)]
    private struct Rect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Margins
    {
        public int Left;
        public int Right;
        public int Top;
        public int Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Point
    {
        public int X;
        public int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MonitorInfo
    {
        public int Size;
        public Rect Monitor;
        public Rect Work;
        public uint Flags;
    }
}
