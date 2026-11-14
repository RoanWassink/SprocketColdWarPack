using System;
using System.Linq;
using System.Text.Json.Nodes;
using Vector3 = System.Numerics.Vector3;

namespace SprocketExpandedBallistics;

// Focused production-helper checks. The coordinator links this file into the
// main suite; these are managed checks, not live game evidence.
public static class AtgmFlightChecks
{
    public static void Run(Action<bool,string> check)
    {
        if (check is null) throw new ArgumentNullException(nameof(check));

        void Check(string name, Func<bool> test)
        {
            try { check(test(), name); }
            catch (Exception ex) { check(false, name + ": " + ex.GetType().Name + ": " + ex.Message); }
        }

        Check("legacy profile leaves extended flight tuning absent", () =>
        {
            var profile = ParseOne(ProfileJson());
            return profile.Atgm is { FlightProfile: null, GuidanceMode: "sight" };
        });

        Check("LOS-lead settings parse with finite values", () =>
        {
            var profile = ParseOne(ProfileJson(configure: p =>
            {
                p["guidanceLaw"] = "losLead";
                p["leadDistanceM"] = 42d;
                p["spiralAmplitudeM"] = .1d;
                p["spiralFrequencyHz"] = 1.2d;
                p["speedCalibration"] = "fixed";
                p["initialElevationDegrees"] = .5d;
            }));
            var tuning = profile.Atgm?.FlightProfile;
            return tuning is not null &&
                tuning.GuidanceLaw == "losLead" && Math.Abs(tuning.LeadDistanceM - 42) < 1e-9 &&
                Math.Abs(tuning.SpiralAmplitudeM - .1) < 1e-9 && tuning.SpeedCalibration == "fixed" &&
                Math.Abs(tuning.InitialElevationDegrees - .5) < 1e-9;
        });

        Check("unknown, duplicate and malformed settings fail closed", () =>
        {
            var unknown = With(ProfileJson(), p => p["mysterySetting"] = 1);
            var duplicate = ProfileJson().Replace("\"id\":\"test_atgm\"", "\"id\":\"test_atgm\",\"id\":\"test_atgm\"");
            var badChoice = With(ProfileJson(), p => p["guidanceLaw"] = "curve");
            var badNumber = With(ProfileJson(), p => p["leadDistanceM"] = "far");
            var negative = With(ProfileJson(), p => p["disturbanceAmplitudeM"] = -1);
            var badElevation = With(ProfileJson(), p => p["initialElevationDegrees"] = 46);
            var badHump = With(ProfileJson("gun", "atgm_gun", p => p["guidanceMode"] = "sight"), p =>
            {
                p["rangePattern"] = "hump";
                p["humpRangeMinM"] = 900;
                p["humpRangeMaxM"] = 200;
            });
            var humpOnLauncher = With(ProfileJson(), p => p["rangePattern"] = "hump");
            return ThrowsFormat(() => ShellProfiles.Parse(unknown)) &&
                ThrowsAny(() => ShellProfiles.Parse("{not json")) &&
                ThrowsAny(() => ShellProfiles.Parse(duplicate)) &&
                ThrowsFormat(() => ShellProfiles.Parse(badChoice)) &&
                ThrowsFormat(() => ShellProfiles.Parse(badNumber)) &&
                ThrowsFormat(() => ShellProfiles.Parse(negative)) &&
                ThrowsFormat(() => ShellProfiles.Parse(badElevation)) &&
                ThrowsFormat(() => ShellProfiles.Parse(badHump)) &&
                ThrowsFormat(() => ShellProfiles.Parse(humpOnLauncher));
        });

        Check("LOS-lead trajectory converges onto the sight ray", () =>
        {
            var settings = new AtgmSettings(FlightSpeed: 200, MaxTurnRate: 40, MaximumFlightTime: 25,
                GuidanceDelay: 0, GuidanceMode: "sight");
            var origin = Vector3.Zero;
            var axis = Vector3.UnitZ;
            var position = new Vector3(8, 5, 30);
            var heading = Vector3.UnitZ;
            var initialOffset = Math.Sqrt(8d * 8d + 5d * 5d);
            const double dt = 1d / 60d;
            for (var i = 0; i < 600; i++)
            {
                if (!AtgmFlightDynamics.TryAimAlongSightRay(origin, axis, position, 25, out var point)) return false;
                var desired = point - position;
                if (desired.LengthSquared() < 1e-6f) break;
                var velocity = AtgmGuidance.Step(heading, desired, settings, dt);
                position += velocity * (float)dt;
                heading = Vector3.Normalize(velocity);
            }
            var offAxis = Math.Sqrt(position.X * position.X + position.Y * position.Y);
            return position.Z > 100 && offAxis < initialOffset * .25;
        });

        Check("turn limit is respected across delta-time values", () =>
        {
            var settings = new AtgmSettings(FlightSpeed: 200, MaxTurnRate: 20, MaximumFlightTime: 25,
                GuidanceDelay: 0, GuidanceMode: "sight");
            var target = Vector3.Normalize(new Vector3(1, 0, 1));
            foreach (var dt in new[] { .001, 1d / 60, .1, .5 })
            {
                var velocity = AtgmGuidance.Step(Vector3.UnitZ, target, settings, dt);
                var dot = Math.Clamp(Vector3.Dot(Vector3.Normalize(velocity), Vector3.UnitZ), -1f, 1f);
                var turned = Math.Acos(dot) * 180 / Math.PI;
                if (!float.IsFinite(velocity.X) || !float.IsFinite(velocity.Y) || !float.IsFinite(velocity.Z) ||
                    turned > 20 * dt + 1e-3) return false;
            }
            var backwards = AtgmGuidance.Step(Vector3.UnitZ, -Vector3.UnitZ, settings, .5);
            return Vector3.Dot(Vector3.Normalize(backwards), Vector3.UnitZ) > .999f;
        });

        Check("range bands choose direct, normal and hump safely", () =>
        {
            var hump = new AtgmFlightProfile(RangePattern: "hump", DirectRangeBelowM: 120,
                HumpRangeMinM: 180, HumpRangeMaxM: 1600, HumpTerminalDistanceM: 90,
                HumpRampDistanceM: 35, ShortRangeThresholdM: 160, ShortRangeGuidanceDelay: .05);
            return AtgmFlightDynamics.SelectPattern(hump, 80) == AtgmRangePattern.Direct &&
                AtgmFlightDynamics.SelectPattern(hump, 900) == AtgmRangePattern.Hump &&
                AtgmFlightDynamics.SelectPattern(hump, 3000) == AtgmRangePattern.Normal &&
                AtgmFlightDynamics.SelectPattern(hump, null) == AtgmRangePattern.Direct &&
                AtgmFlightDynamics.SelectPattern(hump, double.NaN) == AtgmRangePattern.Direct &&
                AtgmFlightDynamics.SelectPattern(AtgmFlightProfile.Legacy, 900) == AtgmRangePattern.Normal &&
                AtgmFlightDynamics.SelectPattern(null, 900) == AtgmRangePattern.Normal &&
                Math.Abs(AtgmFlightDynamics.EffectiveGuidanceDelay(.25, null, 100) - .25) < 1e-9 &&
                Math.Abs(AtgmFlightDynamics.EffectiveGuidanceDelay(.25, hump, 100) - .05) < 1e-9 &&
                Math.Abs(AtgmFlightDynamics.EffectiveGuidanceDelay(.25, hump, 900) - .25) < 1e-9 &&
                Math.Abs(AtgmFlightDynamics.EffectiveGuidanceDelay(.25, hump, null) - .05) < 1e-9;
        });

        Check("default example presets use measured calibration", () =>
        {
            var profiles = ShellProfiles.Parse(ReleaseProfiles.Defaults());
            var tow = profiles.FirstOrDefault(p => p.Id == "atgm_saclos_direct_v1")?.Atgm;
            var mclos = profiles.FirstOrDefault(p => p.Id == "atgm_mclos_manual_v1")?.Atgm;
            var kobra = profiles.FirstOrDefault(p => p.Id == "atgm_gun_hump_v1")?.Atgm;
            var towTuning = tow?.FlightProfile;
            var mclosTuning = mclos?.FlightProfile;
            var kobraTuning = kobra?.FlightProfile;
            return tow is not null && mclos is not null && kobra is not null &&
                towTuning is not null && mclosTuning is not null && kobraTuning is not null &&
                Math.Abs(tow.FlightSpeed - 278) < 1e-9 && Math.Abs(tow.MaxTurnRate - 6.87549354) < 1e-9 &&
                Math.Abs(towTuning.LeadDistanceM - 30) < 1e-9 && towTuning.SpeedCalibration == "motorSmooth" &&
                tow.MaximumFlightTime >= 3750d / 278 &&
                Math.Abs(mclos.FlightSpeed - 115) < 1e-9 && Math.Abs(mclos.MaxTurnRate - 11.45915590) < 1e-9 &&
                mclosTuning.SpeedCalibration == "motorSmooth" && mclos.MaximumFlightTime >= 3000d / 115 &&
                Math.Abs(kobra.FlightSpeed - 375) < 1e-9 && Math.Abs(kobra.MaxTurnRate - 5.72957795) < 1e-9 &&
                Math.Abs(kobra.GuidanceDelay - 1) < 1e-9 &&
                Math.Abs(kobraTuning.LeadDistanceM - 200) < 1e-9 &&
                kobraTuning.ShortRangeThresholdM == 1000 && kobraTuning.DirectRangeBelowM == 1000 &&
                kobraTuning.HumpRangeMinM == 1920 && kobraTuning.HumpRangeMaxM == 4000 &&
                kobraTuning.HumpHeightM == 4 && kobraTuning.HumpTerminalDistanceM == 700 &&
                Math.Abs(kobraTuning.InitialElevationDegrees - .5) < 1e-9 &&
                kobraTuning.SpeedCalibration == "motorSmooth";
        });

        Check("hump height descends to LOS before the snapshotted range", () =>
        {
            const double height = 1.2, range = 1000, terminal = 90, ramp = 35;
            var start = AtgmFlightDynamics.HumpOffset(height, range, 0, terminal, ramp);
            var middle = AtgmFlightDynamics.HumpOffset(height, range, 400, terminal, ramp);
            var near = AtgmFlightDynamics.HumpOffset(height, range, 960, terminal, ramp);
            var end = AtgmFlightDynamics.HumpOffset(height, range, 1000, terminal, ramp);
            var beyond = AtgmFlightDynamics.HumpOffset(height, range, 1200, terminal, ramp);
            return start == 0 && Math.Abs(middle - height) < 1e-9 &&
                near > 0 && near < middle && end == 0 && beyond == 0 &&
                middle <= height + 1e-9;
        });

        Check("disturbances are deterministic, bounded and zero by default", () =>
        {
            var tuning = new AtgmFlightProfile(SpiralAmplitudeM: .2, SpiralFrequencyHz: 1.3,
                DisturbanceAmplitudeM: .1, DisturbanceFrequencyHz: .7);
            var first = AtgmFlightDynamics.Disturbance(tuning, 2.0, new AtgmFlightState(12345), Vector3.UnitZ);
            var same = AtgmFlightDynamics.Disturbance(tuning, 2.0, new AtgmFlightState(12345), Vector3.UnitZ);
            var other = AtgmFlightDynamics.Disturbance(tuning, 2.0, new AtgmFlightState(54321), Vector3.UnitZ);
            var vertical = AtgmFlightDynamics.Disturbance(tuning, 2.0, new AtgmFlightState(12345), Vector3.UnitY);
            var zero = AtgmFlightDynamics.Disturbance(new AtgmFlightProfile(), 2.0, new AtgmFlightState(1), Vector3.UnitZ);
            var zeroHeading = AtgmFlightDynamics.DisturbDirection(Vector3.UnitZ, new AtgmFlightProfile(), .75,
                new AtgmFlightState(1));
            var disturbedHeading = AtgmFlightDynamics.DisturbDirection(Vector3.UnitZ, tuning, 2.0,
                new AtgmFlightState(12345));
            return Vector3.Distance(first, same) < 1e-6f &&
                Vector3.Distance(first, other) > 1e-5f &&
                first.Length() <= .3f + 1e-5f && vertical.Length() <= .3f + 1e-5f &&
                Math.Abs(Vector3.Dot(first, Vector3.UnitZ)) < 1e-5f &&
                Math.Abs(Vector3.Dot(vertical, Vector3.UnitY)) < 1e-5f &&
                zero == Vector3.Zero && zeroHeading == Vector3.UnitZ &&
                Math.Abs(disturbedHeading.Length() - 1) < 1e-5f &&
                Vector3.Dot(disturbedHeading, Vector3.UnitZ) > .99f;
        });

        Check("spiral spans two perpendicular dimensions with constant radius", () =>
        {
            var tuning = new AtgmFlightProfile(SpiralAmplitudeM: .25, SpiralFrequencyHz: .5);
            var state = new AtgmFlightState(98765);
            var period = 1 / tuning.SpiralFrequencyHz;
            var first = AtgmFlightDynamics.Disturbance(tuning, .3, state, Vector3.UnitZ);
            var quarter = AtgmFlightDynamics.Disturbance(tuning, .3 + period / 4, state, Vector3.UnitZ);
            var half = AtgmFlightDynamics.Disturbance(tuning, .3 + period / 2, state, Vector3.UnitZ);
            var cross = Vector3.Cross(first, quarter).Length();
            return Math.Abs(first.Length() - .25f) < 1e-4f &&
                Math.Abs(quarter.Length() - .25f) < 1e-4f &&
                Math.Abs(half.Length() - .25f) < 1e-4f &&
                Math.Abs(Vector3.Dot(first, Vector3.UnitZ)) < 1e-5f &&
                Math.Abs(Vector3.Dot(quarter, Vector3.UnitZ)) < 1e-5f &&
                Math.Abs(Vector3.Dot(first, quarter)) < 1e-4f &&
                cross / (first.Length() * quarter.Length()) > .999f && Vector3.Distance(first, -half) < 1e-4f;
        });

        Check("manual command is bounded, target-free and legacy-exact at zero", () =>
        {
            var settings = new AtgmSettings(FlightSpeed: 200, MaxTurnRate: 20, MaximumFlightTime: 25,
                GuidanceDelay: 0, GuidanceMode: "keyboard");
            const double dt = 1d / 60d;
            var legacyTuning = new AtgmFlightProfile();
            var tuning = new AtgmFlightProfile(SpiralAmplitudeM: .3, SpiralFrequencyHz: 1,
                DisturbanceAmplitudeM: .2, DisturbanceFrequencyHz: .8);
            var legacy = AtgmFlightDynamics.ManualCommand(Vector3.UnitZ, 0, 0, settings, legacyTuning,
                .7, new AtgmFlightState(5), dt, true);
            var keyboard = AtgmGuidance.KeyboardStep(Vector3.UnitZ, 0, 0, settings, dt);
            var active = AtgmFlightDynamics.ManualCommand(Vector3.UnitZ, 0, 0, settings, tuning,
                .7, new AtgmFlightState(5), dt, true);
            var inactive = AtgmFlightDynamics.ManualCommand(Vector3.UnitZ, 0, 0, settings, tuning,
                .7, new AtgmFlightState(5), dt, false);
            var fullTurn = AtgmFlightDynamics.ManualCommand(Vector3.UnitZ, 1, 0, settings, tuning,
                .7, new AtgmFlightState(5), dt, true);
            if (legacy is not { } legacyVelocity || active is not { } activeVelocity ||
                fullTurn is not { } turned) return false;
            var turnedDegrees = Math.Acos(Math.Clamp(Vector3.Dot(Vector3.Normalize(turned), Vector3.UnitZ), -1f, 1f)) * 180 / Math.PI;
            return Vector3.Distance(legacyVelocity, keyboard) < 1e-6f &&
                Finite(activeVelocity) && Math.Abs(activeVelocity.Length() - 200) < 1e-3f &&
                Vector3.Dot(Vector3.Normalize(activeVelocity), Vector3.UnitZ) > .99f &&
                inactive is null && Finite(turned) && turnedDegrees <= 20 * dt + 1e-4;
        });

        Check("held MCLOS input retains disturbance under one command turn cap", () =>
        {
            var settings=new AtgmSettings(FlightSpeed:115,MaxTurnRate:11.4591559,GuidanceMode:"keyboard");
            var tuning=new AtgmFlightProfile(SpiralAmplitudeM:.25,SpiralFrequencyHz:2,DisturbanceAmplitudeM:.1);
            var varied=false;
            foreach(var dt in new[]{1d/30,1d/60,1d/120})
            for(var sample=0;sample<30;sample++)
            {
                var old=AtgmGuidance.KeyboardStep(Vector3.UnitZ,1,0,settings,dt);
                var command=AtgmFlightDynamics.ManualCommand(Vector3.UnitZ,1,0,settings,tuning,sample*.07,new AtgmFlightState(9),dt,true);
                if(command is not {} velocity)return false;
                var unit=Vector3.Normalize(velocity);
                var angle=Math.Atan2(Vector3.Cross(Vector3.UnitZ,unit).Length(),Vector3.Dot(Vector3.UnitZ,unit));
                if(angle>settings.MaxTurnRate*Math.PI/180*dt+1e-6)return false;
                varied|=Vector3.Distance(old,velocity)>1e-5;
            }
            return varied;
        });

        Check("moving disturbed sight target never adds a second heading turn", () =>
        {
            var settings=new AtgmSettings(FlightSpeed:278,MaxTurnRate:6.87549354,GuidanceMode:"sight");
            var tuning=new AtgmFlightProfile(SpiralAmplitudeM:2,DisturbanceAmplitudeM:2);
            foreach(var dt in new[]{1d/30,1d/60,1d/120})
            {
                var heading=Vector3.UnitZ;var state=new AtgmFlightState(44);
                for(var frame=0;frame<300;frame++)
                {
                    var target=new Vector3((float)Math.Sin(frame*.17)*100,40,50);
                    target+=AtgmFlightDynamics.Disturbance(tuning,frame*dt,state,target);
                    var next=Vector3.Normalize(AtgmGuidance.Step(heading,target,settings,dt));
                    var angle=Math.Atan2(Vector3.Cross(heading,next).Length(),Math.Clamp(Vector3.Dot(heading,next),-1f,1f));
                    if(angle>settings.MaxTurnRate*Math.PI/180*dt+1e-6)return false;
                    heading=next;
                }
            }
            return true;
        });

        Check("hump progress stays in the launch frame and never reactivates", () =>
        {
            var launch=new Vector3(100,2,200);
            var progress=AtgmFlightDynamics.RangeProgress(launch,launch+new Vector3(0,0,1800),0);
            var offset=AtgmFlightDynamics.HumpOffset(4,2000,progress,700,35);
            var terminal=AtgmFlightDynamics.RangeProgress(launch,launch+new Vector3(0,0,2100),progress);
            var returned=AtgmFlightDynamics.RangeProgress(launch,launch+new Vector3(0,0,1700),terminal);
            return progress==1800 && offset>0 && offset<4 && terminal==2100 && returned==2100 &&
                AtgmFlightDynamics.HumpOffset(4,2000,returned,700,35)==0 &&
                AtgmFlightDynamics.RangeProgress(launch,new Vector3(float.NaN,0,0),returned)==returned;
        });

        Check("initial elevation obeys lockout, direct bypass and turn cap", () =>
        {
            var launch = Vector3.UnitZ;
            var current = launch;
            const double rate = 5.72957795, dt = 1d / 60d, elevation = .5;
            var first = AtgmFlightDynamics.InitialElevationHeading(launch, elevation, rate, dt, current, true, false);
            var firstDegrees = Math.Acos(Math.Clamp(Vector3.Dot(Vector3.Normalize(first), launch), -1f, 1f)) * 180 / Math.PI;
            for (var i = 0; i < 120; i++)
                current = AtgmFlightDynamics.InitialElevationHeading(launch, elevation, rate, dt, current, true, false);
            var settledDegrees = Math.Acos(Math.Clamp(Vector3.Dot(Vector3.Normalize(current), launch), -1f, 1f)) * 180 / Math.PI;
            var bypass = AtgmFlightDynamics.InitialElevationHeading(launch, elevation, rate, dt, current, true, true);
            var afterLockout = AtgmFlightDynamics.InitialElevationHeading(launch, elevation, rate, dt, current, false, false);
            var zero = AtgmFlightDynamics.InitialElevationHeading(launch, 0, rate, dt, launch, true, false);
            var hump = new AtgmFlightProfile(RangePattern: "hump", DirectRangeBelowM: 1000,
                HumpRangeMinM: 1920, HumpRangeMaxM: 4000, InitialElevationDegrees: elevation);
            var direct = AtgmFlightDynamics.SelectPattern(hump, 500) == AtgmRangePattern.Direct;
            var directHeading = AtgmFlightDynamics.InitialElevationHeading(launch, elevation, rate, dt, launch, true, direct);
            return firstDegrees <= rate * dt + 1e-4 && settledDegrees > .45 && settledDegrees < .55 &&
                bypass == current && afterLockout == current && zero == launch &&
                direct && directHeading == launch;
        });

        Check("zero disturbance leaves a guided command unchanged", () =>
        {
            var settings = new AtgmSettings(FlightSpeed: 200, MaxTurnRate: 20, MaximumFlightTime: 25,
                GuidanceDelay: 0, GuidanceMode: "sight");
            var heading = Vector3.Normalize(new Vector3(.2f, 0, 1));
            var desired = Vector3.UnitZ;
            var baseline = AtgmGuidance.Step(heading, desired, settings, 1d / 60d);
            var offset = AtgmFlightDynamics.Disturbance(new AtgmFlightProfile(), .75,
                new AtgmFlightState(99), desired);
            var adjusted = AtgmGuidance.Step(heading, desired + offset, settings, 1d / 60d);
            return offset == Vector3.Zero && Vector3.Distance(baseline, adjusted) < 1e-6f;
        });

        Check("fixed speed calibration is finite while legacy motor speed is unchanged", () =>
        {
            var settings = new AtgmSettings(FlightSpeed: 240, MaxTurnRate: 20, MaximumFlightTime: 25,
                GuidanceDelay: .2, GuidanceMode: "sight", Acceleration: 100, MotorBurnTime: 4);
            var legacy = AtgmFlightDynamics.FlightSpeed(settings, null, 50, 3);
            var motor = AtgmFlightDynamics.FlightSpeed(settings, new AtgmFlightProfile(), 50, 3);
            var fixedSpeed = AtgmFlightDynamics.FlightSpeed(settings, new AtgmFlightProfile(SpeedCalibration: "fixed"), 50, 30);
            return Math.Abs(legacy - motor) < 1e-9 && Math.Abs(legacy - AtgmGuidance.SpeedAtAge(settings, 50, 3)) < 1e-9 &&
                double.IsFinite(fixedSpeed) && Math.Abs(fixedSpeed - 240) < 1e-9;
        });

        Check("editor set, duplicate and compile retain extended settings", () =>
        {
            var json = ProfileJson("editor_atgm_gun", "atgm_gun", p => p["guidanceMode"] = "sight");
            var draft = new ShellEditorDraft(json);
            draft.Set(0, "guidanceLaw", "losLead");
            draft.Set(0, "leadDistanceM", "42.5");
            draft.Set(0, "rangePattern", "hump");
            draft.Set(0, "speedCalibration", "fixed");
            draft.Set(0, "initialElevationDegrees", "0.5");
            var edited = ParseOne(draft.Serialize());
            if (edited.Atgm?.FlightProfile is not { } tuning ||
                tuning.GuidanceLaw != "losLead" || Math.Abs(tuning.LeadDistanceM - 42.5) > 1e-9 ||
                tuning.RangePattern != "hump" || tuning.SpeedCalibration != "fixed" ||
                Math.Abs(tuning.InitialElevationDegrees - .5) > 1e-9) return false;
            var duplicateIndex = draft.Duplicate(draft.Profiles[0]!.AsObject());
            var duplicated = ShellProfiles.Parse(draft.Serialize())[duplicateIndex];
            return duplicated.Id == "custom_1" && duplicated.Atgm?.FlightProfile is { } copy &&
                Math.Abs(copy.LeadDistanceM - 42.5) < 1e-9 && copy.RangePattern == "hump" &&
                Math.Abs(copy.InitialElevationDegrees - .5) < 1e-9;
        });

        Check("legacy editor roundtrip does not invent extended tuning", () =>
        {
            var json = ProfileJson();
            var original = ParseOne(json);
            var roundtrip = ParseOne(new ShellEditorDraft(json).Serialize());
            return original.Atgm is { FlightProfile: null } && roundtrip.Atgm is { FlightProfile: null } &&
                original.Atgm == roundtrip.Atgm && original.Id == roundtrip.Id;
        });

        Check("tracer policy bounds lifetime, distance, size and rear view", () =>
        {
            var settings = new AtgmTracerSettings(true, .5, .12, 2.5, 5000, 45);
            var near = AtgmTracerPolicy.Evaluate(true, 1, 100, settings, 1);
            var far = AtgmTracerPolicy.Evaluate(true, 1, 1000, settings, 1);
            var edge = AtgmTracerPolicy.Evaluate(true, 1, 4500, settings, 1);
            var front = AtgmTracerPolicy.Evaluate(true, 1, 1000, settings, .55);
            var beyond = AtgmTracerPolicy.Evaluate(true, 1, 10000, settings, 1);
            var old = AtgmTracerPolicy.Evaluate(true, 46, 100, settings, 1);
            var disabled = AtgmTracerPolicy.Evaluate(true, 1, 100, settings with { Enabled = false }, 1);
            var dead = AtgmTracerPolicy.Evaluate(false, 1, 100, settings, 1);
            var rear = AtgmTracerPolicy.RearFactor(new Vector3(0, 0, -10), Vector3.Zero, Vector3.UnitZ);
            var nose = AtgmTracerPolicy.RearFactor(new Vector3(0, 0, 10), Vector3.Zero, Vector3.UnitZ);
            return near.Visible && far.Visible && edge.Visible && near.SizeM >= .12 && far.SizeM >= near.SizeM &&
                far.SizeM <= 2.5 && front.Visible && front.SizeM <= far.SizeM &&
                !beyond.Visible && !old.Visible && !disabled.Visible && !dead.Visible &&
                rear > nose && rear <= 1 && nose >= .45;
        });

        Check("tail flare eligibility ignores command ownership but respects type", () =>
        {
            var settings = new AtgmTracerSettings(true, .5, .12, 2.5, 5000, 45);
            var eligible = AtgmTracerPolicy.FlareEligible(true, true, "sight");
            var manual = AtgmTracerPolicy.FlareEligible(true, true, "keyboard");
            var unguided = AtgmTracerPolicy.FlareEligible(true, true, "none");
            var unpowered = AtgmTracerPolicy.FlareEligible(true, false, "sight");
            var removed = AtgmTracerPolicy.FlareEligible(false, true, "sight");
            var withinRange = AtgmTracerPolicy.Evaluate(true, 10, 4500, settings, 1);
            var tooOld = AtgmTracerPolicy.Evaluate(true, 46, 4500, settings, 1);
            return eligible && manual && !unguided && !unpowered && !removed &&
                withinRange.Visible && !tooOld.Visible;
        });

        Check("extreme finite inputs stay bounded and orthogonal", () =>
        {
            var settings = new AtgmSettings(FlightSpeed: 200, MaxTurnRate: 90, MaximumFlightTime: 25,
                GuidanceDelay: 0, GuidanceMode: "sight");
            var zeroDelta = AtgmGuidance.Step(Vector3.UnitZ, new Vector3(1, 0, 0), settings, 0);
            var vertical = AtgmGuidance.Step(Vector3.UnitY, new Vector3(1, 0, 0), settings, 10);
            var basis = AtgmFlightDynamics.OrthonormalBasis(Vector3.UnitY);
            var aim = AtgmFlightDynamics.TryAimAlongSightRay(new Vector3(0, 0, 1e6f), Vector3.UnitZ,
                new Vector3(0, 0, 1e6f), 5000, out var point);
            var invalidAim = AtgmFlightDynamics.TryAimAlongSightRay(Vector3.Zero, Vector3.Zero,
                Vector3.Zero, 10, out _);
            return Math.Abs(Vector3.Dot(Vector3.Normalize(zeroDelta), Vector3.UnitZ) - 1) < 1e-6f &&
                Finite(vertical) && vertical.Length() > 0 && Math.Abs(vertical.Length() - 200) < 1e-3f &&
                Math.Abs(Vector3.Dot(basis.Right, basis.Up)) < 1e-5f &&
                Math.Abs(basis.Right.Length() - 1) < 1e-5f && Math.Abs(basis.Up.Length() - 1) < 1e-5f &&
                aim && Finite(point) && !invalidAim &&
                !AtgmFlightDynamics.ValidLaunchRange(double.NaN) &&
                !AtgmFlightDynamics.ValidLaunchRange(0) &&
                AtgmFlightDynamics.ValidLaunchRange(5) &&
                !AtgmFlightDynamics.ValidLaunchRange(20001);
        });

        Check("flight identity seeds are stable and missile-specific", () =>
        {
            var first = AtgmFlightDynamics.Seed(7, 12.5f, "atgm_saclos_direct_v1");
            var same = AtgmFlightDynamics.Seed(7, 12.5f, "atgm_saclos_direct_v1");
            var otherId = AtgmFlightDynamics.Seed(8, 12.5f, "atgm_saclos_direct_v1");
            var otherSpawn = AtgmFlightDynamics.Seed(7, 12.6f, "atgm_saclos_direct_v1");
            return first == same && first != otherId && first != otherSpawn;
        });
    }

    private static ShellProfile ParseOne(string json) => ShellProfiles.Parse(json)[0];

    private static string ProfileJson(string id = "test_atgm", string behavior = "atgm",
        Action<JsonObject>? configure = null)
    {
        var profile = new JsonObject
        {
            ["id"] = id,
            ["label"] = "Test ATGM " + id,
            ["behavior"] = behavior,
            ["penetratorDiameterFactor"] = .9d,
            ["penetratorLengthInCalibres"] = 5d,
            ["penetratorDensity"] = 1800d,
            ["velocityEfficiency"] = .1d,
            ["velocityMultiplier"] = 1d,
            ["maximumVelocityFactor"] = 1d,
            ["maximumVelocity"] = 200d,
            ["penetrationQuality"] = .45d,
            ["fragmentDamageMultiplier"] = 1d,
            ["referenceCalibreMm"] = 135d,
            ["chemicalPenetrationMm"] = 600d,
            ["spallMultiplier"] = 1.5d,
            ["coneHalfAngleDegrees"] = 8d,
            ["explosionScale"] = .7d,
            ["secondPlatePenetrationFactor"] = .15d,
            ["airGapLossPerCalibre"] = .35d,
            ["flightSpeed"] = 200d,
            ["maxTurnRate"] = 20d,
            ["maximumFlightTime"] = 25d,
            ["guidanceDelay"] = .25d,
            ["guidanceMode"] = "sight",
            ["launchSpeed"] = 50d,
            ["launchSpeedMode"] = "fixed",
            ["launchSpeedMultiplier"] = 1d,
            ["acceleration"] = 150d,
            ["motorDelay"] = .12d,
            ["motorBurnTime"] = 4d,
            ["coastDeceleration"] = 1d
        };
        configure?.Invoke(profile);
        return new JsonObject { ["schemaVersion"] = 1, ["profiles"] = new JsonArray(profile) }.ToJsonString();
    }

    private static string With(string json, Action<JsonObject> configure)
    {
        var root = JsonNode.Parse(json)!.AsObject();
        configure(root["profiles"]!.AsArray()[0]!.AsObject());
        return root.ToJsonString();
    }

    private static bool ThrowsFormat(Action action)
    {
        try { action(); return false; }
        catch (FormatException) { return true; }
        catch { return false; }
    }

    private static bool ThrowsAny(Action action)
    {
        try { action(); return false; }
        catch { return true; }
    }

    private static bool Finite(Vector3 value) =>
        float.IsFinite(value.X) && float.IsFinite(value.Y) && float.IsFinite(value.Z);
}
