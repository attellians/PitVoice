using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PitVoice.App.Localization;
using PitVoice.Core.Languages;
using PitVoice.Host;

namespace PitVoice.App.ViewModels;

internal sealed record PhraseLine(string Text, bool IsOwn);

internal sealed record CommandEntry(string Name, IReadOnlyList<PhraseLine> Phrases);

internal sealed record GroupEntry(string Name, IReadOnlyList<CommandEntry> Commands);

/// <summary>"Commands" (spec: Command reference, User additions in the reference).</summary>
internal sealed partial class CommandsViewModel : ObservableObject
{
    private readonly AppServices _services;

    public CommandsViewModel(AppServices services)
    {
        _services = services;
        services.Host.SessionChanged += () => Ui.Post(Refresh);
        Loc.Instance.LanguageChanged += Refresh;
        Refresh();
    }

    public ObservableCollection<GroupEntry> Groups { get; } = [];

    public ObservableCollection<string> AdditionIssues { get; } = [];

    [ObservableProperty]
    public partial string PackError { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string AdditionsFile { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string AdditionsState { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string MissingWords { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string Message { get; set; } = string.Empty;

    [RelayCommand]
    private void OpenFile()
    {
        Message = string.Empty;
        string path;
        try
        {
            path = UserAdditionsLoader.CreateTemplate(_services.Host.AdditionsDirectory, _services.Host.Settings.Language);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            Message = Loc.F("OpenFileFailed", e.Message);
            return;
        }

        try
        {
            Process.Start(new ProcessStartInfo(path) { UseShellExecute = true })?.Dispose();
        }
        catch (Win32Exception)
        {
            // No program is associated with .yaml: Notepad is always there.
            try
            {
                Process.Start(new ProcessStartInfo("notepad.exe", $"\"{path}\"") { UseShellExecute = true })?.Dispose();
            }
            catch (Win32Exception e)
            {
                Message = Loc.F("OpenFileFailed", e.Message);
            }
        }

        Refresh();
    }

    [RelayCommand]
    private async Task ReloadAsync()
    {
        Message = string.Empty;
        await _services.Host.ReloadAsync();
        Refresh();
    }

    public void Refresh()
    {
        Groups.Clear();
        AdditionIssues.Clear();
        AdditionsFile = _services.AdditionsFile;
        var pack = _services.Host.Pack;
        var status = _services.Host.Status;
        PackError = pack is null && status.Reason == StatusReason.PackFailed ? Loc.F("reason.PackFailed", status.Detail) : string.Empty;
        if (pack is null)
        {
            AdditionsState = string.Empty;
            MissingWords = string.Empty;
            return;
        }

        foreach (var group in CommandReference.Build(pack))
        {
            Groups.Add(new GroupEntry(
                Texts.Group(group.Group),
                group.Commands.Select(c => new CommandEntry(
                    Texts.Intent(c.Intent),
                    c.Phrases.Select(p => new PhraseLine(Texts.Pattern(p.Pattern), p.IsUserAddition)).ToArray())).ToArray()));
        }

        var additions = _services.Host.Additions;
        AdditionsState = additions switch
        {
            null or { Exists: false } => Loc.T("AdditionsNone"),
            { Applied: false } => Loc.T("AdditionsNotApplied"),
            _ => Loc.F("AdditionsApplied", pack.Templates.Count(t => t.IsUserAddition)),
        };
        foreach (var issue in additions?.Issues ?? [])
        {
            AdditionIssues.Add(issue.ToString());
        }

        var missing = _services.Host.MissingAdditionWords;
        MissingWords = missing.Count == 0 ? string.Empty : Loc.F("AdditionsMissingWords", string.Join(", ", missing));
    }
}
