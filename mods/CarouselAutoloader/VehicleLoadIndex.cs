using HarmonyLib;
using System.Reflection;
using Sprocket.Vehicles;
using Sprocket.Vehicles.AmmunitionStorage;
using Sprocket.Vehicles.Cannons;
using Sprocket.Vehicles.Turrets;
using Sprocket.Vehicles.Weapons;
using Sprocket.WeaponFramework;

namespace SprocketCarouselAutoloader;

// Topology changes during assembly/rebuild, not on every weapon update.
// Cache negative lookups too: ordinary cannons must not scan vehicles each frame.
internal sealed record LoadConnections(BustleState[] Bustles, AmmoRack[] Reserves,
    TurretBasket[] Baskets, CannonBreech? Breech)
{
    internal static readonly LoadConnections Empty = new(Array.Empty<BustleState>(), Array.Empty<AmmoRack>(),
        Array.Empty<TurretBasket>(), null);
}

/// <summary>0.2.28 (M5): a load task resolved once to its cannon and that cannon's connections.</summary>
internal sealed class TaskLink
{
    internal static readonly TaskLink None = new(null, LoadConnections.Empty);
    internal readonly Cannon? Cannon;
    internal readonly LoadConnections Connections;
    internal TaskLink(Cannon? cannon, LoadConnections connections) { Cannon = cannon; Connections = connections; }
}

[HarmonyPatch]
internal static class VehicleLoadIndex
{
    private static readonly Dictionary<IntPtr, LoadConnections> Connections = new();
    // 0.2.28 (M5): early-exit caches for the per-frame hooks, cleared with Connections on every topology change.
    private static readonly PresenceCache<TaskLink> TaskLinks = new();
    private static readonly PresenceCache<TaskLink[]> ControllerLinks = new();
    private static readonly Func<LoadTask, TaskLink?> ResolveTaskLink = ResolveTask;
    private static readonly Func<VehicleWeaponLoadController, TaskLink[]?> ResolveControllerLinks = ResolveController;
    private static int Count<T>(Il2CppSystem.Collections.Generic.IReadOnlyList<T> list) =>
        list.Cast<Il2CppSystem.Collections.Generic.IReadOnlyCollection<T>>().Count;

    internal static LoadConnections Get(Cannon cannon)
    {
        if (Connections.TryGetValue(cannon.Pointer, out var found)) return found;
        // 0.2.28 (M3): a cannon without a vehicle (still assembling or being released) has no connections yet;
        // not cached, so it is resolved again once the vehicle exists. Was a NullReferenceException.
        var vehicle = cannon.Vehicle;
        if (vehicle == null) return LoadConnections.Empty;
        var bustles = new List<BustleState>();
        var reserves = new List<AmmoRack>();
        var baskets = new List<TurretBasket>();
        CannonBreech? breech = null;
        var objects = vehicle.ObjectReader.Items;
        int objectCount = Count(objects);
        for (int i = 0; i < objectCount; i++)
        {
            var parts = objects[i].Components;
            int partCount = Count(parts);
            for (int j = 0; j < partCount; j++)
            {
                var part = parts[j];
                var rack = part.TryCast<AmmoRack>();
                if (rack != null)
                {
                    if (!BustleRuntime.IsPart(rack)) reserves.Add(rack);
                    else if (CarouselRuntime.SameTurret(rack, cannon)) bustles.Add(BustleRuntime.State(rack));
                    continue;
                }
                var basket = part.TryCast<TurretBasket>();
                if (basket != null && CarouselRuntime.SameTurret(basket, cannon)) baskets.Add(basket);
                var candidate = part.TryCast<CannonBreech>();
                if (candidate?.parentCannonVuid.Value == cannon.VUID.Value) breech = candidate;
            }
        }
        bustles.Sort((a, b) => a.Rack.VUID.Value.CompareTo(b.Rack.VUID.Value));
        return Connections[cannon.Pointer] = new(bustles.ToArray(), reserves.ToArray(), baskets.ToArray(), breech);
    }

    // A task whose target is not a cannon is never a candidate (cached). A cannon without its vehicle yet is
    // unknown (not cached): the hooks then run their 0.2.27 path.
    private static TaskLink? ResolveTask(LoadTask task)
    {
        var cannon = task.target?.TryCast<WeaponBehaviour>()?.mount?.TryCast<Cannon>();
        if (cannon == null) return TaskLink.None;
        if (cannon.Vehicle == null) return null;
        return new TaskLink(cannon, Get(cannon));
    }

    private static TaskLink[]? ResolveController(VehicleWeaponLoadController controller)
    {
        var entries = controller.loadTasks;
        if (entries == null) return null;
        // RefreshCapturedLoaders does nothing for more than 128 entries either.
        if (entries.Length > 128) return Array.Empty<TaskLink>();
        var links = new List<TaskLink>();
        for (var i = 0; i < entries.Length; i++)
        {
            var task = entries[i]?.Task?.TryCast<LoadTask>();
            if (task == null) continue;
            var link = Link(task);
            if (link == null) return null;
            links.Add(link);
        }
        return links.ToArray();
    }

    internal static TaskLink? Link(LoadTask task) => TaskLinks.Get(task.Pointer, task, ResolveTaskLink);

    /// <summary>Bustle, revolver or Semi rack in the cannon's turret (unknown counts as true).</summary>
    internal static bool HasBustles(LoadTask task)
    {
        var link = Link(task);
        return link == null || link.Connections.Bustles.Length > 0;
    }

    /// <summary>Any Carousel autoloader for this task's cannon (unknown counts as true).</summary>
    internal static bool HasLoader(LoadTask task)
    {
        var link = Link(task);
        return link == null || Candidate(link.Connections);
    }

    /// <summary>Any Carousel autoloader for a cannon of this controller (unknown counts as true).</summary>
    internal static bool HasLoader(VehicleWeaponLoadController controller)
    {
        var links = ControllerLinks.Get(controller.Pointer, controller, ResolveControllerLinks);
        if (links == null) return true;
        foreach (var link in links) if (Candidate(link.Connections)) return true;
        return false;
    }

    // Baskets are checked live: the Enabled box can be set after the topology was cached (blueprint load).
    private static bool Candidate(LoadConnections connections)
    {
        if (connections.Bustles.Length > 0) return true;
        int enabled = 0;
        foreach (var basket in connections.Baskets) if (CarouselRuntime.EnabledBasket(basket)) enabled++;
        return EarlyExit.Candidate(0, enabled);
    }

    internal static void Invalidate()
    {
        Connections.Clear();
        TaskLinks.Clear();
        ControllerLinks.Clear();
        BustleRuntime.InvalidateRuntimeCaches();
        MagazineRefill.ForgetPointers();
        CarouselRuntime.ForgetPointers();
    }

    [HarmonyTargetMethods]
    private static IEnumerable<MethodBase> Targets()
    {
        yield return AccessTools.Method(typeof(AmmoRack), nameof(AmmoRack.Build));
        yield return AccessTools.Method(typeof(Cannon), nameof(Cannon.Build));
        yield return AccessTools.Method(typeof(TurretBasket), nameof(TurretBasket.Build));
        yield return AccessTools.Method(typeof(Cannon), nameof(Cannon.EnableBehaviour));
        yield return AccessTools.Method(typeof(Cannon), nameof(Cannon.DisableBehaviour));
        yield return AccessTools.Method(typeof(VehicleComponent), nameof(VehicleComponent.ReleaseInternal));
    }

    internal static void CheckHooks(string owner)
    {
        int count = 0;
        foreach (var method in Targets())
        {
            if (Harmony.GetPatchInfo(method)?.Prefixes.Any(p => p.owner == owner && p.PatchMethod.DeclaringType == typeof(VehicleLoadIndex)) != true)
                throw new InvalidOperationException($"Load index invalidation hook missing: {method.Name}");
            count++;
        }
        Plugin.ModLog.LogDebug($"[Autoloaders] Performance cache lifecycle checks passed: {count} rebuild/enable/disable/release hooks.");
    }

    [HarmonyPrefix, HarmonyPriority(Priority.First)]
    private static void TopologyChanged()
    {
        try { Invalidate(); }
        catch (Exception ex) { HookGuard.Warn("Load index invalidation", -1, ex); }
    }
}
