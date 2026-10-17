using System.Collections.Frozen;
using PitVoice.Core.Pit;

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
    public static readonly IntentId TiresLF = new("tires.lf");
    public static readonly IntentId TiresRF = new("tires.rf");
    public static readonly IntentId TiresLR = new("tires.lr");
    public static readonly IntentId TiresRR = new("tires.rr");
    public static readonly IntentId TiresNone = new("tires.none");
    public static readonly IntentId TiresNoneLeft = new("tires.none.left");
    public static readonly IntentId TiresNoneRight = new("tires.none.right");
    public static readonly IntentId TiresNoneFront = new("tires.none.front");
    public static readonly IntentId TiresNoneRear = new("tires.none.rear");
    public static readonly IntentId TiresNoneLF = new("tires.none.lf");
    public static readonly IntentId TiresNoneRF = new("tires.none.rf");
    public static readonly IntentId TiresNoneLR = new("tires.none.lr");
    public static readonly IntentId TiresNoneRR = new("tires.none.rr");
    public static readonly IntentId WindshieldTearoff = new("windshield.tearoff");
    public static readonly IntentId WindshieldNone = new("windshield.none");
    public static readonly IntentId RepairFast = new("repair.fast");
    public static readonly IntentId RepairNone = new("repair.none");
    public static readonly IntentId PitClear = new("pit.clear");

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
        AddTires(TiresAll, Wheels.All),
        AddTires(TiresLeft, Wheels.Left),
        AddTires(TiresRight, Wheels.Right),
        AddTires(TiresFront, Wheels.Front),
        AddTires(TiresRear, Wheels.Rear),
        AddTires(TiresLF, Wheels.LF),
        AddTires(TiresRF, Wheels.RF),
        AddTires(TiresLR, Wheels.LR),
        AddTires(TiresRR, Wheels.RR),
        RemoveTires(TiresNone, Wheels.All),
        RemoveTires(TiresNoneLeft, Wheels.Left),
        RemoveTires(TiresNoneRight, Wheels.Right),
        RemoveTires(TiresNoneFront, Wheels.Front),
        RemoveTires(TiresNoneRear, Wheels.Rear),
        RemoveTires(TiresNoneLF, Wheels.LF),
        RemoveTires(TiresNoneRF, Wheels.RF),
        RemoveTires(TiresNoneLR, Wheels.LR),
        RemoveTires(TiresNoneRR, Wheels.RR),
        new(WindshieldTearoff, NoSlots, WindshieldGroup, IsNegation: false, IsStandalone: false),
        new(WindshieldNone, NoSlots, WindshieldGroup, IsNegation: true, IsStandalone: false),
        new(RepairFast, NoSlots, RepairGroup, IsNegation: false, IsStandalone: false),
        new(RepairNone, NoSlots, RepairGroup, IsNegation: true, IsStandalone: false),
        new(PitClear, NoSlots, null, IsNegation: false, IsStandalone: true),
    ];

    private static IntentDefinition AddTires(IntentId id, Wheels wheels) =>
        new(id, NoSlots, TiresGroup, IsNegation: false, IsStandalone: false) { Wheels = wheels };

    private static IntentDefinition RemoveTires(IntentId id, Wheels wheels) =>
        new(id, NoSlots, TiresGroup, IsNegation: true, IsStandalone: false) { Wheels = wheels };

    private static readonly FrozenDictionary<IntentId, IntentDefinition> ById =
        All.ToFrozenDictionary(d => d.Id);

    public static bool Contains(IntentId id) => ById.ContainsKey(id);

    public static bool TryGet(IntentId id, out IntentDefinition definition) => ById.TryGetValue(id, out definition!);

    /// <remarks>The message has no id: a stage failure reaches the log, and parsed intents must not (logging spec).</remarks>
    public static IntentDefinition Get(IntentId id) =>
        ById.TryGetValue(id, out var definition)
            ? definition
            : throw new KeyNotFoundException("The intent is not in the catalog.");
}
