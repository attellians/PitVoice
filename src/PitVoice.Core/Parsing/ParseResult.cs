using System.Globalization;
using System.Text;
using PitVoice.Core.Intents;

namespace PitVoice.Core.Parsing;

/// <summary>One command extracted from a phrase.</summary>
/// <param name="Intent">Intent id from the catalog.</param>
/// <param name="Amount">Value of <c>{amount}</c>, if the intent has one.</param>
/// <param name="Unit">Canonical unit; for intents with a unit slot it is always set (default unit applies).</param>
/// <param name="Confidence">Match quality × minimum recognition confidence of the part.</param>
public sealed record ParsedCommand(IntentId Intent, int? Amount, string? Unit, double Confidence)
{
    public override string ToString()
    {
        var sb = new StringBuilder(Intent.Value);
        if (Amount is { } amount)
        {
            sb.Append(" amount=").Append(amount.ToString(CultureInfo.InvariantCulture));
        }

        if (Unit is { } unit)
        {
            sb.Append(" unit=").Append(unit);
        }

        sb.Append(" (").Append(Confidence.ToString("0.00", CultureInfo.InvariantCulture)).Append(')');
        return sb.ToString();
    }
}

/// <summary>Why a phrase was rejected.</summary>
public enum RejectReason
{
    /// <summary>No words at all.</summary>
    Empty,

    /// <summary>No template matched the phrase or one of its parts.</summary>
    NotUnderstood,

    /// <summary>Mutually exclusive commands or a standalone command combined with another.</summary>
    Conflict,

    /// <summary>Some part fell below the confidence threshold.</summary>
    LowConfidence,
}

/// <summary>Outcome of parsing: either every part of the phrase was understood, or nothing is executed.</summary>
public abstract record ParseResult
{
    private ParseResult()
    {
    }

    public bool IsAccepted => this is Accepted;

    /// <summary>All parts matched, no conflicts, confidence above threshold.</summary>
    public sealed record Accepted(IReadOnlyList<ParsedCommand> Commands) : ParseResult
    {
        public override string ToString() => "accepted: " + string.Join("; ", Commands);

        public bool Equals(Accepted? other) => other is not null && Commands.SequenceEqual(other.Commands);

        public override int GetHashCode()
        {
            var hash = new HashCode();
            foreach (var command in Commands)
            {
                hash.Add(command);
            }

            return hash.ToHashCode();
        }
    }

    /// <summary>The phrase is rejected as a whole.</summary>
    public sealed record Rejected(RejectReason Reason, string Details) : ParseResult
    {
        public override string ToString() => $"rejected: {Reason} ({Details})";
    }
}
