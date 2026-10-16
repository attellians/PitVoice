using PitVoice.Core.Cues;
using PitVoice.Core.Settings;

namespace PitVoice.Host;

/// <summary>How the "Listen" button ended (spec: Cue preview).</summary>
public enum CuePreviewOutcome
{
    Played,

    /// <summary>Volume is 0%: the cues are switched off, nothing was played.</summary>
    Muted,

    /// <summary>The output device failed; <see cref="CuePreviewResult.Error"/> says why.</summary>
    Failed,
}

/// <param name="Outcome">How the preview ended.</param>
/// <param name="DeviceName">Device that played the cues.</param>
/// <param name="Warning">E.g. the chosen device is missing and the system default was used.</param>
/// <param name="Error">Device error.</param>
public sealed record CuePreviewResult(CuePreviewOutcome Outcome, string? DeviceName, string? Warning, string? Error);

/// <summary>Plays start, success and error one after another with the current device and volume.</summary>
public static class CuePreview
{
    /// <summary>Pause between the cues.</summary>
    public static readonly TimeSpan Gap = TimeSpan.FromMilliseconds(250);

    public static async Task<CuePreviewResult> PlayAsync(IHostPlatform platform, AppSettings settings, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(platform);
        ArgumentNullException.ThrowIfNull(settings);
        if (settings.CueVolume == 0)
        {
            return new CuePreviewResult(CuePreviewOutcome.Muted, null, null, null);
        }

        var player = platform.CreateCuePlayer(settings.OutputDevice, settings.CueVolume);
        try
        {
            CuePlayback? first = null;
            foreach (var cue in new[] { Cue.Start, Cue.Success, Cue.Error })
            {
                var playback = player.Play(cue);
                first ??= playback;
                if (playback.Error is { } error)
                {
                    return new CuePreviewResult(CuePreviewOutcome.Failed, null, null, error);
                }

                await Task.Delay(player.GetDuration(cue) + Gap, cancellationToken).ConfigureAwait(false);
            }

            return new CuePreviewResult(CuePreviewOutcome.Played, first?.DeviceName, first?.Warning, null);
        }
        finally
        {
            (player as IDisposable)?.Dispose();
        }
    }
}
