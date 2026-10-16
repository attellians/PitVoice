using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PitVoice.App.Localization;
using PitVoice.Core;
using PitVoice.Core.Audio;
using PitVoice.Core.Ptt;
using PitVoice.Core.Settings;
using PitVoice.Host;

namespace PitVoice.App.ViewModels;

/// <summary>A choice of a combo box: <see cref="Key"/> is stored, <see cref="Display"/> is shown.</summary>
internal sealed record Choice(string Key, string Display);

/// <summary>
/// "Settings" (spec: Settings in the window, Device lists, Cue preview, Language switch, PTT assignment in the window). Every change is saved at once through the host; a failed save is shown and still applies.
/// </summary>
internal sealed partial class SettingsViewModel : ObservableObject
{
    /// <summary>Delay before a volume change is saved, so dragging the slider writes the file once (design decision 2).</summary>
    public static readonly TimeSpan VolumeDelay = TimeSpan.FromMilliseconds(300);

    private const string DefaultDevice = "";

    private readonly AppServices _services;
    private readonly Action _openModels;
    private readonly DispatcherTimer _volumeTimer;
    private bool _loading;

    /// <summary>The "Diagnostics" block (spec: Diagnostics section in the settings).</summary>
    public DiagnosticsActions Diagnostics => _services.Diagnostics;

    public SettingsViewModel(AppServices services, Action openModels)
    {
        _services = services;
        _openModels = openModels;
        _volumeTimer = new DispatcherTimer { Interval = VolumeDelay };
        _volumeTimer.Tick += (_, _) =>
        {
            _volumeTimer.Stop();
            Save(s => s with { CueVolume = Volume });
        };
        services.Host.StatusChanged += _ => Ui.Post(RefreshStatus);
        services.Host.SettingsChanged += _ => Ui.Post(Load);
        Loc.Instance.LanguageChanged += Load;
        Load();
    }

    public ObservableCollection<Choice> Languages { get; } = [];

    public ObservableCollection<Choice> InterfaceLanguages { get; } = [];

    public ObservableCollection<Choice> Themes { get; } = [];

    public ObservableCollection<Choice> Modes { get; } = [];

    public ObservableCollection<Choice> Units { get; } = [];

    public ObservableCollection<Choice> Microphones { get; } = [];

    public ObservableCollection<Choice> Outputs { get; } = [];

    [ObservableProperty]
    public partial string? Language { get; set; }

    /// <summary>Interface language code; empty follows the language of the commands.</summary>
    [ObservableProperty]
    public partial string? InterfaceLanguage { get; set; }

    [ObservableProperty]
    public partial string? Theme { get; set; }

    [ObservableProperty]
    public partial string? Mode { get; set; }

    [ObservableProperty]
    public partial string? Unit { get; set; }

    [ObservableProperty]
    public partial string? Microphone { get; set; }

    [ObservableProperty]
    public partial string? Output { get; set; }

    [ObservableProperty]
    public partial int Volume { get; set; }

    [ObservableProperty]
    public partial string ThresholdText { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string ThresholdError { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string PttText { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string AssignMessage { get; set; } = string.Empty;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(AssignCommand))]
    public partial bool Assigning { get; set; }

    [ObservableProperty]
    public partial string PreviewMessage { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string SaveError { get; set; } = string.Empty;

    [ObservableProperty]
    public partial bool ModelMissing { get; set; }

    /// <summary>"Check for updates" (app-settings spec); saved at once, applies without a restart.</summary>
    [ObservableProperty]
    public partial bool CheckForUpdates { get; set; }

    /// <summary>False in a build that was not installed: the switch is off-limits there (spec: Developer build).</summary>
    public bool UpdatesAvailable => _services.Updates.IsInstalled;

    public bool UpdatesUnavailable => !UpdatesAvailable;

    /// <summary>Reads the devices again (list opened, window activated, page shown).</summary>
    public void Refresh()
    {
        _loading = true;
        try
        {
            var settings = _services.Host.Settings;
            Fill(Microphones, Devices(_services.Platform.GetInputDevices(), settings.Microphone));
            Fill(Outputs, Devices(_services.Platform.GetOutputDevices(), settings.OutputDevice));
            Microphone = settings.Microphone ?? DefaultDevice;
            Output = settings.OutputDevice ?? DefaultDevice;
        }
        finally
        {
            _loading = false;
        }
    }

    partial void OnLanguageChanged(string? value)
    {
        if (!_loading && value is not null)
        {
            Save(s => s with { Language = value });
        }
    }

    partial void OnInterfaceLanguageChanged(string? value)
    {
        if (!_loading && value is not null)
        {
            Save(s => s with { InterfaceLanguage = value.Length == 0 ? null : value });
        }
    }

    partial void OnCheckForUpdatesChanged(bool value)
    {
        if (!_loading)
        {
            Save(s => s with { CheckForUpdates = value });
        }
    }

    partial void OnThemeChanged(string? value)
    {
        if (!_loading && Enum.TryParse<AppTheme>(value, out var theme))
        {
            Save(s => s with { Theme = theme });
        }
    }

    partial void OnModeChanged(string? value)
    {
        if (!_loading && Enum.TryParse<PttMode>(value, out var mode))
        {
            Save(s => s with { PttMode = mode });
        }
    }

    partial void OnUnitChanged(string? value)
    {
        if (!_loading && value is not null)
        {
            Save(s => s with { DefaultUnit = value });
        }
    }

    partial void OnMicrophoneChanged(string? value)
    {
        if (!_loading && value is not null)
        {
            Save(s => s with { Microphone = value.Length == 0 ? null : value });
        }
    }

    partial void OnOutputChanged(string? value)
    {
        if (!_loading && value is not null)
        {
            Save(s => s with { OutputDevice = value.Length == 0 ? null : value });
        }
    }

    partial void OnVolumeChanged(int value)
    {
        if (!_loading)
        {
            _volumeTimer.Stop();
            _volumeTimer.Start();
        }
    }

    [RelayCommand]
    private void ApplyThreshold()
    {
        var text = ThresholdText.Trim().Replace(',', '.');
        if (!double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var value) || value is < 0 or > 1)
        {
            ThresholdError = Loc.T("ThresholdRange");
            return;
        }

        ThresholdError = string.Empty;
        Save(s => s with { Threshold = value });
    }

    private bool CanAssign() => !Assigning;

    [RelayCommand(CanExecute = nameof(CanAssign))]
    private async Task AssignAsync()
    {
        Assigning = true;
        AssignMessage = Loc.F("AssignPrompt", (int)PttAssignment.Timeout.TotalSeconds);
        try
        {
            var result = await _services.Host.AssignPttAsync();
            AssignMessage = result switch
            {
                { Error: { } error } => Loc.F("AssignFailed", error),
                { Status: PttAssignmentStatus.Assigned, Binding: { } binding } => Loc.F("AssignDone", binding.Describe()),
                { Status: PttAssignmentStatus.TimedOut } => Loc.T("AssignTimedOut"),
                _ => Loc.T("AssignCancelled"),
            };
            if (result.Save is { Saved: false, Error: { } saveError })
            {
                SaveError = Loc.F("SaveFailed", saveError);
            }
        }
        finally
        {
            Assigning = false;
            RefreshStatus();
        }
    }

    [RelayCommand]
    private async Task PreviewAsync()
    {
        PreviewMessage = string.Empty;
        var result = await CuePreview.PlayAsync(_services.Platform, _services.Host.Settings);
        PreviewMessage = result.Outcome switch
        {
            CuePreviewOutcome.Muted => Loc.T("PreviewMuted"),
            CuePreviewOutcome.Failed => Loc.F("PreviewFailed", result.Error),
            _ => result.Warning ?? string.Empty,
        };
    }

    [RelayCommand]
    private void OpenModels() => _openModels();

    private void Save(Func<AppSettings, AppSettings> change)
    {
        var result = _services.Host.Update(change);
        SaveError = result.Saved ? string.Empty : Loc.F("SaveFailed", result.Error);
    }

    private void Load()
    {
        _loading = true;
        try
        {
            var settings = _services.Host.Settings;
            Fill(Languages, _services.Host.Languages.Select(l => new Choice(l.Code, l.Name)));
            Fill(InterfaceLanguages, [
                new Choice(string.Empty, Loc.T("InterfaceAsCommands")),
                .. Loc.Languages.Select(code => new Choice(code, Loc.LoadDictionary(code)?.GetValueOrDefault("LanguageName") ?? code)),
            ]);
            Fill(Themes, Enum.GetValues<AppTheme>().Select(t => new Choice(t.ToString(), Loc.T($"theme.{t}"))));
            Fill(Modes, Enum.GetValues<PttMode>().Select(m => new Choice(m.ToString(), Loc.T($"mode.{m}"))));
            Fill(Units, FuelUnits.All.Select(u => new Choice(u, Loc.T($"unit.{u}"))));
            Language = settings.Language;
            InterfaceLanguage = settings.InterfaceLanguage ?? string.Empty;
            Theme = settings.Theme.ToString();
            Mode = settings.PttMode.ToString();
            Unit = settings.DefaultUnit;
            Volume = settings.CueVolume;
            ThresholdText = settings.Threshold.ToString("0.##", CultureInfo.InvariantCulture);
            CheckForUpdates = settings.CheckForUpdates;
        }
        finally
        {
            _loading = false;
        }

        Refresh();
        RefreshStatus();
    }

    private void RefreshStatus()
    {
        var status = _services.Host.Status;
        var binding = _services.Host.Settings.PttBinding;
        PttText = binding is null ? Loc.T("PttNotAssigned")
            : status.Reason == StatusReason.PttDeviceDisconnected ? Loc.F("PttDisconnected", binding.Describe())
            : binding.Describe();
        ModelMissing = status.Reason == StatusReason.ModelNotInstalled;
    }

    /// <summary>System default first, then present devices, then the chosen one if it is missing (spec: Device lists).</summary>
    private static IEnumerable<Choice> Devices(IReadOnlyList<AudioDevice> present, string? chosen)
    {
        var defaultName = present.FirstOrDefault(d => d.IsDefault)?.Name;
        yield return new Choice(DefaultDevice, defaultName is null ? Loc.T("DeviceDefaultNone") : Loc.F("DeviceDefault", defaultName));
        foreach (var device in present)
        {
            yield return new Choice(device.Name, device.Name);
        }

        if (chosen is not null && !present.Any(d => d.Name == chosen))
        {
            yield return new Choice(chosen, Loc.F("DeviceMissing", chosen));
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
