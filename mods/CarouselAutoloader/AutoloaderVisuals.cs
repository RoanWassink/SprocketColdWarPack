using HarmonyLib;
using Il2CppInterop.Runtime;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using Sprocket.Vehicles;
using Sprocket.Vehicles.AmmunitionStorage;
using Sprocket.Vehicles.Cannons;
using Sprocket.Vehicles.Weapons;
using Sprocket.WeaponFramework;
using UnityEngine;
using Object = UnityEngine.Object;

namespace SprocketCarouselAutoloader;

// Optional visual adapter. No ammunition, loading, assignment, collider or mass writes.
// 0.2.23: a static true-size prop. Two meshes per rack: the frame/drum/arm and the rounds.
// The round triangles are sorted by slot, so AmountStored only changes the length of one
// prebuilt index buffer per material: no transforms, no animation, no per-frame allocation.
internal static class AutoloaderVisuals
{
    private sealed class Visual
    {
        internal readonly AmmoRack Rack;
        internal readonly AmmoRackModel Native;
        internal readonly LoaderModel Model;
        internal readonly GameObject Root;
        internal readonly List<Mesh> Meshes = new();
        internal readonly List<Material> Materials = new();
        internal readonly List<GameObject> Children = new();
        internal readonly List<(Renderer Renderer, bool Enabled)> Suppressed = new();
        internal AmmoRackBehaviour? Stock;
        internal Il2CppSystem.EventHandler? StockChanged;
        internal bool Own, Restoring;
        internal int Layer = -1;
        internal Mesh? Rounds;
        internal Il2CppStructArray<int>[] RoundIndices = Array.Empty<Il2CppStructArray<int>>();
        internal int[][] Prefix = Array.Empty<int[]>();
        internal int Displayed = -1;
        internal Visual(AmmoRack rack, AmmoRackModel native, LoaderModel model, GameObject root)
        { Rack=rack; Native=native; Model=model; Root=root; }
    }

    private static readonly Dictionary<IntPtr, Visual> Models = new();
    private static readonly Dictionary<IntPtr, Visual> NativeModels = new();
    // Racks refused with a reason: retried by AmmoRack.Build, not by every renderer refresh.
    private static readonly HashSet<IntPtr> Refused = new();
    // Warnings are logged once per rack (vehicle design + VUID) and reason for the whole session,
    // also when the same design is spawned several times (Sandbox Targets copies).
    private static readonly HashSet<string> Logged = new();
    private static readonly HashSet<IntPtr> Building = new();
    internal static bool Owns(AmmoRack rack) => rack.VehicleObject?.GUID is
        BustleRuntime.PartGuid or BustleRuntime.RevolverPartGuid;

    private static void Warn(AmmoRack rack, string reason, string detail = "")
    {
        string design;
        try { design = rack.Vehicle?.DesignInfo?.Name ?? ""; } catch { design = ""; }
        if (Logged.Add($"{design}|{rack.VUID.Value}|{reason}"))
            Plugin.ModLog.LogWarning($"[Visuals] Rack {rack.VUID.Value}{(design.Length > 0 ? $" ('{design}')" : "")}: {reason}{detail}; " +
                "native ammunition grid retained, own rounds omitted. Logged once per rack this session.");
    }

    private static void Refuse(AmmoRack rack, string reason, string detail = "")
    { Refused.Add(rack.Pointer); Warn(rack, reason, detail); }

    internal static void Rebuild(AmmoRack rack)
    {
        if (!Owns(rack) || !Building.Add(rack.Pointer)) return;
        try
        {
            Remove(rack.Pointer);
            var native = rack.model?.TryCast<AmmoRackModel>();
            if (native?.material == null) return; // MaterialReady retries after asynchronous asset loading.
            var state = BustleRuntime.State(rack);
            Cannon? assigned = null;
            foreach (var cannon in CarouselRuntime.Cannons(rack))
                if (cannon.VUID.Value == state.CannonId) { assigned=cannon; break; }
            // Assigned guns always use the canonical API. An unassigned editor rack
            // has only its native size encoding until a cannon is selected.
            var shell = assigned == null ? new LoaderShellSize(0, rack.projectileSizeID.Caliber*3.0,
                rack.projectileSizeID.PropellantLength, rack.projectileSizeID.Caliber) : ShellLoaderGeometry.Read(assigned);
            int capacity = Math.Max(0, rack.behaviour?.Capacity ?? rack.Capacity);
            // Seen when a rack is built before its cannon/ammunition is known (vehicle spawn) and for
            // racks whose rounds do not fit (capacity 0). A later Build with valid data succeeds.
            if (!shell.Valid || capacity == 0)
            { Refuse(rack, "no valid shell geometry or empty capacity", $" (geometry status {shell.Status}, capacity {capacity})"); return; }
            if (!AutoloaderVisualPolicy.Supported(capacity)) { Refuse(rack, "own model exceeds visual budget", $" (capacity {capacity})"); return; }
            // 0.2.24: the same spec (bounds, pieces, load point) that places the gameplay feed point,
            // so the arm the player sees is where the game measures from.
            state.FeedValid = false;
            BustleRuntime.Feed(state, assigned);
            if (state.FeedSpec == null) { Refuse(rack, "no valid shell geometry or empty capacity", " (no model input)"); return; }
            var model = AutoloaderModelGeometry.Create(state.FeedSpec with { Capacity = capacity });
            if (!model.Complete) { Refuse(rack, "own model does not match the rack capacity", $" ({model.Slots} places, capacity {capacity})"); return; }
            if (!AutoloaderVisualPolicy.InBudget(model)) { Refuse(rack, "own model exceeds visual budget", $" ({model.Triangles} triangles)"); return; }
            var root = new GameObject("Autoloader true-size model");
            var visual = new Visual(rack,native,model,root);
            // Register immediately: partial creation failures are cleaned up by Remove.
            Models.Add(rack.Pointer,visual); NativeModels[native.Pointer]=visual;
            root.transform.SetParent(rack.transform,false);
            root.transform.localPosition=rack.boundsCollider?.Centre ?? Vector3.zero;
            root.SetActive(false);
            foreach (var color in new[] {new Color(43/255f,55/255f,57/255f,1),new Color(112/255f,134/255f,132/255f,1),
                new Color(186/255f,203/255f,192/255f,1),new Color(204/255f,167/255f,83/255f,1),new Color(147/255f,62/255f,52/255f,1)})
            {
                var material=new Material(native.material);
                visual.Materials.Add(material);
                if(material.HasProperty("_BaseColor")) material.SetColor("_BaseColor",color);
                if(material.HasProperty("_Color")) material.SetColor("_Color",color);
            }
            Build(visual,false);
            Build(visual,true);
            Bind(visual);
            Sync(visual);
            Refused.Remove(rack.Pointer);
            if (Plugin.DetailedLogging)
                Plugin.ModLog.LogDebug($"[Visuals] Rebuilt {model.Spec.Name} rack {rack.VUID.Value}: {model.Triangles} triangles ({model.Detail}), " +
                    $"{model.Slots} true-size rounds {model.DiameterM*1000:0} x {model.LengthM*1000:0} mm, arm beyond +z {model.ArmProtrusionM*1000:0} mm.");
        }
        catch (Exception ex)
        {
            Remove(rack.Pointer);
            Refuse(rack,"visual rebuild failed",": "+ex.Message);
        }
        finally { Building.Remove(rack.Pointer); }
    }

    private static GameObject Child(Visual visual, string name, Mesh mesh)
    {
        var child=new GameObject(name);
        visual.Children.Add(child);
        child.transform.SetParent(visual.Root.transform,false);
        child.AddComponent<MeshFilter>().sharedMesh=mesh;
        return child;
    }

    // One mesh with a submesh per material: the static frame (rounds=false), or all rounds
    // ordered by slot (rounds=true) with a visible prefix per material and stock level.
    private static void Build(Visual visual, bool rounds)
    {
        var parts=visual.Model.Meshes.Where(p => (p.Role==PartRole.Round)==rounds).OrderBy(p => rounds ? p.Slot : 0).ToList();
        int materials=visual.Materials.Count;
        var vertices=new List<Vector3>(); var normals=new List<Vector3>();
        var indices=new List<int>[materials];
        for(int slot=0;slot<materials;slot++) indices[slot]=new List<int>();
        foreach(var part in parts)
        {
            int first=vertices.Count;
            for(int i=0;i<part.Vertices.Count;i+=3)
            {
                vertices.Add(new(part.Vertices[i],part.Vertices[i+1],part.Vertices[i+2]));
                normals.Add(new(part.Normals[i],part.Normals[i+1],part.Normals[i+2]));
            }
            for(int tri=0;tri<part.Triangles;tri++)
                for(int k=0;k<3;k++) indices[part.Materials[tri]].Add(first+part.Indices[tri*3+k]);
        }
        if(vertices.Count==0) return;
        var mesh=new Mesh {name=rounds ? "True-size rounds" : "Frame, drum and feed arm"};
        visual.Meshes.Add(mesh);
        if(vertices.Count>65000) mesh.indexFormat=UnityEngine.Rendering.IndexFormat.UInt32;
        mesh.vertices=new Il2CppStructArray<Vector3>(vertices.ToArray());
        mesh.normals=new Il2CppStructArray<Vector3>(normals.ToArray());
        mesh.subMeshCount=materials;
        var buffers=new Il2CppStructArray<int>[materials];
        for(int slot=0;slot<materials;slot++)
        {
            buffers[slot]=new Il2CppStructArray<int>(indices[slot].ToArray());
            mesh.SetTriangles(buffers[slot],0,rounds ? 0 : buffers[slot].Length,slot,false,0);
        }
        // Bounds include every round, also hidden ones; no recalculation on stock changes.
        mesh.RecalculateBounds();
        var child=Child(visual,mesh.name,mesh);
        child.AddComponent<MeshRenderer>().sharedMaterials=new Il2CppReferenceArray<Material>(visual.Materials.ToArray());
        if(!rounds) return;
        visual.Rounds=mesh;
        visual.RoundIndices=buffers;
        visual.Prefix=new int[materials][];
        for(int slot=0;slot<materials;slot++)
            visual.Prefix[slot]=AutoloaderVisualPolicy.Prefix(parts,slot,visual.Model.Spec.Capacity);
        visual.Displayed=-1;
    }

    private static void Bind(Visual visual)
    {
        var stock=visual.Rack.behaviour;
        if(visual.Stock?.Pointer==stock?.Pointer) return;
        Unbind(visual);
        visual.Stock=stock;
        if(stock==null) return;
        visual.StockChanged=DelegateSupport.ConvertDelegate<Il2CppSystem.EventHandler>(
            (Action<Il2CppSystem.Object,Il2CppSystem.EventArgs>)((_,_)=>
            {
                try { Update(visual); }
                catch(Exception ex) { Warn(visual.Rack,"stock display failed",": "+ex.Message); }
            }));
        stock.add_StorageValueChanged(visual.StockChanged);
    }

    private static void Unbind(Visual visual)
    {
        if(visual.Stock!=null && visual.StockChanged!=null)
        {
            try { visual.Stock.remove_StorageValueChanged(visual.StockChanged); }
            catch { /* The native behaviour may already have been released. */ }
        }
        visual.Stock=null; visual.StockChanged=null;
    }

    // Shows exactly AmountStored rounds (none while native shells are shown). Only a changed stock
    // touches the mesh, and then only the index-buffer lengths.
    private static void Update(Visual visual)
    {
        if(visual.Root==null || !visual.Root.activeInHierarchy || visual.Rounds==null) return;
        int capacity=visual.Model.Spec.Capacity;
        int stored=AutoloaderVisualPolicy.Stock(capacity,visual.Rack.behaviour?.AmountStored ?? capacity);
        int state=visual.Own ? stored : 0;
        if(state==visual.Displayed) return;
        for(int slot=0;slot<visual.RoundIndices.Length;slot++)
            visual.Rounds.SetTriangles(visual.RoundIndices[slot],0,visual.Prefix[slot][state]*3,slot,false,0);
        visual.Displayed=state;
    }

    // Called from MagazineRefill's existing LoadTask.Update tick: an integer comparison per own
    // rack, no transforms and no allocation. Rebinds when the native stock behaviour is replaced.
    internal static void Tick(Cannon cannon, LoadTask task)
    {
        foreach(var state in VehicleLoadIndex.Get(cannon).Bustles)
        {
            if(state.Semi || state.CannonId!=cannon.VUID.Value) continue;
            AutoloaderHudFeed.Sample(state,cannon,task); // at most 10x per second, also without an own model
            if(!Models.TryGetValue(state.Rack.Pointer,out var visual)) continue;
            if(visual.Root==null || !visual.Root.activeInHierarchy) continue;
            try { Bind(visual); Update(visual); }
            catch(Exception ex) { Warn(state.Rack,"stock display failed",": "+ex.Message); }
        }
    }

    private static void Remember(Visual visual, Renderer renderer)
    {
        if(renderer==null) throw new InvalidOperationException("shell renderer not found");
        foreach(var entry in visual.Suppressed)
            if(entry.Renderer!=null && entry.Renderer.Pointer==renderer.Pointer) return;
        visual.Suppressed.Add((renderer,renderer.enabled));
    }

    private static void Suppress(Visual visual)
    {
        // Only the exact rack model's ShellModels and its low-definition model.
        // Never enumerate renderers under a vehicle, another rack or our mechanism.
        var native=visual.Native;
        int found=0;
        var shells=native.models;
        if(shells!=null)
            for(int i=0;i<shells.Count;i++)
            {
                var shell=shells[i];
                if(shell==null) throw new InvalidOperationException("shell model not found");
                Remember(visual,shell.ProjectileRenderer); found++;
                Remember(visual,shell.PropellantRenderer); found++;
            }
        // 0.2.22 Play log: a low-definition model without renderers aborted the own rounds
        // ("low-definition renderer not found"). Nothing to hide there is not an error.
        var root=native.lowDefinitionModel?.transform;
        if(root!=null)
            foreach(var renderer in root.GetComponentsInChildren<Renderer>(true)) { Remember(visual,renderer); found++; }
        if(found==0) throw new InvalidOperationException("native shell renderers not found");
        foreach(var entry in visual.Suppressed)
            if(entry.Renderer!=null) entry.Renderer.enabled=false;
    }

    private static void Restore(Visual visual, bool refresh)
    {
        visual.Restoring=true;
        try
        {
            foreach(var entry in visual.Suppressed)
                if(entry.Renderer!=null) entry.Renderer.enabled=entry.Enabled;
            visual.Suppressed.Clear();
            if(refresh && visual.Native!=null)
            {
                // Native code chooses the active LOD and visibility, not a blanket enabled=true.
                var native=visual.Native;
                native.ApplyRenderers(native.targetLOD,native.propellantLength,native.projectileLength);
                native.ApplyRendererSettings();
                native.SetOptimizedModeActive(native.optimizedMode);
            }
        }
        finally { visual.Restoring=false; }
    }

    private static void Sync(Visual visual)
    {
        if(visual.Restoring || visual.Root==null) return;
        int layer=visual.Native.modelTransform!=null ? visual.Native.modelTransform.gameObject.layer : visual.Rack.gameObject.layer;
        if(layer!=visual.Layer)
        {
            visual.Root.layer=layer;
            foreach(var child in visual.Children) child.layer=layer;
            visual.Layer=layer;
        }
        bool visible=visual.Native.Visible;
        if(visual.Root.activeSelf!=visible) visual.Root.SetActive(visible);
        visual.Own=false;
        try
        {
            if(Plugin.OwnRounds) { Suppress(visual); visual.Own=true; }
            else { Restore(visual,true); Warn(visual.Rack,"Visuals.OwnRounds is false"); }
        }
        catch(Exception ex)
        {
            try { Restore(visual,true); } catch { /* Preserve the original warning cause. */ }
            Warn(visual.Rack,"native shell suppression unavailable",": "+ex.Message);
        }
        Bind(visual);
        visual.Displayed=-1;
        Update(visual);
    }

    // 0.2.28 (M6): one postfix per AmmoRackModel target instead of five. ApplyRenderers and ApplyRendererSettings
    // carried a BustleVisuals and an AutoloaderVisuals postfix (BustleVisuals registered first); SetOptimizedModeActive
    // only this one. Same work, same order; each part keeps its own guard.
    [HarmonyPostfix, HarmonyPatch(typeof(AmmoRackModel),nameof(AmmoRackModel.ApplyRenderers),new[]{typeof(int),typeof(float),typeof(float)})]
    [HarmonyPatch(typeof(AmmoRackModel),nameof(AmmoRackModel.ApplyRendererSettings),new Type[]{})]
    private static void RenderersApplied(AmmoRackModel __instance)
    {
        try { BustleVisuals.MaterialReady(__instance); }
        catch (Exception ex) { HookGuard.Warn("AmmoRackModel renderers (bustle frame)", -1, ex); }
        MaterialReady(__instance);
    }
    [HarmonyPostfix, HarmonyPatch(typeof(AmmoRackModel),nameof(AmmoRackModel.SetOptimizedModeActive),new[]{typeof(bool)})]
    private static void OptimizedModeChanged(AmmoRackModel __instance) => MaterialReady(__instance);

    private static void MaterialReady(AmmoRackModel __instance)
    {
        try
        {
            if(NativeModels.TryGetValue(__instance.Pointer,out var visual)) { Sync(visual); return; }
            var parts=__instance.VehicleObject?.Components;
            if(parts==null) return;
            int count=parts.Cast<Il2CppSystem.Collections.Generic.IReadOnlyCollection<VehicleComponent>>().Count;
            for(int i=0;i<count;i++)
            {
                var rack=parts[i].TryCast<AmmoRack>();
                // A rack refused with a reason is retried by AmmoRack.Build, not on every renderer refresh.
                if(rack!=null && Owns(rack) && rack.model?.Pointer==__instance.Pointer && !Refused.Contains(rack.Pointer)) Rebuild(rack);
            }
        }
        catch(Exception ex)
        {
            if(NativeModels.TryGetValue(__instance.Pointer,out var visual))
            { visual.Own=false; try { Restore(visual,true); visual.Displayed=-1; Update(visual); } catch { } Warn(visual.Rack,"renderer refresh failed",": "+ex.Message); }
        }
    }

    internal static void Remove(IntPtr pointer)
    {
        if(!Models.Remove(pointer,out var visual)) return;
        NativeModels.Remove(visual.Native.Pointer);
        Unbind(visual);
        try { Restore(visual,false); } catch { }
        if(visual.Root!=null) { visual.Root.SetActive(false); Object.Destroy(visual.Root); }
        foreach(var mesh in visual.Meshes) if(mesh!=null) Object.Destroy(mesh);
        foreach(var material in visual.Materials) if(material!=null) Object.Destroy(material);
    }

    internal static void Release(IntPtr pointer) { Remove(pointer); Refused.Remove(pointer); }
}
