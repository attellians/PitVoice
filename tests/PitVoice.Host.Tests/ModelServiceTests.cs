using PitVoice.Core.Settings;
using PitVoice.Models;
using PitVoice.Models.Tests;
using Xunit;

namespace PitVoice.Host.Tests;

/// <summary>Models from the window (design decision 13, spec: Models in the window, Delete model from the window).</summary>
public sealed class ModelServiceTests
{
    private static readonly byte[] Archive = Archives.VoskLike(HostFixture.RuModel, padding: 20_000);
    private static readonly ModelEntry RuEntry = Archives.Entry(HostFixture.RuModel, Archive);

    private static ModelManifest Manifest() => ModelManifest.Parse(
        $$"""
        {
          "models": [
            { "id": "{{RuEntry.Id}}", "type": "asr", "language": "ru", "url": "{{RuEntry.Url}}", "sizeBytes": {{RuEntry.SizeBytes}},
              "sha256": "{{RuEntry.Sha256}}", "license": "Apache-2.0", "source": "https://alphacephei.com/vosk/models" },
            { "id": "{{HostFixture.EnModel}}", "type": "asr", "language": "en", "url": "https://primary.example/en.zip", "sizeBytes": 1,
              "sha256": "{{new string('0', 64)}}", "license": "Apache-2.0", "source": "https://alphacephei.com/vosk/models" }
          ]
        }
        """,
        "manifest.json");

    private static HostFixture WithoutModels() =>
        new(AppSettings.Defaults("ru") with { PttBinding = HostFixture.KeyBinding }, installedModels: [HostFixture.EnModel]);

    [Fact]
    public async Task Models_of_the_language_with_size_license_and_state()
    {
        await using var fixture = WithoutModels();
        var host = await fixture.StartAsync();
        await using var models = new ModelService(host, Manifest(), null, new HttpClient(new FakeHttpHandler()));

        var item = Assert.Single(models.ForLanguage("ru"));

        Assert.Equal(HostFixture.RuModel, item.Entry.Id);
        Assert.Equal(Archive.Length, item.Entry.SizeBytes);
        Assert.Equal("Apache-2.0", item.Entry.License);
        Assert.Equal(ModelState.NotInstalled, item.State);
        Assert.Equal(ModelState.Installed, Assert.Single(models.ForLanguage("en")).State);
        Assert.Equal([HostFixture.RuModel, HostFixture.EnModel], models.All().Select(m => m.Entry.Id));
    }

    [Fact]
    public async Task Download_shows_progress_and_the_model_is_used_without_a_restart()
    {
        await using var fixture = WithoutModels();
        var host = await fixture.StartAsync();
        Assert.Equal(HostState.NoModel, host.Status.State);
        var handler = new FakeHttpHandler().Serve(RuEntry.Url, Archive, chunkSize: 4096);
        await using var models = new ModelService(host, Manifest(), null, new HttpClient(handler));
        var progress = new List<DownloadProgress>();
        models.Changed += () =>
        {
            if (models.ForLanguage("ru")[0] is { State: ModelState.Downloading, Progress: { } p })
            {
                lock (progress)
                {
                    progress.Add(p);
                }
            }
        };

        await models.DownloadAsync(HostFixture.RuModel).WaitAsync(HostFixture.Wait);

        Assert.Contains(progress, p => p.Percent is > 0 and < 100);
        Assert.Equal(ModelState.InUse, models.ForLanguage("ru")[0].State);
        Assert.NotEqual(HostState.NoModel, host.Status.State);
        Assert.NotNull(host.Engine);
    }

    [Fact]
    public async Task Download_without_connection_shows_the_error_and_leaves_nothing()
    {
        await using var fixture = WithoutModels();
        var host = await fixture.StartAsync();
        await using var models = new ModelService(host, Manifest(), null, new HttpClient(new FakeHttpHandler()));

        await models.DownloadAsync(HostFixture.RuModel).WaitAsync(HostFixture.Wait);

        var item = models.ForLanguage("ru")[0];
        Assert.Equal(ModelState.NotInstalled, item.State);
        Assert.Contains("no connection", item.Error, StringComparison.Ordinal);
        Assert.False(File.Exists(fixture.Models.GetPartialFile(HostFixture.RuModel)));
        Assert.False(Directory.Exists(fixture.Models.GetModelDirectory(HostFixture.RuModel)));
    }

    [Fact]
    public async Task Cancel_in_the_middle_leaves_nothing()
    {
        await using var fixture = WithoutModels();
        var host = await fixture.StartAsync();
        ModelService? models = null;
        var handler = new FakeHttpHandler().Serve(RuEntry.Url, Archive, chunkSize: 1024, onChunk: n =>
        {
            if (n == 3)
            {
                models!.Cancel(HostFixture.RuModel);
            }
        });
        models = new ModelService(host, Manifest(), null, new HttpClient(handler));
        await using (models)
        {
            await models.DownloadAsync(HostFixture.RuModel).WaitAsync(HostFixture.Wait);

            var item = models.ForLanguage("ru")[0];
            Assert.Equal(ModelState.NotInstalled, item.State);
            Assert.Null(item.Error);
            Assert.False(File.Exists(fixture.Models.GetPartialFile(HostFixture.RuModel)));
        }
    }

    [Fact]
    public async Task Stopping_the_host_cancels_downloads_and_removes_partial_files()
    {
        await using var fixture = WithoutModels();
        var host = await fixture.StartAsync();
        var started = new TaskCompletionSource();
        var handler = new FakeHttpHandler().Serve(RuEntry.Url, Archive, chunkSize: 512, onChunk: n =>
        {
            started.TrySetResult();
            Thread.Sleep(5);
        });
        var models = new ModelService(host, Manifest(), null, new HttpClient(handler));
        var download = models.DownloadAsync(HostFixture.RuModel);
        await started.Task.WaitAsync(HostFixture.Wait);

        await models.DisposeAsync().AsTask().WaitAsync(HostFixture.Wait);

        Assert.True(download.IsCompleted);
        Assert.False(File.Exists(fixture.Models.GetPartialFile(HostFixture.RuModel)));
        Assert.False(fixture.Models.IsInstalled(HostFixture.RuModel));
    }

    [Fact]
    public async Task Delete_model_in_use_unloads_it_and_gives_no_model()
    {
        await using var fixture = new HostFixture();
        var host = await fixture.StartAsync();
        await using var models = new ModelService(host, Manifest(), null, new HttpClient(new FakeHttpHandler()));
        Assert.Equal(ModelState.InUse, models.ForLanguage("ru")[0].State);

        var result = await models.DeleteAsync(HostFixture.RuModel).WaitAsync(HostFixture.Wait);

        Assert.True(result.Deleted);
        Assert.False(Directory.Exists(fixture.Models.GetModelDirectory(HostFixture.RuModel)));
        Assert.True(fixture.Platform.Engines.Single().Disposed);
        Assert.Equal((HostState.NoModel, StatusReason.ModelNotInstalled), (host.Status.State, host.Status.Reason));
        fixture.PressAndRelease();
        var unavailable = await fixture.WaitForAsync<PttUnavailable>();
        Assert.Equal(StatusReason.ModelNotInstalled, unavailable.Reason);
    }

    [WindowsFact]
    public async Task Locked_files_keep_the_model_and_load_it_again()
    {
        await using var fixture = new HostFixture();
        var host = await fixture.StartAsync();
        await using var models = new ModelService(host, Manifest(), null, new HttpClient(new FakeHttpHandler()));
        var locked = Path.Combine(fixture.Models.GetModelDirectory(HostFixture.RuModel), "locked.bin");
        await using (var file = new FileStream(locked, FileMode.Create, FileAccess.ReadWrite, FileShare.None))
        {
            var result = await models.DeleteAsync(HostFixture.RuModel).WaitAsync(HostFixture.Wait);

            Assert.False(result.Deleted);
            Assert.NotNull(result.Error);
        }

        Assert.NotEqual(HostState.NoModel, host.Status.State);
        Assert.Equal(2, fixture.Platform.Engines.Count);
        Assert.NotNull(host.Engine);
    }
}
