using PitVoice.App.Localization;
using PitVoice.App.ViewModels;
using PitVoice.Core.Settings;
using PitVoice.Host;

namespace PitVoice.App.Overlay;

/// <summary>What the "Indicator" block of the settings and the tray menu work with.</summary>
internal interface IIndicatorControl
{
    OverlaySettings Settings { get; }

    /// <summary>Move mode (design D7): not saved.</summary>
    bool Moving { get; set; }

    /// <summary>The last failed save of the indicator settings or its position, or empty.</summary>
    string SaveError { get; }

    /// <summary>Settings, move mode or the save error changed; raised on the UI thread.</summary>
    event Action? Changed;

    /// <summary>Saves a change at once; it applies even when the file cannot be written.</summary>
    SettingsSaveResult Update(Func<OverlaySettings, OverlaySettings> change);
}

/// <summary>The indicator settings of the host and the move mode of the model.</summary>
internal sealed class IndicatorControl : IIndicatorControl, IDisposable
{
    private readonly AppHost _host;
    private readonly IndicatorModel _model;
    private bool _moving;

    public IndicatorControl(AppHost host, IndicatorModel model)
    {
        _host = host;
        _model = model;
        _host.SettingsChanged += OnSettingsChanged;
        _model.Changed += OnViewChanged;
    }

    public event Action? Changed;

    public OverlaySettings Settings => _host.Settings.Overlay;

    public bool Moving
    {
        get => _model.Moving;
        set => _model.Moving = value;
    }

    public string SaveError { get; private set; } = string.Empty;

    public SettingsSaveResult Update(Func<OverlaySettings, OverlaySettings> change)
    {
        ArgumentNullException.ThrowIfNull(change);
        var result = _host.Update(s => s with { Overlay = change(s.Overlay) });
        Report(result);
        return result;
    }

    /// <summary>A failed save is shown like for the other settings (spec: Settings file not writable).</summary>
    public void Report(SettingsSaveResult result)
    {
        var error = result.Saved ? string.Empty : Loc.F("SaveFailed", result.Error);
        if (error != SaveError)
        {
            SaveError = error;
            Ui.Post(() => Changed?.Invoke());
        }
    }

    public void Dispose()
    {
        _host.SettingsChanged -= OnSettingsChanged;
        _model.Changed -= OnViewChanged;
    }

    private void OnSettingsChanged(AppSettings settings) => Ui.Post(() => Changed?.Invoke());

    private void OnViewChanged(IndicatorView view)
    {
        if (view.Moving != _moving)
        {
            _moving = view.Moving;
            Ui.Post(() => Changed?.Invoke());
        }
    }
}
