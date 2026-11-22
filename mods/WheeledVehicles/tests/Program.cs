using SprocketWheeledVehicles;

int passed = 0;
int failed = 0;

void Check(bool condition, string name)
{
    if (condition) { passed++; }
    else { failed++; Console.WriteLine($"FAIL: {name}"); }
}

List<WheelGeometryInput> MakeVehicleWheels(float[] axleZs, float halfTrack = 1.25f, float radius = 0.52f)
{
    var list = new List<WheelGeometryInput>();
    foreach (float z in axleZs)
    {
        list.Add(new WheelGeometryInput(-halfTrack, -0.4f, z, radius, isLeft: true));
        list.Add(new WheelGeometryInput(+halfTrack, -0.4f, z, radius, isLeft: false));
    }
    return list;
}

// 1. Settings serialization round-trip (Phase 1 + Phase 2 fields)
{
    var s = new WheeledSettings
    {
        Enabled = true,
        HideTrackBelt = true,
        HideEndWheels = false,
        AckermannEnabled = true,
        MaxSteerAngleDeg = 35.5f,
        TireGrip = 1.25f,
        SuspensionStiffness = 1.15f,
        SuspensionDamping = 0.9f,
        RearSteerEnabled = true,
        RearSteerMaxAngleDeg = 14f,
        RearSteerTransitionKmh = 35f,
        RearSteerHighSpeed = RearSteerHighSpeedMode.SameDirection,
        Axles = new List<AxleConfig>
        {
            new() { Driven = true, SteerMode = AxleSteerMode.Front, SteerScale = 1.0f },
            new() { Driven = false, SteerMode = AxleSteerMode.Front, SteerScale = 0.85f },
            new() { Driven = true, SteerMode = AxleSteerMode.None, SteerScale = 1.0f },
            new() { Driven = true, SteerMode = AxleSteerMode.Rear, SteerScale = 1.0f },
        }
    };
    string raw = s.Serialize();
    var p = WheeledSettings.Parse(raw);
    Check(p.Enabled && p.HideTrackBelt && !p.HideEndWheels && p.AckermannEnabled, "Settings flags round-trip");
    Check(MathF.Abs(p.MaxSteerAngleDeg - 35.5f) < 0.01f, "Settings steer angle round-trip");
    Check(MathF.Abs(p.TireGrip - 1.25f) < 0.01f, "Settings grip round-trip");
    Check(p.RearSteerEnabled && p.RearSteerHighSpeed == RearSteerHighSpeedMode.SameDirection, "Settings rear-steer round-trip");
    Check(p.Axles.Count == 4 && !p.Axles[1].Driven && p.Axles[3].SteerMode == AxleSteerMode.Rear, "Settings axles round-trip");
    Check(p.IsValid(), "Parsed settings valid");
}

// 2. Axle grouping front-to-rear (4x4, 6x6, 8x8, 10x10)
foreach (int axleCount in new[] { 2, 3, 4, 5 })
{
    var zs = Enumerable.Range(0, axleCount).Select(i => -2.0f + i * 1.4f).ToArray(); // Intentionally unsorted rear-to-front
    var wheels = MakeVehicleWheels(zs);
    var axles = WheelPhysicsModel.GroupAxles(wheels);
    Check(axles.Count == axleCount, $"GroupAxles count={axleCount}");
    for (int a = 1; a < axles.Count; a++)
        Check(axles[a - 1].MeanZ > axles[a].MeanZ, $"GroupAxles sorted front-to-rear ({axleCount} axles)");
    Check(MathF.Abs(axles[0].TrackWidth - 2.5f) < 0.01f, $"TrackWidth exact ({axleCount} axles)");
}

// 3. 4x4 Ackermann geometry: right turn -> inside (right) wheel turns sharper than outside (left) wheel
{
    var wheels = MakeVehicleWheels(new[] { +1.8f, -1.8f }, halfTrack: 1.2f);
    var axles = WheelPhysicsModel.GroupAxles(wheels);
    var s = new WheeledSettings { Enabled = true, AckermannEnabled = true, MaxSteerAngleDeg = 32f };
    var angRight = WheelPhysicsModel.ComputeWheelSteerAnglesDeg(wheels, axles, s, steerInput: +1.0f, forwardSpeedMps: 3.0f);

    // Axle 0: wheel 0 is left (-X), wheel 1 is right (+X)
    float leftFront = angRight[0];
    float rightFront = angRight[1];
    Check(leftFront > 20f && rightFront > 20f, "4x4 right turn: both front wheels steer positive (right)");
    Check(rightFront > leftFront + 2.5f, $"4x4 Ackermann: inside wheel ({rightFront:0.0}°) sharper than outside ({leftFront:0.0}°)");
    Check(MathF.Abs(angRight[2]) < 0.001f && MathF.Abs(angRight[3]) < 0.001f, "4x4 rear axle does not steer by default");

    // Symmetry check for left turn:
    var angLeft = WheelPhysicsModel.ComputeWheelSteerAnglesDeg(wheels, axles, s, steerInput: -1.0f, forwardSpeedMps: 3.0f);
    Check(MathF.Abs(angLeft[0] + rightFront) < 0.01f && MathF.Abs(angLeft[1] + leftFront) < 0.01f, "4x4 Ackermann left/right symmetry");
}

// 4. 8x8 dual front steering: 2nd front axle automatically turns less than 1st front axle
{
    var wheels = MakeVehicleWheels(new[] { +2.4f, +0.9f, -0.9f, -2.4f }, halfTrack: 1.3f);
    var axles = WheelPhysicsModel.GroupAxles(wheels);
    var s = new WheeledSettings { Enabled = true, AckermannEnabled = true, MaxSteerAngleDeg = 32f };
    var ang = WheelPhysicsModel.ComputeWheelSteerAnglesDeg(wheels, axles, s, steerInput: +1.0f, forwardSpeedMps: 2.0f);

    float avgAxle0 = 0.5f * (ang[0] + ang[1]);
    float avgAxle1 = 0.5f * (ang[2] + ang[3]);
    Check(avgAxle0 > 25f, "8x8 axle 0 steers strongly");
    Check(avgAxle1 > 10f && avgAxle1 < avgAxle0 - 5f, $"8x8 axle 1 ({avgAxle1:0.0}°) steers proportionally less than axle 0 ({avgAxle0:0.0}°)");
    Check(MathF.Abs(ang[4]) < 0.01f && MathF.Abs(ang[6]) < 0.01f, "8x8 rear axles fixed when rear-steer off");
}

// 5. Rear-wheel steering: counter-steer at low speed, off or same-direction at high speed
{
    var wheels = MakeVehicleWheels(new[] { +2.2f, +0.7f, -0.7f, -2.2f }, halfTrack: 1.25f);
    var axles = WheelPhysicsModel.GroupAxles(wheels);
    var sOff = new WheeledSettings
    {
        Enabled = true,
        AckermannEnabled = true,
        MaxSteerAngleDeg = 32f,
        RearSteerEnabled = true,
        RearSteerMaxAngleDeg = 12f,
        RearSteerTransitionKmh = 30f,
        RearSteerHighSpeed = RearSteerHighSpeedMode.Off
    };

    var lowSpeed = WheelPhysicsModel.ComputeWheelSteerAnglesDeg(wheels, axles, sOff, +1.0f, forwardSpeedMps: 2.0f); // 7.2 km/h
    var highSpeedOff = WheelPhysicsModel.ComputeWheelSteerAnglesDeg(wheels, axles, sOff, +1.0f, forwardSpeedMps: 15.0f); // 54 km/h
    Check(lowSpeed[6] < -3f && lowSpeed[7] < -3f, $"Rear steer counter-steers at low speed ({lowSpeed[6]:0.1}° / {lowSpeed[7]:0.1}°)");
    Check(MathF.Abs(highSpeedOff[6]) < 0.01f && MathF.Abs(highSpeedOff[7]) < 0.01f, "Rear steer turns off above transition speed");

    var sSame = sOff.Copy();
    sSame.RearSteerHighSpeed = RearSteerHighSpeedMode.SameDirection;
    var highSpeedSame = WheelPhysicsModel.ComputeWheelSteerAnglesDeg(wheels, axles, sSame, +1.0f, forwardSpeedMps: 15.0f);
    Check(highSpeedSame[6] > 1.5f && highSpeedSame[7] > 1.5f, $"Rear steer steers same direction at high speed ({highSpeedSame[6]:0.1}°)");
}

// 6. Speed-limited steering tapers smoothly at high speed
{
    float low = WheelPhysicsModel.ComputeSpeedLimitedSteerDeg(32f, 2f);   // 7.2 km/h
    float mid = WheelPhysicsModel.ComputeSpeedLimitedSteerDeg(32f, 8.3f); // 30 km/h
    float high = WheelPhysicsModel.ComputeSpeedLimitedSteerDeg(32f, 25f); // 90 km/h
    Check(MathF.Abs(low - 32f) < 0.01f, "Full steering available at low speed");
    Check(mid > 14f && mid < 25f, $"Mid-speed (30 km/h) steering moderated ({mid:0.1}°)");
    Check(high < 10f && high >= 6f, $"High-speed steering smoothly limited ({high:0.1}°)");
}

// 7. Self-calibrating suspension equilibrium across light (6t) and heavy (32t) vehicles + HPS rideHeightOffset
foreach (var (massKg, wheelCount) in new[] { (6000f, 4), (18000f, 6), (32000f, 8) })
{
    float radius = 0.50f, topOff = 0.22f, droop = 0.26f;
    float restHitDist = topOff + radius;
    var susp = WheelPhysicsModel.ComputeSuspension(massKg, wheelCount, radius, topOff, droop, restHitDist, droop, true, 1.0f, 1.0f, 0.02f);
    float expectedWeightPerWheel = (massKg / wheelCount) * WheelPhysicsModel.Gravity;
    Check(MathF.Abs(susp.NormalForce - expectedWeightPerWheel) < expectedWeightPerWheel * 0.02f,
        $"Suspension equilibrium matches weight for {massKg}kg/{wheelCount}w ({susp.NormalForce:0}N vs {expectedWeightPerWheel:0}N)");
    Check(MathF.Abs(susp.VerticalWheelOffset) < 0.005f, $"Visual wheel offset ~0 at rest for {massKg}kg");

    // HPS +0.10m hull raise increases upward spring force at same hull height
    var raised = WheelPhysicsModel.ComputeSuspension(massKg, wheelCount, radius, topOff, droop, restHitDist, droop, true, 1.0f, 1.0f, 0.02f, rideHeightOffset: 0.10f);
    Check(raised.NormalForce > susp.NormalForce * 1.15f, $"HPS +100mm raise increases wheel push force for {massKg}kg");
}

// 8. Elliptical off-road tire grip & Kamm friction limit: resists sideways slide & caps total force
{
    float normalF = 30000f;
    float shareMass = 3000f;
    var latOnly = WheelPhysicsModel.ComputeTireForces(normalF, shareMass, vLong: 10f, vLat: 2.0f, 0f, 0f, false, 1.0f, 0.02f);
    Check(latOnly.LateralForce < -4000f && MathF.Abs(latOnly.LateralForce) <= WheelPhysicsModel.BaseLateralFriction * normalF + 5f,
        $"Lateral grip opposes sideways slide within off-road limit ({latOnly.LateralForce:0}N)");

    // Extreme combined drive + lateral must stay inside elliptical Kamm limit
    var combined = WheelPhysicsModel.ComputeTireForces(normalF, shareMass, vLong: 15f, vLat: 8.0f, driveForceRequested: 80000f, 0f, false, 1.0f, 0.02f);
    float normL = combined.LongitudinalForce / (WheelPhysicsModel.BaseLongitudinalFriction * normalF);
    float normS = combined.LateralForce / (WheelPhysicsModel.BaseLateralFriction * normalF);
    float ellipse = MathF.Sqrt(normL * normL + normS * normS);
    Check(combined.Saturated && ellipse <= 1.01f, $"Elliptical Kamm friction limit enforced (r={ellipse:0.000} <= 1.0)");

    // Parking brake resists rolling forward
    var park = WheelPhysicsModel.ComputeTireForces(normalF, shareMass, vLong: 0.4f, vLat: 0f, 0f, 0f, parkingBrake: true, 1.0f, 0.02f);
    Check(park.LongitudinalForce < -4000f, $"Parking brake holds vehicle against creep ({park.LongitudinalForce:0}N)");
}

// 9. Presets & layout description (6x4, 8x4, 10x10)
{
    var s = new WheeledSettings { Enabled = true };
    s.ApplyPreset(3, new[] { false, true, true }, new[] { AxleSteerMode.Front, AxleSteerMode.None, AxleSteerMode.None });
    Check(s.DescribeDriveLayout(3) == "6x4" && !s.HasAnyRearSteer(3), "6x4 preset layout");

    s.ApplyPreset(5, new[] { true, true, true, true, true }, new[] { AxleSteerMode.Front, AxleSteerMode.Front, AxleSteerMode.None, AxleSteerMode.None, AxleSteerMode.Rear });
    Check(s.DescribeDriveLayout(5) == "10x10" && s.HasAnyRearSteer(5), "10x10 preset with rear steer");
}

// 10. Swing-arm arc & down-travel limit: hub stays strictly attached to arm length even on extreme droop
{
    float restY = -0.18f, restZ = -0.42f;
    float restLen = MathF.Sqrt(restY * restY + restZ * restZ);
    // Extreme downward droop request (-0.80m) must be clamped to maxDownTravel (0.16m) AND preserve exact arm length
    var (cy, cz) = WheelPhysicsModel.SolveSwingArmArc(restY, restZ, requestedDeltaY: -0.80f, maxDownTravel: 0.16f, maxUpTravel: 0.22f);
    float newLen = MathF.Sqrt(cy * cy + cz * cz);
    Check(MathF.Abs(newLen - restLen) < 0.001f, $"Swing-arm length preserved on max droop ({newLen:0.000}m == {restLen:0.000}m)");
    Check(cy >= restY - 0.161f && cy <= restY, $"Down-travel clamped to <= 0.16m ({cy - restY:0.000}m)");
    Check(cz < 0f, "Swing-arm preserves rearward direction");
}

// 11. Procedural tire audio loop: finite, normalized, click-free wrap
{
    var samples = TireAudioSynthesizer.GenerateTireRollLoop();
    Check(samples.Length == (int)(TireAudioSynthesizer.SampleRate * TireAudioSynthesizer.DurationSeconds),
        $"Tire audio length={samples.Length}");
    float maxAbs = 0f;
    bool allFinite = true;
    for (int i = 0; i < samples.Length; i++)
    {
        if (!float.IsFinite(samples[i])) { allFinite = false; break; }
        float a = MathF.Abs(samples[i]);
        if (a > maxAbs) maxAbs = a;
    }
    float wrapStep = MathF.Abs(samples[0] - samples[samples.Length - 1]);
    Check(allFinite, "Tire audio samples all finite");
    Check(maxAbs > 0.4f && maxAbs <= 0.60f, $"Tire audio peak normalized ({maxAbs:0.000})");
    Check(wrapStep < 0.04f, $"Tire audio loop boundary seamless (delta={wrapStep:0.000})");
}

// 12. Localized per-wheel impact & explosion damage (only hit wheel affected, other wheels stay intact)
{
    float radius = 0.50f;
    Check(WheelPhysicsModel.ComputeWheelImpactDamage(0.20f, radius, calibreMm: 15, isExplosion: false) > 0.25f,
        "Small-calibre kinetic hit inside wheel reach deals partial tyre damage");
    Check(WheelPhysicsModel.ComputeWheelImpactDamage(0.20f, radius, calibreMm: 30, isExplosion: false) > 0.55f,
        "Autocannon kinetic hit inside wheel reach deals heavy tyre damage");
    Check(WheelPhysicsModel.ComputeWheelImpactDamage(0.20f, radius, calibreMm: 105, isExplosion: false) >= 0.99f,
        "Large-calibre kinetic hit knocks out that single wheel");
    Check(WheelPhysicsModel.ComputeWheelImpactDamage(1.50f, radius, calibreMm: 105, isExplosion: false) == 0f,
        "Kinetic hit on neighbouring axle (1.5m away) leaves this wheel untouched");
    Check(WheelPhysicsModel.ComputeWheelImpactDamage(1.00f, radius, calibreMm: 120, isExplosion: true) > 0.30f,
        "Nearby 120mm HE blast damages adjacent tyre");
    Check(WheelPhysicsModel.ComputeWheelImpactDamage(4.00f, radius, calibreMm: 120, isExplosion: true) == 0f,
        "Distant HE blast (4m away) leaves far wheel untouched");
}

// 13. NaN / degenerate safety
{
    var zero = WheelPhysicsModel.ComputeTireForces(float.NaN, 2000f, 5f, 1f, 1000f, 0f, false, 1f, 0.02f);
    Check(zero.LongitudinalForce == 0f && zero.LateralForce == 0f, "NaN normal force safely yields zero");
}

Console.WriteLine($"WheeledVehicles tests: {passed} passed, {failed} failed.");
return failed == 0 ? 0 : 1;
