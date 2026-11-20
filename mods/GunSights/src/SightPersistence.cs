using HarmonyLib;
using Il2CppInterop.Runtime;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using Sprocket.Vehicles;
using Sprocket.Vehicles.AttachedBehaviours;
using Sprocket.Vehicles.Serialization;
using UnityEngine;
using SerializationInfo = Il2CppSystem.Runtime.Serialization.SerializationInfo;

namespace SprocketGunSights;

/// <summary>
/// Saves the per-sight choices (reticle variant, "Sight follows gun") in the blueprint object state under
/// sprocketGunSightsV1, through the same two postfixes and signatures as Thermal Sight's ConsolePersistence.
/// The native outer ToBlueprint path bypasses component SaveData, so this is the route that reaches the file.
/// Only our own key is replaced; every other key, including Thermal's, is copied unchanged.
/// </summary>
internal static class SightPersistence
{
    private static HashSet<string> Keys(SerializationInfo info)
    {
        var keys = new HashSet<string>(); var e = info.GetEnumerator();
        while (e.MoveNext()) keys.Add(e.Name);
        return keys;
    }

    private static readonly HashSet<string> warned = new();
    private static void Warn(string area, Exception ex)
    { if (warned.Add(area)) Plugin.Log.LogWarning($"[Gun sights] {area}: {ex.Message}"); }

    [HarmonyPostfix, HarmonyPatch(typeof(VehicleObjectSerialization), nameof(VehicleObjectSerialization.ToBlueprint))]
    private static void Save(VehicleObject __0, VehicleObjectBlueprint __result)
    {
        try
        {
            if (__result?.State == null || __0 == null || __0.Released) return;
            var rows = new List<SightStateEntry>(); var components = __0.Components;
            int count = components.Cast<Il2CppSystem.Collections.Generic.IReadOnlyCollection<VehicleComponent>>().Count;
            bool hasSight = false;
            for (int i = 0; i < count; i++)
            {
                if (components[i]?.TryCast<GunnerSight>() is not { } sight) continue;
                hasSight = true;
                if (SightChoices.SavedEntry(sight) is { } row) rows.Add(row);
            }
            if (!hasSight) return;
            // Leave the native state object untouched unless we have something to write or a stale key to drop.
            if (rows.Count > 0 || Keys(__result.State).Contains(SightStateData.Key)) __result.State = Capture(__result.State, rows);
            Pending(rows);
        }
        catch (Exception ex) { Warn("Save sight choices", ex); }
    }

    private sealed record BatchObject(VehicleObjectBlueprint Blueprint, HashSet<string> Keys, HashSet<int> SightOld);

    [HarmonyPostfix, HarmonyPatch(typeof(VehicleObjectRegister), nameof(VehicleObjectRegister.InstantiateStructured))]
    private static void RestoreBatch(Il2CppReferenceArray<VehicleObjectBlueprint> __0, Il2CppReferenceArray<VehicleObject> __result)
    {
        try
        {
            if (__0 == null || __result == null || __0.Length != __result.Length) return;
            var map = new SightVuidMap(); var actual = new Dictionary<int, GunnerSight>(); var objects = new List<BatchObject>();
            // One VUID map for the whole batch, from authoritative component ownership (as Thermal).
            for (int i = 0; i < __0.Length; i++)
            {
                var bp = __0[i]; var obj = __result[i];
                if (bp?.State == null || obj == null || obj.Released) continue;
                var item = new BatchObject(bp, Keys(bp.State), new HashSet<int>()); objects.Add(item);
                var components = obj.Components;
                int count = components.Cast<Il2CppSystem.Collections.Generic.IReadOnlyCollection<VehicleComponent>>().Count;
                for (int j = 0; j < count; j++)
                {
                    var component = components[j]; if (component == null || !item.Keys.Contains(component.ComponentID)) continue;
                    if (component.TryCast<GunnerSight>() is not { } sight) continue;
                    int old = bp.State.GetInt32(component.ComponentID); int next = component.VUID.Value;
                    if (old < 0 || next < 0) continue;
                    map.Add(old, next); item.SightOld.Add(old);
                    if (!actual.ContainsKey(next)) actual[next] = sight;
                }
            }

            var rows = new List<SightStateEntry>(); var authoritative = new HashSet<int>(); int envelopes = 0;
            foreach (var item in objects)
            {
                if (!item.Keys.Contains(SightStateData.Key)) continue;   // no envelope: legacy LoadData choice stays
                envelopes++;
                // This object carries the canonical envelope: its sights start fresh, then get the saved rows.
                foreach (int old in item.SightOld) if (map.Values.TryGetValue(old, out int next)) authoritative.Add(next);
                try { rows.AddRange(SightStateData.ForOwner(SightStateData.Decode(item.Blueprint.State.GetString(SightStateData.Key)), item.SightOld)); }
                catch (Exception ex) { Warn("Rejected saved gun sight rows (defaults used)", ex); }
            }
            if (actual.Count == 0) return;   // no sights in this batch: nothing to log
            if (envelopes == 0) { LogOnce($"[Gun sights] Loaded 0 sight row(s): no {SightStateData.Key} in {objects.Count} object(s); defaults or legacy choice."); return; }
            foreach (int next in authoritative) if (actual.TryGetValue(next, out var s)) SightChoices.Restore(s, null, null);
            int restored = 0;
            foreach (var row in SightStateData.Plan(rows, map.Values, id => SightCatalogue.Find(id) != null))
            {
                if (!authoritative.Contains(row.Sight) || !actual.TryGetValue(row.Sight, out var sight)) continue;
                if (row.RejectedVariant != null)
                    Plugin.Log.LogWarning($"[Gun sights] Unknown saved sight '{row.RejectedVariant}' on {row.Sight}; default variant used.");
                SightChoices.Restore(sight, row.Variant, row.FollowGun); restored++;
            }
            LogOnce($"[Gun sights] Loaded {restored} sight row(s) from {SightStateData.Key} ({rows.Count} saved, {envelopes} object(s)).");
        }
        catch (Exception ex) { Warn("Restore sight choices", ex); }
    }

    internal static SerializationInfo Capture(SerializationInfo original, IReadOnlyCollection<SightStateEntry> rows)
    {
        var state = new SerializationInfo(Il2CppType.Of<Il2CppSystem.Object>(),
            new Il2CppSystem.Runtime.Serialization.FormatterConverter().Cast<Il2CppSystem.Runtime.Serialization.IFormatterConverter>());
        var entries = original.GetEnumerator();
        while (entries.MoveNext())
            if (!SightStateData.Owned(entries.Name))
                state.AddValue(entries.Name, entries.Value, entries.ObjectType);
        if (rows.Count > 0) state.AddValue(SightStateData.Key, SightStateData.Encode(rows));
        return state;
    }

    // ToBlueprint runs once per object: sum one save into a single line, written at the next frame.
    private static int pendingFrame = -1, pendingRows, pendingObjects;
    private static void Pending(List<SightStateEntry> rows)
    {
        Flush();
        pendingFrame = Time.frameCount; pendingRows += rows.Count; pendingObjects++;
    }

    /// <summary>Called every frame (TurretView) and before each save: writes the summary of an earlier frame's save.</summary>
    internal static void Flush()
    {
        if (pendingFrame < 0 || pendingFrame == Time.frameCount) return;
        LogOnce($"[Gun sights] Saved {pendingRows} sight row(s) in {SightStateData.Key} ({pendingObjects} object(s)).");
        pendingFrame = -1; pendingRows = 0; pendingObjects = 0;
    }

    // Designer snapshots also run ToBlueprint/InstantiateStructured: skip a line identical to the previous one.
    private static string? lastLog;
    private static void LogOnce(string line) { if (line == lastLog) return; lastLog = line; Plugin.Log.LogInfo(line); }
}
