using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PitVoice.App.Localization;
using PitVoice.Core.Parsing;
using PitVoice.Host;

namespace PitVoice.App.ViewModels;

/// <summary>"Check" (spec: Text check, Microphone check). Nothing is sent to the game.</summary>
internal sealed partial class CheckViewModel : ObservableObject
{
    private readonly AppServices _services;

    public CheckViewModel(AppServices services)
    {
        _services = services;
        services.Check.StateChanged += _ => Ui.Post(RefreshMicrophone);
        services.Host.StatusChanged += _ => Ui.Post(RefreshMicrophone);
        Loc.Instance.LanguageChanged += RefreshMicrophone;
        RefreshMicrophone();
    }

    public ObservableCollection<string> Commands { get; } = [];

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(CheckTextCommand))]
    public partial string Phrase { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string MicrophoneButton { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string MicrophoneState { get; set; } = string.Empty;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(MicrophoneCommand))]
    public partial bool MicrophoneAvailable { get; set; }

    [ObservableProperty]
    public partial bool HasResult { get; set; }

    [ObservableProperty]
    public partial string ResultText { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string ResultWords { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string Verdict { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string Confidence { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string Response { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string Error { get; set; } = string.Empty;

    private bool CanCheckText() => !string.IsNullOrWhiteSpace(Phrase);

    [RelayCommand(CanExecute = nameof(CanCheckText))]
    private void CheckText() => Show(_services.Check.CheckText(Phrase), spoken: false);

    private bool CanUseMicrophone() => MicrophoneAvailable;

    // The first click waits for the result; the second one must get through to stop the recording.
    [RelayCommand(CanExecute = nameof(CanUseMicrophone), AllowConcurrentExecutions = true)]
    private async Task MicrophoneAsync()
    {
        switch (_services.Check.State)
        {
            case MicCheckState.Recording:
                // Closing the device waits for the capture thread; keep the window responsive.
                await Task.Run(_services.Check.StopMicrophone);
                return;
            case MicCheckState.Recognizing:
                return;
        }

        Error = string.Empty;
        var result = await Task.Run(_services.Check.StartMicrophone);
        if (result is null)
        {
            MicrophoneState = Loc.T("MicCheckDropped");
            return;
        }

        Show(result, spoken: true);
    }

    private void Show(CheckResult result, bool spoken)
    {
        Commands.Clear();
        Error = result.Error is { } error ? Loc.F("CheckFailed", error) : string.Empty;
        HasResult = result.Error is null;
        if (result.Error is not null)
        {
            return;
        }

        if (result.NoSpeech)
        {
            ResultText = Loc.T("NoSpeech");
            ResultWords = string.Empty;
            Verdict = string.Empty;
            Confidence = string.Empty;
            Response = string.Empty;
            return;
        }

        ResultText = result.Text;
        ResultWords = spoken
            ? string.Join("  ", result.Words.Select(w => $"{w.Text} {Texts.Confidence(w.Confidence)}"))
            : string.Empty;
        foreach (var command in result.Commands)
        {
            Commands.Add(Texts.Command(command));
        }

        Verdict = result.Parse is ParseResult.Rejected rejected
            ? Loc.F("CheckRejected", Texts.Reject(rejected.Reason))
            : Loc.T("CheckAccepted");
        Confidence = Loc.F("CheckConfidence", Texts.Confidence(result.Confidence));
        Response = Loc.F("CheckResponse", result.Response);
    }

    private void RefreshMicrophone()
    {
        MicrophoneAvailable = _services.Check.MicrophoneAvailable;
        var state = _services.Check.State;
        MicrophoneButton = Loc.T(state == MicCheckState.Recording ? "MicStop" : "MicStart");
        MicrophoneState = state switch
        {
            MicCheckState.Recording => Loc.F("MicRecording", ((int)CommandCheck.AutoStop.TotalSeconds).ToString(CultureInfo.InvariantCulture)),
            MicCheckState.Recognizing => Loc.T("MicRecognizing"),
            _ when !MicrophoneAvailable => Loc.T("MicNeedsModel"),
            _ => string.Empty,
        };
    }
}
