using System.Buffers.Binary;
using System.Text;

namespace PitVoice.IRacing.Telemetry;

/// <summary>The few telemetry values PitVoice reads, as found in the SDK memory.</summary>
/// <param name="Status">Header <c>status</c>; bit <see cref="IRacingMemoryLayout.StatusConnected"/> is set while the sim runs.</param>
/// <param name="Tick">The game's update counter of the newest buffer.</param>
/// <param name="IsOnTrack">The driver is in the car.</param>
/// <param name="PitSvFlags">Black box selection bits.</param>
/// <param name="PitSvFuel">Fuel to add, liters.</param>
/// <param name="FuelLevel">Fuel in the tank, liters.</param>
public readonly record struct RawTelemetry(int Status, int Tick, bool IsOnTrack, int PitSvFlags, float PitSvFuel, float FuelLevel)
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
