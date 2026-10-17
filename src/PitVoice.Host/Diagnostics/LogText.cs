using System.Globalization;
using PitVoice.Core.Settings;

namespace PitVoice.Host.Diagnostics;

/// <summary>Settings as one log line; the archive summary uses the same text.</summary>
public static class LogText
{
    public static string Describe(AppSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        return string.Create(
            CultureInfo.InvariantCulture,
            $"language {settings.Language}, interface {settings.EffectiveInterfaceLanguage}, theme {settings.Theme}, " +
            $"PTT {settings.PttBinding?.Describe() ?? "not assigned"} ({settings.PttMode}), " +
            $"microphone {settings.Microphone ?? "system default"}, output {settings.OutputDevice ?? "system default"}, " +
            $"volume {settings.CueVolume}%, unit {settings.DefaultUnit}, threshold {settings.Threshold}");
    }
}
