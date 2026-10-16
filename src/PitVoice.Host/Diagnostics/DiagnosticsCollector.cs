using System.Globalization;
using System.IO.Compression;
using System.Text;
using PitVoice.Core.Audio;
using PitVoice.Core.Pit;

namespace PitVoice.Host.Diagnostics;

/// <summary>Where the collector reads from and writes to.</summary>
/// <param name="LogsDirectory">Log files and crash reports.</param>
/// <param name="DiagnosticsDirectory">Where the archive goes.</param>
/// <param name="SettingsFile">The settings file (it may not exist yet).</param>
/// <param name="AdditionsDirectory">User additions of the language packs (it may not exist).</param>
public sealed record DiagnosticsPaths(string LogsDirectory, string DiagnosticsDirectory, string SettingsFile, string AdditionsDirectory)
{
    /// <summary>The standard log and diagnostics directories with the given settings file and additions directory.</summary>
    public static DiagnosticsPaths Default(string settingsFile, string additionsDirectory) =>
        new(DiagnosticPaths.LogsDirectory, DiagnosticPaths.DiagnosticsDirectory, settingsFile, additionsDirectory);
}

/// <summary>
/// "Collect diagnostics" (design decision 6): a zip with the summary, the logs and crash reports, the settings file and the
/// user additions. Every file is a text file and gets the Windows profile path replaced with <c>%USERPROFILE%</c>; the
/// files on disk stay as they are. The zip is written as <c>*.zip.part</c> and renamed when done, a failure removes the
/// part. The last <see cref="KeptArchives"/> archives are kept. Nothing goes over the network.
/// </summary>
public static class DiagnosticsCollector
{
    public const int KeptArchives = 5;

    public const string ArchiveSearchPattern = "pitvoice-diagnostics-*.zip";

    public const string ProfilePlaceholder = "%USERPROFILE%";

    /// <summary>Builds the archive and returns its path; throws <see cref="IOException"/> or <see cref="UnauthorizedAccessException"/> when it cannot be written.</summary>
    public static string Collect(DiagnosticsSnapshot snapshot, DiagnosticsPaths paths, CancellationToken cancellationToken = default) =>
        Collect(snapshot, paths, Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), TimeProvider.System, cancellationToken);

    internal static string Collect(DiagnosticsSnapshot snapshot, DiagnosticsPaths paths, string? profile, TimeProvider time, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(paths);
        var now = time.GetLocalNow();
        Directory.CreateDirectory(paths.DiagnosticsDirectory);
        var path = NewArchivePath(paths.DiagnosticsDirectory, now);
        var part = path + ".part";
        try
        {
            using (var stream = new FileStream(part, FileMode.Create, FileAccess.Write, FileShare.None))
            using (var zip = new ZipArchive(stream, ZipArchiveMode.Create))
            {
                var files = Gather(paths);
                var included = new List<(string Entry, string Text)>();
                var skipped = new List<(string Entry, string Reason)>();
                foreach (var (entry, source) in files)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    try
                    {
                        included.Add((entry, HideProfile(ReadShared(source), profile)));
                    }
                    catch (Exception e) when (e is IOException or UnauthorizedAccessException)
                    {
                        skipped.Add((entry, e.Message));
                    }
                }

                Write(zip, "summary.txt", HideProfile(Summary(snapshot, now, included.Select(f => f.Entry).ToList(), skipped), profile));
                foreach (var (entry, text) in included)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    Write(zip, entry, text);
                }
            }

            File.Move(part, path);
        }
        catch
        {
            TryDelete(part);
            throw;
        }

        Prune(paths.DiagnosticsDirectory);
        return path;
    }

    /// <summary>
    /// Replaces the profile path in its three forms: with <c>\</c>, with <c>/</c> and with <c>\\</c> as in JSON, ignoring
    /// case.
    /// </summary>
    internal static string HideProfile(string text, string? profile)
    {
        if (string.IsNullOrWhiteSpace(profile))
        {
            return text;
        }

        var backslashes = profile.TrimEnd('\\', '/').Replace('/', '\\');
        if (backslashes.Length < 3)
        {
            return text;
        }

        return text
            .Replace(backslashes.Replace("\\", "\\\\", StringComparison.Ordinal), ProfilePlaceholder, StringComparison.OrdinalIgnoreCase)
            .Replace(backslashes, ProfilePlaceholder, StringComparison.OrdinalIgnoreCase)
            .Replace(backslashes.Replace('\\', '/'), ProfilePlaceholder, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Entry name in the archive and the file it comes from.</summary>
    private static List<(string Entry, string Source)> Gather(DiagnosticsPaths paths)
    {
        var files = new List<(string, string)>();
        foreach (var pattern in new[] { DiagnosticLog.FileSearchPattern, CrashReporter.FileSearchPattern })
        {
            foreach (var file in List(paths.LogsDirectory, pattern))
            {
                files.Add(($"logs/{Path.GetFileName(file)}", file));
            }
        }

        if (File.Exists(paths.SettingsFile))
        {
            files.Add(("settings/settings.json", paths.SettingsFile));
        }

        foreach (var file in List(paths.AdditionsDirectory, "*.yaml"))
        {
            files.Add(($"languages/{Path.GetFileName(file)}", file));
        }

        return files;
    }

    private static IEnumerable<string> List(string directory, string pattern)
    {
        try
        {
            return Directory.Exists(directory) ? Directory.GetFiles(directory, pattern).Order(StringComparer.Ordinal) : [];
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return [];
        }
    }

    /// <summary>The current log is open for writing by Serilog; reading must share it.</summary>
    private static string ReadShared(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using var reader = new StreamReader(stream, Encoding.UTF8);
        return reader.ReadToEnd();
    }

    private static void Write(ZipArchive zip, string entry, string text)
    {
        using var writer = new StreamWriter(zip.CreateEntry(entry, CompressionLevel.Optimal).Open(), new UTF8Encoding(false));
        writer.Write(text);
    }

    private static string Summary(DiagnosticsSnapshot snapshot, DateTimeOffset now, List<string> included, List<(string Entry, string Reason)> skipped)
    {
        var text = new StringBuilder();
        void Line(string line) => text.Append(line).Append('\n');

        Line("PitVoice diagnostics");
        if (skipped.Count == 0)
        {
            Line("skipped files: none");
        }
        else
        {
            Line("skipped files:");
            foreach (var (entry, reason) in skipped)
            {
                Line($"  {entry}: {reason}");
            }
        }

        Line(string.Empty);
        Line(string.Create(CultureInfo.InvariantCulture, $"collected: {now:yyyy-MM-dd HH:mm:ss zzz}"));
        Line($"version: {DiagnosticPaths.Version}");
        Line($"os: {DiagnosticPaths.OperatingSystem}");
        Line($"command language: {snapshot.CommandLanguage}");
        Line($"interface language: {snapshot.InterfaceLanguage}");
        Line(snapshot.ModelId is { } model
            ? $"model: {model} ({(snapshot.ModelInstalled ? "installed" : "not installed")})"
            : "model: none (no language pack names one)");
        Line($"host: {snapshot.Host}");
        Line($"iRacing: {Game(snapshot.Game)}");
        Line($"PTT: {snapshot.Ptt ?? "not assigned"}");

        Line(string.Empty);
        var logs = included.Count(e => e.StartsWith("logs/pitvoice-", StringComparison.Ordinal));
        var crashes = included.Count(e => e.StartsWith("logs/crash-", StringComparison.Ordinal));
        Line(logs == 0 ? "logs: none yet" : string.Create(CultureInfo.InvariantCulture, $"logs: {logs} files"));
        Line(crashes == 0 ? "crash reports: none" : string.Create(CultureInfo.InvariantCulture, $"crash reports: {crashes}"));
        Line(included.Contains("settings/settings.json") ? "settings: included" : "settings: no file yet (defaults)");
        var additions = included.Where(e => e.StartsWith("languages/", StringComparison.Ordinal)).Select(e => e["languages/".Length..]).ToList();
        Line(additions.Count == 0 ? "user additions: none" : $"user additions: {string.Join(", ", additions)}");

        Line(string.Empty);
        Devices("microphones", snapshot.Microphones, AudioDevice);
        Devices("output devices", snapshot.Outputs, AudioDevice);
        Devices("game devices", snapshot.GameDevices, d => $"{d.Name} [{d.Guid}]");
        return text.ToString();

        void Devices<T>(string title, DeviceList<T> list, Func<T, string> describe)
        {
            if (list.Error is { } error)
            {
                Line($"{title}: error: {error}");
                return;
            }

            if (list.Items.Count == 0)
            {
                Line($"{title}: none");
                return;
            }

            Line($"{title}:");
            foreach (var item in list.Items)
            {
                Line($"  {describe(item)}");
            }
        }
    }

    private static string AudioDevice(AudioDevice device) => device.IsDefault ? $"{device.Name} (system default)" : device.Name;

    private static string Game(ConnectionState state) => state switch
    {
        ConnectionState.NotRunning => "not running",
        ConnectionState.Connected => "running, not in the car",
        _ => "in the car",
    };

    private static string NewArchivePath(string directory, DateTimeOffset now)
    {
        var name = string.Create(CultureInfo.InvariantCulture, $"pitvoice-diagnostics-{now:yyyyMMdd-HHmmss}");
        var path = Path.Combine(directory, name + ".zip");
        for (var i = 2; File.Exists(path) || File.Exists(path + ".part"); i++)
        {
            path = Path.Combine(directory, string.Create(CultureInfo.InvariantCulture, $"{name}-{i}.zip"));
        }

        return path;
    }

    private static void Prune(string directory)
    {
        foreach (var old in List(directory, ArchiveSearchPattern).Reverse().Skip(KeptArchives))
        {
            TryDelete(old);
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // Removed next time.
        }
    }
}
