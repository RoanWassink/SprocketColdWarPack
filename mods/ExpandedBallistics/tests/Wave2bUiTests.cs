using System.Diagnostics;
using System.Text.Json.Nodes;
namespace SprocketExpandedBallistics;

/// <summary>Wave 2b: hit panel view model, shell editor checks/visibility, era picker, drafts and the transactional save.</summary>
internal static class Wave2bUiTests
{
    internal static void Run(Action<bool,string> check)
    {
        HitBlocks(check);HitBuffer(check);Captions(check);Eras(check);
        var root=Path.Combine(SprocketExpandedBallistics.Fix02228Tests.FixtureRoot(),"eb-wave2b-"+Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try{Editor(check,root);Drafts(check,root);Saving(check,root);Live(check,root);}
        finally{foreach(var f in Directory.GetFiles(root,"*",SearchOption.AllDirectories))File.SetAttributes(f,FileAttributes.Normal);Directory.Delete(root,true);}
        Console.WriteLine("Wave 2b UI model checks PASS");
    }

    // ---------------- hit panel ----------------
    private static CombatLayerInfo Layer(string id,int entity,string name,string? material,double thickness,double angle)=>new(id,new[]{new CombatLayerPart(entity,name,material,material,thickness,angle,true)},false);
    private static CombatReportShot Shot(int number,string direction,string profile,double? initial,params CombatReportEvent[] events)=>new(number,direction,profile,700,events,0,initial);
    private static void HitBlocks(Action<bool,string> check)
    {
        var pen=HitBlockBuilder.Build(Shot(14,"Outgoing","HEAT-FS 125 mm 3BK14",460,
            new("layerStart","","Reached layer",460,null,"a",Layer("a",11,"Plate #11",null,80,30)),
            new("plateResidual","","Traversed Plate #11",460,96,"a"),
            new("interaction","","Crew member Loader wounded")));
        check(pen.Result==HitBlockBuilder.Penetrated&&pen.Colour=="#79D8E8","penetrated hit is cyan with the outcome word");
        check(pen.Values=="460 → 96 mm"&&pen.Direction=="OUT"&&pen.NumberText=="#14","header numbers and direction");
        check(pen.Detail.Contains("steel −364")&&pen.Detail.Contains("through Plate #11"),"detail lists steel loss and the last plate");
        check(pen.DamageShort=="→ Loader wounded","crew damage on the detail line");

        var stopped=HitBlockBuilder.Build(Shot(15,"Incoming","APFSDS 105 mm M735",318,
            new("layerStart","","Reached layer",318,null,"b",Layer("b",2,"Plate #2","T-64 K",120,0)),
            new("nativeStop","","Native stop in Plate #2",318,null,"b")));
        check(stopped.Result==HitBlockBuilder.Stopped&&stopped.Colour=="#F18E8E"&&stopped.Values=="318 → 0 mm"&&stopped.Direction=="IN","native stop is red, ends at 0 mm");
        check(stopped.Detail.Contains("stopped in Plate #2 (T-64 K)"),"stop names the plate and material");

        var material=HitBlockBuilder.Build(Shot(17,"Incoming","HEAT 105 mm M456",400,
            new("layerStart","","Reached layer",400,null,"c",Layer("c",2,"Plate #2","T-64 K",120,0)),
            new("materialReaction","","textolite: backing confirmed",300,184,"c"),
            new("plateResidual","","Traversed",400,0,"c"),
            new("nativeStop","","Native stop",0,null,"c")));
        check(material.Colour=="#C8A3F3"&&material.Result==HitBlockBuilder.Stopped,"material reaction outranks the stop colour; word still says STOPPED");
        check(material.Detail.StartsWith("textolite −116",StringComparison.Ordinal)&&material.Detail.Contains("steel −284"),"material token first; steel excludes the measured extra");

        var era=HitBlockBuilder.Build(Shot(18,"Outgoing","ATGM 9M133 Kornet",1200,
            new("lightEra","Precursor","Kontakt-5 ERA brick"),new("eraResidual","Precursor","ERA response",250,0,"p"),
            new("layerStart","Precursor","Reached layer",250,null,"p",Layer("p",41,"Block #41","Kontakt-5",15,60)),
            new("layerStart","","Reached layer",1200,null,"m",Layer("m",7,"Plate #7","steel",600,31)),
            new("plateResidual","","Traversed",1200,431,"m"),
            new("interaction","","Crew member Gunner killed"),new("interaction","","Gun damaged"),new("interaction","","Crew member Loader wounded")));
        check(era.Colour=="#F3C66A"&&era.Tandem&&era.Result==HitBlockBuilder.Penetrated,"ERA hit is gold; precursor stage marks tandem");
        check(era.Detail.StartsWith("ERA Kontakt-5 −250 (precursor)",StringComparison.Ordinal),"ERA only in the precursor is labelled");
        check(era.DamageShort=="→ Gunner killed +2"&&era.DamageExpanded=="→ Gunner killed, Gun damaged +1","compact shows one damage item, expanded two");
        check(era.Breakdown.Any(l=>l.StartsWith("PRECURSOR",StringComparison.Ordinal))&&era.Breakdown.Any(l=>l.StartsWith("MAIN CHARGE 1200",StringComparison.Ordinal))&&era.Breakdown.Any(l=>l.StartsWith("Damage:",StringComparison.Ordinal)),"breakdown per stage plus damage line");

        var ricochet=HitBlockBuilder.Build(Shot(16,"Outgoing","AP 100 mm BR-412D",190,
            new("layerStart","","Reached layer",190,null,"d",Layer("d",5,"Plate #5",null,100,68)),
            new("outcome","","Deflected at Plate #5",HitId:"d")));
        check(ricochet.Result==HitBlockBuilder.Ricochet&&ricochet.Colour=="#A5ADB5"&&ricochet.Values=="190 mm"&&ricochet.Detail.Contains("ricochet off Plate #5 (68°)"),"ricochet is grey, one number, angle");

        var unknown=HitBlockBuilder.Build(Shot(19,"Unknown direction","<b>Very long custom shell name that never ends</b>",null,new CombatReportEvent("outcome","","Unknown: no resolved native armour interaction")));
        check(unknown.Result==HitBlockBuilder.Unknown&&unknown.Colour==HitBlockBuilder.Grey&&unknown.Detail=="outcome not measured"&&unknown.Direction=="","unmeasured outcome is grey '?'");
        check(unknown.Ammo.Length<=HitBlockBuilder.AmmoColumns&&unknown.Ammo.EndsWith("…",StringComparison.Ordinal)&&!unknown.Ammo.Contains('<'),"ammo name truncated and rich text escaped");
    }
    private static void HitBuffer(Action<bool,string> check)
    {
        var buffer=new HitPanelBuffer();
        HitBlock B(int n)=>new(){Number=n,NumberText="#"+n};
        var v0=buffer.Version;
        for(int i=1;i<=8;i++)buffer.Add(i*.1,B(i));
        check(buffer.Count==6&&buffer.Shown[0].Number==8&&buffer.Shown[5].Number==3,"at most six blocks, newest first");
        var shown=buffer.Shown;
        check(ReferenceEquals(shown,buffer.Shown)&&buffer.Version==v0+8,"same list instance until something changes (dirty flag)");
        check(!buffer.Expire(5)&&ReferenceEquals(shown,buffer.Shown),"no expiry before the lifetime: no new version, no rebuild");
        check(Math.Abs(buffer.Alpha(buffer.Shown[5],8.8)-.75f)<1e-6&&buffer.Alpha(buffer.Shown[0],5)==1f,"fade over the last two seconds");
        check(buffer.TogglePin()&&buffer.Pinned!.Number==8,"pin the newest block");
        for(int i=9;i<=14;i++)buffer.Add(2+i*.01,B(i));
        check(buffer.Count==6&&buffer.Shown[0].Number==8&&buffer.Shown.Count(b=>b.Number==8)==1,"pinned block survives trimming and stays on top");
        var before=buffer.Version;
        check(buffer.Expire(12.5)&&buffer.Count==1&&buffer.Shown[0].Number==8&&buffer.Version==before+1,"expiry removes old blocks once, pinned stays");
        check(Math.Abs(buffer.Alpha(buffer.Pinned!,20)-HitPanelBuffer.PinnedAlpha)<1e-6,"old pinned block sits at .75");
        check(buffer.IsFocus(buffer.Pinned!),"breakdown focus is the pinned block");
        check(!buffer.TogglePin()&&buffer.Expire(20)&&buffer.Count==0,"unpinned block expires");
        buffer.MaxBlocks=20;buffer.Lifetime=1;check(buffer.MaxBlocks==8&&buffer.Lifetime==4,"config limits clamp to 3-8 hits and 4-30 s");
        buffer.Add(double.NaN,B(99));check(buffer.Count==0,"non-finite time ignored");
        buffer.Add(1,B(1));var text=buffer.CountText;check(ReferenceEquals(text,buffer.CountText)&&text=="1","count text cached per version");
        buffer.Clear();check(buffer.Count==0&&buffer.Pinned==null&&buffer.Shown.Count==0,"clear");
    }

    // ---------------- editor ----------------
    private static void Captions(Action<bool,string> check)
    {
        check(ModularShellDraft.Fields.All(f=>ShellEditorText.Captions.ContainsKey(f.Key)),"every current MSD field has a readable caption");
        check(ShellEditorText.Caption("keepAfterRetirement")=="Keep in later eras (archive)","archive field uses the agreed caption");
        check(ShellEditorText.Caption("someNewFieldM")=="Some new field m","unknown future fields get a generic caption");
        check(ShellEditorText.Caption("kineticKind")=="Kinetic type"&&ShellEditorText.Option("kineticKind","apcbc")=="APCBC"&&ShellEditorText.Option("chemicalSource","tech")=="Dated HEAT technology"
            &&ShellEditorText.Caption("standoffFreeCalibres")=="Free standoff","rebalance 0.21.1 lookup entries present");
        check(ModularShellDraft.Fields.Where(f=>f.Choices!=null).All(f=>f.Choices!.All(c=>ShellEditorText.Option(f.Key,c)!=c)),"every current choice has a readable label");
        check(ModularShellDraft.Options.All(g=>g.Value.All(k=>!ShellEditorText.Module(g.Key,k).Contains("unknown"))),"module labels for all known kinds");
        check(ShellEditorText.Module("tail","laserBeam")=="laserBeam (unknown)","unknown module kind marked");
        check(ShellEditorText.Humane("Profile 'custom_1': guidanceDelay is 5; expected less than maximumFlightTime (3).","custom_1","My ATGM")=="My ATGM: guidance delay is 5; expected less than max flight time (3).","compiler messages use the name and captions");
        check(ShellEditorText.Number(.30000000000000004,.01)=="0.3"&&ShellEditorText.Number(17500,100)=="17500"&&ShellEditorText.Number(.008,.001)=="0.008","number text follows the step");
        check(ShellEditorModel.Groups.SequenceEqual(ModularShellDraft.GroupNames),"groups come from MSD");
    }
    private static void Eras(Action<bool,string> check)
    {
        var files=new[]
        {
            "{\n  \"v\": \"0.0\",\n  \"name\": \"Coldwar3\",\n  \"start\": \"1975.01.01\",\n  \"playable\": true,\n}",
            "{\"name\":\"WWI\",\"start\":\"1914.07.28\",}","{\"name\":\"Coldwar2\",\"start\":\"1960.01.01\"}","{\"name\":\"Coldwar\",\"start\":\"1945.09.03\"}",
            "not json","{\"name\":\"NoDate\"}","{\"name\":\"coldwar2\",\"start\":\"1961.01.01\"}"
        };
        var eras=ShellEraCatalogue.Parse(files);
        check(eras.Select(e=>e.Name).SequenceEqual(new[]{"WWI","Coldwar","Coldwar2","Coldwar3"}),"game era files parse with trailing commas, sorted by date, invalid and duplicate skipped");
        check(eras[3].Friendly=="Cold War III"&&eras[0].Group=="World wars"&&eras[3].Group=="Cold War","friendly names and groups");
        check(ShellEraCatalogue.Load(Path.Combine(SprocketExpandedBallistics.Fix02228Tests.FixtureRoot(),"missing-"+Guid.NewGuid())).Count==8&&ShellEraCatalogue.BuiltIn.Any(e=>e.Name=="Coldwar3"),"fallback list includes Coldwar2/3");
        check(ShellEraCatalogue.Find(eras,"COLDWAR3")==3&&ShellEraCatalogue.Find(eras,"ww1")==0&&ShellEraCatalogue.Find(eras," ")==ShellEraCatalogue.None&&ShellEraCatalogue.Find(eras,"coldwar3x")==ShellEraCatalogue.UnknownEra,"matching follows ShellEraPolicy");
        var options=ShellEraCatalogue.Options(eras,"coldwar3x");
        check(options.Count==6&&options[0].Value==""&&options[^1].Value=="coldwar3x"&&ShellEraCatalogue.OptionIndex(eras,"coldwar3x")==5&&ShellEraCatalogue.OptionIndex(eras,"1960.01.01")==3,"options: always, era start dates, then the custom date");
        check(ShellEraCatalogue.Error(eras,"coldwar3x")!.Contains("real date")&&ShellEraCatalogue.Error(eras,"1945.09.03")==null,"invalid date is an error, a real date is not");
        check(ShellEraCatalogue.Availability(eras,"1975.01.01","Coldwar").EndsWith("✓",StringComparison.Ordinal)&&ShellEraCatalogue.Availability(eras,"1960.01.01","Coldwar3").EndsWith("✗",StringComparison.Ordinal),"availability date against the vehicle era");
    }
    private static (string Runtime,string Authoring,string Import) Files(string root,string name)
    {
        var dir=Path.Combine(root,name);Directory.CreateDirectory(dir);
        var runtime=Path.Combine(dir,"shells.json");File.WriteAllText(runtime,ReleaseProfiles.Defaults());
        return (runtime,Path.Combine(dir,"modules.json"),Path.Combine(dir,"modules.import.json"));
    }
    private static int Find(ShellEditorModel m,string behavior)
    {
        for(int i=0;i<m.Count;i++)if(m.Draft.Profiles[i]!["originBehavior"]!.GetValue<string>()==behavior)return i;
        throw new InvalidOperationException("no "+behavior);
    }
    private static void Editor(Action<bool,string> check,string root)
    {
        var (runtime,authoring,import)=Files(root,"editor");
        var watch=Stopwatch.StartNew();
        // Frozen clock: the debounced compile preview runs only on selection changes here (deterministic).
        var m=ShellEditorModel.Open(runtime,authoring,import,false,null,()=>0);
        var rows=m.Rows;var openMs=watch.Elapsed.TotalMilliseconds;
        check(rows.Count==m.Count&&m.ErrorCount==0&&!m.Dirty,"defaults open clean");
        m.Select(Find(m,"apfsds"));
        check(m.Active("maximumVelocity")&&!m.Active("flightSpeed")&&!m.Active("tandemPrecursorPenetrationMm"),"visibility comes from MSD");
        m.EditText("maximumVelocity","50");
        check(m.FieldError("maximumVelocity")=="Between 100 and 5000 m/s."&&m.FieldText("maximumVelocity")=="50"&&m.Number("maximumVelocity")!=50,"out-of-range text stays as text with a caption-free range message");
        check(m.ErrorCount==1&&m.Dirty&&m.FirstError!.Contains("Propulsion · Max velocity"),"one error counted, named by group and caption");
        m.EditText("maximumVelocity","abc");check(m.FieldError("maximumVelocity")=="Enter a number.","non-number while typing");
        watch.Restart();
        m.EditText("maximumVelocity","2100,5");
        _=m.Rows;var editMs=watch.Elapsed.TotalMilliseconds;
        check(m.FieldError("maximumVelocity")==null&&m.Number("maximumVelocity")==2100.5&&m.FieldText("maximumVelocity")=="2100.5"&&m.ErrorCount==0,"valid text applies at once (decimal comma)");
        check(m.Rows.Single(r=>r.Id==m.SelectedId).State=="EDITED","row marks edited shells");

        var atgm=Find(m,"atgm");m.Select(atgm);
        check(m.Active("launchSpeedMode")&&m.Active("acceleration"),"rocket fields visible on an ATGM");
        m.EditText("acceleration","5000");
        check(m.FieldError("acceleration")!=null&&m.ErrorCount==1,"rocket field error counted");
        m.SetModule("Propulsion",Array.IndexOf(ModularShellDraft.Options["propulsion"],"ballistic"));
        check(!m.Active("acceleration")&&m.FieldError("acceleration")==null&&m.ErrorCount==0,"hidden fields are not checked");
        m.SetModule("Propulsion",Array.IndexOf(ModularShellDraft.Options["propulsion"],"rocket"));
        check(m.FieldError("acceleration")!=null,"the typed error returns with the field");
        m.EditText("acceleration","100");
        m.SetChoice("launchSpeedMode","fixed");
        m.SetNumber("flightSpeed",150);
        check(m.Max("launchSpeed")==150,"launch speed maximum follows flight speed live");
        m.EditText("launchSpeed","180");check(m.FieldError("launchSpeed")=="At most the flight speed (150 m/s).","launch speed above flight speed refused");
        m.EditText("launchSpeed","120");check(m.FieldError("launchSpeed")==null&&m.Number("launchSpeed")==120,"launch speed applied");
        m.SetNumber("flightSpeed",100);
        check(m.FieldError("launchSpeed")=="At most the flight speed (100 m/s).","lowering flight speed flags the visible launch speed");
        m.SetChoice("launchSpeedMode","cannon");
        check(!m.Active("launchSpeed")&&m.Number("launchSpeed")<=100&&m.ErrorCount==0,"hidden launch speed is clamped so Save cannot fail on it");
        m.SetChoice("guidanceTransport","wire");m.SetChoice("guidanceMode","topattack");
        check(m.FieldError("guidanceTransport")!=null,"top attack with wire is flagged");
        m.SetChoice("guidanceMode","sight");check(m.FieldError("guidanceTransport")==null,"rule clears");

        var heat=Find(m,"heat");m.Select(heat);
        m.EditText("chemicalPenetrationMm","0");check(m.FieldError("chemicalPenetrationMm")=="Must be above 0 for HEAT.","HEAT needs a charge");
        m.EditText("chemicalPenetrationMm","400");
        m.SetLabel("");check(m.FieldError("label")=="Enter a name.","empty name");
        var other=m.Draft.Profiles[0]!["label"]!.GetValue<string>();
        m.SetLabel(other.ToUpperInvariant());check(m.FieldError("label")=="Another shell already has this name.","duplicate name (case-insensitive)");
        m.SetLabel("Vanilla ammunition");check(m.FieldError("label")!=null,"reserved name");
        m.SetLabel("  HEAT test  ");check(m.FieldError("label")==null&&m.Label=="HEAT test","valid name applied, trimmed");
        Console.WriteLine($"Wave 2b editor timing: open+rows {openMs:0.0} ms, edit+rows {editMs:0.0} ms");
        check(editMs<250,"an edit recomputes only the edited shell");
    }
    private static void Drafts(Action<bool,string> check,string root)
    {
        var (runtime,authoring,import)=Files(root,"drafts");
        var m=ShellEditorModel.Open(runtime,authoring,import,false);
        var count=m.Count;
        check(!m.CanDelete(0)&&!m.Delete(0)&&!m.CanMove(0,1)&&m.Count==count,"saved shells cannot be deleted or moved");
        var label=m.Draft.Profiles[0]!["label"]!.GetValue<string>();
        var copy=m.Duplicate(0);
        check(copy==1&&m.Selected==1&&m.Label==label+" (copy)"&&m.SelectedId!.StartsWith("custom_",StringComparison.Ordinal)&&m.CanDelete(1)&&m.Rows[1].State=="DRAFT","duplicate lands below as a draft");
        var templates=ShellExampleProfiles.Templates();
        var created=m.Create(templates[0]!.AsObject());
        check(created==m.Count-1&&m.Selected==created&&m.CanDelete(created),"template creates a selected draft at the end");
        check(m.DirtySummary=="2 new drafts","dirty summary counts drafts");
        var id=m.SelectedId;
        check(m.Move(created,-1)&&m.IdAt(created-1)==id&&m.Selected==created-1,"draft moves and stays selected");
        check(m.CanMove(created-1,1)&&!m.CanMove(0,1),"a draft can pass a neighbour; a saved shell never moves");
        m.EditText("maximumVelocity","1");check(m.ErrorCount==1,"error on a draft");
        check(m.Delete(m.Selected)&&m.ErrorCount==0&&m.IndexOf(id!)<0,"deleting a draft drops its errors");
        check(m.Delete(1)&&m.Count==count&&!m.Dirty&&m.DirtySummary=="No unsaved changes","deleting both drafts returns to clean");
        var full=m.Duplicate(0);while(m.CanAdd)m.Duplicate(0);
        check(m.Count==ShellProfiles.MaximumProfiles&&m.Duplicate(0)==-1&&m.Create(templates[0]!.AsObject())==-1,"32-shell limit");
        check(full==1,"first copy index");
    }
    private static void Saving(Action<bool,string> check,string root)
    {
        // Normal save: both files, backup, clean afterwards, drafts become saved.
        var (runtime,authoring,import)=Files(root,"save");
        var original=File.ReadAllText(runtime);
        var m=ShellEditorModel.Open(runtime,authoring,import,false);
        var heat=Find(m,"heat");m.Select(heat);m.EditText("chemicalPenetrationMm","650");
        m.Duplicate(heat);var draftId=m.SelectedId!;
        m.SetEra(m.EraOptions.ToList().FindIndex(o=>o.Value=="1975.01.01"));
        var result=m.Save();
        var saved=ShellProfiles.Parse(File.ReadAllText(runtime));
        check(result.Ready&&File.Exists(authoring)&&saved.Any(p=>p.Id==draftId&&p.MinimumDate==new ShellNativeDate(1975,1,1))&&saved.Single(p=>p.Id==m.IdAt(heat)).ChemicalPenetrationMm==650,"save writes shells.json and modules.json together");
        check(Directory.GetFiles(Path.GetDirectoryName(runtime)!,"shells.json.backup-*").Length==1,"runtime backup written");
        check(!m.Dirty&&m.IsSaved(m.IndexOf(draftId))&&!m.CanDelete(m.IndexOf(draftId)),"after save the draft ID is saved and protected");
        m.Select(draftId);m.SetEra(0);m.Save();
        check(ShellProfiles.Parse(File.ReadAllText(runtime)).Single(p=>p.Id==draftId).MinimumDate==null,"'no restriction' removes minimumDate");

        // Hidden modifiers stay in modules.json but are not compiled (P3).
        (runtime,authoring,import)=Files(root,"hidden");
        var json=File.ReadAllText(runtime);
        var draft=new ModularShellDraft(json);
        var rod=Enumerable.Range(0,draft.Profiles.Count).First(i=>draft.Profiles[i]!["originBehavior"]!.GetValue<string>()=="apfsds");
        draft.Set(rod,"modifiers.tandemPrecursorPenetrationMm","200");
        File.WriteAllText(authoring,draft.SerializeAuthoring());
        var rejected=false;try{new ModularShellDraft(json,File.ReadAllText(authoring)).Compile();}catch(FormatException){rejected=true;}
        m=ShellEditorModel.Open(runtime,authoring,import,false);
        result=m.Save();
        check(rejected&&result.Ready,"a hidden value that the old full compile rejects no longer blocks Save");
        check(JsonNode.Parse(File.ReadAllText(authoring))!["profiles"]![rod]!["modifiers"]!["tandemPrecursorPenetrationMm"]!.GetValue<double>()==200
            &&ShellProfiles.Parse(File.ReadAllText(runtime))[rod].TandemPrecursorPenetrationMm==0,"hidden value kept in modules.json, not compiled into shells.json");

        // An invalid typed date blocks Save until a real date or era start is stored.
        (runtime,authoring,import)=Files(root,"era");
        m=ShellEditorModel.Open(runtime,authoring,import,false,ShellEraCatalogue.BuiltIn);
        m.Select(0);m.SetMinimumDate("coldwar3x");
        check(m.FieldError("minimumDate")!=null&&m.ErrorCount==1&&m.EraIndex==m.EraOptions.Count-1,"invalid date shown as the custom error option");
        var refusedEra=false;try{m.Save();}catch(InvalidOperationException){refusedEra=true;}
        check(refusedEra,"save refused while errors remain");
        m.SetEra(m.EraOptions.ToList().FindIndex(o=>o.Value=="1960.01.01"));
        check(m.ErrorCount==0&&m.EraOptions.All(o=>o.Value!="coldwar3x"),"picking an era start clears the error");

        // Conflict: the catalogue changed after opening.
        (runtime,authoring,import)=Files(root,"conflict");
        m=ShellEditorModel.Open(runtime,authoring,import,false);
        m.SetLabel("Renamed for conflict test");
        var changed=File.ReadAllText(runtime).Replace("\"HE\"","\"HE external\"");File.WriteAllText(runtime,changed);
        var conflict=false;try{m.Save();}catch(IOException ex){conflict=ex.Message.StartsWith("File changed while editing",StringComparison.Ordinal);}
        check(conflict&&!File.Exists(authoring)&&File.ReadAllText(runtime)==changed,"external change detected; nothing written");

        // Rollback: the second file cannot be replaced.
        (runtime,authoring,import)=Files(root,"rollback");
        original=File.ReadAllText(runtime);
        m=ShellEditorModel.Open(runtime,authoring,import,false);
        m.SetLabel("Renamed for rollback test");
        File.SetAttributes(runtime,FileAttributes.ReadOnly);
        var failed=false;try{m.Save();}catch(Exception ex) when (ex is UnauthorizedAccessException or IOException){failed=true;}
        File.SetAttributes(runtime,FileAttributes.Normal);
        check(failed&&!File.Exists(authoring)&&File.ReadAllText(runtime)==original&&m.Dirty,"failed write rolls back modules.json; editor keeps the changes");

        // Conflict at open is recognised (Import offered).
        (runtime,authoring,import)=Files(root,"open-conflict");
        File.WriteAllText(authoring,new ModularShellDraft(File.ReadAllText(runtime)).SerializeAuthoring());
        File.WriteAllText(runtime,File.ReadAllText(runtime).Replace("\"HE\"","\"HE external\""));
        Exception? openError=null;try{ShellEditorModel.Open(runtime,authoring,import,false);}catch(Exception ex){openError=ex;}
        check(openError!=null&&ShellEditorModel.IsConflict(openError),"open conflict recognised");
        m=ShellEditorModel.Open(runtime,authoring,import,true);
        check(m.Dirty&&m.Notice!=null&&m.Draft.Profiles.Any(p=>p!["label"]!.GetValue<string>()=="HE external"),"import resolves the conflict as an unsaved draft");
    }
    private static void Live(Action<bool,string> check,string root)
    {
        var (runtime,authoring,import)=Files(root,"live");
        double now=100;
        var m=ShellEditorModel.Open(runtime,authoring,import,false,null,()=>now);
        m.Cannon=new(120,1700,1800,600,d=>123.4,p=>456,"Coldwar");
        m.Select(Find(m,"apfsds"));
        check(m.LiveValue(6)=="123 mm RHA"&&m.LiveValue(5).EndsWith(" m/s",StringComparison.Ordinal)&&m.LiveValue(4).Contains(" = "),"live numbers from the cannon (immediate on selection)");
        var length=m.LiveValue(3);
        m.SetNumber("penetratorLengthInCalibres",3);
        check(m.LiveValue(3)==length,"live column waits for the debounce");
        now+=.2;
        check(m.LiveValue(3)!=length,"live column recomputes after 150 ms");
        m.Select(Find(m,"heat"));
        check(m.LiveValue(6).StartsWith("456 mm chemical",StringComparison.Ordinal),"chemical budget from the cannon context");
        m.Select(Find(m,"atgm"));
        m.SetNumber("maxTurnRate",0);now+=.2;
        check(m.Issues.Any(i=>i.Kind==ShellIssueKind.Warning&&i.Text.Contains("Turn rate is zero")),"MSD.Analyze warnings in the messages");
        check(m.EraAvailability.Contains("this vehicle: Cold War"),"vehicle era shown next to the era picker");
        var issues=m.Issues;check(ReferenceEquals(issues,m.Issues),"messages cached between frames");
        var watch=Stopwatch.StartNew();
        for(int i=0;i<10;i++){m.SetNumber("maxTurnRate",i+1);m.Refresh(true);}
        Console.WriteLine($"Wave 2b live preview (one-shell compile + analyze + numbers): {watch.Elapsed.TotalMilliseconds/10:0.0} ms per recompute");
        check(ReferenceEquals(m.EraOptions,m.EraOptions)&&ReferenceEquals(m.ModuleLabels("Tail"),m.ModuleLabels("Tail")),"choice lists are cached instances");
    }
}
