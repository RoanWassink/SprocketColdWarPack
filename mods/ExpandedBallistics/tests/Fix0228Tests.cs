using System.Text.Json;
namespace SprocketExpandedBallistics;

internal static class Fix0228Tests
{
    internal static void Run(Action<bool,string> check)
    {
        Registration(check);Reasons(check);Projection(check);
        check(CombatReportMeasurement.NormalThickness(90,-1,-1,-1)==90,"0228: exact declared thickness survives native sentinel");
        check(CombatReportMeasurement.NormalThickness(null,80,90,100)==90,"0228: selected native triangle yields normal thickness");
        foreach(var samples in new[]{new[]{-1d,-1,-1},new[]{90d,-1,90},new[]{0d,90,90},new[]{double.NaN,90,90}})
            check(CombatReportMeasurement.NormalThickness(null,samples[0],samples[1],samples[2])==null,"0228: invalid mesh samples stay unknown");
        Console.WriteLine("0.22.8 registration, reasons, report fixture and thickness checks PASS");
    }
    private static void Registration(Action<bool,string> check)
    {
        check(CassetteAvailability.ForUse("1985.01.01",19910101,true,true).Available,"0228: snapshot plus native lookup authorizes use");
        foreach(bool model in new[]{false,true})foreach(bool? native in new bool?[]{false,null})
            check(!CassetteAvailability.ForUse("1985.01.01",19910101,model,native).Available,"0228: failed/unreadable native registration is blocked");
        check(!CassetteAvailability.ForUse("1985.01.01",19910101,false,true).Available,"0228: native-only entry cannot bypass absent model");
        check(CassetteAvailability.ForUse("1985.01.01",19800101,true,true) is {Available:false,Reason:"From 1985"},"0228: live registration never bypasses vehicle date");
        var root=AppContext.BaseDirectory;
        var materials=CassetteTechnology.ReadDirectory(Path.Combine(root,"NativeData"));
        var reactions=MaterialReactionCatalogue.Parse(File.ReadAllText(Path.Combine(root,MaterialReactionCatalogue.FileName)));
        var presets=CassetteCatalogue.Parse(File.ReadAllText(Path.Combine(root,CassetteCatalogue.FileName)));
        var dir=Path.Combine(SprocketExpandedBallistics.Fix02228Tests.FixtureRoot(),"eb-0228-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(dir);
        try
        {
            var sidecar=Path.Combine(dir,CassetteCatalogue.FileName);File.WriteAllText(sidecar,CassetteComposer.Serialize(presets.Presets));
            var backend=new CassetteComposer(materials,reactions,sidecar,Path.Combine(dir,"Technology"),_=>false);
            bool snapshot=false;bool? frame=false;
            var list=materials.Keys.Where(id=>!CompositePlate.IsCassette(id)).Select(id=>new ComposerMaterial(id,id,CassettePalette.Group(id))).ToArray();
            var model=new CassetteComposerModel(backend,list,Array.Empty<ComposerMaterial>(),()=>CassetteCatalogue.Parse(File.ReadAllText(sidecar)).Presets,_=>false,_=>snapshot,true);
            model.FrameCheck=_=>frame;
            model.SetTarget(new("Plate #281",1,"rha","RHA",true,20,20,19910101,true));
            model.LoadPreset("t80u");model.SetLabel("Live registration fixture");
            var saved=model.Save(true);
            check(File.Exists(Path.Combine(dir,"Technology",saved.Preset.MaterialId+".json")),"0228: save writes versioned Technology");
            check(!model.CanApply&&model.RestartPending,"0228: save alone does not authorize use");
            snapshot=true;model.Invalidate();
            check(!model.CanApply&&model.ApplyBlocker!.Contains(CassetteAvailability.Restart),"0228: model loaded but native frame absent stays blocked");
            model.Tab=1;
            check(model.PresetRows.Single(r=>r.Key==saved.Preset.Id) is {Available:false,Tag:CassetteAvailability.Restart},"0228: own preset shows restart status");
            frame=true;model.RegistrationCompleted(new(saved.Preset,false,"Registered live; ready to apply."));
            check(model.CanApply&&!model.RestartPending&&model.Status?.Contains("Registered live")==true,"0228: verified registration refreshes Apply and status");
            frame=null;model.Invalidate();
            check(!model.CanApply&&model.RestartPending,"0228: unreadable lookup after vehicle reload blocks Apply");
            frame=false;model.RegistrationCompleted(new(saved.Preset,true,"Restart Sprocket to use"));
            check(!model.CanApply&&model.RestartPending,"0228: failed reload never enables Apply");
        }
        finally{Directory.Delete(dir,true);}
    }
    private static void Reasons(Action<bool,string> check)
    {
        foreach(var (text,expected) in new[]{
            ("Cassette not loaded - restart Sprocket; native resistance only.","Cassette not loaded - restart Sprocket"),
            ("Material not in vehicle technology (1985+); native resistance only.","Material not in vehicle technology (1985+)"),
            ("Textolite sandwich: no steel backing reached.","Sandwich: no steel backing confirmed"),
            ("Ceramic: steel backing is too thin.","Ceramic: steel backing is too thin"),
            ("Cassette is part of a blended native block; exact cassette depth is unavailable, native resistance only.","Cassette: exact depth unavailable in blended block")})
        {
            var plate=new CombatLayerPart(281,"Composite armour plate","ebCassette_x","X",90,29,true);
            var shot=new CombatReportShot(1,"Outgoing","APDS",1000,new[]{
                new CombatReportEvent("layerStart","","Reached layer",530,HitId:"A",Layer:new("A",new[]{plate},false)),
                new CombatReportEvent("materialUnavailable","",text,HitId:"A"),new CombatReportEvent("plateResidual","","Passed",530,455,"A")});
            check(CombatReportView.Build(new[]{shot})[0].Layers[0].Reaction==CombatReportLanguage.Reaction(expected)&&CombatHitProjection.Build(shot)[0].Reaction==expected,"0228: event reason is visible (0.22.41 report wording of the same reason): "+expected);
            var history=new CombatReportHistory();history.Restore(new[]{shot},0);
            check(CombatReportHtml.Render(history).Contains(System.Net.WebUtility.HtmlEncode(CombatReportLanguage.Reaction(expected))),"0228: HTML shows the same reason (0.22.41 report wording)");
        }
        var insert=new CombatLayerPart(1,"Insert","textolite","Textolite",60,0,true);
        var support=new CombatLayerPart(2,"Backing","rha","RHA",10,0,true);
        var delayed=new CombatReportShot(2,"Outgoing","APDS",1000,new[]{
            new CombatReportEvent("layerStart","Main","Reached",500,HitId:"I",Layer:new("I",new[]{insert},false)),
            new CombatReportEvent("materialPending","Main","Textolite: awaiting steel backing.",HitId:"I"),
            new CombatReportEvent("plateResidual","Main","Passed",500,450,"I"),
            new CombatReportEvent("layerStart","Main","Reached",450,HitId:"B",Layer:new("B",new[]{support},false)),
            new CombatReportEvent("materialUnavailable","Main","Textolite: steel backing is too thin.",HitId:"B",RelatedEntity:1)});
        var projected=CombatHitProjection.Build(delayed);
        check(projected[0].Reaction=="Textolite: steel backing is too thin"&&projected[1].Reaction=="","0228: delayed reason belongs to exact insert");
        var pending=delayed with{Events=delayed.Events.Take(3).ToArray()};
        check(CombatHitProjection.Build(pending)[0].Reaction=="Sandwich: no steel backing confirmed","0228: pending support has an explicit end-of-shot reason");
    }
    private static void Projection(Action<bool,string> check)
    {
        using var doc=JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory,"fixtures/last-session-0227.json")));
        var shots=JsonSerializer.Deserialize<CombatReportShot[]>(doc.RootElement.GetProperty("shots").GetRawText(),new JsonSerializerOptions{PropertyNameCaseInsensitive=true})!;
        var shot=shots.Single(s=>s.Number==3);var views=CombatHitProjection.Build(shot);
        check(!views.Any(h=>h.Layer?.Parts.Count(p=>p.Armour)>1),"0228: real shot 3 membership bridge is folded");
        check(views[0].Before==529.94384765625&&views[0].After==455.49505615234375,"0228: measured cassette entry and exit are unchanged");
        check(views[0].Details.Any(e=>e.Kind=="blockMembership"&&e.Layer?.Parts.Any(p=>p.Entity==381)==true),"0228: folded group membership remains in details");
        check(views[0].Reaction.Contains("not loaded or vehicle date too early"),"0228: old ambiguous event stays explicitly uncertain");
        var realBridge=shot.Events.Single(e=>e.Kind=="layerStart"&&e.Layer?.Parts.Count==2&&e.Layer.Parts.All(p=>p.Armour));
        // The same shape with a measured equal exit is a real zero-loss observation, not an empty registration.
        var measured=shot with{Events=shot.Events.SelectMany(e=>ReferenceEquals(e,realBridge)?new[]{e,new CombatReportEvent("plateResidual",e.Stage,"Measured zero loss",e.BeforeMm,e.BeforeMm,e.HitId)}:new[]{e}).ToArray()};
        check(CombatHitProjection.Build(measured).Any(h=>h.Id==realBridge.HitId&&h.Before==h.After),"0228: measured zero-loss array remains visible");
        var differentStage=shot with{Events=shot.Events.Select(e=>ReferenceEquals(e,realBridge)?e with{Stage="Precursor"}:e).ToArray()};
        check(CombatHitProjection.Build(differentStage).Any(h=>h.Id==realBridge.HitId),"0228: stage mismatch prevents folding");
        var extra=new CombatLayerPart(999,"Another plate","rha","RHA",40,0,true);
        var unknownMember=shot with{Events=shot.Events.Select(e=>ReferenceEquals(e,realBridge)?e with{Layer=e.Layer! with{Parts=e.Layer.Parts.Append(extra).ToArray()}}:e).ToArray()};
        check(CombatHitProjection.Build(unknownMember).Any(h=>h.Id==realBridge.HitId),"0228: unrepeated member prevents folding");
        var otherSim=shot with{Events=shot.Events.Select(e=>ReferenceEquals(e,realBridge)?e with{HitId="other:1:0:5",Layer=e.Layer! with{Id="other:1:0:5"}}:e).ToArray()};
        check(CombatHitProjection.Build(otherSim).Any(h=>h.Id=="other:1:0:5"),"0228: different simulation prevents folding");
        var paid=shot with{Events=shot.Events.Select(e=>ReferenceEquals(e,realBridge)?e with{BeforeMm=e.BeforeMm-1}:e).ToArray()};
        check(CombatHitProjection.Build(paid).Any(h=>h.Id==realBridge.HitId),"0228: different boundary budget prevents folding");
        // Shot 5 is absent from the supplied export: this is a synthetic regression, not recovered evidence.
        var synthetic=new CombatReportShot(5,"Outgoing","HEAT",800,new[]{
            new CombatReportEvent("layerStart","","Reached",775,HitId:"S:0",Layer:new("S:0",new[]{extra with{ThicknessMm=90,AngleDegrees=66}},false)),
            new CombatReportEvent("plateResidual","","Passed",775,650,"S:0"),
            new CombatReportEvent("layerStart","","Reached",650,HitId:"S:1",Layer:new("S:1",new[]{extra,extra with{Entity=880,ThicknessMm=30}},true)),
            new CombatReportEvent("plateResidual","","Passed array",650,135,"S:1"),
            new CombatReportEvent("layerStart","","Reached",135,HitId:"S:2",Layer:new("S:2",new[]{extra with{Entity=880,Name="Plate #880",ThicknessMm=CombatReportMeasurement.NormalThickness(null,30,30,30)}},false)),
            new CombatReportEvent("plateResidual","","Passed",135,100,"S:2")});
        var syntheticRows=CombatReportView.Build(new[]{synthetic})[0].Layers;
        check(syntheticRows.Length==3&&syntheticRows[1].BeforeAfter=="650 \u2192 135","0228: measured 650-to-135 array is preserved");
        check(syntheticRows[0].Thickness=="90 mm"&&syntheticRows[2].Thickness=="30 mm","0228: declared and mesh normal thickness reach the table");
        var history=new CombatReportHistory();history.Restore(shots,0);
        var html=CombatReportHtml.Render(history);
        check(!html.Contains("see details",StringComparison.OrdinalIgnoreCase),"0228: rendered fixture has no nonexistent-details promise");
        var output=Environment.GetEnvironmentVariable("EB0228_EVIDENCE");
        if(!string.IsNullOrWhiteSpace(output))
        {
            File.WriteAllText(Path.Combine(output,"last-session-reprojected-0.22.8.html"),html);
            File.WriteAllText(Path.Combine(output,"fixture-projection.json"),JsonSerializer.Serialize(CombatReportView.Build(shots).Select(v=>new{v.Key,v.Head,v.Layers}),new JsonSerializerOptions{WriteIndented=true}));
        }
    }
}
