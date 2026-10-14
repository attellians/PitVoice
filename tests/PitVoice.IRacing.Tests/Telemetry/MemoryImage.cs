using System.Buffers.Binary;
using System.Text;
using PitVoice.IRacing.Telemetry;

namespace PitVoice.IRacing.Tests.Telemetry;

/// <summary>Builds a synthetic image of the SDK shared memory with the variables PitVoice reads, plus a decoy.</summary>
internal sealed class MemoryImage
{
    private const int VarHeadersAt = 256;
    private const int BufferLength = 64;
    private const int BuffersAt = 1024;
    private const int SessionInfoAt = 1536;
    private const int SessionInfoLength = 1024;

    // name, type, offset inside a data buffer
    private static readonly (string Name, int Type, int Offset)[] Variables =
    [
        ("SessionTime", 5, 0),   // decoy: a double the reader must skip
        ("IsOnTrack", 1, 8),
        ("PitSvFlags", 3, 12),
        ("PitSvFuel", 4, 16),
        ("FuelLevel", 4, 20),
    ];

    public byte[] Bytes { get; } = new byte[SessionInfoAt + SessionInfoLength];

    public MemoryImage(int status = IRacingMemoryLayout.StatusConnected, int numBuf = 3)
    {
        WriteInt(IRacingMemoryLayout.StatusOffset, status);
        WriteInt(IRacingMemoryLayout.NumVarsOffset, Variables.Length);
        WriteInt(IRacingMemoryLayout.VarHeaderOffsetOffset, VarHeadersAt);
        WriteInt(IRacingMemoryLayout.NumBufOffset, numBuf);
        for (var i = 0; i < Variables.Length; i++)
        {
            var at = VarHeadersAt + (i * IRacingMemoryLayout.VarHeaderSize);
            WriteInt(at, Variables[i].Type);
            WriteInt(at + 4, Variables[i].Offset);
            WriteInt(at + 8, 1);
            Encoding.ASCII.GetBytes(Variables[i].Name).CopyTo(Bytes, at + IRacingMemoryLayout.VarNameOffset);
        }

        for (var b = 0; b < 4; b++)
        {
            WriteInt(IRacingMemoryLayout.VarBufOffset + (b * IRacingMemoryLayout.VarBufSize) + 4, BuffersAt + (b * BufferLength));
        }
    }

    /// <summary>Writes values into buffer <paramref name="buffer"/> and gives it tick <paramref name="tick"/>.</summary>
    public MemoryImage Buffer(int buffer, int tick, bool isOnTrack, int pitSvFlags, float pitSvFuel, float fuelLevel)
    {
        WriteInt(IRacingMemoryLayout.VarBufOffset + (buffer * IRacingMemoryLayout.VarBufSize), tick);
        var data = BuffersAt + (buffer * BufferLength);
        BinaryPrimitives.WriteDoubleLittleEndian(Bytes.AsSpan(data), 1234.5);
        Bytes[data + 8] = isOnTrack ? (byte)1 : (byte)0;
        WriteInt(data + 12, pitSvFlags);
        BinaryPrimitives.WriteSingleLittleEndian(Bytes.AsSpan(data + 16), pitSvFuel);
        BinaryPrimitives.WriteSingleLittleEndian(Bytes.AsSpan(data + 20), fuelLevel);
        return this;
    }

    /// <summary>Writes a session info YAML (null-terminated, Latin-1 as the game does) and bumps its update counter.</summary>
    public MemoryImage SessionInfo(string yaml, int update = 1)
    {
        var bytes = Encoding.Latin1.GetBytes(yaml);
        Array.Clear(Bytes, SessionInfoAt, SessionInfoLength);
        bytes.CopyTo(Bytes, SessionInfoAt);
        WriteInt(IRacingMemoryLayout.SessionInfoUpdateOffset, update);
        WriteInt(IRacingMemoryLayout.SessionInfoLenOffset, SessionInfoLength);
        WriteInt(IRacingMemoryLayout.SessionInfoOffsetOffset, SessionInfoAt);
        return this;
    }

    private void WriteInt(int offset, int value) => BinaryPrimitives.WriteInt32LittleEndian(Bytes.AsSpan(offset), value);
}
