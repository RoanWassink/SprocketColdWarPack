using Sprocket.Vehicles;
using SprocketTelescopicMast.Io;
using UnityEngine;
namespace SprocketTelescopicMast;

internal static partial class HingeRuntime
{
    private static string TwinType(VehicleObject obj)
    {
        if (obj.HasTag(MastRuntime.PartTag)) return "mast";
        if (RotatorRuntime.IsPlate(obj)) return "rotator";
        if (Leaves.TryGetValue(obj.GetInstanceID(), out var leaf)) return leaf.IsMount ? "mount" : "hinge";
        string component = ExternalComponentId(obj);
        return PartSettings.Find(component) != null ? "api:" + component : "";
    }
    private static VehicleTransform? TwinBase(VehicleObject obj, string type)
    {
        string tag = type == "mast" ? MastRuntime.BaseTag : type == "rotator" ? RotatorRuntime.BaseTag : BaseTag;
        var t = obj.VehicleTransform.Parent ?? obj.VehicleTransform.PlaceTarget;
        if (type.StartsWith("api:", StringComparison.Ordinal)) return t;
        for (int i=0;t != null && i<32;i++,t=t.Parent)
            if (t.VehicleObject?.HasTag(tag) == true) return t;
        return null;
    }
    private static int TwinVuid(VehicleObject obj)
    { try { return obj.VUID.Value; } catch { return 0; } }
    private static int TwinMirror(VehicleObject obj)
    {
        // Native read-only mirror reference, also present when Behaviour is null.
        try { return obj.VehicleTransform.MirrorVUID.Value; } catch { return -1; }
    }
    private static bool TwinLinked(NativeFlowPart a, NativeFlowPart b)
    {
        int am = TwinMirror(a.Object), bm = TwinMirror(b.Object);
        if (am > 0 || bm > 0) return am == TwinVuid(b.Object) && bm == TwinVuid(a.Object);
        return true; // Missing/unreadable references: same base + GUID + seat is the fallback.
    }
    private static void PrepareTwins()
    {
        var candidates = new List<(NativeFlowPart Part, string Type, string Guid, int Base)>();
        foreach (var p in FlowParts.Values)
        {
            string type = TwinType(p.Object), guid = p.Object.GUID ?? "";
            var basis = type.Length == 0 || guid.Length == 0 ? null : TwinBase(p.Object,type);
            int id = basis?.VehicleObject?.GetInstanceID() ?? 0;
            if (basis != null && (!p.Object.PhysicsEnabled || !p.TwinSeatReady || p.TwinSeatBase != id))
            {
                p.TwinSeat = Quaternion.Inverse(basis.Rotation) * (p.Object.VehicleTransform.Position-basis.Position);
                p.TwinSeatReady = true; p.TwinSeatBase = id;
            }
            p.TwinPrimary = null;
            if (basis != null && p.Session != null) candidates.Add((p,type,guid,id));
            else { p.TwinSeatReady = false; p.TwinSignature = ""; }
        }
        foreach (var group in candidates.GroupBy(c => (c.Part.Session,c.Type,c.Guid,c.Base)))
        {
            var primaries = new List<NativeFlowPart>();
            foreach (var c in group.OrderBy(c => TwinVuid(c.Part.Object) > 0 ? 0 : 1)
                .ThenBy(c => TwinVuid(c.Part.Object)).ThenBy(c => StablePartKey(c.Part.Object)))
            {
                var p = c.Part;
                var primary = primaries.FirstOrDefault(a => Vector3.Distance(a.TwinSeat,p.TwinSeat) <= .001f && TwinLinked(a,p));
                if (primary == null) primaries.Add(p); else p.TwinPrimary = primary;
                string signature = primary == null ? "" : primary.PartId+":"+p.PartId+":"+c.Type+":"+c.Base;
                if (signature != p.TwinSignature)
                {
                    p.TwinSignature = signature;
                    if (primary != null && Plugin.Detailed)
                        Plugin.Trace($"[FLOW] twin primary={primary.PartId} twin={p.PartId} type={c.Type} base={c.Base}");
                }
            }
        }
    }
    private static bool TwinOwnWiring(NativeFlowPart p) => p.Session?.Storage.Flow?.Nodes.Any(n =>
        FlowValidation.IsOutput(n) && n.PartId == p.PartId && p.Session.Storage.Flow.Wires.Any(w => w.To == n.Id || w.From == n.Id)) == true;
    private static NativeFlowPart TwinControlPart(NativeFlowPart p) => p.TwinPrimary != null && !TwinOwnWiring(p) ? p.TwinPrimary : p;
    internal static VehicleObject? TwinLeader(VehicleObject obj)
    {
        if (!FlowParts.TryGetValue(obj.GetInstanceID(),out var p)) return null;
        var control = TwinControlPart(p); return control == p ? null : control.Object;
    }
    private static void BindTwins(FlowSession s)
    {
        var flow = s.Storage.Flow; if (flow == null) return;
        foreach (var p in FlowParts.Values.Where(p => p.Session == s))
        {
            var control = TwinControlPart(p); if (control == p) continue;
            var node = flow.Nodes.FirstOrDefault(n => FlowValidation.IsOutput(n) && n.PartId == control.PartId && s.Engine.Status.GetValueOrDefault(n.Id) != "duplicate part");
            if (RotatorRuntime.IsPlate(p.Object) && node?.Type == "rotator") RotatorRuntime.Bind(p.Object,node,s.Engine);
            if (Leaves.TryGetValue(p.Object.GetInstanceID(),out var leaf)) leaf.FlowNode = node?.Type == "hinge" ? node : null;
        }
    }
}
