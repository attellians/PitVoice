using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Threading.Channels;
using PitVoice.Core.Ptt;

namespace PitVoice.Input;

/// <summary>
/// Keyboard keys through a low-level hook (<c>WH_KEYBOARD_LL</c>, design decision 6) installed on a dedicated thread with
/// its own message loop. The hook callback only queues the key and always calls <c>CallNextHookEx</c>, so the key still
/// reaches the focused window and the callback stays far below <c>LowLevelHooksTimeout</c>. Key names are looked up and
/// <see cref="Input"/> is raised on a separate dispatcher task. Windows does not deliver keys typed into a window of a
/// process with higher rights (UIPI).
/// </summary>
[SupportedOSPlatform("windows")]
public sealed partial class KeyboardHookSource : IPttSource
{
    private const int WhKeyboardLl = 13;
    private const int WmKeyDown = 0x0100;
    private const int WmKeyUp = 0x0101;
    private const int WmSysKeyDown = 0x0104;
    private const int WmSysKeyUp = 0x0105;
    private const uint WmQuit = 0x0012;
    private const uint LlkhfExtended = 0x01;
    private static readonly TimeSpan StopTimeout = TimeSpan.FromSeconds(2);

    private readonly Channel<RawKey> _queue = Channel.CreateUnbounded<RawKey>(new UnboundedChannelOptions { SingleReader = true, SingleWriter = true });
    private readonly Dictionary<int, string> _names = [];
    private readonly ManualResetEventSlim _started = new();
    private readonly HookProc _proc;
    private Thread? _thread;
    private Task _dispatcher = Task.CompletedTask;
    private uint _threadId;
    private string? _startError;

    public KeyboardHookSource()
    {
        _proc = OnHook;
    }

    private delegate nint HookProc(int code, nint wParam, nint lParam);

    public event Action<PttInput>? Input;

    /// <summary>Never raised: the keyboard is always there.</summary>
    public event Action<PttDeviceChange>? DeviceChanged
    {
        add { }
        remove { }
    }

    public string? Start()
    {
        if (_thread is not null)
        {
            return _startError;
        }

        _dispatcher = Task.Run(DispatchAsync);
        _thread = new Thread(Run) { IsBackground = true, Name = "PitVoice keyboard hook" };
        _thread.Start();
        _started.Wait();
        return _startError;
    }

    public void Dispose()
    {
        if (_thread is { IsAlive: true } thread)
        {
            PostThreadMessageW(_threadId, WmQuit, 0, 0);
            thread.Join(StopTimeout);
        }

        _queue.Writer.TryComplete();
        _dispatcher.Wait(StopTimeout);
        _started.Dispose();
    }

    private void Run()
    {
        _threadId = GetCurrentThreadId();
        var hook = SetWindowsHookExW(WhKeyboardLl, Marshal.GetFunctionPointerForDelegate(_proc), GetModuleHandleW(null), 0);
        if (hook == 0)
        {
            _startError = $"the keyboard hook could not be installed (error {Marshal.GetLastPInvokeError()})";
            _started.Set();
            return;
        }

        _started.Set();
        try
        {
            while (GetMessageW(out _, 0, 0, 0) > 0)
            {
            }
        }
        finally
        {
            UnhookWindowsHookEx(hook);
        }
    }

    private unsafe nint OnHook(int code, nint wParam, nint lParam)
    {
        if (code >= 0)
        {
            var data = (KbdLlHookStruct*)lParam;
            switch ((int)wParam)
            {
                case WmKeyDown or WmSysKeyDown:
                    _queue.Writer.TryWrite(new RawKey((int)data->VkCode, data->ScanCode, (data->Flags & LlkhfExtended) != 0, Down: true));
                    break;
                case WmKeyUp or WmSysKeyUp:
                    _queue.Writer.TryWrite(new RawKey((int)data->VkCode, data->ScanCode, (data->Flags & LlkhfExtended) != 0, Down: false));
                    break;
            }
        }

        return CallNextHookEx(0, code, wParam, lParam);
    }

    private async Task DispatchAsync()
    {
        await foreach (var key in _queue.Reader.ReadAllAsync().ConfigureAwait(false))
        {
            Input?.Invoke(new PttInput(PttKey.KeyboardKey(key.VirtualKey), key.Down, Name(key)));
        }
    }

    private unsafe string Name(RawKey key)
    {
        if (_names.TryGetValue(key.VirtualKey, out var cached))
        {
            return cached;
        }

        var buffer = stackalloc char[64];
        var lParam = (int)((key.ScanCode << 16) | (key.Extended ? 1u << 24 : 0));
        var length = GetKeyNameTextW(lParam, buffer, 64);
        var name = length > 0 ? new string(buffer, 0, length) : $"0x{key.VirtualKey:X2}";
        _names[key.VirtualKey] = name;
        return name;
    }

    private readonly record struct RawKey(int VirtualKey, uint ScanCode, bool Extended, bool Down);

    [StructLayout(LayoutKind.Sequential)]
    private struct KbdLlHookStruct
    {
        public uint VkCode;
        public uint ScanCode;
        public uint Flags;
        public uint Time;
        public nuint ExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Msg
    {
        public nint Hwnd;
        public uint Message;
        public nint WParam;
        public nint LParam;
        public uint Time;
        public int PtX;
        public int PtY;
    }

    [LibraryImport("user32.dll", SetLastError = true)]
    private static partial nint SetWindowsHookExW(int idHook, nint lpfn, nint hmod, uint dwThreadId);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool UnhookWindowsHookEx(nint hhk);

    [LibraryImport("user32.dll")]
    private static partial nint CallNextHookEx(nint hhk, int nCode, nint wParam, nint lParam);

    [LibraryImport("user32.dll")]
    private static partial int GetMessageW(out Msg lpMsg, nint hWnd, uint wMsgFilterMin, uint wMsgFilterMax);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool PostThreadMessageW(uint idThread, uint msg, nint wParam, nint lParam);

    [LibraryImport("user32.dll")]
    private static unsafe partial int GetKeyNameTextW(int lParam, char* lpString, int cchSize);

    [LibraryImport("kernel32.dll", StringMarshalling = StringMarshalling.Utf16)]
    private static partial nint GetModuleHandleW(string? lpModuleName);

    [LibraryImport("kernel32.dll")]
    private static partial uint GetCurrentThreadId();
}
