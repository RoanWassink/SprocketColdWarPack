using DesignerFramework.AssetManagement;
using DesignerFramework.UI;
using HarmonyLib;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using Sprocket.PartImporting;
using Sprocket.Selection;
using Sprocket.VehicleDesigner;
using Sprocket.VehicleDesigner.Access;

namespace SprocketExpandedBallistics.Materials;

// Uses Thermal Sight's accepted native SelectionPanel/OptionGroup adapter.
// Only exact registered cards receive a marker copied by the native Option converter.
[HarmonyPatch]
internal static class EraPartsTab
{
    private const string Label="Reactive armor";
    private const string Marker="sprocket.expandedballistics.materials.era.part:";
    private static readonly HashSet<string> CanonicalGuids=new(EraPresetRegistry.Presets
        .Select(p=>p.PartGuid).Where(g=>!EraGeneratedDefinitions.LegacyPartGuids.Contains(g)),StringComparer.Ordinal);
    private static bool warned;
    // Selection intent belongs to one native panel, cleared by tab navigation and panel teardown.
    private static readonly HashSet<IntPtr> KeepReactive=new();
    private static readonly HashSet<IntPtr> SelectingReactive=new();
    // Exact panel ownership from the native construction path. The panel pointer
    // is bound to that context.Editor and resolved to its current Target at use.
    [HarmonyPostfix,HarmonyPatch(typeof(VehicleComponentSelectionDisplay),nameof(VehicleComponentSelectionDisplay.CreatePlugins))]
    private static void Bind(VehicleComponentSelectionDisplay __instance,EditorPluginLoadContext __1)
        =>EraPartAvailability.Bind(__instance,__1);
    [HarmonyPostfix,HarmonyPatch(typeof(VehicleComponentSelectionDisplayEditorPlugin),nameof(VehicleComponentSelectionDisplayEditorPlugin.Release))]
    private static void Release(VehicleComponentSelectionDisplayEditorPlugin __instance)
        =>EraPartAvailability.Release(__instance.display);
    [HarmonyPrefix,HarmonyPatch(typeof(VehicleEditor),nameof(VehicleEditor.Detach))]
    private static void Detach(VehicleEditor __instance)
        =>EraPartAvailability.ReleaseEditor(__instance.Pointer);
    [HarmonyPrefix,HarmonyPatch(typeof(SelectionPanel),nameof(SelectionPanel.OnClickSelection))]
    private static bool Selected(SelectionPanel __instance,int __0,out bool __state)
    {
        __state=false;
        try
        {
            var group=__instance.activeGroup?.TryCast<OptionGroup>();
            if(group?.Name==Label&&group.items!=null&&__0>=0&&__0<group.items.Length&&Identity(group.items[__0])!=null)
            {
                // A menu built before a date/editor change must not keep offering a
                // stale own card. Verify the exact bound target again at use.
                if(!EraPartAvailability.AllowsOwnPart(__instance)){KeepReactive.Remove(__instance.Pointer);return false;}
                if(KeepReactive.Count<64||KeepReactive.Contains(__instance.Pointer))
                {KeepReactive.Add(__instance.Pointer);__state=SelectingReactive.Add(__instance.Pointer);}
            }
            else KeepReactive.Remove(__instance.Pointer);
        }
        catch(Exception ex){KeepReactive.Remove(__instance.Pointer);Warn(ex);}
        return true;
    }
    [HarmonyFinalizer,HarmonyPatch(typeof(SelectionPanel),nameof(SelectionPanel.OnClickSelection))]
    private static void SelectionFinished(SelectionPanel __instance,bool __state)
    {if(__state)SelectingReactive.Remove(__instance.Pointer);}
    [HarmonyPrefix,HarmonyPatch(typeof(SelectionPanel),nameof(SelectionPanel.OnClickTab))]
    private static void Forget(SelectionPanel __instance)=>KeepReactive.Remove(__instance.Pointer);
    [HarmonyPrefix,HarmonyPatch(typeof(SelectionPanel),nameof(SelectionPanel.Release))]
    private static void Released(SelectionPanel __instance)
    {KeepReactive.Remove(__instance.Pointer);SelectingReactive.Remove(__instance.Pointer);}
    [HarmonyPrefix,HarmonyPatch(typeof(SelectionPanel),nameof(SelectionPanel.ClearAll))]
    private static void Cleared(SelectionPanel __instance)
    {
        // A synchronous option callback can clear the panel before rebuilding its options.
        // Outside that exact callback, ClearAll is a genuine context/lifetime boundary.
        if(!SelectingReactive.Contains(__instance.Pointer))KeepReactive.Remove(__instance.Pointer);
    }

    [HarmonyPostfix,HarmonyPatch(typeof(PartDefinitionCardFactory),nameof(PartDefinitionCardFactory.CreateCard))]
    private static void Mark(PartDefinition __0,PartDisplayCard __result)
    {
        try
        {
            if(__0==null||__result==null||__0.guid!=__result.PartGuid||!CanonicalGuids.Contains(__result.PartGuid))return;
            var tag=Marker+__result.PartGuid;
            if(__result.Tags?.Contains(tag)==true)return;
            // Copy the exact own display card's tags; native JSON definitions and other cards are untouched.
            __result.Tags=new Il2CppStringArray((__result.Tags?.ToArray()??Array.Empty<string>()).Append(tag).ToArray());
        }
        catch(Exception ex){Warn(ex);}
    }
    private static string? Identity(Option? option)
    {
        if(option?.Tags==null)return null;
        foreach(var tag in option.Tags)
            if(tag!=null&&tag.StartsWith(Marker,StringComparison.Ordinal)&&CanonicalGuids.Contains(tag.Substring(Marker.Length)))
                return tag.Substring(Marker.Length);
        return null;
    }
    [HarmonyPrefix,HarmonyPatch(typeof(SelectionPanel),nameof(SelectionPanel.SetDisplayOptions))]
    private static void Groups(SelectionPanel __instance,ref Il2CppReferenceArray<ISelectionGroup> __0,ref int __1)
    {
        if(__0==null){KeepReactive.Remove(__instance.Pointer);return;}
        try
        {
            // Evaluate the exact bound owner once per rebuild, but always against
            // its current target. Unknown/conflicted/ineligible contexts remove
            // only the registered own ERA cards.
            var available=EraPartAvailability.AllowsOwnPart(__instance);
            var groups=new List<ISelectionGroup>();var era=new List<Option>();var seen=new HashSet<string>(StringComparer.Ordinal);
            int newActive=-1,oldIndex=0;bool activeMoved=false,anyOwn=false;
            foreach(var item in __0)
            {
                bool wasActive=oldIndex++==__1;
                var group=item?.TryCast<OptionGroup>();
                if(group?.items==null){if(wasActive)newActive=groups.Count;groups.Add(item!);continue;}
                var retained=new List<Option>();bool changed=false;
                foreach(var option in group.items)
                {
                    if(Identity(option) is {} guid)
                    {
                        anyOwn=true;changed=true;
                        if(available&&seen.Add(guid))era.Add(option);
                        // Unavailable own cards are deliberately discarded, not
                        // retained in their native group as selectable options.
                    }
                    else retained.Add(option!);
                }
                if(!changed){if(wasActive)newActive=groups.Count;groups.Add(item!);continue;}
                if(retained.Count>0)
                {
                    var copy=new OptionGroup(group.Name,retained.ToArray()){Flags=group.Flags,InteractableTest=group.InteractableTest};
                    if(wasActive)newActive=groups.Count;groups.Add(copy.Cast<ISelectionGroup>());
                }
                else if(wasActive)activeMoved=true;
            }
            if(!anyOwn){KeepReactive.Remove(__instance.Pointer);return;}
            if(era.Count>0)
            {
                int structural=groups.FindIndex(g=>g?.TryCast<OptionGroup>()?.Name=="Structural");
                int insertion=structural<0?groups.Count:structural+1;
                var ordered=EraMenuOrder.Sort(era,Identity,option=>option.Name).ToArray();
                var own=new OptionGroup(Label,ordered);groups.Insert(insertion,own.Cast<ISelectionGroup>());
                if(newActive>=insertion)newActive++;
                if(activeMoved||newActive<0)newActive=insertion;
                if(KeepReactive.Contains(__instance.Pointer))newActive=insertion;
            }
            else
            {
                // No own card remains: remap the active index to a real native
                // group (or 0 when the panel is empty) before committing.
                KeepReactive.Remove(__instance.Pointer);
                newActive=groups.Count==0?0:Math.Clamp(activeMoved?__1:newActive,0,groups.Count-1);
            }
            var result=new Il2CppReferenceArray<ISelectionGroup>(groups.ToArray());
            // Commit only after successful reconstruction. Exceptions retain original native inputs.
            __0=result;__1=newActive;
        }
        catch(Exception ex){Warn(ex);}
    }
    private static void Warn(Exception ex)
    {if(!warned){warned=true;MaterialFeature.ModLog.LogWarning("Optional Reactive armor tab unavailable; native selection retained: "+ex.Message);}}
}
