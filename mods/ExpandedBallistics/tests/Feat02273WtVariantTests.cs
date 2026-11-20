using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Xml.Linq;
using SprocketExpandedBallistics.Era;
using SprocketExpandedBallistics.Materials;
namespace SprocketExpandedBallistics;

/// <summary>
/// 0.22.73: four Kontakt-5 variants on War Thunder T-90A sizes (turret L, turret S, hull skirt, glacis) as placed ERA parts.
/// Tests: (1) the four presets load with bounds equal to the mesh, housing and cell masses of phase 1, 13.000 mm cells, two cells and a 0/1/2 series that points to files that exist;
/// (2) the existing Kontakt-5 (rows, masses, recipe, bindings, files) is unchanged; (4) protection and JSON of the existing materials and every suite output equal 0.22.72.
/// Test 3 (one activation per cell, cell hidden, housing shows state 1) runs in test-output/render-harness; test 5 (installer) in Test-Installer-Offline.ps1.
/// </summary>
internal static class Feat02273WtVariantTests
{
    // Phase 1 (2026-11-13\k5-wt-sizes\REPORT.md): outer size L x W x T mm, six 2 mm plates kg, all mount shells kg, cell kg (13 mm, volume x 7850), cell W x L mm.
    internal sealed record Variant(string Id,string Name,double L,double W,double T,double PlatesKg,double MountKg,double CellSteelKg,double CellW,double CellL);
    internal static readonly Variant[] Variants={
        new("kontakt5WtTurretL","Kontakt-5 turret L",436,271,85,5.498,5.753,5.70,130.5,428),
        new("kontakt5WtTurretS","Kontakt-5 turret S",287,269,68,3.533,3.741,3.69,129.5,279),
        new("kontakt5WtSkirt","Kontakt-5 skirt",603,466,42,10.094,10.450,13.84,228,595),
        new("kontakt5WtGlacis","Kontakt-5 glacis",600,520,60,11.759,12.128,15.41,255,592)};
    internal const double Density=3171.788731181019,Steel=7850;
    internal static readonly string[] Roles={"{0}Stage1Cell1-physical-intact","{0}Stage1Cell2-physical-intact","{0}-active-stage-1-cell-1","{0}-active-stage-1-cell-2","{0}-housing-passive","{0}-housing-state-0","{0}-housing-state-1","{0}-housing-state-2"};

    /// <summary>The production files 0.22.73 adds or changes on top of 0.22.72 (older golden-source tests list them as planned).</summary>
    internal static readonly string[] Planned={"Material/EraGeneratedDefinitions.cs","Material/EraMenuOrder.cs","Material/EraWtVariants.cs","RuntimeArmourResponses.cs","Plugin.cs","SprocketExpandedBallistics.csproj","RELEASE-NOTES.md"};
    internal static bool IsPlanned(string rel)=>Planned.Contains(rel)||rel.StartsWith("Material/files/",StringComparison.Ordinal)&&rel.Contains("kontakt5Wt",StringComparison.Ordinal)||Feat02274OverlayApplyTests.IsPlanned(rel); // 0.22.74: its planned files count too

    /// <summary>RuntimeArmourResponses.cs without the one 0.22.73 line that adds the variant bindings (older pins compare the rest byte for byte).</summary>
    internal static string WithoutBindingHook(string text)=>string.Join("\r\n",text.Replace("\r\n","\n").Split('\n').Where(l=>!l.Contains("EraWtVariants.With(bindings)",StringComparison.Ordinal)));

    /// <summary>EraGeneratedDefinitions.cs without the eight preset rows and four menu names of 0.22.73 (older frozen-source tests compare the rest).</summary>
    internal static string StripVariants(string text,IEnumerable<string>? guids=null)
    {
        var g=(guids??EraPresetRegistry.Presets.Where(p=>p.Cassette.StartsWith("kontakt5Wt",StringComparison.Ordinal)).Select(p=>p.PartGuid)).Distinct().ToArray();
        return string.Join("\n",text.Replace("\r\n","\n").Split('\n').Where(l=>!l.Contains("kontakt5Wt",StringComparison.Ordinal)&&!g.Any(x=>l.Contains("[\""+x+"\"]=",StringComparison.Ordinal)||l.Contains("[\""+x+"\"] =",StringComparison.Ordinal))));
    }

    internal sealed class Shell{internal Dictionary<int,bool> Seen=new();internal double[] Lo={1e9,1e9,1e9},Hi={-1e9,-1e9,-1e9};internal double Volume;internal string Group="";}
    internal static (List<Shell> Shells,int Vertices) ReadObj(string path)
    {
        var v=new List<double[]>();var faces=new List<(string g,int[] i)>();var group="";
        foreach(var line in File.ReadLines(path))
        {
            var p=line.Split(' ',StringSplitOptions.RemoveEmptyEntries);if(p.Length==0)continue;
            if(p[0]=="v")v.Add(new[]{double.Parse(p[1],CultureInfo.InvariantCulture),double.Parse(p[2],CultureInfo.InvariantCulture),double.Parse(p[3],CultureInfo.InvariantCulture)});
            else if(p[0]=="g")group=p[1];
            else if(p[0]=="f")faces.Add((group,p.Skip(1).Select(x=>int.Parse(x.Split('/')[0])-1).ToArray()));
        }
        var parent=Enumerable.Range(0,v.Count).ToArray();int Find(int a){while(parent[a]!=a){parent[a]=parent[parent[a]];a=parent[a];}return a;}
        foreach(var f in faces)for(int k=1;k<f.i.Length;k++)parent[Find(f.i[k])]=Find(f.i[0]);
        var map=new Dictionary<int,Shell>();
        foreach(var f in faces)
        {
            var root=Find(f.i[0]);if(!map.TryGetValue(root,out var s))map[root]=s=new Shell{Group=f.g};
            var a=v[f.i[0]];var b=v[f.i[1]];var c=v[f.i[2]];
            s.Volume+=(a[0]*(b[1]*c[2]-b[2]*c[1])-a[1]*(b[0]*c[2]-b[2]*c[0])+a[2]*(b[0]*c[1]-b[1]*c[0]))/6.0;
            foreach(var idx in f.i)for(int d=0;d<3;d++){s.Lo[d]=Math.Min(s.Lo[d],v[idx][d]);s.Hi[d]=Math.Max(s.Hi[d],v[idx][d]);}
        }
        return (map.Values.ToList(),v.Count);
    }
    static bool Near(double a,double b,double tol)=>Math.Abs(a-b)<=tol;
    static string Sha(string path)=>Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));

    internal static void Run(Action<bool,string> check)
    {
        int n=0;void Check(bool ok,string message){n++;check(ok,"FEAT73 Kontakt-5 WT variants: "+message);}
        var root=Fix02228Tests.SourceRoot();var output=Fix02228Tests.OutputRoot();
        var files=Path.Combine(root,"Material/files");var assets=Path.Combine(files,"BepInEx/plugins/SprocketExpandedBallistics/era-assets");
        var parts=Path.Combine(files,"Sprocket_Data/StreamingAssets/Parts");var loc=Path.Combine(files,"Sprocket_Data/StreamingAssets/Localization/en-UK/Parts");
        var table=new StringBuilder("id\tpartGuid\tboundsLxWxT_mm\tplates_kg\tmount_kg\tpresetMount_kg\tcellSteelEq_kg\tcell_kg\tcellThickness_mm\tcells\tstates\n");
        var allGuids=EraPresetRegistry.Presets.Select(p=>p.PartGuid).ToList();
        // ---- 1. the four presets ----
        foreach(var v in Variants)
        {
            string c1=v.Id+"Stage1Cell1",c2=v.Id+"Stage1Cell2",mount=v.Id+"Housing";
            var ok1=EraPresetRegistry.Visual(c1,out var p1,out var isCell1);var ok2=EraPresetRegistry.Visual(c2,out var p2,out var isCell2);var okm=EraPresetRegistry.Visual(mount,out var pm,out var isCellM);
            Check(ok1&&ok2&&okm&&isCell1&&isCell2&&!isCellM&&pm.Mount==p1.Mount,v.Id+": preset loads for both cells and the housing");
            Check(p1.Mount==mount&&p2.Mount==mount&&p1.PartGuid==p2.PartGuid&&allGuids.Count(g=>g==p1.PartGuid)==2,v.Id+": one own part GUID shared by its two cell rows ("+p1.PartGuid+")");
            Check(Guid.TryParse(p1.PartGuid,out var guid)&&guid.ToString()==p1.PartGuid&&p1.PartGuid!="06a8b44f-1ecd-5a28-b2d7-c5ce90d58b3c",v.Id+": GUID is well formed and not the Kontakt-5 GUID");
            Check(EraGeneratedDefinitions.MenuLabels.TryGetValue(p1.PartGuid,out var label)&&label==v.Name&&EraMenuOrder.Ranks.ContainsKey(p1.PartGuid),v.Id+": menu name \""+v.Name+"\" and a menu rank");
            Check(p1.HousingActiveIds!.SequenceEqual(new[]{c1,c2})&&p2.HousingActiveIds!.SequenceEqual(p1.HousingActiveIds!)&&p1.HousingDamageVisuals!.SequenceEqual(new[]{v.Id+"-housing-state-0.obj",v.Id+"-housing-state-1.obj",v.Id+"-housing-state-2.obj"})&&p2.HousingDamageVisuals!.SequenceEqual(p1.HousingDamageVisuals!),
                v.Id+": two cells and the 0/1/2 housing series");
            Check(new[]{p1.Visual,p1.Collision,p1.SpentVisual!,p1.MountCollision!,p2.Visual,p2.Collision}.Concat(p1.HousingDamageVisuals!).All(f=>File.Exists(Path.Combine(assets,f)))&&File.Exists(Path.Combine(assets,p1.Icon)),v.Id+": every file the preset names exists in era-assets (series states, cell meshes, spent visual, mount collision, icon)");
            Check(p1.Visual==c1+"-physical-intact.obj"&&p1.Collision==v.Id+"-active-stage-1-cell-1.obj"&&p2.Visual==c2+"-physical-intact.obj"&&p2.Collision==v.Id+"-active-stage-1-cell-2.obj"&&p1.SpentVisual==v.Id+"-housing-state-2.obj"&&p1.MountCollision==v.Id+"-housing-passive.obj"&&p1.Icon==v.Id+"-icon.png"&&p1.DefaultPlacementRotation==0f/*0.22.94: long side Z, vertical*/&&!p1.WholeCells&&!p1.Resizable,v.Id+": file roles and rotation follow the Kontakt-5 row");
            foreach(var role in Roles)Check(File.Exists(Path.Combine(assets,string.Format(role,v.Id)+".obj")),v.Id+": "+string.Format(role,v.Id)+".obj present");
            var (phys,_)=ReadObj(Path.Combine(assets,c1+"-physical-intact.obj"));
            var mountShells=phys.Where(s=>s.Group=="mount").ToList();var cellShells=phys.Where(s=>s.Group=="cassette").ToList();
            Check(mountShells.Count>0&&cellShells.Count==1&&phys.All(s=>s.Group=="mount"||s.Group=="cassette")&&phys.All(s=>s.Volume>0),v.Id+": physical-intact has groups mount and cassette (one cell shell), all shells positive");
            var plates=mountShells.OrderByDescending(s=>s.Volume).Take(6).ToList();
            bool boxes=plates.All(s=>Near(s.Volume,(s.Hi[0]-s.Lo[0])*(s.Hi[1]-s.Lo[1])*(s.Hi[2]-s.Lo[2]),1e-12));
            var lo=Enumerable.Range(0,3).Select(d=>plates.Min(s=>s.Lo[d])).ToArray();var hi=Enumerable.Range(0,3).Select(d=>plates.Max(s=>s.Hi[d])).ToArray();
            double bx=(hi[0]-lo[0])*1000,by=(hi[1]-lo[1])*1000,bz=(hi[2]-lo[2])*1000;
            Check(boxes&&Near(bx,v.W,1e-6)&&Near(by,v.T,1e-6)&&Near(bz,v.L,1e-6)&&Near(lo[1],0,1e-9),v.Id+$": bounds of the six plates equal the mesh {bz:0.###} x {bx:0.###} x {by:0.###} mm (length x width x thickness), base at 0");
            Check(plates.All(s=>Near(Math.Min(Math.Min(s.Hi[0]-s.Lo[0],s.Hi[1]-s.Lo[1]),s.Hi[2]-s.Lo[2]),0.002,1e-9)),v.Id+": the six plates are loose 2.000 mm boxes");
            double platesKg=plates.Sum(s=>s.Volume)*Steel,mountKg=mountShells.Sum(s=>s.Volume)*Steel;
            Check(Near(platesKg,v.PlatesKg,0.0006)&&Near(mountKg,v.MountKg,0.0006),v.Id+$": housing mass {platesKg:0.000} kg plates, {mountKg:0.000} kg all mount shells equal phase 1");
            Check(Near(pm.MountMass,(float)mountKg,1e-4*mountKg)&&p1.MountMass==pm.MountMass&&p2.MountMass==pm.MountMass,v.Id+$": preset MountMass {pm.MountMass:0.0000} kg = mount volume x 7850");
            double[] thick=new double[2];var cellShell=new List<Shell>();
            foreach(var k in new[]{1,2})
            {
                var (cs,verts)=ReadObj(Path.Combine(assets,v.Id+"-active-stage-1-cell-"+k+".obj"));var cell=cs[0];
                thick[k-1]=(cell.Hi[1]-cell.Lo[1])*1000;cellShell.Add(cell);
                double kg=cell.Volume*Steel,w=(cell.Hi[0]-cell.Lo[0])*1000,l=(cell.Hi[2]-cell.Lo[2])*1000;
                Check(cs.Count==1&&verts==8&&cell.Group=="cassette"&&Near(thick[k-1],13.0,1e-6)&&Near(w,v.CellW,0.0006)&&Near(l,v.CellL,0.0006)&&Near(kg,v.CellSteelKg,0.006),v.Id+$": cell {k} is one box {thick[k-1]:0.000} mm thick, {w:0.0} x {l:0.0} mm, {kg:0.00} kg steel-equivalent");
                var pk=k==1?p1:p2;Check(Near(pk.CassetteMass,(float)(cell.Volume*Density),1e-4*cell.Volume*Density),v.Id+$": cell {k} preset CassetteMass {pk.CassetteMass:0.0000} kg = volume x kontakt5Active density");
                var (fp,_)=ReadObj(Path.Combine(assets,v.Id+"Stage1Cell"+k+"-physical-intact.obj"));
                Check(fp.Count(s=>s.Group=="cassette")==1&&Near(fp.First(s=>s.Group=="cassette").Volume,cell.Volume,1e-15)&&fp.Count(s=>s.Group=="mount")==mountShells.Count,v.Id+": physical-intact "+k+" = this cell plus the whole housing");
            }
            Check(cellShell[0].Hi[0]<0&&cellShell[1].Lo[0]>0&&Near((cellShell[1].Lo[0]-cellShell[0].Hi[0])*1000,2.0,1e-6),v.Id+": cell 1 at x<0, cell 2 at x>0, 2.000 mm seam");
            foreach(var s in new[]{0,1,2})
            {
                var (st,_)=ReadObj(Path.Combine(assets,v.Id+"-housing-state-"+s+".obj"));
                bool open=s>0;double top=st.Max(x=>x.Hi[1]);
                Check(st.All(x=>x.Group=="mount")&&(open?top<=v.T/1000-0.002+1e-9:Near(top,mountShells.Max(x=>x.Hi[1]),1e-12)),v.Id+": state "+s+(open?" is open (lid, frame, rib and bolts gone)":" is the intact housing"));
            }
            Check(File.ReadAllBytes(Path.Combine(assets,v.Id+"-housing-state-0.obj")).SequenceEqual(File.ReadAllBytes(Path.Combine(assets,v.Id+"-housing-passive.obj")))&&File.ReadAllBytes(Path.Combine(assets,v.Id+"-housing-state-1.obj")).SequenceEqual(File.ReadAllBytes(Path.Combine(assets,v.Id+"-housing-state-2.obj"))),v.Id+": state 0 = passive, state 1 = state 2 (as in the Kontakt-5 set)");
            using var doc=JsonDocument.Parse(File.ReadAllText(Path.Combine(parts,v.Id+"V1Part.json")));var jr=doc.RootElement;var comps=jr.GetProperty("components").EnumerateArray().ToArray();
            Check(jr.GetProperty("guid").GetString()==p1.PartGuid&&jr.GetProperty("date").GetString()=="1945.09.03"&&comps.Select(c=>c.GetProperty("fileID").GetString()).SequenceEqual(new[]{mount,c1,c2}),v.Id+": part json has the preset GUID, the Kontakt-5 date and the three component ids");
            Check(comps[0].GetProperty("info").GetProperty("durableMaterialID").GetString()=="rha"&&comps[1].GetProperty("info").GetProperty("durableMaterialID").GetString()=="kontakt5Active"&&comps[2].GetProperty("info").GetProperty("durableMaterialID").GetString()=="kontakt5Active"
                &&Near(comps[0].GetProperty("info").GetProperty("mass").GetDouble(),p1.MountMass,1e-6)&&Near(comps[1].GetProperty("info").GetProperty("mass").GetDouble(),p1.CassetteMass,1e-6)&&Near(comps[2].GetProperty("info").GetProperty("mass").GetDouble(),p2.CassetteMass,1e-6),v.Id+": part json masses equal the preset and the cells use the kontakt5Active recipe");
            var xml=XDocument.Parse(File.ReadAllText(Path.Combine(loc,v.Id+"V1.xml")));
            Check(xml.Root!.Element("name")!.Value==v.Name&&jr.GetProperty("name").GetString()==v.Id+"V1",v.Id+": localisation (en-UK) name \""+v.Name+"\" and part name "+v.Id+"V1");
            table.AppendLine(FormattableString.Invariant($"{v.Id}\t{p1.PartGuid}\t{bz:0.###}x{bx:0.###}x{by:0.###}\t{platesKg:0.0000}\t{mountKg:0.0000}\t{pm.MountMass:0.0000}\t{cellShell[0].Volume*Steel:0.0000}\t{p1.CassetteMass:0.0000}\t{thick[0]:0.000}\t2\t3"));
        }
        Check(Variants.Select(v=>EraPresetRegistry.Presets.First(p=>p.Cassette==v.Id+"Stage1Cell1").PartGuid).Distinct().Count()==4,"the four new GUIDs are distinct");
        // ---- bindings: exact component ids, the Kontakt-5 recipe route ----
        var fixture=File.ReadAllText(Path.Combine(root,"tests/physical-era-bindings.json"));var baseCat=EraPartBindings.Parse(fixture);
        var withVariants=EraWtVariants.With(baseCat);var again=EraWtVariants.With(withVariants);
        Check(withVariants.Bindings.Count==baseCat.Bindings.Count+8&&again.Bindings.Count==withVariants.Bindings.Count&&ReferenceEquals(again,withVariants),"eight bindings are added once (two cells per variant); a second call changes nothing");
        Check(baseCat.Bindings.All(b=>withVariants.TryGetCassette(b.CassetteComponentId,out var x)&&x.MountComponentId==b.MountComponentId&&x.MaterialId==b.MaterialId&&x.ResponseId==b.ResponseId&&x.Kind==b.Kind),"every existing binding is untouched");
        withVariants.TryGetCassette("kontakt5Stage1Cell1",out var k5b);
        foreach(var v in Variants)foreach(var k in new[]{1,2})
        {
            var ok=withVariants.TryGetCassette(v.Id+"Stage1Cell"+k,out var b);var okm=withVariants.TryGetMount(v.Id+"Housing",out var bm);
            Check(ok&&okm&&b!.MountComponentId==v.Id+"Housing"&&b.MaterialId==k5b!.MaterialId&&b.ResponseId==k5b.ResponseId&&b.Kind==k5b.Kind&&bm!.MaterialId=="kontakt5Active",v.Id+" cell "+k+": binding = Kontakt-5 route ("+b?.ResponseId+", "+b?.Kind+")");
        }
        Check(!withVariants.TryGetCassette("kontakt5WtGlacisHousing",out _)&&!withVariants.TryGetCassette("kontakt5Housing",out _),"a housing is never an active cassette");
        // ---- 2. the existing Kontakt-5 is unchanged ----
        EraPresetRegistry.Visual("kontakt5Stage1Cell1",out var o1,out _);EraPresetRegistry.Visual("kontakt5Stage1Cell2",out var o2,out _);EraPresetRegistry.Visual("kontakt5Housing",out var oh,out _);
        Check(o1.PartGuid=="06a8b44f-1ecd-5a28-b2d7-c5ce90d58b3c"&&o1.CassetteMass==1.37f&&o1.MountMass==7.193777905303625f&&o2.CassetteMass==1.37f&&o2.PartGuid==o1.PartGuid&&oh.Mount=="kontakt5Housing"
            &&o1.Visual=="kontakt5Stage1Cell1-physical-intact.obj"&&o1.Collision=="kontakt5-active-stage-1-cell-1.obj"&&o1.SpentVisual=="kontakt5-housing-state-2.obj"&&o1.MountCollision=="kontakt5-housing-passive.obj"&&o1.Icon=="kontakt5-icon.png"&&o1.DefaultPlacementRotation==0f/*0.22.94: long side Z, vertical*/
            &&o1.HousingDamageVisuals!.SequenceEqual(new[]{"kontakt5-housing-state-0.obj","kontakt5-housing-state-1.obj","kontakt5-housing-state-2.obj"}),"Kontakt-5 preset: GUID, masses 1.37 / 7.1938 kg, files, rotation and series as in 0.22.72");
        Check(EraMenuOrder.Ranks["06a8b44f-1ecd-5a28-b2d7-c5ce90d58b3c"]==200&&EraGeneratedDefinitions.MenuLabels["06a8b44f-1ecd-5a28-b2d7-c5ce90d58b3c"]=="Kontakt-5"&&EraGeneratedDefinitions.MaterialRoutes["kontakt5Active"].ResponseId=="kontakt5Active"&&EraGeneratedDefinitions.MaterialRoutes["kontakt5Active"].Kind=="heavyEra","Kontakt-5 rank, menu name and kontakt5Active route unchanged");
        var newFilesRel=Directory.GetFiles(files,"*",SearchOption.AllDirectories).Select(p=>Path.GetRelativePath(files,p).Replace('\\','/')).ToList();
        var added=newFilesRel.Where(r=>r.Contains("kontakt5Wt",StringComparison.Ordinal)).ToList();
        Check(added.Count==44,"exactly 44 kontakt5Wt* files in Material/files (4 x: 8 meshes, icon, part json, localisation): "+added.Count);
        File.WriteAllText(Path.Combine(output,"WT-VARIANTS-CHECK.tsv"),table.ToString());
        File.WriteAllText(Path.Combine(output,"FEAT73-RESULT.json"),JsonSerializer.Serialize(new{passed=true,checks=n}));
        Console.WriteLine($"FEAT73 PASS: {n} checks, 0 failures");
    }
}
