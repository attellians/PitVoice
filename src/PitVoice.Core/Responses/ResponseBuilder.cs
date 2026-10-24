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
    public const string NoDryTiresKey = "no_dry_tires";
    public const string NoWetTiresKey = "no_wet_tires";
    public const string SeveralDryTiresKey = "several_dry_tires";

    /// <summary>Joins the short responses of the commands of a phrase.</summary>
    public const string ShortSeparator = " · ";

    /// <summary>Responses every pack must have: for a rejected phrase and for every execution outcome.</summary>
    public static IReadOnlyList<string> RequiredKeys { get; } =
        [NotUnderstoodKey, NotConnectedKey, NotInCarKey, ApplyFailedKey];

    /// <summary>Responses of the "compound not available" outcome; required when the pack has compound words (design D7).</summary>
    public static IReadOnlyList<string> CompoundKeys { get; } = [NoDryTiresKey, NoWetTiresKey, SeveralDryTiresKey];

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
            ExecutionOutcome.CompoundUnavailable => _pack.Responses[execution.CompoundProblem switch
            {
                CompoundProblem.NoWet => NoWetTiresKey,
                CompoundProblem.SeveralDry => SeveralDryTiresKey,
                _ => NoDryTiresKey,
            }],
            _ => throw new ArgumentOutOfRangeException(nameof(execution), execution.Outcome, "unknown outcome"),
        };
    }

    /// <summary>
    /// Short response for the result chip of a phrase: the short responses of its commands joined by
    /// <see cref="ShortSeparator"/> when applied ("все шины · +25 л"), otherwise empty.
    /// </summary>
    public string BuildShort(ParseResult result, ExecutionResult execution)
    {
        ArgumentNullException.ThrowIfNull(result);
        ArgumentNullException.ThrowIfNull(execution);
        return result is ParseResult.Accepted accepted && execution.Outcome == ExecutionOutcome.Applied
            ? string.Join(ShortSeparator, accepted.Commands.Select(BuildShortApplied))
            : string.Empty;
    }

    /// <summary>The short response of one command, the unit as its abbreviation.</summary>
    public string BuildShort(ParsedCommand command)
    {
        ArgumentNullException.ThrowIfNull(command);
        return Fill(_pack.ShortResponseOf(command.Intent), command, ShortUnit(command.Unit));
    }

    /// <summary>
    /// "Add zero" is executed as clearing refueling, so it is confirmed with the response of <c>fuel.none</c>
    /// rather than "adding 0 liters".
    /// </summary>
    private string BuildApplied(ParsedCommand command) =>
        IsAddZero(command) ? _pack.ResponseOf(IntentCatalog.FuelNone) : Build(command);

    private string BuildShortApplied(ParsedCommand command) =>
        IsAddZero(command) ? _pack.ShortResponseOf(IntentCatalog.FuelNone) : BuildShort(command);

    private bool IsAddZero(ParsedCommand command) =>
        command is { Amount: 0 } && command.Intent == IntentCatalog.FuelAdd && _pack.Supports(IntentCatalog.FuelNone);

    public string Build(ParsedCommand command)
    {
        ArgumentNullException.ThrowIfNull(command);
        return Fill(_pack.ResponseOf(command.Intent), command, UnitForm(command.Unit, command.Amount));
    }

    private static string Fill(string template, ParsedCommand command, string unit) => template
        .Replace("{amount}", command.Amount?.ToString(CultureInfo.InvariantCulture) ?? string.Empty, StringComparison.Ordinal)
        .Replace("{unit}", unit, StringComparison.Ordinal);

    private string ShortUnit(string? unit) =>
        unit is null ? string.Empty
        : _pack.Units.TryGetValue(unit, out var definition) ? definition.Short
        : unit;

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
