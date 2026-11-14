using HarmonyLib;
using Sprocket.AssetManagement;
using Sprocket.PartImporting;
using Il2CppInterop.Runtime.InteropTypes.Arrays;

namespace SprocketExpandedBallistics.Materials;
[HarmonyPatch]
internal static class EraMenuPresentation
{
    private static int warnings;
    [HarmonyPostfix,HarmonyPatch(typeof(PartDefinitionCardFactory),nameof(PartDefinitionCardFactory.CreateCard))]
    private static void Named(PartDefinition __0,PartDisplayCard __result)
    {
        try{if(__0!=null&&__result!=null){if(__0.guid==SprocketExpandedBallistics.CompositeModule.PartGuid)__result.Name=SprocketExpandedBallistics.CompositeModule.DisplayName;else if(EraGeneratedDefinitions.MenuLabels.TryGetValue(__0.guid,out var label))__result.Name=label;}}
        catch(Exception ex){Warn(ex);}
    }
    [HarmonyPostfix,HarmonyPatch(typeof(PartDefinitionCardFactory),nameof(PartDefinitionCardFactory.GetAllWithTags),new[]{typeof(Il2CppStringArray)})]
    private static void Menu(ref Il2CppReferenceArray<IAssetInfo> __result)=>FilterMenu(ref __result);
    [HarmonyPostfix,HarmonyPatch(typeof(PartDefinitionCardFactory),nameof(PartDefinitionCardFactory.GetAllWithTags),new[]{typeof(string[])})]
    private static void ManagedMenu(ref Il2CppReferenceArray<IAssetInfo> __result)=>FilterMenu(ref __result);
    private static void FilterMenu(ref Il2CppReferenceArray<IAssetInfo> cards)
    {
        try
        {
            if(cards==null)return;
            var visible=cards.Where(asset=>asset?.TryCast<PartDisplayCard>() is not {} card||!EraGeneratedDefinitions.LegacyPartGuids.Contains(card.PartGuid)).ToArray();
            if(visible.Length!=cards.Length)cards=new Il2CppReferenceArray<IAssetInfo>(visible);
            // Direct definition/GUID lookups stay intact for legacy blueprint loading.
        }
        catch(Exception ex){Warn(ex);}
    }
    private static void Warn(Exception ex){if(warnings++<16)MaterialFeature.ModLog.LogWarning("Optional ERA menu presentation: "+ex.Message);}
}
