using Il2CppInterop.Runtime;
using Sprocket.Vehicles;
using SprocketTelescopicMast.Io;
namespace SprocketTelescopicMast;

internal static partial class HingeRuntime
{
    // Hook paths only retain objects. Foreign callbacks remain in the managed host phase.
    private static readonly Dictionary<int, VehicleObject> KnownExternalParts = new();
    private static bool LiveExternalObject(VehicleObject? obj) => obj != null && !obj.WasCollected && !obj.Released;
    private static void RememberExternalPart(VehicleObject obj)
    {
        if (LiveExternalObject(obj)) KnownExternalParts[obj.GetInstanceID()] = obj;
    }
    private static IEnumerable<VehicleObject> KnownExternalObjects() => KnownExternalParts.Values
        .Concat(FlowParts.Values.Select(p => p.Object)).Concat(Leaves.Values.Select(l => l.Object));

    private static void PrepareKnownExternalVehicles()
    {
        foreach (var pair in KnownExternalParts.ToArray())
            if (!LiveExternalObject(pair.Value)) KnownExternalParts.Remove(pair.Key);
        foreach (var obj in KnownExternalObjects().Where(LiveExternalObject).ToArray())
        {
            var vehicle = obj.Vehicle?.Behaviour?.TryCast<VehicleBehaviour>();
            if (vehicle == null || vehicle.WasCollected) continue;
            if (!Vehicles.ContainsKey(vehicle.Pointer))
            { Vehicles[vehicle.Pointer] = new VehicleIo { Vehicle = vehicle }; IoApiCore.ResetVehicle(vehicle.gameObject); }
        }
    }

    private static List<VehicleObject> ExternalSources(VehicleIo io, out int readerCount, out int knownCount)
    {
        var sources = new Dictionary<int, VehicleObject>();
        var vehicle = io.Vehicle!;
        bool Belongs(VehicleObject obj) => LiveExternalObject(obj) &&
            obj.Vehicle?.Behaviour?.TryCast<VehicleBehaviour>()?.Pointer == vehicle.Pointer;
        var readerIds = new HashSet<int>();
        try
        {
            // The generic behaviour reader enumerates *components*, not VehicleObjects.
            // VehicleObjectRegister implements both interfaces; Items is the object list.
            var items = vehicle.VehicleInfo?.ObjectReader?.TryCast<IVehicleObjectReader>()?.Items;
            if (items != null)
                for (int i = 0; i < items.Cast<Il2CppSystem.Collections.Generic.IReadOnlyCollection<VehicleObject>>().Count; i++)
                    if (Belongs(items[i])) { sources[items[i].GetInstanceID()] = items[i]; readerIds.Add(items[i].GetInstanceID()); }
        }
        catch (Exception ex) { WarnOnce("external reader " + vehicle.Pointer, $"[IO] Vehicle object reader unavailable: {ex.Message}"); }
        readerCount = readerIds.Count;
        var knownIds = new HashSet<int>();
        foreach (var obj in KnownExternalObjects().ToArray())
            if (Belongs(obj)) { sources[obj.GetInstanceID()] = obj; knownIds.Add(obj.GetInstanceID()); }
        knownCount = knownIds.Count;
        return sources.Values.ToList();
    }

    private static void ScanExternal(VehicleIo io)
    {
        var vehicle = io.Vehicle;
        if (vehicle == null) return;
        var registrations = IoApiCore.OutputRegistrations;
        if (registrations.Count == 0)
        {
            if (io.External.Count > 0) { foreach (var old in io.External) old.DetachFlow?.Invoke(); io.External.Clear(); ConfigRevision++; }
            return;
        }
        var sources = ExternalSources(io, out int readerCount, out int knownCount);
        var found = new List<(ExternalOutputRegistration Registration, VehicleComponent Component)>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var registration in registrations)
        {
            try
            {
                foreach (var component in FindParts(sources, registration.ComponentId))
                {
                    string id = "ext#" + registration.Id + "#" + component.GetInstanceID();
                    if (seen.Add(id)) found.Add((registration, component));
                }
            }
            catch (Exception ex) { WarnOnce("external parts " + registration.Id, $"[IO] External output '{registration.Id}' scan failed: {ex.Message}"); }
        }

        bool changed = false;
        var keep = new HashSet<string>(found.Select(x => "ext#" + x.Registration.Id + "#" + x.Component.GetInstanceID()), StringComparer.Ordinal);
        foreach (var old in io.External.ToArray())
            if (!old.Alive || !keep.Contains(old.OutputId)) { old.DetachFlow?.Invoke(); io.External.Remove(old); changed = true; }

        var byId = io.External.ToDictionary(x => x.OutputId, StringComparer.Ordinal);
        foreach (var (registration, component) in found)
        {
            string id = "ext#" + registration.Id + "#" + component.GetInstanceID();
            if (byId.ContainsKey(id)) continue;
            var ext = new ExternalOutput(registration, component) { Vehicle = vehicle.Pointer };
            io.External.Add(ext); byId[id] = ext; changed = true;
        }

        // A readable per-registration sequence for the panel ("VFX light 1", "VFX light 2").
        foreach (var group in io.External.GroupBy(x => x.RegistrationId).ToArray())
        {
            int n = 0;
            foreach (var ext in group.OrderBy(x => x.Component.GetInstanceID())) ext.Sequence = ++n;
        }
        if (changed) ConfigRevision++;
        string counts = string.Join(",", registrations.Select(r => r.Id + ":" + found.Count(f => f.Registration.Id == r.Id)));
        // Include identities in the comparison: replacing a part at the same count is a change.
        string signature = readerCount + "|" + knownCount + "|" + counts + "|" + string.Join("|", keep.OrderBy(x => x));
        if (signature != io.ExternalScanSignature)
        {
            io.ExternalScanSignature = signature;
            if (Plugin.Detailed) Plugin.Trace($"[IO EXT] scan vehicle={vehicle.Pointer} reader={readerCount} known={knownCount} found={counts}");
        }
    }

    private static List<VehicleComponent> FindParts(IEnumerable<VehicleObject> sources, string componentId)
    {
        var result = new List<VehicleComponent>();
        var seen = new HashSet<int>();
        void AddObject(VehicleObject? obj)
        {
            if (obj == null || obj.WasCollected || obj.Released) return;
            var components = obj.Components;
            if (components == null) return;
            int count = components.Cast<Il2CppSystem.Collections.Generic.IReadOnlyCollection<VehicleComponent>>().Count;
            for (int i = 0; i < count; i++)
            {
                var component = components[i];
                if (component == null || component.WasCollected || component.ComponentID != componentId) continue;
                if (seen.Add(component.GetInstanceID())) result.Add(component);
            }
        }

        foreach (var obj in sources)
            try { AddObject(obj); }
            catch (Exception ex) { WarnOnce("external object " + obj.GetInstanceID(), "[IO EXT] part scan failed: " + ex.Message); }
        return result;
    }

}
