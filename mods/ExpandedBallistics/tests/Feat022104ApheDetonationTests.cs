namespace SprocketExpandedBallistics;

/// <summary>
/// 0.22.104–0.22.107: real post-penetration APHE detonation (fuse sensitivity, genuine air-space flight for fuse delay,
/// safe interior detonation on narrow walls or internal stops, uniform 360-degree Fibonacci fragmentation sphere with
/// physical forward shell-velocity addition, true Air-space AP-profile fragments, optional spall.json config).
/// </summary>
internal static class Feat022104ApheDetonationTests
{
    internal static void Run(Action<bool, string> sink)
    {
        int checks = 0, failures = 0;
        void Check(bool ok, string msg) { checks++; if (!ok) failures++; sink(ok, "FEAT104: " + msg); }

        var def = ApheSettings.Default;

        // 1. Fuse sensitivity (RHA-eq threshold)
        Check(!ApheRules.IsFuseArmed(0, def), "1 0 mm RHA does not arm APHE fuse");
        Check(!ApheRules.IsFuseArmed(5, def), "1 5 mm side skirt does not arm APHE fuse");
        Check(!ApheRules.IsFuseArmed(14.9, def), "1 14.9 mm RHA stays below 15 mm fuse threshold");
        Check(ApheRules.IsFuseArmed(15.0, def), "1 15.0 mm RHA arms APHE fuse");
        Check(ApheRules.IsFuseArmed(80.0, def), "1 80 mm main armour arms APHE fuse");
        Check(!ApheRules.IsFuseArmed(double.NaN, def), "1 NaN RHA never arms fuse");

        // 2. Real Air-space decision logic (DecideAirStep & StopDetonationOffset)
        var dOpen = ApheRules.DecideAirStep(0.60, 2.0, nextIsArmour: true, maxExitM: 2.05, minGapMm: 50, def, justArmed: true);
        Check(dOpen.Action == ApheAirAction.Detonate && Math.Abs(dOpen.OffsetMeters - 0.60) < 1e-6 && dOpen.Reason == "fuse-delay",
            $"2 spacious 2.0 m compartment detonates at full 0.60 m fuse delay ({dOpen})");

        var dNear = ApheRules.DecideAirStep(0.60, 0.61, nextIsArmour: true, maxExitM: 0.65, minGapMm: 50, def, justArmed: true);
        Check(dNear.Action == ApheAirAction.Detonate && dNear.OffsetMeters > 0 && dNear.OffsetMeters <= 0.61 - ApheRules.EdgeClearanceM + 1e-9 && dNear.Reason == "near-surface",
            $"2 fuse expiring within 2 cm of surface pulls back safely into clean air ({dNear})");

        var dNarrowFarWall = ApheRules.DecideAirStep(0.60, 0.40, nextIsArmour: true, maxExitM: 0.44, minGapMm: 50, def, justArmed: true);
        Check(dNarrowFarWall.Action == ApheAirAction.Detonate && dNarrowFarWall.OffsetMeters >= 0.20 && dNarrowFarWall.OffsetMeters <= 0.35 && dNarrowFarWall.Reason == "narrow-compartment",
            $"2 narrow 0.40 m turret with far exit wall clamps safely inside ({dNarrowFarWall})");

        var dModuleClose = ApheRules.DecideAirStep(0.60, 0.25, nextIsArmour: false, maxExitM: 1.80, minGapMm: 50, def, justArmed: true);
        Check(dModuleClose.Action == ApheAirAction.Continue && dModuleClose.Reason == "traverse-interior",
            $"2 close crewman/fuel tank at 0.25 m lets intact shell traverse natively ({dModuleClose})");

        var dModuleLast = ApheRules.DecideAirStep(0.60, 0.25, nextIsArmour: false, maxExitM: 0.35, minGapMm: 50, def, justArmed: true);
        Check(dModuleLast.Action == ApheAirAction.Continue && dModuleLast.Reason == "traverse-interior",
            $"2 soft module even near rear is traversed rather than mistaken for far armour wall ({dModuleLast})");

        var dSeam = ApheRules.DecideAirStep(0.60, 0.000319, nextIsArmour: true, maxExitM: 0.04, minGapMm: 50, def, justArmed: true);
        Check(dSeam.Action == ApheAirAction.Continue && dSeam.Reason == "traverse-interior",
            $"2 0.319 mm plate seam continues cleanly without exploding inside seam ({dSeam})");

        var dSpacedInner = ApheRules.DecideAirStep(0.60, 0.30, nextIsArmour: true, maxExitM: 2.20, minGapMm: 50, def, justArmed: true);
        Check(dSpacedInner.Action == ApheAirAction.Continue && dSpacedInner.Reason == "traverse-interior",
            $"2 spaced inner armour plate with hull behind continues cleanly ({dSpacedInner})");

        var dExpired = ApheRules.DecideAirStep(0.0, 0.50, nextIsArmour: true, maxExitM: 0.55, minGapMm: 50, def, justArmed: false);
        Check(dExpired.Action == ApheAirAction.Detonate && dExpired.OffsetMeters > 0 && dExpired.OffsetMeters <= 0.02 && dExpired.Reason == "delay-expired",
            $"2 delay expired while traversing module detonates immediately in clean Air ({dExpired})");

        var dTouching = ApheRules.DecideAirStep(0.0, 0.002, nextIsArmour: true, maxExitM: 1.0, minGapMm: 50, def, justArmed: false);
        Check(dTouching.Action == ApheAirAction.Continue && dTouching.Reason == "touching-surface",
            $"2 expired delay touching surface defers until clean Air ({dTouching})");

        var dOpenTop = ApheRules.DecideAirStep(0.60, double.PositiveInfinity, nextIsArmour: false, maxExitM: double.PositiveInfinity, minGapMm: 50, def, justArmed: true);
        Check(dOpenTop.Action == ApheAirAction.Detonate && Math.Abs(dOpenTop.OffsetMeters - def.OpenFallbackMeters) < 1e-6 && dOpenTop.Reason == "open-space",
            $"2 open-top / single-plate exit right after arming detonates at {def.OpenFallbackMeters:0.00} m fallback ({dOpenTop})");

        var dOverpen = ApheRules.DecideAirStep(0.60, double.PositiveInfinity, nextIsArmour: false, maxExitM: double.PositiveInfinity, minGapMm: 50, def, justArmed: false);
        Check(dOverpen.Action == ApheAirAction.Overpenetrate && dOverpen.Reason == "overpenetrated",
            $"2 armed shell exiting rear into open sky after travelling inside overpenetrates cleanly ({dOverpen})");

        // Strict Air invariant across sweep: every finite Detonate offset satisfies 0 < offset < nextEnterM
        bool airInvariantOk = true;
        foreach (var delay in new[] { 0.0, 0.01, 0.15, 0.60, 1.50 })
        foreach (var enter in new[] { 0.0003, 0.005, 0.02, 0.06, 0.15, 0.35, 0.59, 0.60, 0.61, 0.80, 2.0 })
        foreach (var armour in new[] { false, true })
        {
            var dec = ApheRules.DecideAirStep(delay, enter, armour, enter + 0.03, 50, def, justArmed: true);
            if (dec.Action == ApheAirAction.Detonate && !(dec.OffsetMeters > 0 && dec.OffsetMeters < enter))
                airInvariantOk = false;
        }
        Check(airInvariantOk, "2 Air invariant: every finite Detonate offset lies strictly inside (0, nextEnterM)");

        // StopDetonationOffset when stopped by engine block / thick rear bulkhead vs tiny seam
        var stopEngine = ApheRules.StopDetonationOffset(0.25, 0.25);
        Check(stopEngine > 0.18 && stopEngine < 0.25, $"2 internal engine stop detonates cleanly in Air before engine ({stopEngine:0.000} m)");
        var stopTight = ApheRules.StopDetonationOffset(0.015, 0.015);
        Check(stopTight > 0 && stopTight < 0.015, $"2 tight 15 mm stop stays strictly in clean Air ({stopTight:0.000} m)");
        Check(ApheRules.StopDetonationOffset(0.0003, 0.0003) == 0, "2 0.3 mm seam stop does not detonate inside crack");

        // Legacy DetonationOffsetMeters compatibility
        Check(Math.Abs(ApheRules.DetonationOffsetMeters(def, null) - def.OpenFallbackMeters) < 1e-6,
            "2 legacy DetonationOffsetMeters null uses OpenFallbackMeters");
        Check(Math.Abs(ApheRules.DetonationOffsetMeters(def, 2000) - def.FuseDelayMeters) < 1e-6,
            "2 legacy DetonationOffsetMeters 2.0 m allows full FuseDelayMeters");
        Check(ApheRules.DetonationOffsetMeters(def with { FuseDelayMeters = 0 }, 2000) == 0,
            "2 zero fuse delay returns 0");

        // 3. Multi-burst plan & exact integer BatchCounts scaling across calibres
        var p50 = ApheRules.Plan(50, 1.0, def);
        var p75 = ApheRules.Plan(75, 1.0, def);
        var p100 = ApheRules.Plan(100, 1.0, def);
        var p122 = ApheRules.Plan(122, 1.0, def);
        var p152 = ApheRules.Plan(152, 1.0, def);
        Check(p50.TotalCount < p75.TotalCount && p75.TotalCount < p100.TotalCount && p100.TotalCount < p122.TotalCount && p122.TotalCount <= p152.TotalCount,
            $"3 fragment counts scale monotonically with calibre: 50={p50.TotalCount}, 75={p75.TotalCount}, 100={p100.TotalCount}, 122={p122.TotalCount}, 152={p152.TotalCount}");
        Check(p75.TotalCount == 54 && p75.BurstCalls == 2 && p75.PerBurstCount <= 32,
            $"3 75 mm APHE spawns 54 fragments in 2 bursts ({p75.PerBurstCount:0.0}/burst)");
        Check(p100.TotalCount == 72 && p100.BurstCalls == 3 && p100.PerBurstCount <= 32,
            $"3 100 mm APHE spawns 72 fragments in 3 bursts ({p100.PerBurstCount:0.0}/burst)");
        var b100 = ApheRules.BatchCounts(p100.TotalCount);
        Check(b100.Sum() == p100.TotalCount && b100.All(c => c >= 1 && c <= ApheRules.NativeBurstCap) && b100.SequenceEqual(new[] { 32, 32, 8 }),
            $"3 BatchCounts(72) = [{string.Join(",", b100)}] sums exactly with identical per-fragment mass");
        Check(Math.Abs(p100.TotalMassKg - 4.5) < 1e-6 && p100.FragmentSpeedMetersPerSecond >= 1000,
            $"3 100 mm APHE casing mass ({p100.TotalMassKg:0.00} kg) and blast speed ({p100.FragmentSpeedMetersPerSecond:0} m/s) are realistic and lethal");

        // Native Sprocket AP-profile (profile 0, K=1658) penetration check across calibres:
        // P_mm = 100 * ((sqrt(m_kg) * v_mps) / (K * (d_mm * 0.01)^0.75))^1.43
        static double NativePen0Mm(ApheBurstPlan plan, double k = 1658.0)
        {
            double m = plan.TotalMassKg / plan.TotalCount;
            double v = plan.FragmentSpeedMetersPerSecond;
            double dMm = 2000.0 * Math.Cbrt((plan.TotalVolumeM3 / plan.TotalCount) * 3.0 / (4.0 * Math.PI));
            return 100.0 * Math.Pow((Math.Sqrt(m) * v) / (k * Math.Pow(dMm * 0.01, 0.75)), 1.43);
        }
        var pen50 = NativePen0Mm(p50);
        var pen75 = NativePen0Mm(p75);
        var pen100 = NativePen0Mm(p100);
        var pen152 = NativePen0Mm(p152);
        Check(pen50 >= 16 && pen50 <= 26 && pen75 >= 22 && pen75 <= 32 && pen100 >= 28 && pen100 <= 38 && pen152 >= 36 && pen152 <= 52,
            $"3 AP-profile fragment penetration defeats thin internal plates (5-30 mm) and stops in heavy walls: 50mm={pen50:0.0}mm, 75mm={pen75:0.0}mm, 100mm={pen100:0.0}mm, 152mm={pen152:0.0}mm");

        // 4. Uniform 360-degree Fibonacci sphere & physical CombineVelocity vector addition
        int fibFront = 0, fibRear = 0;
        bool fibAllUnit = true;
        double sumX = 0, sumY = 0, sumZ = 0;
        const int fibN = 72;
        for (int i = 0; i < fibN; i++)
        {
            var d = ApheRules.FibonacciSphereDirection(i, fibN, 180, 0.73);
            var len = Math.Sqrt(d.X * d.X + d.Y * d.Y + d.Z * d.Z);
            if (Math.Abs(len - 1.0) > 1e-5) fibAllUnit = false;
            if (d.Z >= 0) fibFront++; else fibRear++;
            sumX += d.X; sumY += d.Y; sumZ += d.Z;
        }
        Check(fibAllUnit, "4 all 72 Fibonacci sphere directions are unit vectors");
        Check(fibFront == 36 && fibRear == 36 && Math.Abs(sumZ) < 1e-4 && Math.Sqrt(sumX * sumX + sumY * sumY) < 1.5,
            $"4 72-point Fibonacci sphere is balanced 36/36 with isotropic centroid (sumZ={sumZ:0.0000})");

        var vFwd = ApheRules.CombineVelocity((0, 0, 1), 1050, 800, def.ForwardMomentumBlend);
        var vSide = ApheRules.CombineVelocity((1, 0, 0), 1050, 800, def.ForwardMomentumBlend);
        var vRear = ApheRules.CombineVelocity((0, 0, -1), 1050, 800, def.ForwardMomentumBlend);
        Check(vFwd.Speed > vSide.Speed && vSide.Speed > vRear.Speed && vRear.Direction.Z < -0.99f && vRear.Speed >= 800,
            $"4 CombineVelocity boosts forward ({vFwd.Speed:0} m/s) > side ({vSide.Speed:0} m/s) > rear ({vRear.Speed:0} m/s) while keeping rear cleanly backward");

        var vRearExtreme = ApheRules.CombineVelocity((0, 0, -1), 1050, 3000, def.ForwardMomentumBlend);
        Check(vRearExtreme.Direction.Z < -0.99f && vRearExtreme.Speed >= 550,
            $"4 CombineVelocity never flips rear fragments forward even at extreme shell speed (z={vRearExtreme.Direction.Z:0.00}, v={vRearExtreme.Speed:0} m/s)");

        // Legacy Direction helper check
        int symBack = 0;
        for (int i = 0; i < 1000; i++)
            if (ApheRules.Direction((i * 0.6180339887498949) % 1.0, (i + 0.5) / 1000.0, 180, 0).Z < 0) symBack++;
        Check(symBack == 500, $"4 pure 180° legacy Direction with 0 momentum blend is 50/50 symmetric (back={symBack})");

        // 5. Optional "aphe" config parsing & backwards compatibility with strict SpallBalance.Parse
        var baseJson = SpallBalance.ToJson(new SpallSettings { SpacedKinetic = SpacedKineticSettings.Default, FragmentRicochet = FragmentRicochetSettings.Default });
        var parsedDefault = ApheRules.Read(baseJson, _ => { }, out var stripped1);
        Check(parsedDefault == ApheSettings.Default && ReferenceEquals(stripped1, baseJson),
            "5 spall.json without 'aphe' block returns defaults and leaves JSON untouched");

        var withAphe = baseJson.TrimEnd()[..^1] + ", \"aphe\": { \"fuseSensitivityRhaMm\": 20, \"fuseDelayMeters\": 1.2, \"fragmentCountAt100Mm\": 96, \"coneHalfAngleDegrees\": 180 } }";
        var warnings = new List<string>();
        var custom = ApheRules.Read(withAphe, warnings.Add, out var stripped2);
        Check(custom.FuseSensitivityRhaMm == 20 && custom.FuseDelayMeters == 1.2 && custom.FragmentCountAt100Mm == 96 && warnings.Count == 0,
            "5 optional 'aphe' block parses custom values cleanly");
        var spallAfterStrip = SpallBalance.Parse(stripped2);
        Check(spallAfterStrip.ApheSpallMultiplier == 4,
            "5 stripped spall.json passes strict SpallBalance.Parse without error");

        var reportNote = ApheRules.DetonationReportText("fuse-delay", 147.24, 0.60, 86);
        Check(reportNote == "APHE detonation (fuse-delay): fuse armed after 147.2 mm RHA, burst 600 mm inside (86 fragments).",
            $"5 DetonationReportText uses invariant culture formatting ({reportNote})");

        // 5b. 0.22.109: Per-shell APHE settings in Shell Editor UI (12 controls, save/reload, Effective merge, live preview)
        string[] apheKeys =
        {
            "apheFuseSensitivityRhaMm", "apheFuseDelayMeters", "apheDetonateOnStop",
            "apheConeHalfAngleDegrees", "apheForwardMomentumBlend", "apheFragmentCountAt100Mm",
            "apheFragmentMassKgAt100Mm", "apheFragmentSpeedMetersPerSecond", "apheMaxGapFraction",
            "apheMinWallClearanceMeters", "apheOpenFallbackMeters", "apheExplosionVisualScale"
        };
        Check(apheKeys.All(k => ShellEditorModel.FieldsByKey.ContainsKey(k) && ShellEditorText.Captions.ContainsKey(k)),
            "5b all 12 APHE keys are registered in ModularShellDraft.Fields and ShellEditorText.Captions");

        var tempDir = Path.Combine(Path.GetTempPath(), "eb-feat109-aphe-ui-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        try
        {
            var rtPath = Path.Combine(tempDir, "shells.json");
            var auPath = Path.Combine(tempDir, "modules.json");
            var impPath = Path.Combine(tempDir, "modules.import.json");
            File.WriteAllText(rtPath, ReleaseProfiles.Defaults());
            double clockNow = 100;
            var model = ShellEditorModel.Open(rtPath, auPath, impPath, false, null, () => clockNow);
            model.Cannon = new(120, 975, 1658, 600, _ => 320.0, _ => 0, "Coldwar2");
            int apheIdx = Enumerable.Range(0, model.Count).First(i => model.Draft.Profiles[i]!["originBehavior"]!.GetValue<string>() == "aphe");
            int rodIdx = Enumerable.Range(0, model.Count).First(i => model.Draft.Profiles[i]!["originBehavior"]!.GetValue<string>() == "apfsds");

            model.Select(rodIdx);
            Check(apheKeys.All(k => !model.Active(k)), "5b all 12 APHE fields stay hidden on non-APHE shells (APFSDS)");

            model.Select(apheIdx);
            Check(apheKeys.All(k => model.Active(k)), "5b all 12 APHE fields are active/visible on APHE shells");
            Check(model.Number("apheFuseSensitivityRhaMm") == 15 && Math.Abs(model.Number("apheFuseDelayMeters") - 0.60) < 1e-6 && model.Choice("apheDetonateOnStop") == "yes",
                "5b unedited APHE shell displays defaults (15 mm, 0.60 m, DetonateOnStop=yes)");
            Check(model.LiveValue(6).Contains("320 mm RHA") && model.LiveValue(6).Contains("fuse ≥15 mm / 0.6 m")
                && model.LiveValue(7).Contains("86 frags") && model.LiveValue(7).Contains("360°"),
                $"5b Live column shows APHE fuse and calibre-scaled burst preview ({model.LiveValue(6)} | {model.LiveValue(7)})");

            // Edit all 12 APHE controls and verify save -> reload -> Effective merge -> live preview update
            model.SetNumber("apheFuseSensitivityRhaMm", 10);
            model.SetNumber("apheFuseDelayMeters", 1.15);
            model.SetChoice("apheDetonateOnStop", "no");
            model.SetNumber("apheConeHalfAngleDegrees", 90);
            model.SetNumber("apheForwardMomentumBlend", 0.40);
            model.SetNumber("apheFragmentCountAt100Mm", 120);
            model.SetNumber("apheFragmentMassKgAt100Mm", 9.6);
            model.SetNumber("apheFragmentSpeedMetersPerSecond", 1250);
            model.SetNumber("apheMaxGapFraction", 0.80);
            model.SetNumber("apheMinWallClearanceMeters", 0.03);
            model.SetNumber("apheOpenFallbackMeters", 0.35);
            model.SetNumber("apheExplosionVisualScale", 1.25);
            clockNow += 1.0;
            Check(model.ErrorCount == 0 && model.LiveValue(6).Contains("fuse ≥10 mm / 1.15 m") && model.LiveValue(7).Contains("144 frags") && model.LiveValue(7).Contains("180°"),
                $"5b Live column updates dynamically after APHE edits ({model.LiveValue(6)} | {model.LiveValue(7)})");

            var saveRes = model.Save();
            var parsedProfiles = ShellProfiles.Parse(File.ReadAllText(rtPath));
            var savedAphe = parsedProfiles.Single(p => p.Id == model.IdAt(apheIdx));
            var eff = ApheRules.Effective(savedAphe, ApheSettings.Default);
            Check(saveRes.Ready && savedAphe.Aphe != null
                && eff.FuseSensitivityRhaMm == 10 && Math.Abs(eff.FuseDelayMeters - 1.15) < 1e-6 && !eff.DetonateOnStop
                && eff.ConeHalfAngleDegrees == 90 && Math.Abs(eff.ForwardMomentumBlend - 0.40) < 1e-6
                && eff.FragmentCountAt100Mm == 120 && Math.Abs(eff.FragmentMassKgAt100Mm - 9.6) < 1e-6
                && eff.FragmentSpeedMetersPerSecond == 1250 && Math.Abs(eff.MaxGapFraction - 0.80) < 1e-6
                && Math.Abs(eff.MinWallClearanceMeters - 0.03) < 1e-6 && Math.Abs(eff.OpenFallbackMeters - 0.35) < 1e-6
                && Math.Abs(eff.ExplosionVisualScale - 1.25) < 1e-6,
                "5b Save compiles all 12 per-shell APHE fields into shells.json and ApheRules.Effective merges them cleanly");

            // Reopen from disk and confirm all 12 fields roundtrip cleanly without dirty flag
            var reopened = ShellEditorModel.Open(rtPath, auPath, impPath, false, null, () => clockNow);
            reopened.Select(apheIdx);
            Check(!reopened.Dirty && reopened.ErrorCount == 0 && reopened.Number("apheFuseSensitivityRhaMm") == 10 && reopened.Choice("apheDetonateOnStop") == "no" && reopened.Number("apheFragmentCountAt100Mm") == 120,
                "5b reopened ShellEditorModel retains all saved APHE overrides cleanly");

            // Switching Effect from spall -> kinetic hides APHE fields and strips them cleanly on Save
            reopened.Duplicate(apheIdx);
            reopened.SetModule("Effect", Array.IndexOf(ModularShellDraft.Options["effect"], "kinetic"));
            Check(apheKeys.All(k => !reopened.Active(k)) && reopened.Save().Ready,
                "5b switching a duplicated APHE shell to kinetic hides and strips APHE fields cleanly on Save");

            // Read-only live config compatibility smoke test
            const string liveShells = @"E:\SteamLibrary\steamapps\common\Sprocket\BepInEx\config\sprocket.expandedballistics.shells.json";
            const string liveModules = @"E:\SteamLibrary\steamapps\common\Sprocket\BepInEx\config\sprocket.expandedballistics.modules.json";
            if (File.Exists(liveShells))
            {
                var liveDraft = ModularShellDraft.OpenCurrent(File.ReadAllText(liveShells), File.Exists(liveModules) ? File.ReadAllText(liveModules) : null, out _);
                Check(liveDraft.Profiles.Count > 0 && liveDraft.Compile().Ready,
                    $"5b live user shells.json + modules.json open and compile cleanly ({liveDraft.Profiles.Count} profiles)");
            }
        }
        finally { if (Directory.Exists(tempDir)) Directory.Delete(tempDir, true); }

        // 6. Source invariants & version / hook count
        var root = Fix02228Tests.SourceRoot();
        string Read(string rel) => File.ReadAllText(Path.Combine(root, rel)).Replace("\r\n", "\n");
        var spallRuntime = Read("RuntimeSpall.cs");
        Check(spallRuntime.Contains("ApheRules.IsFuseArmed(", StringComparison.Ordinal)
            && spallRuntime.Contains("ApheRules.Effective(", StringComparison.Ordinal)
            && spallRuntime.Contains("ApheRules.DecideAirStep(", StringComparison.Ordinal)
            && spallRuntime.Contains("ApheRules.StopDetonationOffset(", StringComparison.Ordinal)
            && spallRuntime.Contains("ApheRules.Plan(", StringComparison.Ordinal)
            && spallRuntime.Contains("ApheRules.BatchCounts(", StringComparison.Ordinal)
            && spallRuntime.Contains("ApheRules.FibonacciSphereDirection(", StringComparison.Ordinal)
            && spallRuntime.Contains("ApheRules.CombineVelocity(", StringComparison.Ordinal)
            && spallRuntime.Contains("ApheRules.DetonationReportText(", StringComparison.Ordinal)
            && spallRuntime.Contains("materialIndex=-1", StringComparison.Ordinal)
            && spallRuntime.Contains("depthLookup=null", StringComparison.Ordinal)
            && spallRuntime.Contains("projectileProfileIndex=0", StringComparison.Ordinal)
            && spallRuntime.Contains("FragmentFlag.PenetrationPrecomputed", StringComparison.Ordinal)
            && spallRuntime.Contains("FragmentFlag.OriginalPenetrator|FragmentFlag.Penetrated|FragmentFlag.Killed", StringComparison.Ordinal)
            && spallRuntime.Contains("planeNormal=UnityEngine.Vector3.zero", StringComparison.Ordinal)
            && spallRuntime.Contains("apheExtraBurst", StringComparison.Ordinal),
            "6 RuntimeSpall wires per-shell ApheRules.Effective + Air-space APHE detonation + terminal Killed marker");

        Check(Read("Plugin.cs").Contains("\"0.22.109\"", StringComparison.Ordinal)
            && Read("SprocketExpandedBallistics.csproj").Contains("<Version>0.22.109</Version>", StringComparison.Ordinal)
            && Read("RELEASE-NOTES.md").Contains("## 0.22.109", StringComparison.Ordinal),
            "6 Plugin.cs, csproj, and RELEASE-NOTES.md carry version 0.22.109");

        int hooks = Directory.EnumerateFiles(root, "*.cs", SearchOption.AllDirectories)
            .Where(f => { var r = f.Replace('\\', '/'); return !r.Contains("/tests/") && !r.Contains("/bin/") && !r.Contains("/obj/"); })
            .Sum(f => File.ReadLines(f).Count(l => l.Contains("[HarmonyPatch", StringComparison.Ordinal)));
        Check(hooks == 48, $"6 Non-test [HarmonyPatch line count stays 48 (actual={hooks})");

        Console.WriteLine($"FEAT104: {checks} checks, {failures} failures");
    }
}
