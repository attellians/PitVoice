using System.Text.Json;
using System.Text.Json.Serialization;
using PitVoice.Core.Pit;

namespace PitVoice.IRacing.Recording;

/// <summary>
/// One line of a telemetry recording (design decision 10). Nothing else about the session is stored. <c>fuelCapacity</c>
/// is optional: recordings made before it was added do not have it.
/// </summary>
internal sealed record TelemetryRecord(
    [property: JsonPropertyName("t"), JsonRequired] long T,
    [property: JsonPropertyName("state"), JsonRequired] ConnectionState State,
    [property: JsonPropertyName("tick"), JsonRequired] int Tick,
    [property: JsonPropertyName("pitSvFlags"), JsonRequired] int? PitSvFlags,
    [property: JsonPropertyName("pitSvFuel"), JsonRequired] double? PitSvFuel,
    [property: JsonPropertyName("fuelLevel"), JsonRequired] double? FuelLevel,
    [property: JsonPropertyName("fuelCapacity")] double? FuelCapacity = null);

/// <summary>A snapshot and the time it was taken, from the start of the recording.</summary>
public readonly record struct RecordedSnapshot(TimeSpan Time, TelemetrySnapshot Snapshot);

/// <summary>JSON Lines format of telemetry recordings: one object per line with exactly the keys of <see cref="Keys"/>.</summary>
public static class TelemetryRecordFormat
{
    public static IReadOnlyList<string> Keys { get; } = ["t", "state", "tick", "pitSvFlags", "pitSvFuel", "fuelLevel", "fuelCapacity"];

    public static string ToLine(RecordedSnapshot recorded)
    {
        var (time, snapshot) = recorded;
        var pit = snapshot.Pit;
        var record = new TelemetryRecord(
            (long)time.TotalMilliseconds,
            snapshot.State,
            snapshot.Tick,
            pit is null ? null : (int)pit.Flags,
            pit?.FuelToAdd,
            pit?.FuelLevel,
            snapshot.FuelCapacity);
        return JsonSerializer.Serialize(record, TelemetryRecordContext.Default.TelemetryRecord);
    }

    /// <exception cref="InvalidDataException">The line is not a valid record; the message names the source and line.</exception>
    public static RecordedSnapshot FromLine(string line, string source, int lineNumber)
    {
        TelemetryRecord? record;
        try
        {
            record = JsonSerializer.Deserialize(line, TelemetryRecordContext.Default.TelemetryRecord);
        }
        catch (JsonException e)
        {
            throw new InvalidDataException($"{source}:{lineNumber}: damaged telemetry record: {e.Message}", e);
        }

        if (record is null)
        {
            throw new InvalidDataException($"{source}:{lineNumber}: empty telemetry record");
        }

        PitServiceState? pit = null;
        if (record.PitSvFlags is { } flags)
        {
            pit = new PitServiceState((PitServiceFlags)flags, record.PitSvFuel ?? 0, record.FuelLevel ?? 0);
        }

        return new RecordedSnapshot(TimeSpan.FromMilliseconds(record.T), new TelemetrySnapshot(record.State, pit, record.Tick) { FuelCapacity = record.FuelCapacity });
    }

    /// <summary>Reads a whole recording. Empty lines are skipped; any damaged line fails the whole file.</summary>
    public static IReadOnlyList<RecordedSnapshot> Read(TextReader reader, string source)
    {
        ArgumentNullException.ThrowIfNull(reader);
        var result = new List<RecordedSnapshot>();
        var number = 0;
        while (reader.ReadLine() is { } line)
        {
            number++;
            if (line.Length > 0)
            {
                result.Add(FromLine(line, source, number));
            }
        }

        return result;
    }

    public static IReadOnlyList<RecordedSnapshot> ReadFile(string path)
    {
        using var reader = new StreamReader(path);
        return Read(reader, path);
    }
}

/// <summary>Source-generated serializer: works in trimmed builds where reflection-based JSON is disabled.</summary>
[JsonSourceGenerationOptions(
    UseStringEnumConverter = true,
    DefaultIgnoreCondition = JsonIgnoreCondition.Never,
    UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow)]
[JsonSerializable(typeof(TelemetryRecord))]
internal sealed partial class TelemetryRecordContext : JsonSerializerContext;
