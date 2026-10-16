using System.Diagnostics;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PitVoice.App.Localization;
using PitVoice.Host;
using PitVoice.Host.Diagnostics;

namespace PitVoice.App.ViewModels;

/// <summary>
/// "Collect diagnostics" and "Open logs folder" (design decision 7), shared by the settings page and the block after a
/// crash on the main page. The archive is built off the UI thread and never touches the pipeline.
/// </summary>
internal sealed partial class DiagnosticsActions(AppHost host) : ObservableObject
{
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(CollectCommand))]
    public partial bool Collecting { get; set; }

    /// <summary>Archive name, the error, or "logs not yet".</summary>
    [ObservableProperty]
    public partial string Message { get; set; } = string.Empty;

    private bool CanCollect() => !Collecting;

    /// <summary>Builds the archive and shows it in Explorer; returns the error text or null.</summary>
    [RelayCommand(CanExecute = nameof(CanCollect))]
    public async Task<string?> CollectAsync()
    {
        Collecting = true;
        Message = Loc.T("DiagCollecting");
        try
        {
            var paths = DiagnosticsPaths.Default(host.SettingsPath, host.AdditionsDirectory);
            var path = await Task.Run(() => DiagnosticsCollector.Collect(DiagnosticsSnapshot.FromHost(host), paths));
            Message = Loc.F("DiagCollected", Path.GetFileName(path));
            try
            {
                Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{path}\"") { UseShellExecute = true })?.Dispose();
            }
            catch (System.ComponentModel.Win32Exception)
            {
                // The name of the archive is in the window; the folder is the same as always.
            }

            return null;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            Message = Loc.F("DiagCollectFailed", e.Message);
            return e.Message;
        }
        finally
        {
            Collecting = false;
        }
    }

    [RelayCommand]
    private void OpenLogs()
    {
        var directory = DiagnosticPaths.LogsDirectory;
        if (!Directory.Exists(directory))
        {
            Message = Loc.T("DiagNoLogs");
            return;
        }

        Process.Start(new ProcessStartInfo("explorer.exe", $"\"{directory}\"") { UseShellExecute = true })?.Dispose();
    }
}
