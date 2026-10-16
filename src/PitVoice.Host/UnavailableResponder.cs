using PitVoice.Core.Cues;
using PitVoice.Core.Events;
using PitVoice.Core.Pipeline;
using PitVoice.Core.Ptt;

namespace PitVoice.Host;

/// <summary>
/// A PTT press while the pipeline cannot work (model loading or missing, pack failed): the phrase ends at once with the
/// <c>error</c> cue and this reason (spec: Language switch, Delete model in use).
/// </summary>
/// <param name="PhraseId">Phrase id.</param>
/// <param name="Timestamp">Time of the press.</param>
/// <param name="Reason">Why the pipeline is unavailable.</param>
/// <param name="Detail">Model id or error text.</param>
public sealed record PttUnavailable(int PhraseId, DateTimeOffset Timestamp, StatusReason Reason, string? Detail)
    : PipelineEvent(PhraseId, Timestamp);

/// <summary>Stands in for <see cref="VoicePipeline"/> while it cannot be built: every press gets the error cue.</summary>
internal sealed class UnavailableResponder(
    StatusReason reason,
    string? detail,
    IAudioCuePlayer cues,
    IEventBus events,
    PhraseIdSource ids,
    TimeProvider time) : IPttHandler
{
    public StatusReason Reason { get; } = reason;

    public string? Detail { get; } = detail;

    public bool IsBusy => false;

    public void OnTransition(PttTransition transition)
    {
        if (transition is not PttTransition.Started)
        {
            return;
        }

        var id = ids.Next();
        events.Publish(new PttPressed(id, time.GetUtcNow()));
        cues.Stop();
        CuePlayback playback;
        try
        {
            playback = cues.Play(Cue.Error);
        }
#pragma warning disable CA1031 // A cue failure is only reported.
        catch (Exception e)
#pragma warning restore CA1031
        {
            playback = CuePlayback.Failed(e.Message);
        }

        if (playback.Error is not null)
        {
            events.Publish(new PipelineFailed(id, time.GetUtcNow(), PipelineStage.Cue, playback.Error));
        }

        events.Publish(new PttUnavailable(id, time.GetUtcNow(), Reason, Detail));
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}

/// <summary>What the PTT state machine drives: the real pipeline or <see cref="UnavailableResponder"/>.</summary>
internal interface IPttHandler : IAsyncDisposable
{
    bool IsBusy { get; }

    void OnTransition(PttTransition transition);
}

/// <summary>The real pipeline as a <see cref="IPttHandler"/>.</summary>
internal sealed class PipelineHandler(VoicePipeline pipeline) : IPttHandler
{
    public bool IsBusy => pipeline.IsBusy;

    public void OnTransition(PttTransition transition) => pipeline.OnTransition(transition);

    public ValueTask DisposeAsync() => pipeline.DisposeAsync();
}
