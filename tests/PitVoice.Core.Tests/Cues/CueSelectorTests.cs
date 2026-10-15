using PitVoice.Core.Cues;
using PitVoice.Core.Intents;
using PitVoice.Core.Parsing;
using PitVoice.Core.Pit;
using Xunit;

namespace PitVoice.Core.Tests.Cues;

public sealed class CueSelectorTests
{
    private static readonly ParseResult.Accepted Fuel =
        new([new ParsedCommand(IntentCatalog.FuelAdd, 30, FuelUnits.Liters, 1.0)]);

    private static readonly ParseResult.Accepted Cancel =
        new([new ParsedCommand(IntentCatalog.AssistantCancel, null, null, 1.0)]);

    [Fact]
    public void Applied_IsSuccess() =>
        Assert.Equal(Cue.Success, CueSelector.ForResult(Fuel, ExecutionResult.Of(ExecutionOutcome.Applied)));

    [Fact]
    public void Cancel_IsSuccess()
    {
        Assert.Equal(Cue.Success, CueSelector.ForResult(Cancel, ExecutionResult.Of(ExecutionOutcome.Applied)));
        Assert.Equal(Cue.Success, CueSelector.ForResult(Cancel, null));
    }

    [Theory]
    [InlineData(RejectReason.Empty)]
    [InlineData(RejectReason.NotUnderstood)]
    [InlineData(RejectReason.Conflict)]
    [InlineData(RejectReason.LowConfidence)]
    public void Rejected_IsError(RejectReason reason) =>
        Assert.Equal(Cue.Error, CueSelector.ForResult(new ParseResult.Rejected(reason, "test"), null));

    [Theory]
    [InlineData(ExecutionOutcome.NotConnected)]
    [InlineData(ExecutionOutcome.NotInCar)]
    [InlineData(ExecutionOutcome.PartiallyApplied)]
    [InlineData(ExecutionOutcome.NotApplied)]
    [InlineData(ExecutionOutcome.NotSupported)]
    public void OtherOutcomes_AreError(ExecutionOutcome outcome) =>
        Assert.Equal(Cue.Error, CueSelector.ForResult(Fuel, ExecutionResult.Of(outcome)));

    [Fact]
    public void FailedStage_NoExecution_IsError() =>
        Assert.Equal(Cue.Error, CueSelector.ForResult(Fuel, null));
}
