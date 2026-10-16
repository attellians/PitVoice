using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using PitVoice.Host;

namespace PitVoice.App.Tray;

/// <summary>
/// The PitVoice sign (design decision 8): a microphone on a rounded square, drawn as vectors so it stays sharp from 16 to
/// 256 px. The tray adds a state mark in the lower right corner: none for ready, grey for no game, orange for no model,
/// red for an error. <c>tools/icon-gen</c> renders the same drawing into the <c>.ico</c> of the exe and the window.
/// </summary>
public static class IconArt
{
    /// <summary>Size of the drawing's coordinate space.</summary>
    public const double Size = 32;

    private static readonly Brush Background = Frozen(new SolidColorBrush(Color.FromRgb(0x25, 0x63, 0xEB)));
    private static readonly Brush Glyph = Frozen(new SolidColorBrush(Colors.White));
    private static readonly Brush Outline = Frozen(new SolidColorBrush(Color.FromRgb(0x10, 0x10, 0x10)));

    /// <summary>The sign with the mark of <paramref name="state"/> (no mark for <see cref="HostState.Ready"/>).</summary>
    public static DrawingGroup Draw(HostState state = HostState.Ready)
    {
        var group = new DrawingGroup();
        group.Children.Add(new GeometryDrawing(Background, null, new RectangleGeometry(new Rect(0, 0, Size, Size), 7, 7)));

        var stroke = new Pen(Glyph, 2.4) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };
        group.Children.Add(new GeometryDrawing(Glyph, null, new RectangleGeometry(new Rect(12, 4.5, 8, 14.5), 4, 4)));
        group.Children.Add(new GeometryDrawing(null, stroke, Geometry.Parse("M 8,13.5 A 8,8 0 0 0 24,13.5")));
        group.Children.Add(new GeometryDrawing(null, stroke, Geometry.Parse("M 16,21.5 L 16,26")));
        group.Children.Add(new GeometryDrawing(null, stroke, Geometry.Parse("M 11.5,26.5 L 20.5,26.5")));

        if (MarkColor(state) is { } color)
        {
            var mark = Frozen(new SolidColorBrush(color));
            group.Children.Add(new GeometryDrawing(mark, new Pen(Outline, 1.5), new EllipseGeometry(new Point(25.5, 25.5), 6, 6)));
        }

        group.Freeze();
        return group;
    }

    /// <summary>The sign as a bitmap of <paramref name="pixels"/> × <paramref name="pixels"/>.</summary>
    public static BitmapSource Render(HostState state, int pixels)
    {
        var visual = new DrawingVisual();
        using (var context = visual.RenderOpen())
        {
            context.PushTransform(new ScaleTransform(pixels / Size, pixels / Size));
            context.DrawDrawing(Draw(state));
            context.Pop();
        }

        var bitmap = new RenderTargetBitmap(pixels, pixels, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(visual);
        bitmap.Freeze();
        return bitmap;
    }

    /// <summary>Writes an .ico with one PNG frame per size (Vista and later read PNG frames).</summary>
    public static void WriteIco(Stream output, HostState state, IReadOnlyList<int> sizes)
    {
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(sizes);
        var frames = sizes.Select(size => (Size: size, Png: Png(state, size))).ToArray();
        using var writer = new BinaryWriter(output, System.Text.Encoding.UTF8, leaveOpen: true);
        writer.Write((ushort)0); // reserved
        writer.Write((ushort)1); // icon
        writer.Write((ushort)frames.Length);
        var offset = 6 + (16 * frames.Length);
        foreach (var (size, png) in frames)
        {
            writer.Write((byte)(size >= 256 ? 0 : size));
            writer.Write((byte)(size >= 256 ? 0 : size));
            writer.Write((byte)0); // no palette
            writer.Write((byte)0);
            writer.Write((ushort)1); // planes
            writer.Write((ushort)32); // bits per pixel
            writer.Write(png.Length);
            writer.Write(offset);
            offset += png.Length;
        }

        foreach (var (_, png) in frames)
        {
            writer.Write(png);
        }
    }

    private static byte[] Png(HostState state, int size)
    {
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(Render(state, size)));
        using var stream = new MemoryStream();
        encoder.Save(stream);
        return stream.ToArray();
    }

    public static Color? MarkColor(HostState state) => state switch
    {
        HostState.NoGame => Color.FromRgb(0x9C, 0xA3, 0xAF),
        HostState.NoModel => Color.FromRgb(0xF5, 0x9E, 0x0B),
        HostState.Error => Color.FromRgb(0xE5, 0x48, 0x4D),
        _ => null,
    };

    private static T Frozen<T>(T freezable)
        where T : Freezable
    {
        freezable.Freeze();
        return freezable;
    }
}
