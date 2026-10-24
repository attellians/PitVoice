using PitVoice.Core.Vr;
using Xunit;

namespace PitVoice.VrOverlay.Tests;

/// <summary>Messages between the app and the helper (add-vr-overlay D6).</summary>
public sealed class VrProtocolTests
{
    [Fact]
    public void Every_message_round_trips()
    {
        var pixels = Enumerable.Range(0, 3 * 2 * 4).Select(i => (byte)i).ToArray();
        VrMessage[] messages = [new VrFrame(3, 2, pixels), new VrVisible(true), new VrVisible(false), new VrPlace(-12.5f, 7.25f, 3.1f), new VrPing()];
        using var stream = new MemoryStream();
        foreach (var message in messages)
        {
            VrProtocol.Write(stream, message);
        }

        stream.Position = 0;
        var frame = Assert.IsType<VrFrame>(VrProtocol.Read(stream));
        Assert.Equal((3, 2), (frame.Width, frame.Height));
        Assert.Equal(pixels, frame.Rgba);
        Assert.Equal(new VrVisible(true), VrProtocol.Read(stream));
        Assert.Equal(new VrVisible(false), VrProtocol.Read(stream));
        Assert.Equal(new VrPlace(-12.5f, 7.25f, 3.1f), VrProtocol.Read(stream));
        Assert.IsType<VrPing>(VrProtocol.Read(stream));
        Assert.Null(VrProtocol.Read(stream));
    }

    [Fact]
    public void Closed_stream_between_messages_is_the_end()
    {
        using var stream = new MemoryStream();

        Assert.Null(VrProtocol.Read(stream));
    }

    [Theory]
    [InlineData(2)] // inside the length
    [InlineData(7)] // inside the body
    public void Cut_message_is_an_error(int keep)
    {
        var bytes = Bytes(new VrPlace(1, 2, 3));
        using var stream = new MemoryStream(bytes[..keep]);

        Assert.Throws<VrProtocolException>(() => VrProtocol.Read(stream));
    }

    [Fact]
    public void Unknown_type_is_an_error()
    {
        using var stream = new MemoryStream([1, 0, 0, 0, 99]);

        var error = Assert.Throws<VrProtocolException>(() => VrProtocol.Read(stream));
        Assert.Contains("99", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Frame_with_a_wrong_length_is_an_error()
    {
        var bytes = Bytes(new VrFrame(2, 2, new byte[16]));
        bytes[5] = 3; // width 3: 12 pixels would need 48 bytes

        using var stream = new MemoryStream(bytes);

        Assert.Throws<VrProtocolException>(() => VrProtocol.Read(stream));
        Assert.Throws<ArgumentException>(() => VrProtocol.Write(Stream.Null, new VrFrame(2, 2, new byte[15])));
    }

    [Fact]
    public void Huge_length_is_an_error_before_reading_it()
    {
        using var stream = new MemoryStream([0xFF, 0xFF, 0xFF, 0x7F, 1]);

        Assert.Throws<VrProtocolException>(() => VrProtocol.Read(stream));
    }

    [Theory]
    [InlineData("ready", VrReplyKind.Ready, "")]
    [InlineData("pong\r", VrReplyKind.Pong, "")]
    [InlineData("quit", VrReplyKind.Quit, "")]
    [InlineData("error init-108", VrReplyKind.Error, "init-108")]
    [InlineData("error", VrReplyKind.Error, "unknown")]
    [InlineData("hello", VrReplyKind.Unknown, "hello")]
    [InlineData(null, VrReplyKind.Unknown, "")]
    public void Reply_lines_are_parsed(string? line, VrReplyKind kind, string code) =>
        Assert.Equal(new VrReply(kind, code), VrReply.Parse(line));

    [Fact]
    public void Error_code_is_one_word() => Assert.Equal("error no-library", VrReply.Error("no library"));

    [Fact]
    public void Only_the_app_and_SteamVR_end_the_helper_without_a_failure()
    {
        Assert.False(VrExitCode.IsFailure(VrExitCode.InputClosed));
        Assert.False(VrExitCode.IsFailure(VrExitCode.SteamVrClosed));
        Assert.True(VrExitCode.IsFailure(VrExitCode.InitFailed));
        Assert.True(VrExitCode.IsFailure(VrExitCode.Crashed));
        Assert.True(VrExitCode.IsFailure(-1073741819)); // access violation
    }

    private static byte[] Bytes(VrMessage message)
    {
        using var stream = new MemoryStream();
        VrProtocol.Write(stream, message);
        return stream.ToArray();
    }
}
