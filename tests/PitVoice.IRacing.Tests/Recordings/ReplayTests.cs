using PitVoice.Core.Intents;
using PitVoice.Core.Languages;
using PitVoice.Core.Parsing;
using PitVoice.Core.Pit;
using PitVoice.Core.Tests.Pit;
using PitVoice.IRacing.Recording;
using Xunit;

namespace PitVoice.IRacing.Tests.Recordings;

/// <summary>
/// Executes phrases against telemetry recorded in iRacing Test Drive (2026-10-14, manual check list). Each recording
/// starts shortly before the command was sent and contains the game's real reaction, so the sink only records.
/// </summary>
public class ReplayTests
{
    private static readonly Lazy<CommandParser> Ru = new(() =>
    {
        var result = LanguagePackLoader.LoadFile(Path.Combine(AppContext.BaseDirectory, "languages", "ru.yaml"));
        return new CommandParser(result.Pack ?? throw new InvalidOperationException(string.Join('\n', result.Issues)));
    });

    private static string Recording(string name) => Path.Combine(AppContext.BaseDirectory, "Recordings", name + ".jsonl");

    private static async Task<(ExecutionResult Result, List<SentCommand> Sent, ReplayTelemetrySource Replay)> Execute(
        string recording, string phrase, TimeSpan startAt = default)
    {
        var time = new ManualTimeProvider();
        var replay = ReplayTelemetrySource.FromFile(Recording(recording), time);
        replay.Restart(startAt);
        var sink = new ListSink();
        var parsed = Assert.IsType<ParseResult.Accepted>(Ru.Value.Parse(phrase));
        var result = await new PitExecutor(replay, sink, time).ExecuteAsync(parsed, phrase);
        return (result, sink.Sent, replay);
    }

    [Fact]
    public async Task Replay_remove_front_left_from_all_four()
    {
        var (result, sent, replay) = await Execute("remove-front-left", "не меняй переднее левое");

        Assert.Equal(
            [new SentCommand(PitCommand.ClearTires, 0), new SentCommand(PitCommand.RF, 0), new SentCommand(PitCommand.LR, 0), new SentCommand(PitCommand.RR, 0)],
            sent);
        Assert.Equal(ExecutionOutcome.Applied, result.Outcome);
        Assert.Equal(Wheels.RF | Wheels.LR | Wheels.RR, replay.Current.Pit!.Wheels);
    }

    [Fact]
    public async Task Replay_remove_left_side_passes_through_empty_selection()
    {
        // The game shows "no tires" for one frame between ClearTires and RF; only the final state counts.
        var (result, sent, replay) = await Execute("remove-left-side", "не меняй левые");

        Assert.Equal([new SentCommand(PitCommand.ClearTires, 0), new SentCommand(PitCommand.RF, 0)], sent);
        Assert.Equal(ExecutionOutcome.Applied, result.Outcome);
        Assert.Equal(Wheels.RF, replay.Current.Pit!.Wheels);
        Assert.Contains(replay.Records, r => r.Snapshot.Pit?.Wheels == Wheels.None);
    }

    [Fact]
    public async Task Replay_fuel_above_tank_is_reduced_by_the_game_and_confirmed()
    {
        var (result, sent, replay) = await Execute("fuel-above-tank", "залей двести литров");

        // The recording was made before fuelCapacity existed; 96 l is DriverCarFuelMaxLtr of the same car, read from
        // its session info later in the same Test Drive. The game capped 200 l to the tank, not to the free volume
        // (61 l were in the tank), about 290 ms after the command.
        Assert.Equal([new SentCommand(PitCommand.Fuel, 200)], sent);
        Assert.Equal(38, replay.At(TimeSpan.Zero).Pit!.FuelToAdd, 0.01);
        Assert.Equal(ExecutionOutcome.Applied, result.Outcome);

        // Confirmed by the exact capped amount, not by the 38 l left from before.
        Assert.Equal(96, replay.Current.Pit!.FuelToAdd, 0.01);
    }

    [Fact]
    public async Task Replay_fuel_from_38_to_200_waits_for_the_tank_capacity_reported_by_the_game()
    {
        // Recorded with fuelCapacity from session info (96 l tank). 38 l stay selected for ~400 ms after the command,
        // which the old "0 < amount <= sent" rule would already have taken as confirmation.
        var (result, sent, replay) = await Execute("fuel-38-to-200-with-capacity", "залей двести литров");

        Assert.Equal([new SentCommand(PitCommand.Fuel, 200)], sent);
        Assert.Equal(96, replay.At(TimeSpan.Zero).FuelCapacity);
        Assert.Equal(38, replay.At(TimeSpan.Zero).Pit!.FuelToAdd, 0.01);
        Assert.Equal(ExecutionOutcome.Applied, result.Outcome);
        Assert.Equal(96, replay.Current.Pit!.FuelToAdd, 0.01);
    }

    [Fact]
    public async Task Replay_fuel_command_that_never_arrives_is_not_applied()
    {
        // Only the first 300 ms of the same recording: the game has not shown 96 l yet, and 38 l must not confirm.
        var records = TelemetryRecordFormat.ReadFile(Recording("fuel-38-to-200-with-capacity"))
            .Where(r => r.Time < TimeSpan.FromMilliseconds(300))
            .ToList();
        var time = new ManualTimeProvider();
        var replay = new ReplayTelemetrySource(records, time);
        var parsed = Assert.IsType<ParseResult.Accepted>(Ru.Value.Parse("залей двести литров"));

        var result = await new PitExecutor(replay, new ListSink(), time).ExecuteAsync(parsed, "залей двести литров");

        Assert.Equal(ExecutionOutcome.NotApplied, result.Outcome);
    }

    [Fact]
    public async Task Replay_fast_repair_with_none_left_flashes_and_is_not_applied()
    {
        var (result, _, replay) = await Execute("fast-repair-none-left", "быстрый ремонт");

        Assert.Contains(replay.Records, r => r.Snapshot.Pit?.FastRepair == true);
        Assert.Equal(ExecutionOutcome.NotApplied, result.Outcome);
        Assert.Equal(IntentCatalog.RepairFast, Assert.Single(result.Unconfirmed).Intent);
        Assert.False(replay.Current.Pit!.FastRepair);
    }

    [Fact]
    public void Replay_leaving_the_car_and_closing_the_game()
    {
        var records = TelemetryRecordFormat.ReadFile(Recording("leave-car-and-close"));
        var states = new List<ConnectionState>();
        foreach (var (_, snapshot) in records)
        {
            if (states.Count == 0 || states[^1] != snapshot.State)
            {
                states.Add(snapshot.State);
            }
        }

        Assert.Equal([ConnectionState.InCar, ConnectionState.Connected, ConnectionState.NotRunning], states);
        Assert.All(records.Where(r => r.Snapshot.State == ConnectionState.NotRunning), r => Assert.Null(r.Snapshot.Pit));
    }

    [Fact]
    public async Task Replay_leaving_the_car_while_waiting_is_not_applied()
    {
        var (result, _, _) = await Execute("leave-car-and-close", "меняй шины");

        Assert.Equal(ExecutionOutcome.NotApplied, result.Outcome);
    }

    [Fact]
    public async Task Replay_in_the_garage_and_after_closing()
    {
        Assert.Equal(ExecutionOutcome.NotInCar, (await Execute("leave-car-and-close", "меняй шины", TimeSpan.FromSeconds(5))).Result.Outcome);
        Assert.Equal(ExecutionOutcome.NotConnected, (await Execute("leave-car-and-close", "меняй шины", TimeSpan.FromSeconds(17))).Result.Outcome);
    }

    private sealed class ListSink : IPitCommandSink
    {
        public List<SentCommand> Sent { get; } = [];

        public void Send(PitCommand command, int parameter) => Sent.Add(new SentCommand(command, parameter));
    }
}
