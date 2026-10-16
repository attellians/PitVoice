using System.IO;
using System.Net.Http;
using PitVoice.Core.Languages;
using PitVoice.Core.Settings;
using PitVoice.Host;
using PitVoice.Input;
using PitVoice.Models;

namespace PitVoice.App;

/// <summary>Everything the window and the tray work with; one per app run.</summary>
internal sealed class AppServices : IAsyncDisposable
{
    /// <summary>How long "Exit" waits for the host to stop (spec: Exit during processing).</summary>
    public static readonly TimeSpan StopTimeout = TimeSpan.FromSeconds(3);

    private readonly HttpClient _http = new() { Timeout = TimeSpan.FromMinutes(30) };

    public AppServices()
    {
        Store = new JsonSettingsStore(null, SystemLanguage.Detect());
        Platform = new WindowsHostPlatform();
        Host = new AppHost(
            new AppHostOptions { PacksDirectory = PacksDirectory, Models = new ModelStore() },
            Store,
            Platform);
        var (manifest, error) = ModelService.LoadManifest();
        Models = new ModelService(Host, manifest, error, _http);
        Check = new CommandCheck(Host);
    }

    public JsonSettingsStore Store { get; }

    public WindowsHostPlatform Platform { get; }

    public AppHost Host { get; }

    public ModelService Models { get; }

    public CommandCheck Check { get; }

    /// <summary>The built-in packs next to the exe.</summary>
    public static string PacksDirectory => Path.Combine(AppContext.BaseDirectory, "languages");

    /// <summary>Additions file of the current language (spec: User additions in the reference).</summary>
    public string AdditionsFile => UserAdditionsLoader.GetFile(Host.AdditionsDirectory, Host.Settings.Language);

    public async ValueTask DisposeAsync()
    {
        var stopping = Task.Run(async () =>
        {
            Check.Dispose();
            await Models.DisposeAsync().ConfigureAwait(false);
            await Host.DisposeAsync().ConfigureAwait(false);
        });
        try
        {
            await stopping.WaitAsync(StopTimeout).ConfigureAwait(false);
        }
        catch (TimeoutException)
        {
            // The process ends anyway; commands already sent to the game stay applied.
        }

        Platform.Dispose();
        _http.Dispose();
    }
}
