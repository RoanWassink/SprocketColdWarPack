namespace SprocketExpandedBallistics.Materials;

// The native part materials remain loaded; this is only the player's face menu.
// New shared face/part recipes join FaceMaterials deliberately. New part-only
// materials are hidden automatically through the binding catalogue.
internal static class MaterialMenuVisibility
{
    internal static readonly HashSet<string> FaceMaterials=new(StringComparer.Ordinal)
    {
        "kontakt1Active","kontakt5Active","reliktActive","reliktTurretActive",
        "nizh19Active","nizh34Active","nizh1MActive","nizhLmInsert",
        "duplet2MActive","duplet2MHullActive","aratIActive","aratIIActive",
        "dynaActive","romorAActive","bratActive"
        ,"rha","sheetMetal","sand","advancedArmourSteel","aluminiumArmour",
        "aramidSpallLiner","castArmour","ceramicArray","cwepGlassTextolite",
        "elastomerNERA","heavyAlloyInsert","highHardnessSteel","magnesiumAlloy",
        "structuralSteel","titaniumArmour","ebWood","ebRubber","ebFabric","ebGlass"
    };
    internal static readonly HashSet<string> RetiredPresets=new(StringComparer.Ordinal)
    {
        "cwepLightEraCassette","cwepHeavyEraCassette","cwepNeraCassette","cwepPassiveComposite",
        "varmaComposite","kontakt1Cassette","kontakt5Cassette","reliktCassette","nizhCassette","dupletCassette",
        "experimentalKontakt5Cassette","experimentalReliktCassette","experimentalNizhCassette",
        "experimentalNizh19Insert","experimentalNizh34Insert","nizhLegacyActive",
        "dupletLegacySingleActive","dupletLegacyIntegratedActive","dupletHullActive","dupletHullZonesActive"
    };
    internal static readonly HashSet<string> ComposerOnlyMaterials=new(StringComparer.Ordinal)
    {"ceramicArray","cwepGlassTextolite","elastomerNERA"};
    internal static bool ComposerOnly(string id)=>SprocketExpandedBallistics.CompositePlate.IsCassette(id)||RetiredPresets.Contains(id)||ComposerOnlyMaterials.Contains(id);
    internal static string CurrentPrompt(string id,string label)=>ComposerOnly(id)
        ?label+" — Assigned by the Cassette Composer":"Choose a player material";
    internal static bool Visible(string id,bool usedByPlaceablePart,bool compositePlate=false)=>
        !ComposerOnly(id)&&(FaceMaterials.Contains(id)||!usedByPlaceablePart);
    // 0.22.18: the face menu is split in two groups behind a toggle.
    internal enum MenuGroup{Basic,Composites}
    internal static MenuGroup GroupOf(string id)=>SprocketExpandedBallistics.CompositePlate.IsCassette(id)?MenuGroup.Composites:MenuGroup.Basic;
    internal static bool Visible(string id,bool usedByPlaceablePart,bool compositePlate,MenuGroup group)=>
        GroupOf(id)==group&&Visible(id,usedByPlaceablePart,compositePlate);
    internal static string GroupLabel(MenuGroup group)=>group==MenuGroup.Basic?"Basic materials":"Composites";
    internal static string EmptyReason(MenuGroup group,bool compositePlate)=>group==MenuGroup.Basic
        ?"No basic materials are available for this plate."
        :compositePlate?"No composite presets are available for this plate at this vehicle date."
        :"Composite presets apply to composite plates only. Convert this plate with Open composer first.";
    // Maps a native dropdown index back to the material id of the filtered list.
    internal static string? SelectedId(IReadOnlyList<string> ids,int dropdownIndex,bool hasPrompt)
    {
        var index=MaterialIndex(dropdownIndex,hasPrompt);
        return index<0||index>=ids.Count?null:ids[index];
    }
    // The toggle starts on the group of the plate's current material, then keeps the
    // user's choice while the same plate stays selected. An empty group is never shown.
    internal sealed class GroupState
    {
        long? plate;MenuGroup chosen;
        internal MenuGroup Resolve(long plateKey,string currentId,bool basicHasItems,bool compositesHasItems)
        {
            if(plate!=plateKey){plate=plateKey;chosen=GroupOf(currentId);}
            var group=chosen;
            if(group==MenuGroup.Basic&&!basicHasItems&&compositesHasItems)group=MenuGroup.Composites;
            else if(group==MenuGroup.Composites&&!compositesHasItems&&basicHasItems)group=MenuGroup.Basic;
            return group;
        }
        internal void Choose(long plateKey,MenuGroup group){plate=plateKey;chosen=group;}
    }    // Native dropdown uses this unbound prompt when a legacy material is active.
    // Choosing it cannot write a replacement to a blueprint.
    internal static int MaterialIndex(int dropdownIndex,bool hasPrompt)=>dropdownIndex-(hasPrompt?1:0);
}
