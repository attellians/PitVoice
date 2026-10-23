namespace PitVoice.Core.Languages;

public enum IssueSeverity
{
    Warning,
    Error,
}

/// <summary>What is wrong with an entry of the user's additions, for a message on the page next to its row.</summary>
public enum AdditionProblem
{
    /// <summary>Any other problem of a fuel verb: a unit, a filler, a number alone.</summary>
    Invalid,

    /// <summary>A ready phrase shorter than two words after removing fillers.</summary>
    TooShort,

    /// <summary>Digits or braces.</summary>
    Symbols,

    /// <summary>A conjunction of the pack; the argument is the conjunction.</summary>
    Conjunction,

    /// <summary>A reject word of the pack; the argument is the word.</summary>
    RejectWord,

    /// <summary>A fuel verb that is a negation or has the opposite meaning; the argument is the word.</summary>
    Negation,

    /// <summary>The built-in words read the phrase as other commands; the argument is their intents, comma-separated.</summary>
    MeansOther,

    /// <summary>The built-in words already give the same command (warning, the entry is skipped).</summary>
    AlreadyWorks,

    /// <summary>The same entry with the same command earlier (warning, the entry is skipped).</summary>
    Repeated,

    /// <summary>The same phrase with another command earlier.</summary>
    AssignedElsewhere,

    /// <summary>The command is not on the page or not in the language pack; the argument is the intent.</summary>
    UnknownCommand,

    /// <summary>The fuel verb does not give "‹verb› twenty liters" as a fuel amount.</summary>
    NotFuelVerb,

    /// <summary>Words the speech model does not know; the argument is the words, comma-separated.</summary>
    NotInModel,
}

/// <summary>A validation problem of a language pack, with the file and line it refers to.</summary>
/// <param name="Severity">Error makes the pack unavailable; warning does not.</param>
/// <param name="File">Pack file name or path.</param>
/// <param name="Line">1-based line in the file, when known.</param>
/// <param name="Path">Location inside the document, e.g. <c>intents/fuel.add/patterns/2</c>.</param>
/// <param name="Message">What is wrong.</param>
public sealed record PackIssue(IssueSeverity Severity, string File, int? Line, string Path, string Message)
{
    /// <summary>Entry of the user's additions the problem belongs to (a row of the page), when it belongs to one.</summary>
    public int? Entry { get; init; }

    /// <summary>Kind of problem of an additions entry, when <see cref="Entry"/> is set.</summary>
    public AdditionProblem? Problem { get; init; }

    /// <summary>Word, conjunction or intents named by <see cref="Problem"/>.</summary>
    public string? Argument { get; init; }

    public override string ToString()
    {
        var location = Line is { } line ? $"{File}:{line}" : File;
        var kind = Severity == IssueSeverity.Error ? "error" : "warning";
        return string.IsNullOrEmpty(Path)
            ? $"{location}: {kind}: {Message}"
            : $"{location}: {kind}: {Path}: {Message}";
    }
}
