using PitVoice.Core.Intents;
using Xunit;

namespace PitVoice.Core.Tests.Intents;

public class IntentCatalogTests
{
    private static readonly string[] SpecIntentIds =
    [
        "fuel.add", "fuel.none",
        "tires.all", "tires.left", "tires.right", "tires.front", "tires.rear", "tires.none",
        "windshield.tearoff", "windshield.none",
        "repair.fast", "repair.none",
        "pit.clear", "status.fuel", "assistant.cancel",
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
    [InlineData("assistant.cancel")]
    public void Clear_and_cancel_are_standalone(string id)
    {
        var definition = IntentCatalog.Get(new IntentId(id));
        Assert.True(definition.IsStandalone);
        Assert.Null(definition.ExclusionGroup);
    }

    [Fact]
    public void Status_fuel_is_neither_grouped_nor_standalone()
    {
        var definition = IntentCatalog.Get(IntentCatalog.StatusFuel);
        Assert.Null(definition.ExclusionGroup);
        Assert.False(definition.IsStandalone);
    }

    [Fact]
    public void Unknown_intent_is_rejected()
    {
        Assert.False(IntentCatalog.Contains(new IntentId("tires.flip")));
        Assert.Throws<KeyNotFoundException>(() => IntentCatalog.Get(new IntentId("tires.flip")));
    }
}
