using System.Collections.Concurrent;
using System.Text.Json;
using Microsoft.Extensions.Time.Testing;
using PitVoice.Core.Events;
using PitVoice.Core.Ptt;
using PitVoice.Core.Settings;
using PitVoice.Core.Tests;
using PitVoice.Models;
using Xunit;

namespace PitVoice.Host.Tests;

/// <summary>An <see cref="AppHost"/> over <see cref="FakePlatform"/>, a temporary model store and settings in memory.</summary>
internal sealed class HostFixture : IAsyncDisposable
{
    public const string RuModel = "vosk-model-small-ru-0.22";
    public const string EnModel = "vosk-model-small-en-us-0.15";

    public static readonly PttKey F13 = PttKey.KeyboardKey(0x7C);
    public static readonly PttBinding KeyBinding = new(F13, "F13");
    public static readonly TimeSpan Wait = TimeSpan.FromSeconds(10);

    private readonly ConcurrentQueue<PipelineEvent> _events = new();
    private readonly SemaphoreSlim _eventArrived = new(0);
    private AppHost? _host;

    public HostFixture(AppSettings? settings = null, params string[] installedModels)
    {
        Root = Path.Combine(Path.GetTempPath(), "pitvoice-host-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Root);
        Models = new ModelStore(Path.Combine(Root, "models"));
        foreach (var model in installedModels.Length == 0 ? [RuModel] : installedModels)
        {
            InstallModel(model);
        }

        Store = new MemorySettingsStore(settings ?? AppSettings.Defaults("ru") with { PttBinding = KeyBinding });
        Platform = new FakePlatform(Time);
    }

    public string Root { get; }

    public FakeTimeProvider Time { get; } = new(new DateTimeOffset(2026, 10, 15, 12, 0, 0, TimeSpan.Zero));

    public ModelStore Models { get; }

    public MemorySettingsStore Store { get; }

    public FakePlatform Platform { get; }

    public string AdditionsDirectory => Path.Combine(Root, "languages");

    public AppHost Host => _host ??= CreateHost();

    public IReadOnlyList<PipelineEvent> Events => [.. _events];

    public void InstallModel(string id)
    {
        var directory = Models.GetModelDirectory(id);
        Directory.CreateDirectory(directory);
        File.WriteAllText(
            Path.Combine(directory, ModelStore.MarkerFileName),
            JsonSerializer.Serialize(new { Id = id, Sha256 = "00", InstalledAt = DateTimeOffset.UnixEpoch }));
    }

    public async Task<AppHost> StartAsync()
    {
        Assert.Equal(HostStartResult.Started, await Host.StartAsync().WaitAsync(Wait));
        return Host;
    }

    /// <summary>Holds the key for longer than the minimum hold and releases it.</summary>
    public void Say(string text)
    {
        Platform.Script.Say(text);
        PressAndRelease();
    }

    public void Press() => Platform.LastSource.Press(F13);

    public void Release()
    {
        Time.Advance(PttStateMachine.MinHold + TimeSpan.FromMilliseconds(50));
        Platform.LastSource.Release(F13);
    }

    public void PressAndRelease()
    {
        Press();
        Release();
    }

    /// <summary>
    /// Waits for an event of type <typeparamref name="T"/> matching <paramref name="match"/>. Virtual time moves on in
    /// small steps meanwhile, so the executor's confirmation polling runs (unless <paramref name="pumpTime"/> is false).
    /// </summary>
    public async Task<T> WaitForAsync<T>(Func<T, bool>? match = null, bool pumpTime = true)
        where T : PipelineEvent
    {
        var deadline = DateTime.UtcNow + Wait;
        while (DateTime.UtcNow < deadline)
        {
            if (_events.OfType<T>().FirstOrDefault(e => match?.Invoke(e) ?? true) is { } found)
            {
                return found;
            }

            if (pumpTime)
            {
                Time.Advance(TimeSpan.FromMilliseconds(10));
            }

            await _eventArrived.WaitAsync(TimeSpan.FromMilliseconds(2));
        }

        throw new TimeoutException($"no {typeof(T).Name} event; got: {string.Join(", ", _events)}");
    }

    public async ValueTask DisposeAsync()
    {
        if (_host is not null)
        {
            await _host.DisposeAsync();
        }

        Directory.Delete(Root, recursive: true);
    }

    private AppHost CreateHost()
    {
        var host = new AppHost(
            new AppHostOptions
            {
                PacksDirectory = TestPacks.Directory,
                AdditionsDirectory = AdditionsDirectory,
                Models = Models,
                UseHostLock = false,
            },
            Store,
            Platform,
            Time);
        host.Events.Subscribe(e =>
        {
            _events.Enqueue(e);
            _eventArrived.Release();
        });
        return host;
    }
}
