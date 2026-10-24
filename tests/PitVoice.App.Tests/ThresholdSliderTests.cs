using PitVoice.App.ViewModels;
using Xunit;

namespace PitVoice.App.Tests;

/// <summary>The confidence threshold slider (design D8 of simplify-window-screens; spec: Confidence threshold slider).</summary>
public sealed class ThresholdSliderTests
{
    private static readonly TimeSpan AfterDelay = ThresholdSlider.SaveDelay + TimeSpan.FromMilliseconds(200);

    [Fact]
    public void Moving_to_075_saves_075_once_after_a_series_of_steps() => Sta.Run(() =>
    {
        var saved = new List<double>();
        var slider = new ThresholdSlider(saved.Add);
        slider.Load(0.6);

        foreach (var step in new[] { 0.65, 0.7, 0.75000000001 })
        {
            slider.Value = step;
        }

        Assert.Equal("0.75", slider.Text);
        Assert.Empty(saved);

        Sta.Pump(AfterDelay);

        Assert.Equal([0.75], saved);
    });

    [Fact]
    public void Releasing_the_mouse_saves_at_once() => Sta.Run(() =>
    {
        var saved = new List<double>();
        var slider = new ThresholdSlider(saved.Add);
        slider.Load(0.6);

        slider.Value = 0.7;
        slider.Commit();

        Assert.Equal([0.7], saved);
        Sta.Pump(AfterDelay);
        Assert.Equal([0.7], saved);
    });

    [Fact]
    public void Default_value_shows_06() => Sta.Run(() =>
    {
        var slider = new ThresholdSlider(_ => { });
        slider.Load(0.6);

        Assert.Equal(0.6, slider.Value, 9);
        Assert.Equal("0.6", slider.Text);
    });

    [Fact]
    public void File_value_outside_the_slider_is_shown_as_is_and_not_written() => Sta.Run(() =>
    {
        var saved = new List<double>();
        var slider = new ThresholdSlider(saved.Add);

        slider.Load(0.2);
        Sta.Pump(AfterDelay);

        Assert.Equal("0.2", slider.Text);
        Assert.Equal(ThresholdSlider.Minimum, slider.Value, 9);
        Assert.Empty(saved);
        slider.Commit();
        Assert.Empty(saved);
    });

    /// <summary>Spec: Settings file not writable — the value is still handed to the host, which keeps it until exit.</summary>
    [Fact]
    public void A_failed_save_keeps_the_moved_value() => Sta.Run(() =>
    {
        var attempts = new List<double>();
        var slider = new ThresholdSlider(value =>
        {
            attempts.Add(value);
            // The settings model shows the save error; the slider stays where the user put it.
        });
        slider.Load(0.6);

        slider.Value = 0.7;
        slider.Commit();

        Assert.Equal([0.7], attempts);
        Assert.Equal("0.7", slider.Text);
        Assert.Equal(0.7, slider.Value, 9);
    });
}
