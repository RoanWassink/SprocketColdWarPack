using BepInEx.Configuration;
using HarmonyLib;
using Sprocket;
using Sprocket.PlayerControl;
using Sprocket.VehicleDesigner;
using Sprocket.VehicleDesigner.ArmourEditing;
using Sprocket.VehicleDesigner.UI;
using Sprocket.Vehicles;
using Sprocket.Vehicles.PlateStructures;
using UnityEngine;
using UnityEngine.Rendering;
using Object=UnityEngine.Object;

namespace SprocketExpandedBallistics;

// Event-driven, designer-only overlay. No colliders, damage model or paint edits.
// 0.22.7: the overlay is NOT a child of the plate any more. Each plate gets its own root at the scene top level that copies the plate transform
// every designer frame (Follow), so nothing of ours hangs inside the native plate hierarchy that the structure editor walks. The root has no collider,
// and the layer is the plate layer (renders as before) or, with [Plate visual] Layer = ignoreRaycast, the Unity Ignore Raycast layer.
// The designer is recognised from the loaded scene "VehicleDesignerUI" (the same test the UI Kit uses for its Designer context), not only from
// editor events, which did not always arrive in this flow.
[HarmonyPatch]
internal static class RuntimeCassettePlateVisual
{
    private sealed record View(GameObject Root,string Key,Transform Overlay,Transform Plate,IntPtr Mesh,string MaterialId,OverlayResources<Object> Resources,MeshRenderer Renderer,Vector3 Scale,bool NativeHidden,OverlayCopy Data,string Appearance);
    private static readonly Dictionary<IntPtr,PlateStructure> plates=new();
    private static readonly OverlayBuildQueue<PlateStructure> pending=new();
    private static readonly Dictionary<IntPtr,int> retries=new();
    private static readonly Dictionary<IntPtr,string> signatures=new(); // content signature each view was built from (0.22.24)
    private static float nextSignature;
    private static readonly Dictionary<IntPtr,View> views=new();
    private static ConfigEntry<string> mode=null!,layerMode=null!,lookMode=null!;
    private static ConfigEntry<bool> dumpMode=null!;
    /// <summary>0.22.68: write what the game drew for a plate in layers and how the overlay sorted it (BepInEx\config\sprocket.expandedballistics.layer-overlay).</summary>
    internal static bool LayerOverlayDump=>dumpMode?.Value??true;
    /// <summary>0.22.66: a plate in layers is drawn as one closed block with a stripe per layer (painted, the default) or as its loose layers (layers).</summary>
    internal static bool LayerBoxPainted=>lookMode?.Value!="layers";
    private static ConfigEntry<float> opacity=null!,smoothness=null!;
    private static bool materialLogged;
    private static readonly HashSet<IntPtr> coverageWarnings=new(),summaryLogged=new(),degenerateLogged=new();
    private static readonly PlateDiagnosticDumps diagnosticDumps=new();
    private static readonly OverlayVisibility hidden=new(ex=>Plugin.ModLog.LogWarning("[Plate visual] Restore will retry: "+ex.Message));
    private static readonly Func<long,bool> hasView=owner=>views.ContainsKey(new IntPtr(owner));
    private static bool shaderFallbackLogged;
    private static VehicleEditor? activeEditor;
    private static Sprocket.VehicleDesigner.Access.IVehicleEditor? inspectorEditor;
    private static bool armour,wasDesigner;
    private static readonly CassetteDesignerState lifecycle=new();
    private static GameObject[] sceneRoots=Array.Empty<GameObject>();
    private static GameObject[] controlRoots=Array.Empty<GameObject>();
    private static readonly List<IntPtr> gone=new();
    private static int rootScene;
    private static int controlScene;
    private static int generation,warnings,lastFrame=-1,sceneFrame=-1;
    private static float nextTrack;
    private static IntPtr trackedVehicle;
    internal const string DesignerScene="VehicleDesignerUI";
    /// <summary>
    /// Cached active Designer surface, refreshed every frame and on native state notifications.
    /// </summary>
    private static bool Designer=>lifecycle.Active;
    internal static bool ComposerDesignerActive { get { SampleDesigner();return Designer; } }
    private static bool SceneSurface(string name,ref GameObject[] roots,ref int handle)
    {
        var scene=UnityEngine.SceneManagement.SceneManager.GetSceneByName(name);
        if(!scene.isLoaded){roots=Array.Empty<GameObject>();handle=0;return false;}
        if(handle!=scene.handle||roots.Length==0){handle=scene.handle;roots=scene.GetRootGameObjects().ToArray();}
        bool alive=false;
        foreach(var root in roots)if(root!=null){alive=true;if(root.activeInHierarchy)return true;}
        if(!alive)roots=Array.Empty<GameObject>();
        return false;
    }
    private static void SampleDesigner()
    {
        if(Time.frameCount==sceneFrame)return;
        sceneFrame=Time.frameCount;
        try
        {
            bool editorSurface=SceneSurface(DesignerScene,ref sceneRoots,ref rootScene);
            bool playSurface=SceneSurface("VehicleControlUI",ref controlRoots,ref controlScene);
            var change=lifecycle.Observe(editorSurface&&!playSurface,rootScene);
            if(change is DesignerTransition.Left or DesignerTransition.Restarted)Left();
            if(change is DesignerTransition.Entered or DesignerTransition.Restarted)Entered();
        }
        catch(Exception ex){lifecycle.Reset();Left();if(warnings++<12)Plugin.ModLog.LogWarning("[Plate visual] Designer signal: "+ex.Message);}
    }
    private static bool Visible=>lifecycle.Visible(mode.Value=="off",mode.Value=="always"||armour);
    internal static void Configure(ConfigFile config)
    {
        mode=config.Bind("Plate visual","Mode","auto",new ConfigDescription("auto: shown in armour view or via Show cassette layers; always: designer; off: hidden.",new AcceptableValueList<string>("auto","always","off")));
        opacity=config.Bind("Plate visual","Opacity",1f,new ConfigDescription("Cassette layer opacity. 1: opaque; lower values: transparent.",new AcceptableValueRange<float>(.1f,1f)));
        layerMode=config.Bind("Plate visual","Layer","plate",new ConfigDescription("Layer of the overlay (it has no collider and is no child of the plate either way). plate: the layer of the plate; ignoreRaycast: Unity Ignore Raycast, which no ray or overlap query of the editor sees. Try ignoreRaycast if an editor error ever appears only while the layers are shown.",new AcceptableValueList<string>("plate","ignoreRaycast")));
        lookMode=config.Bind("Plate visual","Layer box look","painted",new ConfigDescription("How a plate in layers (Convert to layers) is drawn. painted: one closed block, the front of the first layer, the back of the last layer and the side walls, each strip in the colour of the layer it spans, no loose layers inside; layers: the loose layers themselves, as in 0.22.65. A side without a wall always shows its layers.",new AcceptableValueList<string>("painted","layers")));
        lookMode.SettingChanged+=(_,_)=>InvalidateCatalogue();
        dumpMode=config.Bind("Plate visual","Layer overlay dump",true,new ConfigDescription("Writes what the game drew for a plate in layers and how the overlay sorted it. For troubleshooting; can be switched off."));
        mode.SettingChanged+=(_,_)=>{lifecycle.ClearManual();Visibility();};
        layerMode.SettingChanged+=(_,_)=>InvalidateCatalogue();
        opacity.SettingChanged+=(_,_)=>InvalidateCatalogue();
        smoothness=config.Bind("Plate visual","Smoothness",.15f,new ConfigDescription("Layer surface smoothness (0: matte, 1: polished).",new AcceptableValueRange<float>(0,1)));
        smoothness.SettingChanged+=(_,_)=>InvalidateCatalogue();
        RuntimeCustomTextures.Initialize();
        CassetteVisualKeys.Configure();
    }
    /// <summary>"Show cassette layers" (or the inspector): sets the manual state and logs one line saying what happened.</summary>
    private static void Toggle(bool? show=null)
    {
        SampleDesigner();
        if(!wasDesigner&&Designer)Entered();
        TrackVehicle();
        lifecycle.Toggle(mode.Value=="always"||armour,show);Visibility();
        int cassettes=0;
        foreach(var p in plates.Values){try{if(p!=null&&CompositePlate.IsCassette(p.armourTechID))cassettes++;}catch(Exception){}}
        var note=mode.Value=="off"?" ([Plate visual] Mode is off)":cassettes==0?" (no plate with a cassette material is known on this vehicle)":"";
        Plugin.ModLog.LogInfo($"[Plate visual] {(Visible?"shown":"hidden")}: {cassettes} cassette plate{(cassettes==1?"":"s")}{note}.");
    }
    /// <summary>The inspector reports the editor it is drawn in, so the vehicle can be read even if no overlay event reached us.</summary>
    internal static void NoteEditor(Sprocket.VehicleDesigner.Access.IVehicleEditor? editor)
    {
        if(editor==null||inspectorEditor!=null&&inspectorEditor.Pointer==editor.Pointer)return;
        lifecycle.PlayerState(true);sceneFrame=-1;SampleDesigner();
        inspectorEditor=editor;nextTrack=0;
    }
    internal static void InvalidateCatalogue(){SuspendViews();foreach(var p in plates.Values)Enqueue(p,true);}
    private static void Visibility()
    {
        if(!Visible){SuspendViews();pending.Clear();return;}
        if(Visible)foreach(var p in plates.Values)Enqueue(p,true);
    }
    private static void Enqueue(PlateStructure plate,bool geometryChanged=false)
    {
        // Existing designer build notifications invalidate the visual state; no new native hooks.
        // 0.22.24: a native fault is not catchable, but a managed one (a released VehicleObject mid rebuild) must never leave the hook.
        try
        {
            if(plate==null)return;
            RuntimeMaterialLooks.Note(plate,geometryChanged);
            if(geometryChanged)Suspend(plate.Pointer);
            if(!wasDesigner||plate.VehicleObject==null||plate.VehicleObject.Released)return;
            // Register before testing mesh readiness: load may expose the component before its mesh.
            plates[plate.Pointer]=plate;
            if(!Visible||!CompositePlate.IsCassette(plate.armourTechID))return;
            if(pending.Contains(plate.Pointer.ToInt64())&&!geometryChanged)return;
            if(!plate.HasMesh){pending.Schedule(plate.Pointer.ToInt64(),plate,Time.unscaledTime,.25f,geometryChanged);return;}
            if(!geometryChanged&&views.ContainsKey(plate.Pointer))return;
            pending.Schedule(plate.Pointer.ToInt64(),plate,Time.unscaledTime,.12f,geometryChanged); // coalesces drag/rebuild notifications
        }
        catch(Exception ex){if(warnings++<12)Plugin.ModLog.LogWarning("[Plate visual] Enqueue: "+ex.GetType().Name+": "+ex.Message);}
    }
    [HarmonyPostfix,HarmonyPatch(typeof(VehicleObjectModel),nameof(VehicleObjectModel.Build))]
    private static void ModelBuilt(VehicleObjectModel __instance)
    {
        RuntimeMaterialLooks.NoteModel(__instance);
        if(!wasDesigner)return;
        var components=__instance.VehicleObject?.Components;if(components==null)return;
        int count=components.Cast<Il2CppSystem.Collections.Generic.IReadOnlyCollection<VehicleComponent>>().Count;
        for(int i=0;i<count;i++)if(components[i]?.TryCast<PlateStructure>() is {} plate)Enqueue(plate);
    }
    [HarmonyPostfix,HarmonyPatch(typeof(PlateStructure),nameof(PlateStructure.Build))]
    private static void Built(PlateStructure __instance){Enqueue(__instance,true);RuntimeLayerFaces.Weigh(__instance);} // 0.22.61: weight of a plate in layers
    // ICF: shared with OnAssignedMeshModified; no argument registers may be read.
    [HarmonyPostfix,HarmonyPatch(typeof(PlateStructure),nameof(PlateStructure.RequestMeshRefresh))]
    private static void Modified(PlateStructure __instance){if(__instance==null)return;Enqueue(__instance,true);}
    [HarmonyPostfix,HarmonyPatch(typeof(VehicleEditor),nameof(VehicleEditor.ApplyOverlay))]
    private static void Overlay(VehicleEditor __instance,VehicleOverlay __0)
    {
        RuntimeMaterialLooks.Overlay(__0!=null);
        activeEditor=__instance;
        lifecycle.PlayerState(true);sceneFrame=-1;SampleDesigner();
        if(!Designer)return;
        armour=__0?.TryCast<ArmourOverlay>()!=null;TrackVehicle();Visibility();
    }
    private static void TrackVehicle()
    {
        if(!Designer)return;
        try
        {
            // Selection/ApplyOverlay callbacks can arrive only after the first edit.
            // The active designer UI already owns the current editor, including after a vehicle load.
            var editor=DiscoverEditor();if(editor==null)return;
            var target=editor.Target;
            var items=target?.ObjectReader.Items;if(items==null)return;
            if(target!.Pointer!=trackedVehicle)
            {ClearViews();pending.Clear();plates.Clear();trackedVehicle=target.Pointer;}
            var found=new HashSet<IntPtr>();
            int count=items.Cast<Il2CppSystem.Collections.Generic.IReadOnlyCollection<VehicleObject>>().Count;
            for(int i=0;i<count;i++)
            {
                var parts=items[i]?.Components;if(parts==null)continue;
                int n=parts.Cast<Il2CppSystem.Collections.Generic.IReadOnlyCollection<VehicleComponent>>().Count;
                for(int j=0;j<n;j++)if(parts[j]?.TryCast<PlateStructure>() is {} plate)
                {found.Add(plate.Pointer);Enqueue(plate);}
            }
            foreach(var key in plates.Keys.Where(key=>!found.Contains(key)).ToArray())
            {pending.Remove(key.ToInt64());retries.Remove(key);Remove(key);plates.Remove(key);}
        }
        catch(Exception ex){if(warnings++<12)Plugin.ModLog.LogWarning("[Plate visual] Designer discovery: "+ex.Message);}
    }
    private static Sprocket.VehicleDesigner.Access.IVehicleEditor? DiscoverEditor()
    {
        foreach(var root in sceneRoots)
        {
            if(root==null||!root.activeInHierarchy)continue;
            foreach(var ui in root.GetComponentsInChildren<VehicleEditorUI>(false))
            {
                if(ui==null||!ui.isActiveAndEnabled)continue;
                var source=ui.editorSource;
                if(source!=null)return source.HasEditor?source.Editor:null;
            }
        }
        return inspectorEditor??activeEditor?.Cast<Sprocket.VehicleDesigner.Access.IVehicleEditor>();
    }
    [HarmonyPostfix,HarmonyPatch(typeof(ArmourOverlayConfig),nameof(ArmourOverlayConfig.Draw))]
    private static void ArmourDraw(){if(!Designer)return;RuntimeMaterialLooks.Overlay(true);if(!armour){armour=true;Visibility();}}
    [HarmonyPostfix,HarmonyPatch(typeof(VehicleEditorScenarioGameState),nameof(VehicleEditorScenarioGameState.OnPlayerStateChanged))]
    private static void State(VehicleEditorScenarioGameState __instance,PlayerStateChangeEventArgs __1)
    {
        if(__1?.NewState==null)return;
        bool editor=__instance.editorPlayerState?.Pointer==__1.NewState.Pointer;
        lifecycle.PlayerState(editor);sceneFrame=-1;SampleDesigner();
    }
    /// <summary>Designer entered (scene loaded, or the editor state came back): read the vehicle and show what the mode says.</summary>
    private static void Entered()
    {
        if(wasDesigner||!Designer)return;
        wasDesigner=true;armour=false;nextTrack=0;
        TrackVehicle();Visibility();
        Plugin.ModLog.LogInfo($"[Plate visual] Designer detected (scene {DesignerScene}); mode {mode.Value}, {plates.Count} plate(s) read.");
    }
    /// <summary>Play or another scene: nothing of the overlay may stay.</summary>
    private static void Left()
    {
        bool had=wasDesigner||views.Count>0||plates.Count>0;wasDesigner=false;
        ClearViews();pending.Clear();plates.Clear();trackedVehicle=IntPtr.Zero;armour=false;inspectorEditor=null;
        if(had&&ReleaseDiagnostics.Enabled)Plugin.ModLog.LogInfo("[Plate visual] Play cleanup: 0 objects, 0 owned meshes.");
    }
    [HarmonyPostfix,HarmonyPatch(typeof(Player),nameof(Player.GameLateUpdate))]
    private static void Drain()=>Tick();
    /// <summary>
    /// Once per frame, from Player.GameLateUpdate and from our own MonoBehaviour (the first one to run that frame wins), so a missing hook cannot
    /// silence the feature. In Play: the cached scene test and one bool. In the designer: the key, discovery, rebuilds and the follow step.
    /// </summary>
    internal static void Tick()
    {
        try{TickCore();}
        catch(Exception ex){ClearViews();if(warnings++<12)Plugin.ModLog.LogWarning("[Plate visual] Tick: "+ex.GetType().Name+": "+ex.Message);}
    }
    private static void TickCore()
    {
        if(lastFrame==Time.frameCount)return;
        lastFrame=Time.frameCount;
        hidden.RestoreOrphans(hasView);
        SampleDesigner();
        RuntimeMaterialLooks.Tick(Designer);
        var pressed=CassetteVisualKeys.Pressed; // keep the shared binding alive across scene changes
        if(!Designer){if(wasDesigner){wasDesigner=false;Left();}return;}
        if(!wasDesigner)Entered();
        if(pressed)Toggle();
        // Reload can replace the vehicle while non-cassette plates remain cached. Discover the current target
        // once a second; only newly discovered plates are queued, so this does not rebuild every mesh.
        if(Time.unscaledTime>=nextTrack){nextTrack=Time.unscaledTime+1f;TrackVehicle();}
        if(PlateMeshAccess.Gate.Quiet){BeginEdit();return;}
        FollowVisible();
        if(PlateMeshAccess.Gate.Busy)return; // mouse-only orbit retains/follows the cached view; no mesh read/upload
        PollSignatures();
        if(pending.Count==0)return;
        foreach(var item in pending.TakeDue(Time.unscaledTime,8))
        {
            var key=new IntPtr(item.Key);
            try{Rebuild(item.Plate);}
            catch(Exception ex)
            {
                Suspend(key);
                if(Visible&&plates.ContainsKey(key)&&!views.ContainsKey(key))pending.Schedule(item.Key,item.Plate,Time.unscaledTime,1f);
                if(warnings++<12)Plugin.ModLog.LogWarning("[Plate visual] "+ex.Message);
            }
        }
    }
    /// <summary>
    /// 0.22.24: the overlay does not rely on OnAssignedMeshModified alone. The edge tool, a vertex move, a thickness edit or an extrude can change a
    /// plate without that event, so once a second (and never in the quiet period) the content signature of each drawn plate is compared with
    /// the one its view was built from, and a change queues a rebuild.
    /// </summary>
    private static void PollSignatures()
    {
        if(!Visible||views.Count==0||Time.unscaledTime<nextSignature)return;
        nextSignature=Time.unscaledTime+1.1f;
        if(PlateMeshAccess.Gate.Busy)return;
        // Notifications and polling validate content; neither destroys the cache.
        foreach(var key in views.Keys)if(plates.TryGetValue(key,out var plate))pending.Schedule(key.ToInt64(),plate,Time.unscaledTime,0);
    }

    private static void Rebuild(PlateStructure plate)
    {
        long started=System.Diagnostics.Stopwatch.GetTimestamp();
        if(plate==null)return;
        if(plate.VehicleObject==null||plate.VehicleObject.Released||!CompositePlate.IsCassette(plate.armourTechID)){Remove(plate.Pointer);return;}
        if(!Visible){Suspend(plate.Pointer);return;}
        // 0.22.24: the mesh is only read through the snapshot gate (not during the quiet period after an edit, not with a mouse button held, not
        // while it is flagged dirty or half deleted). Not readable now: keep retrying without requiring an edit.
        var snap=PlateOverlaySnapshotReader.Read(plate,"plate visual");
        if(snap==null)
        {
            Suspend(plate.Pointer);
            if(views.ContainsKey(plate.Pointer))return; // retry queue is only for missing overlays
            retries.TryGetValue(plate.Pointer,out var tries);
            retries[plate.Pointer]=Math.Min(tries+1,41);
            pending.Schedule(plate.Pointer.ToInt64(),plate,Time.unscaledTime,tries<40?.25f:1f);
            if(tries==40&&warnings++<12)Plugin.ModLog.LogWarning("[Plate visual] Plate #"+plate.VUID.Value+": mesh still unreadable; retrying once a second until ready.");
            return;
        }
        retries.Remove(plate.Pointer);
        var snapshot=RuntimeArmourResponses.Snapshot;
        CassetteModel? model=null;snapshot.Cassettes?.TryGetValue(plate.armourTechID,out model);
        string signature=OverlayContent.Signature(snap.Signature,plate.armourTechID,System.Text.Json.JsonSerializer.Serialize(model?.Preset))+(CompositePlate.IsSolidBlock(RuntimeLayerFaces.MeshName(plate,true))?SolidBlockOverlay.Mark:""); // 0.22.83: the [Solid block] mark only for a marked plate
        string hash=Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(signature)));
        if(views.TryGetValue(plate.Pointer,out var previous)&&previous.Root!=null&&signatures.TryGetValue(plate.Pointer,out var built)&&OverlayContent.Same(built,signature))
        {
            previous.Root.layer=OverlayLayer(plate);Place(previous.Overlay,plate.transform);
            if(previous.Appearance!=Appearance)ApplyAppearance(previous.Renderer.sharedMaterial);
            previous.Renderer.enabled=true;
            previous.Root.SetActive(true);
            var covered=previous.NativeHidden&&hidden.Intact(plate.Pointer.ToInt64());
            if(!covered){hidden.Restore(plate.Pointer.ToInt64());covered=HideNative(plate,previous.Data);}
            views[plate.Pointer]=previous with{Mesh=plate.Mesh.Pointer,MaterialId=plate.armourTechID,Scale=plate.transform.lossyScale,NativeHidden=covered,Appearance=Appearance};
            return;
        }
        var data=CassetteNativeOverlay.Copy(snap.Front,snap.Back,snap.Sides,model?.Preset);
        data=RuntimeLayerFaces.Overlay(plate,data)??data; // 0.22.61: a plate in layers shows each face's own layer
        if(summaryLogged.Add(plate.Pointer))Plugin.ModLog.LogInfo(FormattableString.Invariant($"[Plate visual] Plate #{plate.VUID.Value}: nearest-layer fallback {data.FallbackTriangles} triangles, {data.FallbackArea:0.######}/{data.SideArea:0.######} side area."));
        try{diagnosticDumps.Write(System.IO.Path.Combine(BepInEx.Paths.ConfigPath,"sprocket.expandedballistics.eb-diag"),plate.VUID.Value.ToString(System.Globalization.CultureInfo.InvariantCulture),snap.Front,snap.Back,snap.Sides,data);}
        catch(Exception ex){Plugin.ModLog.LogWarning("[Plate visual] Diagnostic dump: "+ex.Message);}
        if(plate.Mirrored)data=PlateOverlayGeometry.Mirror(data,snap.Mirror);
        var root=new GameObject("Cassette layers"){hideFlags=HideFlags.HideAndDontSave};root.SetActive(false);
        var key=hash+":"+(++generation);var resources=new OverlayResources<Object>(value=>{if(value!=null)Object.DestroyImmediate(value);});
        resources.Own(root);
        try
        {
            root.layer=OverlayLayer(plate);Place(root.transform,plate.transform); // top level on purpose: never a child of the plate
            var stripe=Strip(model?.Preset,resources);
            var renderer=Add(root,data,stripe,resources,out var degenerate);
            // 0.22.74: a point without a usable normal (the tip of a triangle without area) is skipped, not fatal for the plate; one line per plate.
            if(degenerate>0&&degenerateLogged.Add(plate.Pointer))Plugin.ModLog.LogInfo(FormattableString.Invariant($"[Plate visual] Plate #{plate.VUID.Value}: {degenerate} degenerate normals skipped"));
            Remove(plate.Pointer);
            views[plate.Pointer]=new(root,key,root.transform,plate.transform,plate.Mesh.Pointer,plate.armourTechID,resources,renderer,plate.transform.lossyScale,false,data,Appearance);signatures[plate.Pointer]=signature;
            if(!Visible||PlateMeshAccess.Gate.Busy){Remove(plate.Pointer);return;}
            root.SetActive(true);
            var covered=HideNative(plate,data);
            views[plate.Pointer]=views[plate.Pointer] with{NativeHidden=covered};
            if(ReleaseDiagnostics.Enabled)
                Plugin.ModLog.LogInfo($"[Plate visual] Plate #{plate.VUID.Value} generation {generation}: 1 native mesh copy, {data.Vertices.Length} vertices, {(System.Diagnostics.Stopwatch.GetTimestamp()-started)*1000d/System.Diagnostics.Stopwatch.Frequency:0.###} ms (includes native upload). Cached until plate content changes; missing plates retry automatically.");
        }
        catch{Remove(plate.Pointer);hidden.Restore(plate.Pointer.ToInt64());resources.Clear();throw;}
    }
    private static int OverlayLayer(PlateStructure plate)=>layerMode.Value=="ignoreRaycast"?2:plate.gameObject.layer;
    private static void Place(Transform overlay,Transform plate)
    {
        overlay.SetPositionAndRotation(plate.position,plate.rotation);
        overlay.localScale=plate.lossyScale;
    }
    /// <summary>The overlay roots copy the transform of their plate (the plate may be dragged or its vehicle moved); a vanished plate drops its view.</summary>
    internal static void FollowVisible()
    {
        try{FollowCore();}
        catch(Exception ex){ClearViews();if(warnings++<12)Plugin.ModLog.LogWarning("[Plate visual] Follow: "+ex.Message);}
    }
    private static void FollowCore()
    {
        if(!Visible){SuspendViews();return;}
        if(PlateMeshAccess.Gate.Quiet){BeginEdit();return;}
        if(views.Count==0)return;
        gone.Clear();
        foreach(var pair in views)
        {
            try
            {
                if(!plates.TryGetValue(pair.Key,out var plate)||plate==null||plate.VehicleObject==null||plate.VehicleObject.Released||pair.Value.Root==null||!CompositePlate.IsCassette(plate.armourTechID)){gone.Add(pair.Key);continue;}
                // Mesh pointers, renderer replacement and transient refresh flags are not content.
                if(!plate.HasMesh||plate.meshRefresh||plate.Mesh.RebuildRequired){Suspend(pair.Key);Enqueue(plate,true);continue;}
                if(!pair.Value.Root.activeSelf)continue; // awaiting a safe content validation
                if(pair.Value.NativeHidden&&!hidden.Intact(pair.Key.ToInt64()))
                {hidden.Restore(pair.Key.ToInt64());HideNative(plate,pair.Value.Data);}
                Place(pair.Value.Overlay,pair.Value.Plate);
            }
            catch(Exception){gone.Add(pair.Key);}
        }
        foreach(var key in gone){Remove(key);if(plates.TryGetValue(key,out var plate))Enqueue(plate);}
    }
    // Called by the existing edit signal, and by both per-frame paths before any mesh read.
    internal static void BeginEdit()
    {
        RuntimeMaterialLooks.BeginEdit();
        if(views.Count==0)return;
        foreach(var key in views.Keys)if(plates.TryGetValue(key,out var plate))pending.Schedule(key.ToInt64(),plate,Time.unscaledTime,.12f);
        SuspendViews();
    }
    private sealed class NativePlateRenderer:IOverlayNativeRenderer
    {
        private readonly PlateStructure plate;private readonly int slot,instance;private readonly Renderer renderer;
        internal NativePlateRenderer(PlateStructure plate,int slot,Renderer renderer)
        {this.plate=plate;this.slot=slot;this.renderer=renderer;instance=renderer.GetInstanceID();}
        public long Identity=>renderer.Pointer.ToInt64();
        public bool Current
        {
            get
            {
                if(plate==null||renderer==null)return false;
                var current=NativeRenderer(plate,slot);
                return current!=null&&current.Pointer==renderer.Pointer&&current.GetInstanceID()==instance;
            }
        }
        public bool Hidden{get=>renderer.forceRenderingOff;set=>renderer.forceRenderingOff=value;}
    }
    private static Renderer? NativeRenderer(PlateStructure plate,int slot)=>
        (slot==0?plate.highDetailRenderer:slot==1?plate.lowDetailRenderer:plate.shadowProxyRenderer)?.Renderer;
    private static bool HideNative(PlateStructure plate,OverlayCopy data)
    {
        bool any=false;
        for(int slot=0;slot<3;slot++)
        {
            var wrapper=slot==0?plate.highDetailRenderer:slot==1?plate.lowDetailRenderer:plate.shadowProxyRenderer;
            var renderer=wrapper?.Renderer;var mesh=wrapper?.SharedMesh;if(renderer==null)continue;
            bool covered=false;
            if(mesh!=null)
            {
                var bounds=mesh.bounds;var corners=new List<System.Numerics.Vector3>();
                for(int i=0;i<8;i++)
                {
                    var p=bounds.center+Vector3.Scale(bounds.extents,new Vector3((i&1)==0?-1:1,(i&2)==0?-1:1,(i&4)==0?-1:1));
                    p=plate.transform.worldToLocalMatrix.MultiplyPoint3x4(renderer.transform.localToWorldMatrix.MultiplyPoint3x4(p));
                    corners.Add(new(p.x,p.y,p.z));
                }
                long count=0;for(int sub=0;sub<mesh.subMeshCount;sub++)count+=mesh.GetIndexCount(sub)/3;
                covered=count<=int.MaxValue&&PlateOverlayGeometry.Covers(data.Vertices,data.Triangles.Length/3,corners.ToArray(),(int)count);
            }
            if(!covered)
            {
                if(coverageWarnings.Add(plate.Pointer))Plugin.ModLog.LogWarning($"[Plate visual] Plate #{plate.VUID.Value}: overlay coverage not proven for native renderer slot {slot}; native retained.");
                continue;
            }
            hidden.Hide(plate.Pointer.ToInt64(),new NativePlateRenderer(plate,slot,renderer));any=true;
        }
        return any;
    }
    private static Material Make()
    {
        foreach(var name in OverlayMaterialPlan.Candidates)
        {
            var shader=Shader.Find(name);if(shader==null||!shader.isSupported)continue;
            var candidate=new Material(shader){hideFlags=HideFlags.HideAndDontSave};
            if(OverlayMaterialPlan.TextureInput(name,candidate.HasProperty)!=null)return candidate;
            Object.DestroyImmediate(candidate);
        }
        throw new InvalidOperationException("No supported overlay shader with a verified texture input.");
    }
    private static Material Strip(CassettePreset? preset,OverlayResources<Object> resources)
    {
        var raster=CassetteDepthStrip.Build(preset);
        var texture=resources.Own(new Texture2D(1,CassetteDepthStrip.Height,TextureFormat.RGBA32,false)
        {hideFlags=HideFlags.HideAndDontSave,wrapMode=TextureWrapMode.Clamp,filterMode=FilterMode.Point});
        var colours=raster.Select(c=>new Color32((byte)(c>>24),(byte)(c>>16),(byte)(c>>8),(byte)c)).ToArray();
        ((Texture2D)texture).SetPixels32(colours);((Texture2D)texture).Apply(false,true);
        var material=(Material)resources.Own(Make());
        var input=OverlayMaterialPlan.TextureInput(material.shader.name,material.HasProperty)!;
        var plan=new OverlayMaterialPlan(material.shader.name,input,opacity.Value,smoothness.Value);
        var set=new List<string>();
        foreach(var (name,value) in plan.Floats())if(material.HasProperty(name)){material.SetFloat(name,value);set.Add(name+"="+value.ToString(System.Globalization.CultureInfo.InvariantCulture));}
        var colour=new Color(1,1,1,opacity.Value);
        foreach(var name in new[]{"_BaseColor","_UnlitColor","_Color"})if(material.HasProperty(name)){material.SetColor(name,colour);set.Add(name+"=white/alpha:"+opacity.Value);}
        material.SetTexture(input,(Texture2D)texture);material.SetTextureScale(input,Vector2.one);material.SetTextureOffset(input,Vector2.zero);
        if(material.HasProperty("_UVMappingMask")){material.SetVector("_UVMappingMask",new Vector4(1,0,0,0));set.Add("_UVMappingMask=(1,0,0,0)");}
        if(material.HasProperty("_DoubleSidedConstants"))material.SetVector("_DoubleSidedConstants",new Vector4(1,1,-1,0));
        // new Material does not run the editor inspector. Use the installed runtime
        // API after assigning ALL properties/textures to compute valid HDRP state.
        bool hdrp=plan.Shader.StartsWith("HDRP/",StringComparison.Ordinal);
        if(hdrp&&!UnityEngine.Rendering.HighDefinition.HDMaterial.ValidateMaterial(material))
            throw new InvalidOperationException("HDRP material validation failed.");
        if(!hdrp)
        {
            if(plan.Transparent){material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");material.EnableKeyword("_ALPHABLEND_ON");}
            else{material.DisableKeyword("_SURFACE_TYPE_TRANSPARENT");material.DisableKeyword("_ALPHABLEND_ON");}
        }
        material.SetOverrideTag("RenderType",plan.Transparent?"Transparent":"Opaque");material.renderQueue=plan.Queue;
        if(!materialLogged)
        {
            materialLogged=true;
            if(plan.Shader.Contains("Unlit")){shaderFallbackLogged=true;set.Add("darker unlit fallback");}
            Plugin.ModLog.LogInfo("[Plate visual] shader="+plan.Shader+"; properties="+string.Join(",",set)+"; "+input+"=1x"+CassetteDepthStrip.Height+" point/clamp, identity ST; mesh.uv (UV0)=(0.5,d); HDRP validated="+hdrp+"; keywords="+string.Join(",",material.shaderKeywords.ToArray())+"; fallback="+shaderFallbackLogged);
        }
        return material;
    }
    private static MeshRenderer Add(GameObject root,OverlayCopy data,Material material,OverlayResources<Object> resources,out int degenerate)
    {
        var mesh=(Mesh)resources.Own(new Mesh{name="plate-visual-native-copy",hideFlags=HideFlags.HideAndDontSave,indexFormat=IndexFormat.UInt32});
        var scale=root.transform.localScale;
        var scale3=new System.Numerics.Vector3(scale.x,scale.y,scale.z);
        var normals=OverlayNormalOffset.Repair(data.Normals,data.Triangles,scale3,out degenerate); // 0.22.74: the same array when no normal is degenerate
        var inflated=OverlayNormalOffset.Apply(data.Vertices,normals,scale3);
        mesh.vertices=inflated.Select(v=>new Vector3(v.X,v.Y,v.Z)).ToArray();
        mesh.normals=normals.Select(v=>new Vector3(v.X,v.Y,v.Z)).ToArray();
        mesh.uv=data.Uv.Select(v=>new Vector2(v.X,v.Y)).ToArray();mesh.triangles=data.Triangles;mesh.RecalculateBounds();
        root.AddComponent<MeshFilter>().sharedMesh=mesh;var renderer=root.AddComponent<MeshRenderer>();renderer.sharedMaterial=material;
        renderer.shadowCastingMode=ShadowCastingMode.Off;renderer.receiveShadows=true;return renderer;
    }
    private static void Suspend(IntPtr pointer)
    {
        hidden.Restore(pointer.ToInt64());
        if(!views.TryGetValue(pointer,out var view))return;
        if(view.Root!=null&&view.Root.activeSelf)view.Root.SetActive(false);
        // Do not change the dictionary while Follow iterates it.
    }
    private static void SuspendViews(){foreach(var key in views.Keys)Suspend(key);}
    private static string Appearance=>opacity.Value+"|"+smoothness.Value;
    private static void ApplyAppearance(Material material)
    {
        var input=OverlayMaterialPlan.TextureInput(material.shader.name,material.HasProperty)!;
        var plan=new OverlayMaterialPlan(material.shader.name,input,opacity.Value,smoothness.Value);
        foreach(var (name,value) in plan.Floats())if(material.HasProperty(name))material.SetFloat(name,value);
        foreach(var name in new[]{"_BaseColor","_UnlitColor","_Color"})if(material.HasProperty(name))material.SetColor(name,new Color(1,1,1,opacity.Value));
        material.SetOverrideTag("RenderType",plan.Transparent?"Transparent":"Opaque");material.renderQueue=plan.Queue;
        if(plan.Shader.StartsWith("HDRP/",StringComparison.Ordinal))UnityEngine.Rendering.HighDefinition.HDMaterial.ValidateMaterial(material);
        else if(plan.Transparent){material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");material.EnableKeyword("_ALPHABLEND_ON");}
        else{material.DisableKeyword("_SURFACE_TYPE_TRANSPARENT");material.DisableKeyword("_ALPHABLEND_ON");}
    }
    private static void Remove(IntPtr pointer)
    {
        hidden.Restore(pointer.ToInt64());
        signatures.Remove(pointer);
        if(!views.Remove(pointer,out var old))return;
        try{if(old.Root!=null)old.Root.SetActive(false);}catch(Exception ex){Plugin.ModLog.LogWarning("[Plate visual] Deactivate: "+ex.Message);}
        try{old.Resources.Clear();}catch(Exception ex){Plugin.ModLog.LogWarning("[Plate visual] Cleanup: "+ex.Message);}
    }
    [HarmonyPrefix,HarmonyPatch(typeof(PlateStructure),nameof(PlateStructure.Release))]
    private static void Release(PlateStructure __instance){RuntimeMaterialLooks.Release(__instance);pending.Remove(__instance.Pointer.ToInt64());plates.Remove(__instance.Pointer);retries.Remove(__instance.Pointer);Remove(__instance.Pointer);PlateMeshAccess.Forget(__instance);}
    private static void ClearViews()
    {
        foreach(var p in views.Keys.ToArray())Remove(p);hidden.RestoreAll();retries.Clear();
    }
    internal static void Clear(){RuntimeMaterialLooks.Clear();ClearViews();pending.Clear();plates.Clear();gone.Clear();trackedVehicle=IntPtr.Zero;wasDesigner=false;lifecycle.Reset();sceneRoots=Array.Empty<GameObject>();controlRoots=Array.Empty<GameObject>();rootScene=0;controlScene=0;sceneFrame=-1;activeEditor=null;inspectorEditor=null;CassetteVisualKeys.Clear();}
}
