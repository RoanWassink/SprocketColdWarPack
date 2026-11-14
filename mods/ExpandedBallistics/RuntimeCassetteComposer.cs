using System.Text.Json;
using BepInEx;
using HarmonyLib;
using Sprocket.Vehicles.PlateStructures;
using Sprocket.Vehicles.PlateStructures.Design;
using NVector=System.Numerics.Vector3;
namespace SprocketExpandedBallistics;

internal static class RuntimeCassetteComposer
{
    internal static bool CanApply(PlateStructure plate,string id,out string reason)
    {
        reason="";if(!CompositePlate.IsCassette(id))return true;
        var models=RuntimeArmourResponses.Snapshot.Cassettes;
        CassetteModel? model=null;models?.TryGetValue(id,out model);
        bool? native=null;try{native=plate.Vehicle.Tech.TryGetTech(id,out _);}catch(Exception){}
        var state=CassetteAvailability.ForUse(model?.Preset.Date??"",CassetteComposerPanel.VehicleDateKey(plate),model!=null&&RuntimeLiveTechnology.ReachesPlay(id),native);
        reason=state.Available?"":state.Reason;return state.Available;
    }
    internal static CassetteComposer Create(PlateStructureEditor editor)
    {
        var snapshot=RuntimeArmourResponses.Snapshot;
        var reactions=snapshot.MaterialReactions??throw new InvalidOperationException("Material reactions unavailable.");
        var reactive=new HashSet<string>(StringComparer.Ordinal);
        if(snapshot.Catalogue==null)throw new InvalidOperationException("Armour response classification unavailable.");
        foreach(var r in snapshot.Catalogue.Responses)if(r.Kind is "lightEra" or "heavyEra")foreach(var id in r.CompatibleMaterialIds)reactive.Add(id);
        var tech=Path.Combine(Paths.GameRootPath,"Sprocket_Data/StreamingAssets/Technology");
        var sidecar=Path.Combine(Paths.ConfigPath,CassetteCatalogue.FileName);
        // Defaults normally arrive with the installer; absence is read-only.
        if(!File.Exists(sidecar))throw new IOException("Cassette sidecar absent; install the defaults before saving.");
        return new(CassetteTechnology.ReadDirectory(tech),reactions,sidecar,tech,id=>InUse(editor,id),reactive);
    }
    private static bool InUse(PlateStructureEditor editor,string id)
    {
        var items=editor.Component.Vehicle.ObjectReader.Items;
        int count=items.Cast<Il2CppSystem.Collections.Generic.IReadOnlyCollection<Sprocket.Vehicles.VehicleObject>>().Count;
        for(int i=0;i<count;i++)
        {
            var components=items[i].Components;
            int n=components.Cast<Il2CppSystem.Collections.Generic.IReadOnlyCollection<Sprocket.Vehicles.VehicleComponent>>().Count;
            for(int j=0;j<n;j++)if(components[j]?.TryCast<PlateStructure>() is {} plate&&plate.armourTechID==id)return true;
        }
        var roots=new[]{Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),"My Games/Sprocket"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),"AppData/LocalLow/Hamish Dunn/Sprocket"),
            Path.Combine(Paths.GameRootPath,"Sprocket_Data/StreamingAssets")};
        foreach(var root in roots.Where(Directory.Exists))
        foreach(var file in Directory.EnumerateFiles(root,"*.blueprint",SearchOption.AllDirectories))
        {
            using var doc=JsonDocument.Parse(File.ReadAllText(file),new JsonDocumentOptions{AllowTrailingCommas=true});
            if(Contains(doc.RootElement,id))return true;
        }
        return false; // IO/parse failures propagate: never delete on an incomplete index.
    }
    private static bool Contains(JsonElement e,string id)
    {
        if(e.ValueKind==JsonValueKind.Object)foreach(var p in e.EnumerateObject())
        {if(p.Name=="armourTechID"&&p.Value.ValueKind==JsonValueKind.String&&p.Value.GetString()==id)return true;if(Contains(p.Value,id))return true;}
        if(e.ValueKind==JsonValueKind.Array)foreach(var item in e.EnumerateArray())if(Contains(item,id))return true;
        return false;
    }
    internal static CassettePreset FromSelection(PlateStructureEditor editor,string id,string label,string date)
    {
        var selected=RuntimePlateStack.GetSelection(editor);var plates=new List<CassetteSelectionPlate>();
        foreach(var plate in selected)
        {
            if(!plate.HasMesh||plate.VehicleObject.Released)throw new InvalidOperationException("Selected plate is unavailable.");
            var matrix=plate.VehicleTransform.LocalToWorldMatrix;var faces=new List<NVector[]>();
            // 0.22.24: through the snapshot gate (a fresh clean read), never a walk of a mesh that is being edited.
            var mesh=PlateMeshAccess.Read(plate,"from selection",SnapshotUse.Fresh)??throw new InvalidOperationException(PlateMeshAccess.Busy);
            foreach(var face in mesh.Faces)
                faces.Add(face.Corners.Select(p=>N(matrix.MultiplyPoint3x4(new UnityEngine.Vector3(p.X,p.Y,p.Z)))).ToArray());
            var built=plate.Mesh.manifoldMesh?.vertices??throw new InvalidOperationException("Native solid mesh is unavailable.");
            var solid=new NVector[built.Length];for(int i=0;i<built.Length;i++)solid[i]=N(matrix.MultiplyPoint3x4(built[i]));
            plates.Add(new(plate.armourTechID,faces.ToArray(),solid));
        }
        var reference=editor.Component;
        var firstFace=(PlateMeshAccess.Read(reference,"from selection",SnapshotUse.Fresh)??throw new InvalidOperationException(PlateMeshAccess.Busy)).Faces[0].Normal;
        var normal=reference.transform.localToWorldMatrix.inverse.transpose.MultiplyVector(new UnityEngine.Vector3(firstFace.X,firstFace.Y,firstFace.Z)).normalized;
        var rotation=reference.VehicleRoot.VehicleTransform.Rotation;
        return Create(editor).FromSelection(id,label,date,plates,N(normal),N(rotation*UnityEngine.Vector3.up),N(rotation*UnityEngine.Vector3.forward));
    }
    private static NVector N(UnityEngine.Vector3 v)=>new(v.x,v.y,v.z);
    internal static CassetteSaveResult Reload(PlateStructureEditor editor,CassetteSaveResult saved)
    {
        string? error=null;
        try
        {
            SprocketExpandedBallistics.Materials.MaterialEditorRefresh.Reload(editor.Component);
        }
        catch(Exception ex)
        {
            error=ex.Message;
            // Even a failed native reload must leave the saved preset visible with its blocked status.
            try{SprocketExpandedBallistics.Materials.ResponseUi.Reload();SprocketExpandedBallistics.Materials.MaterialDatabase.Reload();}
            catch(Exception ui){error+="; material menu refresh: "+ui.Message;}
        }
        bool native=false;
        try{native=editor.Component.Vehicle.Tech.TryGetTech(saved.Preset.MaterialId,out _);}catch(Exception ex){error??=ex.Message;}
        // 0.22.14: "ready" only when Play will know the material too (the native frame hook); otherwise Apply stays blocked.
        RuntimeLiveTechnology.Registered(saved.Preset.MaterialId);
        var state=CassetteAvailability.AfterLiveRegistration(CassetteAvailability.ForUse(saved.Preset.Date,CassetteComposerPanel.VehicleDateKey(editor.Component),
            RuntimeArmourResponses.Snapshot.Cassettes?.ContainsKey(saved.Preset.MaterialId)==true,native),RuntimeLiveTechnology.Patched);
        Plugin.ModLog.LogInfo("[Cassettes] registered "+saved.Preset.Id+" live: "+(state.Available?"yes":"no")+" (designer frame "+(native?"yes":"no")+", Play frames "+(RuntimeLiveTechnology.Patched?"follow":"startup list only")+")");
        var status=state.Available?"Saved "+saved.Preset.Label+". Registered live; ready to apply.":
            "Saved "+saved.Preset.Label+". "+state.Reason+". Apply is blocked.";
        if(error!=null)status+=" Reload: "+error;
        return saved with{RestartRequired=!state.Available,Status=status};
    }
}

// Guard the native lookup before Build (SetArmourMaterial is inlined there).
// Keep the saved id intact so importing the share file can recover this plate.
[HarmonyPatch]
internal static class RuntimeMissingCassette
{
    private static readonly HashSet<string> warned=new(StringComparer.Ordinal);
    private static readonly HashSet<IntPtr> active=new();
    private static readonly Dictionary<string,CassetteMaterial?> equivalents=new(StringComparer.Ordinal);
    private static string? revision;
    internal static CassetteFallback? Read(PlateStructure plate,string id)
    {
        if(!id.StartsWith("ebCassette_user_",StringComparison.Ordinal)||plate.Vehicle.Tech.TryGetTech(id,out _)){active.Remove(plate.Pointer);return null;}
        CassetteMaterial? known=null;
        // A valid snapshot supplies the equivalent when only the native frame is stale.
        if(RuntimeArmourResponses.Snapshot.Cassettes is {} models&&models.TryGetValue(id,out var cassette))known=cassette.Passive;
        var snapshot=RuntimeArmourResponses.Snapshot;
        if(revision!=snapshot.CassetteJson){equivalents.Clear();revision=snapshot.CassetteJson;}
        if(known==null&&!equivalents.TryGetValue(id,out known))
        {
            try
            {
                var materials=CassetteTechnology.ReadDirectory(Path.Combine(Paths.GameRootPath,"Sprocket_Data/StreamingAssets/Technology"));
                materials.TryGetValue(id,out known);
                var preset=snapshot.CassetteJson==null?null:CassetteCatalogue.Parse(snapshot.CassetteJson).Presets.SingleOrDefault(p=>p.MaterialId==id);
                if(known==null&&preset!=null&&snapshot.MaterialReactions is {} reactions)
                    known=new CassetteModel(preset,materials,reactions).Passive;
            }
            catch(Exception ex){Plugin.ModLog.LogWarning("[Cassettes] Could not recover native equivalent for "+id+": "+ex.Message);}
            equivalents[id]=known;
        }
        var fallback=CassetteMissingPreset.Resolve(id,false,known)!;
        if(warned.Add(id))Plugin.ModLog.LogWarning("[Cassettes] "+id+": "+fallback.Message);
        return fallback;
    }
    [HarmonyPrefix,HarmonyPatch(typeof(PlateStructure),nameof(PlateStructure.Build))]
    [HarmonyPriority(Priority.First)]
    private static void BeforeBuild(PlateStructure __instance,out CassetteFallback? __state)
    {
        __state=Read(__instance,__instance.Blueprint.armourTechID);
        if(__state!=null)__instance.Blueprint.armourTechID="rha";
    }
    [HarmonyFinalizer,HarmonyPatch(typeof(PlateStructure),nameof(PlateStructure.Build))]
    private static void AfterBuild(PlateStructure __instance,CassetteFallback? __state)
    {
        if(__state==null)return;
        __instance.Blueprint.armourTechID=__state.RequestedId;Apply(__instance,__state);
        var mass=__instance.armourVolume*__instance.armourDensity;
        __instance.SetMass(mass,Sprocket.Vehicles.MassType.Armour);__instance.cachedMass=__instance.GetMass(Sprocket.Vehicles.MassType.Everything);__instance.RecalculateCenterOfMass();
        if(SprocketExpandedBallistics.Materials.MaterialBalance.TryMaterialCost(mass,__instance.armourCostMultiplier,Sprocket.Constants.CostPerKgOfArmour,out var cost))
            __instance.SetCost(cost,Sprocket.Vehicles.MassType.Armour,Sprocket.Vehicles.CostType.Material);
        __instance.Vehicle.RaiseDirtyFlags(Sprocket.Vehicles.VehicleDirtyFlags.DamageModel);
    }
    [HarmonyPrefix,HarmonyPatch(typeof(PlateStructure),nameof(PlateStructure.SetArmourMaterial))]
    private static void BeforeMaterial(PlateStructure __instance,ref string __0,out CassetteFallback? __state)
    {__state=Read(__instance,__0);if(__state!=null)__0="rha";}
    [HarmonyPostfix,HarmonyPatch(typeof(PlateStructure),nameof(PlateStructure.SetArmourMaterial))]
    private static void AfterMaterial(PlateStructure __instance,CassetteFallback? __state){if(__state!=null)Apply(__instance,__state);}
    private static void Apply(PlateStructure plate,CassetteFallback fallback)
    {
        active.Add(plate.Pointer);
        plate.armourTechID=fallback.RequestedId;var p=fallback.Passive;
        plate.damageModelParameters=new Sprocket.Vehicles.Colliders.DamageModelParameters((float)p.RhaFactor,(float)p.SpallFactor,(float)p.Density,plate.damageModelParameters.Priority);
        plate.armourDensity=(float)p.Density;plate.armourCostMultiplier=(float)p.CostMultiplier;
    }
    internal static bool Active(PlateStructure plate)=>active.Contains(plate.Pointer);
    [HarmonyPrefix,HarmonyPatch(typeof(PlateStructure),nameof(PlateStructure.Release))]
    private static void Release(PlateStructure __instance)=>active.Remove(__instance.Pointer);
    internal static void Clear(){warned.Clear();active.Clear();equivalents.Clear();revision=null;}
}
