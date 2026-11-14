using System.Numerics;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
namespace SprocketExpandedBallistics;

internal static class Feat02250ApplyScaleTests
{
    /// <summary>Source files 0.22.50 may change on top of 0.22.49 (composer Apply view state and its scaling).</summary>
    internal static readonly string[] Changed={"ApplyThickness.cs","CassetteComposer.cs","CassetteComposerModel.cs","CassetteComposerPanel.cs","RuntimeCompositePlate.cs","Plugin.cs","SprocketExpandedBallistics.csproj","RELEASE-NOTES.md"};
    internal static void Run(Action<bool,string> check)
    {
        int count=0;var sink=check;check=(ok,msg)=>{count++;sink(ok,"feat02250: "+msg);};
        string root=Fix02228Tests.SourceRoot(),output=Fix02228Tests.OutputRoot();
        var materials=CassetteTechnology.ReadDirectory(Path.Combine(AppContext.BaseDirectory,"NativeData"));
        var reactions=MaterialReactionCatalogue.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory,MaterialReactionCatalogue.FileName)));
        var presets=CassetteCatalogue.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory,CassetteCatalogue.FileName))).Presets;
        var preset=new CassettePreset("user_apply_scale_v1","Cast turret array","1975.01.01",150,250,
            new[]{new CassetteLayer("rha",100,0,"up"),new("air",20,0,"up"),new("rha",80,0,"up")});
        var backend=new CassetteComposer(materials,reactions,"","",_=>false);
        var model=new CassetteComposerModel(backend,materials.Keys.Select(id=>new ComposerMaterial(id,id,CassettePalette.Group(id))).ToArray(),Array.Empty<ComposerMaterial>(),()=>new[]{preset});
        check(model.LoadPreset(preset.Id)&&model.ApplyDepthMm==200,"default is preset D");
        model.SetTarget(new("Turret cheek",3,"rha","Steel",true,80,250,SelectedFaces:2));
        var before=new[]{new ushort[]{80,81,82,83},new ushort[]{200,200,200,200},new ushort[]{250,250,250,250}};
        model.SetApplyThickness("70 %");
        var chosen=model.ApplyDepthMm;
        var plan=CompositeThicknessPlan.Create(preset.MaterialId,before,chosen,new[]{true,false,true});
        check(chosen==140&&plan.Thicknesses[0].All(t=>t==140)&&plan.Thicknesses[2].All(t=>t==140),"Uniform 70 % writes 0.7 D on exactly selected faces");
        check(plan.Thicknesses[1].SequenceEqual(before[1])&&before[0][0]==80,"unselected face and original undo mesh remain unchanged");
        foreach(var selection in new bool[]?[]{null,new[]{false,false,false}})
            check(CompositeThicknessPlan.Create(preset.MaterialId,before,chosen,selection).Thicknesses.All(f=>f.All(t=>t==140)),"no selection treats every face");
        var undo=new CompositeUndoSnapshot("rha",preset.MaterialId,true,before,plan);
        check(undo.After(true).Thicknesses.SelectMany(f=>f).SequenceEqual(plan.Thicknesses.SelectMany(f=>f)),"one operation executes all selected faces");
        check(undo.After(false).Material=="rha"&&undo.After(false).Thicknesses.Zip(before).All(p=>p.First.SequenceEqual(p.Second)),"one Revert restores material and every corner");
        check(undo.After(true).Thicknesses[0][0]==140,"redo restores chosen thickness");
        check(!model.Dirty&&model.ApplyPreset==preset,"Apply thickness does not fork or alter saved preset");
        check(model.ThicknessText=="140 mm (70 %)"&&model.ApplyNote.Contains("140 mm")&&model.ApplyScope.Contains("2 selected"),"chosen mm, percent and scope visible");
        model.SetApplyThickness("150 mm");check(model.ApplyDepthMm==150&&model.ThicknessText.Contains("75 %"),"mm entry updates percent");
        foreach(var v in new[]{50,75,100,125}){model.SetApplyPercent(v);check(model.ApplyDepthMm==200*v/100,"quick choice "+v);}
        foreach(var v in new[]{-100d,0,1,10})check(ApplyThickness.FromPercent(200,v)==20,"10 % lower clamp "+v);
        foreach(var v in new[]{300d,301,10000})check(ApplyThickness.FromPercent(200,v)==600,"300 % upper clamp "+v);
        check(ApplyThickness.FromPercent(2,10)==1&&ApplyThickness.Clamp(2,0)==1,"absolute minimum 1 mm");
        foreach(var bad in new[]{"","NaN","Infinity","abc","70 %%"})check(!ApplyThickness.TryParse(bad,200,out _),"invalid entry refused: "+bad);
        check(ApplyThickness.TryParse("1e3 mm",200,out var clamped)&&clamped==600,"mm input shares upper clamp");
        model.LoadPreset(preset.Id);check(model.ApplyDepthMm==200,"loading preset resets Apply to D");
        model.SetApplyPercent(70);model.SetThicknessMode(1);
        var quad=new[]{new Vector3(-.5f,-.5f,0),new(-.5f,.5f,0),new(.5f,.5f,0),new(.5f,-.5f,0)};
        var back=BackplaneThickness.Create(preset.MaterialId,before,new[]{quad,quad,quad},new[]{Vector3.UnitZ,Vector3.UnitZ,Vector3.UnitZ},new[]{true,false,true},model.ApplyDepthMm);
        check(back.Plan.Thicknesses[0].All(t=>t==140)&&back.Plan.Thicknesses[1].SequenceEqual(before[1]),"To back plane uses chosen thickness and selection");
        var warped=new[]{new Vector3(-.5f,-.5f,0),new(-.5f,.5f,0),new(.5f,.5f,-1),new(.5f,-.5f,-1)};
        var limited=BackplaneThickness.Create(preset.MaterialId,new[]{before[0]},new[]{warped},new[]{Vector3.UnitZ},null,140);
        check(limited.Plan.Thicknesses[0].All(t=>t>=1&&t<=280)&&limited.Plan.Thicknesses[0].Contains((ushort)1),"To back plane lower clamp is 1 mm");
        // A strongly sloped normal relative to the average forces the scaled upper clamp.
        var wedgeA=new[]{new Vector3(0,0,0),new(0,1,0),new(1,1,-.1f),new(1,0,-.1f)};
        var wedgeB=wedgeA.Select(v=>new Vector3(-v.X,v.Y,v.Z)).ToArray();
        var upper=BackplaneThickness.Create(preset.MaterialId,new[]{before[0],before[1]},new[]{wedgeA,wedgeB},new[]{Vector3.Normalize(new Vector3(1,0,.1f)),Vector3.Normalize(new Vector3(-1,0,.1f))},null,140);
        check(upper.ClampedCorners>0&&upper.Plan.Thicknesses.SelectMany(f=>f).All(t=>t>=1&&t<=280)&&upper.Plan.Thicknesses.SelectMany(f=>f).Contains((ushort)280),"To back plane upper clamp is twice chosen thickness");
        check(model.ThicknessTooltip.Contains("140 mm")&&model.ApplyNote.Contains("140 mm"),"back-plane tooltip/note name chosen depth");
        model.SetTarget(new("Turret cheek",3,"rha","Steel",false,80,250,ConvertBlocker:null,SelectedFaces:2));
        check(model.ConvertConfirm.Contains("140 mm")&&model.ConvertConfirm.Contains("One undo step"),"conversion confirmation names thickness and single undo");

        var rows=new StringBuilder("preset\tface_percent\tlayer\tnormal_mm\tfraction\n");
        CombatCompositeLayers.Configure(id=>id==preset.MaterialId?preset:presets.FirstOrDefault(p=>p.MaterialId==id));
        try
        {
            var events=new List<CombatReportEvent>();
            foreach(var percent in new[]{50,100,150})
            {
                var depth=200*percent/100d;var id="face-"+percent;
                var part=new CombatLayerPart(percent,"Turret cheek",preset.MaterialId,preset.Label,depth,0,true);
                var subs=CombatCompositeLayers.For(part)!;
                for(int i=0;i<subs.Count;i++)
                {
                    check(subs[i].NormalMm==preset.Layers[i].ThicknessMm*percent/100,"report constituent follows individual face "+percent+" / "+i);
                    rows.AppendLine(FormattableString.Invariant($"{preset.Id}\t{percent}\t{i+1}\t{subs[i].NormalMm:R}\t{subs[i].Fraction:R}"));
                }
                events.Add(new("layerStart","","Reached layer",1000,HitId:id,Layer:new(id,new[]{part},false)));
                events.Add(new("layerEnd","","Left layer",1000,900,HitId:id));
            }
            var shot=new CombatReportShot(1,"outgoing","APFSDS",1600,events,InitialPenetrationMm:1000,Target:"Cast turret");
            var fresh=JsonSerializer.Deserialize<CombatReportShot>(JsonSerializer.Serialize(shot))!;
            var forensic=CombatForensics.Build(fresh);
            check(forensic.Layers.Count==3,"forensic keeps three faces on the same plate/material");
            foreach(var layer in forensic.Layers)
            {
                var thickness=layer.Parts.Single().ThicknessMm!.Value;
                check(layer.Sublayers!=null&&Math.Abs(layer.Sublayers.Sum(s=>s.NormalMm)-thickness)<1e-10,"forensic loaded sublayers sum to real face depth");
                foreach(var sub in layer.Sublayers!)check(sub.NormalMm==preset.Layers[sub.Index-1].ThicknessMm*thickness/200,"forensic per-layer scaled depth");
            }
            var history=new CombatReportHistory();history.Restore(new[]{fresh},0);
            var html=CombatReportHtml.Render(history);File.WriteAllText(Path.Combine(output,"apply-scale-report.html"),html);
            check(html.Contains("100 mm")&&html.Contains("200 mm")&&html.Contains("300 mm"),"combat-report export contains individual face thicknesses");
            File.WriteAllText(Path.Combine(output,"APPLY-SCALE-FORENSIC.json"),JsonSerializer.Serialize(forensic,new JsonSerializerOptions{WriteIndented=true}));
        }
        finally{CombatCompositeLayers.Configure(null);}
        // Use a real saved blueprint's mesh shape/schema, retaining the complete outer saved payload.
        var bp=JsonNode.Parse(File.ReadAllText(Path.Combine(root,"tests/fixtures/cassette-02243.blueprint")))!;
        IEnumerable<JsonObject> Objects(JsonNode? n)
        {
            if(n is JsonObject o){yield return o;foreach(var v in o.Select(k=>k.Value))foreach(var x in Objects(v))yield return x;}
            if(n is JsonArray a)foreach(var v in a)foreach(var x in Objects(v))yield return x;
        }
        var mesh=Objects(bp).First(o=>o["name"]?.ToString()==CompositePlate.Name&&o["mesh"] is JsonObject)["mesh"]!;
        var faces=mesh["faces"]!.AsArray();var depths=new[]{100,200,300};
        for(int f=0;f<3;f++){var a=faces[f]!["t"]!.AsArray();for(int j=0;j<a.Count;j++)a[j]=depths[f];}
        string saved=Path.Combine(output,"mixed-face-thickness.blueprint");File.WriteAllText(saved,bp.ToJsonString());
        var loaded=JsonNode.Parse(File.ReadAllText(saved))!;
        var loadedFaces=Objects(loaded).First(o=>o["name"]?.ToString()==CompositePlate.Name&&o["mesh"] is JsonObject)["mesh"]!["faces"]!.AsArray();
        for(int f=0;f<3;f++)check(loadedFaces[f]!["t"]!.AsArray().All(v=>v!.GetValue<int>()==depths[f]),"native t arrays survive complete saved-payload reload "+f);
        // Verify path scaling for every shipped preset/threat/angle, not a rebuilt recipe at a new thickness.
        foreach(var p in presets)
        {
            var nominal=backend.Preview(p);var table=backend.Table(p);
            var same=backend.Preview(p,nominal.DepthMm);
            check(JsonSerializer.Serialize(same)==JsonSerializer.Serialize(nominal),"100 % preview bit exact "+p.Id);
            check(backend.ProtectionCurve(p,nominal.DepthMm).SequenceEqual(backend.ProtectionCurve(p)),"100 % chart bit exact "+p.Id);
            foreach(var scale in new[]{.1,.5,.7,1,1.5,3})
            {
                var depth=nominal.DepthMm*scale;var scaled=backend.Preview(p,depth);
                check(scaled.DepthMm==depth&&Math.Abs(scaled.KgM2-nominal.KgM2*scale)<1e-8,"preview real depth and mass "+p.Id);
                foreach(var cell in scaled.Protection)foreach(var threat in ProtectionFactorTable.Threats)
                {
                    var expected=ProtectionFactorTable.PathMm(depth,cell.Angle)*table.Factor(threat,cell.Angle);
                    var actual=threat switch{ProtectionThreat.Jet=>cell.HeatMm,ProtectionThreat.LongRod=>cell.KeMm,ProtectionThreat.FullBoreKE=>cell.FullBoreMm,_=>cell.HeshMm};
                    check(BitConverter.DoubleToInt64Bits(expected)==BitConverter.DoubleToInt64Bits(actual),"preview is chosen path times unchanged factor");
                    var end=ProtectionFactorTable.PathMm(depth,cell.Angle)*.001;
                    var paid=new MaterialIntervalLedger().Plan(0,end,new[]{new MaterialInterval(1,p.MaterialId,p.Label,0,end,table.Factor(threat,cell.Angle),cell.Angle)})!;
                    check(Math.Abs(paid.RhaMm-expected)<1e-8,"shot interval payment scales with face path");
                }
            }
        }
        // Native strips use fractional layer boundaries on each wall's actual endpoints.
        foreach(var percent in new[]{50,100,150})
        {
            var d=percent*2d;var edge=CassetteEdgeMesh.Build(quad,new[]{new CassetteVisualFace(new[]{0,1,2,3},d)});
            check(Math.Abs(edge.Edges.Vertices.Max(v=>Math.Abs(v.Z))-d*.001)<1e-6,"CassetteEdgeMesh uses real face depth "+percent);
            var boundaries=CassetteDepthStrip.Boundaries(preset);
            for(int i=0;i<preset.Layers.Length;i++)check(Math.Abs((boundaries[i+1]-boundaries[i])*d-preset.Layers[i].ThicknessMm*percent/100)<1e-10,"strip constituent proportional "+percent+" / "+i);
        }
        model.SetThicknessMode(0);model.SetTarget(new("Turret cheek",3,preset.MaterialId,preset.Label,true,100,300,SelectedFaces:1));model.SetApplyPercent(70);
        File.WriteAllText(Path.Combine(output,"APPLY-PREVIEW-MODEL.json"),JsonSerializer.Serialize(new{title=preset.Label,nominalDepth=200,model.ThicknessInput,model.ThicknessText,model.ApplyNote,model.ApplyScope,model.ThicknessTooltip,section=model.Section,derived=Enumerable.Range(0,8).Select(model.Derived).ToArray()},new JsonSerializerOptions{WriteIndented=true}));
        File.WriteAllText(Path.Combine(output,"APPLY-SCALE-LAYERS.tsv"),rows.ToString());
        File.WriteAllText(Path.Combine(output,"FEAT50-RESULT.json"),JsonSerializer.Serialize(new{passed=true,checks=count,nativeLiveSaveReload=false}));
        Console.WriteLine($"FEAT02250 PASS: {count} checks.");
    }
}
