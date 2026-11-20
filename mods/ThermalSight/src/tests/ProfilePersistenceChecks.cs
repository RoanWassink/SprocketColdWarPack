using System.Text.Json;
using System.Text.Json.Nodes;
namespace SprocketThermalSight;
// 0.4.2: per-sight profile choice in the object state (sprocketThermalProfilesV1), next to sprocketFcsLinksV1.
internal static class ProfilePersistenceChecks
{
    // Same filter/append order as ConsolePersistence.Capture, on a plain dictionary instead of SerializationInfo.
    internal static Dictionary<string,JsonNode?> Capture(IEnumerable<KeyValuePair<string,JsonNode?>> state,
        IEnumerable<ConsoleLinkEntry> links,IEnumerable<ThermalProfileEntry> profiles)
    {
        var next=new Dictionary<string,JsonNode?>();
        foreach(var e in state)if(!ObjectStateRows.Owned(e.Key))next[e.Key]=e.Value?.DeepClone();
        foreach(var env in ObjectStateRows.Envelopes(links,profiles))next[env.Key]=JsonValue.Create(env.Value);
        return next;
    }
    internal static void Run(Action<bool,string> check,Action<Action,string> reject,string fixtureDir)
    {
        // Codec roundtrip, version, broken JSON, duplicates, unknown profile.
        var row=new ThermalProfileEntry(753,"tiGen2");
        var json=ThermalProfileData.Encode(new[]{row});
        check(json=="{\"Version\":1,\"Profiles\":[{\"Sight\":753,\"Profile\":\"tiGen2\"}]}","profile wire format");
        check(ThermalProfileData.Decode(json).SequenceEqual(new[]{row}),"profile roundtrip");
        check(ThermalProfileData.Decode(ThermalProfileData.Encode(Array.Empty<ThermalProfileEntry>())).Length==0,"empty profile envelope");
        var unknown=new ThermalProfileEntry(754,"profileRemovedFromCatalog");
        check(ThermalProfileData.Decode(ThermalProfileData.Encode(new[]{row,unknown})).SequenceEqual(new[]{row,unknown}),"unknown profile id kept for later restore");
        foreach(var bad in new[]{
            "{\"Version\":2,\"Profiles\":[]}",
            "{\"Version\":0,\"Profiles\":[]}",
            "{\"Profiles\":[]}",
            "{\"Version\":1}",
            "{\"Version\":1,\"Profiles\":null}",
            "{\"Version\":1,\"Profiles\":[{\"Sight\":753}]}",
            "{\"Version\":1,\"Profiles\":[{\"Profile\":\"tiGen2\"}]}",
            "{\"Version\":1,\"Profiles\":[{\"Sight\":-1,\"Profile\":\"tiGen2\"}]}",
            "{\"Version\":1,\"Profiles\":[{\"Sight\":\"753\",\"Profile\":\"tiGen2\"}]}",
            "{\"Version\":1,\"Profiles\":[{\"Sight\":753,\"Profile\":7}]}",
            "{\"Version\":1,\"Profiles\":[{\"Sight\":753,\"Profile\":\"\"}]}",
            "{\"Version\":1,\"Profiles\":[{\"Sight\":753,\"Profile\":\"  \"}]}",
            "{\"Version\":1,\"Profiles\":[{\"Sight\":753,\"Profile\":\" tiGen2\"}]}",
            "{\"Version\":1,\"Profiles\":[{\"Sight\":753,\"Profile\":\"ti\\nGen2\"}]}",
            "{\"Version\":1,\"Profiles\":[{\"Sight\":753,\"Profile\":\"tiGen2\",\"extra\":1}]}",
            "{\"Version\":1,\"Profiles\":[{\"Sight\":753,\"Sight\":754,\"Profile\":\"tiGen2\"}]}",
            "{\"Version\":1,\"Profiles\":[{\"Sight\":753,\"Profile\":\"a\"},{\"Sight\":753,\"Profile\":\"b\"}]}",
            "{\"Version\":1,\"Profiles\":[],\"Links\":[]}",
            "{\"Version\":1,\"Profiles\":[",
            "not json","null","[]"})
            reject(()=>ThermalProfileData.Decode(bad),"reject broken profile envelope "+bad);
        reject(()=>ThermalProfileData.Encode(new[]{row,row with{Profile="tiGen1"}}),"duplicate sight rejected on save");
        reject(()=>ThermalProfileData.Encode(new[]{new ThermalProfileEntry(1,new string('x',ThermalProfileData.MaxIdLength+1))}),"overlong id rejected");
        reject(()=>ThermalProfileData.Encode(Enumerable.Range(0,65).Select(i=>new ThermalProfileEntry(i,"tiGen2"))),"bounded profile rows");
        reject(()=>ThermalProfileData.Decode(new string(' ',16385)),"bounded profile bytes");
        var max=Enumerable.Range(0,64).Select(i=>new ThermalProfileEntry(i,"p"+i)).ToArray();
        check(ThermalProfileData.Decode(ThermalProfileData.Encode(max)).SequenceEqual(max),"64 profile rows valid");
        // 0.5.0: the Gen 3 ids roundtrip through the object-state codec like any other profile id.
        var gen3=new[]{new ThermalProfileEntry(801,"tiGen3"),new ThermalProfileEntry(802,"tiGen3Uncooled"),new ThermalProfileEntry(803,"tiGen3HD")};
        check(ThermalProfileData.Encode(gen3)=="{\"Version\":1,\"Profiles\":[{\"Sight\":801,\"Profile\":\"tiGen3\"},{\"Sight\":802,\"Profile\":\"tiGen3Uncooled\"},{\"Sight\":803,\"Profile\":\"tiGen3HD\"}]}","Gen 3 wire format");
        check(ThermalProfileData.Decode(ThermalProfileData.Encode(gen3)).SequenceEqual(gen3),"Gen 3 ids roundtrip");
        check(gen3.All(r=>ThermalProfileData.ValidId(r.Profile)),"Gen 3 ids valid");
        var gen3Map=new ConsoleVuidMap();gen3Map.Add(803,1803);
        check(ThermalProfileData.Remap(gen3,gen3Map.Values).Single()==new ThermalProfileEntry(1803,"tiGen3HD"),"Gen 3 HD remaps on duplicate");

        // Remap on duplicate/instantiate, through the same poison-aware VUID map as the console links.
        var map=new ConsoleVuidMap();map.Add(753,753);
        check(ThermalProfileData.Remap(new[]{row},map.Values).Single()==row,"identity load keeps profile");
        map=new();map.Add(753,1753);
        check(ThermalProfileData.Remap(new[]{row},map.Values).Single()==new ThermalProfileEntry(1753,"tiGen2"),"duplicate remaps sight VUID");
        map=new();map.Add(999,1999);
        check(ThermalProfileData.Remap(new[]{row},map.Values).Length==0,"unmapped sight never keeps raw VUID");
        map=new();map.Add(753,1753);
        check(ThermalProfileData.Remap(new[]{row,row with{Profile="tiGen1"}},map.Values).Length==0,"same sight in two envelopes dropped");
        map=new();map.Add(753,1000);map.Add(754,1000);
        check(ThermalProfileData.Remap(new[]{row,unknown},map.Values).Length==0,"colliding new VUID poisons both rows");
        check(ThermalProfileData.ForOwner(new[]{row},new HashSet<int>{381}).Length==0,"foreign owner profile skipped");
        check(ThermalProfileData.ForOwner(new[]{row,unknown},new HashSet<int>{753}).Single()==row,"owner filter keeps own sight");
        for(int offset=1;offset<=50;offset++)
        {
            map=new();map.Add(754,754+offset*1000);map.Add(753,753+offset*1000);
            var remapped=ThermalProfileData.Remap(new[]{row,unknown},map.Values);
            check(remapped.Length==2&&remapped[0]==new ThermalProfileEntry(753+offset*1000,"tiGen2")&&remapped[1].Sight==754+offset*1000,"independent profile remaps");
        }

        // Coexistence with sprocketFcsLinksV1 in one object state: neither envelope is lost, other keys kept.
        var link=new ConsoleLinkEntry(753,1246);
        var state=new Dictionary<string,JsonNode?>{{"thermalSight",753},{"model",754},{"other.mod",JsonValue.Create("keep")},
            {ConsoleLinkData.Key,ConsoleLinkData.Encode(new[]{link})},{ThermalProfileData.Key,"stale"},{ConsoleLinkData.LegacyKey,992}};
        var both=Capture(state,new[]{link},new[]{row});
        check(ConsoleLinkData.Decode(both[ConsoleLinkData.Key]!.GetValue<string>()).Single()==link,"FCS link survives profile save");
        check(ThermalProfileData.Decode(both[ThermalProfileData.Key]!.GetValue<string>()).Single()==row,"profile survives FCS save");
        check(!both.ContainsKey(ConsoleLinkData.LegacyKey)&&both["thermalSight"]!.GetValue<int>()==753&&both["other.mod"]!.GetValue<string>()=="keep","fresh state keeps foreign keys only");
        var profileOnly=Capture(both,Array.Empty<ConsoleLinkEntry>(),new[]{row});
        check(!profileOnly.ContainsKey(ConsoleLinkData.Key)&&profileOnly.ContainsKey(ThermalProfileData.Key),"unlink keeps profile");
        var linkOnly=Capture(both,new[]{link},Array.Empty<ThermalProfileEntry>());
        check(linkOnly.ContainsKey(ConsoleLinkData.Key)&&!linkOnly.ContainsKey(ThermalProfileData.Key),"default profile clears key, keeps link");
        var read=ConsoleLinkData.Read(both.Keys,k=>both[k]!.GetValue<string>(),k=>both[k]!.GetValue<int>());
        check(read.Canonical&&read.Links.Single()==link,"FCS reader ignores profile key");

        // Real blueprint fixture: copy of Factions\Expedition Garage\...\Object 292.blueprint (source untouched).
        string source=Path.Combine(fixtureDir,"Object 292.blueprint");
        check(File.Exists(source),"fixture copy present in tmp-test");
        string original=File.ReadAllText(source);
        var root=JsonNode.Parse(original)!.AsObject();
        var objects=root["objects"]!.AsArray();
        int index=-1;
        for(int i=0;i<objects.Count;i++)if(objects[i]!.AsObject().ContainsKey(ConsoleLinkData.Key)){check(index<0,"one FCS object in fixture");index=i;}
        check(index>=0,"fixture has sprocketFcsLinksV1");
        var obj=objects[index]!.AsObject();
        int sightVuid=obj["thermalSight"]!.GetValue<int>();
        var savedLinks=ConsoleLinkData.Decode(obj[ConsoleLinkData.Key]!.GetValue<string>());
        check(savedLinks.Length==1&&savedLinks[0].Sight==sightVuid,"fixture link belongs to thermal sight component");
        check(!original.Contains(ThermalProfileData.Key)&&!original.Contains("sprocketThermalProfile"),"0.4.1 blueprint carries no profile (confirmed cause)");
        var before=obj.Select(p=>new KeyValuePair<string,JsonNode?>(p.Key,p.Value)).ToArray();
        var captured=Capture(before,savedLinks,new[]{new ThermalProfileEntry(sightVuid,"tiGen2")});
        var replaced=new JsonObject();foreach(var p in captured)replaced[p.Key]=p.Value;
        objects[index]=replaced;
        string written=Path.Combine(fixtureDir,"Object 292.profile-0.4.2.blueprint");
        File.WriteAllText(written,root.ToJsonString(new JsonSerializerOptions{WriteIndented=true}));
        var reread=JsonNode.Parse(File.ReadAllText(written))!.AsObject();
        var robj=reread["objects"]![index]!.AsObject();
        check(ThermalProfileData.Decode(robj[ThermalProfileData.Key]!.GetValue<string>()).Single()==new ThermalProfileEntry(sightVuid,"tiGen2"),"fixture profile reads back");
        check(ConsoleLinkData.Decode(robj[ConsoleLinkData.Key]!.GetValue<string>()).SequenceEqual(savedLinks),"fixture FCS link unchanged");
        foreach(var p in before.Where(p=>!ObjectStateRows.Owned(p.Key)))
            check(JsonNode.DeepEquals(robj[p.Key],p.Value),"fixture key unchanged: "+p.Key);
        var origRoot=JsonNode.Parse(original)!;
        for(int i=0;i<objects.Count;i++)if(i!=index)
            check(JsonNode.DeepEquals(origRoot["objects"]![i],reread["objects"]![i]),"other fixture objects unchanged");
        check(JsonNode.DeepEquals(origRoot["header"],reread["header"]),"fixture header unchanged");
        // Duplicate of that turret object: old sight VUID -> new, profile follows.
        map=new();map.Add(obj["vuid"]!.GetValue<int>(),5000);map.Add(sightVuid,5001);map.Add(obj["model"]!.GetValue<int>(),5002);
        var restored=ThermalProfileData.Remap(ThermalProfileData.ForOwner(ThermalProfileData.Decode(robj[ThermalProfileData.Key]!.GetValue<string>()),new HashSet<int>{sightVuid}),map.Values);
        check(restored.Single()==new ThermalProfileEntry(5001,"tiGen2"),"fixture duplicate remaps profile");
        check(File.ReadAllText(source)==original,"fixture copy not modified");
    }
}
