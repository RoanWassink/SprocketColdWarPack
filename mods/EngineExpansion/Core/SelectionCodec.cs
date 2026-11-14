using System.Text.Json;
namespace SprocketEngineExpansion.Core;
public static class SelectionCodec
{
    public const string Key="sprocket.engineexpansion.v1";
    private sealed record Payload(int Schema,EngineKind Kind,bool Turbo,double BoostPercent);
    // Schema 2 adds the powerpack flag. Schema 1 stays the output without a powerpack, so 0.1.1 keeps reading those saves.
    private sealed record PayloadV2(int Schema,EngineKind Kind,bool Turbo,double BoostPercent,bool Powerpack);
    private static readonly string[] FieldsV1={"Schema","Kind","Turbo","BoostPercent"};
    private static readonly string[] FieldsV2={"Schema","Kind","Turbo","BoostPercent","Powerpack"};
    public static string Encode(EngineSelection value)
    {
        value.Validate();
        return value.Powerpack
            ?JsonSerializer.Serialize(new PayloadV2(2,value.Kind,value.Turbo,value.BoostPercent,true))
            :JsonSerializer.Serialize(new Payload(1,value.Kind,value.Turbo,value.BoostPercent));
    }
    public static EngineSelection Decode(string json)
    {
        using var document=JsonDocument.Parse(json);
        if(document.RootElement.ValueKind!=JsonValueKind.Object)throw new ArgumentException("Extension must be an object.");
        var names=document.RootElement.EnumerateObject().Select(p=>p.Name).ToArray();
        if(!document.RootElement.TryGetProperty("Schema",out var schemaField)||schemaField.ValueKind!=JsonValueKind.Number||!schemaField.TryGetInt32(out int schema))
            throw new ArgumentException("Unsupported engine expansion schema.");
        var expected=schema switch{1=>FieldsV1,2=>FieldsV2,_=>throw new ArgumentException("Unsupported engine expansion schema.")};
        if(names.Length!=expected.Length||names.Distinct().Count()!=names.Length||names.Any(n=>!expected.Contains(n)))
            throw new ArgumentException("Unsupported or incomplete engine extension fields.");
        EngineSelection selection;
        if(schema==1)
        {
            var value=JsonSerializer.Deserialize<Payload>(json)??throw new ArgumentException("Empty selection.");
            selection=new EngineSelection(value.Kind,value.Turbo,value.BoostPercent);
        }
        else
        {
            var value=JsonSerializer.Deserialize<PayloadV2>(json)??throw new ArgumentException("Empty selection.");
            selection=new EngineSelection(value.Kind,value.Turbo,value.BoostPercent,value.Powerpack);
        }
        selection.Validate();return selection;
    }
}
