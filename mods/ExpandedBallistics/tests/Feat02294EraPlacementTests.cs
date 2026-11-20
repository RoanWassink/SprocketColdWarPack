using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using SprocketExpandedBallistics.Materials;
namespace SprocketExpandedBallistics;

/// <summary>
/// 0.22.94 (brief FEAT-0.22.94-ERA-PLACEMENT-VERTICAL; user 17-11: "de meeste ERA modellen verticaal default", proposal answered "Ja, bouwen").
/// Targeted: DefaultPlacementRotation follows the visual OBJ (Y thin; long side Z = 0, long side X = 90), Kontakt-1 and the composite side module are 0,
/// the generated panels follow their 1 x 1 cell (side modules with fixed rows unchanged), one value per part GUID, the read-only
/// [ERA duplicate placement] log (format, cap 24, no write on its code path), and with EB_BASE_SRC the file ledger against 0.22.92
/// (only the 11th argument of the preset rows changed). Writes FEAT94-PRESETS.tsv and FEAT94-LOG-EXAMPLE.txt to test-output/.
/// What the game does with the rotation (UpFaceNormal: +Z along the slope at 0) is native code: checked only in the game.
/// </summary>
internal static class Feat02294EraPlacementTests
{
    internal static bool IsPlanned(string rel)=>rel is "Material/EraGeneratedDefinitions.cs" or "Material/EraPlacementDefaults.cs"
        or "Plugin.cs" or "SprocketExpandedBallistics.csproj" or "RELEASE-NOTES.md";
    internal const string Kontakt1Guid="62a4ab89-1e44-5885-857d-b482485df8f6";

    internal static (double X,double Y,double Z)? Size(string path)
    {
        if(!File.Exists(path))return null;
        double[] lo={1e9,1e9,1e9},hi={-1e9,-1e9,-1e9};var any=false;
        foreach(var line in File.ReadLines(path))
        {
            if(!line.StartsWith("v ",StringComparison.Ordinal))continue;
            var p=line.Split(' ',StringSplitOptions.RemoveEmptyEntries);any=true;
            for(var i=0;i<3;i++){var v=double.Parse(p[i+1],CultureInfo.InvariantCulture);lo[i]=Math.Min(lo[i],v);hi[i]=Math.Max(hi[i],v);}
        }
        return any?(hi[0]-lo[0],hi[1]-lo[1],hi[2]-lo[2]):null;
    }

    /// <summary>Top-level arguments of one generated row "new(...)," (strings and nested braces kept whole).</summary>
    internal static List<string> Args(string row)
    {
        var inner=row[(row.IndexOf('(')+1)..row.LastIndexOf(')')];
        var list=new List<string>();var depth=0;var quoted=false;var cur=new StringBuilder();
        foreach(var c in inner)
        {
            if(c=='"')quoted=!quoted;
            if(!quoted&&(c=='('||c=='{'))depth++;
            if(!quoted&&(c==')'||c=='}'))depth--;
            if(!quoted&&depth==0&&c==','){list.Add(cur.ToString());cur.Clear();continue;}
            cur.Append(c);
        }
        list.Add(cur.ToString());return list;
    }

    internal static void Run(Action<bool,string> sink)
    {
        int checks=0,failures=0;void Check(bool ok,string message){checks++;if(!ok)failures++;sink(ok,"FEAT94: "+message);}
        string root=Fix02228Tests.SourceRoot(),output=Fix02228Tests.OutputRoot();
        string Read(string f)=>File.ReadAllText(Path.Combine(root,f)).Replace("\r\n","\n");
        var assets=Path.Combine(root,"Material/files/BepInEx/plugins/SprocketExpandedBallistics/era-assets");
        var presets=EraPresetRegistry.Presets;
        string Name(EraVisualPreset p)=>string.IsNullOrEmpty(p.Cassette)?p.Mount:p.Cassette;

        // ================= 1. presets with a visual OBJ: long side Z = 0, long side X = 90 =================
        var table=new StringBuilder("preset\tguid\tx_m\ty_m\tz_m\tlong\tratio\tnear_square\trotation\n");
        int withObj=0,zeros=0,nineties=0;var nearSquare=new List<string>();
        foreach(var p in presets.Where(p=>p.PartGuid!=CompositeModule.PartGuid))
        {
            var size=Size(Path.Combine(assets,p.Visual));if(size is not {} s)continue;
            withObj++;
            var longZ=s.Z>s.X;var ratio=Math.Max(s.X,s.Z)/Math.Min(s.X,s.Z);var expected=longZ?0f:90f;
            if(expected==0f)zeros++;else nineties++;
            if(ratio<=1.3)nearSquare.Add($"{Name(p)} {ratio:0.00} long {(longZ?"Z":"X")} -> {expected:0}");
            table.Append(CultureInfo.InvariantCulture,$"{Name(p)}\t{p.PartGuid}\t{s.X:0.###}\t{s.Y:0.###}\t{s.Z:0.###}\t{(longZ?"Z":"X")}\t{ratio:0.00}\t{(ratio<=1.3?"yes":"")}\t{p.DefaultPlacementRotation:0}\n");
            Check(s.Y<Math.Min(s.X,s.Z),$"1 {Name(p)}: Y is the thin axis of {p.Visual} ({s.X:0.###} x {s.Y:0.###} x {s.Z:0.###} m)");
            Check(p.DefaultPlacementRotation==expected,$"1 {Name(p)}: long side {(longZ?"Z":"X")} ({ratio:0.00}) -> DefaultPlacementRotation {expected:0} (is {p.DefaultPlacementRotation:0})");
        }
        Check(withObj==75&&zeros==54&&nineties==21,$"1 coverage: {withObj} preset rows with a visual OBJ, {zeros} long along Z (0), {nineties} long along X (90)");
        File.WriteAllText(Path.Combine(output,"FEAT94-PRESETS.tsv"),table.ToString());

        // ================= 2. named cases =================
        var k1=presets.Where(p=>p.PartGuid==Kontakt1Guid).ToArray();
        Check(k1.Length==1&&k1[0].DefaultPlacementRotation==0f&&presets.FirstOrDefault(p=>p.PartGuid==Kontakt1Guid)!.DefaultPlacementRotation==0f,"2 Kontakt-1 ("+Kontakt1Guid+"): 0, also as the menu lookup sees it (first row of the GUID)");
        var composite=presets.Where(p=>p.Cassette==CompositeModule.ComponentId).ToArray();
        Check(composite.Length==1&&composite[0].DefaultPlacementRotation==0f,"2 compositeSideModule: 0 (unchanged)");
        Check(presets.First(p=>p.Cassette=="kontakt1Cassette").DefaultPlacementRotation==90f&&presets.First(p=>p.Cassette=="reliktStage1Cell1").DefaultPlacementRotation==0f&&presets.First(p=>p.Cassette=="dupletHullCassette").DefaultPlacementRotation==0f,
            "2 rows that were already right keep their value (flat Kontakt-1 cassette 90, Relikt stage cell 0, Duplet hull cassette 0)");

        // ================= 3. generated panels (no OBJ): the default 1 x 1 cell, fixed rows unchanged =================
        var panels=presets.Where(p=>p.WholeCells&&!File.Exists(Path.Combine(assets,p.Visual))).ToArray();
        Check(panels.Length==10,"3 ten generated panel rows without an OBJ ("+panels.Length+")");
        Check(EraCellPartition.CountForScale(1,8)==1,"3 a new part at native scale 1 is one cell along each scaled axis");
        foreach(var p in panels)
        {
            if(p.FixedCountV>0){Check(p.DefaultPlacementRotation==90f,$"3 {p.Cassette}: {p.FixedCountV} fixed rows, long side depends on the player's X scale -> left at 90");continue;}
            var expected=p.CellLengthM>p.CellWidthM?0f:90f;
            Check(p.DefaultPlacementRotation==expected,$"3 {p.Cassette}: cell {p.CellWidthM:0.###} (X) x {p.CellLengthM:0.###} (Z) m -> {expected:0}");
        }

        // ================= 4. one value per part GUID (the menu scope takes the first row) =================
        var mixed=presets.GroupBy(p=>p.PartGuid).Where(g=>g.Select(p=>p.DefaultPlacementRotation).Distinct().Count()>1).Select(g=>g.Key).ToArray();
        Check(mixed.Length==0,"4 every part GUID has one DefaultPlacementRotation ("+string.Join(", ",mixed)+")");

        // ================= 5. the duplicate log: read only, capped, format =================
        var defaults=Read("Material/EraPlacementDefaults.cs");
        var start=defaults.IndexOf("private static void LogDuplicate(PlaceOperation op)",StringComparison.Ordinal);
        var log=start<0?"":defaults[start..defaults.IndexOf("\n    }\n",start,StringComparison.Ordinal)];
        Check(log.Length>0,"5 LogDuplicate exists");
        Check(defaults.Contains("internal const int DuplicateLogCap=24;",StringComparison.Ordinal)&&defaults.Contains("internal const string DuplicatePrefix=\"[ERA duplicate placement]\";",StringComparison.Ordinal),"5 cap 24, prefix [ERA duplicate placement]");
        var body=log[(log.IndexOf('{')+1)..].TrimStart();
        Check(body.StartsWith("if(duplicateLogs>=DuplicateLogCap)return;",StringComparison.Ordinal),"5 the cap is the first statement (no wrapper call after 24 lines)");
        Check(Regex.Matches(log,@"duplicateLogs\+\+").Count==2&&log.IndexOf("duplicateLogged.Add(op.Pointer)",StringComparison.Ordinal)<log.IndexOf("duplicateLogs++",StringComparison.Ordinal),"5 one count per logged operation and per failure (a failing read cannot spam)");
        Check(!log.Contains("ReleaseDiagnostics",StringComparison.Ordinal),"5 not gated by ReleaseDiagnostics");
        Check(log.Contains("if(obj==null||string.IsNullOrEmpty(guid)||!EraPresetRegistry.Presets.Any(p=>p.PartGuid==guid))return;",StringComparison.Ordinal),"5 only for an EB ERA preset GUID");
        var stripped=Regex.Replace(log,"\\$?\"(?:[^\"\\\\]|\\\\.)*\"","\"\"");
        var assignments=Regex.Matches(stripped,@"(?<![=!<>+\-*/])=(?![=>])").Select(m=>m.Index).ToArray();
        var badAssign=assignments.Where(i=>{var lineStart=stripped.LastIndexOf('\n',i)+1;var left=stripped[lineStart..i];return !Regex.IsMatch(left,@"^\s*var \w+$")&&!Regex.IsMatch(left,@"\(p$");}).Select(i=>stripped[(stripped.LastIndexOf('\n',i)+1)..Math.Min(stripped.Length,i+20)].Trim()).ToArray();
        Check(assignments.Length>=10&&badAssign.Length==0,$"5 no write: all {assignments.Length} '=' in LogDuplicate are local 'var' declarations ({string.Join(" | ",badAssign)})");
        Check(!Regex.IsMatch(log,@"AddRotation|AddScale|\.Set|set_|Revert|Cancel|AttachAndEnd|DestroyAndEnd|Execute\(|\+=|-=|Marshal|IntPtr\.Add|\+0x"),"5 no native call that changes state, no offset or pointer arithmetic");
        foreach(var read in new[]{"op.placementInfo?.placement","placement.ManualRotation","transform.ManualRotation","placement.orientationMode","placement.options","transform.Rotation.eulerAngles","obj.VehicleTransform"})
            Check(log.Contains(read,StringComparison.Ordinal),"5 reads "+read+" through the managed wrapper");
        var template=Regex.Match(log,"LogInfo\\(\\$\"(?<t>[^\"]*)\"\\)").Groups["t"].Value;
        var fields=new[]{"{DuplicatePrefix}","guid=","placementManualRotation=","transformManualRotation=","orientationMode=","options=","worldRotation=","line {duplicateLogs}/{DuplicateLogCap}"};
        var positions=fields.Select(f=>template.IndexOf(f,StringComparison.Ordinal)).ToArray();
        Check(positions.All(i=>i>=0)&&positions.Zip(positions.Skip(1)).All(x=>x.First<x.Second),"5 format: prefix, guid, placement and transform manual rotation, mode, options, world rotation, line n/24 in that order");
        var example=template.Replace("{DuplicatePrefix}","[ERA duplicate placement]").Replace("{guid}",Kontakt1Guid).Replace("{placementRotation}","90").Replace("{transformRotation}","90")
            .Replace("{mode}","UpFaceNormal").Replace("{options}","XMirrorEnabled, ManualRotation (0x...)").Replace("{world}","(0,90,90)").Replace("{duplicateLogs}","1").Replace("{DuplicateLogCap}","24");
        File.WriteAllText(Path.Combine(output,"FEAT94-LOG-EXAMPLE.txt"),example+"\n(example values; the option names and bits come from the game at run time)\n");
        Check(!example.Contains('{')&&example.StartsWith("[ERA duplicate placement] guid=",StringComparison.Ordinal),"5 example line renders: "+example);
        var started=defaults[defaults.IndexOf("private static void Started(PlaceOperation __instance)",StringComparison.Ordinal)..defaults.IndexOf("internal const int DuplicateLogCap",StringComparison.Ordinal)];
        Check(started.Contains("if(scope==null){LogDuplicate(__instance);return;}",StringComparison.Ordinal)&&Regex.Matches(started,"LogDuplicate").Count==1&&Regex.Matches(defaults,"AddRotation").Count==1,
            "5 the log runs only without a new-part scope; AddRotation stays the single write of the new-part path");
        Check(Regex.Matches(defaults,@"Harmony(Patch|Prefix|Postfix|Finalizer)\b").Count==7,"5 EraPlacementDefaults keeps its patch annotations (no new hook)");

        // ================= 6. ledger against 0.22.92 (EB_BASE_SRC) =================
        static int Hooks(string dir)=>Directory.EnumerateFiles(dir,"*.cs",SearchOption.AllDirectories).Where(f=>!f.Replace('\\','/').Contains("/tests/")&&!f.Replace('\\','/').Contains("/bin/")&&!f.Replace('\\','/').Contains("/obj/"))
            .Sum(f=>File.ReadLines(f).Count(l=>l.Contains("[HarmonyPatch",StringComparison.Ordinal)));
        var hooks=Hooks(root);
        var baseDir=Environment.GetEnvironmentVariable("EB_BASE_SRC");
        if(!string.IsNullOrEmpty(baseDir)&&Directory.Exists(baseDir))
        {
            bool Skip(string rel)=>rel.Split('/').Any(p=>p is "bin" or "obj" or "tests" or ".git");
            Dictionary<string,string> Inventory(string dir)=>Directory.EnumerateFiles(dir,"*",SearchOption.AllDirectories).Select(f=>(Rel:Path.GetRelativePath(dir,f).Replace('\\','/'),Full:f)).Where(x=>!Skip(x.Rel)).ToDictionary(x=>x.Rel,x=>x.Full);
            var a=Inventory(baseDir);var b=Inventory(root);
            var diff=a.Keys.Union(b.Keys).Where(k=>!a.ContainsKey(k)||!b.ContainsKey(k)||!File.ReadAllBytes(a[k]).AsSpan().SequenceEqual(File.ReadAllBytes(b[k]))).OrderBy(k=>k,StringComparer.Ordinal).ToArray();
            Check(diff.All(IsPlanned)&&diff.Length==5&&diff.All(b.ContainsKey)&&diff.All(a.ContainsKey),$"6 ledger: only the planned files differ from 0.22.92 ({string.Join(", ",diff)}); {b.Count-diff.Length} files byte for byte");
            var protectedFiles=b.Keys.Where(k=>!IsPlanned(k)&&(k.StartsWith("Material/",StringComparison.Ordinal)||Regex.IsMatch(k,@"^(CombatForensic|CombatReport|LayeredPlateReport|PenetrationWaterfall|NativeMaterial|RuntimeArmour|MaterialInterval|RuntimeOriginalTrajectory|RuntimeAtgm|Atgm|RuntimeSight|Sight)[^/]*\.cs$"))).ToArray();
            Check(protectedFiles.Length>100&&protectedFiles.All(k=>!diff.Contains(k)),$"6 protected set byte for byte: {protectedFiles.Length} files (Material/* except the two, hit path, Forensic/report, ATGM, sight)");
            var oldRows=File.ReadAllText(a["Material/EraGeneratedDefinitions.cs"]).Split('\n');var newRows=File.ReadAllText(b["Material/EraGeneratedDefinitions.cs"]).Split('\n');
            var changed=0;var otherChange=0;var guids=new HashSet<string>();var toZero=0;
            for(var i=0;i<Math.Max(oldRows.Length,newRows.Length);i++)
            {
                if(i>=oldRows.Length||i>=newRows.Length){otherChange++;continue;}
                if(oldRows[i]==newRows[i])continue;
                if(!oldRows[i].StartsWith("new(\"",StringComparison.Ordinal)){otherChange++;continue;}
                var o=Args(oldRows[i]);var n=Args(newRows[i]);
                var onlyEleventh=o.Count==n.Count&&Enumerable.Range(0,o.Count).All(j=>j==10||o[j]==n[j])&&o[^1]==n[^1];
                if(!onlyEleventh||oldRows[i][..oldRows[i].IndexOf('(')]!=newRows[i][..newRows[i].IndexOf('(')]){otherChange++;continue;}
                changed++;guids.Add(n[5].Trim('"'));
                if(float.Parse(n[10].TrimEnd('f'),CultureInfo.InvariantCulture)==0f&&float.Parse(o[10].TrimEnd('f'),CultureInfo.InvariantCulture)==90f)toZero++;
            }
            Check(otherChange==0&&changed==51&&toZero==51&&guids.Count==30,$"6 EraGeneratedDefinitions.cs: only the 11th argument changed, {changed} rows 90 -> 0 ({toZero}), {guids.Count} part GUIDs; other changes {otherChange}");
            Check(hooks==47&&Hooks(baseDir)==47,"6 hooks: [HarmonyPatch annotations "+Hooks(baseDir)+" -> "+hooks+" (47)");
        }
        else Check(hooks==47,"6 hooks: [HarmonyPatch annotations in this tree: "+hooks+" (47); EB_BASE_SRC not set, ledger skipped");

        Console.WriteLine("FEAT94 near-square (ratio <= 1.3): "+string.Join("; ",nearSquare));
        Console.WriteLine($"FEAT94: {checks} checks, {failures} failures");
    }
}
