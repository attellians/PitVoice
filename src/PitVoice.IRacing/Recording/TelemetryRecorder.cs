using PitVoice.Core.Pit;

namespace PitVoice.IRacing.Recording;

/// <summary>
/// Writes telemetry snapshots as JSON Lines: at least every <see cref="Interval"/>, and at once when the connection
/// state or the black box selection changes.
/// </summary>
public sealed class TelemetryRecorder(TextWriter writer)
{
    public static readonly TimeSpan Interval = TimeSpan.FromMilliseconds(100);
    public static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(16);

    private TelemetrySnapshot? _last;
    private TimeSpan _lastWritten;

    public int Written { get; private set; }

    /// <summary>Writes the snapshot when it is due; returns true if a line was written.</summary>
    public bool Sample(TelemetrySnapshot snapshot, TimeSpan time)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        if (_last is not null && time - _lastWritten < Interval && !Changed(_last, snapshot))
        {
            return false;
        }

        writer.WriteLine(TelemetryRecordFormat.ToLine(new RecordedSnapshot(time, snapshot)));
        _last = snapshot;
        _lastWritten = time;
        Written++;
        return true;
    }

    /// <summary>Polls the source until cancelled, flushing after every written line.</summary>
    public async Task RunAsync(ITelemetrySource source, TimeProvider? time = null, Action<TelemetrySnapshot>? onStateChange = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);
        time ??= TimeProvider.System;
        var start = time.GetTimestamp();
        ConnectionState? lastState = null;
        while (!cancellationToken.IsCancellationRequested)
        {
            var snapshot = source.Current;
            if (snapshot.State != lastState)
            {
                lastState = snapshot.State;
                onStateChange?.Invoke(snapshot);
            }

            if (Sample(snapshot, time.GetElapsedTime(start)))
            {
                await writer.FlushAsync(CancellationToken.None).ConfigureAwait(false);
            }

            try
            {
                await Task.Delay(PollInterval, time, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }

        await writer.FlushAsync(CancellationToken.None).ConfigureAwait(false);
    }

    private static bool Changed(TelemetrySnapshot previous, TelemetrySnapshot current) =>
        previous.State != current.State
        || previous.Pit?.Flags != current.Pit?.Flags
        || previous.Pit?.FuelToAdd != current.Pit?.FuelToAdd;
}
