using System.Buffers.Binary;
using System.Globalization;

namespace PitVoice.Core.Vr;

/// <summary>A message from the app to the VR helper (add-vr-overlay D6).</summary>
public abstract record VrMessage;

/// <summary>A picture of the indicator: <paramref name="Rgba"/> holds <c>Width × Height</c> pixels, 4 bytes each, not premultiplied.</summary>
public sealed record VrFrame(int Width, int Height, byte[] Rgba) : VrMessage;

/// <summary>Show or hide the indicator in the headset.</summary>
public sealed record VrVisible(bool Visible) : VrMessage;

/// <summary>
/// Where the picture stands, relative to the head (add-vr-overlay D7): the direction of its center in degrees (yaw to the
/// right, pitch up) and its angular width in degrees.
/// </summary>
public sealed record VrPlace(float Yaw, float Pitch, float Width) : VrMessage;

/// <summary>The helper answers "pong"; no answer for 5 s is a failure (add-vr-overlay D8).</summary>
public sealed record VrPing : VrMessage;

/// <summary>A message on the input of the helper could not be read.</summary>
public sealed class VrProtocolException(string message) : Exception(message);

/// <summary>
/// The binary messages on the standard input of the helper (add-vr-overlay D6): a 4-byte little-endian length of what
/// follows, a type byte, then the payload. The answers on its standard output are text lines, see <see cref="VrReply"/>.
/// </summary>
public static class VrProtocol
{
    /// <summary>Largest picture side; the indicator at 200 % with the longest word is well below it.</summary>
    public const int MaxSide = 2048;

    private const byte FrameType = 1;
    private const byte VisibleType = 2;
    private const byte PlaceType = 3;
    private const byte PingType = 4;

    public static void Write(Stream stream, VrMessage message)
    {
        ArgumentNullException.ThrowIfNull(stream);
        ArgumentNullException.ThrowIfNull(message);
        byte[] body;
        switch (message)
        {
            case VrFrame frame:
                if (frame.Rgba.Length != (long)frame.Width * frame.Height * 4)
                {
                    throw new ArgumentException($"frame of {frame.Width}x{frame.Height} needs {frame.Width * frame.Height * 4} bytes, got {frame.Rgba.Length}", nameof(message));
                }

                body = new byte[9 + frame.Rgba.Length];
                body[0] = FrameType;
                BinaryPrimitives.WriteInt32LittleEndian(body.AsSpan(1), frame.Width);
                BinaryPrimitives.WriteInt32LittleEndian(body.AsSpan(5), frame.Height);
                frame.Rgba.CopyTo(body, 9);
                break;
            case VrVisible visible:
                body = [VisibleType, visible.Visible ? (byte)1 : (byte)0];
                break;
            case VrPlace place:
                body = new byte[13];
                body[0] = PlaceType;
                BinaryPrimitives.WriteSingleLittleEndian(body.AsSpan(1), place.Yaw);
                BinaryPrimitives.WriteSingleLittleEndian(body.AsSpan(5), place.Pitch);
                BinaryPrimitives.WriteSingleLittleEndian(body.AsSpan(9), place.Width);
                break;
            case VrPing:
                body = [PingType];
                break;
            default:
                throw new ArgumentException($"unknown message {message.GetType().Name}", nameof(message));
        }

        Span<byte> length = stackalloc byte[4];
        BinaryPrimitives.WriteInt32LittleEndian(length, body.Length);
        stream.Write(length);
        stream.Write(body);
        stream.Flush();
    }

    /// <summary>The next message, or null when the stream ended between messages (the app closed it).</summary>
    /// <exception cref="VrProtocolException">The stream ended inside a message, or the message is not valid.</exception>
    public static VrMessage? Read(Stream stream)
    {
        ArgumentNullException.ThrowIfNull(stream);
        Span<byte> header = stackalloc byte[4];
        var got = ReadAtMost(stream, header);
        if (got == 0)
        {
            return null;
        }

        if (got < 4)
        {
            throw new VrProtocolException("the input ended inside a message length");
        }

        var length = BinaryPrimitives.ReadInt32LittleEndian(header);
        if (length < 1 || length > 9 + (MaxSide * MaxSide * 4))
        {
            throw new VrProtocolException(string.Create(CultureInfo.InvariantCulture, $"invalid message length {length}"));
        }

        var body = new byte[length];
        if (ReadAtMost(stream, body) < length)
        {
            throw new VrProtocolException("the input ended inside a message");
        }

        return body[0] switch
        {
            FrameType => ReadFrame(body),
            VisibleType when length == 2 => new VrVisible(body[1] != 0),
            PlaceType when length == 13 => new VrPlace(
                BinaryPrimitives.ReadSingleLittleEndian(body.AsSpan(1)),
                BinaryPrimitives.ReadSingleLittleEndian(body.AsSpan(5)),
                BinaryPrimitives.ReadSingleLittleEndian(body.AsSpan(9))),
            PingType when length == 1 => new VrPing(),
            VisibleType or PlaceType or PingType => throw new VrProtocolException(string.Create(CultureInfo.InvariantCulture, $"message of type {body[0]} has length {length}")),
            _ => throw new VrProtocolException(string.Create(CultureInfo.InvariantCulture, $"unknown message type {body[0]}")),
        };
    }

    private static VrFrame ReadFrame(byte[] body)
    {
        if (body.Length < 9)
        {
            throw new VrProtocolException("frame without a size");
        }

        var width = BinaryPrimitives.ReadInt32LittleEndian(body.AsSpan(1));
        var height = BinaryPrimitives.ReadInt32LittleEndian(body.AsSpan(5));
        if (width is < 1 or > MaxSide || height is < 1 or > MaxSide || body.Length - 9 != width * height * 4)
        {
            throw new VrProtocolException(string.Create(CultureInfo.InvariantCulture, $"frame of {width}x{height} with {body.Length - 9} bytes"));
        }

        return new VrFrame(width, height, body[9..]);
    }

    private static int ReadAtMost(Stream stream, Span<byte> buffer)
    {
        var total = 0;
        while (total < buffer.Length)
        {
            var read = stream.Read(buffer[total..]);
            if (read == 0)
            {
                break;
            }

            total += read;
        }

        return total;
    }
}

/// <summary>What the helper says on its standard output, one line each (add-vr-overlay D6).</summary>
public enum VrReplyKind
{
    /// <summary>The overlay exists; the helper takes messages.</summary>
    Ready,

    /// <summary>The answer to <see cref="VrPing"/>.</summary>
    Pong,

    /// <summary>The helper failed; <see cref="VrReply.Code"/> says why. It exits next.</summary>
    Error,

    /// <summary>SteamVR is closing; the helper exits next with <see cref="VrExitCode.SteamVrClosed"/>.</summary>
    Quit,

    /// <summary>A line the app does not know.</summary>
    Unknown,
}

public readonly record struct VrReply(VrReplyKind Kind, string Code = "")
{
    public const string Ready = "ready";
    public const string Pong = "pong";
    public const string Quit = "quit";

    /// <summary>An error line; the code is one word without spaces, like <c>init-108</c>.</summary>
    public static string Error(string code) => "error " + code.Replace(' ', '-');

    public static VrReply Parse(string? line)
    {
        var text = line?.Trim() ?? string.Empty;
        return text switch
        {
            Ready => new VrReply(VrReplyKind.Ready),
            Pong => new VrReply(VrReplyKind.Pong),
            Quit => new VrReply(VrReplyKind.Quit),
            "error" => new VrReply(VrReplyKind.Error, "unknown"),
            _ when text.StartsWith("error ", StringComparison.Ordinal) => new VrReply(VrReplyKind.Error, text[6..].Trim()),
            _ => new VrReply(VrReplyKind.Unknown, text),
        };
    }
}

/// <summary>Exit codes of the helper (add-vr-overlay D4, D8).</summary>
public static class VrExitCode
{
    /// <summary>The app closed the input; not a failure.</summary>
    public const int InputClosed = 0;

    /// <summary>SteamVR is closing (VREvent_Quit); not a failure, the app waits for the next start of SteamVR.</summary>
    public const int SteamVrClosed = 10;

    /// <summary>VR_Init failed: SteamVR is not running or refused the helper.</summary>
    public const int InitFailed = 20;

    /// <summary>The running SteamVR does not know an interface version of the bundled OpenVR SDK.</summary>
    public const int InterfaceVersion = 21;

    /// <summary>The overlay could not be created or changed.</summary>
    public const int OverlayFailed = 22;

    /// <summary>A message from the app could not be read.</summary>
    public const int ProtocolError = 23;

    /// <summary>openvr_api.dll is missing or could not be loaded.</summary>
    public const int NoLibrary = 24;

    /// <summary>An unexpected exception inside the helper.</summary>
    public const int Crashed = 25;

    /// <summary>The exit codes that are not failures: the helper ended because the app or SteamVR asked it to.</summary>
    public static bool IsFailure(int code) => code is not (InputClosed or SteamVrClosed);
}
