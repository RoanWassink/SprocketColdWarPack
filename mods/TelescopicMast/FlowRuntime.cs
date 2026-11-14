using Il2CppInterop.Runtime;
using Sprocket.Vehicles;
using SprocketTelescopicMast.Io;
using UnityEngine;
using SerializationInfo = Il2CppSystem.Runtime.Serialization.SerializationInfo;
namespace SprocketTelescopicMast;

internal static partial class HingeRuntime
{
    private sealed class NativeFlowPart
    {
        internal VehicleObject Object = null!;
        internal string? SavedId, LegacyRaw;
        internal FlowDocument? Copy;
        internal FlowSession? Session;
        internal string PartId = "", Key = "";
        internal bool Dirty = true;
        /// <summary>v1.1: this carrier is an external managed-output part, not a mast part.</summary>
        internal bool Managed;
        internal NativeFlowPart? TwinPrimary;
        internal Vector3 TwinSeat;
        internal int TwinSeatBase;
        internal bool TwinSeatReady;
        internal string TwinSignature = "";
    }
    private sealed class FlowSession
    {
        internal readonly FlowStorage Storage = new();
        internal readonly FlowEngine Engine = new();
        // The engine survives designer/play transitions; its clock must have the same lifetime.
        internal readonly FlowGameClock Clock = new();
        internal VehicleBehaviour? Owner;
        internal string Configured = "";
        internal bool HasStoredFlow, MixedToggleWarned;
        internal string MigrationSignature = "";
        internal string DesignerListSignature = "";
        internal readonly Dictionary<string, bool> Targets = new();
        internal FlowSession()
        {
            Storage.Warning = m => Plugin.Instance.Log.LogWarning("[IO FLOW] " + m);
            Engine.LoopDetected = (id, at) => Plugin.Instance.Log.LogWarning($"[IO FLOW] Output {id} exceeded 10 changes/s at {at:F2}; paused for 2 seconds.");
            Engine.PartExists = Storage.Contains;
            Engine.Apply = (n, on) =>
            {
                Targets[n.PartId] = on;
                if (Plugin.Detailed)
                {
                    var source = Storage.Flow?.Wires.FirstOrDefault(w => w.To == n.Id && w.Port == 0)?.From;
                    var block = Storage.Flow?.Nodes.FirstOrDefault(b => b.Id == source);
                    Plugin.Trace("[FLOW] out " + FlowPresentation.Title(n) + (on ? " on" : " off") + " (by " + (block == null ? "unconnected" : FlowPresentation.Title(block)) + ") part=" + n.PartId);
                }
            };
        }
    }
    private static readonly Dictionary<int, NativeFlowPart> FlowParts = new();
    private static readonly Dictionary<IntPtr, FlowSession> Flows = new();
    internal static string PartId(VehicleObject obj) => FlowParts.TryGetValue(obj.GetInstanceID(), out var p) ? p.PartId : "";
    internal static bool FlowComponent(string id) => id == LeafModel || id == MountModel ||
        id == RotatorRuntime.BaseModel || id == RotatorRuntime.PlateModel ||
        id == MastRuntime.Piston || id == MastRuntime.Sleeve || id == MastRuntime.BaseModel || id == MastRuntime.PartModel ||
        IoApiCore.IsRegisteredComponent(id);
    private static bool IsFlowPart(VehicleObject? obj)
    {
        if (obj == null) return false;
        // v1.1: an API part with a registered ComponentID counts like an own part. Only a
        // managed output owns ioFlowV1/ioPartId; a v1 rules output is handled as external data.
        return IsMoving(obj) || RotatorRuntime.IsPart(obj) || obj.HasTag(MastRuntime.Tag) || obj.HasTag(MastRuntime.BaseTag) || obj.HasTag(MastRuntime.PartTag) ||
            HingeRuntime.IsManagedPart(obj) || PartSettings.Find(ExternalComponentId(obj)) != null;
    }
    internal static void RegisterFlowPart(VehicleObject obj)
    {
        if (obj == null || obj.WasCollected || obj.Released) return;
        // Also remember v1 outputs, which do not own a managed FlowParts entry.
        IsExternalPart(obj);
        if (!IsFlowPart(obj)) return;
        int id = obj.GetInstanceID();
        string component = HingeRuntime.ExternalComponentId(obj);
        var partGo = HingeRuntime.ExternalPartObject(obj);
        if (component.Length > 0 && partGo != null) IoApiCore.AttachPart(partGo, component);
        if (!FlowParts.TryGetValue(id, out var part)) FlowParts[id] = part = new NativeFlowPart { Object = obj };
        part.Managed = HingeRuntime.IsManagedPart(obj) || PartSettings.Find(component) != null;
    }
    private static string StablePartKey(VehicleObject obj, string suffix = "")
    { int vuid = 0; try { vuid = obj.VUID.Value; } catch { } return vuid.ToString("D10") + ":" + obj.GetInstanceID().ToString("D10") + suffix; }
    private static FlowSession? SessionFor(VehicleObject? obj)
    {
        if (obj?.Vehicle == null) return null;
        IntPtr root = obj.Vehicle.Pointer;
        if (!Flows.TryGetValue(root, out var session)) Flows[root] = session = new FlowSession();
        session.Owner = obj.Vehicle.Behaviour?.TryCast<VehicleBehaviour>();
        return session;
    }
    private static FlowSession? SessionFor(VehicleIo io)
    {
        var obj = io.Leaves.FirstOrDefault(l => l.Alive)?.Object ?? io.External.FirstOrDefault(e => e.Alive)?.Component.VehicleObject;
        if (obj != null) return SessionFor(obj);
        return FlowParts.Values.Where(p => p.Object != null && !p.Object.Released)
            .FirstOrDefault(p => p.Object.Vehicle?.Behaviour?.TryCast<VehicleBehaviour>()?.Pointer == io.Vehicle?.Pointer)?.Session;
    }
    internal static bool HasFlowParts(VehicleBehaviour? vehicle) => vehicle != null && FlowParts.Values.Any(p =>
        p.Object != null && !p.Object.Released && p.Object.Vehicle?.Behaviour?.TryCast<VehicleBehaviour>()?.Pointer == vehicle.Pointer);

    /// <summary>Managed host phase, outside every Harmony callback. Discovers membership and calls API persistence.</summary>
    internal static void PrepareFlows()
    {
        foreach (var pair in FlowParts.ToArray())
            if (pair.Value.Object == null || pair.Value.Object.WasCollected || pair.Value.Object.Released)
            { pair.Value.Session?.Storage.Detach(pair.Value.Key); FlowParts.Remove(pair.Key); }
        foreach (var p in FlowParts.Values.OrderBy(p => StablePartKey(p.Object)).ToArray())
        {
            var session = SessionFor(p.Object); if (session == null) continue;
            string key = StablePartKey(p.Object);
            if (p.Dirty || p.Session != session || p.Key != key)
            {
                if (p.Session != null && (p.Session != session || p.Key != key)) p.Session.Storage.Detach(p.Key);
                AdmitStored(session, p.Copy);
                p.Session = session; p.Key = key; p.PartId = session.Storage.Attach(key, p.SavedId, p.Copy); p.SavedId = p.PartId; p.Dirty = false;
            }
            var vehicle = p.Object.Vehicle?.Behaviour?.TryCast<VehicleBehaviour>();
            if (vehicle != null && !Vehicles.ContainsKey(vehicle.Pointer))
            { Vehicles[vehicle.Pointer] = new VehicleIo { Vehicle = vehicle }; IoApiCore.ResetVehicle(vehicle.gameObject); }
        }
        foreach (var io in Vehicles.Values.ToArray())
            foreach (var ext in io.External.Where(e => e.Alive && e.ConfigLoaded && !e.Disabled).OrderBy(e => StablePartKey(e.Component.VehicleObject, e.RegistrationId)))
            {
                var s = SessionFor(ext.Component.VehicleObject); if (s == null) continue;
                if (ext.Managed || PartSettings.Find(ExternalComponentId(ext.Component.VehicleObject)) != null)
                {
                    // A managed part already carries its own copy in FlowParts, like a mast part.
                    RegisterFlowPart(ext.Component.VehicleObject);
                    if (FlowParts.TryGetValue(ext.Component.VehicleObject.GetInstanceID(), out var carrier))
                    {
                        // v1 outputs with settings retain their loadRules/saveRules callbacks.
                        AdmitStored(s, ext.FlowCopy);
                        if (carrier.PartId.Length == 0 && ext.PartId.Length > 0) carrier.SavedId = ext.PartId;
                        // Attach now: a fresh managed part must have its id before this tick.
                        if (carrier.PartId.Length == 0)
                        {
                            AdmitStored(s, carrier.Copy);
                            carrier.Session = s; carrier.Key = StablePartKey(carrier.Object);
                            carrier.PartId = s.Storage.Attach(carrier.Key, carrier.SavedId, carrier.Copy);
                            carrier.SavedId = carrier.PartId; carrier.Dirty = false;
                        }
                        ext.PartId = carrier.PartId;
                    }
                    ext.FlowCopy = null;
                    continue;
                }
                var key = StablePartKey(ext.Component.VehicleObject, ":" + ext.RegistrationId);
                if (ext.FlowKey != key || ext.FlowCopy != null)
                {
                    if (ext.FlowKey != key) ext.DetachFlow?.Invoke();
                    AdmitStored(s, ext.FlowCopy);
                    ext.PartId = s.Storage.Attach(key, ext.PartId, ext.FlowCopy);
                    ext.FlowKey = key;
                    ext.DetachFlow = () => s.Storage.Detach(key);
                }
                ext.FlowCopy = null; // copy admitted once; stale envelopes cannot overwrite a later local edit
            }
        foreach (var p in FlowParts.Values)
            if (ExternalPartObject(p.Object) is { } go) PartSettings.Ensure(go);
        PrepareTwins();
        foreach (var s in Flows.Values)
        {
            if (s.Storage.Flow?.Rev > 1) s.HasStoredFlow = true;
            if (!s.HasStoredFlow)
            {
                var configs = FlowParts.Values.Where(p => p.Session == s && Leaves.ContainsKey(p.Object.GetInstanceID()))
                    .Select(p => (p.PartId, Leaves[p.Object.GetInstanceID()].Config)).ToList();
                configs.AddRange(Vehicles.Values.SelectMany(io => io.External).Where(e => e.PartId.Length > 0 && SessionFor(e.Component.VehicleObject) == s).Select(e => (e.PartId, e.Config)));
                // No speculative migration until every currently discovered API part has loaded.
                string signature = string.Join("|", configs.OrderBy(c => c.PartId).Select(c => c.PartId + HingeConfigJson.Write(c.Item2)));
                if (s.Storage.Flow == null || signature != s.MigrationSignature)
                {
                    s.MigrationSignature = signature;
                    s.Storage.Set(FlowMigration.Migrate(configs, m => { if (!s.MixedToggleWarned) { s.MixedToggleWarned = true; s.Storage.Warning?.Invoke(m); } }));
                }
            }
        }
        foreach (var io in Vehicles.Values.ToArray())
            foreach (var ext in io.External.Where(e => e.Alive && e.ConfigLoaded && !e.Disabled))
            {
                var s = SessionFor(ext.Component.VehicleObject); if (s?.Storage.Flow == null || ext.PartId.Length == 0) continue;
                string json = FlowJson.Envelope(ext.PartId, s.Storage.Flow);
                if (json == ext.LastFlowSaved) continue;
                var vehicle = io.Vehicle?.gameObject; if (vehicle == null) continue;
                if (IoApiCore.TrySaveRules(ext.RegistrationId, vehicle, ext.Component.gameObject, json))
                { ext.LastFlowSaved = json; TraceFlow("save API", s.Storage.Flow, ext.PartId); }
                else ext.Disabled = true;
            }
    }
    private static void TraceFlow(string action, FlowDocument flow, string part)
    {
        if (Plugin.Detailed) Plugin.Trace($"[FLOW] {action} rev={flow.Rev} blocks={flow.Nodes.Count} wires={flow.Wires.Count} part={part}");
    }
    private static void AdmitStored(FlowSession s, FlowDocument? copy)
    {
        if (copy == null || s.HasStoredFlow) return;
        s.HasStoredFlow = true; s.Storage.Set(copy);
    }
    private static void TickFlow(VehicleIo io, double now)
    {
        RotatorRuntime.Unbind(io.Vehicle?.Pointer ?? IntPtr.Zero);
        var s = SessionFor(io); if (s?.Storage.Flow == null) return;
        var flow = s.Storage.Flow; string json = FlowJson.Write(flow);
        foreach (var leaf in io.Leaves) leaf.FlowNode = null;
        if (json != s.Configured)
        {
            var present = flow.Nodes.Where(FlowValidation.IsOutput).Select(n => n.PartId).ToHashSet();
            foreach (var id in s.Targets.Keys.Where(id => !present.Contains(id)).ToArray()) s.Targets.Remove(id);
            foreach (var leaf in io.Leaves) leaf.FlowNode = null;
            s.Engine.Configure(flow); s.Configured = json;
        }
        s.Engine.Tick(now, io.Context);
        if (Plugin.Detailed)
            foreach (var shot in io.Context.Fired)
            {
                int matched = flow.Nodes.Count(n => n.Type == "cannon.fired" &&
                    CannonParams.Matcher(new InputInstance(n.Type, n.InputParams), io.Context)(shot.Vuid, shot.FireGroupMask));
                Plugin.Trace($"[FLOW] event cannon.fired group={FireGroups.Describe(shot.FireGroupMask)} session={io.Vehicle?.Pointer} matched={matched} blocks");
            }
        foreach (var n in flow.Nodes.Where(n => n.Type == "rotator" && s.Engine.Status.GetValueOrDefault(n.Id) != "duplicate part").GroupBy(n => n.PartId).Select(g => g.First()))
        {
            var p = FlowParts.Values.FirstOrDefault(p => p.Session == s && p.PartId == n.PartId);
            if (p != null && RotatorRuntime.IsPlate(p.Object)) RotatorRuntime.Bind(p.Object, n, s.Engine);
        }
        foreach (var e in s.Engine.InputErrors) WarnOnce("flow input " + e.Key, $"[IO FLOW] Input {e.Key}: {e.Value}");
        foreach (var n in flow.Nodes.Where(n => FlowValidation.IsOutput(n) && n.Type == "hinge").GroupBy(n => n.PartId).Select(g => g.First()))
        {
            var p = FlowParts.Values.FirstOrDefault(p => p.Session == s && p.PartId == n.PartId);
            if (p == null || !Leaves.TryGetValue(p.Object.GetInstanceID(), out var leaf)) continue;
            // Flow parameters drive existing hinge motion without editing hingeIoV1.
            leaf.FlowNode = n;
        }
        BindTwins(s);
        foreach (var p in FlowParts.Values.Where(p => p.Session == s))
            if (MastRuntime.IsFlowOutput(p.Object))
            {
                if (s.Targets.TryGetValue(TwinControlPart(p).PartId, out bool on)) MastRuntime.FlowCommand(p.Object, on);
                else MastRuntime.UnbindFlow(p.Object);
            }
        MastRuntime.TickFlow(io.Vehicle?.Pointer ?? IntPtr.Zero);
    }
    private static bool FlowTarget(VehicleIo io, VehicleObject obj)
    {
        var s = SessionFor(io); if (s == null || !FlowParts.TryGetValue(obj.GetInstanceID(), out var p)) return false;
        return s.Targets.TryGetValue(TwinControlPart(p).PartId, out bool on) && on;
    }
    private static bool FlowTarget(VehicleIo io, ExternalOutput ext)
    {
        var s = SessionFor(io);
        var id = FlowParts.TryGetValue(ext.Component.VehicleObject.GetInstanceID(), out var p) ? TwinControlPart(p).PartId : ext.PartId;
        return s != null && s.Targets.TryGetValue(id, out bool on) && on;
    }
    private static void LoadFlow(VehicleObject obj, SerializationInfo info)
    {
        RegisterFlowPart(obj); if (!FlowParts.TryGetValue(obj.GetInstanceID(), out var p)) return;
        if (p.Managed)
        {
            string? error = ExternalPartState.ReadFlow(new SerializationStateBag(info), out var partId, out var loaded);
            if (error != null) Warn("Flow load", new Exception(error));
            if (loaded != null) { if (p.Copy == null || loaded.Rev > p.Copy.Rev) p.Copy = loaded; TraceFlow("load", loaded, partId ?? p.SavedId ?? obj.GetInstanceID().ToString()); }
            if (partId != null) p.SavedId = partId;
            p.Dirty = true;
            return;
        }
        string? copy = null, savedId = null;
        var e = info.GetEnumerator(); while (e.MoveNext())
        {
            if (e.Name == FlowDocument.StateKey) copy = info.GetString(e.Name);
            else if (e.Name == FlowDocument.PartKey) savedId = info.GetString(e.Name);
            else if (e.Name == StateKey) p.LegacyRaw = info.GetString(e.Name);
        }
        if (copy != null) { try { var loaded = FlowJson.Parse(copy); if (p.Copy == null || loaded.Rev > p.Copy.Rev) p.Copy = loaded; TraceFlow("load", loaded, savedId ?? p.SavedId ?? obj.GetInstanceID().ToString()); } catch (Exception ex) { Warn("Flow load", ex); } }
        if (savedId != null) p.SavedId = savedId;
        p.Dirty = true;
    }
    private static void SaveFlow(VehicleObject obj, SerializationInfo state)
    {
        RegisterFlowPart(obj); if (!FlowParts.TryGetValue(obj.GetInstanceID(), out var p)) return;
        var s = SessionFor(obj); if (s == null) return;
        // Saving can precede the next host Update: admit every native copy already received
        // by LoadData before choosing the winner. No foreign callback runs on this hook path.
        foreach (var other in FlowParts.Values.Where(x => x.Object?.Vehicle?.Pointer == obj.Vehicle.Pointer).OrderBy(x => StablePartKey(x.Object)))
        {
            string key = StablePartKey(other.Object);
            if (other.Session == s && !other.Dirty && other.Key == key) continue;
            if (other.Session != null && (other.Session != s || other.Key != key)) other.Session.Storage.Detach(other.Key);
            AdmitStored(s, other.Copy); other.Session = s; other.Key = key;
            other.PartId = s.Storage.Attach(key, other.SavedId, other.Copy); other.SavedId = other.PartId; other.Dirty = false;
        }
        if (p.Session != s || p.Dirty)
        { AdmitStored(s, p.Copy); p.Key = StablePartKey(obj); p.Session = s; p.PartId = s.Storage.Attach(p.Key, p.SavedId, p.Copy); p.SavedId = p.PartId; p.Dirty = false; }
        if (s.Storage.Flow == null)
            s.Storage.Set(FlowMigration.Migrate(FlowParts.Values.Where(x => x.Session == s && Leaves.ContainsKey(x.Object.GetInstanceID())).Select(x => (x.PartId, Leaves[x.Object.GetInstanceID()].Config))));
        var bag = new SerializationStateBag(state);
        if (p.Managed) ExternalPartState.WriteFlow(bag, Il2CppType.Of<string>(), p.PartId, s.Storage.Flow!);
        else
        {
            StateMerge.Merge(bag, FlowDocument.PartKey, p.PartId, Il2CppType.Of<string>());
            StateMerge.Merge(bag, FlowDocument.StateKey, FlowJson.Write(s.Storage.Flow!), Il2CppType.Of<string>());
        }
        TraceFlow("save", s.Storage.Flow!, p.PartId);
    }
}
