using System.Text.Json;
namespace SprocketEngineExpansion.Core;
internal sealed record AssemblyMarker(bool Independent,int OriginalOptions)
{
    internal string Encode()=>JsonSerializer.Serialize(new Payload(1,Independent,OriginalOptions));
    private sealed record Payload(int Schema,bool Independent,int OriginalOptions);
    internal static AssemblyMarker Decode(string raw)
    {
        try
        {
            using var doc=JsonDocument.Parse(raw);
            var names=doc.RootElement.EnumerateObject().Select(f=>f.Name).ToArray();
            if(names.Length!=3||names.Distinct().Count()!=3||names.Any(n=>n!="Schema"&&n!="Independent"&&n!="OriginalOptions"))throw new ArgumentException("Unsupported assembly marker fields.");
            var state=JsonSerializer.Deserialize<Payload>(raw)??throw new ArgumentException("Missing assembly marker.");
            // Exact reviewed PlacementOptions flags (bit512 is not defined in this game build).
            if(state.Schema!=1||state.OriginalOptions<0||(state.OriginalOptions&~3583)!=0)throw new ArgumentException("Unsupported assembly marker.");
            return new(state.Independent,state.OriginalOptions);
        }
        catch(Exception ex) when(ex is JsonException or InvalidOperationException){throw new ArgumentException("Invalid assembly marker.",ex);}
    }
}
