using System.Globalization;
using PitVoice.Core.Parsing;
using PitVoice.Core.Responses;

namespace PitVoice.Cli;

internal static class ParseRunner
{
    public static int Run(string directory, string language, string? phrase, ParseOptions options)
    {
        var pack = Packs.LoadLanguage(directory, language, Console.Error);
        if (pack is null)
        {
            return 2;
        }

        var parser = new CommandParser(pack);
        var responses = new ResponseBuilder(pack);

        if (phrase is not null)
        {
            Print(parser, responses, phrase, options);
            return 0;
        }

        Console.WriteLine($"PitVoice parse: {pack.Name} ({pack.Language}), default unit {options.DefaultUnit}, threshold {options.ConfidenceThreshold.ToString("0.00", CultureInfo.InvariantCulture)}.");
        Console.WriteLine("Type a command and press Enter. Empty line or Ctrl+C exits.");
        while (true)
        {
            Console.Write("> ");
            var line = Console.ReadLine();
            if (string.IsNullOrWhiteSpace(line))
            {
                return 0;
            }

            Print(parser, responses, line, options);
        }
    }

    private static void Print(CommandParser parser, ResponseBuilder responses, string phrase, ParseOptions options) =>
        ParseOutput.Print(parser, responses, RecognizedPhrase.FromText(phrase), options);
}
