using System.Globalization;
using HarmonyLib;
using Il2CppInterop.Runtime;
using Sprocket.UI;
using Sprocket.Vehicles.PlateStructures;
using Sprocket.Vehicles.PlateStructures.Design;
using SprocketExpandedBallistics.Materials;
using UnityEngine.Events;

namespace SprocketExpandedBallistics;

[HarmonyPatch]
internal static class RuntimePlateStackUi
{
    private static bool open=true,reverseDirection;
    private static string selectionKey="",gapText="1",status="";
    private static List<PlateStructure> order=new();
    internal static void Clear(){selectionKey="";status="";order.Clear();}

    [HarmonyPostfix,HarmonyPatch(typeof(PlateStructureEditor),nameof(PlateStructureEditor.OnGUI))]
    private static void Draw(PlateStructureEditor __instance,IGUILayout layout)
    {
        SprocketExpandedBallistics.Materials.Ui.Guard("Plate stack editor",()=>
        {
            IReadOnlyList<PlateStructure> selected;
            try{selected=RuntimePlateStack.GetSelection(__instance);}catch{return;}
            if(selected.Count<2)return;
            // Native multi-selection does not promise that its first item owns
            // the visible inspector. Draw once on the first active plate editor.
            var editors=__instance.Gateway.Editor.SubEditors.ActiveEditors;
            var editorCount=editors.Cast<Il2CppSystem.Collections.Generic.IReadOnlyCollection<Sprocket.VehicleDesigner.DesignerEditor>>().Count;
            for(int i=0;i<editorCount;i++)
            {
                var active=editors[i]?.TryCast<PlateStructureEditor>();
                if(active==null||!active.IsValid()||!selected.Any(p=>p.Pointer==active.Component.Pointer))continue;
                if(active.Pointer!=__instance.Pointer)return;
                break;
            }
            var ui=layout.TryCast<IGUIElementDrawer>();if(ui==null)return;
            var key=__instance.Component.Vehicle.Pointer+":"+string.Join(",",selected.Select(p=>p.Pointer.ToInt64()).OrderBy(p=>p));
            if(key!=selectionKey){selectionKey=key;order=selected.ToList();status="";}
            var labels=new Il2CppSystem.Collections.Generic.List<string>();
            foreach(var plate in selected)
            {
                var name=DamageFeedTruth.StructureLabel(plate.Mesh?.Name,plate.Blueprint?.Name,"Plate #"+plate.VUID.Value);
                var material=MaterialDatabase.Materials.FirstOrDefault(m=>m.Id==plate.armourTechID);
                labels.Add(name+" — "+(material==null?plate.armourTechID:MaterialDatabase.MenuLabel(material))+" ["+plate.VUID.Value+"]");
            }
            layout.EndAllDropdowns();
            layout.BeginDropdown("Armour array",open,SprocketExpandedBallistics.Materials.Ui.BoolCallback(value=>open=value));
            try
            {
                ui.InfoField("Layer 1 stays in place. Flat plates are rotated and aligned automatically.",1);
                for(int row=0;row<order.Count;row++)
                {
                    var captured=row;
                    var index=Enumerable.Range(0,selected.Count).First(i=>selected[i].Pointer==order[row].Pointer);
                    ui.Dropdown(row==0?"Layer 1 — reference":"Layer "+(row+1),labels.Cast<Il2CppSystem.Collections.Generic.IReadOnlyList<string>>(),index,
                        SprocketExpandedBallistics.Materials.Ui.IntCallback(value=>
                        {
                            if(value<0||value>=selected.Count||captured>=order.Count)return;
                            var other=order.FindIndex(p=>p.Pointer==selected[value].Pointer);if(other<0)return;
                            (order[captured],order[other])=(order[other],order[captured]);status="";__instance.RequestRedraw();
                        }),"Choose the plate at this layer. Changing a layer swaps the two plates in the list.");
                }
                ui.InputField("Clear gap (mm)",gapText,DelegateSupport.ConvertDelegate<UnityAction<string>>(new Action<string>(value=>gapText=value))!);
                ui.ToggleField("Reverse stack direction",reverseDirection,SprocketExpandedBallistics.Materials.Ui.BoolCallback(value=>{reverseDirection=value;status="";}),"Stack on the opposite side of the reference plate.");
                var tip=new UITooltip("Build armour stack","Rotate and align the selected flat plates using their built physical thickness. Keep attachments, shape, material and scale. One undo restores their positions and rotations.");
                ui.Button("Build armour stack",SprocketExpandedBallistics.Materials.Ui.Callback(()=>
                {
                    SprocketExpandedBallistics.Materials.Ui.Guard("Build armour stack",()=>
                    {
                        if((!double.TryParse(gapText,NumberStyles.Float,CultureInfo.InvariantCulture,out var gap)&&!double.TryParse(gapText,NumberStyles.Float,CultureInfo.CurrentCulture,out gap))||!double.IsFinite(gap)||gap<0||gap>1000)
                            status="Enter a clear gap between 0 and 1000 mm.";
                        else RuntimePlateStack.TryBuildStack(__instance,order.ToArray(),gap,reverseDirection,out status);
                        __instance.RequestRedraw();
                    });
                }),ref tip);
                if(status.Length>0)ui.InfoField(status,1);
            }
            finally{layout.EndAllDropdowns();}
        });
    }
}
