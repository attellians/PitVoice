using PitVoice.App.Localization;
using PitVoice.App.Overlay;
using PitVoice.App.ViewModels;
using PitVoice.Core.Settings;
using PitVoice.Host;
using Xunit;

namespace PitVoice.App.Tests;

/// <summary>The "Indicator" block of the settings (spec: Indicator settings, Moving the indicator).</summary>
public sealed class IndicatorSettingsViewModelTests
{
    [Fact]
    public void Shows_the_settings_and_applies_changes_at_once()
    {
        var control = new FakeControl();
        var model = new IndicatorSettingsViewModel(control);
        Assert.True(model.Enabled);
        Assert.Equal("A", model.Kind);
        Assert.Equal(100, model.Scale);
        Assert.Equal(90, model.Opacity);

        model.Scale = 150;
        model.Opacity = 40;
        model.Kind = "B";

        Assert.Equal(
            OverlaySettings.Default with { Scale = 150, Opacity = 40, Kind = IndicatorKind.B },
            control.Settings);
        Assert.Equal(3, control.Saves);
    }

    [Fact]
    public void Loading_does_not_save()
    {
        var control = new FakeControl { Settings = OverlaySettings.Default with { Scale = 125 } };

        var model = new IndicatorSettingsViewModel(control);
        control.Settings = control.Settings with { Opacity = 50 };
        control.Raise();

        Assert.Equal(50, model.Opacity);
        Assert.Equal(0, control.Saves);
    }

    [Fact]
    public void Reset_position_clears_the_saved_place()
    {
        var control = new FakeControl { Settings = OverlaySettings.Default with { Position = new ScreenPoint(10, 20) } };
        var model = new IndicatorSettingsViewModel(control);
        Assert.True(model.HasCustomPosition);

        model.ResetPositionCommand.Execute(null);

        Assert.Null(control.Settings.Position);
        Assert.False(model.HasCustomPosition);
    }

    [Fact]
    public void Move_button_turns_into_done()
    {
        var control = new FakeControl();
        var model = new IndicatorSettingsViewModel(control);
        Assert.Equal(Loc.T("OverlayMove"), model.MoveText);

        model.ToggleMoveCommand.Execute(null);
        Assert.True(control.Moving);
        Assert.Equal(Loc.T("OverlayDone"), model.MoveText);

        model.ToggleMoveCommand.Execute(null);
        Assert.False(control.Moving);
        Assert.Equal(Loc.T("OverlayMove"), model.MoveText);
    }

    /// <summary>Spec: Turn off while showing a phrase — move mode ends, "Move" and the rest are hidden (bound to Enabled).</summary>
    [Fact]
    public void Turn_off_ends_move_mode_and_hides_the_buttons()
    {
        var control = new FakeControl();
        var model = new IndicatorSettingsViewModel(control);
        model.ToggleMoveCommand.Execute(null);

        model.Enabled = false;

        Assert.False(control.Settings.Enabled);
        Assert.False(control.Moving);
        Assert.False(model.Enabled);
        Assert.False(model.Moving);
    }

    [Fact]
    public void Failed_save_is_shown_and_the_change_still_applies()
    {
        var control = new FakeControl { SaveFails = true };
        var model = new IndicatorSettingsViewModel(control);

        model.Scale = 175;

        Assert.Equal(175, control.Settings.Scale);
        Assert.Contains("read-only", model.SaveError, StringComparison.Ordinal);
    }

    /// <summary>Behaves like <see cref="IndicatorControl"/>: changes are raised at once, move mode ends with the indicator.</summary>
    private sealed class FakeControl : IIndicatorControl
    {
        private bool _moving;

        public event Action? Changed;

        public OverlaySettings Settings { get; set; } = OverlaySettings.Default;

        public bool Moving
        {
            get => _moving;
            set
            {
                _moving = value && Settings.Enabled;
                Raise();
            }
        }

        public string SaveError { get; private set; } = string.Empty;

        public bool SaveFails { get; init; }

        public int Saves { get; private set; }

        public SettingsSaveResult Update(Func<OverlaySettings, OverlaySettings> change)
        {
            Settings = change(Settings);
            _moving &= Settings.Enabled;
            Saves++;
            var result = SaveFails ? new SettingsSaveResult(false, "file is read-only") : SettingsSaveResult.Ok;
            SaveError = result.Saved ? string.Empty : Loc.F("SaveFailed", result.Error);
            Raise();
            return result;
        }

        public void Raise() => Changed?.Invoke();
    }
}
