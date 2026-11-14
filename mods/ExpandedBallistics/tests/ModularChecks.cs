using System.Text.Json.Nodes;
using SprocketExpandedBallistics;

internal static class ModularChecks
{
    internal static void Run(Action<bool,string> check)
    {
        var json=ReleaseProfiles.Defaults();
        var draft=new ModularShellDraft(json);
        var oldRuntime=JsonNode.Parse(json)!.AsObject();
        oldRuntime["profiles"]!.AsArray().RemoveAt(oldRuntime["profiles"]!.AsArray().Count-1);
        var oldDraft=new ModularShellDraft(oldRuntime.ToJsonString());
        oldDraft.Set(0,"label","Unfinished custom name");
        var rebased=ModularShellDraft.OpenCurrent(json,oldDraft.Root.ToJsonString(),out var appended);
        check(appended&&rebased.Profiles.Count==draft.Profiles.Count,"Edit opens automatically when update only adds a preset");
        check(rebased.Profiles[0]!["label"]!.GetValue<string>()=="Unfinished custom name","automatic additive reconciliation retains unfinished modular edits");
        var changedRuntime=JsonNode.Parse(json)!;
        changedRuntime["profiles"]![0]!["label"]="Changed externally";
        var rejectedChange=false;
        try{ModularShellDraft.OpenCurrent(changedRuntime.ToJsonString(),oldDraft.Root.ToJsonString(),out _);}catch(IOException){rejectedChange=true;}
        check(rejectedChange,"automatic open still blocks conflicting changes to an existing runtime design");
        check(ModularShellDraft.SameJson(json,draft.Compile().RuntimeJson!),"modular default roundtrip preserves exact fields");
        var tandem=new ModularShellDraft(json);
        var heatIndex=tandem.Profiles.ToList().FindIndex(p=>p!["originBehavior"]!.GetValue<string>()=="heat");
        check(heatIndex>=0&&tandem.Active(heatIndex,"tandemPrecursorPenetrationMm"),"HEAT editor exposes precursor field");
        check(tandem.Number(heatIndex,"tandemPrecursorPenetrationMm")==0,"ordinary HEAT remains single charge by default");
        tandem.Set(heatIndex,"modifiers.chemicalPenetrationMm","600");
        tandem.Set(heatIndex,"modifiers.referenceCalibreMm","100");
        tandem.Set(heatIndex,"modifiers.tandemPrecursorPenetrationMm","200");
        var tandemRuntime=tandem.Compile().RuntimeJson!;
        var tandemProfile=ShellProfiles.Parse(tandemRuntime)[heatIndex];
        check(tandemProfile.TandemPrecursorPenetrationMm==200&&tandemProfile.ChemicalPenetrationMm==600&&tandemProfile.ReferenceCalibreMm==100,"editor compiles independent precursor and main budgets");
        check(new ModularShellDraft(tandemRuntime,tandem.SerializeAuthoring(tandemRuntime)).Number(heatIndex,"tandemPrecursorPenetrationMm")==200,"tandem modifier survives authoring save/reopen");
        var importedTandem=new ModularShellDraft(tandemRuntime);
        check(importedTandem.Number(heatIndex,"tandemPrecursorPenetrationMm")==200&&ModularShellDraft.SameJson(tandemRuntime,importedTandem.Compile().RuntimeJson!),"imported tandem retains budgets without field rewrites");
        tandem.Set(heatIndex,"modifiers.tandemPrecursorPenetrationMm","0");
        check(ShellProfiles.Parse(tandem.Compile().RuntimeJson!)[heatIndex].TandemPrecursorPenetrationMm==0,"zero precursor converts back to single HEAT");
        void Reject(Action operation,string message)
        {
            bool rejected=false;try{operation();}catch(Exception){rejected=true;}
            check(rejected,message);
        }
        Reject(()=>tandem.Set(heatIndex,"modifiers.tandemPrecursorPenetrationMm","-1"),"negative precursor rejected");
        Reject(()=>tandem.Set(heatIndex,"modifiers.tandemPrecursorPenetrationMm","2001"),"precursor above supported runtime bound rejected");
        Reject(()=>tandem.Set(heatIndex,"modifiers.tandemPrecursorPenetrationMm","NaN"),"nonfinite precursor rejected");
        check(tandem.Number(heatIndex,"tandemPrecursorPenetrationMm")==0,"rejected precursor edits leave prior value intact");
        var nonHeat=new ModularShellDraft(json);
        var rodIndex=nonHeat.Profiles.ToList().FindIndex(p=>p!["originBehavior"]!.GetValue<string>()=="apfsds");
        check(!nonHeat.Active(rodIndex,"tandemPrecursorPenetrationMm"),"kinetic editor does not offer tandem charge");
        nonHeat.Set(rodIndex,"modifiers.tandemPrecursorPenetrationMm","200");
        Reject(()=>nonHeat.Compile(),"unsupported tandem kinetic configuration cannot reach runtime");
        Reject(()=>draft.Set(0,"id","new_id"),"stable saved IDs cannot be edited");
        Reject(()=>draft.Set(0,"modifiers.maximumVelocity","NaN"),"nonfinite modifier rejected");
        Reject(()=>draft.Set(0,"modifiers.maximumVelocityFactor","0.75"),"native range enforced");
        draft.Set(0,"label","Custom player name");draft.Set(0,"modifiers.maximumVelocity","2100,5");
        var compiled=ShellProfiles.Parse(draft.Compile().RuntimeJson!);
        check(compiled[0].Label=="Custom player name"&&compiled[0].Settings.MaxVelocity==2100.5,"names and decimal comma edits compile");
        foreach(var behavior in ModularShellDraft.Architectures.Keys)
        {
            var conversion=new ModularShellDraft(json);
            var kinds=ModularShellDraft.Architectures[behavior];
            for(int i=0;i<kinds.Length;i++)conversion.Set(0,"modules."+ModularShellDraft.ModuleKeys[i]+".kind",kinds[i]);
            check(ShellProfiles.Parse(conversion.Compile().RuntimeJson!)[0].Behavior==(behavior is "atgm" or "atgm_gun"?"heat":behavior),"supported architecture compiles: "+behavior);
        }
        var implicitRoot=JsonNode.Parse(json)!.AsObject();
        var rod=implicitRoot["profiles"]!.AsArray().First(p=>p!["behavior"]?.GetValue<string>()=="apfsds")!.AsObject();
        rod["id"]="apfsds";rod.Remove("behavior");
        var implicitDraft=new ModularShellDraft(implicitRoot.ToJsonString());
        int original=implicitDraft.Profiles.ToList().FindIndex(p=>p!["id"]!.GetValue<string>()=="apfsds");
        int copied=implicitDraft.Duplicate(implicitDraft.Profiles[original]!.AsObject());
        check(ShellProfiles.Parse(implicitDraft.Compile().RuntimeJson!)[copied].Behavior=="apfsds","duplicated implicit rod retains rod behavior");
        var pending=new ModularShellDraft(json);
        pending.Set(0,"modules.tail.kind","guided");pending.Set(0,"modules.propulsion.kind","rocket");
        pending.Set(0,"modules.body.kind","longRod");pending.Set(0,"modules.effect.kind","kinetic");pending.Set(0,"modules.carrier.kind","launcher");
        check(pending.Compile().Ready&&ShellProfiles.Parse(pending.Compile().RuntimeJson!)[0].Behavior=="apfsds","guided kinetic rod compiles independently"); pending.Profiles[0]!["modules"]!["body"]!["kind"]="unknown_future_body";
        var tech=new ModularShellDraft(json);tech.Profiles[0]!["nativeTechnologyIds"]!.AsArray().Add("custom_unlock");
        check(tech.Compile().Ready,"explicit technology can be runtime enforced");
        check(tech.SerializeAuthoring().Contains("custom_unlock"),"explicit technology requirement preserved");
        var limited=new ModularShellDraft(json);while(limited.Profiles.Count<ShellProfiles.MaximumProfiles)limited.Duplicate(limited.Profiles[0]!.AsObject());
        Reject(()=>limited.Duplicate(limited.Profiles[0]!.AsObject()),"modular capacity enforced");
        var folder=Path.Combine(SprocketExpandedBallistics.Fix02228Tests.FixtureRoot(),"modular-shell-"+Guid.NewGuid());Directory.CreateDirectory(folder);
        try
        {
            var runtime=Path.Combine(folder,"shells.json");var authoring=Path.Combine(folder,"modules.json");File.WriteAllText(runtime,json);
            check(!File.Exists(authoring),"draft edits do not write before save");
            var result=pending.Save(runtime,authoring);
            check(!result.Ready&&File.ReadAllText(runtime)==json&&File.Exists(authoring),"pending save preserves live catalogue byte for byte");
            var reopened=new ModularShellDraft(json,File.ReadAllText(authoring));
            check(!reopened.Compile().Ready,"pending design survives reopen");
            File.AppendAllText(runtime,"\n");var authoringBefore=File.ReadAllText(authoring);
            Reject(()=>reopened.Save(runtime,authoring),"external runtime mutation blocks pending save");
            check(File.ReadAllText(authoring)==authoringBefore,"failed transaction preserves sidecar");
            File.WriteAllText(runtime,json);File.Delete(authoring);
            var ready=new ModularShellDraft(json);ready.Set(0,"label","Saved name");ready.Save(runtime,authoring);
            check(ShellProfiles.Parse(File.ReadAllText(runtime))[0].Label=="Saved name","ready draft commits live catalogue");
            check(new ModularShellDraft(File.ReadAllText(runtime),File.ReadAllText(authoring)).Compile().Ready,"ready sidecar reopens against committed envelope");
        }
        finally{Directory.Delete(folder,true);}
        var fixtureDirectory=Path.Combine(AppContext.BaseDirectory,"modular-fixtures");
        var legacy=File.ReadAllText(Path.Combine(fixtureDirectory,"shells.example.legacy.json"));
        var modular=File.ReadAllText(Path.Combine(fixtureDirectory,"shells.example.modular.json"));
        check(ModularShellDraft.SameJson(legacy,new ModularShellDraft(legacy,null,modular).Compile().RuntimeJson!),"JS eight-family fixture matches native export");
        var pendingJson=File.ReadAllText(Path.Combine(fixtureDirectory,"shells.example.pending.json"));
        var pendingRoot=JsonNode.Parse(pendingJson)!;
        var pendingLegacy=new JsonObject{["schemaVersion"]=1,["profiles"]=new JsonArray(JsonNode.Parse(pendingRoot["profiles"]![0]!["legacyProfile"]!.ToJsonString()))}.ToJsonString();
        check(new ModularShellDraft(pendingLegacy,null,pendingJson).Compile().Ready,"JS guided rod fixture becomes playable");
        var topDraft=new ModularShellDraft(json);
        var topIndex=topDraft.Profiles.ToList().FindIndex(p=>p!["id"]!.GetValue<string>()=="atgm_topattack_test");
        check(topIndex>=0,"top-attack example exists in the modular catalogue");
        check(ModularShellDraft.Fields.Any(f=>f.Key=="topAttackAltitude")&&ModularShellDraft.Fields.Single(f=>f.Key=="guidanceMode").Choices!.Contains("topattack"),"top-attack fields and guidance choice exist");
        check(topDraft.Active(topIndex,"topAttackAltitude")&&topDraft.Active(topIndex,"topAttackDiveAngle"),"top-attack settings surface for the topattack mode");
        topDraft.Set(topIndex,"modifiers.guidanceMode","sight");
        check(!topDraft.Active(topIndex,"topAttackAltitude"),"switching to sight guidance hides top-attack settings");
        topDraft.Set(topIndex,"modifiers.guidanceMode","topattack");
        topDraft.Set(topIndex,"modifiers.topAttackAltitude","120");
        check(ShellProfiles.Parse(topDraft.Compile().RuntimeJson!)[topIndex].Atgm is {GuidanceMode:"topattack",TopAttackAltitude:120},"editor edit compiles into top-attack settings");
        Reject(()=>topDraft.Set(topIndex,"modifiers.topAttackDiveAngle","95"),"top-attack dive angle bound enforced in the editor");
        Reject(()=>topDraft.Set(topIndex,"modifiers.guidanceMode","lock"),"unknown guidance mode rejected in the editor");
    }
}

