using System.Globalization;
using System.Numerics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using SprocketExpandedBallistics.Materials;
namespace SprocketExpandedBallistics;
internal static class Feat02237CompositeModuleTests
{
    private sealed record Obj(Vector3[] Vertices,Dictionary<string,List<int[]>> Faces);
    private static Obj Read(string path)
    {
        var vertices=new List<Vector3>();var faces=new Dictionary<string,List<int[]>>();string group="";
        foreach(var line in File.ReadLines(path))
        {
            var p=line.Split(' ',StringSplitOptions.RemoveEmptyEntries);if(p.Length==0)continue;
            if(p[0]=="v")vertices.Add(new(float.Parse(p[1],CultureInfo.InvariantCulture),float.Parse(p[2],CultureInfo.InvariantCulture),float.Parse(p[3],CultureInfo.InvariantCulture)));
            if(p[0]=="g")group=p[1];
            if(p[0]!="f")continue;
            if(!faces.ContainsKey(group))faces[group]=new();
            var ids=p.Skip(1).Select(x=>int.Parse(x.Split('/')[0],CultureInfo.InvariantCulture)-1).ToArray();
            for(int i=1;i<ids.Length-1;i++)faces[group].Add(new[]{ids[0],ids[i],ids[i+1]});
        }
        return new(vertices.ToArray(),faces);
    }
    private static int NonManifold(Obj obj)
    {
        var edges=new Dictionary<(int,int),int>();
        foreach(var f in obj.Faces.Values.SelectMany(x=>x))for(int i=0;i<3;i++)
        {int a=f[i],b=f[(i+1)%3];var key=(Math.Min(a,b),Math.Max(a,b));edges[key]=edges.GetValueOrDefault(key)+1;}
        return edges.Values.Count(v=>v!=2);
    }
    internal static void Run(Action<bool,string> check)
    {
        int count=0;var sink=check;check=(ok,message)=>{count++;sink(ok,"feat02237: "+message);};
        var root=Fix02228Tests.SourceRoot();var output=Fix02228Tests.OutputRoot();
        string assets=Path.Combine(root,"Material/files/BepInEx/plugins/SprocketExpandedBallistics/era-assets");
        using var manifest=JsonDocument.Parse(File.ReadAllText(Path.Combine(root,"assets/compositeSideModule-mesh.json")));
        var m=manifest.RootElement;var original=Path.Combine(root,m.GetProperty("source").GetString()!);
        string converted=Path.Combine(assets,"compositeSideModule.obj");
        check(Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(original)))==m.GetProperty("sourceSha256").GetString(),"checked source hash");
        check(Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(converted)))==m.GetProperty("convertedSha256").GetString(),"game OBJ hash");
        var a=Read(original);var b=Read(converted);
        check(a.Vertices.Zip(b.Vertices).All(pair=>Vector3.Distance(new Vector3(-pair.First.Y,pair.First.X+.027f,pair.First.Z),pair.Second)<1e-7)&&a.Vertices.Length==1712,"every vertex preserved in order under rigid rotation and rear-origin offset");
        foreach(var group in a.Faces.Keys)
        {
            check(a.Faces[group].Count==b.Faces[group].Count&&a.Faces[group].Zip(b.Faces[group]).All(x=>x.First.SequenceEqual(x.Second)),"exact triangulated winding/group "+group);
            var loaded=EraObjGeometry.Read(File.ReadLines(converted),group,(x,y,z)=>new Vector3(x,y,z),v=>float.IsFinite(v.X)&&float.IsFinite(v.Y)&&float.IsFinite(v.Z));
            check(loaded.SequenceEqual(b.Faces[group].SelectMany(f=>f.Select(i=>b.Vertices[i]))),"production native loader reads exact triangles "+group);
        }
        check(NonManifold(b)==0,"render mesh has zero non-manifold edges");
        var collision=Read(Path.Combine(assets,"compositeSideModule-collision.obj"));
        check(NonManifold(collision)==0,"closed collision mesh");
        for(int i=0;i<3;i++)
        {
            float Axis(Vector3 v)=>i==0?v.X:i==1?v.Y:v.Z;
            check(b.Vertices.Min(Axis)==collision.Vertices.Min(Axis)&&b.Vertices.Max(Axis)==collision.Vertices.Max(Axis),"collision/render bounds axis "+i);
        }
        var p=CompositeModule.Current;check(Math.Abs(p.Mass()-880.5466355)<1e-7,"layer volume times density: scale one 880.5466355 kg");
        check(p.Mass(1,2,3)==p.Mass()*6&&p.Mass(-1,2,-3)==p.Mass()*6,"scaled and mirrored mass, absolute volume determinant");
        check(p.Mass(2,2,2)==p.Mass()*8,"depth as well as footprint scales mass");
        check(!p.Allows(new(1989,12,31))&&p.Allows(new(1990,1,1))&&p.Allows(DateTime.MaxValue),"date only availability including final date");
        check((p with{Date="1900.01.01"}).Allows(new(1900,1,1)),"date configurable");
        using var part=JsonDocument.Parse(File.ReadAllText(Path.Combine(root,"Material/files/Sprocket_Data/StreamingAssets/Parts/compositeSideModulePart.json")));
        var def=part.RootElement;
        check(def.GetProperty("date").GetString()==p.Date&&def.GetProperty("guid").GetString()==CompositeModule.PartGuid,"part date and unique GUID");
        var info=def.GetProperty("components")[0].GetProperty("info");
        check(def.GetProperty("components").GetArrayLength()==1&&info.GetProperty("durableMaterialID").GetString()==CompositeModule.MaterialId,"one passive collider, no active-cell binding");
        check(Math.Abs(info.GetProperty("mass").GetDouble()-CompositeModuleEraGeometry.Mass(p))<1e-8&&info.GetProperty("scaling").GetBoolean(),"native definition mass and scaling");
        var scale=def.GetProperty("transform");check(scale.GetProperty("maxScale").EnumerateArray().Select(x=>x.GetDouble()).SequenceEqual(new[]{8d,1d,8d}),"Relikt 1..8 footprint scale range mapped to module axes");
        var visual=EraPresetRegistry.Presets.Single(v=>v.Cassette==CompositeModule.ComponentId);
        check(visual.Cassette==CompositeModule.ComponentId&&visual.PassiveMaterialId==CompositeModule.MaterialId&&visual.Resizable&&File.Exists(Path.Combine(assets,visual.Icon)),"registry part, passive material and own icon");
        string surface=File.ReadAllText(Path.Combine(root,"Material/EraSurface.cs"));
        check(surface.Contains("Composite no paint\",new Color(0,0,0,1)")&&surface.Contains("current.Dark==null?new[]{current.Material}:new[]{current.Material,current.Dark}"),"dark uses native unpainted mask and second material slot");
        string renderer=File.ReadAllText(Path.Combine(root,"Material/ReliktPrototype.cs"));
        check(renderer.Contains("expanded.AddRange(cover);paintedVertices=expanded.Count;expanded.AddRange(Group(\"dark\"))")&&renderer.Contains("mesh.subMeshCount=2"),"housing/cover paint submesh, dark submesh");
        var table=ProtectionFactorTable.BuildModule(p);var rows=new StringBuilder("threat\tangle\tfactor\tmass_efficiency\trha_mm\n");
        foreach(var threat in new[]{ProtectionThreat.LongRod,ProtectionThreat.FullBoreKE,ProtectionThreat.Jet})foreach(double angle in new[]{0d,30,60})
        {
            bool heat=threat==ProtectionThreat.Jet;double factor=table.Factor(threat,angle),eff=factor*CompositeModule.DepthMm/(p.ArealMass/7.85);
            check(Math.Abs(factor-p.Factor(heat))<1e-14,"table exact calibrated factor "+threat+" @"+angle);
            check(eff>=(heat?2.2:1.2)&&eff<=(heat?3:1.9),"default mass efficiency in band "+threat+" @"+angle);
            check(Math.Abs(table.ProtectionMm(threat,angle)-p.EquivalentMm(heat)/Math.Cos(angle*Math.PI/180))<1e-10,"table native LOS "+threat+" @"+angle);
            rows.AppendLine(FormattableString.Invariant($"{threat}\t{angle}\t{factor:R}\t{eff:R}\t{table.ProtectionMm(threat,angle):R}"));
        }
        var open=p with{Layers=p.Layers.Select(l=>l with{KeMassEfficiency=20,HeatMassEfficiency=40}).ToArray()};
        var parsed=CompositeModulePreset.Parse(JsonSerializer.Serialize(open));check(parsed.Factor(false)>10&&ProtectionFactorTable.BuildModule(parsed).Factor(ProtectionThreat.Jet,0)>20,"config has no hard gameplay efficiency limits");
        var interval=new CompositeModuleInterval(123,0,.327,0,table);
        var isolated=CompositeModule.Plan(0,.327,1,ProtectionThreat.Jet,new[]{interval});
        check(isolated!=null&&!isolated.Grouped&&isolated.Factor==table.Factor(ProtectionThreat.Jet,0),"isolated native block plan");
        var grouped=CompositeModule.Plan(0,.427,1,ProtectionThreat.Jet,new[]{interval});
        check(grouped!=null&&grouped.Grouped&&Math.Abs(grouped.Factor*.427-(table.Factor(ProtectionThreat.Jet,0)*.327+.1))<1e-14,"grouped hull keeps its own native resistance, module uses own 327mm path");
        foreach(double multiplier in new[]{.75,1d,1.4})
        {
            var adjusted=CompositeModule.Plan(0,.427,1/multiplier,ProtectionThreat.Jet,new[]{interval});
            check(adjusted!=null&&Math.Abs(adjusted.Factor*multiplier*.427-(table.Factor(ProtectionThreat.Jet,0)*multiplier*.327+.1))<1e-14,"native coefficient compensation preserves hull for apModifier/obliquity multiplier "+multiplier);
        }
        var second=interval with{Entity=124,StartM=.327,EndM=.427};
        var two=CompositeModule.Plan(0,.427,1,ProtectionThreat.Jet,new[]{interval,second});
        check(two?.Layers.Length==2&&Math.Abs(two.Factor-table.Factor(ProtectionThreat.Jet,0))<1e-14,"two adjacent confirmed intervals counted once each");
        check(CompositeModule.Plan(0,1,1,ProtectionThreat.Jet,new[]{interval,interval with{Entity=124,StartM=.2,EndM=.4}})==null,"ambiguous overlapping modules do not invent independent intervals");
        var evt=CompositeModule.Report(interval,ProtectionThreat.Jet,"Main","simulated",true);
        var shot=CombatReportTruth.Normalize(new(1,"Incoming","HEAT",1000,CompositeModule.ReportEvents(evt)));
        var recovered=JsonSerializer.Deserialize<CombatReportShot>(JsonSerializer.Serialize(shot))!;
        check(recovered.Events.Count==2&&recovered.Events[0].Layer?.Parts.Single().MaterialId==CompositeModule.MaterialId&&recovered.Events[0].Layer?.Combined==false,"simulated hit survives report serialization as own material layer");
        check(recovered.Events[1].Text.Contains("327 mm")&&recovered.Events[1].Text.Contains("interval-weighted")&&recovered.Events[0].Layer?.Parts.Single().Entity==123,"own measured path and truthful grouped qualification");
        var projected=CombatHitProjection.Build(recovered);
        check(projected.Length==1&&projected[0].Layer?.Parts.Single().MaterialId==CompositeModule.MaterialId&&projected[0].Reaction.Contains("327 mm"),"actual combat report projection contains one independent module row");
        File.WriteAllText(Path.Combine(output,"MODULE-FACTORS.tsv"),rows.ToString());
        File.WriteAllText(Path.Combine(output,"SIMULATED-MODULE-HIT.json"),JsonSerializer.Serialize(recovered,new JsonSerializerOptions{WriteIndented=true}));
        Console.WriteLine($"0.22.37 MODULE PASS: {count} checks.");
    }
}
