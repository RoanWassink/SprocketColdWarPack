using HarmonyLib;
using Il2CppInterop.Runtime;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using Sprocket;
using Sprocket.Vehicles;
using Sprocket.Vehicles.Serialization;
using Sprocket.Vehicles.AttachedBehaviours;
using SerializationInfo=Il2CppSystem.Runtime.Serialization.SerializationInfo;
namespace SprocketThermalSight;
internal static class ConsolePersistence
{
    private static HashSet<string> Keys(SerializationInfo info)
    {
        var keys=new HashSet<string>();var e=info.GetEnumerator();
        while(e.MoveNext())keys.Add(e.Name);return keys;
    }
    internal static SavedConsoleLinks Read(SerializationInfo info) =>
        ConsoleLinkData.Read(Keys(info),key=>info.GetString(key),key=>info.GetInt32(key));
    [HarmonyPostfix,HarmonyPatch(typeof(VehicleObjectSerialization),nameof(VehicleObjectSerialization.ToBlueprint))]
    private static void Save(VehicleObject __0,VehicleObjectBlueprint __result)
    {
        try
        {
            if(__result?.State==null || __0==null || __0.Released)return;
            var rows=new List<ConsoleLinkEntry>();var profiles=new List<ThermalProfileEntry>();var components=__0.Components;
            int count=components.Cast<Il2CppSystem.Collections.Generic.IReadOnlyCollection<VehicleComponent>>().Count;
            bool hasSight=false;
            for(int i=0;i<count;i++)
            {
                if(components[i]?.TryCast<GunnerSight>() is not {} sight)continue;
                hasSight=true;
                if(ConsoleLinks.Target(sight)>=0)rows.Add(new(sight.VUID.Value,ConsoleLinks.Target(sight)));
                // Only an explicit inspector choice on a thermal sight is stored; without one the part default applies on load.
                if(ProfileStateHooks.SavedChoice(sight) is {} id)profiles.Add(new(sight.VUID.Value,id));
            }
            // Fresh object state: preserve every vanilla/other-mod value and replace our keys once.
            if(!hasSight)return;
            __result.State=Capture(__result.State,rows,profiles);
            if(profiles.Count>0)
                LogOnce($"[Thermal] Saved {profiles.Count} sight profile(s) in {ThermalProfileData.Key}: "+string.Join(", ",profiles.Select(p=>$"{p.Sight}={p.Profile}"))+".");
        }
        catch(Exception ex){Runtime.Warn("FCS object save: "+ex.Message);}
    }
    private sealed record BatchObject(VehicleObjectBlueprint Blueprint,HashSet<string> Keys,HashSet<int> SightIds,List<int> SightOld);
    [HarmonyPostfix,HarmonyPatch(typeof(VehicleObjectRegister),nameof(VehicleObjectRegister.InstantiateStructured))]
    private static void RestoreBatch(Il2CppReferenceArray<VehicleObjectBlueprint> __0,Il2CppReferenceArray<VehicleObject> __result)
    {
        if(__0==null || __result==null || __0.Length!=__result.Length)return;
        var map=new ConsoleVuidMap();var actual=new Dictionary<int,VehicleComponent>();var objects=new List<BatchObject>();
        try
        {
            // Batch is authoritative: clear provisional/legacy links before decoding any row.
            foreach(var obj in __result)
            {
                if(obj==null || obj.Released)continue;
                var cs=obj.Components;int n=cs.Cast<Il2CppSystem.Collections.Generic.IReadOnlyCollection<VehicleComponent>>().Count;
                for(int j=0;j<n;j++)if(cs[j]?.TryCast<GunnerSight>() is {} sight)ConsoleLinks.Restore(sight,-1);
            }
            // One VUID map for both envelopes, built from authoritative component ownership.
            for(int i=0;i<__0.Length;i++)
            {
                var bp=__0[i];var obj=__result[i];
                if(bp?.State==null || obj==null || obj.Released)continue;
                var item=new BatchObject(bp,Keys(bp.State),new HashSet<int>(),new List<int>());objects.Add(item);
                var components=obj.Components;int count=components.Cast<Il2CppSystem.Collections.Generic.IReadOnlyCollection<VehicleComponent>>().Count;
                for(int j=0;j<count;j++)
                {
                    var component=components[j];if(component==null || !item.Keys.Contains(component.ComponentID))continue;
                    int old=bp.State.GetInt32(component.ComponentID);int next=component.VUID.Value;
                    if(old<0 || next<0)continue;
                    map.Add(old,next);
                    if(component.TryCast<GunnerSight>()!=null){item.SightIds.Add(old);item.SightOld.Add(old);}
                    if(!actual.ContainsKey(next))actual[next]=component;
                }
            }
        }
        catch(Exception ex){Runtime.Warn("FCS batch restore: "+ex.Message);return;}
        try
        {
            var rows=new List<ConsoleLinkEntry>();
            foreach(var item in objects)
            {
                var saved=Read(item.Blueprint.State);
                if(saved.Canonical)rows.AddRange(ConsoleLinkData.ForOwner(saved.Links,item.SightIds));
                else if(saved.Legacy>=0)rows.AddRange(item.SightOld.Select(old=>new ConsoleLinkEntry(old,saved.Legacy)));
            }
            foreach(var row in ConsoleLinkData.Remap(rows,map.Values))
            {
                if(!actual.TryGetValue(row.Sight,out var source) || source.TryCast<GunnerSight>() is not {} sight ||
                    !actual.TryGetValue(row.Console,out var console) || !ConsoleLinks.IsConsole(console) ||
                    sight.Vehicle==null || console.Vehicle?.Pointer!=sight.Vehicle.Pointer)continue;
                ConsoleLinks.Restore(sight,row.Console);
            }
        }
        catch(Exception ex){Runtime.Warn("FCS batch restore: "+ex.Message);}
        // Separate guard: a damaged FCS envelope never blocks profiles, and vice versa.
        try{RestoreProfiles(objects,map,actual);}
        catch(Exception ex){Runtime.Warn("Profile batch restore: "+ex.Message);}
    }
    private static void RestoreProfiles(List<BatchObject> objects,ConsoleVuidMap map,Dictionary<int,VehicleComponent> actual)
    {
        var rows=new List<ThermalProfileEntry>();var authoritative=new HashSet<int>();
        foreach(var item in objects)
        {
            if(!item.Keys.Contains(ThermalProfileData.Key))continue;
            // This object carries the canonical envelope: its sights start fresh (no legacy/provisional choice).
            foreach(int old in item.SightOld)if(map.Values.TryGetValue(old,out int next))authoritative.Add(next);
            try{rows.AddRange(ThermalProfileData.ForOwner(ThermalProfileData.Decode(item.Blueprint.State.GetString(ThermalProfileData.Key)),item.SightIds));}
            catch(Exception ex){Runtime.Warn("Rejected saved sight profiles (part default used): "+ex.Message);}
        }
        if(authoritative.Count==0)return;
        foreach(int next in authoritative)if(actual.TryGetValue(next,out var c))ProfileStateHooks.Restore(c,null);
        var restored=new List<string>();
        foreach(var row in ThermalProfileData.Remap(rows,map.Values))
        {
            if(!authoritative.Contains(row.Sight) || !actual.TryGetValue(row.Sight,out var component) || !Runtime.IsThermal(component))continue;
            ProfileStateHooks.Restore(component,row.Profile);restored.Add($"{row.Sight}={row.Profile}");
        }
        LogOnce($"[Thermal] Restored {restored.Count} sight profile(s) from {ThermalProfileData.Key} ({rows.Count} saved row(s)): "+string.Join(", ",restored)+".");
    }
    // ToBlueprint/InstantiateStructured also run for designer snapshots; skip a line identical to the previous one.
    private static string? lastLog;
    private static void LogOnce(string line){if(line==lastLog)return;lastLog=line;Plugin.Instance.Log.LogInfo(line);}
    internal static SerializationInfo Capture(SerializationInfo original,IEnumerable<ConsoleLinkEntry> links,IEnumerable<ThermalProfileEntry> profiles)
    {
        var state=new SerializationInfo(Il2CppType.Of<Il2CppSystem.Object>(),
            new Il2CppSystem.Runtime.Serialization.FormatterConverter().Cast<Il2CppSystem.Runtime.Serialization.IFormatterConverter>());
        var entries=original.GetEnumerator();
        while(entries.MoveNext())
            if(!ObjectStateRows.Owned(entries.Name))
                state.AddValue(entries.Name,entries.Value,entries.ObjectType);
        foreach(var envelope in ObjectStateRows.Envelopes(links,profiles))state.AddValue(envelope.Key,envelope.Value);
        return state;
    }
    internal static void CheckNativeJsonRoundTrip()
    {
        const string fixture="{\"v\":\"2.0\",\"header\":{\"v\":\"0.2\",\"name\":\"FCS synthetic codec check\",\"gameVersion\":\"0.2.56.1\",\"mass\":0,\"creationDate\":\"1945.01.01\",\"class\":\"\",\"desc\":\"\",\"cost\":0},\"blueprints\":[],\"meshes\":[],\"objects\":[{\"guid\":\"00000000-0000-0000-0000-000000000000\",\"vuid\":967,\"pvuid\":-1,\"flags\":0,\"t72StyleSight\":968,\"sentinelText\":\"retain-string\",\"sentinelInt\":2671}]}";
        var serializer=new VehicleBlueprintSerializer();
        foreach(int target in new[]{992,1992,-1})
        {
            var bp=serializer.DeserializeJSON(fixture);var obj=bp.VehicleObjects[0];
            var captured=new SerializationInfo(Il2CppType.Of<Il2CppSystem.Object>(),
                new Il2CppSystem.Runtime.Serialization.FormatterConverter().Cast<Il2CppSystem.Runtime.Serialization.IFormatterConverter>());
            var e=obj.State.GetEnumerator();
            while(e.MoveNext())
                if(e.Name is not ("guid" or "vuid" or "pvuid" or "flags"))
                    captured.AddValue(e.Name,e.Value,e.ObjectType);
            captured.AddValue(ConsoleLinkData.LegacyKey,992);
            if(Read(captured).Legacy!=992)throw new InvalidDataException("Legacy FCS read failed.");
            var rows=target<0?Array.Empty<ConsoleLinkEntry>():new[]{new ConsoleLinkEntry(968,target)};
            obj.State=Capture(captured,rows,Array.Empty<ThermalProfileEntry>());
            var restored=serializer.DeserializeJSON(serializer.SerializeToJSON(bp,true)).VehicleObjects[0].State;
            if(restored.GetString("sentinelText")!="retain-string" || restored.GetInt32("sentinelInt")!=2671 ||
               restored.GetInt32("t72StyleSight")!=968 || Keys(restored).Contains(ConsoleLinkData.LegacyKey))
                throw new InvalidDataException("FCS snapshot changed native state.");
            var loaded=Read(restored);
            if(!loaded.Links.SequenceEqual(rows) || (target>=0 && !loaded.Canonical))
                throw new InvalidDataException("Native JSON lost FCS string envelope.");
        }
    }}


