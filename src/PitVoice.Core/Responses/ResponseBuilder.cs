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

    /// <summary>Responses every pack must have: for a rejected phrase and for every execution outcome.</summary>
    public static IReadOnlyList<string> RequiredKeys { get; } =
        [NotUnderstoodKey, NotConnectedKey, NotInCarKey, ApplyFailedKey];

    /// <summary>
    /// Responses of the removed fuel status and cancel intents and of their outcome. A pack that still has them
    /// loads with a warning.
    /// </summary>
    public static IReadOnlyList<string> UnusedKeys { get; } = ["not_supported", "cancelled"];

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
    /// Response after execution: the intents' responses when applied, otherwise the response of the outcome.
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
            ExecutionOutcome.Applied => string.Join(", ", accepted.Commands.Select(BuildApplied)),
            ExecutionOutcome.PartiallyApplied or ExecutionOutcome.NotApplied => _pack.Responses[ApplyFailedKey],
            ExecutionOutcome.NotConnected => _pack.Responses[NotConnectedKey],
            ExecutionOutcome.NotInCar => _pack.Responses[NotInCarKey],
            _ => throw new ArgumentOutOfRangeException(nameof(execution), execution.Outcome, "unknown outcome"),
        };
    }

    /// <summary>
    /// "Add zero" is executed as clearing refueling, so it is confirmed with the response of <c>fuel.none</c>
    /// rather than "adding 0 liters".
    /// </summary>
    private string BuildApplied(ParsedCommand command) =>
        command is { Amount: 0 } && command.Intent == IntentCatalog.FuelAdd
            && _pack.Supports(IntentCatalog.FuelNone)
            ? _pack.ResponseOf(IntentCatalog.FuelNone)
            : Build(command);

    public string Build(ParsedCommand command)
    {
        ArgumentNullException.ThrowIfNull(command);
        var template = _pack.ResponseOf(command.Intent);
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
