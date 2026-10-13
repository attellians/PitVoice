using System.Collections.Frozen;

namespace PitVoice.Core.Intents;

/// <summary>
/// The fixed catalog of intents shared by all languages (TZ §5). Language packs cannot add intents: the executor must
/// know what to do with every one of them.
/// </summary>
public static class IntentCatalog
{
    public static readonly IntentId FuelAdd = new("fuel.add");
    public static readonly IntentId FuelNone = new("fuel.none");
    public static readonly IntentId TiresAll = new("tires.all");
    public static readonly IntentId TiresLeft = new("tires.left");
    public static readonly IntentId TiresRight = new("tires.right");
    public static readonly IntentId TiresFront = new("tires.front");
    public static readonly IntentId TiresRear = new("tires.rear");
    public static readonly IntentId TiresNone = new("tires.none");
    public static readonly IntentId WindshieldTearoff = new("windshield.tearoff");
    public static readonly IntentId WindshieldNone = new("windshield.none");
    public static readonly IntentId RepairFast = new("repair.fast");
    public static readonly IntentId RepairNone = new("repair.none");
    public static readonly IntentId PitClear = new("pit.clear");
    public static readonly IntentId StatusFuel = new("status.fuel");
    public static readonly IntentId AssistantCancel = new("assistant.cancel");

    public const string FuelGroup = "fuel";
    public const string TiresGroup = "tires";
    public const string WindshieldGroup = "windshield";
    public const string RepairGroup = "repair";

    private static readonly SlotKind[] NoSlots = [];
    private static readonly SlotKind[] AmountAndUnit = [SlotKind.Amount, SlotKind.Unit];

    /// <summary>All intents in catalog order (the order of TZ §5).</summary>
    public static IReadOnlyList<IntentDefinition> All { get; } =
    [
        new(FuelAdd, AmountAndUnit, FuelGroup, IsNegation: false, IsStandalone: false),
        new(FuelNone, NoSlots, FuelGroup, IsNegation: true, IsStandalone: false),
        new(TiresAll, NoSlots, TiresGroup, IsNegation: false, IsStandalone: false),
        new(TiresLeft, NoSlots, TiresGroup, IsNegation: false, IsStandalone: false),
        new(TiresRight, NoSlots, TiresGroup, IsNegation: false, IsStandalone: false),
        new(TiresFront, NoSlots, TiresGroup, IsNegation: false, IsStandalone: false),
        new(TiresRear, NoSlots, TiresGroup, IsNegation: false, IsStandalone: false),
        new(TiresNone, NoSlots, TiresGroup, IsNegation: true, IsStandalone: false),
        new(WindshieldTearoff, NoSlots, WindshieldGroup, IsNegation: false, IsStandalone: false),
        new(WindshieldNone, NoSlots, WindshieldGroup, IsNegation: true, IsStandalone: false),
        new(RepairFast, NoSlots, RepairGroup, IsNegation: false, IsStandalone: false),
        new(RepairNone, NoSlots, RepairGroup, IsNegation: true, IsStandalone: false),
        new(PitClear, NoSlots, null, IsNegation: false, IsStandalone: true),
        new(StatusFuel, NoSlots, null, IsNegation: false, IsStandalone: false),
        new(AssistantCancel, NoSlots, null, IsNegation: false, IsStandalone: true),
    ];

    private static readonly FrozenDictionary<IntentId, IntentDefinition> ById =
        All.ToFrozenDictionary(d => d.Id);

    public static bool Contains(IntentId id) => ById.ContainsKey(id);

    public static bool TryGet(IntentId id, out IntentDefinition definition) => ById.TryGetValue(id, out definition!);

    public static IntentDefinition Get(IntentId id) =>
        ById.TryGetValue(id, out var definition)
            ? definition
            : throw new KeyNotFoundException($"Intent '{id}' is not in the catalog.");
}
