using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace SprocketExpandedBallistics.ExperimentalArmour;

// Shared pure contract. No native state, ledger, file writes or application adapter.
public sealed class ExperimentalRodResponse
{
    public string ResponseId { get; }
    public double BaseLoss { get; }
    public double MaximumLoss { get; }
    public double ReferenceLD { get; }
    public double MinimumSusceptibility { get; }
    public double MaximumSusceptibility { get; }
    public double ExposureWeight => 1;
    internal ExperimentalRodResponse(string id,double loss,double maximum,double reference,double minimumS,double maximumS)
    {ResponseId=id;BaseLoss=loss;MaximumLoss=maximum;ReferenceLD=reference;MinimumSusceptibility=minimumS;MaximumSusceptibility=maximumS;}
}

public sealed class ExperimentalArmourCatalogue
{
    public const string ModelVersion="2.0.6";
    public string ParsedModelVersion { get; }
    public bool Enabled { get; }
    public double MaximumAdditionalKineticLoss { get; }
    public string Revision { get; }
    public IReadOnlyDictionary<string,ExperimentalRodResponse> Recipes { get; }
    internal ExperimentalArmourCatalogue(string modelVersion,bool enabled,double cap,string revision,Dictionary<string,ExperimentalRodResponse> recipes)
    {ParsedModelVersion=modelVersion;Enabled=enabled;MaximumAdditionalKineticLoss=cap;Revision=revision;Recipes=new ReadOnlyDictionary<string,ExperimentalRodResponse>(recipes);}
    public double EffectiveKineticCap(double legacyCap)
    {
        if(!double.IsFinite(legacyCap)||legacyCap<0||legacyCap>.95)throw new ArgumentOutOfRangeException(nameof(legacyCap));
        return Math.Min(legacyCap,MaximumAdditionalKineticLoss);
    }
}

public static class ExperimentalArmour
{
    // Exact new physical response identities from reviewed family contract2.0.5.
    // Old saved recipes retain their own identities and physical fingerprints.
    public static IReadOnlyList<string> PhysicalResponseIds {get;}=Array.AsReadOnly(new[]
    {
        "kontakt5Active",
        "reliktActive",
        "reliktTurretActive",
        "nizhLegacyActive",
        "dupletLegacySingleActive",
        "dupletLegacyIntegratedActive",
        "dupletHullActive",
        "dupletHullZonesActive",
        "nizh34Active",
        "nizh19Active",
        "nizh1MActive",
        "duplet2MActive",
        "duplet2MHullActive"
    });
    // New exact identities admitted only by model2.0.5. Historical models retain their closed lists.
    public static IReadOnlyList<string> ExtendedPhysicalResponseIds {get;}=Array.AsReadOnly(new[]{"dynaActive"});
    public static IReadOnlyList<string> LegacyMaterialResponseIds {get;}=Array.AsReadOnly(new[]{"reliktCassette","kontakt5Cassette","nizhCassette","dupletCassette"});
    public static bool IsSupportedResponseId(string? id)=>
        id is "experimentalKontakt5Cassette" or "experimentalReliktCassette" or "experimentalNizhCassette" or "experimentalNizh34Insert" or "experimentalNizh19Insert" || (PhysicalResponseIds.Contains(id!,StringComparer.Ordinal)||ExtendedPhysicalResponseIds.Contains(id!,StringComparer.Ordinal)||LegacyMaterialResponseIds.Contains(id!,StringComparer.Ordinal));

    public static ExperimentalArmourCatalogue Parse(string json)
    {
        if(json==null||json.Length>1048576)throw new FormatException("Invalid ERA rod-response catalogue size.");
        try
        {
            using var doc=JsonDocument.Parse(json,new JsonDocumentOptions{AllowTrailingCommas=false,CommentHandling=JsonCommentHandling.Disallow,MaxDepth=32});
            var root=doc.RootElement;
            Fields(root,"schemaVersion","modelVersion","enabled","maximumAdditionalKineticLoss","recipes");
            var modelVersion=root.GetProperty("modelVersion").GetString();
            if(root.GetProperty("schemaVersion").GetInt32()!=2||modelVersion is not ("2.0.2" or "2.0.3" or "2.0.4" or "2.0.5" or "2.0.6"))
                throw new FormatException("Unsupported ERA rod-response schema/model.");
            // Retain and validate the legacy field without requiring a second feature opt-in.
            _=root.GetProperty("enabled").GetBoolean();
            const bool enabled=true;
            var lossCeiling=modelVersion is "2.0.5" or "2.0.6"?.95:.35;
            var cap=Number(root,"maximumAdditionalKineticLoss",0,lossCeiling);
            var array=root.GetProperty("recipes");
            if(array.ValueKind!=JsonValueKind.Array||array.GetArrayLength()>(modelVersion=="2.0.2"?3:modelVersion=="2.0.3"?5:5+PhysicalResponseIds.Count+(modelVersion is "2.0.5" or "2.0.6"?ExtendedPhysicalResponseIds.Count:0)+(modelVersion=="2.0.6"?LegacyMaterialResponseIds.Count:0)))throw new FormatException("Too many recipes for the declared rod-response model.");
            var recipes=new Dictionary<string,ExperimentalRodResponse>(StringComparer.Ordinal);
            foreach(var item in array.EnumerateArray())
            {
                Fields(item,"responseId","kind","baseLoss","maximumLoss","referenceLD","minimumSusceptibility","maximumSusceptibility","exposureWeight");
                var id=item.GetProperty("responseId").GetString();
                var legacyId=id is "experimentalKontakt5Cassette" or "experimentalReliktCassette" or "experimentalNizhCassette";
                var insertId=modelVersion is "2.0.3" or "2.0.4" or "2.0.5" or "2.0.6"&&(id is "experimentalNizh34Insert" or "experimentalNizh19Insert");
                var physicalId=modelVersion is "2.0.4" or "2.0.5" or "2.0.6"&&PhysicalResponseIds.Contains(id!,StringComparer.Ordinal);
                var extendedId=modelVersion is "2.0.5" or "2.0.6"&&ExtendedPhysicalResponseIds.Contains(id!,StringComparer.Ordinal);
                var materialId=modelVersion=="2.0.6"&&LegacyMaterialResponseIds.Contains(id!,StringComparer.Ordinal);
                if((!legacyId&&!insertId&&!physicalId&&!extendedId&&!materialId)||recipes.ContainsKey(id!))throw new FormatException("Unknown/duplicate ERA rod-response recipe identity.");
                if(item.GetProperty("kind").GetString()!="rodDisruption")throw new FormatException("Unsupported ERA rod-response mechanic.");
                // Absolute model bounds preserve recipes when a user lowers the global cap.
                var loss=Number(item,"baseLoss",0,lossCeiling);var maximum=Number(item,"maximumLoss",0,lossCeiling);
                if(loss>maximum)throw new FormatException("Base loss exceeds maximum loss.");
                var reference=Number(item,"referenceLD",1,10000);var minimumS=Number(item,"minimumSusceptibility",.1,4);var maximumS=Number(item,"maximumSusceptibility",.1,4);
                if(maximumS<minimumS)throw new FormatException("Unordered susceptibility bounds.");
                if(Number(item,"exposureWeight",1,1)!=1)throw new FormatException("Exposure must remain one.");
                recipes.Add(id!,new(id!,loss,maximum,reference,minimumS,maximumS));
            }
            return new(modelVersion,enabled,cap,Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(json))),recipes);
        }
        catch(JsonException ex){throw new FormatException("Invalid ERA rod-response JSON.",ex);}
        catch(InvalidOperationException ex){throw new FormatException("Invalid ERA rod-response field type.",ex);}
        catch(OverflowException ex){throw new FormatException("Invalid ERA rod-response number.",ex);}
    }
    private static double Positive(JsonElement root,string name)
    {var value=Number(root,name,0,double.MaxValue);if(value<=0)throw new FormatException("Expected positive "+name);return value;}
    private static double Number(JsonElement root,string name,double min,double max)
    {var value=root.GetProperty(name).GetDouble();if(!double.IsFinite(value)||value<min||value>max)throw new FormatException("Invalid experimental "+name);return value;}
    private static void Fields(JsonElement root,params string[] expected)
    {
        if(root.ValueKind!=JsonValueKind.Object)throw new FormatException("Expected ERA rod-response object.");
        var seen=new HashSet<string>(StringComparer.Ordinal);
        foreach(var p in root.EnumerateObject())if(!expected.Contains(p.Name)||!seen.Add(p.Name))throw new FormatException("Unknown/duplicate ERA rod-response field: "+p.Name);
        if(seen.Count!=expected.Length)throw new FormatException("Missing ERA rod-response fields.");
    }
}
