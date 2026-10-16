using Microsoft.Extensions.Time.Testing;
using PitVoice.Host.Diagnostics;
using Xunit;

namespace PitVoice.Host.Tests.Diagnostics;

/// <summary>Crash reports and the offer after a crash (design decisions 3–5, crash-reports spec).</summary>
public sealed class CrashReporterTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "pitvoice-crash-" + Guid.NewGuid().ToString("N"));
    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 10, 16, 18, 30, 0, TimeSpan.Zero));
    private readonly LogCapture _capture = new();

    public CrashReporterTests()
    {
        _time.SetLocalTimeZone(TimeZoneInfo.Utc);
    }

    private string Logs => Path.Combine(_root, "logs");

    private string Diagnostics => Path.Combine(_root, "diagnostics");

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    [Fact]
    public void Report_has_the_header_and_the_nested_exception_and_goes_to_the_log()
    {
        var reporter = Create();
        var exception = new InvalidOperationException("button handler broke", new IOException("disk is gone"));

        var path = reporter.Report(Thrown(exception), "window thread", fatal: true);

        Assert.Equal(Path.Combine(Logs, "crash-20261016-183000-000.log"), path);
        var text = File.ReadAllText(path!);
        Assert.StartsWith("PitVoice crash report", text, StringComparison.Ordinal);
        Assert.Contains("time: 2026-10-16 18:30:00.000 +00:00", text, StringComparison.Ordinal);
        Assert.Contains($"version: {DiagnosticPaths.Version}", text, StringComparison.Ordinal);
        Assert.Contains("os: ", text, StringComparison.Ordinal);
        Assert.Contains("source: window thread", text, StringComparison.Ordinal);
        Assert.Contains("fatal: true", text, StringComparison.Ordinal);
        Assert.Contains("System.InvalidOperationException: button handler broke", text, StringComparison.Ordinal);
        Assert.Contains("System.IO.IOException: disk is gone", text, StringComparison.Ordinal);
        Assert.Contains(nameof(Thrown), text, StringComparison.Ordinal);

        var line = Assert.Single(_capture.Lines);
        Assert.StartsWith("FTL Unhandled exception (\"window thread\", fatal: True), report \"crash-20261016-183000-000.log\"", line, StringComparison.Ordinal);
        Assert.Contains("button handler broke", line, StringComparison.Ordinal);
    }

    [Fact]
    public void Not_fatal_report_is_marked_and_does_not_close_the_log()
    {
        var reporter = Create();
        using var log = _capture.CreateLogger();
        var other = new CrashReporter(log, Logs, Diagnostics, _time);

        var path = other.Report(new InvalidOperationException("task failed"), "unobserved task", fatal: false);

        Assert.Contains("fatal: false", File.ReadAllText(path!), StringComparison.Ordinal);
        log.Information("still writing");
        Assert.Contains(_capture.Lines, l => l.Contains("still writing", StringComparison.Ordinal));
        Assert.Null(reporter.PendingFatal());
    }

    [Fact]
    public void Old_reports_removed()
    {
        var reporter = Create();
        for (var i = 0; i < 11; i++)
        {
            reporter.Report(new InvalidOperationException($"crash {i}"), "thread 7", fatal: false);
            _time.Advance(TimeSpan.FromMinutes(1));
        }

        var reports = reporter.Reports();
        Assert.Equal(10, reports.Count);
        Assert.Equal("crash-20261016-183100-000.log", Path.GetFileName(reports[0]));
    }

    [Fact]
    public void Two_reports_in_the_same_millisecond_get_different_files()
    {
        var reporter = Create();

        var first = reporter.Report(new InvalidOperationException("a"), "thread 1", fatal: false);
        var second = reporter.Report(new InvalidOperationException("b"), "thread 2", fatal: false);

        Assert.NotEqual(first, second);
        Assert.Equal(2, reporter.Reports().Count);
    }

    [Fact]
    public void Logs_directory_not_writable_does_not_throw()
    {
        Directory.CreateDirectory(_root);
        File.WriteAllText(Logs, "a file where the directory should be");
        var reporter = Create();

        var path = reporter.Report(new InvalidOperationException("boom"), "thread 3", fatal: true);

        Assert.Null(path);
        Assert.Contains("report \"not written\"", Assert.Single(_capture.Lines), StringComparison.Ordinal);
    }

    [Fact]
    public void Fatal_report_raises_the_event_with_the_path()
    {
        var reporter = Create();
        string? reported = null;
        reporter.FatalReported += p => reported = p;

        var path = reporter.Report(new InvalidOperationException("boom"), "thread 3", fatal: true);

        Assert.Equal(path, reported);
    }

    [Fact]
    public void Reports_are_not_touched_by_the_log_rotation()
    {
        var reporter = Create();
        for (var i = 0; i < 3; i++)
        {
            reporter.Report(new InvalidOperationException("boom"), "thread 3", fatal: false);
            _time.Advance(TimeSpan.FromSeconds(1));
        }

        for (var day = 1; day <= 9; day++)
        {
            File.WriteAllText(Path.Combine(Logs, $"pitvoice-202609{day:00}.log"), "old\n");
        }

        using (var log = DiagnosticLog.Create(Logs))
        {
            log.Information("new day");
        }

        Assert.Equal(7, Directory.GetFiles(Logs, DiagnosticLog.FileSearchPattern).Length);
        Assert.Equal(3, reporter.Reports().Count);
    }

    [Fact]
    public void Start_after_a_crash_offers_the_newest_fatal_report()
    {
        var reporter = Create();
        reporter.Report(new InvalidOperationException("old"), "thread 1", fatal: true);
        _time.Advance(TimeSpan.FromSeconds(1));
        var newest = reporter.Report(new InvalidOperationException("new"), "window thread", fatal: true);

        Assert.Equal(newest, Create().PendingFatal());
    }

    [Fact]
    public void Dismissed_offer_does_not_come_back_until_the_next_fatal_crash()
    {
        var reporter = Create();
        reporter.Report(new InvalidOperationException("boom"), "window thread", fatal: true);

        reporter.Acknowledge();

        Assert.Null(Create().PendingFatal());
        Assert.True(File.Exists(Path.Combine(Diagnostics, CrashReporter.SeenMarkName)));

        _time.Advance(TimeSpan.FromMinutes(5));
        var next = reporter.Report(new InvalidOperationException("again"), "thread 9", fatal: true);
        Assert.Equal(next, Create().PendingFatal());
    }

    [Fact]
    public void No_crash_no_offer()
    {
        var reporter = Create();

        Assert.Null(reporter.PendingFatal());
        reporter.Acknowledge();
        Assert.False(File.Exists(Path.Combine(Diagnostics, CrashReporter.SeenMarkName)));
    }

    [Fact]
    public void Not_fatal_report_newer_than_the_mark_is_not_offered()
    {
        var reporter = Create();
        reporter.Report(new InvalidOperationException("boom"), "window thread", fatal: true);
        reporter.Acknowledge();
        _time.Advance(TimeSpan.FromMinutes(1));

        reporter.Report(new InvalidOperationException("task"), "unobserved task", fatal: false);

        Assert.Null(Create().PendingFatal());
    }

    [Theory]
    [InlineData(null, true, null, null)]
    [InlineData("", true, null, null)]
    [InlineData("ui", true, TestCrashKind.Ui, null)]
    [InlineData(" UI ", true, TestCrashKind.Ui, null)]
    [InlineData("background", false, TestCrashKind.Background, null)]
    [InlineData("task", false, TestCrashKind.Task, null)]
    [InlineData("ui", false, null, "PITVOICE_TEST_CRASH=ui needs the window")]
    [InlineData("foo", true, null, "PITVOICE_TEST_CRASH=foo is unknown")]
    public void Test_crash_values(string? value, bool hasWindow, TestCrashKind? expected, string? warning)
    {
        Assert.Equal(expected, TestCrash.Parse(value, hasWindow, out var actualWarning));
        if (warning is null)
        {
            Assert.Null(actualWarning);
        }
        else
        {
            Assert.StartsWith(warning, actualWarning, StringComparison.Ordinal);
        }
    }

    private CrashReporter Create() => new(_capture.CreateLogger(), Logs, Diagnostics, _time);

    private static Exception Thrown(Exception exception)
    {
        try
        {
            throw exception;
        }
        catch (Exception e)
        {
            return e;
        }
    }
}
