using BepInEx.Configuration;
using DesignerFramework.AssetManagement;
using HarmonyLib;
using Sprocket.Selection;
using Sprocket.UI;
using Sprocket.Vehicles.PlateStructures;
using Sprocket.Vehicles.PlateStructures.Design;
using SprocketExpandedBallistics.Materials;
namespace SprocketExpandedBallistics;

[HarmonyPatch]
internal static class RuntimeCompositePlate
{
    private static readonly CompositePlate.Cache cache=new();
    private static readonly Dictionary<IntPtr,OptionGroup> groups=new();
    private static readonly HashSet<string> warned=new(StringComparer.Ordinal);
    private static bool meshRouteFailed;
    private static ConfigEntry<bool> fallback=null!;
    // 0.22.3: new key (default on). The old "Fallback selector group" (default off) is no longer read, so
    // existing configs do not keep the plate hidden. The group is skipped automatically if the game lists the prefab.
    internal static void Configure(ConfigFile config)
    {
        fallback=config.Bind("Composite plate","Show selector group",true,"Show 'Composite armour plate' in its own Addon Structures group. Skipped automatically when the game lists the prefab itself; created through the native cube add-on with the composite quad mesh when the game cannot load the prefab.");
        // 0.22.24 mitigation A (CRASH-MAP.md 3A): To back plane keeps the thickness of neighbouring corners and the minimum within what the native thickener rebuilds safely.
        backplaneMinimum=config.Bind("Composite plate","Back plane minimum thickness (mm)",3.0,new ConfigDescription("To back plane never writes a corner thinner than this (the native thickener rebuilds near-zero thickness badly). 1 = the 0.22.23 behaviour.",new AcceptableValueRange<double>(1,50)));
        backplaneDelta=config.Bind("Composite plate","Back plane max corner difference (mm)",150.0,new ConfigDescription("To back plane lowers a corner that is more than this much thicker than a neighbouring corner (next corner of the face, or a corner of another treated face at the same point). 5000 = no limit. The number of limited corners is reported after Apply.",new AcceptableValueRange<double>(5,5000)));
        fillClearance=config.Bind("Composite fill","Wall clearance (mm)",10.0,new ConfigDescription("Clear space left before the nearest host wall.",new AcceptableValueRange<double>(0,100)));
        fillMaximum=config.Bind("Composite fill","Maximum thickness (mm)",500.0,new ConfigDescription("Maximum corner thickness when filling a cavity.",new AcceptableValueRange<double>(3,2000)));
        fillReserve=config.Bind("Composite fill","Miss reserve thickness (mm)",50.0,new ConfigDescription("Default thickness offered for corners without a wall; Apply confirmation is required. Must not exceed Maximum thickness.",new AcceptableValueRange<double>(3,2000)));
        cheekMaximum=config.Bind("Composite fill","Cheek maximum thickness (mm)",450.0,new ConfigDescription("Maximum thickness of T-80U cheek fillers.",new AcceptableValueRange<double>(3,2000)));
        cheekShellMaximum=config.Bind("Composite fill","Cheek shell maximum thickness (mm)",1000.0,new ConfigDescription("Maximum thickness you can enter for Cheek fill (shell).",new AcceptableValueRange<double>(3,2000)));
        cheekRear=config.Bind("Composite fill","Cheek rear boundary (m)",.35,new ConfigDescription("Rear limit of cheek fillers, measured forward from the turret origin; leaves room for crew and gun.",new AcceptableValueRange<double>(-2,5)));
        cheekRowDepth=config.Bind("Composite fill","Cheek row depth change (mm)",150.0,new ConfigDescription("Stop before a neighbouring outer row moves further back than this.",new AcceptableValueRange<double>(1,1000)));
        cheekWallAngle=config.Bind("Composite fill","Cheek wall angle (degrees)",60.0,new ConfigDescription("Maximum angle of the front or cheek wall normal from forward.",new AcceptableValueRange<double>(0,85)));
        layerWall=config.Bind("Composite fill","Layer box side wall thickness (mm)",5.0,new ConfigDescription("Thickness of the steel side walls that Convert to layers adds to a box, measured inward from the old side faces. The thickness of the original faces is not used.",new AcceptableValueRange<double>(3,100)));
        // 0.22.69: how a plate in layers is paid. uniform: every millimetre of solid material counts with the same factor, whatever face it is; per layer: the calculation of 0.22.60 to 0.22.68.
        layerHit=config.Bind("Composite fill","Layer hit model","uniform",new ConfigDescription("How a shot is paid by a plate in layers. uniform: every millimetre of solid material of the plate counts with the same factor (the recipe table at that angle, scaled so the plate at its recipe depth gives the same as the plate as one block); it does not depend on recognising layers. per layer: each piece is paid by its own layer (0.22.60 to 0.22.68).",new AcceptableValueList<string>("uniform","per layer")));
        void RefreshLayerHit()=>LayerHit.Model=layerHit.Value=="per layer"?LayerHit.HitModel.PerLayer:LayerHit.HitModel.Uniform;
        layerHit.SettingChanged+=(_,_)=>RefreshLayerHit();RefreshLayerHit();
        ignoreDirty=config.Bind("Composite plate","Mesh read ignores dirty flags",false,"Diagnostic only. The designer readers skip a plate mesh while the native editor flags it as changing. Turn this on only if the log keeps saying 'mesh not readable (dirty flags ...)' on a mesh that is at rest.");
    }
    private static ConfigEntry<double> backplaneMinimum=null!,backplaneDelta=null!;
    private static ConfigEntry<double> fillClearance=null!,fillMaximum=null!,fillReserve=null!;
    private static ConfigEntry<double> cheekMaximum=null!,cheekShellMaximum=null!,cheekRear=null!,cheekRowDepth=null!,cheekWallAngle=null!,layerWall=null!;
    internal static double LayerWallMm=>layerWall?.Value??5;
    internal static CheekSettings CheekLimits=>new(cheekMaximum?.Value??450,cheekRear?.Value??.35,cheekRowDepth?.Value??150,cheekWallAngle?.Value??60);
    /// <summary>0.22.59: the thickness limit of the two fields of Cheek fill (shell) only; the preset and Conform keep Cheek maximum thickness.</summary>
    internal static double CheekShellMaximumMm=>cheekShellMaximum?.Value??1000;
    internal static FillSettings FillLimits=>new(fillClearance?.Value??10,fillMaximum?.Value??500,fillReserve?.Value??50);
    private static ConfigEntry<bool> ignoreDirty=null!;
    private static ConfigEntry<string> layerHit=null!;
    /// <summary>The user-tunable native-thickener limits of To back plane (<see cref="BackplaneLimits"/>).</summary>
    internal static BackplaneLimits Limits=>new(backplaneMinimum?.Value??3.0,(backplaneDelta?.Value??150.0)>=5000?double.PositiveInfinity:backplaneDelta?.Value??150.0);
    internal static bool IgnoreDirtyFlags=>ignoreDirty?.Value==true;
    /// <summary>
    /// 0.22.7: a composite plate is one whose mesh carries the identity name OR whose armour material is a cassette
    /// (a cassette can only be set on a composite plate, and the mesh copy a material/thickness operation makes is name-less).
    /// </summary>
    internal static bool Is(PlateStructure? plate)
    {
        if(plate==null)return false;
        try{if(CompositePlate.IsCassette(plate.armourTechID))return true;}catch(Exception){}
        return plate.HasMesh&&cache.Get(plate.Mesh.Pointer,()=>ReadName(plate.Mesh));
    }
    // ToJson returns the bare MeshBlueprint (no name). The name lives on the mesh
    // blueprint wrapper, which is the same route native save/load uses.
    private static string? ReadName(PlateStructureMesh mesh)
    {
        try{return mesh.ToMeshBlueprint(false)?.Name;}
        catch(Exception){return null;}
    }
    // 0.22.60: the same mesh events also drop the layer table of a plate in layers (RuntimeLayerFaces).
    internal static void Invalidate(IntPtr mesh){cache.Invalidate(mesh);RuntimeLayerFaces.Invalidate(mesh);SolidMarks.ForgetMesh(mesh.ToInt64());RuntimeSolidCollider.Invalidate(mesh);}
    /// <summary>Drops the cached identity of the current mesh of this plate (after an own operation or a native mesh edit).</summary>
    internal static void Invalidate(PlateStructure? plate){try{if(plate!=null){SolidMarks.Forget(plate.Pointer.ToInt64());if(plate.HasMesh){cache.Invalidate(plate.Mesh.Pointer);RuntimeLayerFaces.Invalidate(plate.Mesh.Pointer);SolidMarks.ForgetMesh(plate.Mesh.Pointer.ToInt64());RuntimeSolidCollider.Invalidate(plate.Mesh.Pointer);}}}catch(Exception){}}
    /// <summary>
    /// Makes sure this mesh carries the identity name (no mesh replacement, no placement). Called after every Apply operation
    /// and on undo/redo, because a mesh copy made from ToJson has no name. Returns whether the name is there afterwards.
    /// </summary>
    internal static bool EnsureName(PlateStructureMesh? mesh,ThicknessMode? mode=null)
    {
        if(mesh==null)return false;
        try
        {
            var existing=ReadName(mesh);
            var wanted=CompositePlate.NameForMode(mode??CompositePlate.ModeFromName(existing));
            if(!string.Equals(existing,wanted,StringComparison.Ordinal))NameMesh(mesh,mode??CompositePlate.ModeFromName(existing));
            Invalidate(mesh.Pointer);
            return string.Equals(ReadName(mesh),wanted,StringComparison.Ordinal);
        }
        catch(Exception ex){WarnOnce("Naming the composite mesh failed",ex);Invalidate(mesh.Pointer);return false;}
    }
    internal static void Clear(){cache.Clear();groups.Clear();listed.Clear();capture=null;PlateMeshAccess.Clear();RuntimeLayerFaces.Clear();}
    [HarmonyPostfix,HarmonyPatch(typeof(PlateStructureMesh),nameof(PlateStructureMesh.FromJson))]
    private static void Loaded(PlateStructureMesh __instance)=>Invalidate(__instance.Pointer);
    [HarmonyPostfix,HarmonyPatch(typeof(PlateStructureMesh),nameof(PlateStructureMesh.LoadBlueprint),new[]{typeof(Sprocket.Vehicles.MeshGeneration.PlateStructureMeshBlueprint)})]
    private static void ShapeLoaded(PlateStructureMesh __instance)=>Invalidate(__instance.Pointer);
    // ICF: shared with OnAssignedMeshModified; no argument registers may be read.
    [HarmonyPostfix,HarmonyPatch(typeof(PlateStructure),nameof(PlateStructure.RequestMeshRefresh))]
    private static void Modified(PlateStructure __instance){if(__instance==null)return;Invalidate(__instance);PlateMeshAccess.Modified(__instance);}
    [HarmonyPostfix,HarmonyPatch(typeof(PlateStructureDesigner),nameof(PlateStructureDesigner.GetSelection))]
    private static void Selection(PlateStructureDesigner __instance,ISelectionBuffer __0)
    {
        if(fallback?.Value!=true||NativeListed(__instance))return;
        if(!groups.TryGetValue(__instance.Pointer,out var group))
        {
            var designer=__instance;
            var option=new Option{Name=CompositePlate.Name,Description="Native freely shapeable composite armour plate, 1 x 1 m, 20 mm. Configure Show cassette layers in Settings / keybinds.",
                OnSelect=(Option.PerformSelect)(Action<Il2CppSystem.Object>)(_=>Create(designer))};
            groups[__instance.Pointer]=group=new OptionGroup("Composite armour",new[]{option});
        }
        __0.Add(group.Cast<ISelectionGroup>());
    }

    // ---- 0.22.3: the game did not list our prefab, so the selector group is the default route ----
    private static readonly Dictionary<IntPtr,bool> listed=new();
    /// <summary>True when the native Addon Structures list already holds our prefab (then our group is skipped).</summary>
    private static bool NativeListed(PlateStructureDesigner designer)
    {
        if(listed.TryGetValue(designer.Pointer,out var known))return known;
        bool found=false;
        try
        {
            if(designer.selection!=null)
                foreach(var g in designer.selection)
                    foreach(var o in g?.TryCast<OptionGroup>()?.items??new Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppReferenceArray<Option>(0))
                        if(o!=null&&(CompositePlate.IsName(o.Name)||o.Data?.ToString()?.Contains(CompositePlate.PrefabGuid,StringComparison.OrdinalIgnoreCase)==true)){found=true;break;}
        }
        catch(Exception ex){Plugin.ModLog.LogWarning("[Composite plate] Native list unreadable; selector group kept: "+ex.Message);}
        listed[designer.Pointer]=found;
        Plugin.ModLog.LogInfo(found?"[Composite plate] The game lists the composite prefab itself; the fallback selector group is skipped."
            :"[Composite plate] The game does not list the composite prefab; showing the 'Composite armour' selector group.");
        return found;
    }
    private sealed class Capture{internal PlateStructure? Structure;}
    private static Capture? capture;
    // LoadPrefabPlateStructure -> NewStructure(addonPlateStructureGuid, applique, group): remember the add-on it creates.
    [HarmonyPostfix,HarmonyPatch(typeof(PlateStructureDesigner),nameof(PlateStructureDesigner.NewStructure))]
    private static void Created(PlateStructure __result){if(capture!=null&&__result!=null)capture.Structure=__result;}
    // A native menu click on our prefab (if the game ever lists it): give the mesh its identity name.
    [HarmonyPrefix,HarmonyPatch(typeof(PlateStructureDesigner),nameof(PlateStructureDesigner.LoadPrefabPlateStructure),new[]{typeof(string)})]
    private static void BeforePrefab(string __0,out bool __state){__state=capture==null&&__0==CompositePlate.PrefabGuid;if(__state)capture=new();}
    [HarmonyFinalizer,HarmonyPatch(typeof(PlateStructureDesigner),nameof(PlateStructureDesigner.LoadPrefabPlateStructure),new[]{typeof(string)})]
    private static void AfterPrefab(bool __state)
    {
        if(!__state)return;
        var created=capture?.Structure;capture=null;
        try
        {
            if(created!=null&&!Is(created))
            {
                NameMesh(created.Mesh);
                Plugin.ModLog.LogInfo("[Composite plate] Created through route 'native prefab' (identity name applied).");
            }
        }
        catch(Exception ex){WarnOnce("Native prefab created, identity name failed",ex);}
    }
    /// <summary>
    /// Selector click. Route 1: the native prefab, if the game can load it. Route 2: the native cube add-on,
    /// whose mesh is replaced by the packed composite quad inside the same native placement (one undo step).
    /// </summary>
    private static void Create(PlateStructureDesigner designer)
    {
        PlateStructure? created=null;
        try
        {
            capture=new();designer.LoadPrefabPlateStructure(CompositePlate.PrefabGuid);created=capture.Structure;
        }
        catch(Exception ex)
        {
            Plugin.ModLog.LogWarning("[Composite plate] Native prefab route failed ("+ex.Message+"); using the cube route.");
        }
        finally{capture=null;}
        if(created!=null)
        {
            // The native prefab created the plate: never create a second one if naming fails.
            try{if(!Is(created))NameMesh(created.Mesh);}
            catch(Exception ex){WarnOnce("Native prefab created, identity name failed",ex);}
            Plugin.ModLog.LogInfo("[Composite plate] Created through route 'native prefab'.");
            return;
        }
        try
        {
            capture=new();designer.LoadPrefabPlateStructure(CompositePlate.CubePrefabGuid);created=capture.Structure;
            if(created==null)throw new InvalidOperationException("the native cube prefab created no structure");
            ReplaceMesh(created);
            Plugin.ModLog.LogInfo("[Composite plate] Created through route 'cube prefab + composite quad mesh' (the game does not load our prefab); one native placement operation.");
        }
        catch(Exception ex){if(!meshRouteFailed)Plugin.ModLog.LogError("[Composite plate] Could not create the plate: "+ex.Message);}
        finally{capture=null;}
    }
    /// <summary>Sets the native mesh blueprint name; no mesh replacement and no native placement.</summary>
    internal static void NameMesh(PlateStructureMesh mesh,ThicknessMode mode=ThicknessMode.Uniform)
    {
        var blueprint=mesh.ToMeshBlueprint(false)??throw new InvalidOperationException("the native mesh blueprint is unavailable");
        blueprint.Name=CompositePlate.NameForMode(mode);
        mesh.LoadBlueprint(blueprint);
        mesh.Rebuild();
    }
    /// <summary>Packed-mesh route (as Make independent): a fresh registered mesh from <see cref="CompositePlate.QuadMeshJson"/>.</summary>
    private static void ReplaceMesh(PlateStructure structure)
    {
        if(meshRouteFailed)throw new InvalidOperationException("the packed composite mesh route is disabled after an earlier failure");
        var register=structure.Vehicle.Meshes;
        var fresh=register.New<PlateStructureMesh>();
        try
        {
            fresh.FromJson(CompositePlate.QuadMeshJson);fresh.Rebuild();
            var e=fresh.editMesh;
            if(e==null||e.FaceCount!=1||e.VertexCount!=4)throw new InvalidOperationException("composite quad mesh did not load");
            NameMesh(fresh);
            e=fresh.editMesh;
            if(e==null||e.FaceCount!=1||e.VertexCount!=4)throw new InvalidOperationException("naming changed the composite quad mesh");
            structure.Mesh=fresh; // native setter keeps BodyMeshVUID and subscriptions in sync
            structure.RequestMeshRefresh();structure.RequestRebuild();Invalidate(fresh.Pointer);
        }
        catch(Exception ex)
        {
            if(!meshRouteFailed){meshRouteFailed=true;WarnOnce("Packed composite mesh route disabled after one failure",ex);}
            if(fresh.UserCount==0)register.Release(fresh);throw;
        }
        // 0.22.24: no PurgeUnused here (CRASH-MAP.md D): the native editor may still target the previous mesh; unused meshes are purged by the game itself.
        if(!Is(structure))Plugin.ModLog.LogWarning("[Composite plate] The new mesh has no 'Composite armour plate' name; cassette menu may not appear.");
    }
    private static void WarnOnce(string message,Exception ex)
    {
        if(warned.Add(message))Plugin.ModLog.LogWarning("[Composite plate] "+message+": "+ex.Message);
    }
    [HarmonyPostfix,HarmonyPatch(typeof(PlateStructureEditor),nameof(PlateStructureEditor.OnGUI))]
    private static void Draw(PlateStructureEditor __instance)=>SprocketExpandedBallistics.Materials.Ui.Guard("Composite armour plate",()=>
    {
        if(__instance.Component==null)return;
        CassetteComposerPanel.Track(__instance); // the composer's apply target follows the inspected plate
        try{RuntimeCassettePlateVisual.NoteEditor(__instance.Gateway?.Editor);}catch(Exception){} // lets the layer overlay read the vehicle even without an overlay event
    });
    /// <summary>
    /// 0.22.14: the former "Composite armour plate" block, drawn inside the native "Armour material" block under "Reload armour
    /// materials". "Open composer" shows on every add-on plate, so an ordinary plate can reach "Convert to composite plate".
    /// </summary>
    internal static void DrawInMaterialBlock(PlateStructureEditor editor,IGUIElementDrawer ui)
    {
        var plate=editor.Component;if(plate==null)return;
        var composite=Is(plate);var cassette=CompositePlate.IsCassette(plate.armourTechID);
        bool applique=false;try{applique=plate.Applique;}catch(Exception){}
        if(!composite&&!applique)return; // hull and turret stay native: nothing to compose here
        var composerTip=new UITooltip("Open composer",composite?"Build or pick a cassette, see it to scale with its protection, and apply it to this plate."
            :"Build or pick a cassette. Convert to composite plate turns this add-on plate into a composite plate and applies the cassette in one undoable step.");
        ui.Button("Open composer",SprocketExpandedBallistics.Materials.Ui.Callback(()=>CassetteComposerPanel.Open(editor)),ref composerTip);
        if(!composite)
        {
            var why=RuntimeCompositePlateEdit.Eligibility(plate);
            ui.InfoField(why==null?"Normal plate. Open composer, then Convert to composite plate to use a cassette.":"Normal plate: "+why,1);
            return;
        }
        if(!cassette){ui.InfoField(CompositePlate.Name+": choose a cassette material. Shape this plate with the native tools.",1);return;}
        // 0.22.7: a cassette the game does not know on this vehicle (its date is later than the vehicle date, or the Technology file is
        // not loaded) falls back to default values. Say so here instead of pretending the plate is fine or is not a composite.
        if(CassetteComposerPanel.UnknownMaterialLine(plate) is {} unknown)ui.InfoField(unknown,2);
        if(RuntimeArmourResponses.Snapshot.Cassettes is not {} models||!models.TryGetValue(plate.armourTechID,out var model)){ui.InfoField(CompositePlate.Name+": preset missing, native resistance only.",1);return;}
        ui.InfoField(CompositePlate.Name+": "+model.Preset.Label+" | D "+model.DepthMm.ToString("0.##")+" mm",1);
        // 0.22.24: from the snapshot (a stable copy), never from the live native mesh: while a delete or hole cut runs the last good snapshot is drawn.
        var snapshot=PlateMeshAccess.Read(plate,"inspector faces");
        if(snapshot==null)ui.InfoField("Faces: the plate mesh is being edited; the list returns in a moment.",1);
        // 0.22.60: a plate in layers has one face per layer; the per-face list gives way to the assignment line.
        else if(RuntimeLayerFaces.Line(plate,snapshot) is {} layerLine)ui.InfoField(layerLine,2);
        else
            for(int f=0;f<snapshot.Faces.Length;f++)
            {
                var t=snapshot.Faces[f].MeanThickness();
                ui.InfoField($"Face {f+1}: {t:0.##} mm - layers scaled to this face",1);
            }
        var tip=new UITooltip("Reset thickness to cassette depth","Set the selected faces (or every face without a selection) to the nominal cassette depth in one undoable edit.");
        ui.Button("Reset thickness to cassette depth",SprocketExpandedBallistics.Materials.Ui.Callback(()=>
        {if(!RuntimeCompositePlateEdit.TryApply(editor,model.Preset.MaterialId,model.DepthMm,out var message))Plugin.ModLog.LogWarning(message);editor.RequestRedraw();}),ref tip);
    }
}
