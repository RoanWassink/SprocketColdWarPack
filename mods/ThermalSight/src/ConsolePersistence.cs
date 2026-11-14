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
            var rows=new List<ConsoleLinkEntry>();var components=__0.Components;
            int count=components.Cast<Il2CppSystem.Collections.Generic.IReadOnlyCollection<VehicleComponent>>().Count;
            for(int i=0;i<count;i++)
                if(components[i]?.TryCast<GunnerSight>() is {} sight && ConsoleLinks.Target(sight)>=0)
                    rows.Add(new(sight.VUID.Value,ConsoleLinks.Target(sight)));
            // Fresh object state: preserve every vanilla/other-mod value and replace our keys once.
            bool hasSight=false;
            for(int i=0;i<count;i++)if(components[i]?.TryCast<GunnerSight>()!=null)hasSight=true;
            if(!hasSight)return;
            __result.State=Capture(__result.State,rows);
        }
        catch(Exception ex){Runtime.Warn("FCS object save: "+ex.Message);}
    }
    [HarmonyPostfix,HarmonyPatch(typeof(VehicleObjectRegister),nameof(VehicleObjectRegister.InstantiateStructured))]
    private static void RestoreBatch(Il2CppReferenceArray<VehicleObjectBlueprint> __0,Il2CppReferenceArray<VehicleObject> __result)
    {
        try
        {
            if(__0==null || __result==null || __0.Length!=__result.Length)return;
            // Batch is authoritative: clear provisional/legacy links before decoding any row.
            foreach(var obj in __result)
            {
                if(obj==null || obj.Released)continue;
                var cs=obj.Components;int n=cs.Cast<Il2CppSystem.Collections.Generic.IReadOnlyCollection<VehicleComponent>>().Count;
                for(int j=0;j<n;j++)if(cs[j]?.TryCast<GunnerSight>() is {} sight)ConsoleLinks.Restore(sight,-1);
            }
            var map=new ConsoleVuidMap();
            var actual=new Dictionary<int,VehicleComponent>();var rows=new List<ConsoleLinkEntry>();
            for(int i=0;i<__0.Length;i++)
            {
                var bp=__0[i];var obj=__result[i];
                if(bp?.State==null || obj==null || obj.Released)continue;
                var keys=Keys(bp.State);var saved=Read(bp.State);var sourceIds=new HashSet<int>();
                var components=obj.Components;int count=components.Cast<Il2CppSystem.Collections.Generic.IReadOnlyCollection<VehicleComponent>>().Count;
                for(int j=0;j<count;j++)
                {
                    var component=components[j];if(component==null || !keys.Contains(component.ComponentID))continue;
                    int old=bp.State.GetInt32(component.ComponentID);int next=component.VUID.Value;
                    if(old<0 || next<0)continue;
                    map.Add(old,next);
                    if(component.TryCast<GunnerSight>()!=null)sourceIds.Add(old);
                    if(!actual.ContainsKey(next))actual[next]=component;
                    if(!saved.Canonical && saved.Legacy>=0 && component.TryCast<GunnerSight>()!=null)
                        rows.Add(new(old,saved.Legacy));
                }
                if(saved.Canonical)rows.AddRange(ConsoleLinkData.ForOwner(saved.Links,sourceIds));
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
    }
    internal static SerializationInfo Capture(SerializationInfo original,IEnumerable<ConsoleLinkEntry> links)
    {
        var state=new SerializationInfo(Il2CppType.Of<Il2CppSystem.Object>(),
            new Il2CppSystem.Runtime.Serialization.FormatterConverter().Cast<Il2CppSystem.Runtime.Serialization.IFormatterConverter>());
        var entries=original.GetEnumerator();
        while(entries.MoveNext())
            if(entries.Name!=ConsoleLinkData.Key && entries.Name!=ConsoleLinkData.LegacyKey)
                state.AddValue(entries.Name,entries.Value,entries.ObjectType);
        var rows=links.ToArray();
        if(rows.Length>0)state.AddValue(ConsoleLinkData.Key,ConsoleLinkData.Encode(rows));
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
            obj.State=Capture(captured,rows);
            var restored=serializer.DeserializeJSON(serializer.SerializeToJSON(bp,true)).VehicleObjects[0].State;
            if(restored.GetString("sentinelText")!="retain-string" || restored.GetInt32("sentinelInt")!=2671 ||
               restored.GetInt32("t72StyleSight")!=968 || Keys(restored).Contains(ConsoleLinkData.LegacyKey))
                throw new InvalidDataException("FCS snapshot changed native state.");
            var loaded=Read(restored);
            if(!loaded.Links.SequenceEqual(rows) || (target>=0 && !loaded.Canonical))
                throw new InvalidDataException("Native JSON lost FCS string envelope.");
        }
    }}


