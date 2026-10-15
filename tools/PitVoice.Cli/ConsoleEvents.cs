using System.Globalization;
using PitVoice.Core.Events;
using PitVoice.Core.Ptt;

namespace PitVoice.Cli;

/// <summary>Prints pipeline events for <c>run</c>, one line each, prefixed with the time and the phrase id.</summary>
internal static class ConsoleEvents
{
    /// <summary>Target from the end of the recording to the result cue (TZ §7); exceeding it is shown, not an error.</summary>
    public static readonly TimeSpan LatencyBudget = TimeSpan.FromSeconds(1);

    public static void Print(PipelineEvent e)
    {
        var line = Describe(e);
        var prefix = string.Create(CultureInfo.InvariantCulture, $"{e.Timestamp.ToLocalTime():HH:mm:ss.fff}  #{e.PhraseId}  ");
        if (e is PipelineFailed or PipelineWarning)
        {
            Console.Error.WriteLine(prefix + line);
        }
        else
        {
            Console.WriteLine(prefix + line);
        }
    }

    public static string Describe(PipelineEvent e) => e switch
    {
        PttPressed => "recording...",
        PttReleased { Ignored: true } => "released: too short, ignored",
        PttReleased released => $"stopped: {Describe(released.Reason)}",
        AudioCaptured audio => string.Create(CultureInfo.InvariantCulture,
            $"audio:      {audio.Duration.TotalSeconds:0.00} s{(audio.Truncated ? " (cut to 8 s)" : string.Empty)} from '{audio.DeviceName}'"),
        SpeechRecognized speech => string.Create(CultureInfo.InvariantCulture,
            $"recognized: \"{speech.Text}\" [{string.Join(' ', speech.Phrase.Words.Select(w => w.Confidence is { } c ? $"{w.Text}:{c:0.00}" : w.Text))}] in {speech.RecognitionTime.TotalMilliseconds:0} ms"),
        CommandsParsed parsed => $"parsed:     {string.Join("; ", parsed.Commands)}",
        CommandsRejected rejected => $"rejected:   {rejected.Reason}: {rejected.Details}",
        CommandsApplied applied => $"applied:    sent {(applied.Sent.Count == 0 ? "nothing" : string.Join(", ", applied.Sent))}",
        ApplyFailed failed => $"outcome:    {GameRunner.Describe(failed.Outcome)}"
            + (failed.Unconfirmed.Count > 0 ? $"; unconfirmed: {string.Join(", ", failed.Unconfirmed.Select(c => c.Intent))}" : string.Empty),
        ResultSignaled { Cancelled: true } signaled => $"cue {Name(signaled.Cue)} cancelled by a new press; response: {signaled.ResponseText}",
        ResultSignaled signaled => Describe(signaled),
        PipelineFailed failed => $"error at {failed.Stage.ToString().ToLowerInvariant()}: {failed.Error}",
        PipelineWarning warning => $"warning ({warning.Stage.ToString().ToLowerInvariant()}): {warning.Message}",
        _ => e.ToString(),
    };

    private static string Describe(ResultSignaled signaled)
    {
        var latency = signaled.LatencyFromStop.TotalMilliseconds.ToString("0", CultureInfo.InvariantCulture);
        var over = signaled.LatencyFromStop > LatencyBudget ? " (over the 1000 ms target)" : string.Empty;
        var response = signaled.ResponseText.Length > 0 ? $"; response: {signaled.ResponseText}" : string.Empty;
        return $"cue {Name(signaled.Cue)} {latency} ms after the recording ended{over}{response}";
    }

    private static string Describe(RecordingStopReason? reason) => reason switch
    {
        RecordingStopReason.Released => "button released",
        RecordingStopReason.SecondPress => "second press",
        RecordingStopReason.AutoStop => "8 s without a second press",
        RecordingStopReason.DeviceLost => "PTT device disconnected",
        _ => "stopped",
    };

    private static string Name(Core.Cues.Cue cue) => cue.ToString().ToLowerInvariant();
}
