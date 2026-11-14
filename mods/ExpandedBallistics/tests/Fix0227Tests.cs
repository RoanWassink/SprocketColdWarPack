using System.Numerics;
using System.Text.Json;
using System.Text.Json.Nodes;
namespace SprocketExpandedBallistics;

/// <summary>
/// 0.22.7: composite plate identity after Apply, the vehicle-date availability of presets, the saved date rule,
/// the Turret / Hull location and the six hull packages, and the tidied combat report (header, summary, table).
/// </summary>
internal static class Fix0227Tests
{
    private static readonly string Root=AppContext.BaseDirectory;

    internal static void Run(Action<bool,string> check)
    {
        Identity(check);
        var materials=CassetteTechnology.ReadDirectory(Path.Combine(Root,"NativeData"));
        var dates=CassetteTechnology.ReadDates(Path.Combine(Root,"NativeData"));
        var reactions=MaterialReactionCatalogue.Parse(File.ReadAllText(Path.Combine(Root,MaterialReactionCatalogue.FileName)));
        var catalogue=CassetteCatalogue.Parse(File.ReadAllText(Path.Combine(Root,CassetteCatalogue.FileName)));
        Availability(check);
        var dir=Path.Combine(SprocketExpandedBallistics.Fix02228Tests.FixtureRoot(),"eb-fix0227-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(dir);
        try
        {
            Composer(check,dir,materials,dates,reactions,catalogue);
            Location(check,dir,materials,dates,reactions,catalogue);
        }
        finally{Directory.Delete(dir,true);}
        Hull(check,materials,dates,reactions,catalogue);
        Combat(check);
        Console.WriteLine("0.22.7 checks PASS");
    }

    // ---------------- item 1: identity ----------------
    private static void Identity(Action<bool,string> check)
    {
        check(CompositePlate.IsComposite(CompositePlate.Name,"rha")&&CompositePlate.IsComposite(CompositePlate.Name,null),"fix0227: the mesh name alone makes a composite plate");
        check(CompositePlate.IsComposite(null,"ebCassette_t80u")&&CompositePlate.IsComposite("",  "ebCassette_user_test_v2"),"fix0227: a cassette material alone makes a composite plate (the mesh copy of an Apply is name-less)");
        check(!CompositePlate.IsComposite(null,"rha")&&!CompositePlate.IsComposite("Other shape","castArmour")&&!CompositePlate.IsComposite(null,null)&&!CompositePlate.IsComposite("composite armour plate",""),"fix0227: no name and no cassette is no composite plate; the name is exact");
        check(!CompositePlate.IsCassette(null)&&!CompositePlate.IsCassette("ebcassette_x")&&CompositePlate.IsCassette("ebCassette_x"),"fix0227: cassette ids are null-safe and exact");

        double now=0;var cache=new CompositePlate.Cache(()=>now);string? name=null;int reads=0;
        bool Read()=>cache.Get((IntPtr)5,()=>{reads++;return name;});
        check(!Read()&&!Read()&&reads==1,"fix0227: a negative answer is cached for a moment, not read every frame");
        name=CompositePlate.Name;
        check(!Read()&&reads==1,"fix0227: inside that moment the cached negative still answers");
        now+=CompositePlate.Cache.NegativeSeconds+.01;
        check(Read()&&reads==2,"fix0227: the negative expires by itself, so an old false never sticks");
        name=null;now+=1000;
        check(Read()&&reads==2,"fix0227: a positive answer stays until it is invalidated");
        cache.Invalidate((IntPtr)5);
        check(!Read()&&reads==3,"fix0227: Invalidate drops a positive answer");
        name=CompositePlate.Name;cache.Invalidate((IntPtr)5);
        check(Read()&&reads==4,"fix0227: Invalidate beats the negative window (own operations and OnAssignedMeshModified use it)");
        cache.Clear();check(cache.Count==0,"fix0227: Clear empties the cache");
    }

    // ---------------- item 1: vehicle date availability ----------------
    private static void Availability(Action<bool,string> check)
    {
        int Key(int y,int m,int d)=>CassetteAvailability.Key(y,m,d);
        check(CassetteAvailability.Key("1985.01.01")==19850101&&CassetteAvailability.Key("1985-01-01")==null&&CassetteAvailability.Text(19591231)=="1959.12.31"&&CassetteAvailability.Text(99991231)=="9999.12.31","fix0227: date keys and text");
        var early=CassetteAvailability.Evaluate("1985.01.01",Key(1959,12,31),true);
        check(!early.Available&&early.Reason=="From 1985","fix0227: a 1985 cassette is unavailable on a vehicle dated 1959.12.31 (\"From 1985\")");
        check(CassetteAvailability.Evaluate("1985.01.01",Key(1985,1,1),true).Available&&!CassetteAvailability.Evaluate("1985.01.01",Key(1984,12,31),null).Available,"fix0227: the preset date is inclusive");
        check(CassetteAvailability.Evaluate("1985.01.01",Key(9999,12,31),true).Available,"fix0227: the final-era sentinel date can use every cassette");
        check(CassetteAvailability.Evaluate("1985.01.01",null,null) is {Available:true,Reason:"From 1985"},"fix0227: no vehicle context dims nothing");
        var notLoaded=CassetteAvailability.Evaluate("1979.01.01",Key(1991,12,31),false);
        check(!notLoaded.Available&&notLoaded.Reason=="Not loaded"&&CassetteAvailability.Evaluate("1979.01.01",Key(1991,12,31),true).Available,"fix0227: a reached date that the native frame still lacks is reported as not loaded");
        check(CassetteAvailability.Explain("T-80U","1985.01.01",Key(1959,12,31),early)=="T-80U exists from 1985.01.01; this vehicle is dated 1959.12.31."&&CassetteAvailability.Explain("T-80U","1985.01.01",Key(1991,12,31),notLoaded).Contains("restart Sprocket")&&CassetteAvailability.Explain("X","1985.01.01",null,new(true,"From 1985"))=="","fix0227: the refusal names the preset date and the vehicle date");
        check(CassetteAvailability.DateBlocks("1985.01.01",Key(1959,12,31))&&!CassetteAvailability.DateBlocks("1985.01.01",null)&&!CassetteAvailability.DateBlocks(null,Key(1959,12,31)),"fix0227: DateBlocks");
        var longText=CassetteAvailability.UnknownMaterial("ebCassette_t80u","T-80U cellular cast armour","1985.01.01",Key(1959,12,31),false);
        var shortText=CassetteAvailability.UnknownMaterial("ebCassette_t80u","T-80U cellular cast armour","1985.01.01",Key(1959,12,31),true);
        check(longText.Contains("exists from 1985.01.01")&&longText.Contains("1959.12.31")&&longText.Contains("default values")&&shortText.Contains("exists from 1985")&&shortText.Contains("1959.12.31")&&shortText.Length<longText.Length,"fix0227: the unknown-material warning names both dates, long and inspector form");
        var missing=CassetteAvailability.UnknownMaterial("ebCassette_x","X",null,Key(1991,12,31),true);
        check(missing.Contains("Technology missing or not loaded")&&!missing.Contains("exists from"),"fix0227: a missing Technology file is told apart from a date");
        var newest=CassetteAvailability.Newest(new[]{new CassetteLayer("rha",10,0,"up"),new("air",10,0,"up"),new("elastomerNERA",10,0,"up"),new("mystery",10,0,"up")},id=>id switch{"rha"=>new DateTime(1920,1,1),"elastomerNERA"=>new DateTime(1979,1,1),_=>null});
        check(newest is {Material:"elastomerNERA"}&&newest.Value.Date.Year==1979&&CassetteAvailability.Newest(new[]{new CassetteLayer("air",10,0,"up")},_=>null)==null,"fix0227: newest material date ignores air and unknown materials");
    }

    // ---------------- item 1 and the date rule: the composer model ----------------
    private static CassetteComposerModel Model(string dir,string name,Dictionary<string,CassetteMaterial> materials,Dictionary<string,DateTime> dates,MaterialReactionCatalogue reactions,CassetteCatalogue presets,
        out CassetteComposer composer,out string sidecar,ref double now,Func<string,bool?>? frame=null)
    {
        var sub=Path.Combine(dir,name);Directory.CreateDirectory(sub);sidecar=Path.Combine(sub,CassetteCatalogue.FileName);
        File.WriteAllText(sidecar,CassetteComposer.Serialize(presets.Presets));
        var path=sidecar;composer=new CassetteComposer(materials,reactions,sidecar,Path.Combine(sub,"Technology"),_=>false);
        var list=materials.Keys.Where(id=>!CompositePlate.IsCassette(id)&&!SprocketExpandedBallistics.Materials.MaterialMenuVisibility.RetiredPresets.Contains(id))
            .Select(id=>new ComposerMaterial(id,id,CassettePalette.Group(id))).ToArray();
        var native=presets.Presets.Select(p=>p.MaterialId).ToHashSet(StringComparer.Ordinal);
        var clock=now;var box=new double[]{now};
        var m=new CassetteComposerModel(composer,list,Array.Empty<ComposerMaterial>(),()=>CassetteCatalogue.Parse(File.ReadAllText(path)).Presets,_=>false,native.Contains,true,()=>box[0],
            id=>dates.TryGetValue(id,out var d)?d:null);
        if(frame!=null)m.FrameCheck=frame;
        Clock=box;return m;
    }
    private static double[] Clock=new double[1];
    private static void Tick()=>Clock[0]+=.3;
    private static void Material(CassetteComposerModel m,string id)=>m.SetMaterialChoice(m.MaterialChoiceIds.ToList().IndexOf(id));
    /// <summary>The target as the panel reads it: composite by mesh name or cassette material.</summary>
    private static ComposerTarget Target(string? meshName,string material,int faces=1,double min=20,double max=20,int? date=null,bool? known=null)
        =>new("Plate #281",faces,material,material,CompositePlate.IsComposite(meshName,material),min,max,date,known);

    private static void Composer(Action<bool,string> check,string dir,Dictionary<string,CassetteMaterial> materials,Dictionary<string,DateTime> dates,MaterialReactionCatalogue reactions,CassetteCatalogue presets)
    {
        double now=0;
        var m=Model(dir,"apply",materials,dates,reactions,presets,out var composer,out _,ref now);
        var t80=presets.Presets.Single(p=>p.Id=="t80u");var depth=Math.Round(t80.Layers.Sum(CassetteSection.Depth),MidpointRounding.AwayFromZero);

        // ---- the button state around Apply (the screenshot: Apply grey, "NOT A COMPOSITE PLATE") ----
        m.LoadPreset("t80u");
        m.SetTarget(Target(CompositePlate.Name,"rha"));
        check(m.CanApply&&m.ApplyBlocker==null&&m.ApplyButton=="Apply to Plate #281","fix0227: a fresh composite plate can be applied");
        check(m.ApplyNote==$"Sets the material and all faces to {m.ThicknessText} (this preset); faces are now 20 mm.","fix0227: the note is one clear sentence ("+m.ApplyNote+")");
        // after the Apply the mesh is a name-less copy: the plate is still composite through its cassette material
        var after=Target(null,"ebCassette_t80u",1,depth,depth);
        m.SetTarget(after);
        check(after.Composite&&m.CanApply&&m.ApplyBlocker==null&&!m.TargetChip.Contains("not a composite plate")&&!m.TargetChip.Contains("unknown material"),"fix0227: after Apply the plate is still a composite plate and Apply stays on");
        check(m.ApplyNote==$"Sets the material and all faces to {m.ThicknessText} (this preset); faces are now {depth:0} mm.","fix0227: after Apply the note says what the faces are now ("+m.ApplyNote+")");
        m.LoadPreset("t64k");
        check(m.CanApply&&m.ApplyButton=="Apply to Plate #281"&&m.Location=="hull","fix0227: switching to another preset always works (and follows its location)");
        m.LoadPreset("t80u");
        check(m.CanApply,"fix0227: applying the same preset again always works");
        var oldBug=new ComposerTarget("Plate #281",1,"ebCassette_t80u","x",false,depth,depth);
        m.SetTarget(oldBug);
        check(!m.CanApply&&m.ApplyBlocker!.Contains("Composite armour plate")&&m.TargetChip.Contains("not a composite plate"),"fix0227: the 0.22.6 state (no name, identity ignored the cassette) is exactly what blocked Apply");
        m.SetTarget(Target(null,"ebCassette_t80u",1,803,803));
        check(m.CanApply&&m.ApplyNote==$"Sets the material and all faces to {m.ThicknessText} (this preset); faces are now 803 mm."&&!m.ApplyNote.Contains("window"),"fix0227: faces outside the window of the current cassette never disable Apply ("+m.ApplyNote+")");
        m.SetTarget(Target(null,"ebCassette_t80u",2,100,803));
        check(m.ApplyNote.EndsWith("faces are now 100–803 mm.",StringComparison.Ordinal),"fix0227: a range when the faces differ");

        // ---- vehicle date: shown but dimmed, with the reason, and Apply off ----
        int calls=0;var m2=Model(dir,"date",materials,dates,reactions,presets,out _,out _,ref now,id=>{calls++;return true;});
        int old=CassetteAvailability.Key(1959,12,31);
        m2.SetTarget(Target(CompositePlate.Name,"rha",1,20,20,old,true));
        var rows=m2.PresetRows;
        var rowT80=rows.Single(r=>r.Key=="t80u");
        check(rowT80.Tag=="From 1985"&&!rowT80.Available&&rowT80.Reason=="From 1985"&&rows.All(r=>!r.Available),"fix0227: every preset is shown, dimmed with \"From <year>\" on a vehicle dated 1959.12.31");
        var callsAfterRows=calls;
        m2.LoadPreset("t80u");
        check(!m2.CanApply&&m2.ApplyBlocker==$"{t80.Label} exists from 1985.01.01; this vehicle is dated 1959.12.31.","fix0227: Apply is off with the date as the reason ("+m2.ApplyBlocker+")");
        var c1=calls;for(int i=0;i<30;i++){_=m2.CanApply;_=m2.ApplyBlocker;_=m2.ApplyNote;_=m2.PresetRows;_=m2.TargetChip;}
        check(calls==c1&&c1<=callsAfterRows+1,"fix0227: the native frame is asked once per list build and once for the applied preset, never per frame ("+callsAfterRows+"/"+c1+"/"+calls+")");
        m2.SetTarget(Target(CompositePlate.Name,"rha",1,20,20,CassetteAvailability.Key(1985,1,1),true));
        check(m2.PresetRows.Single(r=>r.Key=="t80u").Available&&m2.CanApply,"fix0227: on a vehicle dated 1985.01.01 the preset is available (inclusive)");
        m2.SetTarget(Target(CompositePlate.Name,"rha",1,20,20,CassetteAvailability.Key(9999,12,31),true));
        check(m2.PresetRows.All(r=>r.Available)&&m2.CanApply,"fix0227: the final-era sentinel date allows every preset");
        m2.FrameCheck=_=>false;m2.Invalidate();
        check(!m2.CanApply&&m2.ApplyBlocker!.Contains("Restart Sprocket")&&m2.PresetRows.All(r=>r.Reason==CassetteAvailability.Restart),"fix0227: a preset the native frame lacks although the date is reached says so");
        m2.FrameCheck=null;m2.SetTarget(null);m2.Invalidate();
        check(m2.PresetRows.All(r=>r.Available)&&m2.ApplyBlocker=="Select a plate in the designer.","fix0227: no plate, no vehicle: nothing is dimmed");

        // ---- a plate that already carries a material the game does not know ----
        var m3=Model(dir,"unknown",materials,dates,reactions,presets,out _,out _,ref now);
        m3.LoadPreset("t80u");
        m3.SetTarget(Target(null,"ebCassette_t80u",1,803,803,old,false));
        check(m3.TargetWarning!=null&&m3.TargetWarning.Contains("exists from 1985.01.01")&&m3.TargetWarning.Contains("1959.12.31")&&m3.TargetChip.Contains("unknown material")&&!m3.TargetChip.Contains("not a composite plate"),"fix0227: an applied plate with an unknown material gets a clear warning, not \"not a composite plate\" ("+m3.TargetWarning+")");
        m3.SetTarget(Target(null,"ebCassette_t80u",1,803,803,CassetteAvailability.Key(1991,12,31),false));
        check(m3.TargetWarning!.Contains("Technology file is missing or not loaded"),"fix0227: a missing Technology file is worded as such");
        m3.SetTarget(Target(null,"ebCassette_t80u",1,803,803,old,true));
        check(m3.TargetWarning==null,"fix0227: a known material gets no warning");
        m3.SetTarget(Target(null,"rha",1,20,20,old,false));
        check(m3.TargetWarning==null,"fix0227: only cassette materials are judged");

        // ---- the saved date is never earlier than the newest layer material ----
        var m4=Model(dir,"dates",materials,dates,reactions,presets,out var backend,out var sidecar,ref now);
        m4.NewCassette();
        check(m4.Date=="1975.01.01"&&m4.DateNote=="","fix0227: with no layer the default date stands");
        m4.AddLayer();Tick();
        check(m4.Date=="1920.01.01"&&m4.DateNote.Contains("rha"),"fix0227: the date follows the newest layer material ("+m4.Date+" / "+m4.DateNote+")");
        Material(m4,"cwepGlassTextolite");check(m4.Date=="1964.01.01","fix0227: textolite sets 1964");
        m4.AddLayer();Material(m4,"elastomerNERA");check(m4.Date=="1979.01.01","fix0227: NERA sets 1979");
        m4.AddLayer();Material(m4,"boronCarbide");Tick();check(m4.Date=="1990.01.01"&&m4.DateNote.StartsWith("Newest material: boronCarbide",StringComparison.Ordinal),"fix0227: boron carbide sets 1990 and the note names it ("+m4.DateNote+")");
        m4.SetDate("1980.01.01");Tick();
        check(m4.DateError!=null&&m4.DateError.Contains("boronCarbide")&&m4.HasErrors&&!m4.CanSave&&!m4.CanSaveAsNew,"fix0227: a typed date earlier than the newest material is an error and blocks the save ("+m4.DateError+")");
        m4.SetDate("1995.05.05");Tick();
        check(m4.DateError==null&&m4.Date=="1995.05.05"&&m4.DateNote.StartsWith("Set by you",StringComparison.Ordinal),"fix0227: a later date is accepted and marked as set by the user");
        m4.SetDate("1990.01.01");Tick();
        check(m4.DateError==null&&m4.DateNote.StartsWith("Newest material",StringComparison.Ordinal),"fix0227: typing the newest date hands the date back to the layers");
        m4.SelectLayer(2);m4.DeleteLayer();Tick();
        check(m4.Date=="1979.01.01","fix0227: removing the newest material moves the date back ("+m4.Date+")");
        m4.Undo();Tick();check(m4.Date=="1990.01.01","fix0227: undo restores the date");
        m4.SetDate("1995.05.05");m4.SetLabel("Date test");Tick();
        var saved=m4.Save(true);
        check(saved.Preset.Date=="1995.05.05"&&CassetteCatalogue.Parse(File.ReadAllText(sidecar)).Presets.Any(p=>p.Date=="1995.05.05"),"fix0227: a saved preset carries the shown date");
        var m5=Model(dir,"dates2",materials,dates,reactions,presets,out _,out var sidecar5,ref now);
        m5.NewCassette();m5.AddLayer();Material(m5,"elastomerNERA");m5.SetLabel("Auto date");Tick();
        var autoSaved=m5.Save(true);
        check(autoSaved.Preset.Date=="1979.01.01","fix0227: an own preset is dated with its newest material ("+autoSaved.Preset.Date+")");
        var m6=Model(dir,"dates3",materials,dates,reactions,presets,out _,out _,ref now);
        m6.LoadPreset("leopard2a4");
        check(m6.Date=="1985.01.01","fix0227: a historical preset keeps its own date while it is read-only");
        m6.SelectLayer(0);m6.SetThickness(75);Tick();
        check(m6.Forked&&m6.Date=="1979.01.01","fix0227: the first edit makes an own copy dated with its newest material");
        m6.Undo();Tick();check(m6.Date=="1985.01.01"&&!m6.Forked,"fix0227: undo returns to the historical date");
        foreach(var p in presets.Presets)
        {
            var n=CassetteAvailability.Newest(p.Layers,id=>dates.TryGetValue(id,out var d)?d:null);
            check(n==null||CassetteAvailability.Key(p.Date)>=CassetteAvailability.Key(CassetteAvailability.DateText(n.Value.Date)),"fix0227: preset "+p.Id+" is not dated before its newest material");
        }
        // an imported share file keeps its date unless that is earlier than its newest material
        var share=new CassettePreset("x","Shared","1990.01.01",1,4000,new[]{new CassetteLayer("rha",10,0,"up")},"hull");
        m4.LoadDraft(share,ComposerSource.Import,"imported");
        check(m4.Date=="1990.01.01"&&m4.DraftLocation=="hull"&&m4.Location=="hull","fix0227: an import keeps its later date and its location");
        m4.LoadDraft(share with{Date="1940.01.01",Layers=new[]{new CassetteLayer("cwepGlassTextolite",10,0,"up")},Location="turret"},ComposerSource.Import,"imported");
        check(m4.Date=="1964.01.01"&&m4.DraftLocation=="turret","fix0227: an import dated before its newest material is raised to it");
    }

    // ---------------- item 2: the Turret / Hull location ----------------
    private static void Location(Action<bool,string> check,string dir,Dictionary<string,CassetteMaterial> materials,Dictionary<string,DateTime> dates,MaterialReactionCatalogue reactions,CassetteCatalogue presets)
    {
        var json=File.ReadAllText(Path.Combine(Root,CassetteCatalogue.FileName));
        var hullIds=new[]{"t64k","t72a_hull","t72b_hull","t80u_hull","leopard2a4_hull","m1a1_hull","challenger1_hull","t64a_hull","t90_hull","oplot_hull"};
        check(presets.Presets.Where(p=>p.Location=="hull").Select(p=>p.Id).OrderBy(x=>x).SequenceEqual(hullIds.OrderBy(x=>x)),"fix0227: T-64 combination K, the six 0.22.7 packages and the three 0.22.19 glacis packages are hull; every other default preset is turret");
        check(presets.Presets.Where(p=>p.Location=="turret").Count()==25,"fix0227: 25 turret presets (8 + 17 from 0.22.19)");
        var node=JsonNode.Parse(json)!.AsObject();node["presets"]![1]!.AsObject().Remove("location");
        check(CassetteCatalogue.Parse(node.ToJsonString()).Presets[1].Location=="turret","fix0227: a missing location field means turret");
        void Reject(Action<JsonObject> edit,string what){var n=JsonNode.Parse(json)!.AsObject();edit(n);bool failed=false;try{CassetteCatalogue.Parse(n.ToJsonString());}catch(FormatException){failed=true;}check(failed,"fix0227: parser rejects "+what);}
        Reject(n=>n["presets"]![0]!["location"]="roof","an unknown location");
        Reject(n=>n["presets"]![0]!["location"]=5,"a non-text location");
        Reject(n=>n["presets"]![0]!.AsObject()["locationn"]="hull","a misspelt field");
        var round=CassetteCatalogue.Parse(CassetteComposer.Serialize(presets.Presets));
        check(round.Presets.Select(p=>p.Location).SequenceEqual(presets.Presets.Select(p=>p.Location)),"fix0227: the share/sidecar writer keeps the location");

        double now=0;
        var m=Model(dir,"location",materials,dates,reactions,presets,out var backend,out var sidecar,ref now);
        check(m.Location=="turret"&&m.LocationIndex==0&&m.PresetRows.Count==25&&m.PresetRows.All(r=>r.Location=="turret"),"fix0227: the switch starts on Turret and lists the turret presets");
        m.LocationIndex=1;
        check(m.Location=="hull"&&m.PresetRows.Count==10&&m.PresetRows.All(r=>r.Location=="hull")&&m.PresetRows.Any(r=>r.Key=="t64k"),"fix0227: Hull lists the ten hull presets (0.22.19)");
        m.Location="roof";check(m.Location=="hull","fix0227: an invalid location is ignored");
        m.LoadPreset("t80u");check(m.Location=="turret"&&m.DraftLocation=="turret","fix0227: loading a preset moves the switch to its location");
        m.LoadPreset("t72a_hull");check(m.Location=="hull"&&m.DraftLocation=="hull"&&m.MetaText.Contains(" hull "),"fix0227: ...and the header shows it");
        m.Location="turret";m.NewCassette();check(m.DraftLocation=="turret","fix0227: a new cassette takes the selected location");
        m.Location="hull";m.NewCassette();check(m.DraftLocation=="hull","fix0227: ...also hull");

        // own presets: saved with their location, always listed, each showing its own
        m.LoadPreset("t80u_hull");m.SelectLayer(0);m.SetThickness(65);Tick();
        var ownHull=m.Save(false);
        check(ownHull.Preset.Location=="hull"&&ownHull.Preset.Id.StartsWith("user_t80u_hull",StringComparison.Ordinal),"fix0227: a fork of a hull package is saved as hull ("+ownHull.Preset.Id+")");
        m.LoadPreset("t80u");m.SelectLayer(0);m.SetThickness(170);Tick();
        var ownTurret=m.Save(false);
        check(ownTurret.Preset.Location=="turret","fix0227: a fork of a turret preset is saved as turret");
        m.Tab=1;m.Location="turret";
        var own=m.PresetRows;
        check(own.Count==2&&own.Any(r=>r.Location=="hull"&&r.Subtitle.EndsWith("hull",StringComparison.Ordinal))&&own.Any(r=>r.Location=="turret"&&r.Subtitle.EndsWith("turret",StringComparison.Ordinal)),"fix0227: own presets are listed whatever the switch says, each showing its own location");
        m.LoadPreset(own.Single(r=>r.Location=="hull").Key);
        check(m.Location=="hull"&&m.DraftLocation=="hull","fix0227: choosing an own preset moves the switch to its location");
        check(CassetteCatalogue.Parse(File.ReadAllText(sidecar)).Presets.Count(p=>p.Location=="hull")==11&&CassetteCatalogue.Parse(File.ReadAllText(sidecar)).Presets.Count(p=>p.Location=="turret")==26,"fix0227: the saved sidecar keeps every location (10 + 1 own hull, 8 + 1 own turret)");
    }

    // ---------------- item 2: the hull packages ----------------
    private static void Hull(Action<bool,string> check,Dictionary<string,CassetteMaterial> materials,Dictionary<string,DateTime> dates,MaterialReactionCatalogue reactions,CassetteCatalogue catalogue)
    {
        double energy=.5*17500*.6*1600*1600*Math.PI*.25*.02*.02;
        double Rod(double native,double work)=>600*(1-Math.Pow(Math.Max(0,Math.Pow(Math.Max(0,1-native/600),2/1.43)-work/energy),1.43/2));
        (double Heat,double Ke) Eval(CassetteModel m,double angle)
        {
            var r=angle*Math.PI/180;var ray=new Vector3(0,(float)Math.Sin(r),(float)-Math.Cos(r));
            var jet=m.Evaluate(m.DepthMm,ray,Vector3.UnitZ,Vector3.UnitY,Vector3.UnitX,"chemicalJet",.02,energy,true);
            var rod=m.Evaluate(m.DepthMm,ray,Vector3.UnitZ,Vector3.UnitY,Vector3.UnitX,"longRod",.02,energy,true);
            var native=m.DepthMm*m.Passive.RhaFactor/Math.Cos(r);return (native+jet.JetWorkMm,Rod(native,rod.TotalRodWorkJ));
        }
        // id: reference angle, KE band, HEAT band. 0.22.20: the bands moved up with the calibration of the textolite/ceramic rhaFactors (round 2: textolite 0.65, previous bands 330-360/450-490 for T-72A and so on; reasons in CALIBRATION.md); KE can saturate at the 600 mm reference rod for the T-80U glacis.
        var bands=new Dictionary<string,(double Angle,double K0,double K1,double H0,double H1,string Date,string Label)>
        {
            ["t72a_hull"]=(68,440,510,570,650,"1979.01.01","T-72A glacis"),["t72b_hull"]=(68,490,560,620,700,"1985.01.01","T-72B glacis"),
            ["t80u_hull"]=(68,530,600,660,740,"1985.01.01","T-80U glacis"),["leopard2a4_hull"]=(75,380,440,500,560,"1985.01.01","Leopard 2A4 hull"),
            ["m1a1_hull"]=(72,500,560,700,760,"1985.01.01","M1A1 hull"),["challenger1_hull"]=(68,500,560,600,680,"1983.01.01","Challenger 1 (Burlington) hull")
        };
        int inBand=0;
        foreach(var (id,b) in bands)
        {
            var p=catalogue.Presets.Single(x=>x.Id==id);var m=new CassetteModel(p,materials,reactions);
            check(p.Location=="hull"&&p.Date==b.Date&&p.Label==b.Label,"fix0227: hull preset identity "+id);
            check(materials.TryGetValue(p.MaterialId,out var native)&&native==m.Passive,"fix0227: the Technology file of "+id+" is exactly the generated signature");
            check(dates.TryGetValue(p.MaterialId,out var date)&&CassetteAvailability.DateText(date)==p.Date,"fix0227: the Technology date of "+id+" is the preset date");
            var v=Eval(m,b.Angle);
            bool ke=v.Ke>=b.K0&&v.Ke<=b.K1,heat=v.Heat>=b.H0&&v.Heat<=b.H1;if(ke&&heat)inBand++;
            check(v.Ke>=b.K0*.85&&v.Ke<=b.K1*1.15&&v.Heat>=b.H0*.85&&v.Heat<=b.H1*1.15,$"fix0227: {id} within 15 % of both bands at {b.Angle}° (KE {v.Ke:0}, HEAT {v.Heat:0})");
            check(p.MinimumDepthMm<m.DepthMm&&p.MaximumDepthMm>m.DepthMm&&m.DepthMm<=700&&Math.Abs(p.MinimumDepthMm-Math.Round(m.DepthMm*.75,4))<1e-9,"fix0227: window and depth of "+id);
            check(p.Layers[0].Material is "rha" or "highHardnessSteel"&&p.Layers[^1].Material=="rha"&&p.Layers[^1].ThicknessMm>=20,"fix0227: "+id+" has steel on both faces");
        }
        check(inBand>=4,"fix0227: at least four of the six hull packages are inside both bands ("+inBand+")");
        var t72a=catalogue.Presets.Single(p=>p.Id=="t72a_hull");
        check(t72a.Layers.Select(l=>(l.Material,l.ThicknessMm)).SequenceEqual(new[]{("rha",60d),("cwepGlassTextolite",105d),("rha",50d)}),"fix0227: T-72A glacis is the build of the brief (steel 60 / textolite 105 / steel 50)");
        var t72b=catalogue.Presets.Single(p=>p.Id=="t72b_hull");
        check(t72b.Layers[0].Material=="highHardnessSteel"&&t72b.Layers[0].ThicknessMm==16&&t72b.Layers.Skip(1).Select(l=>(l.Material,l.ThicknessMm)).SequenceEqual(t72a.Layers.Select(l=>(l.Material,l.ThicknessMm))),"fix0227: T-72B glacis is the T-72A build plus a 16 mm HHS add-on plate");
    }

    // ---------------- item 3: the combat report ----------------
    private static CombatReportEvent Hit(string text,int? entity=null)=>new("interaction","",text,RelatedEntity:entity);
    private static CombatReportShot Screenshot16(params CombatReportEvent[] extra)
    {
        var plate=new CombatLayerPart(27,"1","rha","Rolled Homogeneous Armour",95,39,true);
        var events=new List<CombatReportEvent>
        {
            new("layerStart","","Reached layer",530,null,"A",new CombatLayerInfo("A",new[]{plate},false)),
            new("plateResidual","","Traversed",530,406,"A")
        };
        events.AddRange(extra);
        return new CombatReportShot(11,"Outgoing","APDS (tungsten, late)",1265,events,0,530,"M40A1 «Ridgway»",4,2);
    }
    private static void Combat(Action<bool,string> check)
    {
        var damage=new[]{Hit("Crew member Crew member #3 killed",3),Hit("Crew member Crew member #4 killed",4),Hit("ammoRack destroyed",10),Hit("motor destroyed",11),Hit("wheelArray destroyed",12),Hit("fuelTank destroyed",13)};
        var shot=Screenshot16(damage);
        var impact=CombatReportView.Build(new[]{shot})[0];
        check(impact.Head=="#11 OUT · APDS (tungsten, late) → M40A1 «Ridgway» · 530 → 406 mm · PENETRATED","fix0227: the header is one line ("+impact.Head+")");
        check(impact.SummaryLine=="Crew 2/4 killed · ammunition stowage, motor, wheel array, fuel tank destroyed","fix0227 (0.22.41 module name): one summary line, no repeated verb, no dangling 'crew alive' ("+impact.SummaryLine+")");
        check(impact.Summary=="Outgoing · 1265 m/s · 1 layer"&&!impact.Summary.Contains("penetration"),"fix0227: one context line without the penetration ("+impact.Summary+")");
        check(impact.SummaryLines.Length==1&&!impact.SummaryLine.Contains("alive")&&!impact.SummaryLine.Contains("2 of 4"),"fix0227: no separate '2 of 4' line");
        var row=impact.Layers.Single();
        check(row.Name=="Plate #27","fix0227: the plate column reads Plate #27, not the game's number 1 ("+row.Name+")");
        check(row.Reaction=="","fix0227: plain steel has an empty reaction (the 'steel −123' stays in Before → after)");
        check(row.BeforeAfter=="530 → 406"&&row.Thickness=="95 mm"&&row.Angle=="39°"&&row.Note=="","fix0227: the table row, damage not repeated");
        check(!impact.Head.Contains("steel −")&&!impact.SummaryLine.Contains("steel"),"fix0227: no steel loss in the header or the summary");

        // grouping by status
        var mixed=CombatDamageSummary.From(new[]{Hit("Engine destroyed",1),Hit("motor destroyed",2),Hit("Gun damaged",3),Hit("sight damaged",4),Hit("Crew member Gunner killed",5),Hit("Crew member #9 wounded",9)});
        check(mixed.Line(4)=="Crew 1/4 killed (gunner), 1 wounded · engine, motor destroyed; gun, sight damaged","fix0227: parts are grouped by status ("+mixed.Line(4)+")");
        check(CombatDamageSummary.From(new[]{Hit("Crew member #1 killed",1),Hit("Crew member #2 killed",2)}).Line(null)=="2 crew killed"&&CombatDamageSummary.From(new[]{Hit("Crew member #1 wounded",1)}).Line(4)=="Crew 1/4 wounded"&&CombatDamageSummary.From(Array.Empty<CombatReportEvent>()).Line(4)=="","fix0227: crew phrase with and without a known total, and nothing for no damage");
        check(CombatDamageSummary.From(Enumerable.Range(1,12).Select(i=>Hit($"Part{i} destroyed",i))).PartsLine(8).EndsWith(" destroyed +4 more",StringComparison.Ordinal),"fix0227: long part lists are summarised");

        // damage that arose in an earlier layer is noted under it; the last layer's damage is only in the summary
        var plate5=new CombatLayerPart(5,"Plate #5","rha","Steel (RHA)",30,0,true);var plate27=new CombatLayerPart(27,"Plate #27","rha","Steel (RHA)",85,62,true);
        var engine=new CombatLayerPart(41,"Engine",null,null,null,null,false);
        var multi=new CombatReportShot(2,"Incoming","APFSDS 120 mm",1500,new[]
        {
            new CombatReportEvent("layerStart","","Reached layer",550,null,"A",new CombatLayerInfo("A",new[]{plate27},false)),new CombatReportEvent("plateResidual","","Traversed",550,400,"A"),
            Hit("motor destroyed",9),
            new CombatReportEvent("layerStart","","Reached layer",400,null,"B",new CombatLayerInfo("B",new[]{plate5},false)),new CombatReportEvent("plateResidual","","Traversed",400,300,"B"),
            new CombatReportEvent("layerStart","","Reached layer",300,null,"C",new CombatLayerInfo("C",new[]{engine},false)),new CombatReportEvent("nativeStop","","Native stop in Engine",300,0,"C"),
            Hit("Engine destroyed",41)
        },0,550,"T-72A",4,4);
        var multiView=CombatReportView.Build(new[]{multi})[0];
        check(multiView.Layers.Length==3&&multiView.Layers[0].Note=="↳ motor destroyed"&&multiView.Layers[1].Note==""&&multiView.Layers[2].Note=="","fix0227: a damage note under the layer where it arose, none under the last layer ("+string.Join("|",multiView.Layers.Select(l=>l.Note))+")");
        check(multiView.SummaryLine=="motor, engine destroyed"&&multiView.Layers[2].Name=="Engine","fix0227: the summary has all damage; a component layer keeps its name");

        // reaction only for what is not plain steel
        var era=new CombatReportShot(3,"Incoming","HEAT",700,new[]
        {
            new CombatReportEvent("layerStart","","Reached layer",700,null,"E",new CombatLayerInfo("E",new[]{plate27},false)),
            new CombatReportEvent("heavyEra","","Kontakt-5 ERA brick",700,300,"E"),new CombatReportEvent("plateResidual","","Traversed",300,250,"E")
        },0,700,"T-72A");
        var eraRow=CombatReportView.Build(new[]{era})[0].Layers.Single();
        check(eraRow.Reaction.Length>0&&!eraRow.Reaction.Contains("steel "),"fix0227: an ERA reaction is shown ("+eraRow.Reaction+")");

        // column widths: fixed, aligned, text fitted to them
        check(CombatReportView.Columns.Length==7&&CombatReportView.Widths.Length==7&&CombatReportView.RightAligned.Length==7&&CombatReportView.Widths.All(w=>w>0)
              &&CombatReportView.Columns.SequenceEqual(new[]{"#","Plate","Material","Thickness","Obliquity","Pen. in → out mm","Reaction"}),"fix0227 (0.22.41 headers): seven fixed columns # | Plate | Material | Thickness | Obliquity | Pen. in → out | Reaction, no flexible one");
        check(CombatReportView.Widths.Sum()+6*6<=1340,"fix0227: the table fits the 1400 px window ("+(CombatReportView.Widths.Sum()+36)+" px)");
        var long1=new CombatLayerPart(9,new string('P',80),"x",new string('M',90),12345,45,true);
        var wide=new CombatReportShot(4,"Incoming","APFSDS",1500,new[]{new CombatReportEvent("layerStart","","Reached layer",500,null,"A",new CombatLayerInfo("A",new[]{long1},false)),new CombatReportEvent("plateResidual","","Traversed",500,300,"A")},0,500,"T");
        var wideRow=CombatReportView.Build(new[]{wide})[0].Layers.Single();
        check(wideRow.Name.Length<=CombatReportView.Fit(1)&&wideRow.Material.Length<=CombatReportView.Fit(2)&&wideRow.Thickness.Length<=CombatReportView.Fit(3)&&wideRow.Angle.Length<=CombatReportView.Fit(4)&&wideRow.BeforeAfter.Length<=CombatReportView.Fit(5),"fix0227: every cell fits its column ("+wideRow.Name.Length+"/"+CombatReportView.Fit(1)+")");
        check(CombatReportView.Fit(6)*CombatReportView.PixelsPerCharacter<=CombatReportView.Widths[6]&&CombatReportView.Lines("a b c d e f g h i j k l m n o p q r s t u v w x y z a b c d e f g h i j k l m n o p q r s t u v w x y z",CombatReportView.Fit(6),CombatReportView.ReactionMaxLines).All(l=>l.Length<=CombatReportView.Fit(6)),"fix0227: a long reaction wraps inside its column");
        foreach(var word in new[]{HitBlockBuilder.Penetrated,HitBlockBuilder.Stopped,HitBlockBuilder.Ricochet,HitBlockBuilder.NoArmour})
            check(CombatReportView.ResultChipWidth>=word.Length*10+28,"fix0227: the result chip holds "+word);
        check(CombatReportView.DestroyedChipWidth>=CombatReportView.DestroyedText.Length*10+28&&CombatReportView.DestroyedText=="TANK DESTROYED","fix0227: the TANK DESTROYED chip holds its text");
        check(CombatReportView.PlateName("1",27)=="Plate #27"&&CombatReportView.PlateName("#4",9)=="Plate #9"&&CombatReportView.PlateName("",3)=="Plate #3"&&CombatReportView.PlateName("Engine",41)=="Engine"&&CombatReportView.PlateName("Plate #27",27)=="Plate #27"&&CombatReportView.PlateName("Unnamed Structure",8)=="Plate #8","fix0227: numeric or empty names become Plate #entity");
        var destroyed=CombatReportView.Build(new[]{Screenshot16(Hit("Crew member Crew member #1 killed",1),Hit("Crew member Crew member #2 killed",2),Hit("Crew member Crew member #3 killed",3),Hit("Crew member Crew member #4 killed",4)) with{CrewAlive=0}})[0];
        check(destroyed.Destroyed&&destroyed.Head.EndsWith(" · TANK DESTROYED",StringComparison.Ordinal)&&destroyed.SummaryLine=="Crew 4/4 killed","fix0227: TANK DESTROYED closes the header text ("+destroyed.Head+")");
        var withTarget=impact.Head.Split(" · ");
        check(withTarget.Length==4&&withTarget[0]=="#11 OUT"&&withTarget[3]=="PENETRATED","fix0227: header parts: number and direction, ammunition and target, penetration, result");

        // the HTML export follows the same order and wording
        var history=new CombatReportHistory();history.Restore(new[]{multi,shot},0); // the last shot is the newest
        var html=CombatReportHtml.Render(history);
        static string E(string text)=>System.Net.WebUtility.HtmlEncode(text);
        int iHead=html.IndexOf(E("#11 OUT · APDS (tungsten, late) → M40A1 «Ridgway» · 530 → 406 mm · PENETRATED"),StringComparison.Ordinal);
        int iSum=html.IndexOf(E("Crew 2/4 killed · ammunition stowage, motor, wheel array, fuel tank destroyed"),StringComparison.Ordinal);
        int iCtx=html.IndexOf(E("Outgoing · 1265 m/s · 1 layer"),StringComparison.Ordinal);
        int iTable=html.IndexOf("<table>",iHead,StringComparison.Ordinal);
        check(iHead>0&&iHead<iSum&&iSum<iCtx&&iCtx<iTable,"fix0227: the HTML has header, summary, context line, then the table, in the panel order");
        // 0.22.40 adds a separate forensic damage finding. Keep this regression scoped to the original impact header/table.
        var legacyImpact=html[iHead..html.IndexOf("<div class='forensic'>",iHead,StringComparison.Ordinal)];
        check(legacyImpact.IndexOf("Crew 2/4 killed",StringComparison.Ordinal)>0&&legacyImpact.IndexOf("Crew 2/4 killed",StringComparison.Ordinal)==legacyImpact.LastIndexOf("Crew 2/4 killed",StringComparison.Ordinal),"fix0227: the damage text appears once in original header/table; forensic finding is separate");
        var headers=new[]{"<th style='width:40px'>#</th>","<th style='width:190px'>Plate</th>","<th style='width:250px'>Material</th>"};
        int at=0;bool inOrder=true;foreach(var h in headers){var i=html.IndexOf(h,at,StringComparison.Ordinal);if(i<0){inOrder=false;break;}at=i;}
        check(inOrder&&html.Contains("Plate #27")&&html.Contains("<tr class='note'>")&&html.Contains("↳ motor destroyed")&&html.IndexOf("#11 OUT",StringComparison.Ordinal)<html.IndexOf("#2 IN",StringComparison.Ordinal),"fix0227: the HTML table has the same columns, damage notes, and newest impact first");
        check(!html.Contains("Damage: ")&&!html.Contains("crew alive"),"fix0227: no old damage wording in the HTML");
    }
}
