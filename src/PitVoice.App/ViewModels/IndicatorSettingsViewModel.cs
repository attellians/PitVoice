using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PitVoice.App.Localization;
using PitVoice.App.Overlay;
using PitVoice.Core.Settings;

namespace PitVoice.App.ViewModels;

/// <summary>
/// The "Indicator" block of the settings (spec: Indicator settings, Moving the indicator). Every change is saved at once
/// and applies live; "Move" turns into "Done" in move mode; "Move" and "Reset position" are hidden while the indicator is
/// turned off.
/// </summary>
internal sealed partial class IndicatorSettingsViewModel : ObservableObject
{
    private readonly IIndicatorControl _control;
    private bool _loading;

    public IndicatorSettingsViewModel(IIndicatorControl control)
    {
        _control = control;
        control.Changed += Load;
        Loc.Instance.LanguageChanged += Load;
        Load();
    }

    public ObservableCollection<Choice> Kinds { get; } = [];

    public int MinScale => OverlaySettings.MinScale;

    public int MaxScale => OverlaySettings.MaxScale;

    public int MinOpacity => OverlaySettings.MinOpacity;

    public int MaxOpacity => OverlaySettings.MaxOpacity;

    [ObservableProperty]
    public partial bool Enabled { get; set; }

    [ObservableProperty]
    public partial string? Kind { get; set; }

    [ObservableProperty]
    public partial int Scale { get; set; }

    [ObservableProperty]
    public partial int Opacity { get; set; }

    [ObservableProperty]
    public partial bool Moving { get; set; }

    [ObservableProperty]
    public partial string MoveText { get; set; } = string.Empty;

    [ObservableProperty]
    public partial bool HasCustomPosition { get; set; }

    [ObservableProperty]
    public partial string SaveError { get; set; } = string.Empty;

    partial void OnEnabledChanged(bool value) => Save(o => o with { Enabled = value });

    partial void OnKindChanged(string? value)
    {
        if (Enum.TryParse<IndicatorKind>(value, out var kind))
        {
            Save(o => o with { Kind = kind });
        }
    }

    partial void OnScaleChanged(int value) =>
        Save(o => o with { Scale = Math.Clamp(value, OverlaySettings.MinScale, OverlaySettings.MaxScale) });

    partial void OnOpacityChanged(int value) =>
        Save(o => o with { Opacity = Math.Clamp(value, OverlaySettings.MinOpacity, OverlaySettings.MaxOpacity) });

    /// <summary>"Move" / "Done".</summary>
    [RelayCommand]
    private void ToggleMove() => _control.Moving = !_control.Moving;

    [RelayCommand]
    private void ResetPosition() => Save(o => o with { Position = null }, force: true);

    private void Save(Func<OverlaySettings, OverlaySettings> change, bool force = false)
    {
        if (_loading && !force)
        {
            return;
        }

        _control.Update(change);
        SaveError = _control.SaveError;
    }

    private void Load()
    {
        _loading = true;
        try
        {
            var settings = _control.Settings;
            Fill(Kinds, Enum.GetValues<IndicatorKind>().Select(k => new Choice(k.ToString(), Loc.T($"overlayKind.{k}"))));
            Enabled = settings.Enabled;
            Kind = settings.Kind.ToString();
            Scale = settings.Scale;
            Opacity = settings.Opacity;
            HasCustomPosition = settings.Position is not null;
            Moving = _control.Moving;
            MoveText = Loc.T(Moving ? "OverlayDone" : "OverlayMove");
            SaveError = _control.SaveError;
        }
        finally
        {
            _loading = false;
        }
    }

    private static void Fill(ObservableCollection<Choice> target, IEnumerable<Choice> items)
    {
        var list = items.ToList();
        if (target.SequenceEqual(list))
        {
            return;
        }

        target.Clear();
        foreach (var item in list)
        {
            target.Add(item);
        }
    }
}
