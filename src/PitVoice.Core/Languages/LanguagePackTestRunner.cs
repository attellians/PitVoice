using PitVoice.Core.Parsing;

namespace PitVoice.Core.Languages;

/// <summary>A pack self-test that did not produce the expected result.</summary>
public sealed record PackTestFailure(string Language, string File, int? Line, string Say, string Expected, string Actual)
{
    public override string ToString()
    {
        var location = Line is { } line ? $"{File}:{line}" : File;
        return $"{location}: [{Language}] \"{Say}\": expected {Expected}, got {Actual}";
    }
}

/// <summary>Runs the <c>tests:</c> section of a pack through the parser (FR-LANG / pack self-tests).</summary>
public static class LanguagePackTestRunner
{
    public static IReadOnlyList<PackTestFailure> Run(LanguagePack pack, ParseOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(pack);
        var parser = new CommandParser(pack);
        var failures = new List<PackTestFailure>();
        foreach (var test in pack.Tests)
        {
            var failure = RunOne(parser, test, options);
            if (failure is not null)
            {
                failures.Add(failure);
            }
        }

        return failures;
    }

    public static PackTestFailure? RunOne(CommandParser parser, PackTest test, ParseOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(parser);
        ArgumentNullException.ThrowIfNull(test);

        var pack = parser.Pack;
        var result = parser.Parse(test.Say, options);
        var expected = test.Expect is null ? "reject" : string.Join("; ", test.Expect);

        if (test.Expect is null)
        {
            return result.IsAccepted
                ? new PackTestFailure(pack.Language, pack.SourceFile, test.Line, test.Say, expected, result.ToString()!)
                : null;
        }

        if (result is not ParseResult.Accepted accepted)
        {
            return new PackTestFailure(pack.Language, pack.SourceFile, test.Line, test.Say, expected, result.ToString()!);
        }

        if (!Matches(test.Expect, accepted.Commands))
        {
            return new PackTestFailure(pack.Language, pack.SourceFile, test.Line, test.Say, expected, result.ToString()!);
        }

        return null;
    }

    private static bool Matches(IReadOnlyList<ExpectedCommand> expected, IReadOnlyList<ParsedCommand> actual)
    {
        if (expected.Count != actual.Count)
        {
            return false;
        }

        for (var i = 0; i < expected.Count; i++)
        {
            var e = expected[i];
            var a = actual[i];
            if (e.Intent != a.Intent)
            {
                return false;
            }

            if (e.Amount is { } amount && amount != a.Amount)
            {
                return false;
            }

            if (e.Unit is { } unit && unit != a.Unit)
            {
                return false;
            }
        }

        return true;
    }
}
