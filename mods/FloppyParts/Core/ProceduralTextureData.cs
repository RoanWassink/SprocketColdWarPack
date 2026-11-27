using System.Buffers.Binary;

namespace SprocketFloppyParts;

internal sealed record ProceduralTextureSet(int Size, byte[] AlbedoRgba, byte[] NormalRgba);

internal static class ProceduralTextureData
{
    private const float TwoPi = MathF.PI * 2f;

    internal static ProceduralTextureSet GenerateCanvas(int size = 256)
    {
        ValidateSize(size);
        var albedo = new byte[size * size * 4];
        var height = new float[size * size];

        int threads = Math.Max(8, size / 8); // 32 threads at 256px
        for (int y = 0; y < size; y++)
        {
            float v = (float)y / size;
            for (int x = 0; x < size; x++)
            {
                float u = (float)x / size;
                float tx = u * threads;
                float ty = v * threads;
                int ix = (int)MathF.Floor(tx);
                int iy = (int)MathF.Floor(ty);

                float warpPhase = MathF.Sin(TwoPi * tx);
                float weftPhase = MathF.Sin(TwoPi * ty);
                float overUnder = ((ix + iy) & 1) == 0 ? 1f : -1f;

                float warpRidge = MathF.Max(0f, MathF.Sin(MathF.PI * (tx - ix)));
                float weftRidge = MathF.Max(0f, MathF.Sin(MathF.PI * (ty - iy)));
                float fiber = PeriodicNoise(u, v, 32, 11) * 0.14f + PeriodicNoise(u, v, 64, 29) * 0.08f;

                float h = 0.5f * (warpRidge + weftRidge) + 0.16f * overUnder * (warpPhase - weftPhase) + fiber;
                height[y * size + x] = h;

                float shade = 210f + (h - 0.5f) * 34f + (fiber - 0.11f) * 28f;
                byte lum = ToByte(shade);
                WritePixel(albedo, y * size + x, lum, lum, lum, 255);
            }
        }

        var normal = HeightToNormal(height, size, 2.2f);
        return new ProceduralTextureSet(size, albedo, normal);
    }

    internal static ProceduralTextureSet GenerateCamoNet(int size = 256)
    {
        ValidateSize(size);
        var albedo = new byte[size * size * 4];
        var height = new float[size * size];

        int gridFreq = Math.Max(6, size / 24); // ~10 cells across 256px
        for (int y = 0; y < size; y++)
        {
            float v = (float)y / size;
            for (int x = 0; x < size; x++)
            {
                float u = (float)x / size;
                float diag1 = MathF.Abs(MathF.Sin(MathF.PI * (u + v) * gridFreq));
                float diag2 = MathF.Abs(MathF.Sin(MathF.PI * (u - v) * gridFreq));
                bool scrim = diag1 < 0.24f || diag2 < 0.24f;

                float f1 = PeriodicNoise(u, v, 6, 101);
                float f2 = PeriodicNoise(u, v, 12, 211);
                float f3 = PeriodicNoise(u, v, 24, 307);
                float garnish = f1 * 0.50f + f2 * 0.34f + f3 * 0.16f;

                // Leaf/garnish strips cover ~52-58% on top of the ~30% scrim net, leaving ~30-35% open holes.
                bool strip = garnish > 0.455f;
                bool opaque = scrim || strip;

                float h;
                byte lum;
                byte alpha;
                if (strip)
                {
                    float fold = MathF.Sin(TwoPi * (u * 8f + f2 * 0.6f)) * 0.25f + f3 * 0.4f;
                    h = 0.72f + fold * 0.28f;
                    lum = ToByte(188f + (garnish - 0.45f) * 70f + f3 * 22f);
                    alpha = 242;
                }
                else if (scrim)
                {
                    h = 0.42f + f3 * 0.12f;
                    lum = ToByte(168f + f3 * 26f);
                    alpha = 232;
                }
                else
                {
                    h = 0.05f;
                    lum = 120;
                    alpha = 8;
                }

                height[y * size + x] = h;
                WritePixel(albedo, y * size + x, lum, lum, lum, alpha);
            }
        }

        var normal = HeightToNormal(height, size, 2.5f);
        return new ProceduralTextureSet(size, albedo, normal);
    }

    internal static ProceduralTextureSet GenerateRubber(int size = 256)
    {
        ValidateSize(size);
        var albedo = new byte[size * size * 4];
        var height = new float[size * size];

        for (int y = 0; y < size; y++)
        {
            float v = (float)y / size;
            for (int x = 0; x < size; x++)
            {
                float u = (float)x / size;
                float fine = PeriodicNoise(u, v, 48, 401);
                float coarse = PeriodicNoise(u, v, 16, 509);
                float speck = PeriodicHash(x, y, 613);

                float pit = speck < 0.08f ? -0.35f : 0f;
                float h = 0.55f + (fine - 0.5f) * 0.35f + (coarse - 0.5f) * 0.20f + pit;
                height[y * size + x] = h;

                float shade = 222f + (coarse - 0.5f) * 20f + (fine - 0.5f) * 18f + pit * 22f;
                byte lum = ToByte(shade);
                WritePixel(albedo, y * size + x, lum, lum, lum, 255);
            }
        }

        var normal = HeightToNormal(height, size, 1.8f);
        return new ProceduralTextureSet(size, albedo, normal);
    }

    internal static ProceduralTextureSet GenerateWood(int size = 256)
    {
        ValidateSize(size);
        var albedo = new byte[size * size * 4];
        var height = new float[size * size];

        int plankCount = 4;
        for (int y = 0; y < size; y++)
        {
            float v = (float)y / size;
            float plankPos = v * plankCount;
            int plankIdx = (int)MathF.Floor(plankPos) % plankCount;
            float plankFrac = plankPos - MathF.Floor(plankPos);
            float seamDist = MathF.Min(plankFrac, 1f - plankFrac);
            float seam = seamDist < 0.025f ? seamDist / 0.025f : 1f;
            float plankShift = (plankIdx * 0.37f) % 1f;
            float plankTone = ((plankIdx % 2 == 0) ? 1f : -1f) * 6f;

            for (int x = 0; x < size; x++)
            {
                float u = (float)x / size;
                float warpedV = v + 0.035f * PeriodicNoise((u + plankShift) % 1f, v, 8, 701);
                float ring = MathF.Sin(TwoPi * (warpedV * 24f + 1.4f * PeriodicNoise(u, v, 4, 811)));
                float grain = PeriodicNoise((u + plankShift) % 1f, v, 32, 907);

                float h = (0.65f + 0.16f * ring + 0.14f * (grain - 0.5f)) * (0.25f + 0.75f * seam);
                height[y * size + x] = h;

                float lumBase = (206f + plankTone + ring * 14f + (grain - 0.5f) * 16f) * (0.55f + 0.45f * seam);
                byte r = ToByte(lumBase * 1.02f);
                byte g = ToByte(lumBase * 0.96f);
                byte b = ToByte(lumBase * 0.88f);
                WritePixel(albedo, y * size + x, r, g, b, 255);
            }
        }

        var normal = HeightToNormal(height, size, 2.4f);
        return new ProceduralTextureSet(size, albedo, normal);
    }

    internal static byte[] GenerateThermalRainbowRgba(int size = 128)
    {
        ValidateSize(size);
        var rgba = new byte[size * size * 4];
        const float twoPi = 2f * MathF.PI;

        for (int y = 0; y < size; y++)
        {
            float v = (float)y / size;
            for (int x = 0; x < size; x++)
            {
                float u = (float)x / size;
                // Smooth diagonal + subtle optical ripple mimicking coated thermal/AR optics.
                float ripple = 0.08f * MathF.Sin(twoPi * (u * 2f - v)) +
                               0.05f * MathF.Cos(twoPi * (u + v * 2f));
                float phase = (u * 0.85f + v * 0.65f + ripple) * twoPi;

                float r = 0.56f + 0.38f * MathF.Cos(phase + 0.00f);
                float g = 0.54f + 0.36f * MathF.Cos(phase - twoPi / 3f);
                float b = 0.62f + 0.34f * MathF.Cos(phase + twoPi / 3f);

                WritePixel(
                    rgba,
                    y * size + x,
                    ToByte(r * 255f),
                    ToByte(g * 255f),
                    ToByte(b * 255f),
                    255);
            }
        }
        return rgba;
    }

    private static byte[] HeightToNormal(float[] height, int size, float strength)
    {
        var normal = new byte[size * size * 4];
        for (int y = 0; y < size; y++)
        {
            int ym = (y - 1 + size) % size;
            int yp = (y + 1) % size;
            for (int x = 0; x < size; x++)
            {
                int xm = (x - 1 + size) % size;
                int xp = (x + 1) % size;

                float dx = (height[y * size + xp] - height[y * size + xm]) * strength;
                float dy = (height[yp * size + x] - height[ym * size + x]) * strength;
                float nx = -dx;
                float ny = -dy;
                float nz = 1f;
                float invLen = 1f / MathF.Sqrt(nx * nx + ny * ny + nz * nz);
                nx *= invLen;
                ny *= invLen;
                nz *= invLen;

                WritePixel(
                    normal,
                    y * size + x,
                    ToByte((nx * 0.5f + 0.5f) * 255f),
                    ToByte((ny * 0.5f + 0.5f) * 255f),
                    ToByte((nz * 0.5f + 0.5f) * 255f),
                    255);
            }
        }
        return normal;
    }

    private static float PeriodicNoise(float u, float v, int period, int seed)
    {
        float x = u * period;
        float y = v * period;
        int x0 = ((int)MathF.Floor(x)) % period;
        int y0 = ((int)MathF.Floor(y)) % period;
        if (x0 < 0) x0 += period;
        if (y0 < 0) y0 += period;
        int x1 = (x0 + 1) % period;
        int y1 = (y0 + 1) % period;

        float fx = x - MathF.Floor(x);
        float fy = y - MathF.Floor(y);
        float sx = fx * fx * (3f - 2f * fx);
        float sy = fy * fy * (3f - 2f * fy);

        float n00 = PeriodicHash(x0, y0, seed);
        float n10 = PeriodicHash(x1, y0, seed);
        float n01 = PeriodicHash(x0, y1, seed);
        float n11 = PeriodicHash(x1, y1, seed);

        float ix0 = n00 + (n10 - n00) * sx;
        float ix1 = n01 + (n11 - n01) * sx;
        return ix0 + (ix1 - ix0) * sy;
    }

    private static float PeriodicHash(int x, int y, int seed)
    {
        unchecked
        {
            uint h = (uint)x * 374761393u + (uint)y * 668265263u + (uint)seed * 1442695041u;
            h = (h ^ (h >> 13)) * 1274126177u;
            h ^= h >> 16;
            return (h & 0xFFFFu) / 65535f;
        }
    }

    private static void ValidateSize(int size)
    {
        if (size < 16 || size > 2048)
            throw new ArgumentOutOfRangeException(nameof(size), "Texture size must be between 16 and 2048.");
    }

    private static byte ToByte(float v) => (byte)Math.Clamp((int)MathF.Round(v), 0, 255);

    private static void WritePixel(byte[] buffer, int pixelIndex, byte r, byte g, byte b, byte a)
    {
        int o = pixelIndex * 4;
        buffer[o] = r;
        buffer[o + 1] = g;
        buffer[o + 2] = b;
        buffer[o + 3] = a;
    }
}

internal static class CustomTextureRules
{
    internal const int MaxFileBytes = 32 * 1024 * 1024;
    internal const int MinDimension = 64;
    internal const int MaxDimension = 4096;

    internal static (int Width, int Height) ValidatePngHeader(byte[] png)
    {
        if (png == null) throw new InvalidDataException("PNG buffer is null.");
        if (png.Length > MaxFileBytes) throw new InvalidDataException("PNG exceeds 32 MiB.");
        ReadOnlySpan<byte> signature = stackalloc byte[] { 137, 80, 78, 71, 13, 10, 26, 10 };
        ReadOnlySpan<byte> ihdr = stackalloc byte[] { 73, 72, 68, 82 };
        if (png.Length < 33 ||
            !png.AsSpan(0, 8).SequenceEqual(signature) ||
            !png.AsSpan(12, 4).SequenceEqual(ihdr))
            throw new InvalidDataException("Invalid PNG header.");

        int w = BinaryPrimitives.ReadInt32BigEndian(png.AsSpan(16, 4));
        int h = BinaryPrimitives.ReadInt32BigEndian(png.AsSpan(20, 4));
        if (w < MinDimension || w > MaxDimension || h < MinDimension || h > MaxDimension)
            throw new InvalidDataException($"PNG dimensions {w}x{h} outside {MinDimension}..{MaxDimension}.");
        return (w, h);
    }

    internal static byte ConvertNormalGreen(byte g, bool directX) =>
        directX ? (byte)(255 - g) : g;

    internal static (byte R, byte G, byte B, byte A) PackHdrpMask(byte roughness, byte ao) =>
        (0, ao, 255, (byte)(255 - roughness));
}
