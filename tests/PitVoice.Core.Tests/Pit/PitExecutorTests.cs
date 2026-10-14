using System.Diagnostics;
using PitVoice.Core.Intents;
using PitVoice.Core.Parsing;
using PitVoice.Core.Pit;
using PitVoice.Core.Responses;
using Xunit;

namespace PitVoice.Core.Tests.Pit;

public class PitExecutorTests
{
    private readonly ManualTimeProvider _time = new();
    private readonly FakeTelemetrySource _game;
    private readonly RecordingPitCommandSink _sink;
    private readonly RecordingLog _log = new();

    public PitExecutorTests()
    {
        _game = new FakeTelemetrySource(_time);
        _sink = new RecordingPitCommandSink(_game);
    }

    private static ParseResult.Accepted Parse(string phrase, string language = "ru") =>
        Assert.IsType<ParseResult.Accepted>(new CommandParser(TestPacks.Get(language)).Parse(phrase));

    private Task<ExecutionResult> Execute(string phrase, string language = "ru") =>
        new PitExecutor(_game, _sink, _time, _log).ExecuteAsync(Parse(phrase, language), phrase);

    private void Select(Wheels wheels) => _game.Pit = _game.Pit.WithWheels(wheels);

    // --- Connection check before sending

    [Fact]
    public async Task Game_not_running()
    {
        _game.State = ConnectionState.NotRunning;

        var result = await Execute("залей двадцать литров");

        Assert.Equal(ExecutionOutcome.NotConnected, result.Outcome);
        Assert.Empty(_sink.Sent);
        Assert.Equal("нет связи с игрой", new ResponseBuilder(TestPacks.Ru).Build(Parse("залей двадцать литров"), result));
    }

    [Fact]
    public async Task Not_in_car()
    {
        _game.State = ConnectionState.Connected;

        var result = await Execute("меняй шины");

        Assert.Equal(ExecutionOutcome.NotInCar, result.Outcome);
        Assert.Empty(_sink.Sent);
    }

    // --- Commands that change nothing

    [Fact]
    public async Task Cancel_without_game()
    {
        _game.State = ConnectionState.NotRunning;

        var result = await Execute("отмена");

        Assert.Equal(ExecutionOutcome.Applied, result.Outcome);
        Assert.Empty(_sink.Sent);
    }

    [Fact]
    public async Task Fuel_status_is_not_supported_yet()
    {
        var result = await Execute("сколько топлива");

        Assert.Equal(ExecutionOutcome.NotSupported, result.Outcome);
        Assert.Empty(_sink.Sent);
    }

    [Fact]
    public async Task Fuel_status_without_game_is_still_not_supported()
    {
        _game.State = ConnectionState.NotRunning;

        Assert.Equal(ExecutionOutcome.NotSupported, (await Execute("сколько топлива")).Outcome);
    }

    [Fact]
    public async Task Fuel_status_mixed_with_a_pit_command()
    {
        var result = await Execute("залей двадцать и сколько топлива");

        Assert.Equal(ExecutionOutcome.Applied, result.Outcome);
        Assert.Equal([new SentCommand(PitCommand.Fuel, 20)], _sink.Sent);
        Assert.True(_game.Pit.FuelFill);
        Assert.Equal(20, _game.Pit.FuelToAdd);
    }

    // --- Accumulating and removing tires, end to end on the fake

    [Fact]
    public async Task Side_added_to_existing_selection()
    {
        Select(Wheels.LF);

        var result = await Execute("меняй левую сторону");

        Assert.Equal(ExecutionOutcome.Applied, result.Outcome);
        Assert.Equal(Wheels.Left, _game.Pit.Wheels);
    }

    [Fact]
    public async Task Several_tire_commands_in_one_phrase()
    {
        var result = await Execute("меняй левые и меняй передние");

        Assert.Equal(ExecutionOutcome.Applied, result.Outcome);
        Assert.Equal(Wheels.LF | Wheels.LR | Wheels.RF, _game.Pit.Wheels);
    }

    [Theory]
    [InlineData("не меняй переднее левое", Wheels.All, Wheels.RF | Wheels.LR | Wheels.RR)]
    [InlineData("не меняй левые", Wheels.LF | Wheels.LR | Wheels.RF, Wheels.RF)]
    [InlineData("не меняй переднее левое", Wheels.RR, Wheels.RR)]
    [InlineData("не меняй шины", Wheels.LF | Wheels.RF, Wheels.None)]
    [InlineData("меняй правые и не меняй левые", Wheels.LF, Wheels.RF | Wheels.RR)]
    public async Task Removing_tires(string phrase, Wheels before, Wheels after)
    {
        Select(before);

        var result = await Execute(phrase);

        Assert.Equal(ExecutionOutcome.Applied, result.Outcome);
        Assert.Equal(after, _game.Pit.Wheels);
    }

    // --- Fuel

    [Fact]
    public async Task More_than_the_tank_holds_is_confirmed_when_the_game_reduces_it()
    {
        _game.TankCapacity = 110;

        var result = await Execute("залей двести литров");

        Assert.Equal([new SentCommand(PitCommand.Fuel, 200)], _sink.Sent);
        Assert.Equal(110, _game.Pit.FuelToAdd);
        Assert.Equal(ExecutionOutcome.Applied, result.Outcome);
    }

    [Fact]
    public async Task Zero_fuel_turns_refueling_off()
    {
        _game.Pit = new PitServiceState(PitServiceFlags.FuelFill, 30, 20);

        var result = await Execute("залей ноль литров");

        Assert.Equal(ExecutionOutcome.Applied, result.Outcome);
        Assert.False(_game.Pit.FuelFill);
    }

    // --- Other pit commands

    [Fact]
    public async Task Clear_all()
    {
        _game.Pit = new PitServiceState(PitServiceFlags.LFTireChange | PitServiceFlags.FuelFill | PitServiceFlags.WindshieldTearoff, 20, 30);

        var result = await Execute("сбрось всё");

        Assert.Equal(ExecutionOutcome.Applied, result.Outcome);
        Assert.True(_game.Pit.NothingSelected);
    }

    [Fact]
    public async Task Fast_repair_unavailable()
    {
        _game.Ignored.Add(PitCommand.FR);

        var result = await Execute("быстрый ремонт");

        Assert.Equal(ExecutionOutcome.NotApplied, result.Outcome);
        Assert.Equal(IntentCatalog.RepairFast, Assert.Single(result.Unconfirmed).Intent);
        Assert.Equal("команда не применилась", new ResponseBuilder(TestPacks.Ru).Build(Parse("быстрый ремонт"), result));
    }

    // --- Confirmation by target state

    [Fact]
    public async Task Already_in_the_requested_state()
    {
        _game.Pit = new PitServiceState(PitServiceFlags.FuelFill, 30, 10);
        _game.Ignored.Add(PitCommand.Fuel);

        var result = await Execute("залей тридцать литров");

        Assert.Equal(ExecutionOutcome.Applied, result.Outcome);
    }

    [Fact]
    public async Task No_confirmation_in_time_is_logged_without_real_waiting()
    {
        _game.Delay = TimeSpan.FromSeconds(2);
        var clock = Stopwatch.StartNew();

        var result = await Execute("протри стекло");

        Assert.Equal(ExecutionOutcome.NotApplied, result.Outcome);
        Assert.InRange(_time.Elapsed, PitExecutor.ConfirmationTimeout, PitExecutor.ConfirmationTimeout + TimeSpan.FromMilliseconds(100));
        Assert.True(clock.Elapsed < TimeSpan.FromSeconds(1), $"took {clock.Elapsed}");

        var report = Assert.Single(_log.Failures);
        Assert.True(report.NothingConfirmed);
        var line = report.ToString();
        Assert.Contains("протри стекло", line, StringComparison.Ordinal);   // phrase
        Assert.Contains("sent [WS]", line, StringComparison.Ordinal);         // sent commands
        Assert.Contains("expected [tearoff on]", line, StringComparison.Ordinal);
        Assert.Contains("actual [wheels=none", line, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Confirmed_just_before_the_timeout()
    {
        _game.Delay = TimeSpan.FromMilliseconds(1400);

        Assert.Equal(ExecutionOutcome.Applied, (await Execute("протри стекло")).Outcome);
        Assert.Empty(_log.Failures);
    }

    [Fact]
    public async Task Connection_lost_while_waiting()
    {
        _game.Delay = TimeSpan.FromSeconds(1);
        _game.CloseAt = TimeSpan.FromMilliseconds(500);

        var result = await Execute("меняй шины");

        Assert.Equal(ExecutionOutcome.NotApplied, result.Outcome);
        Assert.Contains("connection lost", Assert.Single(_log.Failures).ToString(), StringComparison.Ordinal);
    }

    // --- Execution outcome

    [Fact]
    public async Task Partial_application()
    {
        _game.Ignored.Add(PitCommand.FR);

        var result = await Execute("меняй шины и быстрый ремонт");

        Assert.Equal(ExecutionOutcome.PartiallyApplied, result.Outcome);
        Assert.Equal(IntentCatalog.RepairFast, Assert.Single(result.Unconfirmed).Intent);
        Assert.Equal(Wheels.All, _game.Pit.Wheels);
        Assert.Single(_sink.Sent, c => c.Command == PitCommand.FR);
        var report = Assert.Single(_log.Failures);
        Assert.Equal(ExecutionOutcome.PartiallyApplied, report.Outcome);
        Assert.False(report.NothingConfirmed);
    }

    [Fact]
    public async Task Everything_confirmed()
    {
        var result = await Execute("меняй шины и залей двадцать пять литров");

        Assert.Equal(ExecutionOutcome.Applied, result.Outcome);
        Assert.Empty(result.Unconfirmed);
        Assert.Equal(5, result.Sent.Count);
        Assert.Empty(_log.Failures);
    }

    private sealed class RecordingLog : IExecutionLog
    {
        public List<ApplyFailureReport> Failures { get; } = [];

        public List<string> Warnings { get; } = [];

        public void ApplyFailed(ApplyFailureReport report) => Failures.Add(report);

        public void Warning(string message) => Warnings.Add(message);
    }
}
