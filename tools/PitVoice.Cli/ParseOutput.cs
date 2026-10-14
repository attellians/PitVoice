using PitVoice.Core.Parsing;
using PitVoice.Core.Responses;

namespace PitVoice.Cli;

/// <summary>Prints the parse block shared by <c>parse</c>, <c>recognize</c>, <c>listen</c> and <c>send</c>.</summary>
internal static class ParseOutput
{
    public static ParseResult Print(CommandParser parser, ResponseBuilder responses, RecognizedPhrase phrase, ParseOptions options, bool printResponse = true)
    {
        var tokens = parser.Pack.Normalizer.Normalize(phrase);
        Console.WriteLine($"tokens:   {string.Join(' ', tokens)}");

        var result = parser.Parse(phrase, options);
        switch (result)
        {
            case ParseResult.Accepted accepted:
                Console.WriteLine("accepted");
                foreach (var command in accepted.Commands)
                {
                    Console.WriteLine($"  {command}");
                }

                break;

            case ParseResult.Rejected rejected:
                Console.WriteLine($"rejected: {rejected.Reason}: {rejected.Details}");
                break;
        }

        if (printResponse)
        {
            Console.WriteLine($"response: {responses.Build(result)}");
        }

        return result;
    }
}
