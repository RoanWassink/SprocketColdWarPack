using System.Globalization;
using System.Numerics;
using System.Text;
using System.Text.Json;
using System.Security.Cryptography;
using SprocketExpandedBallistics.Materials;
using SprocketExpandedBallistics.ExperimentalArmour;
namespace SprocketExpandedBallistics;

internal static class Fix02238Tests
{
    private sealed record Mesh(Vector3[] V,int[][] F);
    private static Mesh Read(string path)
    {
        var v=new List<Vector3>();var f=new List<int[]>();
        foreach(var line in File.ReadLines(path))
        {
            var s=line.Split(' ',StringSplitOptions.RemoveEmptyEntries);if(s.Length==0)continue;
            if(s[0]=="v")v.Add(new(float.Parse(s[1],CultureInfo.InvariantCulture),float.Parse(s[2],CultureInfo.InvariantCulture),float.Parse(s[3],CultureInfo.InvariantCulture)));
            if(s[0]=="f"){var ids=s.Skip(1).Select(x=>int.Parse(x.Split('/')[0])-1).ToArray();for(int i=1;i<ids.Length-1;i++)f.Add(new[]{ids[0],ids[i],ids[i+1]});}
        }
        return new(v.ToArray(),f.ToArray());
    }
    internal static void Run(Action<bool,string> check)
    {
        int checks=0;var sink=check;check=(ok,message)=>{checks++;sink(ok,"fix02238: "+message);};
        var root=Fix02228Tests.SourceRoot();var output=Fix02228Tests.OutputRoot();
        var assets=Path.Combine(root,"Material/files/BepInEx/plugins/SprocketExpandedBallistics/era-assets");
        var b=Read(Path.Combine(assets,"compositeSideModule.obj"));var c=Read(Path.Combine(assets,"compositeSideModule-collision.obj"));
        check(b.V.Min(v=>v.Y)==0&&Math.Abs(b.V.Max(v=>v.Y)-.327)<1e-7,"back at Y=0; fixed depth 327 mm");
        double volume=0;
        for(int i=0;i<b.F.Length;i++)
        {
            var f=b.F[i];
            volume+=Vector3.Dot(b.V[f[0]],Vector3.Cross(b.V[f[1]],b.V[f[2]]))/6d;
        }
        check(volume>0,"outward signed closed volume");
        for(int i=0;i<3;i++)
        {
            float Axis(Vector3 v)=>i==0?v.X:i==1?v.Y:v.Z;
            check(b.V.Min(Axis)==c.V.Min(Axis)&&b.V.Max(Axis)==c.V.Max(Axis),"collision bounds equal render bounds axis "+i);
        }
        using var part=JsonDocument.Parse(File.ReadAllText(Path.Combine(root,"Material/files/Sprocket_Data/StreamingAssets/Parts/compositeSideModulePart.json")));
        var transform=part.RootElement.GetProperty("transform");
        check(transform.GetProperty("minScale").EnumerateArray().Select(x=>x.GetDouble()).SequenceEqual(new[]{.5,1,.5}),"minimum X/Z .5, Y locked");
        check(transform.GetProperty("maxScale").EnumerateArray().Select(x=>x.GetDouble()).SequenceEqual(new[]{8d,1,8d}),"maximum X/Z 8, Y locked");
        var p=CompositeModule.Current;var off=p with{Era=p.Era with{Enabled=false}};
        var previous=CompositeModulePreset.Parse(File.ReadAllText(Path.Combine(root,"tests/fixtures/baseline/compositeSideModule.default-0.22.37.json")));
        var rows=new StringBuilder("scale_xz\tenabled_mass_kg\tpassive_mass_kg\tcells\n");
        foreach(var scale in new[]{.5,1,8})
        {
            var mass=CompositeModuleEraGeometry.Mass(p,scale,1,scale);
            check(Math.Abs(mass-CompositeModuleEraGeometry.Mass(p)*scale*scale)<1e-8,"mass scales with paid X/Z area "+scale);
            check(mass==CompositeModuleEraGeometry.Mass(p,-scale,1,-scale),"signed mirror has equal paid mass "+scale);
            check(CompositeModuleEraGeometry.Layout(scale,scale).CellCount==6,"six cover-panel cells follow footprint "+scale);
            check(CompositeModuleEraGeometry.Mass(off,scale,1,scale)==previous.Mass(scale,1,scale),"ERA off mass bit exact to previous permitted axes "+scale);
            rows.AppendLine(FormattableString.Invariant($"{scale}\t{mass:R}\t{off.Mass(scale,1,scale):R}\t6"));
        }
        var passive=ProtectionFactorTable.BuildModule(CompositeModuleEraGeometry.Effective(off));var frozen=ProtectionFactorTable.BuildModule(previous);
        foreach(var threat in ProtectionFactorTable.Threats)foreach(var angle in new[]{0d,30,60,85})
        {
            check(passive.Factor(threat,angle)==frozen.Factor(threat,angle),"ERA off factor bit exact "+threat+" "+angle);
            var before=CompositeModule.Plan(0,.327/Math.Cos(angle*Math.PI/180),1,threat,new[]{new CompositeModuleInterval(7,0,.327/Math.Cos(angle*Math.PI/180),angle,frozen)});
            var after=CompositeModule.Plan(0,.327/Math.Cos(angle*Math.PI/180),1,threat,new[]{new CompositeModuleInterval(7,0,.327/Math.Cos(angle*Math.PI/180),angle,passive)});
            check(before!.Factor==after!.Factor&&before.StartM==after.StartM&&before.EndM==after.EndM,"minus-Y ray same layer order/path as previous minus-X ray");
        }
        for(int u=0;u<2;u++)for(int v=0;v<3;v++)
            check(CompositeModuleEraGeometry.TryCell(u==0?-.15:.15,-.30+v*.304,out var x,out var y)&&x==u&&y==v,"authoritative cell maps authored cover "+u+","+v);
        check(!CompositeModuleEraGeometry.TryCell(-.006,0,out _,out _)&&!CompositeModuleEraGeometry.TryCell(.1,-.146,out _,out _),"seams and cover gaps cannot consume cells");
        check(CompositeModuleEraGeometry.Exterior(1,-1)&&!CompositeModuleEraGeometry.Exterior(-1,1)&&!CompositeModuleEraGeometry.Exterior(0,-1),"only outside-to-wall cover entries can react");
        var cat=ArmourResponses.Parse(File.ReadAllText(Path.Combine(root,"Material/examples/sprocket.expandedballistics.responses.json")));
        var response=cat.ForResponse(p.Era.ResponseId)!;
        check(p.Era.Binding.MatchesRecipe(response.ResponseId,response.Kind,response.CompatibleMaterialIds),"own config maps exact existing heavy response");
        check(ArmourResponses.PassiveMatches(response,(float)p.Era.ActiveDensity,(float)p.Era.ActiveRhaFactor,(float)p.Era.ActiveSpallFactor),"cell definition exactly matches native existing active recipe");
        var experimental=SprocketExpandedBallistics.ExperimentalArmour.ExperimentalArmour.Parse(File.ReadAllText(Path.Combine(root,"Material/defaults/sprocket.expandedballistics.experimental.json")));
        var table=ProtectionFactorTable.BuildModule(CompositeModuleEraGeometry.Effective(p));var ledger=new EraCells();
        var shots=new StringBuilder("threat\tangle\tfirst_retention\tsecond_retention\tneighbor_retention\tpassive_rha_mm\n");
        foreach(var heat in new[]{false,true})foreach(var angle in new[]{0d,30,60})
        {
            ledger.Clear();var threat=heat?"chemicalJet":"longRod";
            double Shot(int x)
            {
                var state=new ArmourShotState();var cell=PlacedEraState.Key(1,7,0,x,0);
                var intact=ArmourResponseModel.ConsumeStacked(ledger,state,cell,0,0,false,out _);
                var weight=ArmourResponseModel.EraAngularWeight(response,threat,angle);
                var loss=heat?(double?)null:ExperimentalRodModel.Loss(experimental.Recipes[response.ResponseId],new(.6,.02,3.3,.12),weight);
                return ArmourResponseModel.Apply(cat,response,state,new ArmourHitKey(1,7,0,0,.327),heat?"heat":"apfsds",true,true,p.Era.ActiveThicknessMm,angle,double.NaN,intact,loss,experimental.EffectiveKineticCap(cat.MaximumAdditionalKineticLoss),"unknown",threat);
            }
            var first=Shot(0);var second=Shot(0);var neighbor=Shot(1);
            check(first<1&&neighbor==first&&second==1,"first ERA + composite, spent composite only, neighbor intact: "+threat+" "+angle);
            var resistance=table.ProtectionMm(heat?ProtectionThreat.Jet:ProtectionThreat.LongRod,angle);
            check(1000*first-resistance<1000*second-resistance,"ERA reduction precedes same native passive resistance");
            shots.AppendLine(FormattableString.Invariant($"{threat}\t{angle}\t{first:R}\t{second:R}\t{neighbor:R}\t{resistance:R}"));
        }
        check(EraDisplayName.Component(CompositeModule.ComponentId)=="Challenger 2 TES side module (ASPRO-HMT ERA)"&&EraMenuOrder.Ranks.ContainsKey(CompositeModule.PartGuid),"same display-name and order route as Duplet");
        var icon=Path.Combine(assets,"compositeSideModule-icon.png");
        check(File.Exists(icon)&&File.ReadAllText(Path.Combine(root,"tools/banner_composite_icon.ps1")).Contains("CR2 TES"),"banner baked into shipped icon through reproducible pipeline");
        File.WriteAllText(Path.Combine(output,"MASS-02238.tsv"),rows.ToString());File.WriteAllText(Path.Combine(output,"ERA-02238.tsv"),shots.ToString());
        Console.WriteLine($"0.22.38 PASS: {checks} checks.");
    }
}
