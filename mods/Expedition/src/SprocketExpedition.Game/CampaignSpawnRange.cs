namespace SprocketExpedition.Game;

// Encounter spacing policy by index in the native era schedule, not by dates.
internal readonly record struct CampaignSpawnRange(float Minimum, float Maximum)
{
    private const float MapMarginPerSide = 32f;
    internal static CampaignSpawnRange Default => new(350, 500);
    internal bool Valid => float.IsFinite(Minimum) && float.IsFinite(Maximum)
        && Minimum >= 24 && Maximum > Minimum && Maximum <= 5000;
    internal static CampaignSpawnRange ForEra(int eraIndex) => eraIndex switch
    {
        0 => Default, 1 => new(450, 600), 2 => new(600, 750),
        3 => new(800, 1000), 4 => new(1000, 1200),
        // Index 5 and every later native era keep the longest authored range.
        >= 5 => new(1200, 1500),
        _ => throw new ArgumentOutOfRangeException(nameof(eraIndex))
    };
    internal string Label => $"{Minimum:0}-{Maximum:0} m";

    // The requested era range is capped by the authored WorldBounds, not by the
    // terrain mesh. The limiting dimension plus a per-side hull/bounds margin is
    // used because a long diagonal is not a complete formation corridor.
    internal CampaignSpawnRange FitToMap(float worldWidth, float worldDepth)
    {
        if (!float.IsFinite(worldWidth) || !float.IsFinite(worldDepth)
            || worldWidth <= 96 || worldDepth <= 96)
            return new(0f, 0f);
        var usableWidth = worldWidth - MapMarginPerSide * 2f;
        var usableDepth = worldDepth - MapMarginPerSide * 2f;
        if (usableWidth <= 96f || usableDepth <= 96f) return new(0f, 0f);

        var corridor = MathF.Min(usableWidth, usableDepth) * .85f;
        var maximum = MathF.Min(Maximum, corridor);
        if (maximum >= Maximum) return this;
        if (!float.IsFinite(maximum) || maximum < 160f) return new(0f, 0f);
        var minimum = MathF.Min(Minimum, maximum * .72f);
        return new(MathF.Max(24f, minimum), maximum);
    }

    // Deterministic compact retries before native fallback. The first variant is
    // always the map-fitted range; later variants trade reach for a complete safe
    // group on narrow or irregular authored bounds.
    internal IEnumerable<CampaignSpawnRange> Attempts()
    {
        if (!Valid) yield break;
        yield return this;
        for (var scale = .85f; scale >= .55f; scale -= .15f)
        {
            var maximum = Maximum * scale;
            if (maximum < 160f) continue;
            var minimum = MathF.Min(Minimum, maximum * .72f);
            var candidate = new CampaignSpawnRange(MathF.Max(24f, minimum), maximum);
            if (candidate.Valid && candidate.Maximum < Maximum - .5f)
                yield return candidate;
        }
    }
}
