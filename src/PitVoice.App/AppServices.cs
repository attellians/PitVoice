using System.IO;
using System.Net.Http;
using PitVoice.App.Localization;
using PitVoice.App.Overlay;
using PitVoice.App.Overlay.Vr;
using PitVoice.App.ViewModels;
using PitVoice.Core.Settings;
using PitVoice.Host;
using PitVoice.Host.Diagnostics;
using PitVoice.Input;
using PitVoice.Models;
using Serilog;

namespace PitVoice.App;

/// <summary>Everything the window and the tray work with; one per app run.</summary>
internal sealed class AppServices : IAsyncDisposable
{
    /// <summary>How long "Exit" waits for the host to stop (spec: Exit during processing).</summary>
    public static readonly TimeSpan StopTimeout = TimeSpan.FromSeconds(3);

    private readonly HttpClient _http = new() { Timeout = TimeSpan.FromMinutes(30) };
    private VrHelperLauncher? _vrLauncher;
    private VrIndicator? _vrIndicator;

    /// <param name="log">The application log; the app owns it and closes it after this object.</param>
    /// <param name="crashes">Crash reports; installed by the app before this object is created.</param>
    public AppServices(ILogger log, CrashReporter crashes)
    {
        Log = log;
        Crashes = crashes;
        Store = new JsonSettingsStore(null, SystemLanguage.Detect());
        Platform = new WindowsHostPlatform(new SerilogExecutionLog(log));
        Host = new AppHost(
            new AppHostOptions { PacksDirectory = PacksDirectory, Models = new ModelStore(), Log = log },
            Store,
            Platform);
        var (manifest, error) = ModelService.LoadManifest();
        Models = new ModelService(Host, manifest, error, _http);
        Diagnostics = new DiagnosticsActions(Host);
    }

    public ILogger Log { get; }

    public CrashReporter Crashes { get; }

    public DiagnosticsActions Diagnostics { get; }

    public JsonSettingsStore Store { get; }

    public WindowsHostPlatform Platform { get; }

    public AppHost Host { get; }

    public ModelService Models { get; }

    /// <summary>State of the indicator above the game; created by <see cref="StartIndicator"/> once the host runs.</summary>
    public IndicatorModel? Indicator { get; private set; }

    /// <summary>Indicator settings and move mode for the settings page and the tray.</summary>
    public IndicatorControl? IndicatorControl { get; private set; }

    /// <summary>"VR mode" and the state of the indicator in the headset; created by <see cref="StartIndicator"/>.</summary>
    public VrMode? Vr { get; private set; }

    /// <summary>Attaches the indicator to the started host; call on the UI thread.</summary>
    public OverlayController StartIndicator()
    {
        var indicator = Indicator = Host.CreateIndicator();
        var control = IndicatorControl = new IndicatorControl(Host, indicator);
        var overlay = new OverlayController(indicator, control);

        // Nothing of VR is created or loaded until the mode is on (add-vr-overlay D1, D9).
        var vr = Vr = new VrMode(
            () => new VrSupervisor(_vrLauncher ??= new VrHelperLauncher(), new SteamVrProbe(), TimeProvider.System, line => Log.Information("{VrEvent}", line)),
            AttachVr);
        control.Changed += () =>
        {
            vr.Apply(control.Settings);
            UpdateVr();
        };
        indicator.Changed += _ => Ui.Post(UpdateVr);
        Loc.Instance.LanguageChanged += UpdateVr;
        overlay.Dragged += (center, monitor) => _vrIndicator?.MoveTo(center, monitor);
        vr.Apply(control.Settings);
        return overlay;
    }

    private void AttachVr(VrSupervisor? supervisor)
    {
        _vrIndicator?.Dispose();
        _vrIndicator = supervisor is null ? null : new VrIndicator(supervisor, OverlayNative.GetMonitors);
        UpdateVr();
    }

    private void UpdateVr()
    {
        if (_vrIndicator is { } vr && Indicator is { } indicator && IndicatorControl is { } control)
        {
            vr.Update(indicator.View, control.Settings, Loc.Instance.Language, OverlayController.Word);
        }
    }

    /// <summary>The built-in packs next to the exe.</summary>
    public static string PacksDirectory => Path.Combine(AppContext.BaseDirectory, "languages");

    public async ValueTask DisposeAsync()
    {
        // The helper gets its input closed and removes the overlay; the job object ends it with the app in any case.
        Loc.Instance.LanguageChanged -= UpdateVr;
        Vr?.Dispose();
        _vrLauncher?.Dispose();
        IndicatorControl?.Dispose();
        Indicator?.Dispose();
        var stopping = Task.Run(async () =>
        {
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
