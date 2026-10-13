namespace PitVoice.Core.Languages;

public enum IssueSeverity
{
    Warning,
    Error,
}

/// <summary>A validation problem of a language pack, with the file and line it refers to.</summary>
/// <param name="Severity">Error makes the pack unavailable; warning does not.</param>
/// <param name="File">Pack file name or path.</param>
/// <param name="Line">1-based line in the file, when known.</param>
/// <param name="Path">Location inside the document, e.g. <c>intents/fuel.add/patterns/2</c>.</param>
/// <param name="Message">What is wrong.</param>
public sealed record PackIssue(IssueSeverity Severity, string File, int? Line, string Path, string Message)
{
    public override string ToString()
    {
        var location = Line is { } line ? $"{File}:{line}" : File;
        var kind = Severity == IssueSeverity.Error ? "error" : "warning";
        return string.IsNullOrEmpty(Path)
            ? $"{location}: {kind}: {Message}"
            : $"{location}: {kind}: {Path}: {Message}";
    }
}
