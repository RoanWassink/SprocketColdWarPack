namespace SprocketThermalSight;

public static class IrBeamPolicy
{
    // angleDeg is the full cone angle; offset and forward are in world metres.
    public static float Illumination(float x, float y, float z, float fx, float fy, float fz, float rangeM, float angleDeg)
    {
        float distance = MathF.Sqrt(x * x + y * y + z * z);
        float length = MathF.Sqrt(fx * fx + fy * fy + fz * fz);
        if (!float.IsFinite(distance) || !float.IsFinite(length) || length < .00001f || distance < .01f || distance > rangeM) return 0;
        float cosine = (x * fx + y * fy + z * fz) / (distance * length);
        float edge = MathF.Cos(angleDeg * MathF.PI / 360);
        if (cosine < edge) return 0;
        float feather = Math.Clamp((cosine - edge) / Math.Max(.000001f, 1 - edge) * 4, 0, 1);
        float attenuation = Math.Max(0, 1 - distance / rangeM);
        return feather * attenuation * attenuation;
    }
}
