using PitVoice.Core.Vr;

namespace PitVoice.VrOverlay;

/// <summary>
/// Input of <c>--demo</c>: the messages the app would send for a green round badge in the top right of the view, then
/// nothing until Enter is pressed in the console, then the end of the input.
/// </summary>
internal sealed class DemoInput : Stream
{
    private readonly MemoryStream _messages;
    private bool _ended;

    private DemoInput(MemoryStream messages) => _messages = messages;

    public override bool CanRead => true;

    public override bool CanSeek => false;

    public override bool CanWrite => false;

    public override long Length => throw new NotSupportedException();

    public override long Position
    {
        get => throw new NotSupportedException();
        set => throw new NotSupportedException();
    }

    public static DemoInput Create()
    {
        var messages = new MemoryStream();
        VrProtocol.Write(messages, new VrPlace(20f, 14f, 3.6f));
        VrProtocol.Write(messages, Badge(144));
        VrProtocol.Write(messages, new VrVisible(true));
        messages.Position = 0;
        return new DemoInput(messages);
    }

    public override int Read(byte[] buffer, int offset, int count)
    {
        var read = _messages.Read(buffer, offset, count);
        if (read > 0 || _ended)
        {
            return read;
        }

        Console.ReadLine();
        _ended = true;
        return 0;
    }

    public override void Flush()
    {
    }

    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

    public override void SetLength(long value) => throw new NotSupportedException();

    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _messages.Dispose();
        }

        base.Dispose(disposing);
    }

    /// <summary>A green disc with a white ring on a transparent square, like the "done" state.</summary>
    private static VrFrame Badge(int size)
    {
        var rgba = new byte[size * size * 4];
        var center = (size - 1) / 2.0;
        for (var y = 0; y < size; y++)
        {
            for (var x = 0; x < size; x++)
            {
                var r = Math.Sqrt(((x - center) * (x - center)) + ((y - center) * (y - center))) / (size / 2.0);
                var i = ((y * size) + x) * 4;
                if (r > 0.7)
                {
                    continue;
                }

                var ring = r is > 0.3 and < 0.38;
                rgba[i] = ring ? (byte)255 : (byte)0x1F;
                rgba[i + 1] = ring ? (byte)255 : (byte)0x9D;
                rgba[i + 2] = ring ? (byte)255 : (byte)0x55;
                rgba[i + 3] = 230;
            }
        }

        return new VrFrame(size, size, rgba);
    }
}
