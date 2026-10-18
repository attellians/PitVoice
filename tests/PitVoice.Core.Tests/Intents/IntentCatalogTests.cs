using PitVoice.Core.Intents;
using PitVoice.Core.Pit;
using Xunit;

namespace PitVoice.Core.Tests.Intents;

public class IntentCatalogTests
{
    private static readonly string[] SpecIntentIds =
    [
        "fuel.add", "fuel.none",
        "tires.all", "tires.left", "tires.right", "tires.front", "tires.rear",
        "tires.lf", "tires.rf", "tires.lr", "tires.rr",
        "tires.dry", "tires.wet",
        "tires.none", "tires.none.left", "tires.none.right", "tires.none.front", "tires.none.rear",
        "tires.none.lf", "tires.none.rf", "tires.none.lr", "tires.none.rr",
        "windshield.tearoff", "windshield.none",
        "repair.fast", "repair.none",
        "pit.clear",
    ];

    public static TheoryData<string> AllIntentIds
    {
        get
        {
            var data = new TheoryData<string>();
            foreach (var id in SpecIntentIds)
            {
                data.Add(id);
            }

            return data;
        }
    }

    [Theory]
    [MemberData(nameof(AllIntentIds))]
    public void Every_intent_from_the_spec_is_in_the_catalog(string id)
    {
        Assert.True(IntentCatalog.Contains(new IntentId(id)));
    }

    [Fact]
    public void Catalog_has_exactly_the_spec_intents()
    {
        var expected = SpecIntentIds.Order().ToArray();
        var actual = IntentCatalog.All.Select(d => d.Id.Value).Order().ToArray();
        Assert.Equal(expected, actual);
    }

    [Fact]
    public void Only_fuel_add_has_slots()
    {
        var fuelAdd = IntentCatalog.Get(IntentCatalog.FuelAdd);
        Assert.Equal([SlotKind.Amount, SlotKind.Unit], fuelAdd.Slots);

        foreach (var definition in IntentCatalog.All.Where(d => d.Id != IntentCatalog.FuelAdd))
        {
            Assert.Empty(definition.Slots);
        }
    }

    [Theory]
    [InlineData("fuel.add", "fuel", false)]
    [InlineData("fuel.none", "fuel", true)]
    [InlineData("tires.all", "tires", false)]
    [InlineData("tires.left", "tires", false)]
    [InlineData("tires.right", "tires", false)]
    [InlineData("tires.front", "tires", false)]
    [InlineData("tires.rear", "tires", false)]
    [InlineData("tires.none", "tires", true)]
    [InlineData("windshield.tearoff", "windshield", false)]
    [InlineData("windshield.none", "windshield", true)]
    [InlineData("repair.fast", "repair", false)]
    [InlineData("repair.none", "repair", true)]
    public void Exclusion_groups_match_the_spec_table(string id, string group, bool isNegation)
    {
        var definition = IntentCatalog.Get(new IntentId(id));
        Assert.Equal(group, definition.ExclusionGroup);
        Assert.Equal(isNegation, definition.IsNegation);
        Assert.False(definition.IsStandalone);
    }

    [Theory]
    [InlineData("pit.clear")]
    public void Clear_is_standalone(string id)
    {
        var definition = IntentCatalog.Get(new IntentId(id));
        Assert.True(definition.IsStandalone);
        Assert.Null(definition.ExclusionGroup);
    }

    [Fact]
    public void Every_tire_intent_has_a_wheel_mask()
    {
        foreach (var definition in IntentCatalog.All)
        {
            var isTire = definition.ExclusionGroup == IntentCatalog.TiresGroup;
            Assert.Equal(isTire, definition.Wheels != Wheels.None);
            if (isTire)
            {
                Assert.True((definition.AddsWheels == Wheels.None) != (definition.RemovesWheels == Wheels.None), definition.Id.Value);
            }
        }
    }

    [Theory]
    [InlineData("tires.all", Wheels.All, Wheels.None)]
    [InlineData("tires.left", Wheels.LF | Wheels.LR, Wheels.None)]
    [InlineData("tires.right", Wheels.RF | Wheels.RR, Wheels.None)]
    [InlineData("tires.front", Wheels.LF | Wheels.RF, Wheels.None)]
    [InlineData("tires.rear", Wheels.LR | Wheels.RR, Wheels.None)]
    [InlineData("tires.lf", Wheels.LF, Wheels.None)]
    [InlineData("tires.rf", Wheels.RF, Wheels.None)]
    [InlineData("tires.lr", Wheels.LR, Wheels.None)]
    [InlineData("tires.rr", Wheels.RR, Wheels.None)]
    [InlineData("tires.dry", Wheels.All, Wheels.None)]
    [InlineData("tires.wet", Wheels.All, Wheels.None)]
    [InlineData("tires.none", Wheels.None, Wheels.All)]
    [InlineData("tires.none.left", Wheels.None, Wheels.LF | Wheels.LR)]
    [InlineData("tires.none.right", Wheels.None, Wheels.RF | Wheels.RR)]
    [InlineData("tires.none.front", Wheels.None, Wheels.LF | Wheels.RF)]
    [InlineData("tires.none.rear", Wheels.None, Wheels.LR | Wheels.RR)]
    [InlineData("tires.none.lf", Wheels.None, Wheels.LF)]
    [InlineData("tires.none.rf", Wheels.None, Wheels.RF)]
    [InlineData("tires.none.lr", Wheels.None, Wheels.LR)]
    [InlineData("tires.none.rr", Wheels.None, Wheels.RR)]
    public void Tire_masks(string id, Wheels adds, Wheels removes)
    {
        var definition = IntentCatalog.Get(new IntentId(id));
        Assert.Equal(adds, definition.AddsWheels);
        Assert.Equal(removes, definition.RemovesWheels);
        Assert.Equal(IntentCatalog.TiresGroup, definition.ExclusionGroup);
    }

    [Fact]
    public void Unknown_intent_is_rejected()
    {
        Assert.False(IntentCatalog.Contains(new IntentId("tires.flip")));
        Assert.Throws<KeyNotFoundException>(() => IntentCatalog.Get(new IntentId("tires.flip")));
    }

    [Theory]
    [InlineData("tires.dry", TireCompoundKind.Dry)]
    [InlineData("tires.wet", TireCompoundKind.Wet)]
    public void Compound_intents_add_all_four_wheels_and_carry_the_compound(string id, TireCompoundKind compound)
    {
        var definition = IntentCatalog.Get(new IntentId(id));
        Assert.Equal(compound, definition.Compound);
        Assert.Equal(Wheels.All, definition.AddsWheels);
        Assert.False(definition.IsNegation);
    }

    [Fact]
    public void Only_compound_intents_carry_a_compound()
    {
        var withCompound = IntentCatalog.All.Where(d => d.Compound is not null).Select(d => d.Id).ToArray();
        Assert.Equal([IntentCatalog.TiresDry, IntentCatalog.TiresWet], withCompound);
    }

    [Fact]
    public void Compound_command_code_is_12()
    {
        Assert.Equal(12, (int)PitCommand.TC);
    }
}
