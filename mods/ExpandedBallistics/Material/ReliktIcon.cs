using HarmonyLib;
using Sprocket.PartImporting;
using UnityEngine;

namespace SprocketExpandedBallistics.Materials;

[HarmonyPatch]
internal static class ReliktIcon
{
    internal const string PartGuid="ef15b8e5-d4c3-4d38-8b03-f07bb395ab63";
    private static readonly Dictionary<string,Sprite> Icons=new();
    private static readonly HashSet<string> Failed=new();
    [HarmonyPostfix,HarmonyPatch(typeof(PartDefinitionCardFactory),nameof(PartDefinitionCardFactory.CreateCard))]
    private static void Created(PartDefinition __0,PartDisplayCard __result)
    {
        if(__0==null||__result==null)return;
        var preset=EraPresetRegistry.Presets.FirstOrDefault(p=>p.PartGuid==__0.guid);
        var file=preset?.Icon??(SprocketExpandedBallistics.CompositePlate.IconMap.TryGetValue(__0.guid,out var mapped)?mapped:null);
        if(file==null||Failed.Contains(__0.guid))return;
        try
        {
            if(!Icons.TryGetValue(__0.guid,out var icon))
            {
                string path=Path.Combine(Path.GetDirectoryName(typeof(MaterialFeature).Assembly.Location)!,"era-assets",file);
                var texture=new Texture2D(2,2,TextureFormat.RGBA32,false){hideFlags=HideFlags.HideAndDontSave};
                if(!ImageConversion.LoadImage(texture,File.ReadAllBytes(path),false))throw new InvalidDataException("Part icon decode failed");
                icon=Sprite.Create(texture,new Rect(0,0,texture.width,texture.height),new Vector2(.5f,.5f),100);
                icon.hideFlags=HideFlags.HideAndDontSave;
                Icons.Add(__0.guid,icon);
            }
            __result.Icon=icon;
        }
        catch(Exception ex){Failed.Add(__0.guid);MaterialFeature.ModLog.LogWarning("Optional part icon: "+ex.Message);}
    }
}
