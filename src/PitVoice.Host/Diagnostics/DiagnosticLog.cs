using System.Globalization;
using System.Reflection;
using System.Runtime.InteropServices;
using Serilog;
using Serilog.Core;

namespace PitVoice.Host.Diagnostics;

/// <summary>Where diagnostics live: <c>%LocalAppData%\PitVoice\logs</c> and <c>%LocalAppData%\PitVoice\diagnostics</c>.</summary>
public static class DiagnosticPaths
{
    public static string Root =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PitVoice");

    /// <summary>Log files and crash reports.</summary>
    public static string LogsDirectory => Path.Combine(Root, "logs");

    /// <summary>Diagnostics archives and the crash-seen mark.</summary>
    public static string DiagnosticsDirectory => Path.Combine(Root, "diagnostics");

    /// <summary>PitVoice version without the commit hash, e.g. <c>0.0.12</c>.</summary>
    public static string Version { get; } =
        typeof(DiagnosticPaths).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion.Split('+')[0]
        ?? typeof(DiagnosticPaths).Assembly.GetName().Version?.ToString(3)
        ?? "unknown";

    /// <summary>Windows version and architecture, e.g. <c>Microsoft Windows 10.0.26200 (X64)</c>.</summary>
    public static string OperatingSystem { get; } =
        $"{RuntimeInformation.OSDescription} ({RuntimeInformation.OSArchitecture})";
}

/// <summary>
/// The application log (design decision 1): a text file per day and per 10 MB, the last 7 files kept, written by a
/// background worker so the pipeline never waits for the disk. When the directory cannot be created the log writes
/// nowhere and the host works as usual (spec: Log directory not writable).
/// </summary>
public static class DiagnosticLog
{
    /// <summary>Serilog adds the date (and <c>_001</c> for the next file of the same day) before the extension.</summary>
    public const string FileNameTemplate = "pitvoice-.log";

    /// <summary>Files of the log, for cleanup and the archive; crash reports (<c>crash-*.log</c>) do not match it.</summary>
    public const string FileSearchPattern = "pitvoice-*.log";

    public const long FileSizeLimit = 10L * 1024 * 1024;

    public const int RetainedFiles = 7;

    public const string OutputTemplate = "{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{Level:u3}] {Message:lj}{NewLine}{Exception}";

    /// <summary>Records waiting for the disk; when the buffer is full new records are dropped, the pipeline never waits.</summary>
    private const int BufferSize = 10_000;

    /// <summary>A logger writing to <paramref name="directory"/> (default <see cref="DiagnosticPaths.LogsDirectory"/>); dispose it to flush.</summary>
    public static Logger Create(string? directory = null)
    {
        directory ??= DiagnosticPaths.LogsDirectory;
        try
        {
            Directory.CreateDirectory(directory);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or NotSupportedException or ArgumentException)
        {
            return new LoggerConfiguration().CreateLogger();
        }

        return new LoggerConfiguration()
            .MinimumLevel.Information()
            .WriteTo.Async(
                sink => sink.File(
                    Path.Combine(directory, FileNameTemplate),
                    outputTemplate: OutputTemplate,
                    formatProvider: CultureInfo.InvariantCulture,
                    fileSizeLimitBytes: FileSizeLimit,
                    rollOnFileSizeLimit: true,
                    rollingInterval: RollingInterval.Day,
                    retainedFileCountLimit: RetainedFiles),
                bufferSize: BufferSize,
                blockWhenFull: false)
            .CreateLogger();
    }
}
