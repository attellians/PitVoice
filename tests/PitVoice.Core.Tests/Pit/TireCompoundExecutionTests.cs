using PitVoice.Core.Cues;
using PitVoice.Core.Parsing;
using PitVoice.Core.Pit;
using PitVoice.Core.Responses;
using Xunit;

namespace PitVoice.Core.Tests.Pit;

/// <summary>
/// Scenarios of the pit-execution and audio-cues deltas of add-tire-compound: picking the compound from the tire list of
/// the car, all or nothing when it is missing, sending and confirming the compound with all four tires.
/// </summary>
public class TireCompoundExecutionTests
{
    private static readonly TireCompound[] DryWet = [new(0, "Dry"), new(1, "Wet")];

    private readonly ManualTimeProvider _time = new();
    private readonly FakeTelemetrySource _game;
    private readonly RecordingPitCommandSink _sink;
    private readonly RecordingLog _log = new();

    public TireCompoundExecutionTests()
    {
        _game = new FakeTelemetrySource(_time) { TireCompounds = DryWet };
        _sink = new RecordingPitCommandSink(_game);
    }

    private static ParseResult.Accepted Parse(string phrase) =>
        Assert.IsType<ParseResult.Accepted>(new CommandParser(TestPacks.Ru).Parse(phrase));

    private Task<ExecutionResult> Execute(string phrase) =>
        new PitExecutor(_game, _sink, _time, _log).ExecuteAsync(Parse(phrase), phrase);

    private static string Response(string phrase, ExecutionResult result) => new ResponseBuilder(TestPacks.Ru).Build(Parse(phrase), result);

    // --- Kind of a compound and the index to send (design D6)

    [Theory]
    [InlineData("Wet", TireCompoundKind.Wet)]
    [InlineData("RAIN", TireCompoundKind.Wet)]
    [InlineData("Rain Tires", TireCompoundKind.Wet)]
    [InlineData("Dry", TireCompoundKind.Dry)]
    [InlineData("Hard", TireCompoundKind.Dry)]
    [InlineData("Primary", TireCompoundKind.Dry)]
    public void Kind_by_type(string type, TireCompoundKind kind) => Assert.Equal(kind, new TireCompound(0, type).Kind);

    [Fact]
    public void Dry_and_wet_list()
    {
        Assert.Equal(1, TireCompoundSelector.Select(DryWet, TireCompoundKind.Wet, out var wetProblem));
        Assert.Null(wetProblem);
        Assert.Equal(0, TireCompoundSelector.Select(DryWet, TireCompoundKind.Dry, out var dryProblem));
        Assert.Null(dryProblem);
    }

    [Fact]
    public void Hard_soft_wet_list()
    {
        TireCompound[] list = [new(0, "Hard"), new(1, "Soft"), new(2, "Wet")];

        Assert.Equal(2, TireCompoundSelector.Select(list, TireCompoundKind.Wet, out _));
        Assert.Null(TireCompoundSelector.Select(list, TireCompoundKind.Dry, out var problem));
        Assert.Equal(CompoundProblem.SeveralDry, problem);
    }

    [Fact]
    public void Dry_only_list()
    {
        TireCompound[] list = [new(0, "Dry")];

        Assert.Null(TireCompoundSelector.Select(list, TireCompoundKind.Wet, out var problem));
        Assert.Equal(CompoundProblem.NoWet, problem);
        Assert.Equal(0, TireCompoundSelector.Select(list, TireCompoundKind.Dry, out _));
    }

    [Theory]
    [InlineData(TireCompoundKind.Dry, CompoundProblem.NoDry)]
    [InlineData(TireCompoundKind.Wet, CompoundProblem.NoWet)]
    public void Unknown_list(TireCompoundKind kind, CompoundProblem expected)
    {
        Assert.Null(TireCompoundSelector.Select(null, kind, out var problem));
        Assert.Equal(expected, problem);
    }

    // --- Compound availability before sending (design D7)

    [Fact]
    public async Task Car_without_wet_tires()
    {
        _game.TireCompounds = [new(0, "Dry")];

        var result = await Execute("меняй на дождевые и залей двадцать литров");

        Assert.Equal(ExecutionOutcome.CompoundUnavailable, result.Outcome);
        Assert.Equal(CompoundProblem.NoWet, result.CompoundProblem);
        Assert.Empty(_sink.Sent);
        Assert.Equal("у машины нет дождевых шин", Response("меняй на дождевые и залей двадцать литров", result));
        var logged = Assert.Single(_log.Warnings);
        Assert.Contains("no wet tire compound", logged, StringComparison.Ordinal);
        Assert.DoesNotContain("дождев", logged, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Several_dry_compounds()
    {
        _game.TireCompounds = [new(0, "Hard"), new(1, "Soft"), new(2, "Wet")];

        var result = await Execute("меняй на сухие");

        Assert.Equal(ExecutionOutcome.CompoundUnavailable, result.Outcome);
        Assert.Empty(_sink.Sent);
        Assert.Equal("у машины несколько сухих составов", Response("меняй на сухие", result));

        var wet = await Execute("меняй на дождевые");
        Assert.Equal(ExecutionOutcome.Applied, wet.Outcome);
        Assert.Equal(new SentCommand(PitCommand.TC, 2), wet.Sent[0]);
    }

    [Fact]
    public async Task No_tire_list()
    {
        _game.TireCompounds = null;

        var result = await Execute("меняй на сухие");

        Assert.Equal(ExecutionOutcome.CompoundUnavailable, result.Outcome);
        Assert.Empty(_sink.Sent);
        Assert.Equal("у машины нет сухих шин", Response("меняй на сухие", result));
    }

    [Fact]
    public async Task Not_in_car_comes_first()
    {
        _game.State = ConnectionState.Connected;
        _game.TireCompounds = null;

        var result = await Execute("меняй на дождевые");

        Assert.Equal(ExecutionOutcome.NotInCar, result.Outcome);
        Assert.Equal("ты не в машине", Response("меняй на дождевые", result));
    }

    // --- Tire compound change (design D8)

    [Fact]
    public async Task Switch_to_wets()
    {
        _game.Pit = PitServiceState.Empty with { TireCompound = 0 };

        var result = await Execute("меняем резину на дождевые");

        Assert.Equal(ExecutionOutcome.Applied, result.Outcome);
        Assert.Equal(
            [new(PitCommand.TC, 1), new(PitCommand.LF, 0), new(PitCommand.RF, 0), new(PitCommand.LR, 0), new(PitCommand.RR, 0)],
            _sink.Sent);
        Assert.Equal(1, _game.Pit.TireCompound);
        Assert.Equal(Wheels.All, _game.Pit.Wheels);
        Assert.Equal("ставим дождевые", Response("меняем резину на дождевые", result));
    }

    [Fact]
    public async Task Already_on_the_requested_compound()
    {
        _game.Pit = PitServiceState.Empty.WithWheels(Wheels.All) with { TireCompound = 1 };
        _game.Ignored.UnionWith([PitCommand.TC, PitCommand.LF, PitCommand.RF, PitCommand.LR, PitCommand.RR]);

        var result = await Execute("на дождевые");

        Assert.Equal(ExecutionOutcome.Applied, result.Outcome);
    }

    [Fact]
    public async Task Compound_not_confirmed()
    {
        _game.Pit = PitServiceState.Empty with { TireCompound = 0 };
        _game.Ignored.Add(PitCommand.TC);

        var result = await Execute("меняй на дождевые");

        Assert.Equal(ExecutionOutcome.NotApplied, result.Outcome);
        Assert.Equal(Wheels.All, _game.Pit.Wheels);
        Assert.Equal("команда не применилась", Response("меняй на дождевые", result));
        Assert.Contains("compound 1", Assert.Single(_log.Failures).ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Compound_and_a_tire_addition_share_one_step()
    {
        var result = await Execute("меняй на дождевые и меняй левые");

        Assert.Equal(ExecutionOutcome.Applied, result.Outcome);
        Assert.Equal(new SentCommand(PitCommand.TC, 1), _sink.Sent[0]);
        Assert.Equal(5, _sink.Sent.Count);
    }

    [Fact]
    public async Task Compound_with_fuel()
    {
        var result = await Execute("меняй на дождевые и залей двадцать литров");

        Assert.Equal(ExecutionOutcome.Applied, result.Outcome);
        Assert.Equal(new SentCommand(PitCommand.Fuel, 20), _sink.Sent[^1]);
        Assert.Equal("ставим дождевые, заливаем 20 литров", Response("меняй на дождевые и залей двадцать литров", result));
    }

    // --- Audio cue (audio-cues delta)

    [Fact]
    public async Task Compound_not_available_gives_the_error_cue()
    {
        _game.TireCompounds = [new(0, "Dry")];

        var result = await Execute("меняй на дождевые");

        Assert.Equal(Cue.Error, CueSelector.ForResult(Parse("меняй на дождевые"), result));
    }

    private sealed class RecordingLog : IExecutionLog
    {
        public List<ApplyFailureReport> Failures { get; } = [];

        public List<string> Warnings { get; } = [];

        public void ApplyFailed(ApplyFailureReport report) => Failures.Add(report);

        public void Warning(string message) => Warnings.Add(message);
    }
}
