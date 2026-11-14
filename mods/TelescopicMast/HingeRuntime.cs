using Il2CppInterop.Runtime;
using Sprocket.PartImporting;
using Sprocket.Vehicles;
using Sprocket.Vehicles.Colliders;
using Sprocket.Vehicles.Control;
using Sprocket.Vehicles.Serialization;
using SprocketTelescopicMast.Io;
using UnityEngine;
using UnityEngine.SceneManagement;
using SerializationInfo = Il2CppSystem.Runtime.Serialization.SerializationInfo;
namespace SprocketTelescopicMast;

/// <summary>
/// Hinge Base + Hinge Leaf / Hinge Mount runtime. Leaf and Mount share all rotation, storage and I/O
/// code (one Leaf record with IsMount); the Mount is a small carrier that is hidden while physics runs. The leaf rotates about the base's local X by writing
/// the leaf VehicleTransform rotation absolutely from a rest pose captured at EnablePhysics
/// (never a delta), then NotifyTransformChanged, like the mast does with Position.
/// </summary>
internal static partial class HingeRuntime
{
    internal const string BaseTag = "sprocketHingeBase", LeafTag = "sprocketHingeLeaf";
    internal const string BaseModel = "sprocketHingeBaseModel", LeafModel = "sprocketHingeLeafModel";
    internal const string MountTag = "sprocketHingeMount", MountModel = "sprocketHingeMountModel";
    internal const string StateKey = "hingeIoV1";

    internal sealed class Leaf
    {
        internal readonly VehicleObject Object;
        internal readonly int Id;
        internal readonly string OutputId;
        internal HingeConfig Config = HingeConfig.CreateDefault();
        internal FlowNode? FlowNode;
        internal float FlowFloat(string key, float fallback) => (float)(FlowNode?.Number(key, Config.GetOutputFloat(key, fallback)) ?? Config.GetOutputFloat(key, fallback));
        internal float Angle;
        internal bool RestCaptured, Reapply, PoseLogged, Fitted;
        internal Quaternion RestRotation = Quaternion.identity; // leaf rotation relative to the base
        internal Vector3 RestOffset;                             // leaf origin relative to the pin, base frame
        internal IntPtr Vehicle;
        internal int PoseSamples;
        internal readonly bool IsMount;
        internal VehicleObjectModel? Model;   // the mount's own model, hidden during play
        internal bool Hidden, TargetOpen;
        internal float NextHide;
        internal int Vuid;
        internal Leaf(VehicleObject obj, bool mount) { Object = obj; IsMount = mount; Id = obj.GetInstanceID(); OutputId = "hinge#" + Id; }
        internal bool Alive => Object != null && !Object.WasCollected && !Object.Released;
        internal string DisplayName(int index) => string.IsNullOrWhiteSpace(Config.Name) ? $"Hinge {index + 1}" : Config.Name;
        internal string Kind => IsMount ? "Mount" : "Leaf";
        /// <summary>State for hinge.state inputs, from the end of the previous tick.</summary>
        internal HingeState State()
        {
            bool openAt = Angle == HingeMath.SanitizeAngle(FlowFloat("openAngle", 90));
            bool closedAt = Angle == HingeMath.SanitizeAngle(FlowFloat("closedAngle", 0));
            return new HingeState(Vuid, Config.Name ?? "", TargetOpen && openAt, !TargetOpen && closedAt);
        }
    }

    /// <summary>An output part owned by another mod, found on the vehicle by ComponentID.</summary>
    internal sealed class ExternalOutput
    {
        internal readonly string RegistrationId, DisplayName, OnLabel, OffLabel, OutputId;
        internal readonly VehicleComponent Component;
        internal readonly bool Managed;
        internal HingeConfig Config = new();
        internal readonly ExternalOutputPower Power = new();
        internal IntPtr Vehicle;
        internal int Sequence;
        internal bool ConfigLoaded, Disabled;
        internal string PartId = "", LastFlowSaved = "", FlowKey = "";
        internal FlowDocument? FlowCopy;
        internal Action? DetachFlow;
        internal string PartName = "";
        internal bool Initialized => Power.Initialized;
        internal bool LastApplied => Power.LastApplied;
        internal ExternalOutput(ExternalOutputRegistration registration, VehicleComponent component)
        {
            RegistrationId = registration.Id;
            DisplayName = registration.DisplayName;
            OnLabel = registration.OnLabel;
            OffLabel = registration.OffLabel;
            Component = component;
            Managed = registration.Managed;
            OutputId = "ext#" + registration.Id + "#" + component.GetInstanceID();
            Config = new HingeConfig { Name = registration.DisplayName, OutputType = registration.Id };
            PartName = PartNameOf(component);
            // The API's part GameObject is this component's; attach before the first apply.
            IoApiCore.AttachPart(component.gameObject, registration.ComponentId);
        }
        internal bool Alive => Component != null && !Component.WasCollected && Component.VehicleObject != null && !Component.VehicleObject.Released;
        /// <summary>M2: the part's own name plus its number; registration displayName when no name is readable.</summary>
        internal string Label => IoApiCore.ExternalLabel(PartName, DisplayName, Sequence);
    }

    /// <summary>
    /// The part definition name, read through the existing native part database. No new hook:
    /// VehicleObject.GUID is the part-definition GUID. Null on any failure, so the caller falls
    /// back to the registration's displayName.
    /// </summary>
    private static string PartNameOf(VehicleComponent component)
    {
        try
        {
            string? guid = component?.VehicleObject?.GUID;
            if (string.IsNullOrWhiteSpace(guid)) return "";
            var definition = IVehiclePartInfoSource.Instance?.GetInfo<PartDefinition>(guid);
            return definition?.name?.Trim() ?? "";
        }
        catch { return ""; }
    }

    internal sealed class VehicleIo
    {
        internal readonly VehicleIoContext Context = new();
        internal readonly FlowGameClock Clock = new();
        internal readonly List<Leaf> Leaves = new();
        internal readonly List<ExternalOutput> External = new();
        internal VehicleBehaviour? Vehicle;
        internal int LastFrame = -1;
        internal float NextExternalScan;
        internal string ExternalScanSignature = "";
    }

    private static readonly Dictionary<int, Leaf> Leaves = new();
    private static readonly Dictionary<int, string> PendingConfigs = new();
    private static readonly Dictionary<IntPtr, VehicleIo> Vehicles = new();
    private static readonly HashSet<string> Warnings = new();
    private static Mesh? baseMesh, leafMesh, mountMesh;
    private static float nextMembership;
    private static int restoreTraces, ioTraces, saveTraces;
    /// <summary>Bumped whenever any hinge config changes; engines reconfigure on the next tick.</summary>
    internal static int ConfigRevision;
    internal static IReadOnlyCollection<Leaf> AllLeaves => Leaves.Values;
    internal static bool AnyLeaves => Leaves.Count > 0 || FlowParts.Count > 0 || IoApiCore.OutputRegistrations.Count > 0;
    internal static IReadOnlyList<ExternalOutput> AllExternalOutputs =>
        Vehicles.Values.SelectMany(io => io.External).Where(x => x.Alive).ToArray();
    internal static ExternalOutput? FindExternal(string outputId) =>
        Vehicles.Values.SelectMany(io => io.External).FirstOrDefault(x => x.OutputId == outputId && x.Alive);

    internal static void Warn(string area, Exception ex)
    { if (Warnings.Add(area)) Plugin.Instance.Log.LogWarning($"[Hinge] {area}: {ex.Message}"); }
    private static void WarnOnce(string key, string message)
    { if (Warnings.Add(key)) Plugin.Instance.Log.LogWarning(message); }

    internal static bool Own(VehicleObjectModel model) =>
        (model.ComponentID == BaseModel && model.VehicleObject?.HasTag(BaseTag) == true) ||
        (model.ComponentID == LeafModel && model.VehicleObject?.HasTag(LeafTag) == true) ||
        (model.ComponentID == MountModel && model.VehicleObject?.HasTag(MountTag) == true);
    /// <summary>Leaf or Mount object: the parts that swing and carry an I/O config.</summary>
    internal static bool IsMoving(VehicleObject? obj) => obj != null && (obj.HasTag(LeafTag) || obj.HasTag(MountTag));

    /// <summary>The first VehicleComponent on this object with an external output registration.</summary>
    private static VehicleComponent? ExternalComponent(VehicleObject? obj)
    {
        if (obj == null || obj.WasCollected) return null;
        try
        {
            var components = obj.Components;
            if (components == null) return null;
            int count = components.Cast<Il2CppSystem.Collections.Generic.IReadOnlyCollection<VehicleComponent>>().Count;
            for (int i = 0; i < count; i++)
            {
                var component = components[i];
                if (component != null && !component.WasCollected && IoApiCore.IsRegisteredComponent(component.ComponentID)) return component;
            }
        }
        catch { }
        return null;
    }

    private static string ExternalComponentId(VehicleObject? obj) => ExternalComponent(obj)?.ComponentID ?? "";

    /// <summary>The GameObject the API uses for this part: VehicleComponent.gameObject, exactly what apply gets.</summary>
    private static GameObject? ExternalPartObject(VehicleObject? obj) => ExternalComponent(obj)?.gameObject;

    /// <summary>A part of any external output (v1 rules output or v1.1 managed output): owns ioPartDataV1.</summary>
    internal static bool IsExternalPart(VehicleObject? obj)
    {
        bool external = ExternalComponentId(obj).Length > 0;
        if (external) RememberExternalPart(obj!);
        return external;
    }

    /// <summary>A v1.1 managed output part: additionally owns ioFlowV1 and ioPartId.</summary>
    internal static bool IsManagedPart(VehicleObject? obj)
    {
        if (obj == null || obj.WasCollected) return false;
        try
        {
            var components = obj.Components;
            if (components == null) return false;
            int count = components.Cast<Il2CppSystem.Collections.Generic.IReadOnlyCollection<VehicleComponent>>().Count;
            for (int i = 0; i < count; i++)
            {
                var component = components[i];
                if (component != null && !component.WasCollected && IoApiCore.IsManagedComponent(component.ComponentID)) return true;
            }
        }
        catch { }
        return false;
    }

    // ---------- Part preparation, mass and cost ----------
    internal static void Prepare(VehicleObjectModel component)
    {
        if (IsExternalPart(component.VehicleObject)) RegisterFlowPart(component.VehicleObject);
        if (!Own(component) || !component.HasModel) return;
        bool isBase = component.ComponentID == BaseModel, isMount = component.ComponentID == MountModel;
        var mesh = isBase ? baseMesh ??= HingeMesh.CreateBase() : isMount ? mountMesh ??= HingeMesh.CreateMount() : leafMesh ??= HingeMesh.CreateLeaf();
        var model = component.Model;
        // Native collision mesh retained (as the mast): our mesh is render, shadow and placement only.
        model.SetMesh(mesh); model.SetShadowProxy(mesh); model.SetInteractionMesh(mesh);
        component.MarkDynamic();
        var placement = component.VehicleObject.VehicleTransform;
        placement.Options |= Sprocket.Placements.PlacementOptions.AllowScale;
        placement.MinScale = new Vector3(.1f, .1f, .1f);
        placement.MaxScale = new Vector3(5f, 5f, 5f);
        PhysicsSafety(component);
        if (isBase) return;
        var leaf = Register(component.VehicleObject, isMount);
        if (isMount) { leaf.Model = component; leaf.Hidden = false; } // a rebuild restores renderers; hide again next tick
    }

    private static Leaf Register(VehicleObject obj, bool mount)
    {
        RegisterFlowPart(obj);
        int id = obj.GetInstanceID();
        if (!Leaves.TryGetValue(id, out var leaf))
        {
            Leaves.Add(id, leaf = new Leaf(obj, mount));
            ConfigRevision++;
            if (Plugin.Detailed) Plugin.Trace($"[Hinge] Registered {leaf.Kind.ToLowerInvariant()} object={id}.");
        }
        if (PendingConfigs.Remove(id, out var json)) ApplyLoaded(leaf, json, "pending");
        return leaf;
    }

    internal static void Balance(VehicleObjectModel component)
    {
        if (!Own(component)) return;
        PartPaint.Apply(component); // Existing post-Build path; the hidden Mount is excluded.
        bool isBase = component.ComponentID == BaseModel, isMount = component.ComponentID == MountModel;
        var s = component.Scale;
        float kg = HingeMath.Mass(isBase ? HingeMath.BaseVolume : isMount ? HingeMath.MountVolume : HingeMath.LeafVolume, s.x, s.y, s.z);
        float previous = component.CachedMass;
        component.SetMass(kg, MassType.Mechanisms);
        // 0.3.7 mast fix: SetMass does not refresh cachedMass used by the COM numerator.
        float resource = component.GetMass(MassType.Everything);
        component.cachedMass = resource;
        component.RecalculateCenterOfMass();
        component.SetCost(HingeMath.MaterialCost(kg), MassType.Mechanisms, CostType.Material);
        component.SetCost(HingeMath.AssemblyCost(isBase, s.x, s.y, s.z), MassType.Mechanisms, CostType.Assembly);
        if (Plugin.Detailed && Warnings.Add("mass " + component.GetInstanceID()))
            Plugin.Trace($"[Hinge MASS] component={component.ComponentID} scale={s} steel={kg:F2}kg oldCached={previous:F3} resource={resource:F3} cached={component.CachedMass:F3}; centre-of-mass cache synchronized.");
    }

    internal static void PhysicsSafety(VehicleObjectModel component)
    {
        if (!Own(component)) return;
        component.generateDamageColliders = false;
        // The hidden mount must not become invisible cover for spotting either: drop Detection as well.
        // Selection and PlacementSurface stay so the mount can be picked and built on in the designer.
        var strip = component.ComponentID == MountModel ? ColliderType.Physics | ColliderType.Detection : ColliderType.Physics;
        component.interactionColliderType &= ~strip;
        var colliders = component.Colliders;
        if (colliders == null) return;
        int count = colliders.Cast<Il2CppSystem.Collections.Generic.IReadOnlyCollection<IVehicleCollider>>().Count;
        for (int i = 0; i < count; i++)
        {
            var collider = colliders[i];
            if (collider == null || collider.Owner?.Pointer != component.Pointer) continue;
            var before = collider.Type;
            collider.Type &= ~strip;
            if (before != collider.Type && Plugin.Detailed && Warnings.Add("collider " + component.GetInstanceID() + " " + i))
                Plugin.Trace($"[Hinge] collider component={component.ComponentID} {before} -> {collider.Type}.");
        }
    }

    // ---------- Topology ----------
    internal static VehicleTransform? FindBase(VehicleTransform leaf)
    {
        var parent = leaf.Parent ?? leaf.PlaceTarget;
        for (int i = 0; parent != null && i < 32; i++, parent = parent.Parent)
            if (parent.VehicleObject?.HasTag(BaseTag) == true) return parent;
        return null;
    }
    private static Vector3 PinWorld(VehicleTransform basis) =>
        basis.Position + basis.Rotation * new Vector3(0, HingeMath.PinHeight * Travel.ScaleAxis(basis.Scale.y), 0);

    internal static bool InDesigner() =>
        SceneManager.GetSceneByName("VehicleDesignerUI").isLoaded && !SceneManager.GetSceneByName("VehicleControlUI").isLoaded;
    internal static bool InControl() => SceneManager.GetSceneByName("VehicleControlUI").isLoaded;

    /// <summary>
    /// Editor seating: origin on the pin, aligned with the base (closed pose). The leaf also takes the
    /// base scale; the mount keeps its own scale so a small carrier fits any base.
    /// </summary>
    internal static void Maintain()
    {
        if (!InDesigner()) return;
        foreach (var pair in Leaves.ToArray())
        {
            var leaf = pair.Value;
            if (!leaf.Alive) { Leaves.Remove(pair.Key); ConfigRevision++; continue; }
            if (leaf.Object.PhysicsEnabled) continue;
            leaf.Angle = 0; leaf.RestCaptured = false; leaf.Reapply = false; leaf.TargetOpen = false;
            if (leaf.Hidden) SetHidden(leaf, false);
            var moving = leaf.Object.VehicleTransform;
            var basis = FindBase(moving);
            if (basis == null) { WarnOnce("unattached", "[Hinge] Attach Hinge Leaf or Hinge Mount to a Hinge Base; an unattached one does not move."); continue; }
            bool changed = false;
            if (!leaf.IsMount && moving.Scale != basis.Scale) { moving.Scale = basis.Scale; changed = true; }
            if (Quaternion.Angle(moving.Rotation, basis.Rotation) > .001f) { moving.Rotation = basis.Rotation; changed = true; }
            var pin = PinWorld(basis);
            if (Vector3.Distance(moving.Position, pin) > .00001f) { moving.Position = pin; changed = true; }
            if (changed) moving.NotifyTransformChanged();
            if (!leaf.Fitted && Plugin.Detailed) Plugin.Trace($"[Hinge] {leaf.Kind} {leaf.Id} seated on base {basis.VehicleObject.GetInstanceID()} scale={moving.Scale}.");
            leaf.Fitted = true;
        }
    }

    // ---------- Pose ----------
    internal static void CaptureRest(VehicleObject obj)
    {
        if (obj == null || !Leaves.TryGetValue(obj.GetInstanceID(), out var leaf)) return;
        if (leaf.RestCaptured && leaf.Angle != 0) ApplyPose(leaf, 0, "recapture");
        var t = obj.VehicleTransform; var basis = FindBase(t);
        if (basis == null) { leaf.RestCaptured = false; return; }
        var inverse = Quaternion.Inverse(basis.Rotation);
        leaf.RestRotation = inverse * t.Rotation;
        leaf.RestOffset = inverse * (t.Position - PinWorld(basis));
        leaf.Angle = 0; leaf.RestCaptured = true; leaf.Reapply = false; leaf.PoseLogged = false; leaf.PoseSamples = 0;
        leaf.Vehicle = obj.Vehicle?.Behaviour?.TryCast<VehicleBehaviour>()?.Pointer ?? IntPtr.Zero;
        nextMembership = 0;
        if (Plugin.Detailed) Plugin.Trace($"[Hinge POSE] rest captured leaf={leaf.Id} base={basis.VehicleObject.GetInstanceID()} restRot={leaf.RestRotation.eulerAngles} pinOffset={leaf.RestOffset} vehicle={leaf.Vehicle}.");
    }

    /// <summary>Absolute pose from the base and the stored rest; never a delta on the current transform.</summary>
    internal static void ApplyPose(Leaf leaf, float angle, string reason)
    {
        if (!leaf.RestCaptured || !leaf.Alive) return;
        var t = leaf.Object.VehicleTransform; var basis = FindBase(t);
        if (basis == null) return;
        var rest = leaf;
        var leader = TwinLeader(leaf.Object);
        if (leader != null && Leaves.TryGetValue(leader.GetInstanceID(),out var primary) && primary.RestCaptured) rest = primary;
        var swing = Quaternion.AngleAxis(HingeMath.LocalRotationDegrees(angle), Vector3.right);
        var rotation = basis.Rotation * swing * rest.RestRotation;
        var position = PinWorld(basis) + basis.Rotation * (swing * rest.RestOffset);
        bool log = Plugin.Detailed && !leaf.PoseLogged && angle != 0;
        if (log) LogPose(leaf, "before", reason);
        bool changed = false;
        if (Quaternion.Angle(t.Rotation, rotation) > .0005f) { t.Rotation = rotation; changed = true; }
        if (Vector3.Distance(t.Position, position) > .00001f) { t.Position = position; changed = true; }
        if (changed) t.NotifyTransformChanged();
        if (log) { LogPose(leaf, "after", reason); leaf.PoseLogged = true; }
    }

    /// <summary>Measuring point 3: leaf, children and body COM before/after the first rotation.</summary>
    private static void LogPose(Leaf leaf, string when, string reason)
    {
        var t = leaf.Object.VehicleTransform;
        var body = leaf.Object.Vehicle?.Behaviour?.TryCast<VehicleBehaviour>()?.Rigidbody;
        string inBody(Vector3 p) => body != null ? body.transform.InverseTransformPoint(p).ToString("F3") : "n/a";
        Plugin.Trace($"[Hinge POSE] {when} reason={reason} leaf={leaf.Id} angle={leaf.Angle:F1} rot={t.Rotation.eulerAngles} localRot={t.LocalRotation.eulerAngles} pos={t.Position} inBody={inBody(t.Position)} unityRot={t.transform.rotation.eulerAngles} bodyCOM={(body != null ? body.centerOfMass.ToString("F3") : "n/a")} mass={(body != null ? body.mass : 0):F1}");
        var children = t.Children;
        if (children == null) return;
        int n = children.Cast<Il2CppSystem.Collections.Generic.IReadOnlyCollection<VehicleTransform>>().Count;
        for (int i = 0; i < n && i < 8; i++)
        {
            var c = children[i];
            if (c == null) continue;
            Plugin.Trace($"[Hinge CHILD] {when} leaf={leaf.Id} child={c.VehicleObject?.GetInstanceID()} guid={c.VehicleObject?.GUID} pos={c.Position} inBody={inBody(c.Position)} rot={c.Rotation.eulerAngles} unityPos={c.transform.position} unityParent={c.transform.parent?.name}");
        }
    }

    internal static void Restore(VehicleObject obj, string reason, bool keepAngle)
    {
        if (obj == null || !Leaves.TryGetValue(obj.GetInstanceID(), out var leaf) || !leaf.RestCaptured) return;
        if (leaf.Angle == 0 && !leaf.Reapply) return;
        ApplyPose(leaf, 0, reason);
        if (keepAngle) leaf.Reapply = leaf.Angle != 0;
        else { leaf.Angle = 0; leaf.Reapply = false; }
        if (Plugin.Detailed && restoreTraces++ < 40) Plugin.Trace($"[Hinge POSE] restored rest for {reason} leaf={leaf.Id} keepAngle={keepAngle} angle={leaf.Angle:F1} (frame {Time.frameCount}).");
    }

    internal static void Designer(VehicleObject obj)
    {
        TurnOffExternal(obj, "designer");
        Restore(obj, "designer", false);
        if (obj != null && Leaves.TryGetValue(obj.GetInstanceID(), out var leaf))
        {
            leaf.RestCaptured = false; leaf.Fitted = false; leaf.TargetOpen = false;
            if (leaf.IsMount) SetHidden(leaf, false);
        }
    }

    /// <summary>
    /// Mount visibility through the model's own IVehicleModel.SetVisible (the call Relikt ERA uses for spent
    /// cells). Only the mount's renderer: parts built on it have their own models and stay visible.
    /// </summary>
    internal static void SetHidden(Leaf leaf, bool hidden)
    {
        var m = leaf.Model;
        leaf.Hidden = hidden;
        leaf.NextHide = Time.unscaledTime + 2f;
        if (m == null || m.WasCollected || !m.HasModel) return;
        try
        {
            m.Model?.SetVisible(hidden ? false : m.visible);
            if (Plugin.Detailed && Warnings.Add($"mount {(hidden ? "hidden" : "shown")} {leaf.Id}"))
                Plugin.Trace($"[Hinge MOUNT] mount={leaf.Id} {(hidden ? "hidden for play" : "visible in designer")} (SetVisible({(hidden ? "false" : m.visible.ToString())})).");
        }
        catch (Exception ex) { Warn("Mount visibility", ex); }
    }
    internal static void Remove(VehicleObject obj)
    {
        if (obj == null) return;
        TurnOffExternal(obj, "release");
        PartPaint.Release(obj);
        int id = obj.GetInstanceID();
        if (Leaves.Remove(id)) ConfigRevision++;
        PendingConfigs.Remove(id);
        KnownExternalParts.Remove(id);
        if (FlowParts.Remove(id, out var p)) p.Session?.Storage.Detach(p.Key);
        var partGo = ExternalPartObject(obj);
        if (partGo != null) IoApiCore.ForgetPart(partGo);
    }

    /// <summary>M4: one apply(part, false) for an external output that was still on.</summary>
    private static void TurnOffExternal(VehicleObject obj, string reason)
    {
        foreach (var io in Vehicles.Values.ToArray())
            foreach (var ext in io.External.ToArray())
            {
                try
                {
                    if (ext.Disabled || !ext.ConfigLoaded || !ext.Alive) continue;
                    if (ext.Component.VehicleObject == null || ext.Component.VehicleObject.Pointer != obj.Pointer) continue;
                    if (!ext.Power.TurnOff()) continue;
                }
                catch { continue; }
                try { ApplyExternal(io, ext, false); }
                catch (Exception ex) { Warn("external off " + reason, ex); }
            }
    }

    // ---------- Persistence: own object-state key, JSON string ----------
    /// <summary>Adapter so the merge itself stays Unity-free and testable (Io/StateMerge.cs).</summary>
    private sealed class SerializationStateBag : IStateBag<Il2CppSystem.Type>
    {
        private readonly SerializationInfo state;
        internal SerializationStateBag(SerializationInfo state) { this.state = state; }
        public int Count => state.MemberCount;
        public string NameAt(int index) => state.m_members[index];
        public string? ValueAt(int index)
        {
            try { return state.GetString(state.m_members[index]); } catch { return null; }
        }
        public void Set(int index, string key, string value, Il2CppSystem.Type type) => state.UpdateValue(key, value, type);
        public void Add(string key, string value, Il2CppSystem.Type type) => state.AddValue(key, value, type);
    }

    internal static void Save(VehicleObject obj, VehicleObjectBlueprint result)
    {
        if (result == null || obj == null || obj.Released) return;
        bool flowPart = IsFlowPart(obj), external = IsExternalPart(obj);
        GameObject? partGo = external ? ExternalPartObject(obj) : null;
        // A v1 rules output keeps exactly its v1 save unless it actually holds API data.
        bool externalData = partGo != null && IoApiCore.PartDataJson(partGo) != null;
        if (!flowPart && !externalData) return;
        Leaves.TryGetValue(obj.GetInstanceID(), out var leaf);
        // A null State is legal (0.7.1): give it a fresh SerializationInfo instead of skipping the save.
        result.State ??= new SerializationInfo(Il2CppType.Of<Il2CppSystem.Object>(),
            new Il2CppSystem.Runtime.Serialization.FormatterConverter().Cast<Il2CppSystem.Runtime.Serialization.IFormatterConverter>());
        string json = FlowParts.TryGetValue(obj.GetInstanceID(), out var prior) && prior.LegacyRaw != null ? prior.LegacyRaw : leaf != null ? HingeConfigJson.Write(leaf.Config) : "";
        // In-place merge: every other entry keeps its name, value and type; only our key is set.
        bool replaced = json.Length > 0 && StateMerge.Merge(new SerializationStateBag(result.State), StateKey, json, Il2CppType.Of<string>());
        if (flowPart) SaveFlow(obj, result.State);
        if (externalData)
        {
            // v1.1: one extra state key per API part, including v1 rules outputs (their
            // loadRules/saveRules behaviour is unchanged). Managed outputs also got ioFlowV1 above.
            string component = ExternalComponentId(obj);
            if (component.Length > 0) IoApiCore.AttachPart(partGo!, component);
            ExternalPartState.WriteData(new SerializationStateBag(result.State), Il2CppType.Of<string>(), partGo!);
        }
        if (Plugin.Detailed && saveTraces++ < 300)
            Plugin.Trace($"[IO SAVE] leaf={obj.GetInstanceID()} flow={flowPart} external={external} data={externalData} key={StateKey} replaced={replaced} entries={result.State.MemberCount} links={leaf?.Config.Links.Count ?? 0} json={json}");
    }

    internal static void Load(VehicleObject? obj, SerializationInfo? info, string path)
    {
        if (obj == null || info == null) return;
        bool flowPart = IsFlowPart(obj), external = IsExternalPart(obj);
        if (!flowPart && !external) return;
        if (flowPart) LoadFlow(obj, info);
        if (external) LoadExternalData(obj, info);
        if (!flowPart) return;
        if (!IsMoving(obj)) return;
        string? json = null;
        var e = info.GetEnumerator();
        while (e.MoveNext()) if (e.Name == StateKey) { json = info.GetString(StateKey); break; }
        if (json == null)
        {
            if (Plugin.Detailed && ioTraces++ < 80) Plugin.Trace($"[IO LOAD] path={path} object={obj.GetInstanceID()} no {StateKey} key (new hinge or older save).");
            return;
        }
        int id = obj.GetInstanceID();
        if (Leaves.TryGetValue(id, out var leaf)) ApplyLoaded(leaf, json, path);
        else PendingConfigs[id] = json;
    }

    private static void LoadExternalData(VehicleObject obj, SerializationInfo info)
    {
        var component = ExternalComponent(obj);
        if (component == null) return;
        ExternalPartState.ReadData(new SerializationStateBag(info), component.gameObject, component.ComponentID);
    }

    private static void ApplyLoaded(Leaf leaf, string json, string path)
    {
        leaf.Config = HingeConfigJson.Parse(json);
        ConfigRevision++;
        if (Plugin.Detailed && ioTraces++ < 80) Plugin.Trace($"[IO LOAD] path={path} leaf={leaf.Id} name='{leaf.Config.Name}' links={leaf.Config.Links.Count} kept={leaf.Config.PreservedLinks.Count} readOnly={leaf.Config.ReadOnly}");
    }

    // ---------- External outputs (public I/O API) ----------

    /// <summary>
    /// Main-thread discovery and loadRules pass. Called from HingeHost.Update, never from a
    /// Harmony hook: external loadRules/saveRules callbacks must not run inside a native patch.
    /// </summary>
    internal static void PrepareExternal()
    {

        RefreshMembership();
        PrepareKnownExternalVehicles();
        // External outputs can exist on a vehicle without a single hinge (and therefore without a
        // leaf that would put it in Vehicles). Discover those vehicles from the scene on a slow
        // timer; the old per-vehicle GameUpdate hook used to do this implicitly.
        if ((IoApiCore.OutputRegistrations.Count > 0 || FlowParts.Count > 0) && (InControl() || HingePanel.IsOpen && InDesigner())) DiscoverVehicles();
        foreach (var pair in Vehicles.ToArray())
        {
            var io = pair.Value;
            if (io.Vehicle == null || io.Vehicle.WasCollected)
            {
                if (io.Vehicle != null) { IoApiCore.ResetVehicle(io.Vehicle.gameObject); HingeFire.Discard(pair.Key); }
                Vehicles.Remove(pair.Key);
                continue;
            }
            if (Time.unscaledTime >= io.NextExternalScan)
            {
                ScanExternal(io);
                io.NextExternalScan = Time.unscaledTime + 2f;
            }
            foreach (var ext in io.External.ToArray())
            {
                if (!ext.Alive) { ext.DetachFlow?.Invoke(); io.External.Remove(ext); ConfigRevision++; continue; }
                if (!ext.ConfigLoaded && !ext.Disabled) LoadExternal(io, ext);
            }
            if (io.Leaves.Count == 0 && io.External.Count == 0 && !HasFlowParts(io.Vehicle)) { HingeFire.Discard(pair.Key); Vehicles.Remove(pair.Key); }
        }
    }

    private static float nextVehicleScan;
    private static void DiscoverVehicles()
    {
        if (Time.unscaledTime < nextVehicleScan) return;
        nextVehicleScan = Time.unscaledTime + 2f;
        var found = UnityEngine.Object.FindObjectsOfType<VehicleBehaviour>();
        foreach (var vehicle in found)
        {
            if (vehicle == null || vehicle.WasCollected) continue;
            if (!Vehicles.TryGetValue(vehicle.Pointer, out var io))
            {
                Vehicles[vehicle.Pointer] = io = new VehicleIo { Vehicle = vehicle };
                IoApiCore.ResetVehicle(vehicle.gameObject);
            }
            else if (io.Vehicle == null) io.Vehicle = vehicle;
        }
    }

    /// <summary>
    /// Load external rules in the managed host phase, outside native hooks.
    /// </summary>
    private static void LoadExternal(VehicleIo io, ExternalOutput ext)
    {
        ext.ConfigLoaded = true;
        GameObject? vehicleGo = io.Vehicle?.gameObject;
        GameObject? partGo = ext.Component.gameObject;
        if (vehicleGo == null || partGo == null || !ext.Alive) { ext.Disabled = true; ConfigRevision++; return; }
        if (!IoApiCore.TryLoadRules(ext.RegistrationId, vehicleGo, partGo, out string? json))
        {
            ext.Disabled = true; ConfigRevision++;
            return;
        }
        if (!string.IsNullOrWhiteSpace(json))
        {
            try
            {
                using var envelope = System.Text.Json.JsonDocument.Parse(json);
                if (envelope.RootElement.TryGetProperty("flow", out _))
                {
                    var saved = FlowJson.ReadEnvelope(json); ext.PartId = saved.PartId; ext.FlowCopy = saved.Flow;
                    TraceFlow("load API", saved.Flow, saved.PartId);
                    ext.Power.Reset(); ConfigRevision++; return;
                }
            }
            catch (Exception ex) { Warn("External flow load", ex); }
        }
        var config = string.IsNullOrWhiteSpace(json)
            ? new HingeConfig { Name = ext.DisplayName, OutputType = ext.RegistrationId }
            : HingeConfigJson.Parse(json);
        if (!config.ReadOnly)
        {
            // The external output has no configurable parameters; keep the stored links and name.
            config.OutputType = ext.RegistrationId;
            config.OutputParams.Clear();
        }
        ext.Config = config;
        ext.Power.Reset();
        ConfigRevision++;
        if (Plugin.Detailed)
            Plugin.Trace($"[IO EXT] load id={ext.RegistrationId} part={ext.Component.GetInstanceID()} rules={config.Links.Count} kept={config.PreservedLinks.Count} readOnly={config.ReadOnly}");
    }

    internal static void SaveExternal(ExternalOutput ext)
    {
        if (ext == null || ext.Disabled || ext.Config.ReadOnly) return;
        var vehicleGo = VehicleGo(ext);
        var partGo = ext.Component.gameObject;
        if (vehicleGo == null || partGo == null) return;
        string json = HingeConfigJson.Write(ext.Config);
        if (!IoApiCore.TrySaveRules(ext.RegistrationId, vehicleGo, partGo, json))
        {
            ext.Disabled = true; ConfigRevision++;
            return;
        }
        if (Plugin.Detailed)
            Plugin.Trace($"[IO EXT] save id={ext.RegistrationId} part={ext.Component.GetInstanceID()} links={ext.Config.Links.Count} json={json}");
    }

    private static GameObject? VehicleGo(ExternalOutput ext)
    {
        var io = IoFor(ext.Vehicle);
        if (io?.Vehicle != null) return io.Vehicle.gameObject;
        try { return ext.Component.Vehicle?.gameObject; } catch { return null; }
    }

    private static void ApplyExternal(VehicleIo io, ExternalOutput ext, bool on)
    {
        if (ext.Disabled) return;
        var vehicleGo = io.Vehicle?.gameObject ?? VehicleGo(ext);
        var partGo = ext.Component.gameObject;
        if (vehicleGo == null || partGo == null) return;
        if (!IoApiCore.TryApply(ext.RegistrationId, vehicleGo, partGo, on))
        {
            ext.Disabled = true; ConfigRevision++;
            return;
        }
        ext.Power.Applied(on);
        if (Plugin.Detailed && ExtTraces++ < 300)
            Plugin.Trace($"[IO EXT] apply id={ext.RegistrationId} part={ext.Component.GetInstanceID()} on={on}.");
    }
    private static int ExtTraces;

    // ---------- Per-vehicle I/O tick ----------

    /// <summary>
    /// Own-MonoBehaviour tick (HingeHost.LateUpdate, after native Update). Replaces the per-vehicle
    /// VehicleBehaviour.GameUpdate postfix: iterate our registered vehicles, including AI vehicles.
    /// </summary>
    internal static void TickAll()
    {
        if (!AnyLeaves) return;
        // The old postfix only ran while the vehicle game updated. Keep that context: play only,
        // not the designer, and not while paused (LateUpdate still runs then).
        if (!InControl() || GameState.Paused() || Time.deltaTime <= 0) return;
        RefreshMembership();
        foreach (var io in Vehicles.Values.ToArray()) TickVehicle(io);
    }

    private static void TickVehicle(VehicleIo io)
    {
        var vehicle = io.Vehicle;
        if (vehicle == null || vehicle.WasCollected) return;
        if (io.LastFrame == Time.frameCount) return;
        io.LastFrame = Time.frameCount;
        if (io.Leaves.Count == 0 && io.External.Count == 0 && !HasFlowParts(io.Vehicle)) { HingeFire.Discard(vehicle.Pointer); return; }
        float dt = Time.deltaTime;
        double now = (SessionFor(io)?.Clock ?? io.Clock).Advance(dt);
        io.Context.Begin(vehicle, now, io.Leaves.Where(l => l.Alive && l.Object.PhysicsEnabled).Select(l => l.State()));
        try { TickFlow(io, now); }
        finally { io.Context.End(); }
        RotatorRuntime.TickVehicle(vehicle, now, dt);
        foreach (var leaf in io.Leaves.OrderBy(l => TwinLeader(l.Object) == null ? 0 : 1))
        {
            if (!leaf.Alive || !leaf.Object.PhysicsEnabled) continue;
            if (leaf.IsMount && (!leaf.Hidden || Time.unscaledTime >= leaf.NextHide)) SetHidden(leaf, true);
            if (leaf.FlowNode == null) continue;
            if (!leaf.RestCaptured) { CaptureRest(leaf.Object); if (!leaf.RestCaptured) continue; }
            bool open = FlowTarget(io, leaf.Object);
            leaf.TargetOpen = open;
            float target = open ? leaf.FlowFloat("openAngle", 90) : leaf.FlowFloat("closedAngle", 0);
            var twinLeader = TwinLeader(leaf.Object);
            float next = twinLeader != null && Leaves.TryGetValue(twinLeader.GetInstanceID(),out var primaryLeaf)
                ? primaryLeaf.Angle : HingeMath.StepAngle(leaf.Angle, target, leaf.FlowFloat("speed", 90), dt);
            if (next != leaf.Angle || leaf.Reapply || twinLeader != null)
            {
                bool stop = next == target && leaf.Angle != target;
                leaf.Angle = next; leaf.Reapply = false;
                ApplyPose(leaf, next, "io");
                if (stop && Plugin.Detailed && leaf.PoseSamples++ < 20)
                    Plugin.Trace($"[Hinge] leaf={leaf.Id} reached {(open ? "open" : "closed")} angle={next:F1} bodyCOM={vehicle.Rigidbody?.centerOfMass.ToString("F3")} leafInBody={(vehicle.Rigidbody != null ? vehicle.Rigidbody.transform.InverseTransformPoint(leaf.Object.VehicleTransform.Position).ToString("F3") : "n/a")}.");
            }
        }
        TickExternal(io);
    }

    private static void TickExternal(VehicleIo io)
    {
        foreach (var ext in io.External)
        {
            if (!ext.Alive || ext.Disabled || !ext.ConfigLoaded) continue;
            bool open = FlowTarget(io, ext);
            if (ext.Power.ShouldApply(open)) ApplyExternal(io, ext, open);
        }
    }

    internal static VehicleIo? IoFor(IntPtr vehicle) => Vehicles.TryGetValue(vehicle, out var io) ? io : null;

    private static void RefreshMembership(bool force = false)
    {
        if (!force && Time.unscaledTime < nextMembership) return;
        nextMembership = Time.unscaledTime + 1f;
        foreach (var io in Vehicles.Values) io.Leaves.Clear();
        foreach (var pair in Leaves.ToArray())
        {
            var leaf = pair.Value;
            if (!leaf.Alive) { Leaves.Remove(pair.Key); ConfigRevision++; continue; }
            var vehicle = leaf.Object.Vehicle?.Behaviour?.TryCast<VehicleBehaviour>();
            if (leaf.Vehicle == IntPtr.Zero) leaf.Vehicle = vehicle?.Pointer ?? IntPtr.Zero;
            RefreshVuid(leaf);
            if (leaf.Vehicle == IntPtr.Zero) continue;
            if (!Vehicles.TryGetValue(leaf.Vehicle, out var io))
            {
                Vehicles[leaf.Vehicle] = io = new VehicleIo { Vehicle = vehicle };
                if (vehicle != null) IoApiCore.ResetVehicle(vehicle.gameObject);
            }
            else if (io.Vehicle == null) io.Vehicle = vehicle;
            io.Leaves.Add(leaf);
        }
        foreach (var gone in Vehicles.Where(p => p.Value.Leaves.Count == 0 && p.Value.External.Count == 0 && !HasFlowParts(p.Value.Vehicle)).Select(p => p.Key).ToArray()) { HingeFire.Discard(gone); Vehicles.Remove(gone); }
        foreach (var io in Vehicles.Values) io.Leaves.Sort((a, b) => a.Id.CompareTo(b.Id));
    }

    /// <summary>Object VUID: the stable key of a hinge for hinge.state chains (0 until the game assigns it).</summary>
    internal static void RefreshVuid(Leaf leaf)
    {
        try { leaf.Vuid = leaf.Object.VUID.Value; } catch { leaf.Vuid = 0; }
    }

    internal static void ResetAll()
    {
        foreach (var leaf in Leaves.Values.ToArray())
            try
            {
                if (leaf.Alive && leaf.RestCaptured && leaf.Angle != 0) { ApplyPose(leaf, 0, "unload"); leaf.Angle = 0; }
                if (leaf.Alive && leaf.Hidden) SetHidden(leaf, false);
            }
            catch (Exception ex) { Warn("Reset", ex); }
    }
}
