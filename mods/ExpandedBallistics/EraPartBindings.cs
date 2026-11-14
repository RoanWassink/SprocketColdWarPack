using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Text.Json;

namespace SprocketExpandedBallistics.Era;

// Immutable classification data only. No native state, file IO, ledger or fallback.
public sealed class EraPartBinding
{
    public string CassetteComponentId { get; }
    public string MountComponentId { get; }
    public string MaterialId { get; }
    public string ResponseId { get; }
    public string Kind { get; }
    internal EraPartBinding(string cassette, string mount, string material, string response, string kind)
    { CassetteComponentId=cassette; MountComponentId=mount; MaterialId=material; ResponseId=response; Kind=kind; }

    // Must still be combined with native date, fingerprint, thickness and threat gates.
    public bool MatchesRecipe(string? responseId, string? kind, IEnumerable<string>? compatibleMaterialIds)
        => string.Equals(ResponseId,responseId,StringComparison.Ordinal)
        && string.Equals(Kind,kind,StringComparison.Ordinal)
        && compatibleMaterialIds!=null && compatibleMaterialIds.Contains(MaterialId,StringComparer.Ordinal);
}

public sealed class EraPartBindingCatalogue
{
    public const string MatchMode = "componentId";
    public IReadOnlyList<EraPartBinding> Bindings { get; }
    private readonly IReadOnlyDictionary<string,EraPartBinding> cassettes;
    private readonly IReadOnlyDictionary<string,EraPartBinding> mounts;
    internal EraPartBindingCatalogue(List<EraPartBinding> entries)
    {
        Bindings=Array.AsReadOnly(entries.ToArray());
        cassettes=new ReadOnlyDictionary<string,EraPartBinding>(entries.ToDictionary(x=>x.CassetteComponentId,StringComparer.Ordinal));
        // A mount lookup is classification only; active consumption always uses cassette identity.
        mounts=new ReadOnlyDictionary<string,EraPartBinding>(entries.GroupBy(x=>x.MountComponentId,StringComparer.Ordinal).ToDictionary(x=>x.Key,x=>x.First(),StringComparer.Ordinal));
    }
    public bool TryGetCassette(string? componentId,[NotNullWhen(true)] out EraPartBinding? binding)
    { binding=null;return componentId!=null && cassettes.TryGetValue(componentId,out binding); }
    public bool TryGetMount(string? componentId,[NotNullWhen(true)] out EraPartBinding? binding)
    { binding=null;return componentId!=null && mounts.TryGetValue(componentId,out binding); }
}

public static class EraPartBindings
{
    public static EraPartBindingCatalogue Parse(string json)
    {
        if(json==null)throw new FormatException("ERA binding JSON is required.");
        try
        {
            using var document=JsonDocument.Parse(json,new JsonDocumentOptions { AllowTrailingCommas=false,CommentHandling=JsonCommentHandling.Disallow });
            var root=document.RootElement;
            if(root.ValueKind!=JsonValueKind.Object)throw new FormatException("An ERA binding object is required.");
            if(!root.TryGetProperty("schemaVersion",out var version))throw new FormatException("ERA binding schema is required.");
            if(version.ValueKind!=JsonValueKind.Number||!version.TryGetInt32(out var number)||(number!=1&&number!=2))
                throw new FormatException("Unsupported ERA binding schema; expected integer 1 or 2.");
            if(number==1)Fields(root,"schemaVersion","matchMode","bindings");
            else Fields(root,"schemaVersion","matchMode","bindings","sharedHousings");
            if(Text(root,"matchMode")!=EraPartBindingCatalogue.MatchMode)
                throw new FormatException("Only exact componentId matching is supported.");
            var shared=new Dictionary<string,HashSet<string>>(StringComparer.Ordinal);
            var declaredCassettes=new HashSet<string>(StringComparer.Ordinal);
            if(number==2)
            {
                var housings=root.GetProperty("sharedHousings");
                if(housings.ValueKind!=JsonValueKind.Array)throw new FormatException("Shared housings must be an array.");
                foreach(var housing in housings.EnumerateArray())
                {
                    Fields(housing,"mountComponentId","cassetteComponentIds");
                    var mountId=Text(housing,"mountComponentId");
                    var cells=housing.GetProperty("cassetteComponentIds");
                    if(cells.ValueKind!=JsonValueKind.Array)throw new FormatException("Shared housing cassette IDs must be an array.");
                    var members=new HashSet<string>(StringComparer.Ordinal);
                    foreach(var cell in cells.EnumerateArray())
                    {
                        var id=Identifier(cell,"cassetteComponentIds");
                        if(id==mountId||!members.Add(id)||!declaredCassettes.Add(id))throw new FormatException("Duplicate or conflicting shared housing member.");
                    }
                    if(members.Count<2||!shared.TryAdd(mountId,members))throw new FormatException("A shared housing requires a unique mount and at least two cassettes.");
                }
            }
            var array=root.GetProperty("bindings");
            if(array.ValueKind!=JsonValueKind.Array)throw new FormatException("ERA bindings must be an array.");
            var entries=new List<EraPartBinding>();
            var roles=new HashSet<string>(StringComparer.Ordinal);
            var cassetteIds=new HashSet<string>(StringComparer.Ordinal);
            var mounted=new Dictionary<string,List<EraPartBinding>>(StringComparer.Ordinal);
            var materialRoutes=new Dictionary<string,(string Response,string Kind)>(StringComparer.Ordinal);
            var responseKinds=new Dictionary<string,string>(StringComparer.Ordinal);
            foreach(var item in array.EnumerateArray())
            {
                Fields(item,"cassetteComponentId","mountComponentId","materialId","responseId","kind");
                var cassette=Text(item,"cassetteComponentId");var mount=Text(item,"mountComponentId");
                var material=Text(item,"materialId");var response=Text(item,"responseId");var kind=Text(item,"kind");
                if(kind!="lightEra"&&kind!="heavyEra")throw new FormatException("Unsupported placed ERA kind: "+kind);
                // Unique across both roles: no mount can accidentally become active.
                if(cassette==mount||!cassetteIds.Add(cassette)||mounted.ContainsKey(cassette)||cassetteIds.Contains(mount))
                    throw new FormatException("Duplicate or conflicting ERA component role.");
                if(number==1&&(!roles.Add(cassette)||!roles.Add(mount)))throw new FormatException("Duplicate or conflicting ERA component role.");
                if(mounted.ContainsKey(mount)&&!shared.ContainsKey(mount))throw new FormatException("Undeclared shared ERA housing.");
                var route=(Response:response,Kind:kind);
                if(materialRoutes.TryGetValue(material,out var existing)&&existing!=route)
                    throw new FormatException("A material is bound to conflicting ERA responses.");
                if(responseKinds.TryGetValue(response,out var previousKind)&&previousKind!=kind)
                    throw new FormatException("An ERA response is bound to conflicting kinds.");
                materialRoutes[material]=route;responseKinds[response]=kind;
                var entry=new EraPartBinding(cassette,mount,material,response,kind);
                entries.Add(entry);
                if(!mounted.TryGetValue(mount,out var members))mounted[mount]=members=new();
                members.Add(entry);
            }
            foreach(var declaration in shared)
            {
                if(!mounted.TryGetValue(declaration.Key,out var members)||!declaration.Value.SetEquals(members.Select(x=>x.CassetteComponentId)))
                    throw new FormatException("Shared housing membership must match all bindings exactly.");
                var first=members[0];
                if(members.Any(x=>x.MaterialId!=first.MaterialId||x.ResponseId!=first.ResponseId||x.Kind!=first.Kind))
                    throw new FormatException("A shared housing has conflicting material response routes.");
            }
            if(shared.Keys.Any(cassetteIds.Contains))throw new FormatException("A shared housing cannot be an active cassette.");
            return new(entries);
        }
        catch(JsonException ex){throw new FormatException("Invalid ERA binding JSON.",ex);}
    }

    private static void Fields(JsonElement item,params string[] expected)
    {
        if(item.ValueKind!=JsonValueKind.Object)throw new FormatException("An ERA binding object is required.");
        var keys=new HashSet<string>(StringComparer.Ordinal);
        foreach(var property in item.EnumerateObject())
            if(!expected.Contains(property.Name,StringComparer.Ordinal)||!keys.Add(property.Name))
                throw new FormatException("Unknown or duplicate ERA binding field: "+property.Name);
        if(keys.Count!=expected.Length)throw new FormatException("Required ERA binding fields are missing.");
    }
    private static string Text(JsonElement item,string key)
    {
        var value=item.GetProperty(key);
        return Identifier(value,key);
    }
    private static string Identifier(JsonElement value,string key)
    {
        if(value.ValueKind!=JsonValueKind.String)throw new FormatException("ERA binding "+key+" must be a string.");
        var text=value.GetString()!;
        if(text.Length==0||text.Length>128||text!=text.Trim()||text.Any(c=>char.IsControl(c)))
            throw new FormatException("ERA binding "+key+" must be a nonempty, unpadded identifier of at most 128 characters.");
        return text;
    }
}
