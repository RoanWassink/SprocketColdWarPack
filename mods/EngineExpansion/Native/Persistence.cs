using HarmonyLib;
using Sprocket.Blueprints;
using Sprocket.Vehicles.Engines;
using SprocketEngineExpansion.Core;
namespace SprocketEngineExpansion.Native;

[HarmonyPatch]
internal static class Persistence
{
    [HarmonyPostfix,HarmonyPatch(typeof(EngineBlueprint),nameof(EngineBlueprint.Save))]
    private static void Save(EngineBlueprint __instance,IBlueprintSerializer __0)
    {
        try
        {
            if(Selections.RawValue(__instance,out var raw)){__0.AddValue(SelectionCodec.Key,raw!);return;}
            var encoded=Selections.Document(__instance).Save();if(encoded!=null)__0.AddValue(SelectionCodec.Key,encoded);
        }
        catch(Exception ex)
        {Plugin.Warn("save refused",ex);throw new InvalidOperationException("Engine Expansion metadata could not be preserved; save refused.",ex);}
    }
    [HarmonyPostfix,HarmonyPatch(typeof(EngineBlueprint),nameof(EngineBlueprint.Load))]
    private static void Load(EngineBlueprint __instance,IBlueprintDeserializer __0)
    {
        Selections.Clear(__instance);
        try
        {
            bool present=false;Il2CppSystem.Object? rawValue=null;
            if(__0.TryCast<BlueprintCopier>() is {} copy)
            {present=copy.values.ContainsKey(SelectionCodec.Key);if(present)rawValue=copy.values[SelectionCodec.Key];}
            else if(__0.TryCast<SerializationBlueprintWrapper>() is {} wrapper)
            {
                var entries=wrapper.info.GetEnumerator();while(entries.MoveNext())if(entries.Name==SelectionCodec.Key){present=true;rawValue=entries.Value;break;}
            }
            else {Plugin.Warn("load",new InvalidOperationException("Unknown native deserializer; native profile retained."));return;}
            if(present)
            {
                SelectionDocument document;
                try{document=SelectionDocument.Read(__0.GetString(SelectionCodec.Key));}
                catch(Exception ex){document=SelectionDocument.Opaque(null,ex.Message);}
                Selections.Load(__instance,document,rawValue);
                if(!document.Editable)Plugin.Warn("preserved metadata",new InvalidOperationException(document.Reason));
            }
        }
        catch(Exception ex){Plugin.Warn("load",ex);}
    }
    // Native Copy/Clone may route through the copier; this also covers direct Copy.
    [HarmonyPostfix,HarmonyPatch(typeof(Blueprint),nameof(Blueprint.Copy))]
    private static void Copy(Blueprint __0,Blueprint __1)
    {
        try{if(__0.TryCast<EngineBlueprint>() is {} source&&__1.TryCast<EngineBlueprint>() is {} target)Selections.Copy(source,target);}
        catch(Exception ex){Plugin.Warn("copy",ex);}
    }
}
