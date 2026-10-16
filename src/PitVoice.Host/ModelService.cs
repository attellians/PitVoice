using PitVoice.Models;

namespace PitVoice.Host;

/// <summary>State of a model on the "Models" page.</summary>
public enum ModelState
{
    NotInstalled,
    Downloading,
    Installed,

    /// <summary>Installed and loaded by the recognizer of the current language.</summary>
    InUse,
}

/// <summary>A model as the "Models" page shows it.</summary>
/// <param name="Entry">Manifest entry: id, size, license.</param>
/// <param name="State">Current state.</param>
/// <param name="Progress">Download progress while downloading.</param>
/// <param name="Error">Why the last download or delete failed, or null.</param>
public sealed record ModelItem(ModelEntry Entry, ModelState State, DownloadProgress? Progress, string? Error);

/// <summary>How deleting a model ended.</summary>
/// <param name="Deleted">True when the model directory is gone.</param>
/// <param name="Error">Why it could not be deleted (files locked); the model is loaded again then.</param>
public sealed record ModelDeleteResult(bool Deleted, string? Error);

/// <summary>
/// Models from the window (design decision 13, spec: Models in the window, Delete model from the window). Downloads run
/// in the host, not in the window: closing the window does not stop them, stopping the host cancels them (the
/// downloader removes partial files). When the model of the current language is installed, the host loads it; a model
/// in use is unloaded before it is deleted and loaded again if the files cannot be deleted.
/// </summary>
public sealed class ModelService : IAsyncDisposable
{
    private readonly AppHost _host;
    private readonly ModelDownloader _downloader;
    private readonly Lock _lock = new();
    private readonly Dictionary<string, Download> _downloads = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _errors = new(StringComparer.Ordinal);

    /// <param name="host">The pipeline host.</param>
    /// <param name="manifest">The manifest, or null when it could not be read.</param>
    /// <param name="manifestError">Why the manifest could not be read.</param>
    /// <param name="http">HTTP client for downloads.</param>
    public ModelService(AppHost host, ModelManifest? manifest, string? manifestError, HttpClient http)
    {
        ArgumentNullException.ThrowIfNull(host);
        ArgumentNullException.ThrowIfNull(http);
        _host = host;
        Manifest = manifest;
        ManifestError = manifestError;
        _downloader = new ModelDownloader(http, host.Models, time: host.Time);
        _host.StatusChanged += _ => Changed?.Invoke();
    }

    /// <summary>A model's state or progress changed; raised on any thread.</summary>
    public event Action? Changed;

    public ModelManifest? Manifest { get; }

    public string? ManifestError { get; }

    /// <summary>Loads <c>models/manifest.json</c>; a broken manifest leaves the page with an error instead of models.</summary>
    public static (ModelManifest? Manifest, string? Error) LoadManifest(string? path = null)
    {
        try
        {
            return (ModelManifest.Load(ModelManifest.ResolvePath(path)), null);
        }
        catch (ModelManifestException e)
        {
            return (null, e.Message);
        }
    }

    /// <summary>Speech models of <paramref name="language"/> with their state.</summary>
    public IReadOnlyList<ModelItem> ForLanguage(string language)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(language);
        return Items(e => string.Equals(e.Language, language, StringComparison.Ordinal));
    }

    /// <summary>Speech models of every language, in manifest order.</summary>
    public IReadOnlyList<ModelItem> All() => Items(_ => true);

    private IReadOnlyList<ModelItem> Items(Func<ModelEntry, bool> include)
    {
        if (Manifest is null)
        {
            return [];
        }

        var inUse = _host.Engine is not null ? _host.Status.Facts.ModelId : null;
        lock (_lock)
        {
            return Manifest.Entries
                .Where(e => e.Type == ModelType.Asr && include(e))
                .Select(e =>
                {
                    var downloading = _downloads.TryGetValue(e.Id, out var download);
                    var state = downloading ? ModelState.Downloading
                        : !_host.Models.IsInstalled(e.Id) ? ModelState.NotInstalled
                        : e.Id == inUse ? ModelState.InUse
                        : ModelState.Installed;
                    return new ModelItem(e, state, download?.Progress, _errors.GetValueOrDefault(e.Id));
                })
                .ToArray();
        }
    }

    /// <summary>Starts downloading; the task completes when the download ends (installed, failed or cancelled).</summary>
    public Task DownloadAsync(string id)
    {
        if (Manifest?.Find(id) is not { } entry)
        {
            throw new ArgumentException($"no model '{id}' in the manifest", nameof(id));
        }

        Download download;
        lock (_lock)
        {
            if (_downloads.TryGetValue(id, out var running))
            {
                return running.Task;
            }

            _errors.Remove(id);
            download = new Download();
            _downloads[id] = download;
            download.Task = RunAsync(entry, download);
        }

        Changed?.Invoke();
        return download.Task;
    }

    public void Cancel(string id)
    {
        lock (_lock)
        {
            if (_downloads.TryGetValue(id, out var download))
            {
                download.Cancellation.Cancel();
            }
        }
    }

    /// <summary>Deletes an installed model; a model in use is unloaded first.</summary>
    public async Task<ModelDeleteResult> DeleteAsync(string id)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        var inUse = string.Equals(_host.Status.Facts.ModelId, id, StringComparison.Ordinal);
        if (inUse)
        {
            await _host.ReleaseModelAsync().ConfigureAwait(false);
        }

        ModelDeleteResult result;
        try
        {
            _host.Models.Delete(id);
            result = new ModelDeleteResult(true, null);
            lock (_lock)
            {
                _errors.Remove(id);
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            result = new ModelDeleteResult(false, e.Message);
            lock (_lock)
            {
                _errors[id] = e.Message;
            }
        }

        if (inUse)
        {
            // Deleted: the host finds no model. Not deleted: the model is loaded again (spec: Files locked).
            await _host.ReloadAsync().ConfigureAwait(false);
        }

        Changed?.Invoke();
        return result;
    }

    /// <summary>Cancels every download and waits until their partial files are removed (spec: Exit during download).</summary>
    public async ValueTask DisposeAsync()
    {
        Task[] running;
        lock (_lock)
        {
            foreach (var download in _downloads.Values)
            {
                download.Cancellation.Cancel();
            }

            running = [.. _downloads.Values.Select(d => d.Task)];
        }

        await Task.WhenAll(running).ConfigureAwait(false);
    }

    private async Task RunAsync(ModelEntry entry, Download download)
    {
        await Task.Yield();
        string? error = null;
        var installed = false;
        try
        {
            var progress = new Progress(p =>
            {
                download.Progress = p;
                Changed?.Invoke();
            });
            await _downloader.DownloadAsync(entry, progress, download.Cancellation.Token).ConfigureAwait(false);
            installed = true;
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception e) when (e is ModelDownloadException or IOException or UnauthorizedAccessException or InvalidDataException or HttpRequestException)
        {
            error = e.Message;
        }
        finally
        {
            lock (_lock)
            {
                _downloads.Remove(entry.Id);
                if (error is not null)
                {
                    _errors[entry.Id] = error;
                }
            }

            download.Cancellation.Dispose();
        }

        if (installed && string.Equals(_host.Status.Facts.ModelId, entry.Id, StringComparison.Ordinal))
        {
            await _host.ReloadAsync().ConfigureAwait(false);
        }

        Changed?.Invoke();
    }

    private sealed class Download
    {
        public CancellationTokenSource Cancellation { get; } = new();

        public Task Task { get; set; } = Task.CompletedTask;

        public DownloadProgress? Progress { get; set; }
    }

    /// <summary>Reports synchronously, unlike <see cref="Progress{T}"/>, which posts to a captured context.</summary>
    private sealed class Progress(Action<DownloadProgress> report) : IProgress<DownloadProgress>
    {
        public void Report(DownloadProgress value) => report(value);
    }
}
