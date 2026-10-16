using System.IO.Compression;
using Microsoft.Extensions.Time.Testing;
using PitVoice.Core.Audio;
using PitVoice.Core.Events;
using PitVoice.Core.Pit;
using PitVoice.Host.Diagnostics;
using PitVoice.Input;
using Xunit;

namespace PitVoice.Host.Tests.Diagnostics;

/// <summary>The diagnostics archive (design decision 6, diagnostics-archive spec).</summary>
public sealed class DiagnosticsCollectorTests : IDisposable
{
    private const string Profile = @"C:\Users\Pilot";

    private readonly string _root = Path.Combine(Path.GetTempPath(), "pitvoice-collect-" + Guid.NewGuid().ToString("N"));
    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 10, 16, 18, 30, 0, TimeSpan.Zero));

    public DiagnosticsCollectorTests()
    {
        _time.SetLocalTimeZone(TimeZoneInfo.Utc);
        Paths = new DiagnosticsPaths(
            Path.Combine(_root, "logs"),
            Path.Combine(_root, "diagnostics"),
            Path.Combine(_root, "roaming", "settings.json"),
            Path.Combine(_root, "roaming", "languages"));
    }

    private DiagnosticsPaths Paths { get; }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    [Fact]
    public void Full_archive()
    {
        WriteFile(Paths.LogsDirectory, "pitvoice-20261015.log", "2026-10-15 10:00:00.000 +03:00 [INF] Host stopped\n");
        WriteFile(Paths.LogsDirectory, "pitvoice-20261016.log", "2026-10-16 10:00:00.000 +03:00 [INF] PitVoice started\n");
        WriteFile(Paths.LogsDirectory, "crash-20261016-101500-000.log", "PitVoice crash report\nfatal: true\n");
        WriteFile(Paths.LogsDirectory, "notes.txt", "not a log");
        WriteFile(Path.GetDirectoryName(Paths.SettingsFile)!, "settings.json", "{ \"language\": \"ru\" }");
        WriteFile(Paths.AdditionsDirectory, "ru.yaml", "intents: {}\n");

        var path = Collect(Snapshot());

        Assert.Equal(Path.Combine(Paths.DiagnosticsDirectory, "pitvoice-diagnostics-20261016-183000.zip"), path);
        Assert.Equal(
            ["languages/ru.yaml", "logs/crash-20261016-101500-000.log", "logs/pitvoice-20261015.log", "logs/pitvoice-20261016.log", "settings/settings.json", "summary.txt"],
            Entries(path));
        var summary = Read(path, "summary.txt");
        Assert.StartsWith("PitVoice diagnostics\nskipped files: none\n", summary, StringComparison.Ordinal);
        Assert.Contains("logs: 2 files", summary, StringComparison.Ordinal);
        Assert.Contains("crash reports: 1", summary, StringComparison.Ordinal);
        Assert.Contains("user additions: ru.yaml", summary, StringComparison.Ordinal);
        Assert.Empty(Directory.GetFiles(Paths.DiagnosticsDirectory, "*.part"));
    }

    [Fact]
    public void Summary_has_every_field_of_the_snapshot()
    {
        var summary = Read(Collect(Snapshot()), "summary.txt");

        Assert.Contains("collected: 2026-10-16 18:30:00 +00:00", summary, StringComparison.Ordinal);
        Assert.Contains($"version: {DiagnosticPaths.Version}", summary, StringComparison.Ordinal);
        Assert.Contains($"os: {DiagnosticPaths.OperatingSystem}", summary, StringComparison.Ordinal);
        Assert.Contains("command language: ru", summary, StringComparison.Ordinal);
        Assert.Contains("interface language: en", summary, StringComparison.Ordinal);
        Assert.Contains("model: vosk-model-small-ru-0.22 (installed)", summary, StringComparison.Ordinal);
        Assert.Contains("host: NoGame: GameNotRunning", summary, StringComparison.Ordinal);
        Assert.Contains("iRacing: not running", summary, StringComparison.Ordinal);
        Assert.Contains("PTT: GT Neo: button 1", summary, StringComparison.Ordinal);
    }

    [Fact]
    public void Devices_listed()
    {
        var summary = Read(Collect(Snapshot()), "summary.txt");

        Assert.Contains("microphones:\n  Desk Mic\n  Headset Mic (system default)\n", summary, StringComparison.Ordinal);
        Assert.Contains("output devices:\n  Headset (system default)\n", summary, StringComparison.Ordinal);
        Assert.Contains("game devices:\n  GT Neo [0300abcd]\n", summary, StringComparison.Ordinal);
    }

    [Fact]
    public void Device_enumeration_fails()
    {
        var snapshot = Snapshot() with { GameDevices = DeviceList<GameDevice>.Read(() => throw new InvalidOperationException("SDL did not start")) };

        var summary = Read(Collect(snapshot), "summary.txt");

        Assert.Contains("game devices: error: SDL did not start", summary, StringComparison.Ordinal);
        Assert.Contains("microphones:\n  Desk Mic", summary, StringComparison.Ordinal);
        Assert.Contains("output devices:\n  Headset", summary, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Snapshot_of_a_running_host_without_its_phrases()
    {
        await using var fixture = new HostFixture();
        fixture.Platform.ConnectedDevices.Add(("0300abcd", "GT Neo"));
        fixture.Platform.Microphones.Add(new AudioDevice("Headset Mic", false));
        var host = await fixture.StartAsync();
        fixture.Say("залей тридцать литров");
        await fixture.WaitForAsync<ResultSignaled>();
        Assert.NotEmpty(host.Journal.Entries);

        var path = Collect(DiagnosticsSnapshot.FromHost(host));

        var summary = Read(path, "summary.txt");
        Assert.Contains("game devices:\n  GT Neo [0300abcd]", summary, StringComparison.Ordinal);
        Assert.Contains("Headset Mic", summary, StringComparison.Ordinal);
        Assert.Contains($"model: {HostFixture.RuModel} (installed)", summary, StringComparison.Ordinal);
        Assert.Matches("PTT: [^\n]*F13", summary);
        foreach (var entry in Entries(path))
        {
            Assert.DoesNotContain("тридцать", Read(path, entry), StringComparison.Ordinal);
        }

        Assert.DoesNotContain(Entries(path), e => e.EndsWith(".wav", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Nothing_to_collect_yet()
    {
        var snapshot = Snapshot() with { ModelInstalled = false };

        var path = Collect(snapshot);

        Assert.Equal(["summary.txt"], Entries(path));
        var summary = Read(path, "summary.txt");
        Assert.Contains("logs: none yet", summary, StringComparison.Ordinal);
        Assert.Contains("crash reports: none", summary, StringComparison.Ordinal);
        Assert.Contains("settings: no file yet (defaults)", summary, StringComparison.Ordinal);
        Assert.Contains("(not installed)", summary, StringComparison.Ordinal);
    }

    [Fact]
    public void Old_archives_removed()
    {
        var paths = new List<string>();
        for (var i = 0; i < 6; i++)
        {
            paths.Add(Collect(Snapshot()));
            _time.Advance(TimeSpan.FromMinutes(1));
        }

        var archives = Directory.GetFiles(Paths.DiagnosticsDirectory, DiagnosticsCollector.ArchiveSearchPattern);
        Assert.Equal(5, archives.Length);
        Assert.False(File.Exists(paths[0]));
        Assert.True(File.Exists(paths[^1]));
    }

    [Fact]
    public void Archive_directory_that_is_a_file_fails_without_leftovers()
    {
        Directory.CreateDirectory(_root);
        File.WriteAllText(Paths.DiagnosticsDirectory, "a file where the directory should be");

        Assert.ThrowsAny<IOException>(() => Collect(Snapshot()));

        Assert.Equal("a file where the directory should be", File.ReadAllText(Paths.DiagnosticsDirectory));
    }

    [Fact]
    public void Failure_while_writing_removes_the_part_file()
    {
        WriteFile(Paths.LogsDirectory, "pitvoice-20261016.log", "line\n");
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();

        Assert.ThrowsAny<OperationCanceledException>(() =>
            DiagnosticsCollector.Collect(Snapshot(), Paths, Profile, _time, cancelled.Token));

        Assert.Empty(Directory.GetFiles(Paths.DiagnosticsDirectory));
    }

    [Fact]
    public void Locked_file()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        WriteFile(Paths.LogsDirectory, "pitvoice-20261015.log", "old\n");
        var locked = WriteFile(Paths.LogsDirectory, "pitvoice-20261016.log", "locked\n");
        string path;
        using (new FileStream(locked, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
        {
            path = Collect(Snapshot());
        }

        Assert.DoesNotContain("logs/pitvoice-20261016.log", Entries(path));
        Assert.Contains("logs/pitvoice-20261015.log", Entries(path));
        var summary = Read(path, "summary.txt");
        Assert.Contains("skipped files:\n  logs/pitvoice-20261016.log: ", summary, StringComparison.Ordinal);
    }

    [Fact]
    public void Path_in_the_log()
    {
        WriteFile(Paths.LogsDirectory, "pitvoice-20261016.log",
            @"[INF] Model vosk-model-small-ru-0.22 loaded from C:\Users\Pilot\AppData\Local\PitVoice\models\vosk-model-small-ru-0.22" + "\n" +
            "[INF] Settings file c:/users/PILOT/AppData/Roaming/PitVoice/settings.json\n");

        var log = Read(Collect(Snapshot()), "logs/pitvoice-20261016.log");

        Assert.Contains(@"%USERPROFILE%\AppData\Local\PitVoice\models\vosk-model-small-ru-0.22", log, StringComparison.Ordinal);
        Assert.Contains("%USERPROFILE%/AppData/Roaming/PitVoice/settings.json", log, StringComparison.Ordinal);
        Assert.DoesNotContain("Pilot", log, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Path_in_the_settings_file_is_replaced_in_its_escaped_form()
    {
        WriteFile(Path.GetDirectoryName(Paths.SettingsFile)!, "settings.json",
            "{\n  \"note\": \"C:\\\\Users\\\\Pilot\\\\Documents\\\\x.txt\"\n}\n");

        var settings = Read(Collect(Snapshot()), "settings/settings.json");

        Assert.Contains("\"%USERPROFILE%\\\\Documents\\\\x.txt\"", settings, StringComparison.Ordinal);
    }

    [Fact]
    public void Original_files_unchanged()
    {
        const string text = @"[INF] C:\Users\Pilot\AppData\Local\PitVoice" + "\n";
        var log = WriteFile(Paths.LogsDirectory, "pitvoice-20261016.log", text);
        var settings = WriteFile(Path.GetDirectoryName(Paths.SettingsFile)!, "settings.json", "{ \"microphone\": \"Pilot's Mic\" }");

        Collect(Snapshot());

        Assert.Equal(text, File.ReadAllText(log));
        Assert.Equal("{ \"microphone\": \"Pilot's Mic\" }", File.ReadAllText(settings));
    }

    [Fact]
    public void Archive_names_have_no_user_name()
    {
        WriteFile(Paths.LogsDirectory, "pitvoice-20261016.log", "x\n");

        var path = Collect(Snapshot());

        Assert.DoesNotContain(Environment.UserName, Path.GetFileName(path), StringComparison.OrdinalIgnoreCase);
        Assert.All(Entries(path), e => Assert.DoesNotContain("Pilot", e, StringComparison.OrdinalIgnoreCase));
    }

    private static DiagnosticsSnapshot Snapshot() => new()
    {
        Host = "NoGame: GameNotRunning",
        CommandLanguage = "ru",
        InterfaceLanguage = "en",
        ModelId = "vosk-model-small-ru-0.22",
        ModelInstalled = true,
        Game = ConnectionState.NotRunning,
        Ptt = "GT Neo: button 1",
        Microphones = new([new AudioDevice("Desk Mic", false), new AudioDevice("Headset Mic", true)], null),
        Outputs = new([new AudioDevice("Headset", true)], null),
        GameDevices = new([new GameDevice("0300abcd", "GT Neo")], null),
    };

    private string Collect(DiagnosticsSnapshot snapshot) =>
        DiagnosticsCollector.Collect(snapshot, Paths, Profile, _time, CancellationToken.None);

    private static string WriteFile(string directory, string name, string text)
    {
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, name);
        File.WriteAllText(path, text);
        return path;
    }

    private static List<string> Entries(string zip)
    {
        using var archive = ZipFile.OpenRead(zip);
        return [.. archive.Entries.Select(e => e.FullName).Order(StringComparer.Ordinal)];
    }

    private static string Read(string zip, string entry)
    {
        using var archive = ZipFile.OpenRead(zip);
        using var reader = new StreamReader(archive.GetEntry(entry)!.Open());
        return reader.ReadToEnd();
    }
}
