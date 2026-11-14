using Sprocket.Vehicles.Engines;
using SprocketEngineExpansion.Core;
namespace SprocketEngineExpansion.Native;

internal static class Selections
{
    // Retain the native wrapper: pointer-keyed weak wrappers can vanish while the game still owns the blueprint.
    private sealed record Entry(EngineBlueprint Blueprint,SelectionDocument Document,Il2CppSystem.Object? RawValue,bool PreserveRawValue);
    private static readonly Dictionary<IntPtr,Entry> values=new();
    internal sealed record Snapshot(SelectionDocument Document,Il2CppSystem.Object? Raw,bool Preserve);
    internal static Snapshot Capture(EngineBlueprint bp)=>values.TryGetValue(bp.Pointer,out var e)?new(e.Document,e.RawValue,e.PreserveRawValue):new(SelectionDocument.Default,null,false);
    internal static void Restore(EngineBlueprint bp,Snapshot snapshot)=>values[bp.Pointer]=new(bp,snapshot.Document,snapshot.Raw,snapshot.Preserve);
    internal static EngineSelection Get(EngineBlueprint bp)
    {
        return Document(bp).Selection;
    }
    internal static SelectionDocument Document(EngineBlueprint bp)=>values.TryGetValue(bp.Pointer,out var entry)?entry.Document:SelectionDocument.Default;
    internal static void Set(EngineBlueprint bp,EngineSelection value)
    {
        values[bp.Pointer]=new(bp,Document(bp).Edit(value),null,false);
    }
    internal static void Load(EngineBlueprint bp,SelectionDocument document,Il2CppSystem.Object? rawValue)=>values[bp.Pointer]=new(bp,document,rawValue,true);
    internal static bool RawValue(EngineBlueprint bp,out Il2CppSystem.Object? value)
    {value=null;if(!values.TryGetValue(bp.Pointer,out var entry)||!entry.PreserveRawValue)return false;value=entry.RawValue;return true;}
    internal static void Copy(EngineBlueprint source,EngineBlueprint target)
    {if(values.TryGetValue(source.Pointer,out var entry))values[target.Pointer]=entry with {Blueprint=target};else Clear(target);}
    internal static void Clear(EngineBlueprint bp)=>values.Remove(bp.Pointer);
}
