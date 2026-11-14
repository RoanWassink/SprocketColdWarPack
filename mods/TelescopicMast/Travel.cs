namespace SprocketTelescopicMast;

public static class Travel
{
    public static float ScaleAxis(float value) => float.IsFinite(value) ? Math.Clamp(MathF.Abs(value), .1f, 5f) : 1f;
    public static float WorldExtension(float height, float scaleY) => height * ScaleAxis(scaleY);
    public static float InsertPosition(float height, float scaleY) => (1.25f + Math.Clamp(height,0,1)) * ScaleAxis(scaleY);
    public static float VolumeFactor(float x, float y, float z) => ScaleAxis(x) * ScaleAxis(y) * ScaleAxis(z);
    public static float Step(float height, int direction, float speed, float dt, float stroke)
    {
        if (!float.IsFinite(stroke) || stroke <= 0) stroke = 1;
        stroke = Math.Clamp(stroke, .05f, 1f); // Keep a minimum overlap in the cylinder.
        if (!float.IsFinite(height)) height = 0;
        height = Math.Clamp(height, 0, stroke);
        if (!float.IsFinite(speed) || !float.IsFinite(dt) || speed <= 0 || dt <= 0) return height;
        return Math.Clamp(height + Math.Clamp(direction, -1, 1) * Math.Clamp(speed, .01f, 1f) * Math.Min(dt, .1f), 0, stroke);
    }
}
