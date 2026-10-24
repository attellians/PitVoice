using CommunityToolkit.Mvvm.ComponentModel;
using PitVoice.App.Localization;
using PitVoice.App.Overlay;
using PitVoice.App.Overlay.Vr;

namespace PitVoice.App.ViewModels;

/// <summary>The state of the indicator in the headset for the settings page.</summary>
internal interface IVrStatus
{
    VrState State { get; }

    /// <summary>The state may have changed; possibly raised on a background thread.</summary>
    event Action? Changed;
}

/// <summary>
/// The "VR mode" row of the "Overlay" page (spec: VR mode setting): the switch saves <c>overlay.vr</c> at
/// once, the state of the indicator in the headset follows the label in the same line (D8).
/// </summary>
internal sealed partial class VrSettingsViewModel : ObservableObject
{
    private readonly IIndicatorControl _control;
    private readonly IVrStatus _status;
    private bool _loading;

    public VrSettingsViewModel(IIndicatorControl control, IVrStatus status)
    {
        _control = control;
        _status = status;
        control.Changed += Load;
        status.Changed += () => Ui.Post(Load);
        Loc.Instance.LanguageChanged += Load;
        Load();
    }

    [ObservableProperty]
    public partial bool Enabled { get; set; }

    [ObservableProperty]
    public partial string StatusText { get; set; } = string.Empty;

    /// <summary>Green "in the headset", red "VR error", gray otherwise (window-visual-style).</summary>
    [ObservableProperty]
    public partial StateTone StatusTone { get; set; }

    [ObservableProperty]
    public partial string SaveError { get; set; } = string.Empty;

    /// <summary>Text and tone of the status line for a state.</summary>
    public static (string Text, StateTone Tone) Describe(VrState state) => (
        Loc.T($"vr.{state}"),
        state switch
        {
            VrState.InHeadset => StateTone.Ok,
            VrState.Failed => StateTone.Problem,
            _ => StateTone.Neutral,
        });

    partial void OnEnabledChanged(bool value)
    {
        if (_loading)
        {
            return;
        }

        _control.Update(o => o with { Vr = value });
        SaveError = _control.SaveError;
        Load();
    }

    /// <summary>Reads the switch and the state again.</summary>
    public void Load()
    {
        _loading = true;
        try
        {
            Enabled = _control.Settings.Vr;
            (StatusText, StatusTone) = Describe(_status.State);
            SaveError = _control.SaveError;
        }
        finally
        {
            _loading = false;
        }
    }
}
