using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using PitVoice.Core.Pit;

namespace PitVoice.IRacing.Diagnostics;

/// <summary>Result of looking at the game process.</summary>
public enum GameElevation
{
    /// <summary>The game runs with the same or lower rights.</summary>
    NotElevated,

    /// <summary>The game runs with higher rights: broadcast messages from this process do not reach it.</summary>
    Elevated,

    /// <summary>No game process, or it could not be inspected.</summary>
    Unknown,
}

/// <summary>Tells whether the game process runs with higher rights than this one.</summary>
public interface IGameElevationProbe
{
    GameElevation Check();
}

/// <summary>
/// Passes everything to the inner log and, when not a single command was confirmed, checks whether the game runs
/// elevated and writes a hint (design decision 11). Never asks for elevation and never throws because of the check.
/// </summary>
public sealed class ElevationHintLog(IExecutionLog inner, IGameElevationProbe probe) : IExecutionLog
{
    public const string Hint =
        "iRacing seems to run as administrator while PitVoice does not, so Windows blocks pit commands from reaching it. " +
        "Start both normally (recommended) or both as administrator.";

    public void ApplyFailed(ApplyFailureReport report)
    {
        ArgumentNullException.ThrowIfNull(report);
        inner.ApplyFailed(report);
        if (report.Outcome != ExecutionOutcome.NotApplied || !report.NothingConfirmed || report.Actual is null)
        {
            return;
        }

        GameElevation elevation;
        try
        {
            elevation = probe.Check();
        }
        catch (Exception e) when (e is InvalidOperationException or System.ComponentModel.Win32Exception or UnauthorizedAccessException or NotSupportedException)
        {
            elevation = GameElevation.Unknown;
        }

        if (elevation == GameElevation.Elevated)
        {
            inner.Warning(Hint);
        }
    }

    public void Warning(string message) => inner.Warning(message);
}

/// <summary>
/// Opens the game process with <c>PROCESS_QUERY_LIMITED_INFORMATION</c> and tries to open its token. Access denied on the
/// token of a process we can see means it runs at a higher integrity level.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed partial class WindowsGameElevationProbe : IGameElevationProbe
{
    public static IReadOnlyList<string> GameProcessNames { get; } = ["iRacingSim64DX11", "iRacingSim64"];

    private const uint ProcessQueryLimitedInformation = 0x1000;
    private const uint TokenQuery = 0x0008;
    private const int ErrorAccessDenied = 5;

    public GameElevation Check()
    {
        foreach (var name in GameProcessNames)
        {
            foreach (var process in Process.GetProcessesByName(name))
            {
                using (process)
                {
                    var result = Check(process.Id);
                    if (result != GameElevation.Unknown)
                    {
                        return result;
                    }
                }
            }
        }

        return GameElevation.Unknown;
    }

    private static GameElevation Check(int processId)
    {
        var handle = OpenProcess(ProcessQueryLimitedInformation, false, (uint)processId);
        if (handle == 0)
        {
            return GameElevation.Unknown;
        }

        try
        {
            if (OpenProcessToken(handle, TokenQuery, out var token))
            {
                CloseHandle(token);
                return GameElevation.NotElevated;
            }

            return Marshal.GetLastPInvokeError() == ErrorAccessDenied ? GameElevation.Elevated : GameElevation.Unknown;
        }
        finally
        {
            CloseHandle(handle);
        }
    }

    [LibraryImport("kernel32.dll", SetLastError = true)]
    private static partial nint OpenProcess(uint access, [MarshalAs(UnmanagedType.Bool)] bool inheritHandle, uint processId);

    [LibraryImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool OpenProcessToken(nint process, uint access, out nint token);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool CloseHandle(nint handle);
}
