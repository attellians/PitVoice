using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using PitVoice.Core.Settings;
using PitVoice.Host;

namespace PitVoice.App.Overlay;

/// <summary>
/// The icon of the indicator and, in kind B, the pill with the word (design D4, D5, mockup of 2026-10-18). Every state
/// change is animated: the color fades, the old glyph leaves turning and shrinking, the new one comes in with a slight
/// overshoot, the pill of kind B changes its width. "Listening" pulses and "recognizing" spins until the state ends; "done"
/// pops, "not understood" and "error" shake. Finished animations are removed, so the idle icon runs none and WPF draws no
/// frames. Sizes are in device-independent units before <see cref="Scale"/>.
/// </summary>
internal sealed class IndicatorBadge : Grid
{
    public static readonly TimeSpan ColorTime = TimeSpan.FromMilliseconds(250);
    public static readonly TimeSpan GlyphOutTime = TimeSpan.FromMilliseconds(180);
    public static readonly TimeSpan GlyphInTime = TimeSpan.FromMilliseconds(300);
    public static readonly TimeSpan PulseTime = TimeSpan.FromMilliseconds(1100);
    public static readonly TimeSpan SpinTime = TimeSpan.FromSeconds(1);
    public static readonly TimeSpan PopTime = TimeSpan.FromMilliseconds(350);
    public static readonly TimeSpan ShakeTime = TimeSpan.FromMilliseconds(400);
    public static readonly TimeSpan WidthTime = TimeSpan.FromMilliseconds(300);

    private const double LabelGap = 6;
    private const double LabelEnd = 16;
    private const double LabelFontSize = 15;
    private const double GlyphSize = 22;

    private static readonly FontFamily LabelFont = new("Segoe UI");

    private readonly SolidColorBrush _fill = new(Palette(IndicatorVisual.Idle).Fill);
    private readonly SolidColorBrush _ink = new(Palette(IndicatorVisual.Idle).Ink);
    private readonly ScaleTransform _scale = new(1, 1);
    private readonly Grid _content = new();
    private readonly TranslateTransform _shake = new();
    private readonly Border _pill = new();
    private readonly TextBlock _label = new();
    private readonly Grid _icon = new() { Width = OverlayLayout.IconSize, Height = OverlayLayout.IconSize };
    private readonly ScaleTransform _pop = new(1, 1, OverlayLayout.IconSize / 2, OverlayLayout.IconSize / 2);
    private readonly Ellipse _ring = new() { StrokeThickness = 2, Opacity = 0 };
    private readonly ScaleTransform _ringScale = new(1, 1, OverlayLayout.IconSize / 2, OverlayLayout.IconSize / 2);
    private readonly GlyphLayer[] _layers = [new(), new()];
    private readonly Rectangle _frame = new()
    {
        StrokeThickness = 1.5,
        StrokeDashArray = [4, 3],
        RadiusX = 8,
        RadiusY = 8,
        Stroke = Brushes.White,
        // Almost transparent, so the frame area takes the mouse in move mode (fully transparent pixels let it through).
        Fill = new SolidColorBrush(Color.FromArgb(1, 0, 0, 0)),
        Visibility = Visibility.Collapsed,
    };

    private readonly Dictionary<(DependencyObject Target, DependencyProperty Property), AnimationClock> _clocks = [];
    private int _front;
    private IndicatorVisual _visual = IndicatorVisual.Idle;
    private IndicatorKind _kind = IndicatorKind.A;
    private double _labelMax;
    private string _word = string.Empty;
    private double _opacity = OverlaySettings.DefaultOpacity / 100.0;
    private bool _stopped;

    public IndicatorBadge()
    {
        LayoutTransform = _scale;
        _ring.Stroke = _fill;
        _ring.RenderTransform = _ringScale;
        _icon.RenderTransform = _pop;
        _icon.Children.Add(_ring);
        _icon.Children.Add(new Ellipse { Fill = _fill });
        foreach (var layer in _layers)
        {
            _icon.Children.Add(layer.Host);
        }

        _label.FontFamily = LabelFont;
        _label.FontSize = LabelFontSize;
        _label.FontWeight = FontWeights.SemiBold;
        _label.Foreground = _ink;
        _label.VerticalAlignment = VerticalAlignment.Center;
        _label.TextWrapping = TextWrapping.NoWrap;
        _label.Opacity = 0;
        _pill.Height = OverlayLayout.IconSize;
        _pill.Width = OverlayLayout.IconSize;
        _pill.CornerRadius = new CornerRadius(OverlayLayout.IconSize / 2);
        _pill.Background = _fill;
        _pill.ClipToBounds = true;
        _pill.Child = _label;

        _content.Margin = new Thickness(OverlayLayout.Padding);
        _content.Height = OverlayLayout.IconSize;
        _content.RenderTransform = _shake;
        _content.Children.Add(_pill);
        _content.Children.Add(_icon);
        Children.Add(_frame);
        Children.Add(_content);

        _layers[0].Set(Glyph(IndicatorVisual.Idle, _ink), visible: true);
        Opacity = _opacity * OverlaySettings.IdleOpacityShare;
        Arrange(IndicatorKind.A, labelMax: 0);
    }

    public IndicatorVisual Visual => _visual;

    /// <summary>Animations still running; zero in the idle state once its transition ended (task 3.4).</summary>
    public int RunningAnimations => _clocks.Count;

    /// <summary>Running animations that never end on their own (pulse, spin).</summary>
    public int RunningEndless => _clocks.Values.Count(c => c.Timeline.RepeatBehavior == RepeatBehavior.Forever);

    /// <summary>Width of the content before scaling.</summary>
    public double ContentWidth => Width;

    /// <summary>Room the longest word takes to the right of the icon, before scaling; 0 for kind A.</summary>
    public double WordRoom => WordRoomFor(_kind, _labelMax);

    public static double WordRoomFor(IndicatorKind kind, double labelMax) =>
        kind == IndicatorKind.B && labelMax > 0 ? LabelGap + labelMax + LabelEnd : 0;

    /// <summary>Size around nothing in particular: the window keeps the icon center in place (design D5).</summary>
    public double Scale
    {
        get => _scale.ScaleX;
        set
        {
            _scale.ScaleX = value;
            _scale.ScaleY = value;
        }
    }

    public bool ShowFrame
    {
        get => _frame.Visibility == Visibility.Visible;
        set => _frame.Visibility = value ? Visibility.Visible : Visibility.Collapsed;
    }

    /// <summary>Width of a word in the label font, in device-independent units.</summary>
    public static double MeasureWord(string word, double pixelsPerDip)
    {
        var text = new FormattedText(
            word,
            CultureInfo.InvariantCulture,
            FlowDirection.LeftToRight,
            new Typeface(LabelFont, FontStyles.Normal, FontWeights.SemiBold, FontStretches.Normal),
            LabelFontSize,
            Brushes.White,
            pixelsPerDip);
        return Math.Ceiling(text.WidthIncludingTrailingWhitespace);
    }

    /// <summary>Kind and room for the longest word; the window size follows. The icon is always at the left.</summary>
    public void Arrange(IndicatorKind kind, double labelMax)
    {
        _kind = kind;
        _labelMax = kind == IndicatorKind.B ? labelMax : 0;
        _content.HorizontalAlignment = HorizontalAlignment.Left;
        _pill.HorizontalAlignment = HorizontalAlignment.Left;
        _icon.HorizontalAlignment = HorizontalAlignment.Left;
        _label.HorizontalAlignment = HorizontalAlignment.Left;
        _label.Margin = new Thickness(OverlayLayout.IconSize + LabelGap, 0, LabelEnd, 0);
        _pill.Visibility = kind == IndicatorKind.B ? Visibility.Visible : Visibility.Collapsed;
        Width = (OverlayLayout.Padding * 2) + OverlayLayout.IconSize + WordRoom;
        Height = (OverlayLayout.Padding * 2) + OverlayLayout.IconSize;
        Stop(_pill, WidthProperty);
        _pill.Width = PillWidth(_visual == IndicatorVisual.Idle ? string.Empty : _word);
    }

    /// <summary>Opacity of the active states in 0..1; the idle icon gets <see cref="OverlaySettings.IdleOpacityShare"/> of it.</summary>
    public void SetOpacity(double opacity, bool animate)
    {
        _opacity = opacity;
        To(this, OpacityProperty, TargetOpacity(_visual), ColorTime, animate);
    }

    /// <summary>Shows a state. <paramref name="animate"/> false jumps to it (the window was hidden).</summary>
    public void Show(IndicatorVisual visual, string word, bool animate)
    {
        var previous = _visual;
        var changed = _stopped || previous != visual || word != _word;
        _stopped = false;
        _visual = visual;
        _word = word;
        if (!changed)
        {
            return;
        }

        var palette = Palette(visual);
        ToColor(_fill, palette.Fill, animate);
        ToColor(_ink, palette.Ink, animate);
        To(this, OpacityProperty, TargetOpacity(visual), ColorTime, animate);

        // Endless animations belong to their state only.
        StopRing();
        if (previous != visual || !animate)
        {
            SwapGlyph(visual, animate);
        }

        if (_kind == IndicatorKind.B)
        {
            _label.Text = visual == IndicatorVisual.Idle ? _label.Text : word;
            To(_pill, WidthProperty, PillWidth(visual == IndicatorVisual.Idle ? string.Empty : word), WidthTime, animate, new CubicEase { EasingMode = EasingMode.EaseInOut });
            To(_label, OpacityProperty, visual == IndicatorVisual.Idle ? 0.0 : 1.0, WidthTime, animate);
        }

        if (!animate)
        {
            Stop(_pop, ScaleTransform.ScaleXProperty);
            Stop(_pop, ScaleTransform.ScaleYProperty);
            Stop(_shake, TranslateTransform.XProperty);
        }

        switch (visual)
        {
            case IndicatorVisual.Listening:
                Pulse();
                break;
            case IndicatorVisual.Done when animate:
                Pop();
                break;
            case IndicatorVisual.NotUnderstood or IndicatorVisual.Error when animate:
                Shake();
                break;
        }
    }

    /// <summary>Ends every animation at its final value (the window is hidden).</summary>
    public void StopAll()
    {
        foreach (var key in _clocks.Keys.ToArray())
        {
            Stop(key.Target, key.Property);
        }

        _ring.Opacity = 0;
        _ringScale.ScaleX = 1;
        _ringScale.ScaleY = 1;
        _stopped = true;
    }

    private double TargetOpacity(IndicatorVisual visual) =>
        visual == IndicatorVisual.Idle ? _opacity * OverlaySettings.IdleOpacityShare : _opacity;

    private double PillWidth(string word) =>
        word.Length == 0 || _kind != IndicatorKind.B
            ? OverlayLayout.IconSize
            : Math.Min(OverlayLayout.IconSize + LabelGap + MeasureWord(word, 1) + LabelEnd, OverlayLayout.IconSize + LabelGap + _labelMax + LabelEnd);

    private void SwapGlyph(IndicatorVisual visual, bool animate)
    {
        var old = _layers[_front];
        _front = 1 - _front;
        var next = _layers[_front];
        StopSpin(old);
        StopSpin(next);
        next.Set(Glyph(visual, _ink), visible: true);
        if (visual == IndicatorVisual.Recognizing)
        {
            Spin(next);
        }

        if (!animate)
        {
            old.Set(null, visible: false);
            foreach (var layer in _layers)
            {
                Stop(layer.Host, OpacityProperty);
                Stop(layer.Scale, ScaleTransform.ScaleXProperty);
                Stop(layer.Scale, ScaleTransform.ScaleYProperty);
                Stop(layer.Rotate, RotateTransform.AngleProperty);
            }

            next.Host.Opacity = 1;
            next.Scale.ScaleX = next.Scale.ScaleY = 1;
            next.Rotate.Angle = 0;
            return;
        }

        var easeOut = new CubicEase { EasingMode = EasingMode.EaseIn };
        To(old.Host, OpacityProperty, 0.0, GlyphOutTime, true, easeOut);
        To(old.Scale, ScaleTransform.ScaleXProperty, 0.3, GlyphOutTime, true, easeOut);
        To(old.Scale, ScaleTransform.ScaleYProperty, 0.3, GlyphOutTime, true, easeOut);
        To(old.Rotate, RotateTransform.AngleProperty, -60.0, GlyphOutTime, true, easeOut);

        next.Host.Opacity = 0;
        next.Scale.ScaleX = next.Scale.ScaleY = 0.3;
        next.Rotate.Angle = 60;
        var back = new BackEase { EasingMode = EasingMode.EaseOut, Amplitude = 0.4 };
        To(next.Host, OpacityProperty, 1.0, GlyphOutTime, true);
        To(next.Scale, ScaleTransform.ScaleXProperty, 1.0, GlyphInTime, true, back);
        To(next.Scale, ScaleTransform.ScaleYProperty, 1.0, GlyphInTime, true, back);
        To(next.Rotate, RotateTransform.AngleProperty, 0.0, GlyphInTime, true, back);
    }

    private void Pulse()
    {
        Begin(_ringScale, ScaleTransform.ScaleXProperty, new DoubleAnimation(1, 1.55, PulseTime) { RepeatBehavior = RepeatBehavior.Forever }, 1.0);
        Begin(_ringScale, ScaleTransform.ScaleYProperty, new DoubleAnimation(1, 1.55, PulseTime) { RepeatBehavior = RepeatBehavior.Forever }, 1.0);
        Begin(_ring, OpacityProperty, new DoubleAnimation(0.7, 0, PulseTime) { RepeatBehavior = RepeatBehavior.Forever }, 0.0);
    }

    private void StopRing()
    {
        Stop(_ringScale, ScaleTransform.ScaleXProperty);
        Stop(_ringScale, ScaleTransform.ScaleYProperty);
        Stop(_ring, OpacityProperty);
    }

    private void Spin(GlyphLayer layer)
    {
        if (layer.Spinner is { } spinner)
        {
            Begin(spinner, RotateTransform.AngleProperty, new DoubleAnimation(0, 360, SpinTime) { RepeatBehavior = RepeatBehavior.Forever }, 0.0);
        }
    }

    private void StopSpin(GlyphLayer layer)
    {
        if (layer.Spinner is { } spinner)
        {
            Stop(spinner, RotateTransform.AngleProperty);
        }
    }

    private void Pop()
    {
        foreach (var property in new[] { ScaleTransform.ScaleXProperty, ScaleTransform.ScaleYProperty })
        {
            var pop = new DoubleAnimationUsingKeyFrames { Duration = PopTime };
            pop.KeyFrames.Add(new EasingDoubleKeyFrame(1.14, KeyTime.FromPercent(0.4), new QuadraticEase { EasingMode = EasingMode.EaseOut }));
            pop.KeyFrames.Add(new EasingDoubleKeyFrame(1, KeyTime.FromPercent(1), new QuadraticEase { EasingMode = EasingMode.EaseIn }));
            Begin(_pop, property, pop, 1.0);
        }
    }

    private void Shake()
    {
        var shake = new DoubleAnimationUsingKeyFrames { Duration = ShakeTime };
        double[] steps = [-5, 5, -4, 4, -2, 0];
        for (var i = 0; i < steps.Length; i++)
        {
            shake.KeyFrames.Add(new LinearDoubleKeyFrame(steps[i], KeyTime.FromPercent((i + 1) / (double)steps.Length)));
        }

        Begin(_shake, TranslateTransform.XProperty, shake, 0.0);
    }

    private void ToColor(SolidColorBrush brush, Color color, bool animate)
    {
        if (!animate)
        {
            Stop(brush, SolidColorBrush.ColorProperty);
            brush.Color = color;
            return;
        }

        Begin(brush, SolidColorBrush.ColorProperty, new ColorAnimation(color, ColorTime), color);
    }

    private void To(DependencyObject target, DependencyProperty property, double value, TimeSpan time, bool animate, IEasingFunction? easing = null, Action? completed = null)
    {
        if (!animate)
        {
            Stop(target, property);
            target.SetValue(property, value);
            completed?.Invoke();
            return;
        }

        Begin(target, property, new DoubleAnimation(value, time) { EasingFunction = easing }, value, completed);
    }

    /// <summary>
    /// Starts the animation from the current value (so a press interrupts a transition at once) and, when it ends, sets
    /// the final value and removes the clock.
    /// </summary>
    private void Begin(DependencyObject target, DependencyProperty property, AnimationTimeline animation, object final, Action? completed = null)
    {
        var key = (target, property);
        var clock = animation.CreateClock();
        clock.Completed += (_, _) =>
        {
            if (_clocks.TryGetValue(key, out var current) && current == clock)
            {
                Stop(target, property);
                target.SetValue(property, final);
                completed?.Invoke();
            }
        };
        _clocks[key] = clock;
        ((IAnimatable)target).ApplyAnimationClock(property, clock, HandoffBehavior.SnapshotAndReplace);
    }

    /// <summary>Removes the animation of the property and keeps the value it showed.</summary>
    private void Stop(DependencyObject target, DependencyProperty property)
    {
        if (!_clocks.Remove((target, property), out _))
        {
            return;
        }

        var shown = target.GetValue(property);
        ((IAnimatable)target).ApplyAnimationClock(property, null);
        target.SetValue(property, shown);
    }

    /// <summary>Fill and glyph colors of the mockup.</summary>
    internal static (Color Fill, Color Ink) Palette(IndicatorVisual visual) => visual switch
    {
        IndicatorVisual.Listening => (Rgb(0x2F7BF6), Colors.White),
        IndicatorVisual.Recognizing => (Rgb(0x1E3A8A), Colors.White),
        IndicatorVisual.Done => (Rgb(0x1F9D55), Colors.White),
        IndicatorVisual.NotUnderstood => (Rgb(0xF2B01E), Rgb(0x1F1F1F)),
        IndicatorVisual.NoGame => (Rgb(0x6B7280), Colors.White),
        IndicatorVisual.Error => (Rgb(0xE5484D), Colors.White),
        _ => (Rgb(0x3A3F47), Colors.White),
    };

    private static Color Rgb(int rgb) => Color.FromRgb((byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb);

    /// <summary>Glyphs on a 24×24 grid, drawn with round strokes.</summary>
    private static (UIElement Element, RotateTransform? Spinner) Glyph(IndicatorVisual visual, Brush ink)
    {
        var data = visual switch
        {
            IndicatorVisual.Recognizing => "M12,4 A8,8 0 1 1 4,12",
            IndicatorVisual.Done => "M5.5,12.5 L10,17 L18.5,7.5",
            IndicatorVisual.NotUnderstood => "M9,9 A3,3 0 1 1 13.6,11.5 C12.6,12.1 12,12.8 12,14.2 M12,18 L12,18.2",
            IndicatorVisual.NoGame => "M9,3 L9,7 M15,3 L15,7 M7,7 L17,7 L17,10 A5,5 0 0 1 7,10 Z M12,15 L12,21",
            IndicatorVisual.Error => "M7,7 L17,17 M17,7 L7,17",
            _ => "M12,3 A3,3 0 0 1 15,6 L15,11 A3,3 0 0 1 9,11 L9,6 A3,3 0 0 1 12,3 Z M6.5,11 A5.5,5.5 0 0 0 17.5,11 M12,16.5 L12,20.5 M9,20.5 L15,20.5",
        };
        var path = new Path
        {
            Data = Geometry.Parse(data),
            Stroke = ink,
            StrokeThickness = visual == IndicatorVisual.NotUnderstood ? 2.4 : 2.2,
            StrokeStartLineCap = PenLineCap.Round,
            StrokeEndLineCap = PenLineCap.Round,
            StrokeLineJoin = PenLineJoin.Round,
            Width = 24,
            Height = 24,
            Stretch = Stretch.None,
        };
        RotateTransform? spinner = null;
        if (visual == IndicatorVisual.Recognizing)
        {
            spinner = new RotateTransform(0, 12, 12);
            path.RenderTransform = spinner;
        }

        return (new Viewbox { Width = GlyphSize, Height = GlyphSize, Child = path }, spinner);
    }

    /// <summary>One of the two glyph slots: the old glyph leaves from one while the new one comes into the other.</summary>
    private sealed class GlyphLayer
    {
        public GlyphLayer()
        {
            Host.RenderTransformOrigin = new Point(0.5, 0.5);
            Host.RenderTransform = new TransformGroup { Children = { Scale, Rotate } };
            Host.HorizontalAlignment = HorizontalAlignment.Center;
            Host.VerticalAlignment = VerticalAlignment.Center;
            Host.IsHitTestVisible = false;
        }

        public ContentControl Host { get; } = new() { Focusable = false };

        public ScaleTransform Scale { get; } = new(1, 1);

        public RotateTransform Rotate { get; } = new(0);

        public RotateTransform? Spinner { get; private set; }

        public void Set((UIElement Element, RotateTransform? Spinner)? glyph, bool visible)
        {
            Host.Content = glyph?.Element;
            Spinner = glyph?.Spinner;
            Host.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
        }
    }
}
