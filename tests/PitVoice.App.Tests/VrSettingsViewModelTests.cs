using PitVoice.App.Localization;
using PitVoice.App.Overlay;
using PitVoice.App.Overlay.Vr;
using PitVoice.App.ViewModels;
using PitVoice.Core.Settings;
using PitVoice.Host;
using Xunit;

namespace PitVoice.App.Tests;

/// <summary>The "VR mode" row of the settings (add-vr-overlay spec: VR mode setting; D8).</summary>
[Collection("Interface language")]
public sealed class VrSettingsViewModelTests
{
    [Theory]
    [InlineData("Off", "ru", "выключен", "Neutral")]
    [InlineData("OverlayOff", "ru", "индикатор выключен в разделе «Оверлей»", "Neutral")]
    [InlineData("WaitingSteamVr", "ru", "ждёт SteamVR", "Neutral")]
    [InlineData("Starting", "ru", "подключение…", "Neutral")]
    [InlineData("InHeadset", "ru", "в шлеме", "Ok")]
    [InlineData("Failed", "ru", "ошибка VR", "Problem")]
    [InlineData("WaitingSteamVr", "en", "waiting for SteamVR", "Neutral")]
    [InlineData("InHeadset", "en", "in the headset", "Ok")]
    [InlineData("Failed", "en", "VR error", "Problem")]
    public void Status_line_names_every_state(string state, string language, string text, string tone)
    {
        Loc.Instance.SetLanguage(language);
        try
        {
            var model = new VrSettingsViewModel(new FakeControl(), new FakeStatus { State = Enum.Parse<VrState>(state) });

            Assert.Equal(text, model.StatusText);
            Assert.Equal(Enum.Parse<StateTone>(tone), model.StatusTone);
        }
        finally
        {
            Loc.Instance.SetLanguage(Loc.Fallback);
        }
    }

    [Fact]
    public void Every_state_has_a_text_in_both_languages()
    {
        foreach (var language in Loc.Languages)
        {
            var strings = Loc.LoadDictionary(language)!;
            Assert.All(Enum.GetValues<VrState>(), s => Assert.True(strings.ContainsKey($"vr.{s}"), $"{language}: vr.{s}"));
            Assert.True(strings.ContainsKey("VrLabel") && strings.ContainsKey("VrHint"), language);
        }
    }

    /// <summary>Spec: Status line — on at once, "waiting for SteamVR" right after the switch.</summary>
    [Fact]
    public void Switch_saves_the_mode_and_shows_the_new_state_at_once()
    {
        var control = new FakeControl();
        var status = new FakeStatus();
        control.Saved += s => status.State = s.Vr ? VrState.WaitingSteamVr : VrState.Off;
        var model = new VrSettingsViewModel(control, status);
        Assert.False(model.Enabled);

        model.Enabled = true;

        Assert.True(control.Settings.Vr);
        Assert.Equal(1, control.Saves);
        Assert.Equal(Loc.T("vr.WaitingSteamVr"), model.StatusText);
    }

    [Fact]
    public void Loading_does_not_save_and_follows_the_state()
    {
        var control = new FakeControl { Settings = OverlaySettings.Default with { Vr = true } };
        var status = new FakeStatus { State = VrState.Starting };
        var model = new VrSettingsViewModel(control, status);

        status.State = VrState.InHeadset;
        model.Load();

        Assert.True(model.Enabled);
        Assert.Equal(StateTone.Ok, model.StatusTone);
        Assert.Equal(0, control.Saves);
    }

    /// <summary>Spec: File not writable — the error is shown, the mode applies until exit.</summary>
    [Fact]
    public void Failed_save_is_shown_and_the_mode_still_applies()
    {
        var control = new FakeControl { SaveFails = true };
        var model = new VrSettingsViewModel(control, new FakeStatus());

        model.Enabled = true;

        Assert.True(control.Settings.Vr);
        Assert.True(model.Enabled);
        Assert.Contains("read-only", model.SaveError, StringComparison.Ordinal);
    }

    private sealed class FakeStatus : IVrStatus
    {
        public event Action? Changed;

        public VrState State { get; set; }

        public void Raise() => Changed?.Invoke();
    }

    private sealed class FakeControl : IIndicatorControl
    {
        public event Action? Changed;

        public event Action<OverlaySettings>? Saved;

        public OverlaySettings Settings { get; set; } = OverlaySettings.Default;

        public bool Moving { get; set; }

        public string SaveError { get; private set; } = string.Empty;

        public bool SaveFails { get; init; }

        public int Saves { get; private set; }

        public SettingsSaveResult Update(Func<OverlaySettings, OverlaySettings> change)
        {
            Settings = change(Settings);
            Saves++;
            Saved?.Invoke(Settings);
            var result = SaveFails ? new SettingsSaveResult(false, "file is read-only") : SettingsSaveResult.Ok;
            SaveError = result.Saved ? string.Empty : Loc.F("SaveFailed", result.Error);
            Changed?.Invoke();
            return result;
        }
    }
}
