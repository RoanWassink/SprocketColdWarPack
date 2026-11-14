using HarmonyLib;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using Sprocket;
using Sprocket.Vehicles.Missions;
using Sprocket.Vehicles.Spawning;
using UnityEngine;

namespace SprocketSandboxTargets;

// The normal sandbox map spawns its IS-3 through its own spawn event whose VehicleSource
// is in Explicit mode with a blueprint name. The flat map has only the EditorExitSave
// event, so each target blueprint gets a clone of that event set to Explicit. The clone
// then builds, registers and resets its vehicles natively, like the stock copy.
internal static class CustomSpawners
{
    private static readonly List<StageVehicleSpawnEvent> pool = new();
    private static readonly HashSet<IntPtr> clones = new();
    private static readonly Dictionary<IntPtr,Il2CppStructArray<VehicleSpawnInstance>> pending = new();
    private static IntPtr owner;
    private static Il2CppSystem.Threading.CancellationTokenSource? cancellation;

    internal static bool IsClone(StageVehicleSpawnEvent e) => clones.Contains(e.Pointer);

    internal static bool TryTake(StageVehicleSpawnEvent e,out Il2CppStructArray<VehicleSpawnInstance> instances)
    {
        instances = null!;
        if (!IsClone(e) || !pending.TryGetValue(e.Pointer,out var found)) return false;
        instances = found;
        Plugin.Info($"{e.gameObject.name}: {found.Length} doelen aan de native spawn gegeven.");
        return true;
    }

    // Returns the blueprint names that could not be spawned natively; those become own-design copies.
    internal static List<string> Start(StageVehicleSpawnEvent stock,Dictionary<string,List<VehicleSpawnInstance>> groups)
    {
        var failed = new List<string>();
        var index = 0;
        foreach (var (name,instances) in groups)
        {
            try
            {
                var clone = Get(stock,index++);
                if (!Resolve(clone.blueprintSource,name)) { failed.Add(name); continue; }
                var array = new Il2CppStructArray<VehicleSpawnInstance>(instances.Count);
                for (var i = 0; i < instances.Count; i++) array[i] = instances[i];
                pending[clone.Pointer] = array;
                if (stock.register != null) clone.SetVehicleRegister(stock.register);
                cancellation ??= new Il2CppSystem.Threading.CancellationTokenSource();
                clone.StageStart(cancellation.Token);
                Plugin.Info($"{clone.gameObject.name}: '{name}' gestart voor {instances.Count} doel(en).");
            }
            catch (Exception ex) { Plugin.Warn($"Blueprint '{name}': {ex.Message}; eigen tank gebruikt."); failed.Add(name); }
        }
        return failed;
    }

    internal static void ResetClones()
    {
        try { cancellation?.Cancel(); } catch (Exception ex) { Plugin.Warn("Annuleren mislukt: " + ex.Message); }
        cancellation = null;
        foreach (var clone in pool)
        {
            if (clone == null || clone.spawns == null) continue;
            try { clone.StageReset(); }
            catch (Exception ex) { Plugin.Warn($"Reset van {clone.gameObject.name} mislukt: {ex.Message}"); }
        }
        pending.Clear();
    }

    private static StageVehicleSpawnEvent Get(StageVehicleSpawnEvent stock,int index)
    {
        if (owner != stock.Pointer || pool.Any(c => c == null))
        {
            pool.Clear(); clones.Clear(); pending.Clear(); cancellation = null;
            owner = stock.Pointer;
        }
        while (pool.Count <= index)
        {
            var go = UnityEngine.Object.Instantiate(stock.gameObject,stock.transform.parent);
            go.name = $"SandboxTargets Spawner {pool.Count + 1}";
            var clone = go.GetComponent<StageVehicleSpawnEvent>();
            // Disabled so Unity never runs its Start: the mod alone drives this event.
            clone.enabled = false;
            clones.Add(clone.Pointer);
            clone.blueprintSource.Mode = SpawnMode.Explicit;
            pool.Add(clone);
        }
        return pool[index];
    }

    // Lets the native VehicleSource resolve the path itself and keeps the first setting
    // that points at exactly the file the config names.
    private static bool Resolve(VehicleSource source,string name)
    {
        var expected = TargetGeometry.BlueprintPath(SprocketIO.FactionDirectory,name,SprocketIO.CurrentFactionName);
        if (!File.Exists(expected)) { Plugin.Warn($"Blueprint '{name}' ontbreekt: {expected}; eigen tank gebruikt."); return false; }
        var vehicles = Path.GetDirectoryName(expected)!;
        var faction = Directory.GetParent(vehicles)!.Parent!.Name;
        var file = Path.GetFileNameWithoutExtension(expected);
        var candidates = new List<(GameDataDirectory Source,string Subdirectory,string Name)>
        {
            // Native path is <Factions>/Blueprints/Vehicles/<Subdirectory>/<Name>.blueprint (seen in 0.3.1 log),
            // so climb back out of Blueprints/Vehicles into the faction folder.
            (GameDataDirectory.Factions,"../../" + faction + "/Blueprints/Vehicles",file),
            (GameDataDirectory.Factions,faction,file),
            (GameDataDirectory.Factions,faction + "/Blueprints/Vehicles",file),
            (GameDataDirectory.Factions,"",faction + "/Blueprints/Vehicles/" + file),
            (GameDataDirectory.Factions,"",faction + "/" + file),
        };
        if (string.Equals(faction,SprocketIO.CurrentFactionName,StringComparison.OrdinalIgnoreCase))
            candidates.Insert(0,(GameDataDirectory.CurrentFaction,"",file));
        var tried = new List<string>();
        foreach (var c in candidates)
        {
            source.Mode = SpawnMode.Explicit;
            source.Source = c.Source; source.Subdirectory = c.Subdirectory; source.BlueprintName = c.Name;
            string got;
            try { got = source.GetPath() ?? "(null)"; } catch (Exception ex) { got = "fout: " + ex.Message; }
            if (Same(got,expected))
            {
                Plugin.Info($"Blueprint '{name}' native gevonden via {c.Source}/'{c.Subdirectory}'/'{c.Name}': {got}");
                return true;
            }
            tried.Add($"{c.Source}/'{c.Subdirectory}'/'{c.Name}' -> {got}");
        }
        Plugin.Warn($"Blueprint '{name}': native pad wijkt af van {expected}; eigen tank gebruikt. Geprobeerd: " + string.Join(" | ",tried));
        return false;
    }

    private static bool Same(string a,string b)
    {
        try { return string.Equals(Path.GetFullPath(a),Path.GetFullPath(b),StringComparison.OrdinalIgnoreCase); }
        catch { return false; }
    }
}

[HarmonyPatch(typeof(StageVehicleSpawnEvent),nameof(StageVehicleSpawnEvent.StageReset))]
internal static class StockResetPatch
{
    // Native StageReset clears the stock spawns synchronously; the clones follow it.
    private static void Postfix(StageVehicleSpawnEvent __instance)
    {
        if (NativeTargets.Stopped || !NativeTargets.IsStock(__instance)) return;
        CustomSpawners.ResetClones();
    }
}

[HarmonyPatch(typeof(MissionScenarioGameState),nameof(MissionScenarioGameState.Exit))]
internal static class MissionExitPatch
{
    private static void Prefix()
    {
        if (!NativeTargets.Stopped) CustomSpawners.ResetClones();
    }
}
