using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Xml.Linq;
using SprocketExpandedBallistics.Materials;
namespace SprocketExpandedBallistics;

internal static class Feat02244MaterialsTests
{
    internal static readonly string[] Ids={"ebWood","ebRubber","ebFabric","ebGlass"};
    internal static readonly string[] Added=Ids.Select(id=>"native-data/Technology/"+id+".json")
        .Concat(Ids.Select(id=>"native-data/Localization/en-UK/Parts/"+id+".xml")).ToArray();
    internal static readonly string[] Allowed=new[]{"RuntimeCassettePlateVisual.cs","Plugin.cs","SprocketExpandedBallistics.csproj","RELEASE-NOTES.md",
        "Material/MaterialMenuVisibility.cs","Material/MaterialFeature.cs","Material/MaterialAvailability.cs","Material/ResponseUi.cs",
        "Material/defaults/sprocket.expandedballistics.materials.editor.json","Material/MaterialEditorModel.cs","CassetteComposerPanel.cs","CassettePalette.cs","CombatForensics.cs","CombatReportView.cs"}.Concat(Fix02247IntervalTests.Changed).Concat(Added).Concat(new[]{"MaterialLook.cs","RuntimeMaterialLooks.cs","CustomTexture.cs","RuntimeCustomTextures.cs"}).Concat(Feat02250ApplyScaleTests.Changed).Concat(Fix02251DuplicateRowTests.Changed).Concat(Feat02253ComposerFillTests.Changed).ToArray();
    internal static void Run(Action<bool,string> check)
    {
        var count=0;var callback=check;check=(ok,text)=>{count++;callback(ok,"feat02244: "+text);};
        var root=Fix02228Tests.SourceRoot();
        var output=Fix02228Tests.OutputRoot();Directory.CreateDirectory(output);
        var technology=Path.Combine(root,"native-data/Technology");
        var materials=CassetteTechnology.ReadDirectory(technology);
        var dates=CassetteTechnology.ReadDates(technology);
        var presets=CassetteCatalogue.Parse(File.ReadAllText(Path.Combine(root,"Material/defaults",CassetteCatalogue.FileName))).Presets;
        var reactions=MaterialReactionCatalogue.Parse(File.ReadAllText(Path.Combine(root,"Material/defaults",MaterialReactionCatalogue.FileName)));
        var menu=new StringBuilder("id\tplain\tcomposite_plate\n");
        foreach(var id in materials.Keys.Concat(presets.Select(p=>p.MaterialId)).Append("ebCassette_user_fixture_v1").Distinct())
        {
            var plain=MaterialMenuVisibility.Visible(id,false,false);var composite=MaterialMenuVisibility.Visible(id,false,true);
            check(plain==composite,"same picker filter on either plate kind: "+id);
            if(CompositePlate.IsCassette(id)||MaterialMenuVisibility.RetiredPresets.Contains(id)||MaterialMenuVisibility.ComposerOnlyMaterials.Contains(id))
                check(!plain,"hidden composite: "+id);
            else check(plain,"simple material retained: "+id);
            menu.AppendLine($"{id}\t{plain}\t{composite}");
        }
        foreach(var id in Ids)check(MaterialMenuVisibility.Visible(id,false),"new simple material selectable: "+id);
        check(MaterialMenuVisibility.SelectedId(Ids,0,true)==null,"read-only current prompt never assigns a replacement");
        check(MaterialMenuVisibility.CurrentPrompt("ebCassette_user_fixture_v1","Fixture")=="Fixture — Assigned by the Cassette Composer","current cassette name and composer reference");
        check(MaterialMenuVisibility.CurrentPrompt("ceramicArray","Ceramic Array").Contains("Assigned by the Cassette Composer"),"legacy composite gets the same read-only reference");
        var panel=File.ReadAllText(Path.Combine(root,"Material/MaterialFeature.cs"));
        check(!panel.Contains("GroupToggle(")&&!panel.Contains("if (visible.Count == 0) return;"),"no composite toggle or empty-list early return");
        check(panel.Contains("RuntimeCompositePlate.DrawInMaterialBlock(__instance,ui)"),"Open composer remains on the existing inspector route");
        File.WriteAllText(Path.Combine(output,"MENU-02244.tsv"),menu.ToString());

        float[] density={600,1200,600,2500},factor={.03f,.02f,.003f,.2f},spall={.00005f,0,0,.001f},cost={.5f,.8f,.8f,1};
        double[] floor={.314,.1046666666666667,.0314,.5024};
        string[] names={"Wood","Rubber","Fabric (canvas)","Glass (soda-lime)"},colours={"#8b5a2b","#202124","#777b3f","#a8dcef"};
        var values=new StringBuilder("id\tdensity_kg_m3\trha_factor\tspall\trequested_cost\tminimum_cost\teffective_cost\tdate\tmass_1m2_10mm_kg\n");
        var language=new StringBuilder("id\tforbidden_terms\tuncertainty\n");
        for(var i=0;i<Ids.Length;i++)
        {
            var id=Ids[i];using var doc=JsonDocument.Parse(File.ReadAllText(Path.Combine(technology,id+".json")));
            var p=doc.RootElement.GetProperty("properties");
            check(p.GetProperty("density").GetSingle()==density[i],id+" density");
            check(p.GetProperty("rhaFactor").GetSingle()==factor[i],id+" shared KE/HEAT passive factor");
            check(p.GetProperty("spallFactor").GetSingle()==spall[i],id+" spall");
            check(p.GetProperty("costMultiplier").GetSingle()==cost[i],id+" requested cost");
            check(dates[id]==new DateTime(1900,1,1),id+" date 1900-01-01");
            var b=MaterialBalance.Calculate(factor[i],density[i],cost[i]);
            check(b.IsValid&&!b.WasAdjusted&&b.EffectiveCostMultiplier==cost[i],id+" existing price floor retains requested price");
            check(Math.Abs(b.MinimumCostMultiplier-floor[i])<1e-7,id+" expected price floor");
            var mass=density[i]*.01;check(mass==new[]{6d,12,6,25}[i],id+" mass of 1 m2 x 10 mm");
            check(!reactions.Bindings.ContainsKey(id),id+" has no special material reaction");
            check(MaterialAvailability.Label(id,"")==names[i]&&EraDisplayName.Material(id)==names[i]&&CombatCompositeLayers.Label(id)==names[i],id+" readable names in report/forensic/composer");
            check(CassettePalette.Hex(id)==colours[i]&&CombatForensics.MaterialColour(id)==colours[i],id+" own report/forensic colour");
            var xml=XDocument.Load(Path.Combine(root,"native-data/Localization/en-UK/Parts",id+".xml"));
            var text=string.Join(" ",xml.Root!.Elements().Select(e=>e.Value));
            check(text.Contains("adjustable starting values rather than measurements"),id+" explicit calibration language");
            var jargon=CombatReportLanguage.JargonIn(text);var uncertainty=CombatReportLanguage.UncertaintyIn(text);
            check(jargon.Count==0&&uncertainty.Count==0,id+" new visible text: forbidden words 0");
            language.AppendLine($"{id}\t{jargon.Count}\t{uncertainty.Count}");
            var part=new CombatLayerPart(1,"Plate #1",id,null,10,0,true);
            var layer=new CombatLayerInfo("fixture-"+id,new[]{part},false);
            var shot=new CombatReportShot(i+1,"Incoming","Test projectile",1000,new[]{new CombatReportEvent("layerStart","Main","Plate",10,null,layer.Id,layer),new CombatReportEvent("plateResidual","Main","",10,5,layer.Id)},InitialPenetrationMm:10);
            var forensic=CombatForensics.Build(shot);var report=CombatReportView.Build(new[]{shot})[0];
            check(forensic.Layers.Any(l=>l.Name==names[i]&&l.Colour==colours[i]),id+" forensic row has readable name and colour");
            check(report.Layers.Any(l=>l.Material==names[i]&&l.Colour==colours[i]),id+" report row has readable name and colour");
            values.AppendLine(FormattableString.Invariant($"{id}\t{density[i]}\t{factor[i]:R}\t{spall[i]:R}\t{cost[i]:R}\t{b.MinimumCostMultiplier:R}\t{b.EffectiveCostMultiplier:R}\t1900.01.01\t{mass}"));
        }
        File.WriteAllText(Path.Combine(output,"MATERIALS-02244.tsv"),values.ToString());File.WriteAllText(Path.Combine(output,"LANGUAGE-02244.tsv"),language.ToString());

        var scratch=Path.Combine(output,"managed-fixtures","materials-02244");Directory.CreateDirectory(scratch);
        var editable=Path.Combine(scratch,"editable");Directory.CreateDirectory(editable);
        foreach(var id in Ids)File.Copy(Path.Combine(technology,id+".json"),Path.Combine(editable,id+".json"),true);
        var draft=new MaterialEditorDraft(editable,Path.Combine(scratch,"responses.json"),Path.Combine(scratch,"labels.json"),
            Ids.Select((id,i)=>(id,names[i],Path.Combine(editable,id+".json"))),File.ReadAllText(Path.Combine(root,"Material/defaults/sprocket.expandedballistics.responses.json")));
        var editor=new MaterialEditorModel(draft);editor.Select("ebWood");
        check(editor.Text("spallFactor")=="0.00005","material editor preserves five-decimal wood spall precision");
        check(editor.CanSave(out _),"new simple material is editable through the existing config editor");
        editor.ApplyPending();draft.Save();
        using(var edited=JsonDocument.Parse(File.ReadAllText(Path.Combine(editable,"ebWood.json"))))
            check(edited.RootElement.GetProperty("properties").GetProperty("spallFactor").GetDouble()==.00005,"material editor save retains exact wood spall value");
        var backend=new CassetteComposer(materials,reactions,Path.Combine(scratch,CassetteCatalogue.FileName),Path.Combine(scratch,"Technology"),_=>false);
        var list=materials.Keys.Where(id=>!CompositePlate.IsCassette(id)).Select(id=>new ComposerMaterial(id,id,CassettePalette.Group(id))).ToArray();
        var model=new CassetteComposerModel(backend,list,Array.Empty<ComposerMaterial>(),()=>presets,_=>false,_=>true);
        check(model.LoadPreset("challenger2"),"composer still loads an existing cassette");
        model.SetTarget(new ComposerTarget("Fixture plate",6,"rha","RHA",true,20,20,20000101,true));
        check(model.CanApply&&model.ApplyPreset?.MaterialId=="ebCassette_challenger2","composer still authorizes cassette assignment");
        var plan=CompositeThicknessPlan.Create(model.ApplyPreset!.MaterialId,new[]{new ushort[]{20,20,20,20}},backend.Preview(model.ApplyPreset).DepthMm,null);
        var applied=new CompositeUndoSnapshot("rha",model.ApplyPreset.MaterialId,true,new[]{new ushort[]{20,20,20,20}},plan).After(true);
        check(applied.Material=="ebCassette_challenger2","existing apply/undo plan assigns the cassette ID");

        // An actual v2 saved blueprint, copied locally and with only its header name/description anonymized.
        // This exercises payload reserialization and re-resolution, not the native Unity loader.
        var fixture=JsonNode.Parse(File.ReadAllText(Path.Combine(root,"tests/fixtures/cassette-02243.blueprint")))!;
        var reloaded=JsonNode.Parse(fixture.ToJsonString())!;
        string[] CassetteIds(JsonNode n)=>n["blueprints"]!.AsArray().Select(b=>b!["blueprint"]?["armourTechID"]?.GetValue<string>()).Where(id=>id!=null&&CompositePlate.IsCassette(id)).Select(id=>id!).ToArray();
        var savedIds=CassetteIds(fixture);check(savedIds.Length>=2&&savedIds.SequenceEqual(CassetteIds(reloaded)),"saved v2 blueprint retains every cassette material ID on clean JSON reload");
        var newModels=presets.ToDictionary(p=>p.MaterialId,p=>new CassetteModel(p,materials,reactions));
        var newTables=ProtectionFactorTable.ForMaterials(newModels,reactions,materials,id=>id);
        foreach(var id in savedIds)check(newModels.ContainsKey(id),"saved blueprint cassette resolves to a model: "+id);
        foreach(var id in Ids)foreach(var threat in ProtectionFactorTable.Threats)check(Math.Abs(newTables[id].Factor(threat,0)-factor[Array.IndexOf(Ids,id)])<1e-7,"new loose material has passive-only protection: "+id+" / "+threat);
        File.WriteAllText(Path.Combine(output,"FEAT44-RESULT.json"),JsonSerializer.Serialize(new{checks=count,passed=true,nativeLoadTest=false,installed=false}));
        Console.WriteLine($"FEAT02244 PASS: {count} checks.");
    }
}
