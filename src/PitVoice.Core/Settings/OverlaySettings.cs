namespace PitVoice.Core.Settings;

/// <summary>The indicator above the game (overlay-indicator spec, design D8): the <c>overlay</c> object of settings.json.</summary>
public sealed record OverlaySettings
{
    public const int MinScale = 75;
    public const int MaxScale = 200;
    public const int DefaultScale = 100;
    public const int MinOpacity = 30;
    public const int MaxOpacity = 100;
    public const int DefaultOpacity = 90;

    /// <summary>Share of <see cref="Opacity"/> the idle icon is drawn with (the dim icon of the mockup).</summary>
    public const double IdleOpacityShare = 0.45;

    public static OverlaySettings Default { get; } = new();

    public bool Enabled { get; init; } = true;

    public IndicatorKind Kind { get; init; } = IndicatorKind.A;

    /// <summary>Size in percent, <see cref="MinScale"/>..<see cref="MaxScale"/>.</summary>
    public int Scale { get; init; } = DefaultScale;

    /// <summary>Opacity in percent, <see cref="MinOpacity"/>..<see cref="MaxOpacity"/>.</summary>
    public int Opacity { get; init; } = DefaultOpacity;

    /// <summary>Center of the icon in physical pixels of the virtual screen; null is the default place (design D6).</summary>
    public ScreenPoint? Position { get; init; }
}

/// <summary>A: the round icon; B: the icon and a short word of the state.</summary>
public enum IndicatorKind
{
    A,
    B,
}

/// <summary>A point in physical pixels of the virtual screen.</summary>
public readonly record struct ScreenPoint(int X, int Y);
