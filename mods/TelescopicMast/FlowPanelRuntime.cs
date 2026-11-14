using Il2CppInterop.Runtime;
using Sprocket.Vehicles;
using Sprocket.Vehicles.Control;
using SprocketTelescopicMast.Io;
namespace SprocketTelescopicMast;

internal sealed class FlowPanelData
{
    internal object Token = null!;
    internal FlowDocument Flow = null!;
    internal FlowEngine Engine = null!;
    internal List<FlowPart> Parts = new();
    internal Func<string, bool> Exists = _ => false;
    internal Func<FlowNode, double> Position = _ => 0;
    internal Func<FlowNode, string?> TwinHint = _ => null;
    internal Func<FlowNode, SettingsBinding?> Settings = _ => null;
    internal Action Save = () => { };
}

internal static partial class HingeRuntime
{
    internal static FlowPanelData? PanelFlow(bool designer, object? preferred)
    {
        var candidates = FlowParts.Values.Where(p => p.Object != null && !p.Object.WasCollected && !p.Object.Released && p.Session != null)
            .Select(p => (Object: p.Object, Session: p.Session!))
            .Concat(Vehicles.Values.SelectMany(io => io.External).Where(e => e.Alive && e.ConfigLoaded && !e.Disabled)
                .Select(e => (Object: e.Component.VehicleObject, Session: SessionFor(e.Component.VehicleObject)!)))
            .Where(p => p.Session?.Storage.Flow != null);
        var available = candidates.Where(p => designer ? !p.Object.PhysicsEnabled :
            p.Object.Vehicle?.Behaviour?.TryCast<VehicleBehaviour>()?.ControlType == ControlType.Player).ToArray();
        var chosen = available.FirstOrDefault(p => p.Session == preferred);
        if (chosen.Session == null) chosen = available.FirstOrDefault();
        var session = chosen.Session;
        // Keep the graph when its last output part is removed, while the vehicle still exists.
        if (session == null && preferred is FlowSession old && old.Owner != null && !old.Owner.WasCollected &&
            (designer || old.Owner.ControlType == ControlType.Player)) session = old;
        if (session?.Storage.Flow == null) return null;
        var native = FlowParts.Values.Where(p => p.Session == session && p.Object != null && !p.Object.Released && !p.Object.WasCollected).OrderBy(p => p.Key).ToArray();
        var parts = new List<FlowPart>();
        var external = Vehicles.Values.SelectMany(io => io.External).Where(e => e.Alive && e.ConfigLoaded && !e.Disabled &&
            SessionFor(e.Component.VehicleObject) == session).ToArray();
        var pending = new List<(NativeFlowPart Part, ExternalOutputRegistration Registration, VehicleComponent Component)>();
        int hingeIndex = 0, mastIndex = 0, rotatorIndex = 0;
        foreach (var p in native.Where(p => p.TwinPrimary == null))
        {
            // Designer vehicles have no runtime VehicleBehaviour. Their hook-known carriers
            // can be listed from the registration without discovery or foreign callbacks.
            if (p.Managed)
            {
                if (external.Any(e => e.PartId == p.PartId)) continue;
                var component = ExternalComponent(p.Object);
                var registration = component == null ? null : IoApiCore.FindByComponent(component.ComponentID);
                if (registration != null && p.PartId.Length > 0) pending.Add((p, registration, component!));
                else if (registration == null && p.PartId.Length > 0)
                    WarnOnce("managed unlisted " + p.Object.GetInstanceID(), "[IO EXT] managed part not listed: " +
                        p.PartId + " has no registered ComponentID");
                continue;
            }
            if (Leaves.TryGetValue(p.Object.GetInstanceID(), out var leaf))
            {
                RefreshVuid(leaf); string title = leaf.DisplayName(hingeIndex++);
                parts.Add(new(p.PartId, "hinge", title, "", PartSelector.ForVuid(leaf.Vuid, title)));
            }
            else if (RotatorRuntime.Has(p.Object)) parts.Add(new(p.PartId, "rotator", "Rotator " + ++rotatorIndex));
            else if (MastRuntime.IsFlowOutput(p.Object)) parts.Add(new(p.PartId, "mast", "Mast " + ++mastIndex));
        }
        foreach (var group in pending.GroupBy(p => p.Registration.Id))
        {
            var used = external.Where(e => e.RegistrationId == group.Key).Select(e => e.Sequence).ToHashSet();
            int sequence = 0;
            foreach (var item in group.OrderBy(p => p.Component.GetInstanceID()))
            {
                do { sequence++; } while (used.Contains(sequence));
                string title = IoApiCore.ExternalLabel(PartNameOf(item.Component), item.Registration.DisplayName, sequence);
                parts.Add(new(item.Part.PartId, item.Registration.Id, title, item.Registration.Provider));
            }
        }
        foreach (var ext in external.Where(e => !native.Any(p => p.PartId == e.PartId && p.TwinPrimary != null)))
            parts.Add(new(ext.PartId, ext.RegistrationId, ext.Label, IoApiCore.FindOutput(ext.RegistrationId)?.Provider ?? ext.DisplayName));
        if (designer)
        {
            var apiParts = parts.Where(p => IoApiCore.FindOutput(p.Type) != null).OrderBy(p => p.Id).ToArray();
            string signature = string.Join("|", apiParts.Select(p => p.Id + ":" + p.Type + ":" + p.Title));
            if (signature != session.DesignerListSignature)
            {
                session.DesignerListSignature = signature;
                if (Plugin.Detailed) Plugin.Trace($"[IO EXT] designer list parts={apiParts.Length} registration=" +
                    string.Join(",", apiParts.GroupBy(p => p.Type).OrderBy(g => g.Key).Select(g => g.Key + ":" + g.Count())));
            }
        }
        // Source choices are scoped to exactly the displayed vehicle.
        var groups = new Dictionary<string, int>();
        var masks = new Dictionary<string, int>();
        var root = chosen.Object?.Vehicle?.Pointer ?? Flows.First(p => p.Value == session).Key;
        IoCatalog.Cannons = HingeCannons.Options(new HashSet<IntPtr> { root }, groups, masks);
        IoCatalog.CannonFireGroups = groups;
        IoCatalog.CannonFireMasks = masks;
        IoCatalog.Hinges = native.Where(p => p.TwinPrimary == null && Leaves.ContainsKey(p.Object.GetInstanceID())).Select(p =>
        {
            var leaf = Leaves[p.Object.GetInstanceID()]; RefreshVuid(leaf);
            var title = parts.First(x => x.Id == p.PartId).Title;
            return new ParamOption(PartSelector.ForVuid(leaf.Vuid, title), title);
        }).ToArray();
        IoCatalog.Rotators = parts.Where(p => p.Type == "rotator").Select(p => new ParamOption(p.Id,p.Title)).ToArray();
        return new FlowPanelData
        {
            Token = session, Flow = session.Storage.Flow!, Engine = session.Engine, Parts = parts,
            Settings = n =>
            {
                var registration = IoApiCore.FindOutput(n.Type);
                var settings = PartSettings.Find(registration?.ComponentId);
                if (settings == null) return null;
                var carrier = native.FirstOrDefault(p => p.PartId == n.PartId);
                var selected = carrier == null ? external.FirstOrDefault(e => e.PartId == n.PartId)?.Component.gameObject : ExternalPartObject(carrier.Object);
                if (selected == null) return null;
                var targets = new List<(object Part,string Id)> { (selected,n.PartId) };
                if (carrier != null)
                    foreach (var twin in native.Where(p => p.TwinPrimary == carrier && !TwinOwnWiring(p)))
                        if (ExternalPartObject(twin.Object) is { } go) targets.Add((go,twin.PartId));
                foreach (var t in targets) PartSettings.Ensure(t.Part);
                return new SettingsBinding { Registration = settings, Targets = targets };
            },
            Exists = id => parts.Any(p => p.Id == id) || native.Any(p => p.TwinPrimary != null && p.PartId == id),
            TwinHint = n =>
            {
                var p = native.FirstOrDefault(p => p.PartId == n.PartId);
                if (p?.TwinPrimary == null) return null;
                string title = parts.FirstOrDefault(part => part.Id == p.TwinPrimary.PartId)?.Title ?? n.Type;
                return "twin of " + title + ", can be removed";
            },
            Position = n =>
            {
                var p = native.FirstOrDefault(p => p.PartId == n.PartId);
                if (p == null) return 0;
                if (Leaves.TryGetValue(p.Object.GetInstanceID(), out var leaf)) return leaf.Angle;
                if (RotatorRuntime.Has(p.Object)) return RotatorRuntime.Angle(p.Object);
                return MastRuntime.FlowPosition(p.Object);
            },
            Save = () =>
            {
                session.HasStoredFlow = true;
                session.Storage.Publish(session.Storage.Flow!);
                foreach (var p in native)
                { p.Copy = session.Storage.Flow; TraceFlow("save edit", session.Storage.Flow!, p.PartId); }
                // Same managed phase-A path writes the latest envelope to every API part.
                PrepareFlows();
            }
        };
    }
}
