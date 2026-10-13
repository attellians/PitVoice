using System.CommandLine;
using System.Globalization;
using System.Text;
using PitVoice.Cli;
using PitVoice.Core;
using PitVoice.Core.Parsing;

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

var validateCommand = new Command("validate", "Validate every language pack and run its test phrases.")
{
    dirOption,
};
validateCommand.SetAction(result => ValidateRunner.Run(Packs.ResolveDirectory(result.GetValue(dirOption))));

var root = new RootCommand("PitVoice command-line tool.")
{
    parseCommand,
    validateCommand,
};

CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
return root.Parse(args).Invoke();
