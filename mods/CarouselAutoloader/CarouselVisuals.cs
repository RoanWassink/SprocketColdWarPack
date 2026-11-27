using HarmonyLib;
using Il2CppInterop.Runtime;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using Sprocket.TurretBaskets;
using Sprocket.Vehicles;
using Sprocket.Vehicles.Turrets;
using Sprocket.Vehicles.AmmunitionStorage;
using Sprocket.Vehicles.Weapons;
using UnityEngine;
using Object = UnityEngine.Object;

namespace SprocketCarouselAutoloader;

// Optional rendering only. Native geometry, collision, ammo and load tasks remain owned by Sprocket.
internal static class CarouselVisuals
{
    private sealed class Visual
    {
        internal readonly TurretBasket Basket;
        internal readonly BasketGenerator Generator;
        internal readonly GameObject Root;
        internal readonly List<Mesh> Meshes = new();
        internal readonly List<Material> Materials = new();
        internal readonly List<Renderer> Native = new();
        // 0.2.31: cassettes and rounds hang under this transform; a stock change turns it (the lift and the mechanism stay).
        internal GameObject? Ring;
        internal int LiftSlot;
        internal int RingPitchSlots;
        internal bool Stacked;
        // 0.2.33: the ring parts (our cassette/projectile/charge objects and meshes) and where they are in Play.
        internal readonly List<GameObject> RingObjects = new();
        internal readonly List<IntPtr> RingMeshes = new();
        internal RingPart[] Parts = Array.Empty<RingPart>();
        internal CarouselRingPartPath Path = CarouselRingPartPath.UnderRing;
        internal int Clones;
        internal double PartsAngle;
        internal bool BakedNoted;
        // 0.2.34: for the proof line.
        internal string CapacityReason = "";
        internal CarouselModelLevel Level;
        internal double LiftTop, BasketTop;
        internal CarouselLiftTargetSource TargetSource = CarouselLiftTargetSource.None;
        // 0.2.32: smooth turn state (only used while Turning): angles in radians, times from Time.unscaledTime.
        internal bool Turning;
        internal double Angle, TurnFrom, TurnTo, TurnStart, LastTick;
        internal readonly List<(Mesh Mesh, int[] Triangles, int[] Prefix)> Ammo = new();
        internal AmmoRackBehaviour? Rack;
        internal Il2CppSystem.EventHandler? StorageChanged;
        internal int Displayed = -1, Capacity;
        internal Visual(TurretBasket basket, BasketGenerator generator, GameObject root)
        { Basket = basket; Generator = generator; Root = root; }
    }
    /// <summary>0.2.33: one ring part found by mesh, with its pose in the Root frame (position, rotation relative to Root) at ring angle 0.</summary>
    internal sealed class RingPart
    {
        internal Transform T = null!;
        internal Vector3 Pos;
        internal Quaternion Rot;
    }
    // 0.2.33: the last valid lift target per basket (VUID); plain data, no hook.
    private static readonly Dictionary<long, CarouselLiftTarget> LastTargets = new();
    private static readonly Dictionary<IntPtr, Visual> Models = new();
    private static readonly Dictionary<IntPtr, Visual> Generators = new();
    // 0.2.29: one Info line per basket (VUID) and model level when the detailed model is not used.
    private static readonly HashSet<string> ModelNotes = new();
    // 0.2.32: rings that are turning right now. The list is reused (no allocation per frame); Tick is called from the existing
    // LoadTask.Update postfix and returns at once when nothing turns.
    private static readonly List<Visual> Turns = new();

    private static void Guard(TurretBasket basket, Action work)
    {
        try { work(); }
        catch (Exception ex)
        {
            Remove(basket.Pointer);
            Plugin.ModLog.LogWarning($"[Carousel visual] Native basket restored after optional visual failure: {ex.Message}");
        }
    }

    /// <summary>0.2.28: true once the carousel-visuals group is patched; the merged core hooks call this class only then.</summary>
    internal static bool Available;

    // 0.2.28 (M3): part of the merged TurretBasket.Build postfix (CarouselRuntime.BasketBuilt), second as before.
    internal static void Built(TurretBasket __instance) => Guard(__instance, () => Build(__instance));
    [HarmonyPostfix, HarmonyPatch(typeof(TurretBasket), nameof(TurretBasket.ApplyAssets))]
    private static void AssetsReady(TurretBasket __instance) => Guard(__instance, () => Build(__instance));

    private static void Build(TurretBasket basket)
    {
        Remove(basket.Pointer);
        var state = CarouselRuntime.State(basket);
        if (!state.Active) return;
        var generator = basket.generator?.TryCast<BasketGenerator>();
        if (generator?.transform == null || generator.SegmentCount == 0) return;
        var native = new List<Renderer>();
        for (int i = 0; i < generator.SegmentCount; i++)
        {
            var segment = generator.GetSegment(i);
            var floor = segment.FloorModel?.gameObject;
            var wall = segment.WallModel?.gameObject;
            foreach (var model in new[] { floor, wall })
                if (model != null)
                    foreach (var renderer in model.GetComponentsInChildren<Renderer>(true)) native.Add(renderer);
        }
        var original = native.Select(r => r.sharedMaterial).FirstOrDefault(m => m != null);
        if (original == null) return; // Assets can arrive asynchronously; ApplyAssets will retry.
        var cannon = CarouselRuntime.Cannons(basket).FirstOrDefault(c => c.VUID.Value == state.CannonId);
        var bounds = generator.LocalBounds;
        var scale = basket.Scale;
        if (Math.Abs(scale.x) < .0001f || Math.Abs(scale.y) < .0001f || Math.Abs(scale.z) < .0001f) return;
        CarouselSize size;
        double caliber;
        if (cannon != null)
        {
            size = CarouselRuntime.Calculate(basket, cannon);
            caliber = cannon.ShellBlueprint.Diameter / 1000.0;
        }
        else
        {
            var bp = basket.BlueprintSlot.Blueprint;
            var segments = bp.Segments.Select(s => new Segment(s.Diameter, s.Depth));
            var resolved = CarouselGeometry.ResolveBasket(segments, basket.ringDiameter);
            var diameter = resolved.Min(s => s.DiameterMm) * Math.Min(Math.Abs(scale.x), Math.Abs(scale.z));
            var depth = resolved.Sum(s => s.DepthMm) * Math.Abs(scale.y);
            size = new(0,0,diameter,depth,0,0,"", StoredProjectileMm:375, StoredChargeMm:408);
            caliber = .125;
        }
        if (size.DiameterMm <= 0 || size.DepthMm <= 0) return;
        // 0.2.30: the load point of the assigned cannon in the model frame (read once, here; nothing per frame): the lift stands behind it.
        // Same pattern as the bustle feed arm: cannon.transform.TransformPoint(cannon.LoadLocalPosition), then into the generator frame; the
        // model root sits at (bounds.center.x, bounds.min.y, bounds.center.z) with scale 1/scale, so model metres = (local - root) * |scale|.
        CarouselLiftTarget? liftTarget = null;
        if (cannon != null && CarouselModelGeometry.Lift)
        {
            try
            {
                var local = generator.transform.InverseTransformPoint(cannon.transform.TransformPoint(cannon.LoadLocalPosition));
                var point = new CarouselLiftTarget((local.x - bounds.center.x) * Math.Abs(scale.x), (local.y - bounds.min.y) * Math.Abs(scale.y),
                    (local.z - bounds.center.z) * Math.Abs(scale.z));
                if (double.IsFinite(point.X) && double.IsFinite(point.Y) && double.IsFinite(point.Z) && Math.Sqrt(point.X * point.X + point.Y * point.Y + point.Z * point.Z) < 50)
                    liftTarget = point;
            }
            catch { liftTarget = null; } // invalid transform: the lift stays at local -Z
        }
        // 0.2.33: a rebuild at the start of Play can see an invalid load point; then the last valid target of this basket is used.
        long targetKey = basket.VUID.Value;
        CarouselLiftTarget? cachedTarget = LastTargets.TryGetValue(targetKey, out var lastTarget) ? lastTarget : null;
        liftTarget = CarouselModelGeometry.ChooseLiftTarget(liftTarget, cachedTarget, CarouselModelGeometry.Lift && state.CannonId >= 0, out var targetSource);
        if (targetSource == CarouselLiftTargetSource.Fresh) LastTargets[targetKey] = liftTarget!.Value;
        else if (targetSource == CarouselLiftTargetSource.Cached && Plugin.DetailedLogging)
            Plugin.ModLog.LogInfo($"[Carousel visual] Basket {basket.VUID.Value}: lift target not valid now, the last valid target is used.");
        // 0.2.29: the approved T-72/T-80 models ([Visuals] CarouselModel); 0.3.5: 2S38-style stacked unitary procedural model.
        bool stacked = state.Layout == CarouselLayout.StackedUnitary;
        IReadOnlyList<CarouselVisualMesh> geometry;
        CarouselModelChoice choice;
        if (stacked)
        {
            geometry = StackedCarouselGeometry.Build(size.DiameterMm / 1000, size.DepthMm / 1000,
                size.Capacity, size.SlotsPerTier, caliber, Math.Max(.001, size.StoredProjectileMm / 1000),
                target: liftTarget, showLift: CarouselModelGeometry.Lift);
            choice = new(CarouselModelLevel.Simple, "2S38", "", CarouselModelGeometry.Triangles(geometry));
        }
        else
        {
            geometry = CarouselModelGeometry.Build(size.DiameterMm / 1000, size.DepthMm / 1000,
                size.Capacity, caliber, Math.Max(.001,size.StoredProjectileMm/1000),
                Math.Max(0,size.StoredChargeMm/1000), state.Layout == CarouselLayout.VerticalCharge, out choice, target: liftTarget);
        }
        if (choice.Info is { } info && ModelNotes.Add($"{basket.VUID.Value}|{choice.Level}"))
            Plugin.ModLog.LogInfo($"[Carousel visual] Basket {basket.VUID.Value}: {info}.");
        var root = new GameObject("Carousel autoloader visual");
        root.transform.SetParent(generator.transform, false);
        root.transform.localPosition = new(bounds.center.x, bounds.min.y, bounds.center.z);
        root.transform.localScale = new(1/Math.Abs(scale.x),1/Math.Abs(scale.y),1/Math.Abs(scale.z));
        var ring = new GameObject("Carousel ring");
        ring.transform.SetParent(root.transform, false);
        int ringPitch = stacked && size.SlotsPerTier > 0 ? size.SlotsPerTier : size.Capacity;
        var visual = new Visual(basket, generator, root) { Capacity = size.Capacity, Ring = ring, Stacked = stacked, RingPitchSlots = ringPitch,
            LiftSlot = CarouselModelGeometry.LiftSlotFor(ringPitch, liftTarget), TargetSource = targetSource,
            CapacityReason = size.Reason ?? "", Level = choice.Level, BasketTop = size.DepthMm / 1000.0,
            LiftTop = geometry.Where(m => m.Material == "Mechanism").SelectMany(m => m.Vertices).Select(v => v.Y).DefaultIfEmpty(0).Max() };
        Models[basket.Pointer] = visual;
        Generators[generator.Pointer] = visual;
        visual.Native.AddRange(native);
        foreach (var data in geometry)
        {
            var child = new GameObject(data.Name);
            child.transform.SetParent((CarouselModelGeometry.RotatesWithRing(data.Material) ? ring : root).transform, false);
            var mesh = new Mesh { name = "Carousel " + data.Name };
            visual.Meshes.Add(mesh);
            if (CarouselModelGeometry.RotatesWithRing(data.Material)) { visual.RingObjects.Add(child); visual.RingMeshes.Add(mesh.Pointer); }
            var vertices = data.Vertices.Select(p => new Vector3((float)p.X,(float)p.Y,(float)p.Z)).ToArray();
            var normals = data.Normals.Select(p => new Vector3((float)p.X,(float)p.Y,(float)p.Z)).ToArray();
            mesh.vertices = new Il2CppStructArray<Vector3>(vertices);
            mesh.normals = new Il2CppStructArray<Vector3>(normals);
            mesh.triangles = new Il2CppStructArray<int>(data.Triangles.ToArray());
            mesh.RecalculateBounds();
            var material = new Material(original);
            visual.Materials.Add(material);
            var color = data.Material switch
            {
                "Projectile" => new Color(.40f,.43f,.33f,1),
                "Charge" => new Color(.49f,.38f,.22f,1),
                "DriveTrim" => new Color(.30f,.32f,.33f,1),
                "Cassette" => new Color(.19f,.23f,.18f,1),
                _ => new Color(.10f,.13f,.10f,1)
            };
            if (material.HasProperty("_BaseColor")) material.SetColor("_BaseColor",color);
            if (material.HasProperty("_Color")) material.SetColor("_Color",color);
            child.AddComponent<MeshFilter>().sharedMesh = mesh;
            child.AddComponent<MeshRenderer>().sharedMaterial = material;
            if (data.SlotPrefixIndexCounts.Length > 0)
                visual.Ammo.Add((mesh,data.Triangles.ToArray(),data.SlotPrefixIndexCounts));
        }
        Sync(visual);
        Bind(visual,state.Rack);
    }

    private static void Sync(Visual visual)
    {
        if (visual.Root == null) return;
        visual.Root.SetActive(visual.Generator.visible);
        visual.Root.layer = visual.Generator.layer;
        foreach (var mesh in visual.Root.GetComponentsInChildren<Renderer>(true)) mesh.gameObject.layer = visual.Generator.layer;
        foreach (var renderer in visual.Native) if (renderer != null) renderer.enabled = false;
        if (!visual.Turning && visual.Ring != null && visual.Displayed >= 0) SetAngle(visual, visual.Angle); // re-assert the ring angle
    }

    private static void Bind(Visual visual, AmmoRackBehaviour? rack)
    {
        if (visual.Rack?.Pointer != rack?.Pointer)
        {
            if (visual.Rack != null && visual.StorageChanged != null)
                visual.Rack.remove_StorageValueChanged(visual.StorageChanged);
            visual.Rack = rack;
            visual.Displayed = -1;
            visual.StorageChanged = null;
            if (rack != null)
            {
                visual.StorageChanged = DelegateSupport.ConvertDelegate<Il2CppSystem.EventHandler>(
                    (Action<Il2CppSystem.Object,Il2CppSystem.EventArgs>)((_,_) => Guard(visual.Basket, () => UpdateStock(visual))));
                rack.add_StorageValueChanged(visual.StorageChanged);
            }
        }
        UpdateStock(visual);
    }
    private static void UpdateStock(Visual visual)
    {
        var remaining=visual.Basket.HealthFraction<=0 ? 0 : visual.Rack==null ? visual.Capacity : Math.Clamp((int)visual.Rack.AmountStored,0,visual.Capacity);
        if(remaining==visual.Displayed) return;
        // 0.2.33: find the ring parts at the first change after a (re)binding, and again when the set no longer fits (not per frame).
        if(visual.Displayed<0 || !PartsValid(visual)) ResolveRingParts(visual);
        // 0.2.31: the ring turns one slot per shot (a jump, no animation, only when the native stock changes): the full slots are those from
        // slot (L + fired) round the ring, the ring transform is turned by the same amount so that full slot stands under the lift.
        int n=visual.Capacity;
        int pitch=visual.RingPitchSlots>0 ? visual.RingPitchSlots : n;
        var ranges=visual.Stacked
            ? StackedCarouselGeometry.VisibleRanges(n,pitch,visual.LiftSlot,remaining)
            : CarouselModelGeometry.RingVisibleRanges(n,visual.LiftSlot,remaining);
        foreach(var group in visual.Ammo)
        {
            if(group.Prefix.Length<=n){ group.Mesh.triangles=new Il2CppStructArray<int>(group.Triangles.Take(group.Prefix[Math.Min(remaining,group.Prefix.Length-1)]).ToArray()); continue; }
            var list=new List<int>();
            foreach(var (start,count) in ranges)
                list.AddRange(group.Triangles.Skip(group.Prefix[start]).Take(group.Prefix[start+count]-group.Prefix[start]));
            group.Mesh.triangles=new Il2CppStructArray<int>(list.ToArray());
        }
        // 0.2.32: a stock change while a turn runs sends that ring straight to its end angle first (it never stays half way).
        FinishTurn(visual);
        int fired=CarouselModelGeometry.RingFired(n,remaining);
        double angle=pitch<=0 ? 0 : -Math.Max(0,fired-1)*2*Math.PI/pitch;
        double seconds=CarouselModelGeometry.TurnSeconds;
        bool smooth=visual.Ring!=null && visual.Displayed>=0 && CarouselModelGeometry.IsSingleShot(n,visual.Displayed,remaining,seconds) && Math.Abs(angle-visual.Angle)>1e-9;
        int previous=visual.Displayed;
        visual.Displayed=remaining;
        if(smooth)
        {
            // From wherever the ring stands (the old end angle) to the new one: one pitch, or nothing for the first shot (f - 1 = 0).
            visual.TurnFrom=visual.Angle; visual.TurnTo=angle; visual.TurnStart=Time.unscaledTime; visual.LastTick=visual.TurnStart; visual.Turning=true;
            Turns.Add(visual);
        }
        else SetAngle(visual,angle);
        if(Plugin.DetailedLogging) LogStock(visual,remaining,previous,smooth);
    }
    private static void SetAngle(Visual visual, double radians)
    {
        visual.Angle=radians;
        if(visual.Path==CarouselRingPartPath.Moved) { ApplyParts(visual,radians); return; }
        if(visual.Ring!=null) visual.Ring.transform.localRotation=Quaternion.Euler(0f,(float)(radians*180/Math.PI),0f);
    }
    // 0.2.33: parts the game moved or cloned: each is set to its Root-frame base pose turned about the Y axis of Root through the origin of Root,
    // which is what the Ring transform would give. The Root scale (1/scale) is in TransformPoint; rotation is Root.rotation * yaw * base.
    private static void ApplyParts(Visual visual, double radians)
    {
        var root=visual.Root.transform;
        var rootRotation=root.rotation;
        var yaw=Quaternion.Euler(0f,(float)(radians*180/Math.PI),0f);
        var parts=visual.Parts;
        for(int i=0;i<parts.Length;i++)
        {
            var part=parts[i];
            if(part.T==null) continue;
            var (x,z)=CarouselModelGeometry.RotateAboutY(part.Pos.x,part.Pos.z,radians);
            part.T.position=root.TransformPoint(new Vector3((float)x,part.Pos.y,(float)z));
            part.T.rotation=rootRotation*yaw*part.Rot;
        }
        visual.PartsAngle=radians;
    }
    private static bool PartsValid(Visual visual)
    {
        switch(visual.Path)
        {
            case CarouselRingPartPath.UnderRing:
                if(visual.Ring==null) return false;
                if(visual.Ring.transform.childCount!=visual.RingObjects.Count) return false;
                for(int i=0;i<visual.RingObjects.Count;i++) if(visual.RingObjects[i]==null) return false;
                return true;
            case CarouselRingPartPath.Moved:
                if(visual.Parts.Length==0) return false;
                for(int i=0;i<visual.Parts.Length;i++) if(visual.Parts[i].T==null) return false;
                return true;
            default: return true; // baked: nothing to follow until the next rebinding
        }
    }
    private static int UnderRingCount(Visual visual)
    {
        int count=0;
        if(visual.Ring==null) return 0;
        var ring=visual.Ring.transform;
        for(int i=0;i<visual.RingObjects.Count;i++)
        {
            var go=visual.RingObjects[i];
            if(go==null) continue;
            var parent=go.transform.parent;
            if(parent!=null && parent.Pointer==ring.Pointer) count++;
        }
        return count;
    }
    // 0.2.33: where are the ring parts now? Under Ring: 0.2.32 path. Else find the MeshFilters that use one of our ring meshes (under the generator,
    // then under the vehicle root) and keep their pose in the Root frame without the angle applied so far. Nothing found: baked by the game.
    private static void ResolveRingParts(Visual visual)
    {
        int total=visual.RingObjects.Count, under=UnderRingCount(visual);
        if(total==0)
        {
            // 0.2.34: capacity 0 has no cassettes or rounds: nothing to turn, nothing was baked (no Info line, "path None" in the proof line).
            visual.Path=CarouselRingPartPath.None; visual.Parts=Array.Empty<RingPart>(); visual.Clones=0;
            return;
        }
        if(visual.Ring!=null && total>0 && under==total && visual.Ring.transform.childCount==total)
        {
            visual.Path=CarouselRingPartPath.UnderRing; visual.Parts=Array.Empty<RingPart>(); visual.Clones=0; visual.PartsAngle=0;
            return;
        }
        var found=new List<MeshFilter>();
        var wanted=new HashSet<IntPtr>(visual.RingMeshes);
        for(int level=0;level<2 && found.Count==0;level++)
        {
            var from=level==0 ? visual.Generator.transform : visual.Generator.transform.root;
            foreach(var filter in from.GetComponentsInChildren<MeshFilter>(true))
            {
                var mesh=filter.sharedMesh;
                if(mesh!=null && wanted.Contains(mesh.Pointer)) found.Add(filter);
            }
        }
        visual.Path=CarouselModelGeometry.RingPartPath(total,under,found.Count);
        if(visual.Path!=CarouselRingPartPath.Moved)
        {
            visual.Parts=Array.Empty<RingPart>(); visual.Clones=0;
            if(visual.Path==CarouselRingPartPath.Baked && !visual.BakedNoted)
            {
                visual.BakedNoted=true;
                Plugin.ModLog.LogInfo($"[Carousel visual] Basket {visual.Basket.VUID.Value}: ring meshes baked by the game; ring cannot turn in Play.");
            }
            return;
        }
        // The Ring object itself stays still from now on (parts under it are turned one by one like the others).
        if(visual.Ring!=null) visual.Ring.transform.localRotation=Quaternion.identity;
        var ours=new HashSet<IntPtr>();
        foreach(var go in visual.RingObjects) if(go!=null) ours.Add(go.Pointer);
        var root=visual.Root.transform;
        var inverseRoot=Quaternion.Inverse(root.rotation);
        var undo=Quaternion.Euler(0f,(float)(-visual.PartsAngle*180/Math.PI),0f);
        var parts=new RingPart[found.Count];
        int clones=0;
        for(int i=0;i<found.Count;i++)
        {
            var t=found[i].transform;
            var local=root.InverseTransformPoint(t.position);
            var (x,z)=CarouselModelGeometry.RotateAboutY(local.x,local.z,-visual.PartsAngle);
            parts[i]=new RingPart{ T=t, Pos=new Vector3((float)x,local.y,(float)z), Rot=undo*(inverseRoot*t.rotation) };
            if(!ours.Contains(found[i].gameObject.Pointer)) clones++;
        }
        visual.Parts=parts; visual.Clones=clones;
        ApplyParts(visual,visual.Angle);
    }
    private static void FinishTurn(Visual visual)
    {
        if(!visual.Turning) return;
        visual.Turning=false;
        Turns.Remove(visual);
        SetAngle(visual,visual.TurnTo);
    }
    /// <summary>0.2.32: drives the smooth turns. Called from the existing LoadTask.Update postfix (CarouselRuntime.AfterUpdate) for every load task;
    /// one list-count check when nothing turns, otherwise one angle and one localRotation per turning ring. Time based, so several calls in a
    /// frame are harmless; a ring whose last tick is more than a second old, or whose time is up, goes to its end angle.</summary>
    internal static void Tick()
    {
        if(Turns.Count==0) return;
        double now=Time.unscaledTime;
        for(int i=Turns.Count-1;i>=0;i--)
        {
            var visual=Turns[i];
            try
            {
                double elapsed=now-visual.TurnStart, since=now-visual.LastTick;
                visual.LastTick=now;
                double angle=CarouselModelGeometry.TurnAngleAt(visual.TurnFrom,visual.TurnTo,elapsed,CarouselModelGeometry.TurnSeconds,since);
                if(angle==visual.TurnTo || visual.Ring==null || elapsed>=CarouselModelGeometry.TurnSeconds) { FinishTurn(visual); if(Plugin.DetailedLogging) Plugin.ModLog.LogInfo($"[Carousel visual] Basket {visual.Basket.VUID.Value} ring turn done, {visual.Angle*180/Math.PI:0.0} deg."); }
                else SetAngle(visual,angle);
            }
            catch (Exception ex)
            {
                Turns.RemoveAt(i < Turns.Count ? i : Turns.Count-1);
                visual.Turning=false;
                Remove(visual.Basket.Pointer);
                Plugin.ModLog.LogWarning($"[Carousel visual] Native basket restored after optional visual failure: {ex.Message}");
            }
        }
    }
    // 0.2.32 proof line (DetailedLogging only): one per stock change; angle read back from the transform to show that it was applied.
    private static void LogStock(Visual visual, int stock, int previous, bool smooth)
    {
        int n=visual.Capacity, fired=CarouselModelGeometry.RingFired(n,stock), under=CarouselModelGeometry.RingSlotUnderLift(n,visual.LiftSlot,stock);
        double target=CarouselModelGeometry.RingTurn(n,stock)*180/Math.PI;
        float applied=visual.Ring!=null ? visual.Ring.transform.localEulerAngles.y : float.NaN;
        // 0.2.33: where the parts are (under the ring / moved / clones), the parent of the first part (4 levels), and where the lift target came from.
        int underRing=UnderRingCount(visual), alive=0;
        for(int i=0;i<visual.RingObjects.Count;i++) if(visual.RingObjects[i]!=null) alive++;
        int moved=alive-underRing;
        Transform? first=visual.Path==CarouselRingPartPath.Moved && visual.Parts.Length>0 ? visual.Parts[0].T : visual.RingObjects.Count>0 && visual.RingObjects[0]!=null ? visual.RingObjects[0].transform : null;
        string parents="none";
        if(first!=null)
        {
            parents=""; var up=first.parent;
            for(int level=0;level<4 && up!=null;level++, up=up.parent) parents+=(level>0?" < ":"")+up.name;
            if(parents.Length==0) parents="(no parent)";
        }
        string partAngle=visual.Path==CarouselRingPartPath.Moved && visual.Parts.Length>0 && first!=null ? $", first part world yaw {first.eulerAngles.y:0.0}" : "";
        Plugin.ModLog.LogInfo($"[Carousel visual] Basket {visual.Basket.VUID.Value} stock {stock}/{n}, fired {fired}, ring {target:0.0} deg ({(smooth?"turning":"set")}, was {previous}, transform now {applied:0.0}{partAngle}), slot under lift {under} ({(CarouselModelGeometry.RingEmpty(n,visual.LiftSlot,stock,under)?"empty":"full")}), ring children {(visual.Ring!=null?visual.Ring.transform.childCount:-1)}, capacity reason \"{visual.CapacityReason}\", model {visual.Level}, lift top {visual.LiftTop:0.000} / basket top {visual.BasketTop:0.000}, ring parts {underRing}/{visual.RingObjects.Count} under ring, path {visual.Path}, moved {moved}, clones {visual.Clones}, first part parents {parents}, lift target {visual.TargetSource.ToString().ToLowerInvariant()}.");
    }
    // 0.2.28 (M3): part of the merged UpdateRequest postfix (CarouselRuntime.RequestUpdated), second as before.
    internal static void SupplyReady(LoadTask __instance)
    {
        if (!CarouselRuntime.Mechanical(__instance,out var state)) return;
        if(!Models.TryGetValue(state.Basket.Pointer,out var visual)) return;
        Guard(state.Basket, () =>
        {
            Bind(visual,state.Rack);
        });
    }
    [HarmonyPostfix, HarmonyPatch(typeof(BasketGenerator), nameof(BasketGenerator.SetVisible))]
    private static void Visibility(BasketGenerator __instance)
    { if (Generators.TryGetValue(__instance.Pointer,out var visual)) Guard(visual.Basket, () => Sync(visual)); }
    [HarmonyPostfix, HarmonyPatch(typeof(BasketGenerator), nameof(BasketGenerator.ApplyLayer))]
    private static void Layer(BasketGenerator __instance)
    { if (Generators.TryGetValue(__instance.Pointer,out var visual)) Guard(visual.Basket, () => Sync(visual)); }
    [HarmonyPostfix, HarmonyPatch(typeof(TurretBasket), nameof(TurretBasket.OnFlagsChanged))]
    private static void Flags(TurretBasket __instance)
    { if (Models.TryGetValue(__instance.Pointer,out var visual)) Guard(__instance, () => Sync(visual)); }

    private static void Remove(IntPtr pointer)
    {
        if (!Models.Remove(pointer,out var visual)) return;
        if (visual.Turning) { visual.Turning = false; Turns.Remove(visual); }
        Generators.Remove(visual.Generator.Pointer);
        if (visual.Rack != null && visual.StorageChanged != null)
        {
            try { visual.Rack.remove_StorageValueChanged(visual.StorageChanged); }
            catch (Exception ex) { Plugin.ModLog.LogWarning($"[Carousel visual] Stock event cleanup: {ex.Message}"); }
        }
        if (visual.Root != null) { visual.Root.SetActive(false); Object.Destroy(visual.Root); }
        foreach (var part in visual.RingObjects) if (part != null) Object.Destroy(part); // ours that the game moved out of Root; clones of the game stay
        foreach (var mesh in visual.Meshes) if (mesh != null) Object.Destroy(mesh);
        foreach (var material in visual.Materials) if (material != null) Object.Destroy(material);
        foreach (var renderer in visual.Native) if (renderer != null) renderer.enabled = visual.Generator.visible;
    }
    // 0.2.28 (M3): part of the merged TurretBasket.Release prefix (CarouselRuntime.Released), last as before.
    internal static void Release(TurretBasket __instance) => Remove(__instance.Pointer);
}


