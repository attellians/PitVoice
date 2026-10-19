using System.Windows;
using System.Windows.Controls;
using Wpf.Ui.Controls;

namespace PitVoice.App.Controls;

/// <summary>
/// A hue of the window (refresh-window-visual-style, spec: Section colors): one per section, plus the muted red for
/// repair. Blocks inside a section reuse these hues: microphone and tires — Home (blue), the PTT button — Overlay
/// (lilac), the game and fuel — Commands (peach), the windshield — About (teal), repair — Alert.
/// </summary>
public enum Section
{
    Home,
    Settings,
    Overlay,
    Models,
    Commands,
    About,
    Alert,
}

/// <summary>
/// The icon of a section or a block (design D2): a thin Fluent symbol in the hue of the section on a square tint of the
/// same hue with rounded corners. Without a symbol it shows the first letter of <see cref="Fallback"/>. Sizes: 28 in the
/// menu and list rows, 32 in tiles and card headers, 44 in the status card.
/// </summary>
public sealed class SectionIcon : Border
{
    public static readonly DependencyProperty SectionProperty = DependencyProperty.Register(
        nameof(Section), typeof(Section), typeof(SectionIcon), new PropertyMetadata(Section.Home, (d, _) => ((SectionIcon)d).ApplySection()));

    public static readonly DependencyProperty SymbolProperty = DependencyProperty.Register(
        nameof(Symbol), typeof(SymbolRegular), typeof(SectionIcon), new PropertyMetadata(SymbolRegular.Empty, (d, _) => ((SectionIcon)d).ApplySymbol()));

    public static readonly DependencyProperty SizeProperty = DependencyProperty.Register(
        nameof(Size), typeof(double), typeof(SectionIcon), new PropertyMetadata(28.0, (d, _) => ((SectionIcon)d).ApplySize()));

    public static readonly DependencyProperty FallbackProperty = DependencyProperty.Register(
        nameof(Fallback), typeof(string), typeof(SectionIcon), new PropertyMetadata(string.Empty, (d, _) => ((SectionIcon)d).ApplySymbol()));

    private readonly SymbolIcon _symbol = new() { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
    private readonly System.Windows.Controls.TextBlock _letter = new()
    {
        HorizontalAlignment = HorizontalAlignment.Center,
        VerticalAlignment = VerticalAlignment.Center,
        FontWeight = FontWeights.SemiBold,
        Visibility = Visibility.Collapsed,
    };

    public SectionIcon()
    {
        var grid = new Grid();
        grid.Children.Add(_symbol);
        grid.Children.Add(_letter);
        Child = grid;
        SnapsToDevicePixels = true;
        HorizontalAlignment = HorizontalAlignment.Left;
        VerticalAlignment = VerticalAlignment.Center;
        ApplySection();
        ApplySize();
        ApplySymbol();
    }

    public Section Section
    {
        get => (Section)GetValue(SectionProperty);
        set => SetValue(SectionProperty, value);
    }

    /// <summary>The Fluent symbol; <see cref="SymbolRegular.Empty"/> shows the fallback letter instead.</summary>
    public SymbolRegular Symbol
    {
        get => (SymbolRegular)GetValue(SymbolProperty);
        set => SetValue(SymbolProperty, value);
    }

    /// <summary>Side of the square tint in device-independent pixels.</summary>
    public double Size
    {
        get => (double)GetValue(SizeProperty);
        set => SetValue(SizeProperty, value);
    }

    /// <summary>The title of the block; its first letter stands in for a missing symbol.</summary>
    public string Fallback
    {
        get => (string)GetValue(FallbackProperty);
        set => SetValue(FallbackProperty, value);
    }

    /// <summary>True while the letter is shown instead of a symbol.</summary>
    public bool ShowsLetter => _letter.Visibility == Visibility.Visible;

    /// <summary>The letter shown instead of a symbol, or empty.</summary>
    public string Letter => _letter.Text;

    public static string BrushKey(Section section) => $"Section{section}Brush";

    public static string TintKey(Section section) => $"Section{section}TintBrush";

    private void ApplySection()
    {
        SetResourceReference(BackgroundProperty, TintKey(Section));
        _symbol.SetResourceReference(IconElement.ForegroundProperty, BrushKey(Section));
        _letter.SetResourceReference(System.Windows.Controls.TextBlock.ForegroundProperty, BrushKey(Section));
    }

    private void ApplySize()
    {
        var size = Size;
        Width = size;
        Height = size;
        CornerRadius = new CornerRadius(size >= 40 ? 10 : 8);
        _symbol.FontSize = Math.Round(size * 0.57);
        _letter.FontSize = Math.Round(size * 0.5);
    }

    private void ApplySymbol()
    {
        var symbol = Symbol;
        if (symbol != SymbolRegular.Empty)
        {
            _symbol.Symbol = symbol;
            _symbol.Visibility = Visibility.Visible;
            _letter.Visibility = Visibility.Collapsed;
            _letter.Text = string.Empty;
            return;
        }

        var fallback = Fallback.Trim();
        _letter.Text = fallback.Length == 0 ? "?" : fallback[..1].ToUpperInvariant();
        _symbol.Visibility = Visibility.Collapsed;
        _letter.Visibility = Visibility.Visible;
    }
}

/// <summary>Puts a <see cref="SectionIcon"/> where WPF-UI wants an <see cref="IconElement"/> (the navigation menu).</summary>
public sealed class SectionIconElement : IconElement
{
    public static readonly DependencyProperty SectionProperty = DependencyProperty.Register(
        nameof(Section), typeof(Section), typeof(SectionIconElement), new PropertyMetadata(Section.Home, (d, _) => ((SectionIconElement)d).Update()));

    public static readonly DependencyProperty SymbolProperty = DependencyProperty.Register(
        nameof(Symbol), typeof(SymbolRegular), typeof(SectionIconElement), new PropertyMetadata(SymbolRegular.Empty, (d, _) => ((SectionIconElement)d).Update()));

    public static readonly DependencyProperty FallbackProperty = DependencyProperty.Register(
        nameof(Fallback), typeof(string), typeof(SectionIconElement), new PropertyMetadata(string.Empty, (d, _) => ((SectionIconElement)d).Update()));

    private SectionIcon? _icon;

    public Section Section
    {
        get => (Section)GetValue(SectionProperty);
        set => SetValue(SectionProperty, value);
    }

    public SymbolRegular Symbol
    {
        get => (SymbolRegular)GetValue(SymbolProperty);
        set => SetValue(SymbolProperty, value);
    }

    public string Fallback
    {
        get => (string)GetValue(FallbackProperty);
        set => SetValue(FallbackProperty, value);
    }

    protected override UIElement InitializeChildren()
    {
        _icon = new SectionIcon { Size = 28 };
        Update();
        return _icon;
    }

    private void Update()
    {
        if (_icon is null)
        {
            return;
        }

        _icon.Section = Section;
        _icon.Symbol = Symbol;
        _icon.Fallback = Fallback;
    }
}
