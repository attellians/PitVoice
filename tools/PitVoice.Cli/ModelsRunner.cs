using System.Globalization;
using PitVoice.Models;

namespace PitVoice.Cli;

/// <summary><c>models list|download|delete|verify</c> (FR-MOD-5).</summary>
internal static class ModelsRunner
{
    public static int List(ModelStore store)
    {
        if (LoadManifest() is not { } manifest)
        {
            return 1;
        }

        Console.WriteLine($"Models directory: {store.Root}");
        foreach (var entry in manifest.Entries)
        {
            var status = store.IsInstalled(entry.Id) ? "installed"
                : Directory.Exists(store.GetModelDirectory(entry.Id)) ? "incomplete (delete and download again)"
                : "not installed";
            Console.WriteLine($"{entry.Id,-32} {entry.Language,-3} {entry.Type.ToString().ToLowerInvariant(),-4} {FormatSize(entry.SizeBytes),9}  {entry.License,-12} {status}");
        }

        return 0;
    }

    public static async Task<int> DownloadAsync(ModelStore store, string id, CancellationToken cancellationToken)
    {
        if (LoadManifest() is not { } manifest || Find(manifest, id) is not { } entry)
        {
            return 1;
        }

        if (store.IsInstalled(entry.Id))
        {
            Console.WriteLine($"{entry.Id} is already installed in {store.GetModelDirectory(entry.Id)}");
            return 0;
        }

        Console.WriteLine($"Downloading {entry.Id} ({FormatSize(entry.SizeBytes)}, {entry.License}) from {entry.Url}");
        Console.WriteLine("Press Ctrl+C to cancel.");
        using var http = new HttpClient { Timeout = TimeSpan.FromMinutes(30) };
        var downloader = new ModelDownloader(http, store, message => Console.Error.WriteLine(message));
        var progress = new ConsoleProgress();
        try
        {
            await downloader.DownloadAsync(entry, progress, cancellationToken).ConfigureAwait(false);
            progress.Finish();
            Console.WriteLine($"Installed {entry.Id} in {store.GetModelDirectory(entry.Id)}");
            return 0;
        }
        catch (OperationCanceledException)
        {
            progress.Finish();
            Console.Error.WriteLine($"Cancelled; partial files of {entry.Id} removed.");
            return 130;
        }
        catch (Exception e) when (e is ModelDownloadException or IOException or UnauthorizedAccessException or InvalidDataException)
        {
            progress.Finish();
            Console.Error.WriteLine($"Download failed: {e.Message}");
            return 1;
        }
    }

    public static int Delete(ModelStore store, string id)
    {
        if (LoadManifest() is not { } manifest || Find(manifest, id) is not { } entry)
        {
            return 1;
        }

        Console.WriteLine(store.Delete(entry.Id) ? $"Deleted {entry.Id}." : $"{entry.Id} is not installed.");
        return 0;
    }

    public static int Verify(ModelStore store, string id)
    {
        if (LoadManifest() is not { } manifest || Find(manifest, id) is not { } entry)
        {
            return 1;
        }

        if (!store.TryGetInstalled(entry.Id, out var installed))
        {
            var reason = Directory.Exists(store.GetModelDirectory(entry.Id))
                ? "the install did not finish (no marker); delete it and download again"
                : "not installed";
            Console.Error.WriteLine($"{entry.Id}: {reason}");
            return 1;
        }

        if (!string.Equals(installed.Sha256, entry.Sha256, StringComparison.OrdinalIgnoreCase))
        {
            Console.Error.WriteLine($"{entry.Id}: installed archive {installed.Sha256} differs from the manifest {entry.Sha256}; download again");
            return 1;
        }

        Console.WriteLine($"{entry.Id}: OK, installed {installed.InstalledAt.ToLocalTime():yyyy-MM-dd HH:mm} in {installed.Directory}");
        return 0;
    }

    public static ModelManifest? LoadManifest()
    {
        try
        {
            return ModelManifest.Load(ModelManifest.ResolvePath(null));
        }
        catch (ModelManifestException e)
        {
            Console.Error.WriteLine(e.Message);
            return null;
        }
    }

    private static ModelEntry? Find(ModelManifest manifest, string id)
    {
        if (manifest.Find(id) is { } entry)
        {
            return entry;
        }

        Console.Error.WriteLine($"Unknown model '{id}'. Available: {string.Join(", ", manifest.Entries.Select(e => e.Id))}");
        return null;
    }

    private static string FormatSize(long bytes) =>
        (bytes / 1024.0 / 1024.0).ToString("0.0", CultureInfo.InvariantCulture) + " MB";

    /// <summary>
    /// One self-overwriting console line, updated at most every 100 ms; when output is redirected, a line every 5 s.
    /// </summary>
    private sealed class ConsoleProgress : IProgress<DownloadProgress>
    {
        private readonly bool _redirected = Console.IsOutputRedirected;
        private long _lastTick;
        private bool _written;

        public void Report(DownloadProgress value)
        {
            var now = Environment.TickCount64;
            var done = value.TotalBytes is { } size && value.BytesReceived >= size;
            if (!done && now - _lastTick < (_redirected ? 5000 : 100))
            {
                return;
            }

            _lastTick = now;
            if (_redirected)
            {
                var stageName = value.Stage == DownloadStage.Downloading ? "downloading" : "extracting";
                var percentText = value.Percent is { } pr ? $" {pr:0.0}%" : string.Empty;
                Console.WriteLine($"{stageName} {FormatSize(value.BytesReceived)}{percentText}");
                return;
            }

            var stage = value.Stage == DownloadStage.Downloading ? "downloading" : "extracting ";
            var percent = value.Percent is { } p ? $" {p,5:0.0}%" : string.Empty;
            var total = value.TotalBytes is { } t ? $" / {FormatSize(t)}" : string.Empty;
            Console.Write($"\r{stage} {FormatSize(value.BytesReceived)}{total}{percent}   ");
            _written = true;
        }

        public void Finish()
        {
            if (_written)
            {
                Console.WriteLine();
                _written = false;
            }
        }
    }
}
