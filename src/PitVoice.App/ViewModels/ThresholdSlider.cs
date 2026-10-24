using System.Globalization;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;

namespace PitVoice.App.ViewModels;

/// <summary>
/// The confidence threshold slider of the settings (design D8 of simplify-window-screens; spec: Confidence threshold
/// slider): 0.3–0.9 in steps of 0.05 with the number on the right. A drag saves once, after <see cref="SaveDelay"/>
/// without movement or when the mouse is released. A file value outside the slider (but within 0–1) is shown as the
/// number it is and is not written back until the user moves the slider.
/// </summary>
internal sealed partial class ThresholdSlider : ObservableObject
{
    public const double Minimum = 0.3;

    public const double Maximum = 0.9;

    public const double Step = 0.05;

    /// <summary>Delay before a move is saved, so dragging writes the file once.</summary>
    public static readonly TimeSpan SaveDelay = TimeSpan.FromMilliseconds(300);

    private readonly Action<double> _save;
    private readonly DispatcherTimer _timer;
    private double? _pending;
    private bool _loading;

    /// <param name="save">Saves the threshold; called once per move.</param>
    public ThresholdSlider(Action<double> save)
    {
        _save = save;
        _timer = new DispatcherTimer { Interval = SaveDelay };
        _timer.Tick += (_, _) => Commit();
    }

    /// <summary>Position of the slider.</summary>
    [ObservableProperty]
    public partial double Value { get; set; } = Minimum;

    /// <summary>The number on the right: "0.75", or the file value as it is ("0.2").</summary>
    [ObservableProperty]
    public partial string Text { get; set; } = string.Empty;

    /// <summary>Shows the value of the settings file without saving it.</summary>
    public void Load(double threshold)
    {
        _loading = true;
        try
        {
            _timer.Stop();
            _pending = null;
            Value = Math.Clamp(threshold, Minimum, Maximum);
            Text = Format(threshold);
        }
        finally
        {
            _loading = false;
        }
    }

    /// <summary>Saves a pending move now: the mouse was released.</summary>
    public void Commit()
    {
        _timer.Stop();
        if (_pending is { } value)
        {
            _pending = null;
            _save(value);
        }
    }

    partial void OnValueChanged(double value)
    {
        if (_loading)
        {
            return;
        }

        var snapped = Math.Round(Math.Round(value / Step) * Step, 2);
        Text = Format(snapped);
        _pending = snapped;
        _timer.Stop();
        _timer.Start();
    }

    private static string Format(double value) => value.ToString("0.##", CultureInfo.InvariantCulture);
}
