using PitVoice.Core.Languages;
using PitVoice.Host;
using PitVoice.Host.Diagnostics;
using PitVoice.Models;

namespace PitVoice.Cli;

/// <summary>
/// <c>diagnostics collect</c>: the same archive as the "Collect diagnostics" button, for when the window does not open.
/// The summary comes from the settings file, the model store, the game and the devices; the path is printed and no folder
/// is opened.
/// </summary>
internal static class DiagnosticsRunner
{
    public static int Collect(string? settingsPath, string packsDirectory, ModelStore models)
    {
        if (!OperatingSystem.IsWindows())
        {
            Console.Error.WriteLine("diagnostics collect needs Windows (devices and iRacing).");
            return 1;
        }

        var store = Settings.Open(settingsPath);
        var settings = Settings.Load(store);
        using var platform = new WindowsHostPlatform();
        var snapshot = DiagnosticsSnapshot.WithoutHost(settings, packsDirectory, models, platform.Telemetry, platform);
        var paths = DiagnosticsPaths.Default(store.Path, UserAdditionsLoader.ResolveDirectory(null));
        try
        {
            var path = DiagnosticsCollector.Collect(snapshot, paths);
            Console.WriteLine($"diagnostics archive: {path}");
            return 0;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            Console.Error.WriteLine($"diagnostics not collected: {e.Message}");
            return 1;
        }
    }
}
