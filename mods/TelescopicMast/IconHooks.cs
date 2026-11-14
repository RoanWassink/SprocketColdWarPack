using HarmonyLib;
using Sprocket.PartImporting;
using SprocketTelescopicMast.Io;
using UnityEngine;
namespace SprocketTelescopicMast;

[HarmonyPatch]
internal static class IconHooks
{
    private static readonly Dictionary<string,Sprite> Icons = new(StringComparer.Ordinal);
    private static readonly Dictionary<string,Texture2D> Textures = new(StringComparer.Ordinal);
    private static readonly HashSet<string> Attempted = new(StringComparer.Ordinal);
    [HarmonyPostfix,HarmonyPatch(typeof(PartDefinitionCardFactory),nameof(PartDefinitionCardFactory.CreateCard))]
    private static void Card(PartDefinition __0,PartDisplayCard __result)
    {
        if(__0 == null || __result == null) return;
        // The mast's own icons keep priority. An API registration is read only on the first card
        // for that GUID; a failure leaves the native card icon untouched.
        string? file=__0.guid switch {
            "ec6a0aa7-af9e-4eba-9829-0c3bd844f30c" => "telescope-base-icon.png",
            "df5a04af-2904-42d3-b958-2d816d8d1e57" => "telescope-part-icon.png",
            "754d193e-e56a-4740-9138-0852fb0ef119" => "hinge-base-icon.png",
            "b907b6b7-ef88-4d12-aeb5-75e80ab53875" => "hinge-leaf-icon.png",
            "5c1e9a37-2d4b-4f86-9e0a-8b7d3f61c2a4" => "hinge-mount-icon.png",
            "a0955a06-58b8-4da3-9dc5-5080cb54c96b" => "rotator-base-icon.png",
            "08add1ba-efce-4246-a7a6-30b69297e7e6" => "rotator-plate-icon.png",
            _ => null
        };
        string key;
        string path;
        if(file != null)
        {
            key = "asset:" + file;
            path = Path.Combine(Path.GetDirectoryName(typeof(Plugin).Assembly.Location)!,"assets",file);
        }
        else
        {
            if(!IoApiCore.TryGetPartIcon(__0.guid, out path)) return;
            key = "api:" + __0.guid;
        }
        try {
            if(Attempted.Add(key)) {
                var texture=new Texture2D(2,2,TextureFormat.RGBA32,false) { hideFlags=HideFlags.HideAndDontSave };
                Textures[key]=texture;
                if(!ImageConversion.LoadImage(texture,File.ReadAllBytes(path),false)) throw new InvalidOperationException("Invalid icon PNG");
                var icon=Sprite.Create(texture,new Rect(0,0,texture.width,texture.height),new Vector2(.5f,.5f),100);
                icon.hideFlags=HideFlags.HideAndDontSave;
                Icons[key]=icon;
                Plugin.Instance.Log.LogInfo($"[Mast] Loaded selector icon {(file ?? __0.guid)}.");
            }
            if(Icons.TryGetValue(key,out var cached)) __result.Icon=cached;
        } catch(Exception ex) { MastRuntime.Warn("Icon "+key,ex); }
    }
}
