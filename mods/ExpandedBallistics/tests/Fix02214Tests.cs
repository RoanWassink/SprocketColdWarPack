using System.Globalization;
namespace SprocketExpandedBallistics;

/// <summary>
/// 0.22.14 acceptance: Apply on selected faces (or all without a selection), the eligibility rule and composer states of
/// "Convert to composite plate", its undo snapshot (oldWasComposite), the spaced-plate report text in the Kit view and the
/// HTML export, and the gate of a live-registered own cassette.
/// </summary>
internal static class Fix02214Tests
{
    private static readonly string Root=AppContext.BaseDirectory;

    internal static void Run(Action<bool,string> check)
    {
        ThicknessPlan(check);
        Eligibility(check);
        UndoSnapshot(check);
        var dir=Path.Combine(SprocketExpandedBallistics.Fix02228Tests.FixtureRoot(),"eb-fix02214-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(dir);
        try{Composer(check,dir);}
        finally{Directory.Delete(dir,true);}
        SpacedText(check);
        LiveCassette(check);
        Console.WriteLine("0.22.14 checks PASS");
    }

    private static ushort[][] Old()=>new[]{new ushort[]{20,20,20,20},new ushort[]{5,5,5,5},new ushort[]{5,6,5,6},new ushort[]{20,20,20}};
    private static bool Same(ushort[][] a,ushort[][] b)=>a.Length==b.Length&&a.Zip(b).All(p=>p.First.SequenceEqual(p.Second));

    // ---------------- item 4: thickness plan with and without a selection ----------------
    private static void ThicknessPlan(Action<bool,string> check)
    {
        var old=Old();
        var all=CompositeThicknessPlan.Create("ebCassette_t80u",old,133,null);
        check(all.Thicknesses.All(f=>f.All(t=>t==133)),"0.22.14: without a selection every face gets the cassette depth");
        check(Same(all.Thicknesses,CompositeThicknessPlan.Create("ebCassette_t80u",old,133).Thicknesses),"0.22.14: the old Create overload is the no-selection plan");
        check(Same(CompositeThicknessPlan.Create("x",old,133,new[]{false,false,false,false}).Thicknesses,all.Thicknesses),"0.22.14: an all-false selection counts as no selection");
        var some=CompositeThicknessPlan.Create("ebCassette_t80u",old,133,new[]{true,false,false,true});
        check(some.Thicknesses[0].All(t=>t==133)&&some.Thicknesses[3].All(t=>t==133),"0.22.14: selected faces get the depth");
        check(some.Thicknesses[1].SequenceEqual(old[1])&&some.Thicknesses[2].SequenceEqual(new ushort[]{5,6,5,6}),"0.22.14: unselected faces keep their own (also per-corner) thickness");
        check(some.Changes(old,0)&&!some.Changes(old,1)&&!some.Changes(old,2)&&some.Changes(old,3),"0.22.14: only changed faces are written, so a kept face is never flattened");
        check(!ReferenceEquals(some.Thicknesses[1],old[1]),"0.22.14: the plan is a value snapshot, not the caller's arrays");
        check(Same(CompositeThicknessPlan.Create("x",old,null,new[]{true,true,true,true}).Thicknesses,old),"0.22.14: no depth changes nothing, selection or not");
        check(Same(CompositeThicknessPlan.Create("x",old,133,new[]{true}).Thicknesses,new[]{new ushort[]{133,133,133,133},old[1],old[2],old[3]}),"0.22.14: a short selection array marks only its own faces");
        check(CompositeThicknessPlan.SelectedCount(null,4)==0&&CompositeThicknessPlan.SelectedCount(new[]{true,false,true,true},4)==3&&CompositeThicknessPlan.SelectedCount(new[]{true,true},1)==1,"0.22.14: selected face count");
        check(CompositeThicknessPlan.Scope(16,4)=="Applies to 4 selected faces"&&CompositeThicknessPlan.Scope(16,0)=="Applies to all 16 faces"&&CompositeThicknessPlan.Scope(16,1)=="Applies to 1 selected face"&&CompositeThicknessPlan.Scope(1,0)=="Applies to the only face","0.22.14: the composer says where Apply works");
        var thrown=false;try{CompositeThicknessPlan.Create("x",old,0,new[]{true});}catch(ArgumentOutOfRangeException){thrown=true;}
        check(thrown,"0.22.14: an invalid depth is still refused with a selection");
    }

    // ---------------- item 1: which plates may be converted ----------------
    private static void Eligibility(Action<bool,string> check)
    {
        check(CompositeConversion.Blocker(true,true,false,false,false)==null,"0.22.14: an add-on plate with a mesh can be converted");
        check(CompositeConversion.Blocker(false,true,false,false,false)==CompositeConversion.HullOrTurret&&CompositeConversion.HullOrTurret.StartsWith("Hull and turret",StringComparison.Ordinal),"0.22.14: hull and turret are dimmed with their reason");
        check(CompositeConversion.Blocker(true,true,false,true,false)==CompositeConversion.Mirrored&&CompositeConversion.Mirrored.Contains("mirrored",StringComparison.OrdinalIgnoreCase),"0.22.14: a mirrored pair is dimmed with its reason");
        check(CompositeConversion.Blocker(true,false,false,false,false)==CompositeConversion.NoMesh&&CompositeConversion.Blocker(true,true,true,false,false)==CompositeConversion.NoMesh,"0.22.14: no mesh or a released mesh cannot be converted");
        check(CompositeConversion.Blocker(false,true,false,true,true)==CompositeConversion.Already,"0.22.14: an existing composite plate needs no conversion");
        check(CompositeConversion.LinkedNote(0)==""&&CompositeConversion.LinkedNote(1)=="Unlinks it from 1 linked copy; it stays a normal plate."&&CompositeConversion.LinkedNote(3)=="Unlinks it from 3 linked copies; they stay normal plates.","0.22.14: the confirmation counts the Alt duplicates that come loose");
    }

    // ---------------- item 1: undo snapshot ----------------
    private static void UndoSnapshot(Action<bool,string> check)
    {
        check(CompositeConversion.NameAfter(true,false)&&CompositeConversion.NameAfter(true,true)&&!CompositeConversion.NameAfter(false,false)&&CompositeConversion.NameAfter(false,true),"0.22.14: naming rule: execute names, undo names only an old composite plate");
        var old=Old();var plan=CompositeThicknessPlan.Create("ebCassette_t80u",old,133,null);
        var convert=new CompositeUndoSnapshot("rha","ebCassette_t80u",false,old,plan);
        check(convert.NameOnExecute&&!convert.NameOnRevert,"0.22.14: undoing a conversion never names the old (possibly shared) mesh");
        var back=convert.After(false);
        check(back.Material=="rha"&&Same(back.Thicknesses,Old()),"0.22.14: undo restores the old material and every old thickness");
        check(convert.After(true).Material=="ebCassette_t80u"&&convert.After(true).Thicknesses.All(f=>f.All(t=>t==133)),"0.22.14: redo restores the cassette and its depth");
        var apply=new CompositeUndoSnapshot("ebCassette_t64k","ebCassette_t80u",true,old,plan);
        check(apply.NameOnExecute&&apply.NameOnRevert,"0.22.14: Apply on a composite plate keeps its name on undo (unchanged behaviour)");
        var partial=new CompositeUndoSnapshot("rha","ebCassette_t80u",false,old,CompositeThicknessPlan.Create("ebCassette_t80u",old,133,new[]{false,true,false,false}));
        check(Same(partial.After(false).Thicknesses,Old())&&partial.After(true).Thicknesses[1].All(t=>t==133)&&partial.After(true).Thicknesses[2].SequenceEqual(old[2]),"0.22.14: undo of a selected-face Apply restores all faces");
    }

    // ---------------- items 1 and 4: composer states ----------------
    private static CassetteComposerModel Model(string dir,CassetteCatalogue presets)
    {
        var materials=CassetteTechnology.ReadDirectory(Path.Combine(Root,"NativeData"));
        var dates=CassetteTechnology.ReadDates(Path.Combine(Root,"NativeData"));
        var reactions=MaterialReactionCatalogue.Parse(File.ReadAllText(Path.Combine(Root,MaterialReactionCatalogue.FileName)));
        var sidecar=Path.Combine(dir,CassetteCatalogue.FileName);
        File.WriteAllText(sidecar,CassetteComposer.Serialize(presets.Presets));
        var composer=new CassetteComposer(materials,reactions,sidecar,Path.Combine(dir,"Technology"),_=>false);
        var list=materials.Keys.Where(id=>!CompositePlate.IsCassette(id)&&!SprocketExpandedBallistics.Materials.MaterialMenuVisibility.RetiredPresets.Contains(id))
            .Select(id=>new ComposerMaterial(id,id,CassettePalette.Group(id))).ToArray();
        var native=presets.Presets.Select(p=>p.MaterialId).ToHashSet(StringComparer.Ordinal);
        return new CassetteComposerModel(composer,list,Array.Empty<ComposerMaterial>(),()=>CassetteCatalogue.Parse(File.ReadAllText(sidecar)).Presets,_=>false,native.Contains,true,()=>0,
            id=>dates.TryGetValue(id,out var d)?d:null);
    }
    private static void Composer(Action<bool,string> check,string dir)
    {
        var presets=CassetteCatalogue.Parse(File.ReadAllText(Path.Combine(Root,CassetteCatalogue.FileName)));
        var t80=presets.Presets.Single(p=>p.Id=="t80u");var depth=Math.Round(t80.Layers.Sum(CassetteSection.Depth),MidpointRounding.AwayFromZero);
        var m=Model(dir,presets);

        // An ordinary add-on plate, two Alt duplicates, nothing chosen yet.
        var addon=new ComposerTarget("Plate #880",16,"rha","RHA",false,20,20,ConvertBlocker:null,LinkedCopies:2);
        m.SetTarget(addon);
        check(m.Converts&&!m.CanApply&&m.ApplyBlocker=="Pick a preset first: Convert to composite plate applies it."&&m.ApplyButton=="Convert to composite plate","0.22.14: without a preset Convert is off, with its reason ("+m.ApplyBlocker+")");
        m.LoadPreset("t80u");
        check(m.Converts&&m.CanApply&&m.ApplyBlocker==null&&m.ApplyButton=="Convert to composite plate","0.22.14: with a preset Apply becomes Convert to composite plate");
        check(m.ApplyNote==$"Converts Plate #880 to a composite plate, then sets the material and all faces to {m.ThicknessText} (this preset); faces are now 20 mm.","0.22.14: the note says it converts and applies ("+m.ApplyNote+")");
        var confirm=m.ConvertConfirm;
        check(confirm.Contains("Plate #880 becomes a Composite armour plate with "+t80.Label)&&confirm.Contains("Material now: RHA")&&confirm.Contains("Applies to all 16 faces ("+m.ThicknessText+")")
            &&confirm.Contains("Unlinks it from 2 linked copies; they stay normal plates.")&&confirm.Contains("Ctrl+Z makes it a normal plate again"),"0.22.14: the confirmation names plate, preset, material, faces, linked copies and undo: "+confirm);
        check(m.TargetChip.Contains("not a composite plate"),"0.22.14: the chip still says what the plate is now");

        // Hull and a mirrored pair: dimmed with the reason, no Convert.
        m.SetTarget(new ComposerTarget("Plate #12",6,"rha","RHA",false,40,40,ConvertBlocker:CompositeConversion.HullOrTurret));
        check(!m.Converts&&!m.CanApply&&m.ApplyBlocker==CompositeConversion.HullOrTurret&&m.ApplyButton=="Apply to Plate #12"&&m.ConvertConfirm=="","0.22.14: hull/turret: Apply dimmed with the reason");
        m.SetTarget(new ComposerTarget("Plate #13",6,"rha","RHA",false,40,40,ConvertBlocker:CompositeConversion.Mirrored));
        check(!m.Converts&&!m.CanApply&&m.ApplyBlocker==CompositeConversion.Mirrored,"0.22.14: mirrored pair: Apply dimmed with the reason");
        m.SetTarget(new ComposerTarget("Plate #14",1,"rha","RHA",false,20,20));
        check(!m.Converts&&m.ApplyBlocker!.Contains("Composite armour plate"),"0.22.14: an unread target keeps the old wording");

        // A composite plate: Apply unchanged without a selection, only the selected faces with one.
        m.SetTarget(new ComposerTarget("Plate #296",16,"ebCassette_t64k","T-64",true,90,90));
        check(!m.Converts&&m.CanApply&&m.ApplyButton=="Apply to Plate #296"&&m.ApplyScope=="Applies to all 16 faces"&&m.ApplyNote==$"Sets the material and all faces to {m.ThicknessText} (this preset); faces are now 90 mm.","0.22.14: no selection: uniform Apply as before ("+m.ApplyNote+")");
        m.SetTarget(new ComposerTarget("Plate #296",16,"ebCassette_t64k","T-64",true,5,90,SelectedFaces:4));
        check(m.ApplyScope=="Applies to 4 selected faces"&&m.ApplyNote==$"Sets the material and the 4 selected faces to {m.ThicknessText} (this preset); faces are now 5–90 mm.","0.22.14: a selection: the composer shows the faces Apply works on ("+m.ApplyNote+")");
        m.SetTarget(new ComposerTarget("Plate #881",16,"rha","RHA",false,20,20,ConvertBlocker:null,SelectedFaces:1));
        check(m.ConvertConfirm.Contains("Applies to 1 selected face")&&m.ConvertConfirm.Contains("the other faces keep their thickness")&&!m.ConvertConfirm.Contains("Unlinks"),"0.22.14: Convert also works on the selected faces only");
        m.SetTarget(null);
        check(m.ApplyScope==""&&!m.Converts,"0.22.14: no plate, no scope line");
    }

    // ---------------- item 3: spaced plate text ----------------
    private static CombatLayerInfo Layer(string id,params CombatLayerPart[] parts)=>new(id,parts,parts.Length>1);
    private static void SpacedText(Action<bool,string> check)
    {
        // Shot #4 from the 2026-11-02 test session: "Spaced plate: −3 % (yaw)", 445.69 → 432.32 mm, no entity or hit id.
        var plate=new CombatLayerPart(276,"t84 turret oscilating","rha","Rolled Homogeneous Armour",100,12.4,true);
        var shot=new CombatReportShot(4,"Outgoing","APDS (tungsten, late)",1265,new CombatReportEvent[]{
            new("layerStart","","Reached layer",445.693359375,null,"A",Layer("A",plate)),
            new("plateResidual","","Traversed t84 turret oscilating",445.693359375,438.69,"A"),
            new("materialReaction","","Spaced plate: −3 % (yaw)",445.693359375,432.32255859375)},0,529.94);
        var expected="Spaced plate: −3 % (yaw), −"+(445.693359375-432.32255859375).ToString("0.#",CultureInfo.CurrentCulture)+" mm";
        var view=CombatHitProjection.Build(CombatReportTruth.Normalize(shot)).Single();
        check(view.Reaction==expected,"0.22.14: a spaced-plate reaction keeps its own text and its mm: "+view.Reaction);
        check(!view.Reaction.Contains("Backing confirmed",StringComparison.Ordinal),"0.22.14: the sandwich text is not used for a spaced plate");
        var row=CombatReportView.Build(new[]{shot})[0].Layers.Single();
        check(row.Reaction.Contains("Spaced plate",StringComparison.Ordinal)&&!row.Reaction.Contains("Backing confirmed",StringComparison.Ordinal),"0.22.14: the Kit report row shows the spaced text: "+row.Reaction);
        var history=new CombatReportHistory();history.Restore(new[]{shot},0);
        var html=CombatReportHtml.Render(history);
        check(html.Contains(System.Net.WebUtility.HtmlEncode(expected))&&!html.Contains("Backing confirmed",StringComparison.Ordinal),"0.22.14: the HTML export shows the spaced text, no 'Backing confirmed'");
        var tiny=new CombatReportEvent("materialReaction","","Spaced plate: cap stripped (APCBC → AP)",300,300);
        check(CombatMaterialReason.Spaced(tiny)=="Spaced plate: cap stripped (APCBC → AP)","0.22.14: no mm suffix without a reduction");
        // A real sandwich reaction still uses the sandwich wording.
        var sandwich=new CombatReportShot(5,"Outgoing","APDS",1265,new CombatReportEvent[]{
            new("layerStart","","Reached layer",400,null,"A",Layer("A",plate)),
            new("materialReaction","","Steel backing behind ceramic",400,380,"A",RelatedEntity:276)},0,500);
        check(CombatHitProjection.Build(CombatReportTruth.Normalize(sandwich)).Single().Reaction.StartsWith("Backing confirmed",StringComparison.Ordinal),"0.22.14: other material reactions keep their own wording");
    }

    // ---------------- item 5: a live-registered own cassette ----------------
    private static void LiveCassette(Action<bool,string> check)
    {
        var vehicle=CassetteAvailability.Key(1991,12,31);
        var designer=CassetteAvailability.ForUse("1979.01.01",vehicle,true,true);
        check(designer.Available,"0.22.14: a live-registered own cassette is available on the vehicle date");
        check(CassetteAvailability.AfterLiveRegistration(designer,true).Available,"0.22.14: with the frame hook the registration is ready without a restart");
        var noHook=CassetteAvailability.AfterLiveRegistration(designer,false);
        check(!noHook.Available&&noHook.Reason==CassetteAvailability.Restart,"0.22.14: without the frame hook it says Restart Sprocket to use, never ready");
        var tooEarly=CassetteAvailability.ForUse("1995.01.01",vehicle,true,true);
        check(!CassetteAvailability.AfterLiveRegistration(tooEarly,true).Available&&CassetteAvailability.AfterLiveRegistration(tooEarly,true).Reason=="From 1995","0.22.14: the preset date still applies to a live cassette");
        check(CassetteAvailability.ForUse("2030.01.01",CassetteAvailability.Sentinel,true,true).Available,"0.22.14: the 9999.12.31 sentinel allows a later own cassette");
        check(CassetteAvailability.HitBlockReason("1979.01.01",vehicle,true)==null,"0.22.14: in Play the hit path reacts once the frame holds the cassette");
        check(CassetteAvailability.HitBlockReason("1979.01.01",vehicle,false)!.Contains("not loaded"),"0.22.14: the old Play state (startup frame) was the 'not loaded' reason");
        check(CassetteAvailability.LiveReachesPlay(true,true)&&!CassetteAvailability.LiveReachesPlay(true,false)&&CassetteAvailability.LiveReachesPlay(false,false),"0.22.14: startup cassettes always reach Play, live ones with the hook");
        check(!CassetteAvailability.ForUse("1979.01.01",vehicle,CassetteAvailability.LiveReachesPlay(true,false),true).Available,"0.22.14: Apply is blocked for a live cassette that Play would not know");
    }
}
