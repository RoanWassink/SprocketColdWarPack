using System.Reflection;
using System.Security.Cryptography;
using BepInEx;
using HarmonyLib;
using Sprocket.PartImporting;
using UnityEngine;

namespace SprocketReferenceImages;

internal static class IconHooks
{
    private static Texture2D? texture;
    private static Sprite? sprite;
    internal static void Install(Harmony harmony)
    {
        using(var file=File.OpenRead(Path.Combine(Paths.GameRootPath,"GameAssembly.dll")))
        using(var sha=SHA256.Create())
            if(Convert.ToHexString(sha.ComputeHash(file))!="3E58082804D97CF32FFD400E7427E767EF9576D23DB71B2BE96FBB7F825FC17B")
                throw new InvalidOperationException("GameAssembly changed; H3 requires a new ICF/RVA audit.");
        var target = AccessTools.DeclaredMethod(typeof(PartDefinitionCardFactory), nameof(PartDefinitionCardFactory.CreateCard), new[] { typeof(PartDefinition) })
            ?? throw new MissingMethodException("H3 CreateCard(PartDefinition) unavailable.");
        harmony.Patch(target, postfix: new HarmonyMethod(typeof(IconHooks), nameof(Postfix)));
    }
    private static void Postfix(PartDefinition __0, PartDisplayCard __result)
    {
        try
        {
            if (__0 == null || __result == null || __0.guid != BoardPolicy.PartGuid || __0.name != BoardPolicy.PartName) return;
            if (sprite == null)
            {
                texture = ImageLibrary.Decode(ImageLibrary.Resource("reference-image-icon.png"), "icon");
                sprite = Sprite.Create(texture,new Rect(0,0,texture.width,texture.height),new Vector2(.5f,.5f));
                sprite.hideFlags = HideFlags.HideAndDontSave;
            }
            __result.Icon = sprite;
        }
        catch(Exception ex) { Plugin.WarnOnce("icon error: " + ex.Message); }
    }
    internal static void Clear()
    {
        if(sprite != null) UnityEngine.Object.Destroy(sprite);
        if(texture != null) UnityEngine.Object.Destroy(texture);
        sprite=null; texture=null;
    }
}
