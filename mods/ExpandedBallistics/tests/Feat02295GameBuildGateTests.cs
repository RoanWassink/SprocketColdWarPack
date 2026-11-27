using System.Text.RegularExpressions;
namespace SprocketExpandedBallistics;

/// <summary>
/// 0.22.95 (brief FEAT-0.22.95-GAME-BUILD-GATE): the game-build hash gate. Checks, in the pure test build
/// (EB_PURE_SOLID_MARK compiles only the pure core of GameBuildGate.cs):
/// 1. Decide(): both audited hashes -> true, unknown hash -> false, unknown + trust -> true, null (read
///    failure) -> false; the hash compare is case-insensitive and two hashes are listed.
/// 2. Source text: every part-B hook class is patched only at call sites behind GameBuildGate.AllowHotPath,
///    and the parity file gates its FinalInput install; the skipped classes never set Ready/Patched ungated;
///    ATGM (dependent on the shell-selection hooks) stays off with one Info line.
/// 3. The "[HarmonyPatch" line count in the non-test sources stays 47.
/// 4. The log texts and the cfg key of the gate exist in GameBuildGate.cs; Plugin.cs wires it and is 0.22.95.
/// 7. With EB_BASE_SRC set, a file ledger against the pre-change snapshot: only the planned files differ.
/// </summary>
internal static class Feat02295GameBuildGateTests
{
    // Part B of the brief. RuntimeArmourComparisonParity itself carries no PatchAll: its file installs
    // RuntimeArmourComparisonFinalInput (same feature), so that call site is the one gated there.
    internal static readonly string[] GatedClasses={"RuntimeDamageFeed","RuntimeShellSelection","RuntimeSpall","RuntimeTandem",
        "RuntimeArmourResponses","RuntimeArmourSimulator","RuntimeArmourComparisonCapture","RuntimeArmourComparisonFinalInput",
        "RuntimeOriginalTrajectory","NativeCassetteGeometry","RuntimeCassettePlateVisual","RuntimeCompositePlate","RuntimeSolidCollider"};
    internal static readonly string[] Planned={"GameBuildGate.cs","Plugin.cs","ShellFeature.cs","RuntimeOriginalTrajectory.cs",
        "RuntimeArmourComparisonParity.cs","SprocketExpandedBallistics.csproj","RELEASE-NOTES.md",
        "tests/Feat02295GameBuildGateTests.cs","tests/Program.cs","tests/ExpandedBallistics.Tests.csproj"};
    internal static bool IsPlanned(string rel)=>Planned.Contains(rel.Replace('\\','/'),StringComparer.Ordinal);

    internal static void Run(Action<bool,string> sink)
    {
        int checks=0,failures=0;void Check(bool ok,string message){checks++;if(!ok)failures++;sink(ok,"FEAT95: "+message);}
        string root=Fix02228Tests.SourceRoot();
        string Read(string f)=>File.ReadAllText(Path.Combine(root,f)).Replace("\r\n","\n");

        // ================= 1. Decide() =================
        const string oldHash="3E58082804D97CF32FFD400E7427E767EF9576D23DB71B2BE96FBB7F825FC17B";
        const string newHash="77FC601748991DC2101435C6B72C7A5A4B3DC3C68429B349C2EFEE711F6D5388";
        const string unknownHash="0000000000000000000000000000000000000000000000000000000000000000";
        Check(GameBuildGate.Decide(oldHash,false),"1 Decide: audited hash 3E580828...C17B -> true");
        Check(GameBuildGate.Decide(newHash,false),"1 Decide: audited hash 77FC6017...5388 -> true");
        Check(GameBuildGate.Decide(newHash.ToLowerInvariant(),false),"1 Decide: hash comparison is case-insensitive");
        Check(!GameBuildGate.Decide(unknownHash,false),"1 Decide: unknown hash -> false");
        Check(GameBuildGate.Decide(unknownHash,true),"1 Decide: unknown hash + TrustUnknownGameBuild -> true");
        Check(!GameBuildGate.Decide(null,false),"1 Decide: null hash (GameAssembly.dll unreadable) -> false (treated as unknown)");
        Check(GameBuildGate.Decide(null,true),"1 Decide: null hash + trust -> true");
        Check(GameBuildGate.Decide(oldHash,true)&&GameBuildGate.Decide(newHash,true),"1 Decide: known hash + trust -> true");
        Check(GameBuildGate.KnownHashes.Length==2&&GameBuildGate.KnownHashes.Contains(oldHash,StringComparer.Ordinal)&&GameBuildGate.KnownHashes.Contains(newHash,StringComparer.Ordinal),
            "1 exactly the two audited hashes are listed");

        // ================= 2. every part-B class patches only behind the gate =================
        var files=Directory.EnumerateFiles(root,"*.cs",SearchOption.AllDirectories)
            .Where(f=>{var r=f.Replace('\\','/');return !r.Contains("/tests/")&&!r.Contains("/bin/")&&!r.Contains("/obj/");})
            .Select(f=>(Rel:Path.GetRelativePath(root,f).Replace('\\','/'),Lines:File.ReadAllLines(f))).ToArray();
        foreach(var cls in GatedClasses)
        {
            var sites=new List<(string Rel,int I)>();
            foreach(var f in files)
                for(var i=0;i<f.Lines.Length;i++)
                    if(Regex.IsMatch(f.Lines[i],@"(?:PatchAll|CreateClassProcessor)\s*\(\s*typeof\s*\(\s*"+cls+@"\s*\)"))
                        sites.Add((f.Rel,i));
            Check(sites.Count>=1,$"2 {cls}: {sites.Count} PatchAll/CreateClassProcessor call site(s) found");
            var ungated=sites.Where(s=>{var ls=files.First(f=>f.Rel==s.Rel).Lines;
                for(var j=Math.Max(0,s.I-3);j<=s.I;j++)if(ls[j].Contains("GameBuildGate.AllowHotPath",StringComparison.Ordinal))return false;
                return true;}).Select(s=>s.Rel+":"+(s.I+1)).ToArray();
            Check(ungated.Length==0,$"2 {cls}: every call site is on/behind a GameBuildGate.AllowHotPath line"+(ungated.Length>0?" — UNGATED: "+string.Join("; ",ungated):""));
        }
        var parity=Read("RuntimeArmourComparisonParity.cs");
        Check(parity.Contains("GameBuildGate.AllowHotPath",StringComparison.Ordinal),"2 RuntimeArmourComparisonParity.cs gates its install (RuntimeArmourComparisonFinalInput)");
        Check(!files.Any(f=>f.Lines.Any(l=>Regex.IsMatch(l,@"(?:PatchAll|CreateClassProcessor)\s*\(\s*typeof\s*\(\s*RuntimeArmourComparisonParity\s*\)"))),
            "2 RuntimeArmourComparisonParity is never PatchAll'd directly (its hook class is the gated FinalInput)");
        // skipped classes must not set their Ready/Patched flags ungated
        var shell=Read("ShellFeature.cs");
        Check(Regex.IsMatch(shell,@"if\s*\(\s*GameBuildGate\.AllowHotPath\s*\)\s*RuntimeDamageFeed\.Ready\s*=\s*true"),"2 RuntimeDamageFeed.Ready is set only behind the gate");
        Check(Regex.IsMatch(shell,@"if\s*\(\s*GameBuildGate\.AllowHotPath\s*\)\s*\{[^{}]*PatchAll\(typeof\(RuntimeAtgm\)\)[^{}]*RuntimeAtgm\.Ready\s*=\s*true"),"2 ATGM patch and RuntimeAtgm.Ready only behind the gate");
        Check(shell.Contains("[ATGM] Missile firing left off on an unaudited game build",StringComparison.Ordinal),"2 ATGM stays off with one Info line when the gate is closed");
        var traj=Read("RuntimeOriginalTrajectory.cs");
        Check(Regex.IsMatch(traj,@"if\s*\(\s*!\s*GameBuildGate\.AllowHotPath\s*\)\s*return\s+false"),"2 RuntimeOriginalTrajectory.Install returns false when gated (Ready stays off)");
        var plugin=Read("Plugin.cs");
        Check(!plugin.Contains("GameBuildGate.AllowHotPath)try{new Harmony(\"sprocket.expandedballistics.cassette.live-technology\")",StringComparison.Ordinal),
            "2 RuntimeLiveTechnology (not in part B) is still patched unconditionally");

        // ================= 3. hook annotation count (48: 0.22.97 +1 audited RuntimeSolidCollider) =================
        var hooks=files.Sum(f=>f.Lines.Count(l=>l.Contains("[HarmonyPatch",StringComparison.Ordinal)));
        Check(hooks==48,$"3 [HarmonyPatch lines in non-test sources: {hooks} (48)");

        // ================= 4. gate source: log texts, cfg, hash source =================
        var gate=Read("GameBuildGate.cs");
        Check(gate.Contains("[Game build gate] GameAssembly")&&gate.Contains("known; all hooks enabled"),"4 known-build Info line text present");
        Check(gate.Contains("is not audited; shot/hit-path hooks disabled (set TrustUnknownGameBuild=true to override at your own risk)"),"4 unknown-build Warning line text present");
        Check(gate.Contains("\"Game build gate\"")&&gate.Contains("\"TrustUnknownGameBuild\""),"4 cfg [Game build gate] TrustUnknownGameBuild");
        Check(gate.Contains("GameAssembly.dll")&&gate.Contains("Paths.GameRootPath")&&gate.Contains("SHA256"),"4 hashes GameAssembly.dll under Paths.GameRootPath with SHA256");
        Check(gate.Contains("internal static bool AllowHotPath")&&gate.Contains("internal static string? Hash")&&gate.Contains("internal static bool Known"),"4 API AllowHotPath / Hash / Known present");

        // ================= 5. wiring and version =================
        Check(plugin.Contains("\"0.22.111\""),"5 Plugin.cs version 0.22.111 (0.22.95 gate unchanged)");
        Check(Read("SprocketExpandedBallistics.csproj").Contains("<Version>0.22.111</Version>"),"5 csproj version 0.22.111");
        Check(plugin.Contains("GameBuildGate.Configure(Config,Log);"),"5 Plugin.Load binds the gate config");
        Check(plugin.IndexOf("GameBuildGate.Configure",StringComparison.Ordinal)>=0&&
              plugin.IndexOf("GameBuildGate.Configure",StringComparison.Ordinal)<plugin.IndexOf("PatchAll(typeof(NativeCassetteGeometry)",StringComparison.Ordinal),
            "5 the gate is configured before the first gated PatchAll");
        Check(Read("RELEASE-NOTES.md").Contains("## 0.22.95"),"5 RELEASE-NOTES has the 0.22.95 entry");

        // ================= 7. file ledger against the pre-change snapshot (EB_BASE_SRC) =================
        var baseDir=Environment.GetEnvironmentVariable("EB_BASE_SRC");
        if(!string.IsNullOrEmpty(baseDir)&&Directory.Exists(baseDir))
        {
            var exts=new HashSet<string>(StringComparer.OrdinalIgnoreCase){ ".cs",".csproj",".props",".md" };
            bool Skip(string rel)=>rel.Split('/').Any(p=>p is "bin" or "obj" or ".git");
            Dictionary<string,string> Inventory(string dir)=>Directory.EnumerateFiles(dir,"*",SearchOption.AllDirectories)
                .Select(f=>(Rel:Path.GetRelativePath(dir,f).Replace('\\','/'),Full:f))
                .Where(x=>!Skip(x.Rel)&&exts.Contains(Path.GetExtension(x.Rel))).ToDictionary(x=>x.Rel,x=>x.Full);
            var a=Inventory(baseDir);var b=Inventory(root);
            var changed=a.Keys.Intersect(b.Keys).Where(k=>!File.ReadAllBytes(a[k]).AsSpan().SequenceEqual(File.ReadAllBytes(b[k]))).OrderBy(k=>k,StringComparer.Ordinal).ToArray();
            var added=b.Keys.Except(a.Keys).OrderBy(k=>k,StringComparer.Ordinal).ToArray();
            var addedCs=added.Where(k=>k.EndsWith(".cs",StringComparison.OrdinalIgnoreCase)).ToArray();
            Check(changed.All(IsPlanned),"7 changed vs snapshot are only planned files: "+string.Join(", ",changed));
            Check(addedCs.All(IsPlanned)&&addedCs.Length==2,"7 new .cs files are exactly GameBuildGate.cs and the test: "+string.Join(", ",addedCs));
            Check(Planned.All(p=>changed.Contains(p)||added.Contains(p)),"7 all 10 planned files show up in the ledger");
            var baseHooks=a.Keys.Where(k=>k.EndsWith(".cs",StringComparison.OrdinalIgnoreCase)&&!k.StartsWith("tests/",StringComparison.Ordinal))
                .Sum(k=>File.ReadLines(a[k]).Count(l=>l.Contains("[HarmonyPatch",StringComparison.Ordinal)));
            Check(baseHooks==47,"7 snapshot also has 47 [HarmonyPatch lines ("+baseHooks+")");
        }
        else Check(true,"7 EB_BASE_SRC not set; ledger against the snapshot skipped (hooks count still checked above)");

        Console.WriteLine($"FEAT95: {checks} checks, {failures} failures");
    }
}
