using System.Globalization;
using PitVoice.Core.Intents;
using PitVoice.Core.Languages;
using PitVoice.Core.Parsing;
using PitVoice.Core.Pit;
using PitVoice.Core.Plural;

namespace PitVoice.Core.Responses;

/// <summary>
/// Builds the spoken confirmation text: the intent's response with <c>{amount}</c> and <c>{unit}</c> substituted,
/// the unit agreed with the number by the language's plural rules, parts of a compound command joined by commas.
/// A rejected phrase yields <c>responses.not_understood</c>.
/// </summary>
public sealed class ResponseBuilder
{
    public const string NotUnderstoodKey = "not_understood";
    public const string NotConnectedKey = "not_connected";
    public const string NotInCarKey = "not_in_car";
    public const string ApplyFailedKey = "apply_failed";
    public const string NotSupportedKey = "not_supported";

    /// <summary>Responses every pack must have: for a rejected phrase and for every execution outcome.</summary>
    public static IReadOnlyList<string> RequiredKeys { get; } =
        [NotUnderstoodKey, NotConnectedKey, NotInCarKey, ApplyFailedKey, NotSupportedKey];

    private readonly LanguagePack _pack;

    public ResponseBuilder(LanguagePack pack)
    {
        ArgumentNullException.ThrowIfNull(pack);
        _pack = pack;
    }

    public string Build(ParseResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        return result switch
        {
            ParseResult.Accepted accepted => string.Join(", ", accepted.Commands.Select(Build)),
            _ => _pack.Responses[NotUnderstoodKey],
        };
    }

    /// <summary>
    /// Response after execution: the intents' responses when applied (commands that were skipped because they are not
    /// supported yet are left out), otherwise the response of the outcome.
    /// </summary>
    public string Build(ParseResult result, ExecutionResult execution)
    {
        ArgumentNullException.ThrowIfNull(result);
        ArgumentNullException.ThrowIfNull(execution);
        if (result is not ParseResult.Accepted accepted)
        {
            return _pack.Responses[NotUnderstoodKey];
        }

        return execution.Outcome switch
        {
            ExecutionOutcome.Applied => string.Join(", ", accepted.Commands
                .Where(c => c.Intent != IntentCatalog.StatusFuel)
                .Select(Build)),
            ExecutionOutcome.PartiallyApplied or ExecutionOutcome.NotApplied => _pack.Responses[ApplyFailedKey],
            ExecutionOutcome.NotConnected => _pack.Responses[NotConnectedKey],
            ExecutionOutcome.NotInCar => _pack.Responses[NotInCarKey],
            ExecutionOutcome.NotSupported => _pack.Responses[NotSupportedKey],
            _ => throw new ArgumentOutOfRangeException(nameof(execution), execution.Outcome, "unknown outcome"),
        };
    }

    public string Build(ParsedCommand command)
    {
        ArgumentNullException.ThrowIfNull(command);
        var template = _pack.Intents[command.Intent].Response;
        return template
            .Replace("{amount}", command.Amount?.ToString(CultureInfo.InvariantCulture) ?? string.Empty, StringComparison.Ordinal)
            .Replace("{unit}", UnitForm(command.Unit, command.Amount), StringComparison.Ordinal);
    }

    private string UnitForm(string? unit, int? amount)
    {
        if (unit is null)
        {
            return string.Empty;
        }

        if (!_pack.Units.TryGetValue(unit, out var definition))
        {
            return unit;
        }

        var category = _pack.PluralRules.Select(amount ?? 0);
        if (definition.Forms.TryGetValue(category, out var form)
            || definition.Forms.TryGetValue(PluralCategory.Other, out form)
            || definition.Forms.TryGetValue(PluralCategory.Many, out form))
        {
            return form;
        }

        return definition.Forms.Values.FirstOrDefault() ?? unit;
    }
}
