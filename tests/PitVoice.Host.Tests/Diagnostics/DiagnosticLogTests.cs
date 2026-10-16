using PitVoice.Host.Diagnostics;
using Xunit;

namespace PitVoice.Host.Tests.Diagnostics;

/// <summary>The log file (design decision 1, spec: Log location and retention).</summary>
public sealed class DiagnosticLogTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "pitvoice-log-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    [Fact]
    public void Record_reaches_the_dated_file_once_the_log_is_disposed()
    {
        var directory = Path.Combine(_root, "logs");
        using (var log = DiagnosticLog.Create(directory))
        {
            log.Information("Model {ModelId} loaded", "vosk-model-small-ru-0.22");
        }

        var file = Assert.Single(Directory.GetFiles(directory));
        Assert.Equal($"pitvoice-{DateTime.Now:yyyyMMdd}.log", Path.GetFileName(file));
        var line = Assert.Single(File.ReadAllLines(file));
        Assert.Matches(@"^\d{4}-\d{2}-\d{2} \d{2}:\d{2}:\d{2}\.\d{3} [+-]\d{2}:\d{2} \[INF\] Model vosk-model-small-ru-0\.22 loaded$", line);
    }

    [Fact]
    public void Directory_that_cannot_be_created_gives_a_log_that_writes_nowhere()
    {
        Directory.CreateDirectory(_root);
        var blocker = Path.Combine(_root, "logs");
        File.WriteAllText(blocker, "a file where the directory should be");

        using (var log = DiagnosticLog.Create(blocker))
        {
            log.Information("started");
            log.Error(new InvalidOperationException("boom"), "failed");
        }

        Assert.Equal("a file where the directory should be", File.ReadAllText(blocker));
        Assert.Single(Directory.GetFileSystemEntries(_root));
    }

    [Fact]
    public void File_name_template_is_fixed_because_rotation_only_removes_its_own_files()
    {
        Assert.Equal("pitvoice-.log", DiagnosticLog.FileNameTemplate);
        Assert.Equal("pitvoice-*.log", DiagnosticLog.FileSearchPattern);
        Assert.Equal(7, DiagnosticLog.RetainedFiles);
        Assert.Equal(10L * 1024 * 1024, DiagnosticLog.FileSizeLimit);
    }

    [Fact]
    public void Old_files_are_removed_down_to_seven()
    {
        var directory = Path.Combine(_root, "logs");
        Directory.CreateDirectory(directory);
        for (var day = 1; day <= 9; day++)
        {
            var file = Path.Combine(directory, $"pitvoice-202609{day:00}.log");
            File.WriteAllText(file, "old\n");
            File.SetLastWriteTimeUtc(file, new DateTime(2026, 9, day, 12, 0, 0, DateTimeKind.Utc));
        }

        using (var log = DiagnosticLog.Create(directory))
        {
            log.Information("new day");
        }

        var files = Directory.GetFiles(directory, DiagnosticLog.FileSearchPattern).Select(Path.GetFileName).Order(StringComparer.Ordinal).ToList();
        Assert.Equal(7, files.Count);
        Assert.Equal($"pitvoice-{DateTime.Now:yyyyMMdd}.log", files[^1]);
        Assert.DoesNotContain("pitvoice-20260901.log", files);
        Assert.DoesNotContain("pitvoice-20260903.log", files);
    }
}
