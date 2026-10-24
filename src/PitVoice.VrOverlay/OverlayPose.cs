namespace PitVoice.VrOverlay;

/// <summary>
/// The overlay relative to the head (add-vr-overlay D7): <see cref="Distance"/> in front of the eyes in the direction of
/// the place, turned to face the eyes. OpenVR axes: +X right, +Y up, −Z forward.
/// </summary>
internal static class OverlayPose
{
    /// <summary>Distance from the eyes in meters.</summary>
    public const float Distance = 1f;

    /// <summary>
    /// The HmdMatrix34_t (3 rows of 4, row-major) from the head to the overlay: rotation <c>Ry(−yaw)·Rx(pitch)</c> and the
    /// point <see cref="Distance"/> away in that direction.
    /// </summary>
    public static float[] Matrix(float yawDegrees, float pitchDegrees, float distance = Distance)
    {
        var yaw = -yawDegrees * MathF.PI / 180f;
        var pitch = pitchDegrees * MathF.PI / 180f;
        var (sy, cy) = MathF.SinCos(yaw);
        var (sp, cp) = MathF.SinCos(pitch);

        // Ry(yaw) · Rx(pitch)
        float[,] r =
        {
            { cy, sy * sp, sy * cp },
            { 0, cp, -sp },
            { -sy, cy * sp, cy * cp },
        };

        // R · (0, 0, −distance)
        var tx = -r[0, 2] * distance;
        var ty = -r[1, 2] * distance;
        var tz = -r[2, 2] * distance;
        return
        [
            r[0, 0], r[0, 1], r[0, 2], tx,
            r[1, 0], r[1, 1], r[1, 2], ty,
            r[2, 0], r[2, 1], r[2, 2], tz,
        ];
    }

    /// <summary>Width in meters of a flat overlay that takes <paramref name="widthDegrees"/> at <paramref name="distance"/>.</summary>
    public static float WidthInMeters(float widthDegrees, float distance = Distance) =>
        2 * distance * MathF.Tan(widthDegrees * MathF.PI / 360f);
}
