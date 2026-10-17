using System.IO.Compression;
using System.Net;
using System.Security.Cryptography;

namespace PitVoice.Models.Tests;

/// <summary>A temporary directory removed after the test.</summary>
internal sealed class TempDirectory : IDisposable
{
    public TempDirectory()
    {
        Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "pitvoice-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path);
    }

    public string Path { get; }

    public void Dispose()
    {
        if (Directory.Exists(Path))
        {
            Directory.Delete(Path, recursive: true);
        }
    }
}

/// <summary>Serves canned responses per URL and counts requests.</summary>
internal sealed class FakeHttpHandler : HttpMessageHandler
{
    private readonly Dictionary<Uri, Func<HttpResponseMessage>> _routes = [];
    private readonly HashSet<Uri> _hanging = [];

    public List<Uri> Requests { get; } = [];

    public FakeHttpHandler Serve(Uri url, byte[] body, int chunkSize = 1024, Action<int>? onChunk = null)
    {
        _routes[url] = () => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StreamContent(new ChunkedStream(body, chunkSize, onChunk)) { Headers = { ContentLength = body.Length } },
        };
        return this;
    }

    public FakeHttpHandler Fail(Uri url, HttpStatusCode status)
    {
        _routes[url] = () => new HttpResponseMessage(status);
        return this;
    }

    /// <summary>Accepts the request and never answers, like a source behind a proxy that drops it.</summary>
    public FakeHttpHandler Hang(Uri url)
    {
        _hanging.Add(url);
        return this;
    }

    /// <summary>Answers and sends <paramref name="sendBytes"/> of <paramref name="body"/>, then sends nothing more.</summary>
    public FakeHttpHandler Stall(Uri url, byte[] body, int sendBytes)
    {
        _routes[url] = () => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StreamContent(new StallingStream(body, sendBytes, this)) { Headers = { ContentLength = body.Length } },
        };
        return this;
    }

    /// <summary>Set when a request waits for its answer or for data; tests move the clock on after it.</summary>
    public SemaphoreSlim Waiting { get; } = new(0);

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Requests.Add(request.RequestUri!);
        if (_hanging.Contains(request.RequestUri!))
        {
            Waiting.Release();
            await Task.Delay(Timeout.Infinite, cancellationToken);
        }

        if (_routes.TryGetValue(request.RequestUri!, out var route))
        {
            return route();
        }

        throw new HttpRequestException($"no route to {request.RequestUri!.Host}");
    }

    /// <summary>Gives the first bytes, then waits until the read is cancelled.</summary>
    private sealed class StallingStream(byte[] data, int sendBytes, FakeHttpHandler owner) : MemoryStream(data, 0, sendBytes, writable: false)
    {
        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            var read = await base.ReadAsync(buffer, cancellationToken);
            if (read > 0)
            {
                return read;
            }

            owner.Waiting.Release();
            await Task.Delay(Timeout.Infinite, cancellationToken);
            return 0;
        }
    }

    /// <summary>Returns data in small chunks and calls back after each chunk, so tests can cancel mid-download.</summary>
    private sealed class ChunkedStream(byte[] data, int chunkSize, Action<int>? onChunk) : MemoryStream(data, writable: false)
    {
        private int _chunks;

        public override int Read(byte[] buffer, int offset, int count) => base.Read(buffer, offset, Math.Min(count, chunkSize));

        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (_chunks > 0)
            {
                onChunk?.Invoke(_chunks);
            }

            _chunks++;
            cancellationToken.ThrowIfCancellationRequested();
            return await base.ReadAsync(buffer[..Math.Min(buffer.Length, chunkSize)], cancellationToken);
        }
    }
}

/// <summary>Synchronous <see cref="IProgress{T}"/> (the BCL one posts to the thread pool).</summary>
internal sealed class SyncProgress<T>(Action<T> report) : IProgress<T>
{
    public void Report(T value) => report(value);
}

internal static class Archives
{
    /// <summary>Builds a zip in memory. Names ending with '/' become directory entries.</summary>
    public static byte[] Zip(params (string Name, string Content)[] files)
    {
        using var memory = new MemoryStream();
        using (var zip = new ZipArchive(memory, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var (name, content) in files)
            {
                var entry = zip.CreateEntry(name, CompressionLevel.NoCompression);
                if (!name.EndsWith('/'))
                {
                    using var writer = new StreamWriter(entry.Open());
                    writer.Write(content);
                }
            }
        }

        return memory.ToArray();
    }

    /// <summary>A Vosk-like archive: one root folder with model files.</summary>
    public static byte[] VoskLike(string root, int padding = 0) => Zip(
        ($"{root}/", string.Empty),
        ($"{root}/am/final.mdl", "acoustic model" + new string('x', padding)),
        ($"{root}/conf/model.conf", "--sample-frequency=16000"),
        ($"{root}/graph/HCLr.fst", "graph" + new string('y', padding)),
        ($"{root}/README", "readme"));

    public static string Sha256(byte[] data) => Convert.ToHexStringLower(SHA256.HashData(data));

    public static ModelEntry Entry(string id, byte[] archive, Uri? mirror = null) => new(
        id,
        ModelType.Asr,
        "ru",
        new Uri($"https://primary.example/{id}.zip"),
        mirror,
        archive.Length,
        Sha256(archive),
        "Apache-2.0",
        "https://primary.example");
}
