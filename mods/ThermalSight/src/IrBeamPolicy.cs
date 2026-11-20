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

    // 0.5.4: the beam shape per pixel. The cone falloff comes from the pixel's own ray direction against the
    // lamp axis (smooth to the cone edge), so the lit silhouette follows the scene and not the raycast grid.
    // The coarse grid only supplies distance falloff (inverse square, clamped) and occlusion.
    public static float PixelWeight(float dx, float dy, float dz, float fx, float fy, float fz, float angleDeg)
    {
        float dl = MathF.Sqrt(dx * dx + dy * dy + dz * dz), fl = MathF.Sqrt(fx * fx + fy * fy + fz * fz);
        if (!float.IsFinite(dl) || !float.IsFinite(fl) || dl < .00001f || fl < .00001f) return 0;
        float cosine = (dx * fx + dy * fy + dz * fz) / (dl * fl);
        float edge = MathF.Cos(angleDeg * MathF.PI / 360);
        if (!(cosine > edge)) return 0;
        // Smoothstep from the cone edge to the axis: zero slope at the edge, no visible ring.
        float t = Math.Clamp((cosine - edge) / Math.Max(.000001f, 1 - edge), 0, 1);
        return t * t * (3 - 2 * t);
    }

    // Inverse-square falloff of the lamp, clamped in the near field so close surfaces do not blow up
    // and smoothly tapered to zero at the beam range limit.
    public static float DistanceFalloff(float distanceM, float rangeM, float nearM = 200)
    {
        if (!float.IsFinite(distanceM) || !float.IsFinite(rangeM) || rangeM <= 0 || distanceM < 0 || distanceM >= rangeM) return 0;
        float clampedNear = Math.Min(nearM, rangeM * .5f);
        if (distanceM <= clampedNear) return 1;
        float invSq = (clampedNear * clampedNear) / (distanceM * distanceM);
        float ratio = distanceM / rangeM;
        float fade = Math.Clamp(1 - ratio * ratio, 0, 1);
        float nearRatio = clampedNear / rangeM;
        float nearFade = Math.Max(.0001f, 1 - nearRatio * nearRatio);
        return Math.Clamp(invSq * (fade / nearFade), 0, 1);
    }

    // Per-pixel illumination: cone shape times the grid's distance falloff and occlusion (0 = blocked or no hit).
    public static float Combine(float cone, float grid)
        => !float.IsFinite(cone) || !float.IsFinite(grid) || cone <= 0 || grid <= 0 ? 0 : Math.Clamp(cone * grid, 0, 1);

    // 0.5.4: normalised 2x2 bilinear sample of the distance/occlusion grid.
    // Cell values: < 0 = camera ray missed (sky), 0 = occluded or out of range, > 0 = DistanceFalloff.
    // Missed cells get weight 0 so a sky cell never darkens adjacent ground; occluded cells (= 0) keep
    // their weight so shadows stay dark; pure sky (all 4 corners missed) returns 0.
    public static float SampleGrid(float[] grid, int width, int height, float sx, float sy)
    {
        if (grid == null || width <= 0 || height <= 0 || grid.Length < width * height || !float.IsFinite(sx) || !float.IsFinite(sy)) return 0;
        sx = Math.Clamp(sx, 0, width - 1);
        sy = Math.Clamp(sy, 0, height - 1);
        int x0 = (int)sx, y0 = (int)sy, x1 = Math.Min(x0 + 1, width - 1), y1 = Math.Min(y0 + 1, height - 1);
        float tx = sx - x0, ty = sy - y0;
        float sum = 0, wsum = 0;
        float v00 = grid[y0 * width + x0], v10 = grid[y0 * width + x1], v01 = grid[y1 * width + x0], v11 = grid[y1 * width + x1];
        float w00 = (1 - tx) * (1 - ty), w10 = tx * (1 - ty), w01 = (1 - tx) * ty, w11 = tx * ty;
        if (v00 >= 0) { sum += v00 * w00; wsum += w00; }
        if (v10 >= 0) { sum += v10 * w10; wsum += w10; }
        if (v01 >= 0) { sum += v01 * w01; wsum += w01; }
        if (v11 >= 0) { sum += v11 * w11; wsum += w11; }
        return wsum > 1e-6f ? sum / wsum : 0;
    }
}
