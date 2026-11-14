using System.Reflection;
using UnityEngine;
namespace SprocketExpandedBallistics;

// Reflection preserves the existing soft Keybinds dependency and saved bindings.
internal static class CassetteVisualKeys
{
    private static object? key;
    private static PropertyInfo? pressed;
    private static Type? api;
    private static float retryAt;
    private static int warnings;
    internal static void Configure()
    {
        if(key!=null||Time.unscaledTime<retryAt)return;
        retryAt=Time.unscaledTime+2;
        try
        {
            api=AppDomain.CurrentDomain.GetAssemblies().Select(a=>a.GetType("SprocketKeybinds.Keybinds",false)).FirstOrDefault(t=>t!=null);
            if(api==null)return;
            BallisticsKeybindMetadata.Register(api);
            var register=api.GetMethods(BindingFlags.Public|BindingFlags.Static).Single(m=>m.Name=="RegisterButton"&&m.GetParameters().Length==6);
            key=register.Invoke(null,new object?[]{"sprocket.expandedballistics",BallisticsKeybindMetadata.Name,"cassette-layers","Show cassette layers","",null});
            pressed=key?.GetType().GetProperty("WasPressedThisFrame");
        }
        catch(Exception ex){if(warnings++<3)Plugin.ModLog.LogWarning("[Plate visual] Keybinds unavailable: "+ex.Message);}
    }
    internal static bool Pressed
    {
        get{Configure();try{return key!=null&&pressed?.GetValue(key) is true;}catch{return false;}}
    }
    internal static void Clear()
    {
        try{api?.GetMethod("Unregister",new[]{typeof(string),typeof(string)})?.Invoke(null,new object?[]{"sprocket.expandedballistics","cassette-layers"});}
        catch(Exception ex){Plugin.ModLog.LogWarning("[Plate visual] Keybind cleanup: "+ex.Message);}
        key=null;pressed=null;api=null;
    }
}
