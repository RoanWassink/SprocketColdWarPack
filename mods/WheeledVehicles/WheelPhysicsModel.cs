namespace SprocketWheeledVehicles;

public readonly struct Vec3
{
    public readonly float X;
    public readonly float Y;
    public readonly float Z;

    public Vec3(float x, float y, float z) { X = x; Y = y; Z = z; }

    public static Vec3 Zero => new(0f, 0f, 0f);
    public float LengthSq => X * X + Y * Y + Z * Z;
    public float Length => MathF.Sqrt(LengthSq);
    public bool IsFinite => float.IsFinite(X) && float.IsFinite(Y) && float.IsFinite(Z);

    public static Vec3 operator +(Vec3 a, Vec3 b) => new(a.X + b.X, a.Y + b.Y, a.Z + b.Z);
    public static Vec3 operator -(Vec3 a, Vec3 b) => new(a.X - b.X, a.Y - b.Y, a.Z - b.Z);
    public static Vec3 operator *(Vec3 a, float s) => new(a.X * s, a.Y * s, a.Z * s);
    public static Vec3 operator *(float s, Vec3 a) => new(a.X * s, a.Y * s, a.Z * s);
    public static float Dot(Vec3 a, Vec3 b) => a.X * b.X + a.Y * b.Y + a.Z * b.Z;
}

public sealed class AxleGroup
{
    public int AxleIndex { get; init; }
    public float MeanZ { get; init; }
    public float TrackWidth { get; init; }
    public List<int> WheelIndices { get; } = new();
}

public readonly struct WheelGeometryInput
{
    public readonly float LocalX;
    public readonly float LocalY;
    public readonly float LocalZ;
    public readonly float Radius;
    public readonly bool IsLeft;

    public WheelGeometryInput(float localX, float localY, float localZ, float radius, bool isLeft)
    {
        LocalX = localX;
        LocalY = localY;
        LocalZ = localZ;
        Radius = radius;
        IsLeft = isLeft;
    }
}

public readonly struct SuspensionResult
{
    public readonly float NormalForce;
    public readonly float Compression;
    public readonly float CompressionVelocity;
    public readonly float VerticalWheelOffset;

    public SuspensionResult(float normalForce, float compression, float compressionVelocity, float verticalWheelOffset)
    {
        NormalForce = normalForce;
        Compression = compression;
        CompressionVelocity = compressionVelocity;
        VerticalWheelOffset = verticalWheelOffset;
    }
}

public readonly struct TireForceResult
{
    public readonly float LongitudinalForce;
    public readonly float LateralForce;
    public readonly float SlipRatio;
    public readonly float SlipAngleRad;
    public readonly bool Saturated;

    public TireForceResult(float longitudinalForce, float lateralForce, float slipRatio, float slipAngleRad, bool saturated)
    {
        LongitudinalForce = longitudinalForce;
        LateralForce = lateralForce;
        SlipRatio = slipRatio;
        SlipAngleRad = slipAngleRad;
        Saturated = saturated;
    }
}

/// <summary>
/// Pure, Unity-independent wheeled vehicle dynamics math:
/// axle grouping, Ackermann steering (2 to 5+ axles), speed-sensitive rear steering,
/// mass-calibrated suspension + anti-roll, and Kamm-circle tire grip.
/// </summary>
public static class WheelPhysicsModel
{
    public const float Gravity = 9.81f;
    public const float DegToRad = MathF.PI / 180f;
    public const float RadToDeg = 180f / MathF.PI;
    public const float RollingResistanceCoeff = 0.018f;
    public const float BaseLongitudinalFriction = 0.86f;
    public const float BaseLateralFriction = 0.66f;
    public const float BaseTireFrictionCoeff = BaseLongitudinalFriction;
    public const float MaxSteerClampDeg = 55f;

    /// <summary>
    /// Groups wheels into front-to-rear axles (index 0 = frontmost axle, highest +Z).
    /// </summary>
    public static List<AxleGroup> GroupAxles(IReadOnlyList<WheelGeometryInput> wheels)
    {
        var result = new List<AxleGroup>();
        if (wheels.Count == 0) return result;

        var order = new int[wheels.Count];
        for (int i = 0; i < wheels.Count; i++) order[i] = i;
        Array.Sort(order, (a, b) => wheels[b].LocalZ.CompareTo(wheels[a].LocalZ));

        var rawGroups = new List<List<int>>();
        foreach (int idx in order)
        {
            float z = wheels[idx].LocalZ;
            float tol = Math.Clamp(wheels[idx].Radius * 0.45f, 0.15f, 0.45f);
            bool placed = false;
            for (int g = 0; g < rawGroups.Count; g++)
            {
                float sumZ = 0f;
                foreach (int wi in rawGroups[g]) sumZ += wheels[wi].LocalZ;
                float avgZ = sumZ / rawGroups[g].Count;
                if (MathF.Abs(z - avgZ) <= tol)
                {
                    rawGroups[g].Add(idx);
                    placed = true;
                    break;
                }
            }
            if (!placed)
                rawGroups.Add(new List<int> { idx });
        }

        // Re-sort groups strictly from front (+Z) to rear (-Z).
        rawGroups.Sort((ga, gb) =>
        {
            float za = 0f, zb = 0f;
            foreach (int i in ga) za += wheels[i].LocalZ;
            foreach (int i in gb) zb += wheels[i].LocalZ;
            return (zb / gb.Count).CompareTo(za / ga.Count);
        });

        for (int a = 0; a < rawGroups.Count; a++)
        {
            var g = rawGroups[a];
            float sumZ = 0f, minX = float.PositiveInfinity, maxX = float.NegativeInfinity;
            foreach (int wi in g)
            {
                sumZ += wheels[wi].LocalZ;
                if (wheels[wi].LocalX < minX) minX = wheels[wi].LocalX;
                if (wheels[wi].LocalX > maxX) maxX = wheels[wi].LocalX;
            }
            var axle = new AxleGroup
            {
                AxleIndex = a,
                MeanZ = sumZ / g.Count,
                TrackWidth = MathF.Max(0f, maxX - minX)
            };
            axle.WheelIndices.AddRange(g);
            result.Add(axle);
        }

        return result;
    }

    /// <summary>
    /// Smooth speed-sensitive steering angle limit so heavy armoured cars stay planted in high-speed turns.
    /// Full angle up to 10 km/h; smoothly tapers to ~55% at 30 km/h, ~32% at 55 km/h and ~20% by 85+ km/h.
    /// </summary>
    public static float ComputeSpeedLimitedSteerDeg(float maxSteerAngleDeg, float speedMps)
    {
        float speedKmh = MathF.Abs(speedMps) * 3.6f;
        if (speedKmh <= 10f) return maxSteerAngleDeg;
        float t = Math.Clamp((speedKmh - 10f) / 75f, 0f, 1f);
        // Smooth inverse curve: quick gentle taper at mid speeds without feeling dead at high speed.
        float factor = 1f / (1f + 3.4f * t * t + 0.8f * t);
        return MathF.Max(6f, maxSteerAngleDeg * Math.Clamp(factor, 0.20f, 1f));
    }

    /// <summary>
    /// Computes the steering angle in degrees for every wheel using multi-axle Ackermann geometry
    /// (or parallel steering when Ackermann is disabled) and speed-dependent rear-axle steering.
    /// Positive steerInput (+1) turns right (+Y yaw in Unity left-handed coordinates).
    /// </summary>
    public static float[] ComputeWheelSteerAnglesDeg(
        IReadOnlyList<WheelGeometryInput> wheels,
        IReadOnlyList<AxleGroup> axles,
        WheeledSettings settings,
        float steerInput,
        float forwardSpeedMps)
    {
        var angles = new float[wheels.Count];
        if (wheels.Count == 0 || axles.Count == 0 || MathF.Abs(steerInput) < 1e-4f)
            return angles;

        float clampedInput = Math.Clamp(steerInput, -1f, 1f);
        float sign = MathF.Sign(clampedInput);
        float mag = MathF.Abs(clampedInput);

        float effectiveFrontMaxDeg = ComputeSpeedLimitedSteerDeg(settings.MaxSteerAngleDeg, forwardSpeedMps);
        float centerFrontDeg = effectiveFrontMaxDeg * mag;

        // Classify axles.
        int totalAxles = axles.Count;
        float frontmostSteerZ = float.NegativeInfinity;
        float nonSteerZSum = 0f;
        int nonSteerCount = 0;
        float rearSteerZSum = 0f;
        int rearSteerCount = 0;
        float rearmostZ = axles[totalAxles - 1].MeanZ;

        var configs = new AxleConfig[totalAxles];
        for (int a = 0; a < totalAxles; a++)
        {
            configs[a] = settings.GetEffectiveAxleConfig(a, totalAxles);
            if (configs[a].SteerMode == AxleSteerMode.Front)
            {
                if (axles[a].MeanZ > frontmostSteerZ) frontmostSteerZ = axles[a].MeanZ;
            }
            else if (configs[a].SteerMode == AxleSteerMode.Rear)
            {
                rearSteerZSum += axles[a].MeanZ;
                rearSteerCount++;
            }
            else
            {
                nonSteerZSum += axles[a].MeanZ;
                nonSteerCount++;
            }
        }

        if (!float.IsFinite(frontmostSteerZ))
            frontmostSteerZ = axles[0].MeanZ;

        // Rear-steering blend factor as a function of vehicle speed:
        // Low speed: -1 (counter-steering for tight turn radius).
        // Above transition speed: 0 (Off) or +0.35 (SameDirection for high-speed stability).
        float speedKmh = MathF.Abs(forwardSpeedMps) * 3.6f;
        float rearSpeedFactor = ComputeRearSteerSpeedFactor(speedKmh, settings.RearSteerTransitionKmh, settings.RearSteerHighSpeed);

        // Determine longitudinal turn center Z (the point along Z with zero slip angle).
        float turnCenterZ;
        if (nonSteerCount > 0)
        {
            turnCenterZ = nonSteerZSum / nonSteerCount;
        }
        else if (rearSteerCount > 0 && rearSpeedFactor < -0.05f)
        {
            // All axles steer (e.g. 4x4 with 4WS): turn center lies between front and rear axles.
            float rearZ = rearSteerZSum / rearSteerCount;
            float rearRatio = Math.Clamp(settings.RearSteerMaxAngleDeg / MathF.Max(1f, settings.MaxSteerAngleDeg), 0f, 1f) * MathF.Abs(rearSpeedFactor);
            float t = rearRatio / (1f + rearRatio);
            turnCenterZ = rearZ + (frontmostSteerZ - rearZ) * t;
        }
        else
        {
            turnCenterZ = rearmostZ;
        }

        float refWheelbase = MathF.Max(0.4f, frontmostSteerZ - turnCenterZ);
        float tanCenter = MathF.Tan(Math.Clamp(centerFrontDeg, 0.01f, MaxSteerClampDeg) * DegToRad);
        float turnRadius = refWheelbase / MathF.Max(1e-4f, tanCenter);

        for (int a = 0; a < totalAxles; a++)
        {
            var cfg = configs[a];
            if (cfg.SteerMode == AxleSteerMode.None || cfg.SteerScale <= 0.001f)
                continue;

            var axle = axles[a];
            foreach (int wi in axle.WheelIndices)
            {
                float angleDeg;
                if (cfg.SteerMode == AxleSteerMode.Front)
                {
                    if (settings.AckermannEnabled)
                    {
                        float dz = wheels[wi].LocalZ - turnCenterZ;
                        float dx = MathF.Max(0.35f, turnRadius - sign * wheels[wi].LocalX);
                        angleDeg = sign * MathF.Atan2(dz, dx) * RadToDeg * cfg.SteerScale;
                    }
                    else
                    {
                        float lever = Math.Clamp((wheels[wi].LocalZ - turnCenterZ) / refWheelbase, -1f, 1f);
                        angleDeg = clampedInput * centerFrontDeg * lever * cfg.SteerScale;
                    }
                }
                else // AxleSteerMode.Rear
                {
                    if (MathF.Abs(rearSpeedFactor) < 1e-3f)
                    {
                        angleDeg = 0f;
                    }
                    else if (rearSpeedFactor < 0f && settings.AckermannEnabled)
                    {
                        // Counter-steering: Ackermann geometry pointing toward the same turning center,
                        // scaled smoothly by |rearSpeedFactor| and capped to RearSteerMaxAngleDeg.
                        float dz = wheels[wi].LocalZ - turnCenterZ; // negative when behind turnCenterZ
                        float dx = MathF.Max(0.35f, turnRadius - sign * wheels[wi].LocalX);
                        float geoDeg = sign * MathF.Atan2(dz, dx) * RadToDeg * MathF.Abs(rearSpeedFactor) * cfg.SteerScale;
                        float cap = settings.RearSteerMaxAngleDeg * mag * cfg.SteerScale;
                        angleDeg = Math.Clamp(geoDeg, -cap, cap);
                    }
                    else
                    {
                        // Same-direction high-speed stability steer (crab/lane-change) or non-Ackermann counter-steer.
                        float rawRear = clampedInput * settings.RearSteerMaxAngleDeg * rearSpeedFactor * cfg.SteerScale;
                        angleDeg = rawRear;
                    }
                }

                angles[wi] = Math.Clamp(angleDeg, -MaxSteerClampDeg, MaxSteerClampDeg);
            }
        }

        return angles;
    }

    /// <summary>
    /// Returns -1 at low speed (full counter-steer), smoothly transitioning across transitionKmh
    /// to 0 (Off) or +0.35 (SameDirection).
    /// </summary>
    public static float ComputeRearSteerSpeedFactor(float speedKmh, float transitionKmh, RearSteerHighSpeedMode highSpeedMode)
    {
        float lowBound = MathF.Max(5f, transitionKmh * 0.5f);
        float highBound = MathF.Max(lowBound + 5f, transitionKmh * 1.25f);
        float highTarget = highSpeedMode == RearSteerHighSpeedMode.SameDirection ? 0.35f : 0f;
        if (speedKmh <= lowBound) return -1f;
        if (speedKmh >= highBound) return highTarget;
        float t = (speedKmh - lowBound) / (highBound - lowBound);
        float s = t * t * (3f - 2f * t);
        return -1f + (highTarget + 1f) * s;
    }

    /// <summary>
    /// Computes individual wheel damage (0..1) from an impact or explosion at distance distMeters
    /// from the wheel center, given projectile calibre in mm and whether the event is explosive.
    /// </summary>
    public static float ComputeWheelImpactDamage(float distMeters, float wheelRadius, int calibreMm, bool isExplosion)
    {
        if (!float.IsFinite(distMeters) || distMeters < 0f || wheelRadius <= 0.05f)
            return 0f;

        float directReach = wheelRadius * 1.25f + 0.18f;
        if (!isExplosion)
        {
            if (distMeters > directReach) return 0f;
            if (calibreMm >= 55) return 1.0f;
            if (calibreMm >= 25) return 0.65f;
            return 0.35f;
        }

        // Explosion splash radius scales modestly with calibre so HE blasts near a wheel damage nearby tyres only.
        float blastRadius = Math.Clamp(directReach + 0.55f + calibreMm * 0.012f, directReach + 0.4f, 3.2f);
        if (distMeters > blastRadius) return 0f;
        float falloff = 1f - (distMeters / blastRadius);
        float baseDmg = calibreMm >= 75 ? 1.25f : (calibreMm >= 40 ? 0.90f : 0.60f);
        return Math.Clamp(baseDmg * falloff * 1.4f, 0f, 1.0f);
    }

    /// <summary>
    /// Computes the exact swing-arm endpoint in the vehicle (Y, Z) plane for a requested vertical offset.
    /// Keeps the wheel hub strictly attached to the physical swing-arm arc and respects down/up travel limits.
    /// </summary>
    public static (float clampedY, float arcZ) SolveSwingArmArc(
        float restArmY,
        float restArmZ,
        float requestedDeltaY,
        float maxDownTravel,
        float maxUpTravel)
    {
        float armLenSq = restArmY * restArmY + restArmZ * restArmZ;
        if (armLenSq < 1e-4f)
            return (restArmY + Math.Clamp(requestedDeltaY, -maxDownTravel, maxUpTravel), restArmZ);

        float armLen = MathF.Sqrt(armLenSq);
        float maxAbsY = armLen * 0.90f; // Never exceed physical arm reach (keeps at least ~25° from dead vertical).
        float clampedDelta = Math.Clamp(requestedDeltaY, -MathF.Abs(maxDownTravel), MathF.Abs(maxUpTravel));
        float newY = Math.Clamp(restArmY + clampedDelta, -maxAbsY, maxAbsY);
        float zSign = restArmZ >= 0f ? 1f : -1f;
        float newZ = zSign * MathF.Sqrt(MathF.Max(0f, armLenSq - newY * newY));
        return (newY, newZ);
    }

    /// <summary>
    /// Self-calibrating spring-damper suspension step per wheel.
    /// Guarantees consistent ride height regardless of vehicle mass (from 4t scouts to 40t 8x8s),
    /// and supports an optional rideHeightOffset (positive raises the hull, e.g. from HPS).
    /// </summary>
    public static SuspensionResult ComputeSuspension(
        float vehicleMassKg,
        int totalWheels,
        float wheelRadius,
        float restTopOffset,
        float maxDroop,
        float rayHitDistance,
        float previousCompression,
        bool wasGrounded,
        float stiffnessScale,
        float dampingScale,
        float dt,
        float rideHeightOffset = 0f)
    {
        float clampedRide = Math.Clamp(rideHeightOffset, -0.35f, 0.35f);
        float effectiveDroop = MathF.Max(0.08f, maxDroop + clampedRide);
        float totalTravel = MathF.Max(0.14f, restTopOffset + effectiveDroop);
        float restCompression = Math.Clamp(effectiveDroop, 0.08f, totalTravel * 0.62f);

        // Wheel hub distance below suspension top anchor = hitDistance - wheelRadius.
        float hubBelowTop = rayHitDistance - wheelRadius;
        float maxHubBelowTop = restTopOffset + effectiveDroop;
        float compression = Math.Clamp(maxHubBelowTop - hubBelowTop, 0f, totalTravel);
        float compVel = wasGrounded && dt > 1e-4f
            ? Math.Clamp((compression - previousCompression) / dt, -5f, 5f)
            : 0f;

        float wheelShareMass = MathF.Max(150f, vehicleMassKg / MathF.Max(2, totalWheels));
        float staticWeight = wheelShareMass * Gravity;
        float baseRestComp = Math.Clamp(maxDroop, 0.08f, MathF.Max(0.14f, restTopOffset + maxDroop) * 0.62f);
        float springK = (staticWeight / baseRestComp) * Math.Clamp(stiffnessScale, WheeledSettings.MinSuspension, WheeledSettings.MaxSuspension);
        float criticalDamping = 2f * MathF.Sqrt(springK * wheelShareMass);
        float damperC = criticalDamping * 0.58f * Math.Clamp(dampingScale, WheeledSettings.MinSuspension, WheeledSettings.MaxSuspension);

        // Gentle progressive bump-stop near full compression.
        float bumpRatio = Math.Clamp((compression - totalTravel * 0.82f) / MathF.Max(0.02f, totalTravel * 0.18f), 0f, 1f);
        float springForce = springK * compression * (1f + 1.1f * bumpRatio * bumpRatio);
        float damperForce = damperC * compVel;

        float maxSafeForce = staticWeight * 2.6f;
        float normalForce = Math.Clamp(springForce + damperForce, 0f, maxSafeForce);

        // Visual wheel vertical offset relative to its design rest position in vehicle local space:
        // When HPS raises the hull by +h, the wheel sits -h lower relative to the hull at equilibrium.
        float verticalOffset = (compression - restCompression) - clampedRide;

        return new SuspensionResult(normalForce, compression, compVel, verticalOffset);
    }

    /// <summary>
    /// Computes an anti-roll bar differential normal-force adjustment between left and right wheels on an axle.
    /// Positive return value adds force to the left wheel and subtracts it from the right wheel.
    /// </summary>
    public static float ComputeAntiRollForce(
        float leftCompression,
        float rightCompression,
        float vehicleMassKg,
        int totalWheels,
        float restCompression,
        float stiffnessScale)
    {
        float wheelShareMass = MathF.Max(150f, vehicleMassKg / MathF.Max(2, totalWheels));
        float springK = (wheelShareMass * Gravity / MathF.Max(0.08f, restCompression)) * stiffnessScale;
        float diff = leftCompression - rightCompression;
        float maxRollForce = wheelShareMass * Gravity * 1.1f;
        return Math.Clamp(diff * springK * 0.38f, -maxRollForce, maxRollForce);
    }

    /// <summary>
    /// Computes longitudinal and lateral tire forces with an elliptical Kamm friction limit:
    /// strong longitudinal traction/braking, realistic off-road lateral cornering grip that stays planted
    /// in normal turns and slopes but breaks away progressively instead of flipping tall armoured cars.
    /// </summary>
    public static TireForceResult ComputeTireForces(
        float normalForce,
        float wheelShareMassKg,
        float vLong,
        float vLat,
        float driveForceRequested,
        float brakeForceRequested,
        bool parkingBrake,
        float tireGripScale,
        float dt)
    {
        if (normalForce <= 1f || !float.IsFinite(normalForce) || dt <= 1e-5f)
            return new TireForceResult(0f, 0f, 0f, 0f, false);

        float grip = Math.Clamp(tireGripScale, WheeledSettings.MinGrip, WheeledSettings.MaxGrip);
        float maxLongForce = BaseLongitudinalFriction * grip * normalForce;
        float maxLatForce = BaseLateralFriction * grip * normalForce;

        // Lateral grip: planted at low speed (slopes/crawling) and progressive off-road slip curve at speed.
        float absLong = MathF.Abs(vLong);
        float slipAngle = MathF.Atan2(vLat, MathF.Max(absLong, 0.8f));
        float tauLat = 0.11f;
        float stopLatForce = -(vLat * wheelShareMassKg) / MathF.Max(dt, tauLat);
        // Progressive off-road tyre cornering curve (saturates smoothly around ~18-22 deg slip angle):
        float slipAngleForce = -maxLatForce * MathF.Tanh(slipAngle * 3.2f);
        float speedBlend = Math.Clamp((absLong - 1.2f) / 4.0f, 0f, 1f);
        float desiredLat = stopLatForce * (1f - speedBlend) + slipAngleForce * speedBlend;
        float maxLatSingleTick = (MathF.Abs(vLat) * wheelShareMassKg) / MathF.Max(dt, 0.015f) + normalForce * 0.12f;
        desiredLat = Math.Clamp(desiredLat, -maxLatSingleTick, maxLatSingleTick);

        // Longitudinal force: drive, rolling resistance, service brake, or parking brake.
        float desiredLong;
        if (parkingBrake)
        {
            float holdForce = -(vLong * wheelShareMassKg) / MathF.Max(dt, 0.07f);
            desiredLong = Math.Clamp(holdForce, -maxLongForce, maxLongForce);
        }
        else if (brakeForceRequested > 1f)
        {
            float stopForce = -(vLong * wheelShareMassKg) / MathF.Max(dt, 0.06f);
            float maxBrake = MathF.Min(brakeForceRequested, maxLongForce);
            desiredLong = Math.Clamp(stopForce, -maxBrake, maxBrake);
        }
        else
        {
            float rollingRes = 0f;
            if (absLong > 0.05f)
            {
                float rrMag = RollingResistanceCoeff * normalForce;
                float rrTickCap = (absLong * wheelShareMassKg) / MathF.Max(dt, 0.06f);
                rollingRes = -MathF.Sign(vLong) * MathF.Min(rrMag, rrTickCap);
            }
            desiredLong = driveForceRequested + rollingRes;
        }

        // Elliptical Kamm friction limit: (F_long / maxLong)^2 + (F_lat / maxLat)^2 <= 1.
        float normLong = desiredLong / MathF.Max(1f, maxLongForce);
        float normLat = desiredLat / MathF.Max(1f, maxLatForce);
        float ellipseMag = MathF.Sqrt(normLong * normLong + normLat * normLat);
        bool saturated = false;
        if (ellipseMag > 1f)
        {
            float scale = 1f / ellipseMag;
            desiredLong *= scale;
            desiredLat *= scale;
            saturated = true;
        }

        float slipRatio = absLong > 0.2f ? Math.Clamp(desiredLong / MathF.Max(1f, maxLongForce), -1f, 1f) : 0f;
        return new TireForceResult(desiredLong, desiredLat, slipRatio, slipAngle, saturated);
    }
}
