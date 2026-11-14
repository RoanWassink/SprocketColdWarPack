using System.Text.Json.Nodes;
using NVector=System.Numerics.Vector3;
namespace SprocketExpandedBallistics;

/// <summary>0.22.22: no spall in thin gaps, a dying thin-gap ricochet, STOPPED · spall damage, the probe texts, the config merge, flat-plate back plane, quiet shipped presets.</summary>
internal static class Fix02222Tests
{
    private static CombatLayerInfo Layer(string id,int entity,string name,double thickness,double angle)=>new(id,new[]{new CombatLayerPart(entity,name,null,null,thickness,angle,true)},false);
    private static CombatReportEvent Hit(string text,int entity)=>new("interaction","",text,RelatedEntity:entity);
    private static CombatReportShot Shot(int number,params CombatReportEvent[] events)=>new(number,"Incoming","HEAT 775 mm",700,events,0,318,"T-84",4,4);
    private static CombatReportEvent[] Stop(params CombatReportEvent[] after)=>new[]
    {
        new CombatReportEvent("layerStart","","Reached layer",318,null,"b",Layer("b",2,"Plate #2",120,0)),
        new CombatReportEvent("nativeStop","","Native stop in Plate #2",318,null,"b")
    }.Concat(after).ToArray();
    private static CombatReportEvent[] Through(double after,params CombatReportEvent[] more)=>new[]
    {
        new CombatReportEvent("layerStart","","Reached layer",400,null,"c",Layer("c",3,"Plate #3",60,0)),
        new CombatReportEvent("plateResidual","","Traversed block",400,after,"c")
    }.Concat(more).ToArray();
    private static string E(string text)=>System.Net.WebUtility.HtmlEncode(text);

    internal static void Run(Action<bool,string> check)
    {
        int count=0;var assert=check;check=(ok,text)=>{assert(ok,"fix02222: "+text);count++;};
        Gap(check);Ricochet(check);Badge(check);Probe(check);Config(check);Vanilla(check);FlatPlate(check);Presets(check);
        Console.WriteLine($"0.22.22 checks PASS: {count} checks.");
    }

    // ---- 1. spall in a thin gap ----
    private static void Gap(Action<bool,string> check)
    {
        // Path: plate A (hits 0,1: 0.000-0.020 m), 5.66 mm air, plate B (hits 2,3), then 200 mm air and plate C (hits 4,5), then nothing.
        float[] d={0f,.02f,.02566f,.04566f,.24566f,.26566f};
        bool[] enter={true,false,true,false,true,false};bool[] solid={true,true,true,true,true,true};
        var thin=SpallGapModel.NextSolidGapMm(d,enter,solid,1);
        check(Math.Abs(thin-5.66)<.01,"gap after the first plate is the 5.66 mm air gap ("+thin.ToString("0.00",System.Globalization.CultureInfo.InvariantCulture)+")");
        var wide=SpallGapModel.NextSolidGapMm(d,enter,solid,3);
        check(Math.Abs(wide-200)<.01,"gap after the second plate is 200 mm");
        check(double.IsPositiveInfinity(SpallGapModel.NextSolidGapMm(d,enter,solid,5)),"nothing solid behind the last plate means no gap limit (interior)");
        check(SpallGapModel.SuppressBurst(SpallGapModel.DefaultMinSpallGapMm,thin),"5.66 mm gap: no fragments with the default 50 mm");
        check(!SpallGapModel.SuppressBurst(SpallGapModel.DefaultMinSpallGapMm,wide),"200 mm gap: fragments are made");
        check(!SpallGapModel.SuppressBurst(50,double.PositiveInfinity)&&!SpallGapModel.SuppressBurst(50,double.NaN),"interior or unknown gap never suppresses");
        check(!SpallGapModel.SuppressBurst(0,0)&&SpallGapModel.SuppressBurst(10,5)&&!SpallGapModel.SuppressBurst(10,12),"minSpallGapMm 0 turns the rule off and the threshold is configurable");
        solid[2]=false;
        check(Math.Abs(SpallGapModel.NextSolidGapMm(d,enter,solid,1)-225.66)<.01,"an Enter into a non-solid object (air volume) does not count as a block");
        check(SpallGapModel.NextSolidGapMm(new float[]{0,.01f,.01f,.02f},new[]{true,false,true,false},new[]{true,true,true,true},1)==0,"a block that follows directly (no air) is gap 0");
        var ledger=new SpallGapLedger();var sim=new IntPtr(1);
        ledger.Record(sim,0,.020,5.66);ledger.Record(sim,0,.04566,200);ledger.Record(sim,3,.02,1);
        check(ledger.Match(sim,0,.0205,out var by)==5.66&&by,"burst at the first exit matches the first block by distance");
        check(ledger.Match(sim,0,.0460,out by)==200&&by,"burst at the second exit matches the second block");
        check(ledger.Match(sim,0,null,out by)==200&&!by,"without a distance the latest block of that fragment is used");
        check(ledger.Match(sim,7,.02,out _)==null,"a fragment without records is left native");
        ledger.Clear();check(ledger.Count==0&&ledger.Match(sim,0,.02,out _)==null,"the ledger is cleared per shot");
        check(SpallGapModel.Applies("heat")&&SpallGapModel.Applies("hesh")&&SpallGapModel.Applies("apfsds")&&!SpallGapModel.Applies("aphe")&&!SpallGapModel.Applies("ap")&&!SpallGapModel.Applies(null),"the rule covers HEAT, HESH and APFSDS cones only");
    }

    // ---- 2. a fragment that ricochets in a thin gap dies ----
    private static void Ricochet(Action<bool,string> check)
    {
        var s=FragmentRicochetSettings.Default;
        check(s.ThinGapKillMm==25&&s.GrazingDoubleDegrees==70,"defaults: thinGapKillMm 25, grazingDoubleDegrees 70");
        check(SpallGapModel.KillThinGap(s.ThinGapKillMm,10)&&!SpallGapModel.KillThinGap(s.ThinGapKillMm,40),"a ricochet child with 10 mm to the next block dies, with 40 mm it lives");
        check(!SpallGapModel.KillThinGap(0,1)&&!SpallGapModel.KillThinGap(25,double.NaN)&&!SpallGapModel.KillThinGap(25,double.PositiveInfinity),"0 turns it off; unknown or open space never kills");
        // A reflection at theta from the normal turns the fragment by 180 - 2 theta.
        (double x,double y,double z) Out(double theta){var r=(180-2*theta)*Math.PI/180;return (Math.Sin(r),0,Math.Cos(r));}
        var o45=Out(45);var o80=Out(80);var o70=Out(70.1);
        check(FragmentRicochetModel.Weight(s,0,0,1,o45.x,o45.y,o45.z)==1,"a 45 degree ricochet counts once");
        check(FragmentRicochetModel.Weight(s,0,0,1,o80.x,o80.y,o80.z)==2,"an 80 degree (grazing) ricochet counts twice");
        check(FragmentRicochetModel.Weight(s,0,0,1,o70.x,o70.y,o70.z)==2&&FragmentRicochetModel.Weight(s with{GrazingDoubleDegrees=85},0,0,1,o80.x,o80.y,o80.z)==1,"the angle is configurable");
        check(FragmentRicochetModel.Weight(s,0,0,0,1,0,0)==1&&FragmentRicochetModel.Weight(s,double.NaN,0,1,0,0,1)==1,"unknown directions count once");
        // Max 3 ricochets: two grazing bounces (weight 2+2=4) already kill.
        int c=0;foreach(var _ in new[]{2,2})c+=_;
        check(FragmentRicochetModel.Next(c,s.MaxRicochets)==RicochetOutcome.Kill&&FragmentRicochetModel.Next(1+1,s.MaxRicochets)==RicochetOutcome.Scale,"two grazing ricochets kill, two normal ones do not");
        // Config validation and round trip.
        var round=FragmentRicochetModel.Parse(JsonNode.Parse(FragmentRicochetModel.ToNode(s with{ThinGapKillMm=30,GrazingDoubleDegrees=75}).ToJsonString()));
        check(round.ThinGapKillMm==30&&round.GrazingDoubleDegrees==75,"the new keys round-trip");
        bool bad=false;try{FragmentRicochetModel.Validate(s with{ThinGapKillMm=-1});}catch(FormatException){bad=true;}
        bool bad2=false;try{FragmentRicochetModel.Validate(s with{GrazingDoubleDegrees=10});}catch(FormatException){bad2=true;}
        check(bad&&bad2,"out-of-range values are rejected");
    }

    // ---- 3. the badge ----
    private static void Badge(Action<bool,string> check)
    {
        var spall=Shot(1,Stop(Hit("Crew member Gunner killed",1),Hit("Engine destroyed",5)));
        var block=HitBlockBuilder.Build(spall);
        check(block.Result=="STOPPED · spall damage"&&block.Result==HitBlockBuilder.StoppedSpall,"core stopped with module and crew damage reads STOPPED · spall damage ("+block.Result+")");
        check(block.Colour=="#F18E8E","the badge keeps the stop colour ("+block.Colour+")");
        check(CombatReportView.Build(new[]{spall})[0].Head.EndsWith(" · STOPPED · spall damage",StringComparison.Ordinal),"Kit report header");
        var history=new CombatReportHistory();history.Restore(new[]{spall},0);
        var html=CombatReportHtml.Render(history);
        check(html.Contains(E(" · STOPPED · spall damage")+"</h2>",StringComparison.Ordinal)&&!html.Contains("PENETRATED",StringComparison.Ordinal),"HTML export heading");
        check(HitBlockBuilder.Build(Shot(2,Stop())).Result==HitBlockBuilder.Stopped,"a plain stop stays STOPPED");
        var real=HitBlockBuilder.Build(Shot(3,Through(180,Hit("Engine destroyed",5))));
        check(real.Result==HitBlockBuilder.Penetrated,"a real perforation with residual penetration stays PENETRATED ("+real.Result+")");
        var zero=HitBlockBuilder.Build(Shot(4,Through(0,Hit("Crew member Loader wounded",2))));
        check(zero.Result==HitBlockBuilder.StoppedSpall,"zero residual penetration with damage reads STOPPED · spall damage ("+zero.Result+")");
        check(HitBlockBuilder.Build(Shot(5,Through(0))).Result==HitBlockBuilder.Penetrated,"zero residual without any damage is left as the armour result");
        check(HitResultRule.Resolve(HitBlockBuilder.Ricochet,new[]{Hit("Gun damaged",6)},true)==HitBlockBuilder.Ricochet,"a ricochet is never changed");
        check(CombatReportView.ResultChipWidth>=CombatReportView.ChipWidth(HitBlockBuilder.StoppedSpall),"the Kit chip is wide enough for the longer word");
    }

    // ---- 4. probe texts ----
    private static void Probe(Action<bool,string> check)
    {
        check(FragmentRicochetModel.ClassifyInPlace(true,false,true)==InPlaceKind.Embedded,"Deflected plus Embedded is an embedded fragment, not a ricochet");
        check(FragmentRicochetModel.ClassifyInPlace(true,false,false)==InPlaceKind.Ricochet&&FragmentRicochetModel.ClassifyInPlace(false,false,false)==InPlaceKind.None,"only Deflected without Embedded is a ricochet in place");
        var embedded=FragmentRicochetModel.InPlaceProbeText(InPlaceKind.Embedded,5,"GenerateSpalling","Embedded",120,1,3,.707);
        check(embedded.Contains("fragment embedded (no ricochet)")&&!embedded.Contains("ricochet in place")&&!embedded.Contains("IN PLACE"),"embedded text: "+embedded);
        var place=FragmentRicochetModel.InPlaceProbeText(InPlaceKind.Ricochet,5,"GenerateSpalling","Deflected",120,2,3,.707);
        check(place.Contains("ricochet in place")&&place.Contains("chain 2/3")&&(place.Contains("factor 0.707")||place.Contains("factor 0,707")),"ricochet text: "+place);
        check(FragmentRicochetModel.InPlaceProbeText(InPlaceKind.None,1,"","",0,0,0,0)=="","no kind, no text");
    }

    // ---- 5. config merge ----
    private static void Config(Action<bool,string> check)
    {
        var baseJson=SpallBalance.ToJson(new SpallSettings());
        var old=JsonNode.Parse(baseJson)!.AsObject();old.Remove("minSpallGapMm");
        old["fragmentRicochet"]=new JsonObject{["enabled"]=true,["energyLoss"]=.4,["maxRicochets"]=3,["behaviors"]=new JsonArray("heat"),["kineticKinds"]=new JsonArray("apds"),["affectsOriginal"]=false};
        var text=old.ToJsonString();
        var merged=FragmentRicochetModel.MergeMissing(text);
        var node=JsonNode.Parse(merged)!.AsObject();
        check(merged!=text&&node["minSpallGapMm"]!.GetValue<double>()==50,"merge adds minSpallGapMm 50");
        var fr=node["fragmentRicochet"]!.AsObject();
        check(fr["thinGapKillMm"]!.GetValue<double>()==25&&fr["grazingDoubleDegrees"]!.GetValue<double>()==70,"merge adds thinGapKillMm and grazingDoubleDegrees");
        check(fr["energyLoss"]!.GetValue<double>()==.4&&fr["behaviors"]!.AsArray().Count==1,"merge keeps the user's own values");
        check(FragmentRicochetModel.MergeMissing(merged)==merged,"merge is idempotent");
        var parsed=SpallBalance.Parse(merged);
        check(parsed.MinSpallGapMm==50&&parsed.FragmentRicochet!.ThinGapKillMm==25&&parsed.FragmentRicochet.EnergyLoss==.4,"the merged file parses with the C# parser");
        var custom=JsonNode.Parse(merged)!.AsObject();custom["minSpallGapMm"]=80;
        check(SpallBalance.Parse(custom.ToJsonString()).MinSpallGapMm==80&&FragmentRicochetModel.MergeMissing(custom.ToJsonString())==custom.ToJsonString(),"a user value of minSpallGapMm is kept");
        check(SpallBalance.Parse(SpallBalance.ToJson(new SpallSettings())).MinSpallGapMm==50,"default is 50 mm and round-trips");
        bool bad=false;try{SpallBalance.Parse(custom.ToJsonString().Replace("\"minSpallGapMm\": 80","\"minSpallGapMm\": -5").Replace("\"minSpallGapMm\":80","\"minSpallGapMm\":-5"));}catch(FormatException){bad=true;}
        check(bad,"a negative minSpallGapMm is rejected");
    }

    // ---- 6. vanilla AP stays native ----
    private static void Vanilla(Action<bool,string> check)
    {
        var d=FragmentRicochetSettings.Default;
        check(!FragmentRicochetModel.Applies(d,false,"ap","apds")&&!FragmentRicochetModel.Applies(d,false,"heat",null),"vanilla (no impact context) is never touched by the ricochet rules");
        check(!SpallGapModel.Applies("ap")&&!SpallGapModel.Applies("aphe"),"AP and APHE bursts never follow the gap rule");
        check(HitResultRule.Resolve(HitBlockBuilder.Penetrated,Array.Empty<CombatReportEvent>())==HitBlockBuilder.Penetrated&&HitResultRule.Resolve(HitBlockBuilder.Stopped,Array.Empty<CombatReportEvent>())==HitBlockBuilder.Stopped,"no damage, no change of the word");
    }

    // ---- 7. To back plane on a flat plate ----
    private static NVector[] Quad(float z)=>new[]{new NVector(0,0,z),new NVector(1,0,z),new NVector(1,1,z),new NVector(0,1,z)};
    private static void FlatPlate(Action<bool,string> check)
    {
        var old=new List<ushort[]>{new ushort[]{10,10,10,10},new ushort[]{10,10,10,10}};
        // Two coplanar quads with opposite normals: the average cancels, the old code threw.
        var verts=new List<NVector[]>{Quad(0),Quad(0)};var normals=new List<NVector>{NVector.UnitZ,-NVector.UnitZ};
        bool threw=false;try{BackplaneThickness.Create("m",old,verts,normals,null,40);}catch(ArgumentException){threw=true;}
        check(threw,"the plain back-plane plan still refuses a cancelling average");
        var plan=BackplaneThickness.CreateOrUniform("m",old,verts,normals,null,40,out var summary,out var flat);
        check(flat&&summary==BackplaneThickness.FlatPlateNote&&plan.Thicknesses.All(f=>f.All(v=>v==40)),"a flat plate silently gets Uniform: every corner D, note 'flat plate: applied uniform'");
        var one=BackplaneThickness.CreateOrUniform("m",new List<ushort[]>{new ushort[]{10,10,10,10}},new List<NVector[]>{Quad(0)},new List<NVector>{NVector.UnitZ},null,25,out summary,out flat);
        check(flat&&one.Thicknesses[0].All(v=>v==25),"one planar face: Uniform too");
        // A real shape: two parallel planes facing away from each other cancel and keep the error.
        bool kept=false;try{BackplaneThickness.CreateOrUniform("m",old,new List<NVector[]>{Quad(0),Quad(-.2f)},new List<NVector>{NVector.UnitZ,-NVector.UnitZ},null,40,out _,out _);}catch(ArgumentException){kept=true;}
        check(kept,"a solid shape whose normals cancel keeps its refusal");
        // A corner: two faces at 90 degrees give a genuine back plane (not flat).
        var wall=new NVector[]{new(0,0,0),new(0,0,-1),new(0,1,-1),new(0,1,0)};
        var corner=BackplaneThickness.CreateOrUniform("m",old,new List<NVector[]>{Quad(0),wall},new List<NVector>{NVector.UnitZ,NVector.UnitX},null,40,out summary,out flat);
        check(!flat&&summary.Contains("face"),"a bent plate still uses the back plane ("+summary+")");
        // A plan that is already D on every corner equals Uniform.
        var tilted=BackplaneThickness.CreateOrUniform("m",new List<ushort[]>{new ushort[]{10,10,10,10}},new List<NVector[]>{Quad(0)},new List<NVector>{NVector.UnitZ},null,40,out summary,out flat);
        check(flat&&summary==BackplaneThickness.FlatPlateNote,"result already D everywhere is reported as flat/uniform");
        // Selection: only the selected faces are checked for flatness and written.
        var sel=BackplaneThickness.CreateOrUniform("m",old,new List<NVector[]>{Quad(0),wall},new List<NVector>{NVector.UnitZ,NVector.UnitX},new[]{true,false},40,out _,out flat);
        check(flat&&sel.Thicknesses[0].All(v=>v==40)&&sel.Thicknesses[1].All(v=>v==10),"a flat selection is uniform and leaves the other face alone");
    }

    // ---- 8. the shipped presets are quiet ----
    internal static List<(string Id,string Text)> PresetMessages(string technologyDir,string reactionsJson,IEnumerable<CassettePreset> presets)
    {
        var materials=CassetteTechnology.ReadDirectory(technologyDir);var reactions=MaterialReactionCatalogue.Parse(reactionsJson);
        var composer=new CassetteComposer(materials,reactions,"","",_=>false);var list=new List<(string,string)>();
        foreach(var p in presets)
        {
            foreach(var i in composer.Validate(p))list.Add((p.Id,i.Code+": "+i.Text));
            // The composer opens at 0 degrees with the tilt on; the other angles are physics (a reduced gain), not a defect of the preset.
            var tilted=p.Layers.Any(l=>l.TiltDegrees>0);
            foreach(var i in composer.AngleNotes(p,0,true,tilted))list.Add((p.Id,i.Code+": "+i.Text));
        }
        return list;
    }
    private static void Presets(Action<bool,string> check)
    {
        var root=AppContext.BaseDirectory;var tech=Path.Combine(root,"NativeData");
        var reactionsJson=File.ReadAllText(Path.Combine(root,MaterialReactionCatalogue.FileName));
        var presets=CassetteCatalogue.Parse(File.ReadAllText(Path.Combine(root,CassetteCatalogue.FileName))).Presets;
        check(presets.Length==35,"35 shipped presets");
        var messages=PresetMessages(tech,reactionsJson,presets);
        if(Environment.GetEnvironmentVariable("EB_LIST_PRESET_MESSAGES")=="1")foreach(var m in messages)Console.WriteLine("PRESET-MESSAGE "+m.Id+" | "+m.Text);
        check(messages.Count==0,"every shipped preset opens in the composer with zero messages: "+string.Join(" || ",messages.Select(m=>m.Id+" "+m.Text)));
    }
}
