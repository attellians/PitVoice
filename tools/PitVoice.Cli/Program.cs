using System.CommandLine;
using System.Globalization;
using System.Text;
using PitVoice.Cli;
using PitVoice.Core;
using PitVoice.Core.Parsing;
using PitVoice.Models;
using PitVoice.Speech.Asr;

Console.OutputEncoding = Encoding.UTF8;
Console.InputEncoding = Encoding.UTF8;

var dirOption = new Option<string?>("--dir", "-d")
{
    Description = "Directory with language packs (default: 'languages' next to the executable).",
};

var langOption = new Option<string>("--lang", "-l")
{
    Description = "Language code of the pack to use, e.g. ru or en.",
    Required = true,
};

var unitOption = new Option<string>("--unit", "-u")
{
    Description = "Default fuel unit when the phrase does not name one.",
    DefaultValueFactory = _ => FuelUnits.Liters,
};
unitOption.AcceptOnlyFromAmong(FuelUnits.Liters, FuelUnits.Gallons);

var thresholdOption = new Option<double>("--threshold", "-t")
{
    Description = "Confidence threshold below which a phrase is rejected.",
    DefaultValueFactory = _ => ParseOptions.DefaultConfidenceThreshold,
};

var phraseArgument = new Argument<string?>("phrase")
{
    Description = "Phrase to parse. Omit to start an interactive session.",
    Arity = ArgumentArity.ZeroOrOne,
};

var parseCommand = new Command("parse", "Parse a text command without executing anything.")
{
    langOption,
    unitOption,
    thresholdOption,
    dirOption,
    phraseArgument,
};
parseCommand.SetAction(result =>
{
    var options = new ParseOptions
    {
        DefaultUnit = result.GetValue(unitOption)!,
        ConfidenceThreshold = result.GetValue(thresholdOption),
    };
    return ParseRunner.Run(
        Packs.ResolveDirectory(result.GetValue(dirOption)),
        result.GetValue(langOption)!,
        result.GetValue(phraseArgument),
        options);
});

var modelsDirOption = new Option<string?>("--models-dir")
{
    Description = $"Model directory (default: ${ModelStore.EnvironmentVariable} or {ModelStore.DefaultRoot}).",
};

var validateCommand = new Command("validate", "Validate every language pack, run its test phrases and check its words against the installed model.")
{
    dirOption,
    modelsDirOption,
};
validateCommand.SetAction((result, cancellationToken) => ValidateRunner.RunAsync(
    Packs.ResolveDirectory(result.GetValue(dirOption)),
    new ModelStore(result.GetValue(modelsDirOption)),
    cancellationToken));

var modelIdArgument = new Argument<string>("id") { Description = "Model id from the manifest." };

var modelsListCommand = new Command("list", "Show manifest models with size, license and install status.") { modelsDirOption };
modelsListCommand.SetAction(result => ModelsRunner.List(new ModelStore(result.GetValue(modelsDirOption))));

var modelsDownloadCommand = new Command("download", "Download, verify and install a model. Ctrl+C cancels.") { modelIdArgument, modelsDirOption };
modelsDownloadCommand.SetAction((result, cancellationToken) =>
    ModelsRunner.DownloadAsync(new ModelStore(result.GetValue(modelsDirOption)), result.GetValue(modelIdArgument)!, cancellationToken));

var modelsDeleteCommand = new Command("delete", "Delete an installed model.") { modelIdArgument, modelsDirOption };
modelsDeleteCommand.SetAction(result => ModelsRunner.Delete(new ModelStore(result.GetValue(modelsDirOption)), result.GetValue(modelIdArgument)!));

var modelsVerifyCommand = new Command("verify", "Check that a model is completely installed and matches the manifest.") { modelIdArgument, modelsDirOption };
modelsVerifyCommand.SetAction(result => ModelsRunner.Verify(new ModelStore(result.GetValue(modelsDirOption)), result.GetValue(modelIdArgument)!));

var modelsCommand = new Command("models", "Manage speech models.")
{
    modelsListCommand,
    modelsDownloadCommand,
    modelsDeleteCommand,
    modelsVerifyCommand,
};

var devicesCommand = new Command("devices", "List microphones.");
devicesCommand.SetAction(_ => SpeechRunner.Devices());

var grammarOption = new Option<GrammarMode>("--grammar")
{
    Description = "Recognizer grammar: words (vocabulary) or phrases (templates with numbers expanded).",
    DefaultValueFactory = _ => GrammarBuilder.DefaultMode,
};

var wavOption = new Option<string>("--wav", "-w")
{
    Description = "WAV file to recognize (any PCM or float format).",
    Required = true,
};

var recognizeCommand = new Command("recognize", "Recognize a WAV file and show the parse result without executing anything.")
{
    langOption,
    wavOption,
    modelsDirOption,
    unitOption,
    thresholdOption,
    grammarOption,
    dirOption,
};
recognizeCommand.SetAction((result, cancellationToken) =>
    SpeechRunner.RecognizeAsync(SpeechSettingsFrom(result), result.GetValue(wavOption)!, cancellationToken));

var micOption = new Option<string?>("--mic", "-m")
{
    Description = "Microphone name as shown by 'devices' (default: the system default).",
};

var saveWavOption = new Option<string?>("--save-wav")
{
    Description = "Save every recording as 16 kHz mono WAV at this path (later takes get -2, -3, ...). Off by default.",
};

var listenCommand = new Command("listen", "Record from the microphone, recognize and show the parse result without executing anything.")
{
    langOption,
    micOption,
    saveWavOption,
    modelsDirOption,
    unitOption,
    thresholdOption,
    grammarOption,
    dirOption,
};
listenCommand.SetAction((result, cancellationToken) =>
    SpeechRunner.ListenAsync(SpeechSettingsFrom(result), result.GetValue(micOption), result.GetValue(saveWavOption), cancellationToken));

var watchOption = new Option<bool>("--watch")
{
    Description = "Keep printing the state whenever it changes, until Ctrl+C.",
};

var statusCommand = new Command("status", "Show the connection to iRacing and what is selected in the black box.") { watchOption };
statusCommand.SetAction((result, cancellationToken) => GameRunner.StatusAsync(result.GetValue(watchOption), cancellationToken));

var sendPhraseArgument = new Argument<string>("phrase") { Description = "Phrase to parse and execute in the game." };

var sendCommand = new Command("send", "Parse a text command and execute it in iRacing: send, wait for confirmation, print the outcome. Exit code is 0 only when applied.")
{
    langOption,
    unitOption,
    thresholdOption,
    dirOption,
    sendPhraseArgument,
};
sendCommand.SetAction((result, cancellationToken) => GameRunner.SendAsync(
    Packs.ResolveDirectory(result.GetValue(dirOption)),
    result.GetValue(langOption)!,
    result.GetValue(sendPhraseArgument)!,
    new ParseOptions
    {
        DefaultUnit = result.GetValue(unitOption)!,
        ConfidenceThreshold = result.GetValue(thresholdOption),
    },
    cancellationToken));

var outOption = new Option<string>("--out", "-o")
{
    Description = "File to write the recording to (JSON Lines).",
    Required = true,
};

var recordCommand = new Command("record", "Record connection and black box telemetry to a file for replay tests, until Ctrl+C.") { outOption };
recordCommand.SetAction((result, cancellationToken) => GameRunner.RecordAsync(result.GetValue(outOption)!, cancellationToken));

var root = new RootCommand("PitVoice command-line tool.")
{
    parseCommand,
    statusCommand,
    sendCommand,
    recordCommand,
    validateCommand,
    modelsCommand,
    devicesCommand,
    recognizeCommand,
    listenCommand,
};

CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
return await root.Parse(args).InvokeAsync();

SpeechSettings SpeechSettingsFrom(System.CommandLine.ParseResult result) => new(
    Packs.ResolveDirectory(result.GetValue(dirOption)),
    result.GetValue(langOption)!,
    new ModelStore(result.GetValue(modelsDirOption)),
    new ParseOptions
    {
        DefaultUnit = result.GetValue(unitOption)!,
        ConfidenceThreshold = result.GetValue(thresholdOption),
    },
    result.GetValue(grammarOption));
