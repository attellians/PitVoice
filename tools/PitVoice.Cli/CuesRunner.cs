using System.Globalization;
using PitVoice.Core.Cues;
using PitVoice.Speech.Cues;

namespace PitVoice.Cli;

/// <summary><c>cues play</c>: plays the cues without the game or a microphone, to check the device and the volume.</summary>
internal static class CuesRunner
{
    public const string All = "all";

    private static readonly TimeSpan Pause = TimeSpan.FromMilliseconds(500);

    /// <summary>Names accepted by <c>cues play</c>.</summary>
    public static IReadOnlyList<string> Names { get; } = [.. Enum.GetValues<Cue>().Select(Name), All];

    public static async Task<int> PlayAsync(string name, int? volume, string? device, string? settingsPath, CancellationToken cancellationToken)
    {
        if (!OperatingSystem.IsWindows())
        {
            Console.Error.WriteLine("cues play needs Windows.");
            return 1;
        }

        var settings = Settings.Load(Settings.Open(settingsPath));
        var level = volume ?? settings.CueVolume;
        if (level is < 0 or > 100)
        {
            Console.Error.WriteLine(string.Create(CultureInfo.InvariantCulture, $"warning: volume {level} is out of 0..100, using {Math.Clamp(level, 0, 100)}"));
        }

        using var player = new NAudioCuePlayer(device ?? settings.OutputDevice, level);
        Console.WriteLine(string.Create(CultureInfo.InvariantCulture,
            $"device: {player.DeviceName ?? "system default"}, volume {player.Volume}%"));
        if (player.Volume == 0)
        {
            Console.WriteLine("volume 0%: cues are muted, nothing plays");
        }

        var cues = name == All ? Enum.GetValues<Cue>() : [Enum.Parse<Cue>(name, ignoreCase: true)];
        var failed = false;
        foreach (var cue in cues)
        {
            var playback = player.Play(cue);
            if (playback.Warning is not null)
            {
                Console.Error.WriteLine($"warning: {playback.Warning}");
            }

            if (playback.Error is not null)
            {
                Console.Error.WriteLine($"error: {Name(cue)}: {playback.Error}");
                failed = true;
                continue;
            }

            var duration = player.GetDuration(cue);
            Console.WriteLine(string.Create(CultureInfo.InvariantCulture,
                $"{Name(cue),-8} {duration.TotalMilliseconds:0} ms{(playback.Played ? $" on '{playback.DeviceName}'" : " (muted)")}"));
            try
            {
                await Task.Delay(duration + Pause, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return 1;
            }
        }

        return failed ? 1 : 0;
    }

    private static string Name(Cue cue) => cue.ToString().ToLowerInvariant();
}
