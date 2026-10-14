using PitVoice.Core.Intents;
using PitVoice.Core.Parsing;
using PitVoice.Core.Pit;
using Xunit;

namespace PitVoice.Core.Tests.Pit;

public class PitPlannerTests
{
    private static IReadOnlyList<ParsedCommand> Parse(string language, string phrase, string unit = FuelUnits.Liters) =>
        Assert.IsType<ParseResult.Accepted>(
            new CommandParser(TestPacks.Get(language)).Parse(phrase, new ParseOptions { DefaultUnit = unit })).Commands;

    private static PitPlan Plan(string phrase, Wheels selected = Wheels.None, string language = "ru") =>
        PitPlanner.Plan(Parse(language, phrase), PitServiceState.Empty.WithWheels(selected));

    private static PitServiceState Selected(Wheels wheels) => PitServiceState.Empty.WithWheels(wheels);

    private static SentCommand Cmd(PitCommand command, int parameter = 0) => new(command, parameter);

    // --- Accumulating tire selection

    [Fact]
    public void Single_wheel_is_added_without_clearing()
    {
        var plan = Plan("меняй переднее левое");

        Assert.Equal([Cmd(PitCommand.LF)], plan.Commands);
        var step = Assert.Single(plan.Steps);
        Assert.True(step.IsConfirmed(Selected(Wheels.LF)));
        Assert.True(step.IsConfirmed(Selected(Wheels.LF | Wheels.RR)));
        Assert.False(step.IsConfirmed(Selected(Wheels.None)));
    }

    [Fact]
    public void Side_is_added_to_existing_selection()
    {
        var plan = Plan("меняй левую сторону", Wheels.LF);

        Assert.Equal([Cmd(PitCommand.LF), Cmd(PitCommand.LR)], plan.Commands);
        Assert.True(plan.Steps[0].IsConfirmed(Selected(Wheels.Left)));
        Assert.False(plan.Steps[0].IsConfirmed(Selected(Wheels.LF)));
    }

    [Fact]
    public void Several_tire_commands_merge_into_one_set()
    {
        var plan = Plan("меняй левые и меняй передние");

        var step = Assert.Single(plan.Steps);
        Assert.Equal(2, step.Sources.Count);
        Assert.Equal([Cmd(PitCommand.LF), Cmd(PitCommand.RF), Cmd(PitCommand.LR)], step.Commands);
        Assert.True(step.IsConfirmed(Selected(Wheels.LF | Wheels.RF | Wheels.LR)));
    }

    [Fact]
    public void Pressure_is_kept_by_sending_zero()
    {
        Assert.All(Plan("меняй шины").Commands, c => Assert.Equal(0, c.Parameter));
    }

    [Fact]
    public void All_tires_never_clear_first()
    {
        Assert.DoesNotContain(Cmd(PitCommand.ClearTires), Plan("меняй шины", Wheels.RR).Commands);
    }

    // --- Removing tires from selection

    [Fact]
    public void Remove_one_wheel_sends_clear_and_the_rest()
    {
        var plan = Plan("не меняй переднее левое", Wheels.All);

        Assert.Equal([Cmd(PitCommand.ClearTires), Cmd(PitCommand.RF), Cmd(PitCommand.LR), Cmd(PitCommand.RR)], plan.Commands);
        var step = plan.Steps[0];
        Assert.True(step.IsConfirmed(Selected(Wheels.RF | Wheels.LR | Wheels.RR)));
        Assert.False(step.IsConfirmed(Selected(Wheels.All)));
    }

    [Fact]
    public void Remove_a_side()
    {
        var plan = Plan("не меняй левые", Wheels.LF | Wheels.LR | Wheels.RF);

        Assert.Equal([Cmd(PitCommand.ClearTires), Cmd(PitCommand.RF)], plan.Commands);
        Assert.True(plan.Steps[0].IsConfirmed(Selected(Wheels.RF)));
    }

    [Fact]
    public void Remove_a_wheel_that_is_not_selected_keeps_the_rest()
    {
        var plan = Plan("не меняй переднее левое", Wheels.RR);

        Assert.Equal([Cmd(PitCommand.ClearTires), Cmd(PitCommand.RR)], plan.Commands);
        Assert.True(plan.Steps[0].IsConfirmed(Selected(Wheels.RR)));
    }

    [Fact]
    public void Remove_all()
    {
        var plan = Plan("не меняй шины", Wheels.LF | Wheels.RF);

        Assert.Equal([Cmd(PitCommand.ClearTires)], plan.Commands);
        Assert.True(plan.Steps[0].IsConfirmed(Selected(Wheels.None)));
        Assert.False(plan.Steps[0].IsConfirmed(Selected(Wheels.LF)));
    }

    [Fact]
    public void Add_and_remove_in_one_phrase()
    {
        var plan = Plan("меняй правые и не меняй левые", Wheels.LF);

        Assert.Equal([Cmd(PitCommand.ClearTires), Cmd(PitCommand.RF), Cmd(PitCommand.RR)], plan.Commands);
        Assert.True(plan.Steps[0].IsConfirmed(Selected(Wheels.Right)));
        Assert.False(plan.Steps[0].IsConfirmed(Selected(Wheels.Right | Wheels.LF)));
    }

    // --- Fuel

    [Fact]
    public void Liters_as_spoken()
    {
        Assert.Equal([Cmd(PitCommand.Fuel, 30)], Plan("залей тридцать литров").Commands);
    }

    [Fact]
    public void Gallons_converted_and_rounded()
    {
        var plan = PitPlanner.Plan(Parse("en", "add ten gallons"), PitServiceState.Empty);

        Assert.Equal([Cmd(PitCommand.Fuel, 38)], plan.Commands);
        Assert.Equal(38, PitPlanner.ToLiters(10, FuelUnits.Gallons));
        Assert.Equal(4, PitPlanner.ToLiters(1, FuelUnits.Gallons));
        Assert.Equal(10, PitPlanner.ToLiters(10, FuelUnits.Liters));
    }

    [Fact]
    public void More_than_the_tank_is_sent_as_is()
    {
        Assert.Equal([Cmd(PitCommand.Fuel, 200)], Plan("залей двести литров").Commands);
    }

    [Fact]
    public void Zero_liters_clears_refueling()
    {
        var plan = Plan("залей ноль литров");

        Assert.Equal([Cmd(PitCommand.ClearFuel)], plan.Commands);
        Assert.True(plan.Steps[0].IsConfirmed(PitServiceState.Empty with { FuelToAdd = 30 }));
        Assert.False(plan.Steps[0].IsConfirmed(new PitServiceState(PitServiceFlags.FuelFill, 30, 0)));
    }

    [Theory]
    [InlineData(true, 200.0, true)]   // as requested
    [InlineData(true, 110.0, true)]   // the game reduced it to the tank size
    [InlineData(true, 200.4, true)]   // float rounding in telemetry
    [InlineData(true, 201.0, false)]
    [InlineData(true, 0.0, false)]
    [InlineData(false, 200.0, false)] // refueling not checked
    public void Fuel_confirmation(bool fuelOn, double amount, bool confirmed)
    {
        var step = Plan("залей двести литров").Steps[0];
        var state = new PitServiceState(fuelOn ? PitServiceFlags.FuelFill : PitServiceFlags.None, amount, 10);

        Assert.Equal(confirmed, step.IsConfirmed(state));
    }

    // --- Other commands

    [Fact]
    public void Compound_phrase_keeps_spoken_order()
    {
        var plan = Plan("протри стекло и быстрый ремонт");

        Assert.Equal([Cmd(PitCommand.WS), Cmd(PitCommand.FR)], plan.Commands);
        var both = new PitServiceState(PitServiceFlags.WindshieldTearoff | PitServiceFlags.FastRepair, 0, 0);
        Assert.All(plan.Steps, s => Assert.True(s.IsConfirmed(both)));
        Assert.False(plan.Steps[1].IsConfirmed(new PitServiceState(PitServiceFlags.WindshieldTearoff, 0, 0)));
    }

    [Fact]
    public void Tire_step_takes_the_place_of_the_first_tire_command()
    {
        var plan = Plan("залей десять литров и меняй левые и протри стекло");

        Assert.Equal([Cmd(PitCommand.Fuel, 10), Cmd(PitCommand.LF), Cmd(PitCommand.LR), Cmd(PitCommand.WS)], plan.Commands);
    }

    [Fact]
    public void Clear_all_expects_nothing_selected()
    {
        var plan = Plan("сбрось всё");

        Assert.Equal([Cmd(PitCommand.Clear)], plan.Commands);
        Assert.True(plan.Steps[0].IsConfirmed(PitServiceState.Empty with { FuelToAdd = 20, FuelLevel = 40 }));
        Assert.False(plan.Steps[0].IsConfirmed(new PitServiceState(PitServiceFlags.WindshieldTearoff, 0, 0)));
    }

    [Theory]
    [InlineData("без топлива", PitCommand.ClearFuel, PitServiceFlags.FuelFill)]
    [InlineData("стекло не трогай", PitCommand.ClearWS, PitServiceFlags.WindshieldTearoff)]
    [InlineData("без ремонта", PitCommand.ClearFR, PitServiceFlags.FastRepair)]
    public void Clearing_commands_expect_the_bit_off(string phrase, PitCommand command, PitServiceFlags flag)
    {
        var plan = Plan(phrase);

        Assert.Equal([Cmd(command)], plan.Commands);
        Assert.True(plan.Steps[0].IsConfirmed(PitServiceState.Empty));
        Assert.False(plan.Steps[0].IsConfirmed(new PitServiceState(flag, 0, 0)));
    }

    [Fact]
    public void Fuel_status_sends_nothing_and_does_not_hide_other_commands()
    {
        var plan = Plan("залей двадцать и сколько топлива");

        Assert.Equal([Cmd(PitCommand.Fuel, 20)], plan.Commands);
        Assert.Equal(IntentCatalog.StatusFuel, Assert.Single(plan.Skipped).Intent);
    }

    [Fact]
    public void Cancel_sends_nothing()
    {
        var plan = Plan("отмена");

        Assert.Empty(plan.Steps);
        Assert.Equal(IntentCatalog.AssistantCancel, Assert.Single(plan.Skipped).Intent);
    }
}
