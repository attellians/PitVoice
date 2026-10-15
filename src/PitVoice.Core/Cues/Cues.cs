using PitVoice.Core.Audio;
using PitVoice.Core.Intents;
using PitVoice.Core.Parsing;
using PitVoice.Core.Pit;

namespace PitVoice.Core.Cues;

/// <summary>The three built-in sounds (design decision 1). There is no voice and no separate end-of-recording cue.</summary>
public enum Cue
{
    /// <summary>Recording started.</summary>
    Start,

    /// <summary>The phrase was applied, or it was "cancel".</summary>
    Success,

    /// <summary>Anything else: not understood, no game, not in the car, not applied, not supported, a failed stage.</summary>
    Error,
}

/// <summary>Result of <see cref="IAudioCuePlayer.Play"/>. Problems are values, never exceptions.</summary>
/// <param name="Played">True when the sound started.</param>
/// <param name="DeviceName">Device actually used.</param>
/// <param name="Warning">E.g. the selected device is missing and the default one is used.</param>
/// <param name="Error">Why nothing plays (no output device, broken sound).</param>
public sealed record CuePlayback(bool Played, string? DeviceName, string? Warning, string? Error)
{
    /// <summary>Volume 0: nothing plays, nothing is wrong.</summary>
    public static CuePlayback Muted { get; } = new(false, null, null, null);

    public static CuePlayback Ok(string deviceName, string? warning = null) => new(true, deviceName, warning, null);

    public static CuePlayback Failed(string error) => new(false, null, null, error);
}

/// <summary>
/// Plays the cues. A new cue always cuts the current one, so cues never overlap. Implementations never throw for device
/// or sound problems.
/// </summary>
public interface IAudioCuePlayer
{
    CuePlayback Play(Cue cue);

    /// <summary>Cuts the sound that is playing, if any.</summary>
    void Stop();

    /// <summary>Length of the sound; zero when it cannot be played.</summary>
    TimeSpan GetDuration(Cue cue);
}

/// <summary>Lists output devices; devices are chosen by name like microphones (FR-AUD-1).</summary>
public interface IAudioOutputCatalog
{
    IReadOnlyList<AudioDevice> GetOutputDevices();
}

/// <summary>Which result cue a phrase gets (design decision 9).</summary>
public static class CueSelector
{
    /// <summary>
    /// <c>success</c> for <see cref="ExecutionOutcome.Applied"/> and for a phrase that is only <c>assistant.cancel</c>;
    /// <c>error</c> for a rejected phrase and every other outcome.
    /// </summary>
    public static Cue ForResult(ParseResult parse, ExecutionResult? execution)
    {
        ArgumentNullException.ThrowIfNull(parse);
        if (parse is not ParseResult.Accepted accepted)
        {
            return Cue.Error;
        }

        if (accepted.Commands.Count > 0 && accepted.Commands.All(c => c.Intent == IntentCatalog.AssistantCancel))
        {
            return Cue.Success;
        }

        return execution?.Outcome == ExecutionOutcome.Applied ? Cue.Success : Cue.Error;
    }
}
