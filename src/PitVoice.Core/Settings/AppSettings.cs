using PitVoice.Core.Parsing;
using PitVoice.Core.Ptt;

namespace PitVoice.Core.Settings;

/// <summary>User settings of this stage (FR-APP-10, design decision 10). Devices are identified by name.</summary>
public sealed record AppSettings
{
    public const int CurrentVersion = 1;
    public const int MaxCueVolume = 100;

    /// <summary>Language code of the pack and the recognition model.</summary>
    public required string Language { get; init; }

    /// <summary>The PTT control, or null when not assigned yet.</summary>
    public PttBinding? PttBinding { get; init; }

    public PttMode PttMode { get; init; } = PttMode.Hold;

    /// <summary>Microphone name; null means the system default.</summary>
    public string? Microphone { get; init; }

    /// <summary>Output device name for the cues; null means the system default.</summary>
    public string? OutputDevice { get; init; }

    /// <summary>Cue volume in percent, 0 (silent) to 100.</summary>
    public int CueVolume { get; init; } = MaxCueVolume;

    /// <summary>Fuel unit when the phrase names none (<see cref="FuelUnits"/>).</summary>
    public string DefaultUnit { get; init; } = FuelUnits.Liters;

    /// <summary>Confidence threshold of the parser, 0..1.</summary>
    public double Threshold { get; init; } = ParseOptions.DefaultConfidenceThreshold;

    /// <summary>True once the user checked a phrase in the window; the setup checklist then hides its last item.</summary>
    public bool SetupCheckDone { get; init; }

    /// <summary>Language of the window and the tray menu; null follows <see cref="Language"/>.</summary>
    public string? InterfaceLanguage { get; init; }

    /// <summary>Colors of the window.</summary>
    public AppTheme Theme { get; init; } = AppTheme.System;

    /// <summary>Automatic update check of the installed app (app-settings spec); a file without the field means on.</summary>
    public bool CheckForUpdates { get; init; } = true;

    /// <summary>The interface language in effect.</summary>
    public string EffectiveInterfaceLanguage => InterfaceLanguage ?? Language;

    public static AppSettings Defaults(string language) => new() { Language = language };

    public ParseOptions ToParseOptions() => new() { DefaultUnit = DefaultUnit, ConfidenceThreshold = Threshold };
}

/// <summary>Window theme: follow Windows, or always light or dark.</summary>
public enum AppTheme
{
    System,
    Light,
    Dark,
}

/// <summary>Result of <see cref="ISettingsStore.Load"/>: usable settings plus what was wrong with the file.</summary>
/// <param name="Settings">Settings to use; defaults for anything missing or invalid.</param>
/// <param name="Warnings">One line per replaced value or recovered file.</param>
/// <param name="BackupPath">Where a corrupted file was moved, or null.</param>
public sealed record SettingsLoadResult(AppSettings Settings, IReadOnlyList<string> Warnings, string? BackupPath);

/// <summary>Reads and writes the user settings file.</summary>
public interface ISettingsStore
{
    /// <summary>Full path of the settings file.</summary>
    string Path { get; }

    /// <summary>Never throws for a missing or broken file: defaults are used and the problem is in the warnings.</summary>
    SettingsLoadResult Load();

    /// <summary>Writes atomically: the old file stays intact if writing fails.</summary>
    void Save(AppSettings settings);
}
