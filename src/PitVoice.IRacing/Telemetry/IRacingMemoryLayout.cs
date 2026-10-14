using System.Buffers.Binary;
using System.Globalization;
using System.Text;

namespace PitVoice.IRacing.Telemetry;

/// <summary>The few telemetry values PitVoice reads, as found in the SDK memory.</summary>
/// <param name="Status">Header <c>status</c>; bit <see cref="IRacingMemoryLayout.StatusConnected"/> is set while the sim runs.</param>
/// <param name="Tick">The game's update counter of the newest buffer.</param>
/// <param name="IsOnTrack">The driver is in the car.</param>
/// <param name="PitSvFlags">Black box selection bits.</param>
/// <param name="PitSvFuel">Fuel to add, liters.</param>
/// <param name="FuelLevel">Fuel in the tank, liters.</param>
/// <param name="FuelCapacity">Usable tank of the car from session info, liters; filled in by the telemetry source.</param>
public readonly record struct RawTelemetry(int Status, int Tick, bool IsOnTrack, int PitSvFlags, float PitSvFuel, float FuelLevel, double? FuelCapacity = null)
{
    public bool IsConnected => (Status & IRacingMemoryLayout.StatusConnected) != 0;
}

/// <summary>
/// Reads <see cref="RawTelemetry"/> from an image of the SDK shared memory (<c>irsdk_header</c>, <c>irsdk_varHeader</c>,
/// rotating data buffers). Works on a byte span, so it is tested on synthetic images on any OS.
/// </summary>
public static class IRacingMemoryLayout
{
    public const string MapName = @"Local\IRSDKMemMapFileName";
    public const int StatusConnected = 1;

    internal const int StatusOffset = 4;
    internal const int SessionInfoUpdateOffset = 12;
    internal const int SessionInfoLenOffset = 16;
    internal const int SessionInfoOffsetOffset = 20;
    internal const int NumVarsOffset = 24;
    internal const int VarHeaderOffsetOffset = 28;
    internal const int NumBufOffset = 32;
    internal const int VarBufOffset = 48;
    internal const int VarBufSize = 16;
    internal const int MaxBuffers = 4;
    internal const int HeaderSize = VarBufOffset + (MaxBuffers * VarBufSize);
    internal const int VarHeaderSize = 144;
    internal const int VarNameOffset = 16;
    internal const int VarNameLength = 32;

    /// <summary>Variable types (<c>irsdk_VarType</c>).</summary>
    internal enum VarType
    {
        Char = 0,
        Bool = 1,
        Int = 2,
        BitField = 3,
        Float = 4,
        Double = 5,
    }

    private const int ReadAttempts = 3;

    /// <summary>
    /// Reads the values. Returns false when the image is too small or inconsistent. A header without the connected bit
    /// gives a value with only <see cref="RawTelemetry.Status"/> set.
    /// </summary>
    public static bool TryRead(ReadOnlySpan<byte> memory, out RawTelemetry telemetry)
    {
        telemetry = default;
        if (memory.Length < HeaderSize)
        {
            return false;
        }

        var status = ReadInt(memory, StatusOffset);
        if ((status & StatusConnected) == 0)
        {
            telemetry = new RawTelemetry(status, 0, false, 0, 0, 0);
            return true;
        }

        var numVars = ReadInt(memory, NumVarsOffset);
        var varHeaderOffset = ReadInt(memory, VarHeaderOffsetOffset);
        var numBuf = Math.Clamp(ReadInt(memory, NumBufOffset), 1, MaxBuffers);
        if (numVars < 0 || varHeaderOffset < 0 || (long)varHeaderOffset + ((long)numVars * VarHeaderSize) > memory.Length)
        {
            return false;
        }

        Variable? isOnTrack = null, pitSvFlags = null, pitSvFuel = null, fuelLevel = null;
        for (var i = 0; i < numVars; i++)
        {
            var header = memory.Slice(varHeaderOffset + (i * VarHeaderSize), VarHeaderSize);
            var name = ReadName(header.Slice(VarNameOffset, VarNameLength));
            var variable = new Variable((VarType)ReadInt(header, 0), ReadInt(header, 4));
            switch (name)
            {
                case "IsOnTrack": isOnTrack = variable; break;
                case "PitSvFlags": pitSvFlags = variable; break;
                case "PitSvFuel": pitSvFuel = variable; break;
                case "FuelLevel": fuelLevel = variable; break;
            }
        }

        // The game writes buffers in rotation; take the newest one and make sure it did not change while reading.
        for (var attempt = 0; attempt < ReadAttempts; attempt++)
        {
            var latest = 0;
            for (var b = 1; b < numBuf; b++)
            {
                if (BufferTick(memory, b) > BufferTick(memory, latest))
                {
                    latest = b;
                }
            }

            var tick = BufferTick(memory, latest);
            var offset = ReadInt(memory, VarBufOffset + (latest * VarBufSize) + 4);
            if (offset < 0 || offset >= memory.Length)
            {
                return false;
            }

            var data = memory[offset..];
            if (!TryValue(data, isOnTrack, out var onTrack)
                || !TryValue(data, pitSvFlags, out var flags)
                || !TryValue(data, pitSvFuel, out var fuel)
                || !TryValue(data, fuelLevel, out var level))
            {
                return false;
            }

            if (BufferTick(memory, latest) == tick)
            {
                telemetry = new RawTelemetry(status, tick, onTrack != 0, (int)flags, (float)fuel, (float)level);
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// The session info update counter (<c>sessionInfoUpdate</c>); it changes whenever the session YAML is rewritten.
    /// </summary>
    public static int SessionInfoUpdate(ReadOnlySpan<byte> memory) =>
        memory.Length < HeaderSize ? -1 : ReadInt(memory, SessionInfoUpdateOffset);

    /// <summary>
    /// Usable fuel tank of the driver's car in liters: <c>DriverCarFuelMaxLtr × DriverCarMaxFuelPct</c> from the session
    /// YAML. Only these two lines are read; nothing else of the session (names, results) is decoded.
    /// </summary>
    public static bool TryReadFuelCapacity(ReadOnlySpan<byte> memory, out double liters)
    {
        liters = 0;
        if (memory.Length < HeaderSize)
        {
            return false;
        }

        var length = ReadInt(memory, SessionInfoLenOffset);
        var offset = ReadInt(memory, SessionInfoOffsetOffset);
        if (length <= 0 || offset < 0 || (long)offset + length > memory.Length)
        {
            return false;
        }

        var yaml = memory.Slice(offset, length);
        var end = yaml.IndexOf((byte)0);
        if (end >= 0)
        {
            yaml = yaml[..end];
        }

        if (!TryReadNumber(yaml, " DriverCarFuelMaxLtr:"u8, out var maxLiters, out _) || maxLiters <= 0)
        {
            return false;
        }

        var fraction = 1.0;
        if (TryReadNumber(yaml, " DriverCarMaxFuelPct:"u8, out var pct, out var percentSign))
        {
            fraction = percentSign ? pct / 100 : pct;
        }

        if (fraction <= 0 || fraction > 1)
        {
            fraction = 1.0;
        }

        liters = maxLiters * fraction;
        return true;
    }

    /// <summary>Reads the number after <paramref name="key"/> up to the end of the line; notes a trailing <c>%</c>.</summary>
    private static bool TryReadNumber(ReadOnlySpan<byte> yaml, ReadOnlySpan<byte> key, out double value, out bool percent)
    {
        value = 0;
        percent = false;
        var at = yaml.IndexOf(key);
        if (at < 0)
        {
            return false;
        }

        var rest = yaml[(at + key.Length)..];
        var newline = rest.IndexOf((byte)'\n');
        var text = Encoding.ASCII.GetString(newline < 0 ? rest : rest[..newline]).Trim();
        if (text.EndsWith('%'))
        {
            percent = true;
            text = text[..^1].TrimEnd();
        }

        return double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value);
    }

    private readonly record struct Variable(VarType Type, int Offset);

    private static int BufferTick(ReadOnlySpan<byte> memory, int buffer) => ReadInt(memory, VarBufOffset + (buffer * VarBufSize));

    private static int ReadInt(ReadOnlySpan<byte> memory, int offset) => BinaryPrimitives.ReadInt32LittleEndian(memory.Slice(offset, 4));

    private static string ReadName(ReadOnlySpan<byte> bytes)
    {
        var end = bytes.IndexOf((byte)0);
        return Encoding.ASCII.GetString(end < 0 ? bytes : bytes[..end]);
    }

    /// <summary>Reads a variable as a number; a variable missing from this game version reads as 0.</summary>
    private static bool TryValue(ReadOnlySpan<byte> data, Variable? variable, out double value)
    {
        value = 0;
        if (variable is not { } v)
        {
            return true;
        }

        var size = v.Type switch
        {
            VarType.Char or VarType.Bool => 1,
            VarType.Int or VarType.BitField or VarType.Float => 4,
            VarType.Double => 8,
            _ => 0,
        };
        if (size == 0 || v.Offset < 0 || v.Offset + size > data.Length)
        {
            return false;
        }

        var bytes = data.Slice(v.Offset, size);
        value = v.Type switch
        {
            VarType.Char or VarType.Bool => bytes[0],
            VarType.Int or VarType.BitField => BinaryPrimitives.ReadInt32LittleEndian(bytes),
            VarType.Float => BinaryPrimitives.ReadSingleLittleEndian(bytes),
            _ => BinaryPrimitives.ReadDoubleLittleEndian(bytes),
        };
        return true;
    }
}
