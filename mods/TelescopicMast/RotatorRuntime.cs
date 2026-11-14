using System.Diagnostics;
using Il2CppInterop.Runtime;
using Sprocket.Vehicles;
using Sprocket.Vehicles.Colliders;
using SprocketTelescopicMast.Io;
using UnityEngine;
namespace SprocketTelescopicMast;

/// <summary>HingeHost-owned turntables. Same absolute VehicleTransform + Notify child route as Leaf.</summary>
internal static class RotatorRuntime
{
    internal const string BaseTag = "sprocketRotatorBase", PlateTag = "sprocketRotatorPlate";
    internal const string BaseModel = "sprocketRotatorBaseModel", PlateModel = "sprocketRotatorPlateModel";
    private sealed class Plate
    {
        internal VehicleObject Object = null!;
        internal VehicleObjectModel Model = null!;
        internal readonly RotatorMotion Motion = new();
        internal FlowNode? Node;
        internal FlowEngine? Engine;
        internal bool Captured, Reapply, Warned;
        internal Quaternion RestRotation;
        internal Vector3 RestOffset;
        internal bool Alive => Object != null && !Object.WasCollected && !Object.Released;
    }
    private sealed class MeshEntry
    { internal VehicleObjectModel Model = null!; internal Mesh Mesh = null!; internal float X, Z; }
    private sealed class VehicleStats
    {
        internal readonly RotatorComGate Com = new();
        internal double Sum, Max, Since;
        internal int Ticks;
    }
    private static readonly Dictionary<int, Plate> Plates = new();
    private static readonly Dictionary<int, MeshEntry> Meshes = new();
    private static readonly Dictionary<IntPtr, VehicleStats> Stats = new();
    internal static bool Own(VehicleObjectModel m) => m != null &&
        (m.ComponentID == BaseModel && m.VehicleObject?.HasTag(BaseTag) == true || m.ComponentID == PlateModel && m.VehicleObject?.HasTag(PlateTag) == true);
    internal static bool IsPart(VehicleObject o) => o != null && (o.HasTag(BaseTag) || o.HasTag(PlateTag));
    internal static bool IsPlate(VehicleObject o) => o != null && o.HasTag(PlateTag);
    internal static bool Has(VehicleObject o) => o != null && Plates.ContainsKey(o.GetInstanceID());
    internal static double Angle(VehicleObject o) => Plates.TryGetValue(o.GetInstanceID(), out var p) ? p.Motion.Angle : 0;
    internal static void Prepare(VehicleObjectModel m)
    {
        if (!Own(m) || !m.HasModel) return;
        HingeRuntime.RegisterFlowPart(m.VehicleObject);
        int id = m.GetInstanceID(); var s = m.Scale;
        if (!Meshes.TryGetValue(id, out var entry) || entry.X != s.x || entry.Z != s.z)
        {
            var mesh = RotatorMesh.Create(m.ComponentID == PlateModel, s.x, s.z);
            if (entry != null) UnityEngine.Object.Destroy(entry.Mesh);
            Meshes[id] = entry = new MeshEntry { Model = m, Mesh = mesh, X = s.x, Z = s.z };
        }
        m.Model.SetMesh(entry.Mesh); m.Model.SetShadowProxy(entry.Mesh); m.Model.SetInteractionMesh(entry.Mesh);
        m.MarkDynamic();
        var t = m.VehicleObject.VehicleTransform;
        t.Options |= Sprocket.Placements.PlacementOptions.AllowScale;
        t.MinScale = new(.1f,.1f,.1f); t.MaxScale = new(5,5,5);
        // Identical owned-collider policy to Hinge Leaf. Native collision mesh stays loaded.
        m.generateDamageColliders = false; m.interactionColliderType &= ~ColliderType.Physics;
        var colliders = m.Colliders;
        if (colliders != null)
        {
            int count = colliders.Cast<Il2CppSystem.Collections.Generic.IReadOnlyCollection<IVehicleCollider>>().Count;
            for (int i=0; i<count; i++) if (colliders[i]?.Owner?.Pointer == m.Pointer) colliders[i].Type &= ~ColliderType.Physics;
        }
        if (m.ComponentID == PlateModel)
        {
            int objectId = m.VehicleObject.GetInstanceID();
            if (!Plates.TryGetValue(objectId, out var p)) Plates[objectId] = p = new Plate { Object = m.VehicleObject };
            p.Model = m;
        }
    }
    internal static void Balance(VehicleObjectModel m)
    {
        if (!Own(m)) return;
        PartPaint.Apply(m);
        var s = m.Scale; bool plate = m.ComponentID == PlateModel;
        float kg = RotatorMesh.Mass(plate, s.x, s.y);
        m.SetMass(kg, MassType.Mechanisms); m.cachedMass = m.GetMass(MassType.Everything);
        m.RecalculateCenterOfMass();
        m.SetCost(HingeMath.MaterialCost(kg), MassType.Mechanisms, CostType.Material);
        m.SetCost(HingeMath.AssemblyCost(!plate, s.x, s.y, s.x), MassType.Mechanisms, CostType.Assembly);
    }
    private static VehicleTransform? Basis(VehicleTransform t)
    {
        var parent = t.Parent ?? t.PlaceTarget;
        for (int i=0; parent != null && i<32; i++, parent=parent.Parent)
            if (parent.VehicleObject?.HasTag(BaseTag) == true) return parent;
        return null;
    }
    private static Vector3 Pivot(VehicleTransform basis) => basis.Position + basis.Rotation * new Vector3(0,.06f * Travel.ScaleAxis(basis.Scale.y),0);
    internal static void Maintain()
    {
        if (!HingeRuntime.InDesigner()) return;
        foreach (var p in Plates.Values.ToArray())
        {
            if (!p.Alive) { Remove(p.Object); continue; }
            if (p.Object.PhysicsEnabled) continue;
            Restore(p.Object, false); p.Motion.Reset(); p.Captured = false; p.Reapply = false;
            var t = p.Object.VehicleTransform; var basis = Basis(t);
            if (basis == null) { WarnUnattached(p); continue; }
            // Mesh bottom is y=.06 at scale 1. Independent Plate thickness is allowed.
            var position = Pivot(basis) - basis.Rotation * new Vector3(0,.06f * Travel.ScaleAxis(t.Scale.y),0);
            bool changed = false;
            if (Quaternion.Angle(t.Rotation,basis.Rotation) > .001f) { t.Rotation=basis.Rotation; changed=true; }
            if (Vector3.Distance(t.Position,position) > .00001f) { t.Position=position; changed=true; }
            if (changed) t.NotifyTransformChanged();
        }
    }
    private static void WarnUnattached(Plate p)
    {
        if (p.Warned) return; p.Warned = true;
        Plugin.Instance.Log.LogWarning("[Rotator] Attach Rotator Plate to Rotator Base; this unattached Plate does not rotate.");
    }
    internal static void CaptureRest(VehicleObject o)
    {
        if (o == null || !Plates.TryGetValue(o.GetInstanceID(),out var p)) return;
        Restore(o,false); var t=o.VehicleTransform; var basis=Basis(t);
        if (basis == null) { WarnUnattached(p); p.Captured=false; return; }
        var inverse=Quaternion.Inverse(basis.Rotation);
        p.RestRotation=inverse*t.Rotation; p.RestOffset=inverse*(t.Position-Pivot(basis));
        p.Motion.Reset(); p.Captured=true; p.Reapply=false;
    }
    private static bool ApplyPose(Plate p, double angle)
    {
        if (!p.Alive || !p.Captured) return false;
        var t=p.Object.VehicleTransform; var basis=Basis(t);
        if (basis == null) { WarnUnattached(p); return false; }
        var rest=p;
        var leader=HingeRuntime.TwinLeader(p.Object);
        if (leader != null && Plates.TryGetValue(leader.GetInstanceID(),out var primary) && primary.Captured) rest=primary;
        var spin=Quaternion.AngleAxis((float)angle,Vector3.up);
        var rotation=basis.Rotation*spin*rest.RestRotation;
        var position=Pivot(basis)+basis.Rotation*(spin*rest.RestOffset);
        bool changed=false;
        if (Quaternion.Angle(t.Rotation,rotation) > .0005f) { t.Rotation=rotation; changed=true; }
        if (Vector3.Distance(t.Position,position) > .00001f) { t.Position=position; changed=true; }
        if (changed) t.NotifyTransformChanged(); // native Leaf route carries all descendants
        return changed;
    }
    internal static void Restore(VehicleObject o, bool keepAngle)
    {
        if (o == null || !Plates.TryGetValue(o.GetInstanceID(),out var p) || !p.Captured) return;
        if (p.Motion.Angle != 0 || p.Reapply) ApplyPose(p,0);
        if (keepAngle) p.Reapply = p.Motion.Angle != 0;
        else { p.Motion.Reset(); p.Reapply=false; p.Captured=false; }
    }
    internal static void Unbind(IntPtr vehicle)
    {
        foreach (var p in Plates.Values.Where(p => p.Alive && p.Object.Vehicle?.Behaviour?.TryCast<VehicleBehaviour>()?.Pointer == vehicle))
        { p.Node=null; p.Engine=null; }
    }
    internal static void Bind(VehicleObject o, FlowNode n, FlowEngine e)
    { if (Plates.TryGetValue(o.GetInstanceID(),out var p)) { p.Node=n; p.Engine=e; } }
    internal static void Collect(IntPtr vehicle, Func<VehicleObject,string> id, List<RotatorState> result)
    {
        result.Clear();
        foreach (var p in Plates.Values.Where(p => p.Alive && p.Object.PhysicsEnabled && p.Object.Vehicle?.Behaviour?.TryCast<VehicleBehaviour>()?.Pointer == vehicle))
            result.Add(new(id(p.Object),p.Node == null ? "Rotator" : FlowPresentation.Title(p.Node),p.Motion.Angle));
    }
    internal static void TickVehicle(VehicleBehaviour vehicle, double now, float dt)
    {
        long started=Plugin.Detailed ? Stopwatch.GetTimestamp() : 0;
        var plates=Plates.Values.Where(p => p.Alive && p.Object.PhysicsEnabled && p.Object.Vehicle?.Behaviour?.TryCast<VehicleBehaviour>()?.Pointer == vehicle.Pointer).ToArray();
        if (plates.Length == 0) { Stats.Remove(vehicle.Pointer); return; }
        if (!Stats.TryGetValue(vehicle.Pointer,out var stats)) Stats[vehicle.Pointer]=stats=new VehicleStats { Since=now };
        bool changed=false, moving=false, stopped=false;
        foreach (var p in plates.OrderBy(p => HingeRuntime.TwinLeader(p.Object) == null ? 0 : 1))
        {
            if (!p.Captured) CaptureRest(p.Object);
            if (!p.Captured || Basis(p.Object.VehicleTransform) == null) { WarnUnattached(p); continue; }
            double before=p.Motion.Angle; bool wasMoving=p.Motion.Moving;
            var twinLeader=HingeRuntime.TwinLeader(p.Object);
            if (twinLeader != null && Plates.TryGetValue(twinLeader.GetInstanceID(),out var primaryPlate)) p.Motion.Follow(primaryPlate.Motion);
            else p.Motion.Tick(p.Node,p.Node == null ? default : p.Engine!.Rotator(p.Node.Id),dt);
            if (p.Reapply || before != p.Motion.Angle || twinLeader != null) { changed |= ApplyPose(p,p.Motion.Angle); p.Reapply=false; }
            moving |= p.Motion.Moving;
            stopped |= wasMoving && !p.Motion.Moving;
            if (p.Node != null) p.Engine!.RotatorFooters[p.Node.Id]=p.Motion.Footer(p.Node);
        }
        if (stats.Com.Tick(now,changed,moving,stopped)) foreach (var p in plates) p.Model.RecalculateCenterOfMass();
        if (Plugin.Detailed)
        {
            double ms=(Stopwatch.GetTimestamp()-started)*1000.0/Stopwatch.Frequency;
            stats.Sum+=ms; stats.Max=Math.Max(stats.Max,ms); stats.Ticks++;
            if (now-stats.Since >= 10)
            {
                int parts=plates.Sum(p => Descendants(p.Object.VehicleTransform,new HashSet<int>()));
                Plugin.Trace($"[ROTATOR PERF] vehicle={vehicle.GetInstanceID()} avg={stats.Sum/stats.Ticks:F4}ms max={stats.Max:F4}ms/tick rotators={plates.Length} parts={parts} ticks={stats.Ticks}");
                stats.Sum=stats.Max=0; stats.Ticks=0; stats.Since=now;
            }
        }
    }
    private static int Descendants(VehicleTransform t, HashSet<int> seen)
    {
        if (!seen.Add(t.GetInstanceID())) return 0;
        var children=t.Children; if (children == null) return 0;
        int n=children.Cast<Il2CppSystem.Collections.Generic.IReadOnlyCollection<VehicleTransform>>().Count, count=0;
        for (int i=0;i<n;i++) if (children[i] != null) count+=1+Descendants(children[i],seen);
        return count;
    }
    internal static void Remove(VehicleObject o)
    {
        if (o == null) return; Plates.Remove(o.GetInstanceID());
        foreach (var pair in Meshes.Where(e => e.Value.Model.VehicleObject?.Pointer == o.Pointer).ToArray())
        { UnityEngine.Object.Destroy(pair.Value.Mesh); Meshes.Remove(pair.Key); }
    }
    internal static void ResetAll()
    {
        foreach (var p in Plates.Values.ToArray()) if (p.Alive) Restore(p.Object,false);
        foreach (var m in Meshes.Values) UnityEngine.Object.Destroy(m.Mesh);
        Meshes.Clear(); Plates.Clear(); Stats.Clear();
    }
}
