using System.Globalization;
using System.Text;
using PitVoice.App.Localization;
using PitVoice.Core;
using PitVoice.Core.Intents;
using PitVoice.Core.Parsing;
using PitVoice.Core.Pit;
using PitVoice.Host;

namespace PitVoice.App;

/// <summary>Host states, reasons, outcomes and commands as text of the interface language.</summary>
internal static class Texts
{
    public static string State(HostState state) => Loc.T($"state.{state}");

    /// <summary>The reason with its detail, or empty when the host is ready.</summary>
    public static string Reason(HostStatus status) =>
        status.Reason == StatusReason.None ? string.Empty : Loc.F($"reason.{status.Reason}", status.Detail);

    /// <summary>"PitVoice: ready" or "PitVoice: error — PTT button is not assigned" (spec: Tray status).</summary>
    public static string Tooltip(HostStatus status)
    {
        var reason = Reason(status);
        var text = Loc.F("TrayTooltip", State(status.State));
        return reason.Length == 0 ? text : $"{text} — {reason}";
    }

    public static string Game(ConnectionState state) => Loc.T($"game.{state}");

    public static string Outcome(PhraseOutcome outcome) => Loc.T($"outcome.{outcome}");

    public static string Stage(Core.Events.PipelineStage stage) => Loc.T($"stage.{stage}");

    public static string Reject(RejectReason reason) => Loc.T($"reject.{reason}");

    public static string Intent(IntentId intent) => Loc.T($"intent.{intent.Value}");

    public static string Group(CommandGroup group) => Loc.T($"group.{group}");

    /// <summary>"Действия", "Переднее левое", "Отрицания": the name of a class of words in the reference.</summary>
    public static string WordClass(Core.Languages.WordClass? wordClass) => wordClass switch
    {
        null => Loc.T("class.negations"),
        { Target: { } target } => Loc.T($"target.{Core.Languages.LexiconSchema.Name(target)}"),
        { Compound: { } compound } => Loc.T($"compound.{Core.Languages.LexiconSchema.Name(compound)}"),
        { } other => Loc.T($"class.{Core.Languages.LexiconSchema.Name(other.Role)}"),
    };

    public static string Unit(string unit) => Loc.T($"unit.{unit}");

    /// <summary>"Fuel 25 l" for a parsed command.</summary>
    public static string Command(ParsedCommand command)
    {
        var sb = new StringBuilder(Intent(command.Intent));
        if (command.Amount is { } amount)
        {
            sb.Append(' ').Append(amount.ToString(CultureInfo.InvariantCulture));
            if (command.Unit is { } unit)
            {
                sb.Append(' ').Append(Unit(unit));
            }
        }

        return sb.ToString();
    }

    /// <summary>A pattern with slots in words: "залей {amount} {unit?}" → "залей ‹число› ‹единица›".</summary>
    public static string Pattern(string pattern) => pattern
        .Replace("{amount}", "‹" + Loc.T("slot.amount") + "›", StringComparison.Ordinal)
        .Replace("{unit?}", "‹" + Loc.T("slot.unit") + "›", StringComparison.Ordinal)
        .Replace("{unit}", "‹" + Loc.T("slot.unit") + "›", StringComparison.Ordinal);

    public static string Confidence(double? confidence) =>
        confidence is { } value ? value.ToString("0.00", CultureInfo.InvariantCulture) : "—";

    public static string Megabytes(long bytes) => (bytes / 1024.0 / 1024.0).ToString("0.0", CultureInfo.InvariantCulture);

    /// <summary>Keys the code builds from enum values and catalog ids; the dictionary test checks every one exists.</summary>
    internal static IEnumerable<string> DynamicKeys()
    {
        foreach (var state in Enum.GetValues<HostState>())
        {
            yield return $"state.{state}";
        }

        foreach (var reason in Enum.GetValues<StatusReason>().Where(r => r != StatusReason.None))
        {
            yield return $"reason.{reason}";
        }

        foreach (var game in Enum.GetValues<ConnectionState>())
        {
            yield return $"game.{game}";
        }

        foreach (var outcome in Enum.GetValues<PhraseOutcome>())
        {
            yield return $"outcome.{outcome}";
        }

        foreach (var stage in Enum.GetValues<Core.Events.PipelineStage>())
        {
            yield return $"stage.{stage}";
        }

        foreach (var reject in Enum.GetValues<RejectReason>())
        {
            yield return $"reject.{reject}";
        }

        foreach (var intent in IntentCatalog.All)
        {
            yield return $"intent.{intent.Id.Value}";
        }

        foreach (var group in Enum.GetValues<CommandGroup>())
        {
            yield return $"group.{group}";
        }

        foreach (var visual in Enum.GetValues<IndicatorVisual>().Where(v => v != IndicatorVisual.Idle))
        {
            yield return $"overlay.{visual}";
        }

        foreach (var kind in Enum.GetValues<Core.Settings.IndicatorKind>())
        {
            yield return $"overlayKind.{kind}";
        }

        yield return "class.negations";
        foreach (var role in Enum.GetValues<Core.Languages.WordRole>().Where(r => r is not (Core.Languages.WordRole.Target or Core.Languages.WordRole.Compound)))
        {
            yield return $"class.{Core.Languages.LexiconSchema.Name(role)}";
        }

        foreach (var target in Enum.GetValues<Core.Languages.TireTarget>())
        {
            yield return $"target.{Core.Languages.LexiconSchema.Name(target)}";
        }

        foreach (var compound in Enum.GetValues<Core.Pit.TireCompoundKind>())
        {
            yield return $"compound.{Core.Languages.LexiconSchema.Name(compound)}";
        }

        foreach (var unit in FuelUnits.All)
        {
            yield return $"unit.{unit}";
        }

        foreach (var state in Enum.GetValues<ModelState>())
        {
            yield return $"model.{state}";
        }

        foreach (var theme in Enum.GetValues<Core.Settings.AppTheme>())
        {
            yield return $"theme.{theme}";
        }

        foreach (var mode in Enum.GetValues<Core.Ptt.PttMode>())
        {
            yield return $"mode.{mode}";
        }
    }
}
