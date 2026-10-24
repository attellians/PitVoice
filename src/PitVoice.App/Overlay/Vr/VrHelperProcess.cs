using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading.Channels;
using PitVoice.Core.Vr;

namespace PitVoice.App.Overlay.Vr;

/// <summary>SteamVR runs when its server process does.</summary>
internal sealed class SteamVrProbe : ISteamVrProbe
{
    public int? ServerProcessId()
    {
        var processes = Process.GetProcessesByName("vrserver");
        try
        {
            return processes.Length == 0 ? null : processes.Min(p => p.Id);
        }
        finally
        {
            foreach (var process in processes)
            {
                process.Dispose();
            }
        }
    }
}

/// <summary>
/// Starts <c>PitVoice.VrOverlay.exe</c> next to the app with redirected streams, inside a job object that kills it when the
/// app ends in any way (add-vr-overlay D6).
/// </summary>
internal sealed class VrHelperLauncher : IVrHelperLauncher, IDisposable
{
    public const string FileName = "PitVoice.VrOverlay.exe";

    private readonly Lazy<KillOnCloseJob> _job = new(KillOnCloseJob.Create);

    public IVrHelperProcess Start(Action<string> line, Action<int> exited)
    {
        var path = Path.Combine(AppContext.BaseDirectory, FileName);
        if (!File.Exists(path))
        {
            throw new FileNotFoundException("the VR helper is not installed", path);
        }

        var process = new Process
        {
            StartInfo = new ProcessStartInfo(path)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                WorkingDirectory = AppContext.BaseDirectory,
            },
            EnableRaisingEvents = true,
        };
        var helper = new VrHelperProcess(process, line, exited);
        try
        {
            process.Start();
        }
        catch
        {
            process.Dispose();
            throw;
        }

        _job.Value.Assign(process);
        helper.Begin();
        return helper;
    }

    public void Dispose()
    {
        if (_job.IsValueCreated)
        {
            _job.Value.Dispose();
        }
    }
}

/// <summary>A started helper: messages go out through a queue on their own task, so a hung helper never blocks the app.</summary>
internal sealed class VrHelperProcess : IVrHelperProcess
{
    /// <summary>How long the helper gets to remove its overlay after its input is closed.</summary>
    private static readonly TimeSpan ExitWait = TimeSpan.FromSeconds(1);

    private readonly Process _process;
    private readonly Channel<VrMessage> _outgoing = Channel.CreateBounded<VrMessage>(
        new BoundedChannelOptions(16) { FullMode = BoundedChannelFullMode.DropOldest, SingleReader = true });

    private readonly Action<int> _exited;
    private int _disposed;

    /// <summary>Takes a process that is not started yet, so no line and no exit is missed.</summary>
    public VrHelperProcess(Process process, Action<string> line, Action<int> exited)
    {
        _process = process;
        _exited = exited;
        _process.OutputDataReceived += (_, e) =>
        {
            if (e.Data is not null)
            {
                line(e.Data);
            }
        };

        // The helper's own diagnostics; the app keeps nothing of them.
        _process.ErrorDataReceived += (_, _) => { };
        _process.Exited += (_, _) => OnExited();
    }

    /// <summary>Starts reading the output and writing the messages of the started process.</summary>
    public void Begin()
    {
        _process.BeginOutputReadLine();
        _process.BeginErrorReadLine();
        _ = Task.Run(WriteAsync);
    }

    public void Send(VrMessage message) => _outgoing.Writer.TryWrite(message);

    public void Kill()
    {
        try
        {
            _process.Kill();
        }
        catch (InvalidOperationException)
        {
            // Already gone.
        }
        catch (Win32Exception)
        {
            // Exiting right now.
        }
    }

    /// <summary>Closes the input, so the helper removes the overlay and exits; kills it if it does not within a second.</summary>
    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        _outgoing.Writer.TryComplete();
        _ = Task.Run(() =>
        {
            try
            {
                if (!_process.WaitForExit(ExitWait))
                {
                    Kill();
                }
            }
            catch (InvalidOperationException)
            {
            }
            finally
            {
                _process.Dispose();
            }
        });
    }

    private async Task WriteAsync()
    {
        var input = _process.StandardInput.BaseStream;
        try
        {
            await foreach (var message in _outgoing.Reader.ReadAllAsync().ConfigureAwait(false))
            {
                VrProtocol.Write(input, message);
            }
        }
        catch (IOException)
        {
            // The helper is gone; its exit is reported by Exited.
        }
        catch (ObjectDisposedException)
        {
        }
        finally
        {
            try
            {
                input.Dispose();
            }
            catch (IOException)
            {
            }
        }
    }

    private void OnExited()
    {
        int code;
        try
        {
            // Lets the last lines of the output arrive before the exit is reported.
            _process.WaitForExit();
            code = _process.ExitCode;
        }
        catch (InvalidOperationException)
        {
            code = -1;
        }

        _exited(code);
    }
}

/// <summary>A job object with JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE: its processes end when the app's handle closes.</summary>
internal sealed partial class KillOnCloseJob : IDisposable
{
    private const int ExtendedLimitInformation = 9;
    private const uint KillOnJobClose = 0x2000;

    private readonly nint _handle;

    private KillOnCloseJob(nint handle) => _handle = handle;

    public static KillOnCloseJob Create()
    {
        var handle = CreateJobObject(0, null);
        if (handle == 0)
        {
            throw new Win32Exception(Marshal.GetLastPInvokeError(), "CreateJobObject");
        }

        var info = new ExtendedLimit { LimitFlags = KillOnJobClose };
        if (!SetInformationJobObject(handle, ExtendedLimitInformation, ref info, (uint)Marshal.SizeOf<ExtendedLimit>()))
        {
            var error = Marshal.GetLastPInvokeError();
            CloseHandle(handle);
            throw new Win32Exception(error, "SetInformationJobObject");
        }

        return new KillOnCloseJob(handle);
    }

    /// <summary>A process that cannot join the job keeps running; the closed input still ends it with the app.</summary>
    public void Assign(Process process) => AssignProcessToJobObject(_handle, process.Handle);

    public void Dispose() => CloseHandle(_handle);

    [LibraryImport("kernel32.dll", EntryPoint = "CreateJobObjectW", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    private static partial nint CreateJobObject(nint attributes, string? name);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SetInformationJobObject(nint job, int infoClass, ref ExtendedLimit info, uint length);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool AssignProcessToJobObject(nint job, nint process);

    [LibraryImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool CloseHandle(nint handle);

    /// <summary>JOBOBJECT_EXTENDED_LIMIT_INFORMATION with its JOBOBJECT_BASIC_LIMIT_INFORMATION and IO_COUNTERS inline.</summary>
    [StructLayout(LayoutKind.Sequential)]
    private struct ExtendedLimit
    {
        public long PerProcessUserTimeLimit;
        public long PerJobUserTimeLimit;
        public uint LimitFlags;
        public nuint MinimumWorkingSetSize;
        public nuint MaximumWorkingSetSize;
        public uint ActiveProcessLimit;
        public nuint Affinity;
        public uint PriorityClass;
        public uint SchedulingClass;
        public ulong ReadOperationCount;
        public ulong WriteOperationCount;
        public ulong OtherOperationCount;
        public ulong ReadTransferCount;
        public ulong WriteTransferCount;
        public ulong OtherTransferCount;
        public nuint ProcessMemoryLimit;
        public nuint JobMemoryLimit;
        public nuint PeakProcessMemoryUsed;
        public nuint PeakJobMemoryUsed;
    }
}
