using HarmonyLib;
using Sprocket.UI;
using Sprocket.Vehicles.PlateStructures.Design;
using SprocketExpandedBallistics.Materials;

namespace SprocketExpandedBallistics;

// Optional editor feature: no changes to normal duplication or combat simulation.
[HarmonyPatch]
internal static class RuntimeIndependentStructureUi
{
    private static bool open=true;
    private static IntPtr selected;
    private static string status="";
    internal static void Clear(){selected=IntPtr.Zero;status="";}

    [HarmonyPostfix, HarmonyPatch(typeof(PlateStructureEditor),nameof(PlateStructureEditor.OnGUI))]
    private static void Draw(PlateStructureEditor __instance,IGUILayout layout)
    {
        SprocketExpandedBallistics.Materials.Ui.Guard("Independent structure editor",()=>
        {
            var component=__instance.Component;
            if(component==null||!component.Applique||!component.HasMesh)return;
            var ui=layout.TryCast<IGUIElementDrawer>();
            if(ui==null)return;
            if(selected!=component.Pointer){selected=component.Pointer;status="";}
            var linked=RuntimeIndependentStructure.CanMakeIndependent(component);
            if(linked&&status.StartsWith("Structure is independent",StringComparison.Ordinal))status="";
            layout.EndAllDropdowns();
            layout.BeginDropdown("Structure editing",open,SprocketExpandedBallistics.Materials.Ui.BoolCallback(value=>open=value));
            try
            {
                if(linked)
                {
                    var tip=new UITooltip("Make independent","Keep this structure's current shape and placement, but edit it separately from its linked copies.");
                    ui.Button("Make independent",SprocketExpandedBallistics.Materials.Ui.Callback(()=>
                    {
                        SprocketExpandedBallistics.Materials.Ui.Guard("Make structure independent",()=>
                        {
                            RuntimeIndependentStructure.TryMakeIndependent(__instance,out status);
                            __instance.RequestRedraw();
                        });
                    }),ref tip);
                }
                else ui.InfoField("No linked addon structure found.",1);
                if(status.Length>0)ui.InfoField(status,1);
            }
            finally{layout.EndAllDropdowns();}
        });
    }
}



