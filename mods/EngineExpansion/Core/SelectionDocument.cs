using System.Text.Json;
namespace SprocketEngineExpansion.Core;

// Preserve the original extension exactly until a supported document is explicitly edited.
public sealed record SelectionDocument(EngineSelection Selection,string? Raw,bool Editable,string? Reason)
{
    public static SelectionDocument Default=>new(new(),null,true,null);
    public static SelectionDocument Read(string? raw)
    {
        try{return new(SelectionCodec.Decode(raw??throw new ArgumentException("Null extension.")),raw,true,null);}
        catch(Exception ex) when(ex is ArgumentException or JsonException)
        {return Opaque(raw,ex.Message);}
    }
    public static SelectionDocument Opaque(string? raw,string reason)=>new(new(),raw,false,reason);
    public SelectionDocument Edit(EngineSelection next)
    {
        if(!Editable)throw new InvalidOperationException("Unsupported engine metadata is preserved; edit with a compatible plugin version.");
        next.Validate();return new(next,null,true,null);
    }
    public string? Save()=>Raw??(!Editable?null:(!Selection.IsNative||Selection.BoostPercent!=25?SelectionCodec.Encode(Selection):null));
}
