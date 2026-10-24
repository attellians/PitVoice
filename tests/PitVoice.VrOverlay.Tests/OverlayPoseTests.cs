using Xunit;

namespace PitVoice.VrOverlay.Tests;

/// <summary>The overlay relative to the head (add-vr-overlay D7).</summary>
public sealed class OverlayPoseTests
{
    [Fact]
    public void Straight_ahead_is_one_meter_forward_facing_the_eyes()
    {
        var m = OverlayPose.Matrix(0, 0);

        Assert.Equal([1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1, -1], m.Select(v => MathF.Round(v, 5)));
    }

    [Theory]
    [InlineData(20, 0)]
    [InlineData(-25, 0)]
    [InlineData(0, 18)]
    [InlineData(23, -15)]
    public void Overlay_stands_in_the_direction_of_the_place_and_faces_the_eyes(float yaw, float pitch)
    {
        var m = OverlayPose.Matrix(yaw, pitch);
        var (x, y, z) = (m[3], m[7], m[11]);

        Assert.Equal(1, MathF.Sqrt((x * x) + (y * y) + (z * z)), 4);
        Assert.Equal(yaw, MathF.Atan2(x, -z) * 180 / MathF.PI, 3);
        Assert.Equal(pitch, MathF.Asin(y) * 180 / MathF.PI, 3);

        // The normal of the overlay (its +Z axis, the third column) points back at the eyes.
        Assert.Equal(-x, m[2], 4);
        Assert.Equal(-y, m[6], 4);
        Assert.Equal(-z, m[10], 4);

        // Its X axis stays level: no roll.
        Assert.Equal(0, m[4], 4);
    }

    [Fact]
    public void Width_in_meters_covers_the_angle()
    {
        Assert.Equal(2 * MathF.Tan(1.25f * MathF.PI / 180), OverlayPose.WidthInMeters(2.5f), 6);
        Assert.Equal(0.0436, OverlayPose.WidthInMeters(2.5f), 3);
    }
}
