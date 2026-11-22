namespace SprocketWheeledVehicles;

/// <summary>
/// Synthesizes a steady, deep, low-frequency rubber tyre rolling loop
/// (warm 40-140 Hz tyre carcass rumble + soft low-passed road contact, zero rhythmic sweeping
/// and zero high metallic track clatter).
/// </summary>
public static class TireAudioSynthesizer
{
    public const int SampleRate = 22050;
    public const float DurationSeconds = 4.0f;

    public static float[] GenerateTireRollLoop()
    {
        int n = (int)(SampleRate * DurationSeconds);
        int fade = SampleRate / 2; // 0.5 s raised-cosine crossfade for totally inaudible loop seam
        var buf = new float[n];

        uint rng = 0x7A31C905u;
        float NextNoise()
        {
            rng ^= rng << 13;
            rng ^= rng >> 17;
            rng ^= rng << 5;
            return ((rng & 0xFFFF) / 32768f) - 1f;
        }

        var rawSub = new float[n + fade];
        var rawBody = new float[n + fade];

        // 3-pole steep low-pass for deep rubber rumble (< ~95 Hz)
        // + gentle low-pass for soft earth/asphalt contact (< ~220 Hz, zero broom/hiss frequencies).
        const float aSub = 0.026f;
        const float aBody = 0.062f;
        const float aDcBlock = 0.992f;

        float s1 = 0f, s2 = 0f, s3 = 0f;
        float b1 = 0f, b2 = 0f;
        float dcInPrev = 0f, dcOut = 0f;

        // Warm up filters for 0.5 s before sampling.
        for (int i = 0; i < SampleRate / 2; i++)
        {
            float w = NextNoise();
            s1 += aSub * (w - s1);
            s2 += aSub * (s1 - s2);
            s3 += aSub * (s2 - s3);
            b1 += aBody * (w - b1);
            b2 += aBody * (b1 - b2);
        }

        for (int i = 0; i < n + fade; i++)
        {
            float w = NextNoise();
            s1 += aSub * (w - s1);
            s2 += aSub * (s1 - s2);
            s3 += aSub * (s2 - s3);

            b1 += aBody * (w - b1);
            b2 += aBody * (b1 - b2);

            float mixed = s3 * 3.8f + b2 * 0.85f;
            // Remove sub-25Hz DC drift so speakers stay clean.
            dcOut = aDcBlock * (dcOut + mixed - dcInPrev);
            dcInPrev = mixed;

            rawSub[i] = dcOut;
            rawBody[i] = b2;
        }

        // Seamless overlap-add crossfade from [n .. n+fade-1] into [0 .. fade-1].
        for (int i = 0; i < fade; i++)
        {
            float t = i / (float)fade;
            float s = 0.5f - 0.5f * MathF.Cos(t * MathF.PI);
            rawSub[i] = rawSub[i] * s + rawSub[n + i] * (1f - s);
        }

        // Add subtle steady tyre tread-block hum (exact integer cycles over 4.0 s, zero slow LFO sweep).
        float twoPiOverN = 2f * MathF.PI / n;
        float peak = 0f;
        for (int i = 0; i < n; i++)
        {
            float phase = i * twoPiOverN;
            // 220 cycles / 4s = 55 Hz, 332 cycles / 4s = 83 Hz, 464 cycles / 4s = 116 Hz
            float treadHum =
                0.030f * MathF.Sin(phase * 220f) +
                0.018f * MathF.Sin(phase * 332f + 0.9f) +
                0.008f * MathF.Sin(phase * 464f + 2.1f);

            float sample = MathF.Tanh((rawSub[i] * 1.9f + treadHum) * 1.25f);
            buf[i] = sample;
            float a = MathF.Abs(sample);
            if (a > peak) peak = a;
        }

        if (peak > 1e-4f)
        {
            float gain = 0.58f / peak;
            for (int i = 0; i < n; i++)
                buf[i] *= gain;
        }

        return buf;
    }
}
