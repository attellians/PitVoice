using PitVoice.Core.Events;
using PitVoice.Core.Pit;
using Serilog;

namespace PitVoice.Host.Diagnostics;

/// <summary>
/// Writes stage failures and warnings of the pipeline to the log: the stage, the phrase id and the message (design
/// decision 2). Every other event carries what the user said and is not looked at, so a phrase leaves no record.
/// </summary>
public sealed class PipelineIssueLogger(ILogger log)
{
    public void OnEvent(PipelineEvent pipelineEvent)
    {
        switch (pipelineEvent)
        {
            case PipelineFailed failed:
                log.Error("Stage {Stage} failed, phrase {PhraseId}: {Error}", failed.Stage, failed.PhraseId, failed.Error);
                break;
            case PipelineWarning warning:
                log.Warning("Stage {Stage} warning, phrase {PhraseId}: {Message}", warning.Stage, warning.PhraseId, warning.Message);
                break;
        }
    }
}

/// <summary>
/// The execution report for the file log: the outcome and how many commands were sent and not confirmed. The phrase, the
/// commands and the black box states stay out (spec: Pit commands not applied).
/// </summary>
public sealed class SerilogExecutionLog(ILogger log) : IExecutionLog
{
    public void ApplyFailed(ApplyFailureReport report)
    {
        ArgumentNullException.ThrowIfNull(report);
        log.Warning(
            "Pit commands {Outcome}: {Sent} sent, {Unconfirmed} not confirmed{Nothing}{Lost}",
            report.Outcome == ExecutionOutcome.PartiallyApplied ? "partially applied" : "not applied",
            report.Sent.Count,
            report.Expected.Count,
            report.NothingConfirmed ? ", nothing confirmed" : string.Empty,
            report.Actual is null ? ", connection to the game lost" : string.Empty);
    }

    public void Warning(string message) => log.Warning("{Message}", message);
}

/// <summary>Passes everything to several logs (the console and the file for <c>run</c>).</summary>
public sealed class CompositeExecutionLog(params IExecutionLog[] logs) : IExecutionLog
{
    public void ApplyFailed(ApplyFailureReport report)
    {
        foreach (var log in logs)
        {
            log.ApplyFailed(report);
        }
    }

    public void Warning(string message)
    {
        foreach (var log in logs)
        {
            log.Warning(message);
        }
    }
}
