using System.Net;
using Xunit;

namespace PitVoice.Models.Tests;

public sealed class ModelDownloaderTests
{
    private static readonly Uri MirrorUrl = new("https://mirror.example/m.zip");

    [Fact]
    public async Task Download_Success_ExtractsStripsRootAndWritesMarker()
    {
        using var temp = new TempDirectory();
        var archive = Archives.VoskLike("vosk-model-test");
        var entry = Archives.Entry("vosk-model-test", archive);
        var handler = new FakeHttpHandler().Serve(entry.Url, archive);
        var store = new ModelStore(temp.Path);
        var reports = new List<DownloadProgress>();

        var outcome = await Downloader(handler, store).DownloadAsync(entry, new SyncProgress<DownloadProgress>(reports.Add));

        Assert.Equal(DownloadOutcome.Installed, outcome);
        var directory = store.GetModelDirectory(entry.Id);
        Assert.True(File.Exists(Path.Combine(directory, "am", "final.mdl")));
        Assert.True(File.Exists(Path.Combine(directory, "conf", "model.conf")));
        Assert.True(store.TryGetInstalled(entry.Id, out var installed));
        Assert.Equal(entry.Sha256, installed.Sha256);
        Assert.False(File.Exists(store.GetPartialFile(entry.Id)));
        var last = reports.Last(r => r.Stage == DownloadStage.Downloading);
        Assert.Equal(archive.Length, last.BytesReceived);
        Assert.Equal(100.0, last.Percent);
    }

    [Fact]
    public async Task Download_ChecksumMismatch_DeletesArchiveAndNamesBothSums()
    {
        using var temp = new TempDirectory();
        var archive = Archives.VoskLike("m");
        var entry = Archives.Entry("m", archive) with { Sha256 = new string('0', 64) };
        var handler = new FakeHttpHandler().Serve(entry.Url, archive);
        var store = new ModelStore(temp.Path);

        var error = await Assert.ThrowsAsync<ModelDownloadException>(() => Downloader(handler, store).DownloadAsync(entry));

        Assert.Contains("'m'", error.Message, StringComparison.Ordinal);
        Assert.Contains(new string('0', 64), error.Message, StringComparison.Ordinal);
        Assert.Contains(Archives.Sha256(archive), error.Message, StringComparison.Ordinal);
        AssertNothingLeft(store, "m");
    }

    [Fact]
    public async Task Download_PrimaryFails_UsesMirrorAndLogs()
    {
        using var temp = new TempDirectory();
        var archive = Archives.VoskLike("m");
        var entry = Archives.Entry("m", archive, MirrorUrl);
        var handler = new FakeHttpHandler().Fail(entry.Url, HttpStatusCode.ServiceUnavailable).Serve(MirrorUrl, archive);
        var store = new ModelStore(temp.Path);
        var log = new List<string>();

        var outcome = await Downloader(handler, store, log.Add).DownloadAsync(entry);

        Assert.Equal(DownloadOutcome.Installed, outcome);
        Assert.Equal([entry.Url, MirrorUrl], handler.Requests);
        Assert.Contains(log, l => l.Contains("mirror", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Download_PrimaryUnreachable_UsesMirror()
    {
        using var temp = new TempDirectory();
        var archive = Archives.VoskLike("m");
        var entry = Archives.Entry("m", archive, MirrorUrl);
        var handler = new FakeHttpHandler().Serve(MirrorUrl, archive);
        var store = new ModelStore(temp.Path);

        Assert.Equal(DownloadOutcome.Installed, await Downloader(handler, store).DownloadAsync(entry));
    }

    [Fact]
    public async Task Download_EverythingUnreachable_NoConnectionAndNoPartialFiles()
    {
        using var temp = new TempDirectory();
        var entry = Archives.Entry("m", Archives.VoskLike("m"), MirrorUrl);
        var store = new ModelStore(temp.Path);

        var error = await Assert.ThrowsAsync<ModelDownloadException>(() => Downloader(new FakeHttpHandler(), store).DownloadAsync(entry));

        Assert.Contains("no connection", error.Message, StringComparison.Ordinal);
        AssertNothingLeft(store, "m");
    }

    [Fact]
    public async Task Download_NoMirrorAndPrimaryFails_NoConnection()
    {
        using var temp = new TempDirectory();
        var entry = Archives.Entry("m", Archives.VoskLike("m"));
        var handler = new FakeHttpHandler().Fail(entry.Url, HttpStatusCode.NotFound);
        var store = new ModelStore(temp.Path);

        var error = await Assert.ThrowsAsync<ModelDownloadException>(() => Downloader(handler, store).DownloadAsync(entry));

        Assert.Contains("404", error.Message, StringComparison.Ordinal);
        AssertNothingLeft(store, "m");
    }

    [Fact]
    public async Task Download_CancelAfterFirstChunk_LeavesNothing()
    {
        using var temp = new TempDirectory();
        var archive = Archives.VoskLike("m", padding: 20_000);
        var entry = Archives.Entry("m", archive);
        using var cts = new CancellationTokenSource();
        var handler = new FakeHttpHandler().Serve(entry.Url, archive, chunkSize: 1024, onChunk: _ => cts.Cancel());
        var store = new ModelStore(temp.Path);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Downloader(handler, store).DownloadAsync(entry, null, cts.Token));

        AssertNothingLeft(store, "m");
    }

    [Fact]
    public async Task Download_CancelDuringExtraction_RemovesModelDirectory()
    {
        using var temp = new TempDirectory();
        var archive = Archives.VoskLike("m");
        var entry = Archives.Entry("m", archive);
        var handler = new FakeHttpHandler().Serve(entry.Url, archive);
        var store = new ModelStore(temp.Path);
        using var cts = new CancellationTokenSource();
        var progress = new SyncProgress<DownloadProgress>(p =>
        {
            if (p is { Stage: DownloadStage.Extracting, BytesReceived: > 0 })
            {
                cts.Cancel();
            }
        });

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Downloader(handler, store).DownloadAsync(entry, progress, cts.Token));

        AssertNothingLeft(store, "m");
    }

    [Fact]
    public async Task Download_ZipSlip_FailsAndLeavesNothing()
    {
        using var temp = new TempDirectory();
        var archive = Archives.Zip(("ok.txt", "fine"), ("../evil.txt", "escape"));
        var entry = Archives.Entry("m", archive);
        var handler = new FakeHttpHandler().Serve(entry.Url, archive);
        var store = new ModelStore(Path.Combine(temp.Path, "models"));

        var error = await Assert.ThrowsAsync<ModelDownloadException>(() => Downloader(handler, store).DownloadAsync(entry));

        Assert.Contains("outside", error.Message, StringComparison.Ordinal);
        Assert.False(File.Exists(Path.Combine(temp.Path, "evil.txt")));
        AssertNothingLeft(store, "m");
    }

    [Fact]
    public async Task Download_ArchiveWithoutRootFolder_ExtractsAsIs()
    {
        using var temp = new TempDirectory();
        var archive = Archives.Zip(("am/final.mdl", "a"), ("conf/model.conf", "c"));
        var entry = Archives.Entry("m", archive);
        var handler = new FakeHttpHandler().Serve(entry.Url, archive);
        var store = new ModelStore(temp.Path);

        await Downloader(handler, store).DownloadAsync(entry);

        Assert.True(File.Exists(Path.Combine(store.GetModelDirectory("m"), "am", "final.mdl")));
    }

    [Fact]
    public async Task Download_AlreadyInstalled_MakesNoRequests()
    {
        using var temp = new TempDirectory();
        var archive = Archives.VoskLike("m");
        var entry = Archives.Entry("m", archive);
        var handler = new FakeHttpHandler().Serve(entry.Url, archive);
        var store = new ModelStore(temp.Path);
        await Downloader(handler, store).DownloadAsync(entry);
        handler.Requests.Clear();

        var outcome = await Downloader(handler, store).DownloadAsync(entry);

        Assert.Equal(DownloadOutcome.AlreadyInstalled, outcome);
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task Download_IncompleteLeftovers_AreReplaced()
    {
        using var temp = new TempDirectory();
        var archive = Archives.VoskLike("m");
        var entry = Archives.Entry("m", archive);
        var handler = new FakeHttpHandler().Serve(entry.Url, archive);
        var store = new ModelStore(temp.Path);
        Directory.CreateDirectory(Path.Combine(store.GetModelDirectory("m"), "am"));
        File.WriteAllText(Path.Combine(store.GetModelDirectory("m"), "am", "final.mdl"), "stale");

        await Downloader(handler, store).DownloadAsync(entry);

        Assert.Equal("acoustic model", File.ReadAllText(Path.Combine(store.GetModelDirectory("m"), "am", "final.mdl")));
    }

    private static ModelDownloader Downloader(FakeHttpHandler handler, ModelStore store, Action<string>? log = null) =>
        new(new HttpClient(handler), store, log);

    private static void AssertNothingLeft(ModelStore store, string id)
    {
        Assert.False(File.Exists(store.GetPartialFile(id)), "partial archive left on disk");
        Assert.False(Directory.Exists(store.GetModelDirectory(id)), "model directory left on disk");
        Assert.False(store.IsInstalled(id));
    }
}
