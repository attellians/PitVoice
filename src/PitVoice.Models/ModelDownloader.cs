using System.IO.Compression;
using System.Net;
using System.Security.Cryptography;

namespace PitVoice.Models;

/// <summary>What the downloader is doing right now.</summary>
public enum DownloadStage
{
    Downloading,
    Extracting,
}

/// <summary>Progress report. <see cref="TotalBytes"/> is null when the server did not send a length.</summary>
public sealed record DownloadProgress(DownloadStage Stage, long BytesReceived, long? TotalBytes)
{
    /// <summary>0..100, or null when the total size is unknown.</summary>
    public double? Percent => TotalBytes is > 0 ? Math.Min(100.0, 100.0 * BytesReceived / TotalBytes.Value) : null;
}

/// <summary>Outcome of a successful <see cref="ModelDownloader.DownloadAsync"/>.</summary>
public enum DownloadOutcome
{
    Installed,
    AlreadyInstalled,
}

/// <summary>A download failed; nothing of the model is left on disk.</summary>
public sealed class ModelDownloadException(string message, Exception? inner = null) : Exception(message, inner);

/// <summary>
/// Downloads a model archive on user request (FR-MOD-2..3): streams to <c>&lt;id&gt;.zip.part</c> while hashing, verifies
/// SHA-256 before extraction, falls back to the mirror once, extracts with zip-slip protection and writes the install
/// marker last. On cancellation or any failure the partial archive and the model directory are removed. A source that
/// does not answer within <see cref="ResponseTimeout"/>, or stops sending for <see cref="StallTimeout"/>, counts as
/// unavailable, so a hung primary source gives way to the mirror instead of holding the download.
/// </summary>
public sealed class ModelDownloader
{
    /// <summary>How long a source may take to answer (response headers) before the next one is tried.</summary>
    public static readonly TimeSpan ResponseTimeout = TimeSpan.FromSeconds(20);

    /// <summary>How long a started download may receive nothing before the next source is tried.</summary>
    public static readonly TimeSpan StallTimeout = TimeSpan.FromSeconds(30);

    private const int BufferSize = 81920;

    private readonly HttpClient _http;
    private readonly ModelStore _store;
    private readonly Action<string> _log;
    private readonly TimeProvider _time;

    /// <param name="http">Client for both sources; its own timeout only limits the whole download.</param>
    /// <param name="store">Where the model is installed.</param>
    /// <param name="log">Receives the switch to the mirror and cleanup problems.</param>
    /// <param name="time">Clock of the timeouts and the install date; virtual in tests.</param>
    public ModelDownloader(HttpClient http, ModelStore store, Action<string>? log = null, TimeProvider? time = null)
    {
        ArgumentNullException.ThrowIfNull(http);
        ArgumentNullException.ThrowIfNull(store);
        _http = http;
        _store = store;
        _log = log ?? (_ => { });
        _time = time ?? TimeProvider.System;
    }

    public async Task<DownloadOutcome> DownloadAsync(
        ModelEntry entry,
        IProgress<DownloadProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(entry);

        if (_store.IsInstalled(entry.Id))
        {
            return DownloadOutcome.AlreadyInstalled;
        }

        Directory.CreateDirectory(_store.Root);
        _store.Delete(entry.Id); // leftovers of an interrupted install

        var partial = _store.GetPartialFile(entry.Id);
        var directory = _store.GetModelDirectory(entry.Id);
        try
        {
            var actual = await DownloadWithFallbackAsync(entry, partial, progress, cancellationToken).ConfigureAwait(false);
            if (!string.Equals(actual, entry.Sha256, StringComparison.OrdinalIgnoreCase))
            {
                throw new ModelDownloadException(
                    $"model '{entry.Id}': checksum mismatch: expected {entry.Sha256}, actual {actual}");
            }

            await ExtractAsync(partial, directory, progress, cancellationToken).ConfigureAwait(false);
            File.Delete(partial);
            _store.WriteMarker(entry.Id, entry.Sha256, _time.GetUtcNow());
            return DownloadOutcome.Installed;
        }
        catch
        {
            Cleanup(partial, directory);
            throw;
        }
    }

    private async Task<string> DownloadWithFallbackAsync(
        ModelEntry entry,
        string partial,
        IProgress<DownloadProgress>? progress,
        CancellationToken cancellationToken)
    {
        try
        {
            return await DownloadFileAsync(entry.Url, partial, progress, cancellationToken).ConfigureAwait(false);
        }
        catch (SourceUnavailableException primary)
        {
            if (entry.Mirror is null)
            {
                throw new ModelDownloadException($"model '{entry.Id}': no connection: {primary.Message}", primary.InnerException);
            }

            _log($"model '{entry.Id}': primary source failed ({primary.Message}), switching to mirror {entry.Mirror}");
            try
            {
                return await DownloadFileAsync(entry.Mirror, partial, progress, cancellationToken).ConfigureAwait(false);
            }
            catch (SourceUnavailableException mirror)
            {
                throw new ModelDownloadException(
                    $"model '{entry.Id}': no connection: primary: {primary.Message}; mirror: {mirror.Message}",
                    mirror.InnerException);
            }
        }
    }

    /// <summary>Streams <paramref name="url"/> into <paramref name="path"/> and returns the lower-case hex SHA-256.</summary>
    private async Task<string> DownloadFileAsync(
        Uri url,
        string path,
        IProgress<DownloadProgress>? progress,
        CancellationToken cancellationToken)
    {
        // One timer for both waits: the answer, then every read. It fires only when the source is silent for too long.
        using var silence = new CancellationTokenSource(ResponseTimeout, _time);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, silence.Token);
        var token = linked.Token;

        HttpResponseMessage response;
        try
        {
            response = await _http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, token).ConfigureAwait(false);
        }
        catch (HttpRequestException e)
        {
            throw new SourceUnavailableException($"{url}: {e.Message}", e);
        }
        catch (OperationCanceledException e) when (!cancellationToken.IsCancellationRequested)
        {
            throw new SourceUnavailableException(
                silence.IsCancellationRequested ? $"{url}: no answer in {(int)ResponseTimeout.TotalSeconds} s" : $"{url}: timed out", e);
        }

        using (response)
        {
            if (!response.IsSuccessStatusCode)
            {
                throw new SourceUnavailableException($"{url}: HTTP {(int)response.StatusCode} {StatusText(response.StatusCode)}", null);
            }

            var total = response.Content.Headers.ContentLength;
            using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            var buffer = new byte[BufferSize];
            long received = 0;
            try
            {
                silence.CancelAfter(StallTimeout);
                var source = await response.Content.ReadAsStreamAsync(token).ConfigureAwait(false);
                await using (source.ConfigureAwait(false))
                {
                    var target = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None, BufferSize, useAsync: true);
                    await using (target.ConfigureAwait(false))
                    {
                        progress?.Report(new DownloadProgress(DownloadStage.Downloading, 0, total));
                        int read;
                        while ((read = await source.ReadAsync(buffer, token).ConfigureAwait(false)) > 0)
                        {
                            silence.CancelAfter(StallTimeout);
                            hash.AppendData(buffer, 0, read);
                            await target.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
                            received += read;
                            progress?.Report(new DownloadProgress(DownloadStage.Downloading, received, total));
                        }
                    }
                }
            }
            catch (Exception e) when (e is HttpRequestException or IOException && !cancellationToken.IsCancellationRequested)
            {
                throw new SourceUnavailableException($"{url}: connection lost after {received} bytes: {e.Message}", e);
            }
            catch (OperationCanceledException e) when (silence.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
            {
                throw new SourceUnavailableException(
                    $"{url}: no data for {(int)StallTimeout.TotalSeconds} s after {received} bytes", e);
            }

            return Convert.ToHexStringLower(hash.GetHashAndReset());
        }
    }

    /// <summary>
    /// Extracts the archive into <paramref name="directory"/>. When every entry sits under one top-level folder (Vosk
    /// archives do), that folder is stripped so the model lives directly in <paramref name="directory"/>.
    /// </summary>
    internal static async Task ExtractAsync(
        string archive,
        string directory,
        IProgress<DownloadProgress>? progress,
        CancellationToken cancellationToken)
    {
        using var zip = ZipFile.OpenRead(archive);
        var entries = zip.Entries.Where(e => e.FullName.Length > 0).ToArray();
        var prefix = CommonRootFolder(entries);
        var root = Path.GetFullPath(directory);
        var rootWithSeparator = Path.EndsInDirectorySeparator(root) ? root : root + Path.DirectorySeparatorChar;
        var total = entries.Sum(e => e.Length);
        long done = 0;

        Directory.CreateDirectory(root);
        progress?.Report(new DownloadProgress(DownloadStage.Extracting, 0, total));
        foreach (var entry in entries)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var relative = entry.FullName.Replace('\\', '/')[prefix.Length..];
            if (relative.Length == 0)
            {
                continue;
            }

            var target = Path.GetFullPath(Path.Combine(root, relative));
            if (!target.StartsWith(rootWithSeparator, StringComparison.Ordinal))
            {
                throw new ModelDownloadException($"{archive}: entry '{entry.FullName}' points outside the model directory");
            }

            if (relative.EndsWith('/'))
            {
                Directory.CreateDirectory(target);
                continue;
            }

            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            var source = entry.Open();
            await using (source.ConfigureAwait(false))
            {
                var output = new FileStream(target, FileMode.CreateNew, FileAccess.Write, FileShare.None, BufferSize, useAsync: true);
                await using (output.ConfigureAwait(false))
                {
                    await source.CopyToAsync(output, BufferSize, cancellationToken).ConfigureAwait(false);
                }
            }

            done += entry.Length;
            progress?.Report(new DownloadProgress(DownloadStage.Extracting, done, total));
        }
    }

    private static string CommonRootFolder(IReadOnlyList<ZipArchiveEntry> entries)
    {
        string? first = null;
        var hasNested = false;
        foreach (var entry in entries)
        {
            var name = entry.FullName.Replace('\\', '/');
            var slash = name.IndexOf('/', StringComparison.Ordinal);
            if (slash <= 0)
            {
                return string.Empty;
            }

            var top = name[..(slash + 1)];
            if (first is null)
            {
                first = top;
            }
            else if (!string.Equals(first, top, StringComparison.Ordinal))
            {
                return string.Empty;
            }

            hasNested |= name.Length > top.Length;
        }

        return first is not null && hasNested ? first : string.Empty;
    }

    private void Cleanup(string partial, string directory)
    {
        try
        {
            if (File.Exists(partial))
            {
                File.Delete(partial);
            }

            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            _log($"could not remove partial files of {directory}: {e.Message}");
        }
    }

    private static string StatusText(HttpStatusCode code) => code.ToString();

    /// <summary>Network error or non-2xx answer: the next source may be tried.</summary>
    private sealed class SourceUnavailableException(string message, Exception? inner) : Exception(message, inner);
}
