using PitVoice.Core.Pit;

namespace PitVoice.IRacing.Recording;

/// <summary>
/// Plays a recording back instead of the game: <see cref="Current"/> is the last recorded snapshot at the time elapsed
/// on the <see cref="TimeProvider"/> since the source was created (or <see cref="Restart"/>ed).
/// </summary>
public sealed class ReplayTelemetrySource : ITelemetrySource
{
    private readonly IReadOnlyList<RecordedSnapshot> _records;
    private readonly TimeProvider _time;
    private long _start;

    public ReplayTelemetrySource(IReadOnlyList<RecordedSnapshot> records, TimeProvider? time = null)
    {
        ArgumentNullException.ThrowIfNull(records);
        if (records.Count == 0)
        {
            throw new ArgumentException("The recording is empty.", nameof(records));
        }

        _records = records;
        _time = time ?? TimeProvider.System;
        _start = _time.GetTimestamp();
    }

    public static ReplayTelemetrySource FromFile(string path, TimeProvider? time = null) =>
        new(TelemetryRecordFormat.ReadFile(path), time);

    public IReadOnlyList<RecordedSnapshot> Records => _records;

    public TelemetrySnapshot Current => At(_time.GetElapsedTime(_start));

    /// <summary>Starts the playback again from the beginning, or from <paramref name="offset"/> into the recording.</summary>
    public void Restart(TimeSpan offset = default) => _start = _time.GetTimestamp() - (long)(offset.TotalSeconds * _time.TimestampFrequency);

    /// <summary>The snapshot in effect at <paramref name="time"/> from the start of the recording.</summary>
    public TelemetrySnapshot At(TimeSpan time)
    {
        var current = _records[0].Snapshot;
        foreach (var (at, snapshot) in _records)
        {
            if (at > time)
            {
                break;
            }

            current = snapshot;
        }

        return current;
    }
}
