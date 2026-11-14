using System;

namespace SprocketStabilization;

/// <summary>
/// The four validated boost values that may be written to a native
/// TurretBehaviour for the duration of one MoveToTarget call.
/// </summary>
internal readonly struct BoostSettings
{
    internal BoostSettings(
        float torqueMultiplier,
        float maxTraverseRate,
        float maxAcceleration,
        float maxBrakeAcceleration)
    {
        TorqueMultiplier = torqueMultiplier;
        MaxTraverseRate = maxTraverseRate;
        MaxAcceleration = maxAcceleration;
        MaxBrakeAcceleration = maxBrakeAcceleration;
    }

    internal float TorqueMultiplier { get; }

    internal float MaxTraverseRate { get; }

    internal float MaxAcceleration { get; }

    internal float MaxBrakeAcceleration { get; }
}

/// <summary>
/// Validated realistic drive limits for the default stabilizer mode.
///
/// The rates are degrees per second. Acceleration and braking are derived from
/// the rate and the response time, so a real-world-class servo loop cannot snap
/// to a new target instantly: 24 deg/s over 0.75 s means 32 deg/s^2.
/// </summary>
internal readonly struct RealisticLimits
{
    internal RealisticLimits(
        float horizontalMaxTraverseRate,
        float elevationMaxTraverseRate,
        float responseTimeSeconds)
    {
        HorizontalMaxTraverseRate = horizontalMaxTraverseRate;
        ElevationMaxTraverseRate = elevationMaxTraverseRate;
        ResponseTimeSeconds = responseTimeSeconds;
    }

    internal float HorizontalMaxTraverseRate { get; }

    internal float ElevationMaxTraverseRate { get; }

    internal float ResponseTimeSeconds { get; }

    internal float RateFor(bool elevation)
        => elevation ? ElevationMaxTraverseRate : HorizontalMaxTraverseRate;

    internal float AccelerationFor(bool elevation)
        => RateFor(elevation) / ResponseTimeSeconds;
}

/// <summary>
/// Production validation for the configured stabilizer boost.
///
/// Every configured value must be finite and inside a documented positive
/// range. A value that is NaN, infinite, nonpositive or outside its range is
/// replaced by the built-in default instead of being written to the behaviour.
/// This file is intentionally free of Unity/IL2CPP/BepInEx types so the exact
/// same logic is linked by the local checks.
/// </summary>
internal static class StabilizerBoost
{
    // Built-in fallbacks, also the documented config defaults.
    internal const float DefaultTorqueMultiplier = 100f;
    internal const float DefaultMaxTraverseRate = 360f;
    internal const float DefaultMaxAcceleration = 3600f;
    internal const float DefaultMaxBrakeAcceleration = 3600f;

    // Realistic defaults from Cold War stabilizer-class drives. AFV Database
    // (M60/M60A1/M60A2/M60A3): M60 24 deg/s traverse and 4 deg/s elevation,
    // M60A1/A3 22.5/4, M60A2 (stabilized) 40/10. 24/6 with a 0.75 s response
    // time is a defensible middle ground for the mod's era; all configurable.
    // These are NOT used by the explicit OverrideRateLimits mode.
    internal const float DefaultHorizontalMaxTraverseRate = 24f;
    internal const float DefaultElevationMaxTraverseRate = 6f;
    internal const float DefaultResponseTimeSeconds = 0.75f;

    internal const float MinResponseTimeSeconds = 0.1f;
    internal const float MaxResponseTimeSeconds = 5f;

    // Reasonable bounds: strictly positive, finite and far above any value the
    // game needs, while still rejecting absurd/overflowing input.
    internal const float MinTorqueMultiplier = 0.01f;
    internal const float MaxTorqueMultiplier = 100000f;

    internal const float MinMaxTraverseRate = 0.01f;
    internal const float MaxMaxTraverseRate = 100000f;

    internal const float MinMaxAcceleration = 0.01f;
    internal const float MaxMaxAcceleration = 10000000f;

    internal const float MinMaxBrakeAcceleration = 0.01f;
    internal const float MaxMaxBrakeAcceleration = 10000000f;

    internal static bool IsValid(float value, float min, float max)
        => float.IsFinite(value) && value >= min && value <= max;

    internal static float Sanitize(float configured, float fallback, float min, float max)
        => IsValid(configured, min, max) ? configured : fallback;

    /// <summary>
    /// Sanitizes all four values independently. Invalid fields fall back to the
    /// built-in default; the other fields keep their configured value.
    /// </summary>
    internal static BoostSettings SanitizeAll(
        float torqueMultiplier,
        float maxTraverseRate,
        float maxAcceleration,
        float maxBrakeAcceleration)
    {
        return new BoostSettings(
            Sanitize(torqueMultiplier, DefaultTorqueMultiplier, MinTorqueMultiplier, MaxTorqueMultiplier),
            Sanitize(maxTraverseRate, DefaultMaxTraverseRate, MinMaxTraverseRate, MaxMaxTraverseRate),
            Sanitize(maxAcceleration, DefaultMaxAcceleration, MinMaxAcceleration, MaxMaxAcceleration),
            Sanitize(maxBrakeAcceleration, DefaultMaxBrakeAcceleration, MinMaxBrakeAcceleration, MaxMaxBrakeAcceleration));
    }

    /// <summary>
    /// Reads the plugin config entries (null means "not bound yet") through the
    /// same sanitizer used by the native-call path.
    /// </summary>
    internal static BoostSettings FromConfig(
        float? torqueMultiplier,
        float? maxTraverseRate,
        float? maxAcceleration,
        float? maxBrakeAcceleration)
    {
        return SanitizeAll(
            torqueMultiplier ?? DefaultTorqueMultiplier,
            maxTraverseRate ?? DefaultMaxTraverseRate,
            maxAcceleration ?? DefaultMaxAcceleration,
            maxBrakeAcceleration ?? DefaultMaxBrakeAcceleration);
    }

    /// <summary>
    /// Sanitizes the realistic-mode limits independently. Invalid rates or a
    /// nonpositive/nonfinite response time fall back to the documented defaults;
    /// valid values are used exactly.
    /// </summary>
    internal static RealisticLimits RealisticFromConfig(
        float? horizontalRate,
        float? elevationRate,
        float? responseTimeSeconds)
    {
        return new RealisticLimits(
            Sanitize(
                horizontalRate ?? DefaultHorizontalMaxTraverseRate,
                DefaultHorizontalMaxTraverseRate,
                MinMaxTraverseRate,
                MaxMaxTraverseRate),
            Sanitize(
                elevationRate ?? DefaultElevationMaxTraverseRate,
                DefaultElevationMaxTraverseRate,
                MinMaxTraverseRate,
                MaxMaxTraverseRate),
            Sanitize(
                responseTimeSeconds ?? DefaultResponseTimeSeconds,
                DefaultResponseTimeSeconds,
                MinResponseTimeSeconds,
                MaxResponseTimeSeconds));
    }
}
