using System.Diagnostics;
using SprocketExpandedBallistics.Materials;
namespace SprocketExpandedBallistics;

/// <summary>Composer screen (0.22.3): view model, version/dirty, cross-section scale, table = Preview, button states.</summary>
internal static class ComposerUiTests
{
    internal static void Run(Action<bool,string> check)
    {
        var root=AppContext.BaseDirectory;
        var materials=CassetteTechnology.ReadDirectory(Path.Combine(root,"NativeData"));
        var reactions=MaterialReactionCatalogue.Parse(File.ReadAllText(Path.Combine(root,MaterialReactionCatalogue.FileName)));
        var presets=CassetteCatalogue.Parse(File.ReadAllText(Path.Combine(root,CassetteCatalogue.FileName)));
        var dir=Path.Combine(SprocketExpandedBallistics.Fix02228Tests.FixtureRoot(),"eb-composer-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(dir);
        try{Body(check,dir,materials,reactions,presets);}
        finally{Directory.Delete(dir,true);}
        Console.WriteLine("Composer UI model checks PASS");
    }
    private static void Body(Action<bool,string> check,string dir,Dictionary<string,CassetteMaterial> materials,MaterialReactionCatalogue reactions,CassetteCatalogue presets)
    {
        var sidecar=Path.Combine(dir,CassetteCatalogue.FileName);File.WriteAllText(sidecar,CassetteComposer.Serialize(presets.Presets));
        var composer=new CassetteComposer(materials,reactions,sidecar,Path.Combine(dir,"Technology"),_=>false);
        var list=materials.Keys.Where(id=>!CompositePlate.IsCassette(id)&&!MaterialMenuVisibility.RetiredPresets.Contains(id))
            .Select(id=>new ComposerMaterial(id,id=="rha"?"Steel (RHA)":id,CassettePalette.Group(id))).ToArray();
        var legacy=MaterialMenuVisibility.RetiredPresets.Where(materials.ContainsKey).Select(id=>new ComposerMaterial(id,"Legacy "+id,"Other")).ToList();
        if(legacy.Count==0)legacy.Add(new ComposerMaterial("rha","Legacy steel","Steel"));
        var native=presets.Presets.Select(p=>p.MaterialId).ToHashSet(StringComparer.Ordinal);var used=new HashSet<string>(StringComparer.Ordinal);
        double now=0;
        var m=new CassetteComposerModel(composer,list,legacy,()=>CassetteCatalogue.Parse(File.ReadAllText(sidecar)).Presets,used.Contains,native.Contains,true,()=>now);

        // ---- palette shared with the plate stripes ----
        check(CassetteStripRaster.Colour("cwepGlassTextolite")==CassettePalette.Colour("cwepGlassTextolite")&&CassettePalette.Hex("cwepGlassTextolite")=="#a9b347"&&CassettePalette.Hex("air")=="#1b1c15","plate stripes and cross-section share one palette");
        check(m.Materials.Select(x=>CassettePalette.GroupIndex(x.Group)).SequenceEqual(m.Materials.Select(x=>CassettePalette.GroupIndex(x.Group)).OrderBy(x=>x)),"material choices grouped in family order");

        // ---- preset list ----
        check(m.PresetRows.Count==presets.Presets.Count(p=>p.Location=="turret")&&m.PresetRows.All(r=>r.Kind==ComposerSource.Historical),"historical tab lists the turret presets (the Turret | Hull switch defaults to turret)");
        check(ReferenceEquals(m.PresetRows,m.PresetRows),"preset rows cached");
        m.Tab=1;check(m.PresetRows.Count==0&&m.EmptyText.StartsWith("No own cassettes",StringComparison.Ordinal),"own tab empty state");
        m.Tab=2;check(m.PresetRows.Count==legacy.Count&&m.PresetRows.All(r=>r.Tag=="LEGACY"),"legacy tab");
        m.Tab=0;m.Location="hull";m.Search="t-64";check(m.PresetRows.Count==2&&m.PresetRows.Any(r=>r.Key=="t64k")&&m.PresetRows.Any(r=>r.Key=="t64a_hull"),"search filters by name (T-64 combination K and T-64A glacis are hull packages)");m.Search="";

        // ---- load: read-only historical, table equals Preview ----
        var t64=presets.Presets.Single(p=>p.Id=="t64k");
        check(m.LoadPreset("t64k")&&m.ReadOnly&&!m.Dirty&&m.Layers.Count==t64.Layers.Length&&m.StateChip=="READ-ONLY","historical preset loads read-only");
        var expected=composer.Preview(t64);
        bool TableMatches(CassettePreview p)
        {
            for(int r=0;r<3;r++)for(int c=0;c<4;c++)
            {
                var cell=p.Protection.Single(x=>x.Angle==CassetteComposerModel.Angles[r]&&x.WithTilt==(c%2==0));
                if(m.TableValue(r,c)!=(c<2?cell.KeMm:cell.HeatMm))return false;
            }
            return true;
        }
        check(TableMatches(expected),"protection table equals CassetteComposer.Preview (12 cells)");
        check(m.Derived(2)==$"{expected.KgM2:0} kg/m²"&&m.Derived(4)==expected.NativeRha.ToString("0.###",System.Globalization.CultureInfo.InvariantCulture)&&m.Derived(0).StartsWith($"{expected.DepthMm:0.#} mm",StringComparison.Ordinal),"derived values from Preview");
        check(m.Derived(6).StartsWith("layer 3",StringComparison.Ordinal)&&m.Cell(0,0)==expected.Protection[0].KeMm.ToString("0",System.Globalization.CultureInfo.InvariantCulture),"spall source and formatted cells");
        check(m.TableNote.Length==0,"no tilt: with = against, nothing to explain (0.22.19)");

        // ---- button states on a read-only preset ----
        check(!m.CanSave&&m.CanSaveAsNew&&m.CanExport&&!m.CanUndo,"unchanged preset: nothing to save as a version, copy/export allowed");
        check(!m.CanApply&&m.ApplyBlocker=="Select a plate in the designer.","apply needs a plate");
        m.SetTarget(new("Plate #12",1,"rha","RHA",false,20,20));
        check(!m.CanApply&&m.ApplyBlocker!.Contains("Composite armour plate")&&m.TargetChip.Contains("not a composite plate"),"apply needs a composite plate");
        var tv=m.TargetVersion;m.SetTarget(new("Plate #12",1,"rha","RHA",true,20,20));m.SetTarget(new("Plate #12",1,"rha","RHA",true,20,20));
        check(m.CanApply&&m.TargetVersion==tv+1&&m.ApplyButton=="Apply to Plate #12"&&m.ApplyNote.Contains($"all faces to {Math.Round(t64.Layers.Sum(CassetteSection.Depth)):0} mm"),"target compared by value; apply enabled for a saved preset");

        // ---- edit forks, version, dirty, debounce, undo ----
        var v=m.Version;var before=m.TableValue(0,2);
        m.SelectLayer(1);m.SetThickness(120);
        check(m.Version>v&&m.Forked&&m.Dirty&&!m.ReadOnly&&m.Label=="T-64 combination K (own)"&&m.Status!.Contains("own copy"),"first edit forks the historical preset");
        check(!m.CanApply&&m.ApplyBlocker!.StartsWith("Save the draft first",StringComparison.Ordinal),"apply waits for a save");
        check(m.TableValue(0,2)==before,"preview waits 150 ms after the last edit");
        now+=.2;
        check(m.TableValue(0,2)!=before&&TableMatches(composer.Preview(m.Draft())),"preview recomputed once, equal to Preview of the draft");
        var stamp=m.EvalStamp;check(m.EvalStamp==stamp&&ReferenceEquals(m.LayerRows,m.LayerRows)&&ReferenceEquals(m.MaterialChoiceIds,m.MaterialChoiceIds),"no recompute without a change; lists cached");
        check(m.CanSave&&m.CanUndo,"forked draft can be saved");
        m.Undo();now+=.2;
        check(!m.Forked&&!m.Dirty&&m.ReadOnly&&m.Label==t64.Label&&m.Layers[1].ThicknessMm==t64.Layers[1].ThicknessMm&&TableMatches(expected),"undo returns to the read-only preset");

        // ---- cross-section to scale ----
        var g0=CassetteSection.Build(t64.Layers,0);var g30=CassetteSection.Build(t64.Layers,30);var g60=CassetteSection.Build(t64.Layers,60);
        check(g0.Scale==g30.Scale&&g30.Scale==g60.Scale&&Math.Abs(g60.PathPx-CassetteSection.FitPx)<1e-3&&Math.Abs(g0.PathPx-CassetteSection.FitPx/2)<1e-3,"scale fixed per draft; the 60° path fits 480 px");
        check(g30.Bands.All(b=>Math.Abs((b.X1-b.X0)*Math.Cos(Math.PI/6)-b.DepthMm*g30.Scale)<1e-3)&&Math.Abs(g30.PathMm-t64.Layers.Sum(CassetteSection.Depth)/Math.Cos(Math.PI/6))<1e-6,"band thickness along the plate normal is to scale");
        check(Math.Abs(g0.FrontX+g0.PathPx/2-CassetteSection.Width/2)<1e-3&&g0.Bands[0].Colour==CassettePalette.Hex(t64.Layers[0].Material),"path centred, band colours from the palette");
        var tilted=new[]{new CassetteLayer("rha",20,60,"up"),new CassetteLayer("air",30,0,"up")};
        check(Math.Abs(CassetteSection.Depth(tilted[0])-40)<1e-9&&Math.Abs(CassetteSection.Build(tilted,0).DepthMm-70)<1e-9,"depth = t / cos tilt; air counts its thickness");
        check(CassetteSection.ScaleBarMm(g0.Scale)*g0.Scale>=40,"scale bar at least 40 px");
        var dv=m.DiagramVersion;check(m.DiagramVersion==dv,"diagram version stable without changes");
        m.AngleIndex=2;check(m.DiagramVersion!=dv,"angle change redraws");dv=m.DiagramVersion;
        m.WithTilt=false;check(m.DiagramVersion!=dv,"with/against change redraws");

        // ---- layer editing and states ----
        m.SelectLayer(0);check(!m.CanMoveLayer(-1)&&m.CanMoveLayer(1)&&m.CanDeleteLayer&&m.CanAddLayer,"layer button states");
        var air=m.MaterialChoiceIds.ToList().IndexOf("air");
        m.SetTilt(30);m.SetMaterialChoice(air);
        check(m.LayerIsAir&&m.LayerTilt==0&&m.Layers[0].Material=="air","air resets tilt");
        m.SetTilt(20);check(m.LayerTilt==0,"air cannot tilt");
        m.SetMaterialChoice(m.MaterialChoiceIds.ToList().IndexOf("rha"));m.SetTilt(80);
        check(m.LayerTilt==75&&m.LayerTiltError==null,"tilt stepper clamps to 75°");
        m.SetDirection(2);check(m.Layers[0].TiltDirection=="left"&&m.AnyTilted,"direction relative to the vehicle");
        m.AddAir();check(m.Layers[1].Material=="air"&&m.Layers[1].ThicknessMm==20&&m.SelectedLayer==1,"+ Air inserts 20 mm after the selection");
        m.AddLayer();check(m.Layers[2].Material=="rha"&&m.Layers[2].ThicknessMm==10,"+ Layer inserts steel 10 mm");
        m.DuplicateLayer();check(m.Layers[3]==m.Layers[2]&&m.SelectedLayer==3,"duplicate below");
        m.MoveLayer(-1);check(m.SelectedLayer==2,"move up keeps the selection");
        while(m.CanAddLayer)m.AddLayer();
        check(m.Layers.Count==CassetteComposerModel.MaxLayers&&!m.CanDuplicateLayer,"64-layer limit");
        while(m.CanDeleteLayer)m.DeleteLayer();
        check(m.Layers.Count==1&&!m.CanDeleteLayer,"the last layer stays");

        // ---- errors block save ----
        m.LoadDraft(new CassettePreset("sel","From selection","1980.01.01",1,4000,new[]{new CassetteLayer("rha",20,0,"up"),new CassetteLayer("rha",30,78,"up")}),ComposerSource.Selection,"from selection");
        check(m.Dirty&&m.HasErrors&&!m.CanSave&&!m.CanExport&&m.Issues.Any(i=>i.Error&&i.FixLabel=="Clamp to 75°"&&i.Layer==1)&&m.LayerRows[1].Error,"tilt above 75° (selection/import) blocks save, offers the fix");
        m.SelectLayer(1);check(m.LayerTiltError!=null,"tilt field error");
        m.ClampTilts();now+=.2;check(!m.HasErrors&&m.CanSave,"clamp fixes it");
        m.SetDate("1980-1-1");now+=.2;check(m.DateError!=null&&m.HasErrors&&!m.CanSave,"invalid date blocks save");
        m.SetDate("1980.01.01");m.SetLabel(" ");now+=.2;check(m.LabelError!=null&&!m.CanSave,"empty name blocks save");
        m.SetLabel("Test pack");
        m.LoadDraft(new CassettePreset("air","Air only","1980.01.01",1,4000,new[]{new CassetteLayer("air",20,0,"up")}),ComposerSource.Import,"x");
        check(m.HasErrors&&m.Issues.Any(i=>i.Text.Contains("no material layer")),"air-only cassette refused");
        m.NewCassette();check(!m.Dirty&&m.HasErrors&&!m.CanSave&&m.LayersNote=="no layers","empty new cassette");

        // ---- save, versions, restart notice, apply after restart ----
        m.LoadPreset("t64k");m.SelectLayer(1);m.SetThickness(110);
        var watch=Stopwatch.StartNew();var saved=m.Save(false);var saveMs=watch.Elapsed.TotalMilliseconds;
        check(saved.Preset.Id=="user_t64k_v1"&&saved.RestartRequired&&m.RestartNotice&&m.Source==ComposerSource.Own&&!m.Dirty&&m.ReadOnly,"save as new version: first own version, loaded read-only");
        check(m.Status!.Contains("Native registration pending")&&File.Exists(Path.Combine(dir,"Technology",saved.Preset.MaterialId+".json")),"registration pending and Technology file");
        m.Tab=1;check(m.PresetRows.Count==1&&m.PresetRows[0].Tag==CassetteAvailability.Restart,"own tab tags a version the game has not loaded");
        check(!m.CanApply&&m.ApplyBlocker!.StartsWith(CassetteAvailability.Restart,StringComparison.Ordinal),"apply waits for verified registration");
        native.Add(saved.Preset.MaterialId);used.Add(saved.Preset.MaterialId);m.Invalidate();
        check(m.CanApply&&m.PresetRows[0].Tag=="IN USE","after a restart: apply enabled, usage tagged");
        m.SetThickness(100);var v2=m.Save(false);
        check(v2.Preset.Id=="user_t64k_v2"&&m.Catalogue.Any(p=>p.Id=="user_t64k_v1"),"editing an own preset saves the next version; v1 stays");
        m.SetLabel("Test pack");var named=m.Save(true);
        check(named.Preset.Id=="user_test_pack_v1","save as new cassette uses the name");
        var none=false;try{m.Save(false);}catch(InvalidOperationException){none=true;}
        check(none&&!m.CanSave,"nothing to save without a change");

        // ---- export / import / legacy / plate cassette ----
        var path=m.Export(Path.Combine(dir,"share"));
        m.Import(path);
        check(File.Exists(path)&&m.Source==ComposerSource.Import&&m.Dirty&&m.Layers.SequenceEqual(named.Preset.Layers)&&m.StateChip=="DRAFT","export then import gives the same layers as a draft");
        check(m.LoadPreset("legacy:"+legacy[0].Id)&&m.Layers.Count==1&&m.Layers[0].ThicknessMm==CassetteComposerModel.DefaultLegacyMm&&m.Dirty,"legacy loads as a one-layer draft");
        check(!m.LoadFromMaterial("ebCassette_t64k"),"an unsaved draft is never replaced on open");
        m.NewCassette();check(m.LoadFromMaterial("ebCassette_t64k")&&m.Origin!.Id=="t64k","open loads the target plate's cassette");
        Console.WriteLine($"Composer save (SaveNewVersion + reload): {saveMs:0.0} ms");
        var stamps=m.EvalStamp;var timing=Stopwatch.StartNew();
        for(int i=0;i<20;i++){m.SelectLayer(i%3);m.SetThickness(100+i);now+=.2;_=m.Preview;}
        var perChange=timing.Elapsed.TotalMilliseconds/20;
        check(m.EvalStamp-stamps>=20&&m.Preview!=null,"one Validate+Preview per change");
        Console.WriteLine($"Composer Validate+Preview per change: {perChange:0.00} ms");
    }
}
