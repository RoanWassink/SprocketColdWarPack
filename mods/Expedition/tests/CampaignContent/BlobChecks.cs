using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using SprocketExpedition.Core;
using SprocketExpedition.Core.Encounters;
using SprocketExpedition.Game;

internal static class BlobChecks
{
    internal static void Run(string root, CatalogueDefinition original)
    {
        static void Check(bool ok,string message){if(!ok)throw new Exception(message);}
        static void Reject(Action action){try{action();}catch(InvalidDataException){return;}throw new Exception("Bad blob must be rejected");}
        var bytes=Encoding.UTF8.GetBytes("exact immutable blueprint bytes");var hash=CampaignBlueprintBlob.Hash(bytes);
        var path=CampaignBlueprintBlob.Write(root,hash,bytes);
        Check(CampaignBlueprintBlob.Read(path,hash).SequenceEqual(bytes),"blob roundtrip");
        Reject(()=>CampaignBlueprintBlob.PathFor(root,"../escape"));
        var corrupt=bytes.ToArray();corrupt[0]++;
        File.WriteAllBytes(path,corrupt);Reject(()=>CampaignBlueprintBlob.Read(path,hash));
        Reject(()=>CampaignBlueprintBlob.Write(root,hash,bytes)); // never silently overwrite corrupt content
        var blueprint=Encoding.UTF8.GetBytes("{\"header\":{\"name\":\"Own tank\",\"creationDate\":\"native date\"},\"parts\":[1,2,3]}");
        var cloned=JsonNode.Parse(CampaignBlueprintBlob.SupportClone(blueprint,"Own tank"))!;
        Check(cloned["header"]!["name"]!.GetValue<string>().StartsWith("Expedition support "),"support has actual native header identity");
        Check(cloned["header"]!["creationDate"]!.GetValue<string>()=="native date"&&cloned["parts"]!.ToJsonString()=="[1,2,3]","clone keeps native era and parts");
        Check(JsonNode.Parse(blueprint)!["header"]!["name"]!.GetValue<string>()=="Own tank","source player design unchanged");
        var tanks=Enumerable.Range(0,600).Select(i=>{
            var b=Encoding.UTF8.GetBytes("large-pool synthetic "+i);var h=CampaignBlueprintBlob.Hash(b);
            return new ArchivedCampaignTank("native."+h[..32],"Test "+i,h,original.Eras[i%6].Era.NativeId,10m+i,b);
        }).OrderBy(t=>t.Id,StringComparer.Ordinal).ToArray();
        var definition=original with{Version="native.cost.1.large-blob-fixture",Eras=original.Eras.Select(era=>era with{Enemies=tanks.Where(t=>t.Era==era.Era.NativeId)
            .Select(t=>new EnemyDesignEntry(new BlueprintReference(t.Id,t.Hash,era.Era,Array.Empty<ModRequirement>()),EnemyRole.Line,t.Price,new ScenarioValue(t.Price,original.PriceUnitId),ContentQualification.Candidate)).ToArray()}).ToArray()};
        var data=new CampaignArchiveData(definition,100m,tanks[0].Id,tanks);
        var largeRoot=Path.Combine(root,"large");CampaignCatalogueArchive.Write(largeRoot,data);
        var loaded=CampaignCatalogueArchive.Read(largeRoot,definition.Version)!;
        Check(loaded.Tanks.Count==600&&loaded.Tanks.All(t=>t.Bytes.Length==0&&t.BlobHash==t.Hash),"large pool uses manifest rather than eager bytes");
        Check(loaded.Tanks[420].ReadBytes().SequenceEqual(tanks[420].Bytes),"selected large-pool tank retains exact bytes");
        var file=Directory.GetFiles(Path.Combine(largeRoot,"catalogues")).Single();
        Check(new FileInfo(file).Length<2*1024*1024,"manifest bounded");
        // Construct an actual pre-milestone embedded-byte archive to prove read
        // compatibility, rather than reading a second new-format fixture.
        var legacy=data with{Definition=definition with{Version="native.cost.1.legacy-embedded"}};
        var payload=JsonSerializer.Serialize(legacy);
        var encoded=JsonSerializer.Serialize(new{Schema=1,Hash=CampaignBlueprintBlob.Hash(Encoding.UTF8.GetBytes(payload)),Payload=payload});
        var legacyPath=Path.Combine(largeRoot,"catalogues",CampaignBlueprintBlob.Hash(Encoding.UTF8.GetBytes(legacy.Definition.Version))+".json");
        File.WriteAllText(legacyPath,encoded);
        Check(CampaignCatalogueArchive.Read(largeRoot,legacy.Definition.Version)!.Tanks[420].ReadBytes().SequenceEqual(tanks[420].Bytes),"legacy embedded-byte archive remains readable");
        Console.WriteLine("PASS 600-design lazy manifest, exact selected bytes, immutable hash failures, old embedded archives and native-header support clones");
    }
}
