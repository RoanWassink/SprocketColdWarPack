using SprocketExpandedBallistics.Era;
using SprocketExpandedBallistics.Materials;
namespace SprocketExpandedBallistics;

/// <summary>
/// 0.22.96 (brief FEAT-0.22.96-K1-BINDING-KEYS): built-in exact bindings for the stage cells (Kontakt-1 goes off),
/// factory default Ctrl+L for "Show cassette layers", and bounded build-path logging. Checks in the pure test build:
/// 1. merged over the shipped default config + the 0.22.73 War Thunder variants, every cassette-classified preset row
///    of EraGeneratedDefinitions has a binding; a binding the user config already holds wins; a custom user entry for
///    a stage cell is not replaced by the built-in default;
/// 2. the Kontakt-1 stage cell route is kontakt1Active/lightEra (the recipe kontakt1PhysicalV1Part.json declares),
///    equal to tests/physical-era-bindings.json, and matches the kontakt1Active response recipe;
/// 3. kontakt1Active is CE-only: reactive vs heat, not vs apfsds (lightEra);
/// 4. no exception on the path without a binding (source: the throw is gone, native retained, one warning per id),
///    the build-path catchers are bounded, and an unknown id simply resolves to false;
/// 5. CassetteVisualKeys registers cassette-layers with factory default Ctrl+&lt;Keyboard&gt;/l;
/// 6. K1 balance: paid mass 2.7 kg unchanged, kontakt1Active balance valid at multiplier 8.25, material cost
///    2.7 x 8.25 x CostPerKgOfArmour = 22.275 x C, passive fingerprint = the native kontakt1Active technology;
/// 7. [HarmonyPatch line count in the non-test sources stays 47.
/// </summary>
internal static class Feat02296StageCellBindingTests
{
    internal static void Run(Action<bool,string> check)
    {
        var root=Fix02228Tests.SourceRoot();
        string Read(string f)=>File.ReadAllText(Path.Combine(root,f)).Replace("\r\n","\n");
        var n=0;void Check(bool ok,string m){n++;check(ok,"FEAT96: "+m);}

        // ================= 1. merged catalogue covers every cassette-classified preset row =================
        var shipped=EraPartBindings.Parse(Read("Material/defaults/sprocket.expandedballistics.era.bindings.json"));
        var merged=EraStageCellDefaults.With(EraWtVariants.With(shipped));
        Check(EraStageCellDefaults.Bindings().Count()==65,"1 built-in defaults: 65 rows (58 stage cells + 7 experimental)");
        var rows=EraGeneratedDefinitions.Presets.Where(p=>!string.IsNullOrEmpty(p.Cassette)).ToArray();
        Check(rows.Length==83,"1 cassette-classified preset rows: "+rows.Length+" (10 shipped + 8 WT variants + 65 defaults)");
        foreach(var p in rows)
            Check(merged.TryGetCassette(p.Cassette,out var b)&&b.MountComponentId==p.Mount,"1 binding present: "+p.Cassette+" -> "+(b?.MaterialId??"-")+"/"+(b?.Kind??"-"));
        Check(merged.TryGetCassette("kontakt1Cassette",out var k1c)&&k1c.ResponseId=="kontakt1Cassette"&&k1c.Kind=="lightEra"&&k1c.MaterialId=="kontakt1Cassette",
            "1 shipped kontakt1Cassette entry retained (user config wins over the built-in defaults)");
        var custom=new EraPartBindingCatalogue(shipped.Bindings.Append(new EraPartBinding("kontakt1Stage1Cell1","kontakt1Housing","customMat","customResp","heavyEra")).ToList());
        Check(EraStageCellDefaults.With(custom).TryGetCassette("kontakt1Stage1Cell1",out var cb)&&cb.MaterialId=="customMat"&&cb.Kind=="heavyEra",
            "1 a user entry for a stage cell is not replaced by the built-in default");
        Check(merged.Bindings.Count==shipped.Bindings.Count+8+65,"1 merged catalogue size = shipped + 8 WT + 65 defaults ("+merged.Bindings.Count+")");

        // ================= 2. K1 stage-cell route =================
        Check(merged.TryGetCassette("kontakt1Stage1Cell1",out var k1)&&k1.MountComponentId=="kontakt1Housing"&&k1.MaterialId=="kontakt1Active"&&k1.ResponseId=="kontakt1Active"&&k1.Kind=="lightEra",
            "2 kontakt1Stage1Cell1 -> kontakt1Housing, material/response kontakt1Active, lightEra (native part recipe)");
        var fixture=EraPartBindings.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory,"physical-era-bindings.json")));
        Check(fixture.TryGetCassette("kontakt1Stage1Cell1",out var fb)&&fb.MountComponentId==k1!.MountComponentId&&fb.MaterialId==k1.MaterialId&&fb.ResponseId==k1.ResponseId&&fb.Kind==k1.Kind,
            "2 K1 route equals tests/physical-era-bindings.json");

        // ================= 3. K1 CE-reactive, not kinetic =================
        var responses=ArmourResponses.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory,"physical-era-responses.json")));
        var k1resp=responses.Responses.Single(r=>r.ResponseId=="kontakt1Active");
        Check(k1resp.Kind=="lightEra"&&k1!.MatchesRecipe(k1resp.ResponseId,k1resp.Kind,k1resp.CompatibleMaterialIds),"3 K1 binding matches the kontakt1Active recipe exactly");
        Check(ArmourResponseModel.ReactiveThreat(k1resp,"heat")&&!ArmourResponseModel.ReactiveThreat(k1resp,"apfsds"),"3 kontakt1Active reacts to HEAT/CE only, not to APFSDS/AP");

        // ================= 4. no exception without a binding; bounded logging =================
        var proto=Read("Material/ReliktPrototype.cs");
        Check(!proto.Contains("Exact cassette binding is unavailable",StringComparison.Ordinal),"4 the missing-binding throw is gone from the build path");
        Check(proto.Contains("missingBindingWarnings.Add(__instance.ComponentID)",StringComparison.Ordinal)&&proto.Contains("native definition retained, SetCost skipped",StringComparison.Ordinal),
            "4 missing binding: native retained, SetCost skipped, one warning per id per session");
        Check(proto.Contains("if(balanceExceptions.Add(__instance.ComponentID))",StringComparison.Ordinal),"4 balance catch bounded to one line per id per session");
        Check(proto.Contains("modelLogs++ < 12",StringComparison.Ordinal)&&proto.Contains("damageIdentityLogs++ < 12",StringComparison.Ordinal),"4 model/damage catchers bounded (max 12 per session)");
        var empty=EraPartBindings.Parse("{\"schemaVersion\":1,\"matchMode\":\"componentId\",\"bindings\":[]}");
        Check(!empty.TryGetCassette("unknownCell",out _)&&!EraStageCellDefaults.With(empty).TryGetCassette("unknownCell",out _),"4 an unknown id resolves to false, never throws");

        // ================= 5. factory default Ctrl+L =================
        var keys=Read("CassetteVisualKeys.cs");
        Check(keys.Contains("\"cassette-layers\",\"Show cassette layers\",\"Ctrl+<Keyboard>/l\"",StringComparison.Ordinal),"5 cassette-layers factory default is Ctrl+<Keyboard>/l");

        // ================= 6. K1 balance before/after =================
        var preset=EraGeneratedDefinitions.Presets.Single(p=>p.Cassette=="kontakt1Stage1Cell1");
        Check(preset.CassetteMass==2.7f,"6 K1 cell paid mass 2.7 kg (unchanged by the binding)");
        var bal=MaterialBalance.Calculate(0.15f,3169.907016060862f,8.25f);
        Check(bal.IsValid&&bal.EffectiveCostMultiplier==8.25f,"6 kontakt1Active balance valid, effective multiplier 8.25 (floor "+bal.MinimumCostMultiplier.ToString("0.####")+")");
        Check(MaterialBalance.TryMaterialCost(2.7f,bal.EffectiveCostMultiplier,1f,out var cost)&&Math.Abs(cost-22.275f)<1e-4f,
            "6 K1 material cost after the binding = 2.7 kg x 8.25 x CostPerKgOfArmour = 22.275 x C (was: SetCost skipped, native cost)");
        Check((float)k1resp.PassiveMaterial.RhaFactor==0.15f&&(float)k1resp.PassiveMaterial.Density==3169.907016060862f&&(float)k1resp.PassiveMaterial.SpallFactor==0.25f,
            "6 passive fingerprint stays the native kontakt1Active technology (rha 0.15, density 3169.907, spall 0.25)");

        // ================= 7. hook count (0.22.97: +1 audited RuntimeSolidCollider) =================
        var hooks=Directory.EnumerateFiles(root,"*.cs",SearchOption.AllDirectories)
            .Where(f=>!f.Replace('\\','/').Contains("/tests/",StringComparison.Ordinal))
            .Sum(f=>File.ReadLines(f).Count(l=>l.Contains("[HarmonyPatch",StringComparison.Ordinal)));
        Check(hooks==48,"7 [HarmonyPatch lines in non-test sources: "+hooks+" (48)");

        // ================= 8. ledger vs the pre-change snapshot (EB_BASE_SRC) =================
        var baseDir=Environment.GetEnvironmentVariable("EB_BASE_SRC");
        if(!string.IsNullOrEmpty(baseDir)&&Directory.Exists(baseDir))
        {
            var exts=new HashSet<string>(StringComparer.OrdinalIgnoreCase){ ".cs",".csproj",".md" };
            bool Skip(string rel)=>rel.Split('/').Any(p=>p is "bin" or "obj" or ".git");
            Dictionary<string,string> Inventory(string dir)=>Directory.EnumerateFiles(dir,"*",SearchOption.AllDirectories)
                .Select(f=>(Rel:Path.GetRelativePath(dir,f).Replace('\\','/'),Full:f))
                .Where(x=>!Skip(x.Rel)&&exts.Contains(Path.GetExtension(x.Rel))).ToDictionary(x=>x.Rel,x=>x.Full);
            var a=Inventory(baseDir);var b=Inventory(root);
            var changed=a.Keys.Intersect(b.Keys).Where(k=>!File.ReadAllBytes(a[k]).AsSpan().SequenceEqual(File.ReadAllBytes(b[k]))).OrderBy(k=>k,StringComparer.Ordinal).ToArray();
            var added=b.Keys.Except(a.Keys).OrderBy(k=>k,StringComparer.Ordinal).ToArray();
            string[] planned={"CassetteVisualKeys.cs","Material/EraStageCellDefaults.cs","Material/ReliktPrototype.cs","Plugin.cs","RELEASE-NOTES.md","RuntimeArmourResponses.cs","SprocketExpandedBallistics.csproj","tests/ExpandedBallistics.Tests.csproj","tests/Feat02295GameBuildGateTests.cs","tests/Feat02296StageCellBindingTests.cs","tests/Program.cs"};
            var ledger=changed.Concat(added).ToArray();
            Check(ledger.All(p=>planned.Contains(p,StringComparer.Ordinal)),"8 changed/added vs snapshot are only the planned 0.22.96 files: "+string.Join(", ",ledger));
            Check(planned.All(p=>ledger.Contains(p,StringComparer.Ordinal)),"8 all 11 planned files show up in the ledger ("+ledger.Length+")");
            var baseHooks=a.Keys.Where(k=>k.EndsWith(".cs",StringComparison.OrdinalIgnoreCase)&&!k.StartsWith("tests/",StringComparison.OrdinalIgnoreCase))
                .Sum(k=>File.ReadLines(a[k]).Count(l=>l.Contains("[HarmonyPatch",StringComparison.Ordinal)));
            Check(baseHooks==47,"8 snapshot also has 47 [HarmonyPatch lines ("+baseHooks+")");
        }
        else Check(true,"8 EB_BASE_SRC not set; ledger against the snapshot skipped");

        Console.WriteLine($"FEAT96: {n} checks");
    }
}
