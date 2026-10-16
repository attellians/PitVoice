using System.Globalization;
using System.Text;
using Serilog;
using Serilog.Events;

namespace PitVoice.Host.Diagnostics;

/// <summary>
/// Unhandled exceptions (design decisions 3 and 4). A report is written straight to <c>crash-&lt;time&gt;.log</c> next to the
/// log, without Serilog, so it survives the end of the process; then the same exception goes to the log, and before a
/// fatal end the log is flushed for at most <see cref="FlushTimeout"/>. The last <see cref="KeptReports"/> reports are
/// kept. The newest fatal report not shown yet is offered on the main page until <see cref="Acknowledge"/>.
/// </summary>
public sealed class CrashReporter
{
    public const int KeptReports = 10;

    /// <summary>Crash reports; the log rotation (<c>pitvoice-*.log</c>) never touches them.</summary>
    public const string FileSearchPattern = "crash-*.log";

    /// <summary>File in the diagnostics directory with the name of the last report the user saw.</summary>
    public const string SeenMarkName = "crash-seen";

    public const string FatalLine = "fatal: true";

    public static readonly TimeSpan FlushTimeout = TimeSpan.FromSeconds(3);

    private readonly ILogger _log;
    private readonly TimeProvider _time;
    private readonly Lock _lock = new();
    private int _flushed;

    /// <param name="log">The application log; it is disposed (flushed) on a fatal crash.</param>
    /// <param name="logsDirectory">Where reports go; default <see cref="DiagnosticPaths.LogsDirectory"/>.</param>
    /// <param name="diagnosticsDirectory">Where the seen mark goes; default <see cref="DiagnosticPaths.DiagnosticsDirectory"/>.</param>
    /// <param name="time">Clock for the report time.</param>
    public CrashReporter(ILogger log, string? logsDirectory = null, string? diagnosticsDirectory = null, TimeProvider? time = null)
    {
        ArgumentNullException.ThrowIfNull(log);
        _log = log;
        _time = time ?? TimeProvider.System;
        LogsDirectory = logsDirectory ?? DiagnosticPaths.LogsDirectory;
        DiagnosticsDirectory = diagnosticsDirectory ?? DiagnosticPaths.DiagnosticsDirectory;
    }

    public string LogsDirectory { get; }

    public string DiagnosticsDirectory { get; }

    /// <summary>A fatal report was written (its path, or null when it could not be); <c>run</c> prints the path.</summary>
    public event Action<string?>? FatalReported;

    /// <summary>
    /// Writes the report, logs the exception and, when <paramref name="fatal"/>, flushes the log. Never throws.
    /// Returns the report path, or null when it could not be written.
    /// </summary>
    /// <param name="exception">The unhandled exception.</param>
    /// <param name="source">Where it came from: the window thread, a background thread, an unobserved task.</param>
    /// <param name="fatal">True when the process ends after it.</param>
    public string? Report(Exception exception, string source, bool fatal)
    {
        string? path;
        lock (_lock)
        {
            path = Write(exception, source, fatal);
        }

        try
        {
            _log.Write(
                fatal ? LogEventLevel.Fatal : LogEventLevel.Error,
                exception,
                "Unhandled exception ({Source}, fatal: {Fatal}), report {Report}",
                source,
                fatal,
                path is null ? "not written" : Path.GetFileName(path));
        }
#pragma warning disable CA1031 // Spec: a report that cannot be written must not get in the way of the crash handling.
        catch (Exception)
        {
        }

        if (fatal)
        {
            Flush();
            try
            {
                FatalReported?.Invoke(path);
            }
            catch (Exception)
            {
            }
        }
#pragma warning restore CA1031

        return path;
    }

    /// <summary>Unhandled exceptions of background threads (fatal) and unobserved tasks (not fatal, the work goes on).</summary>
    public void Install()
    {
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
            Report(e.ExceptionObject as Exception ?? new InvalidOperationException($"non-exception object thrown: {e.ExceptionObject}"), ThreadSource(), fatal: true);
        TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            Report(e.Exception, "unobserved task", fatal: false);
            e.SetObserved();
        };
    }

    /// <summary>The newest fatal report written after the last one the user saw, or null.</summary>
    public string? PendingFatal()
    {
        var seen = ReadSeenMark();
        foreach (var report in Reports().Reverse())
        {
            if (seen is not null && string.CompareOrdinal(Path.GetFileName(report), seen) <= 0)
            {
                return null;
            }

            if (IsFatal(report))
            {
                return report;
            }
        }

        return null;
    }

    /// <summary>Every report so far counts as seen; the offer comes back only after the next fatal crash.</summary>
    public void Acknowledge()
    {
        if (Reports().LastOrDefault() is not { } newest)
        {
            return;
        }

        try
        {
            Directory.CreateDirectory(DiagnosticsDirectory);
            File.WriteAllText(Path.Combine(DiagnosticsDirectory, SeenMarkName), Path.GetFileName(newest));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // The offer shows again at the next start; nothing else breaks.
        }
    }

    /// <summary>Reports oldest first (the name holds the time).</summary>
    public IReadOnlyList<string> Reports()
    {
        try
        {
            return Directory.Exists(LogsDirectory)
                ? [.. Directory.GetFiles(LogsDirectory, FileSearchPattern).Order(StringComparer.Ordinal)]
                : [];
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return [];
        }
    }

    internal static string ThreadSource()
    {
        var thread = Thread.CurrentThread;
        return thread.Name is { Length: > 0 } name
            ? string.Create(CultureInfo.InvariantCulture, $"thread {thread.ManagedThreadId} ({name})")
            : string.Create(CultureInfo.InvariantCulture, $"thread {thread.ManagedThreadId}");
    }

    private string? Write(Exception exception, string source, bool fatal)
    {
        try
        {
            Directory.CreateDirectory(LogsDirectory);
            var now = _time.GetLocalNow();
            var name = string.Create(CultureInfo.InvariantCulture, $"crash-{now:yyyyMMdd-HHmmss-fff}");
            var path = Path.Combine(LogsDirectory, name + ".log");
            for (var i = 2; File.Exists(path); i++)
            {
                path = Path.Combine(LogsDirectory, string.Create(CultureInfo.InvariantCulture, $"{name}-{i}.log"));
            }

            var text = new StringBuilder()
                .AppendLine("PitVoice crash report")
                .AppendLine(CultureInfo.InvariantCulture, $"time: {now:yyyy-MM-dd HH:mm:ss.fff zzz}")
                .AppendLine(CultureInfo.InvariantCulture, $"version: {DiagnosticPaths.Version}")
                .AppendLine(CultureInfo.InvariantCulture, $"os: {DiagnosticPaths.OperatingSystem}")
                .AppendLine(CultureInfo.InvariantCulture, $"source: {source}")
                .AppendLine(fatal ? FatalLine : "fatal: false")
                .AppendLine()
                .AppendLine(exception.ToString())
                .ToString();
            File.WriteAllText(path, text, Encoding.UTF8);
            Prune();
            return path;
        }
#pragma warning disable CA1031 // Spec: Logs directory not writable — the crash is handled the same way without a report.
        catch (Exception)
#pragma warning restore CA1031
        {
            return null;
        }
    }

    private void Prune()
    {
        foreach (var old in Reports().Reverse().Skip(KeptReports))
        {
            try
            {
                File.Delete(old);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                // Removed next time.
            }
        }
    }

    /// <summary>Closes the log so the background writer puts the last records on disk; waits no longer than <see cref="FlushTimeout"/>.</summary>
    private void Flush()
    {
        if (Interlocked.Exchange(ref _flushed, 1) == 1 || _log is not IDisposable disposable)
        {
            return;
        }

        try
        {
            Task.Run(disposable.Dispose).Wait(FlushTimeout);
        }
#pragma warning disable CA1031 // The process ends anyway.
        catch (Exception)
#pragma warning restore CA1031
        {
        }
    }

    private string? ReadSeenMark()
    {
        try
        {
            var path = Path.Combine(DiagnosticsDirectory, SeenMarkName);
            return File.Exists(path) ? File.ReadAllText(path).Trim() : null;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private static bool IsFatal(string report)
    {
        try
        {
            return File.ReadLines(report).Take(10).Any(l => string.Equals(l.Trim(), FatalLine, StringComparison.Ordinal));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }
}
