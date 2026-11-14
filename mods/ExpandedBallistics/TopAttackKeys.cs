using System.Reflection;
using UnityEngine;

namespace SprocketExpandedBallistics;

// Optional keybind for the top-attack target lock, registered through Sprocket Keybinds by
// reflection (soft dependency; unbound by default, like the ammunition loadout keys). Lazy
// retries cover Keybinds loading after this plugin, and the lock logic degrades to
// "no hotkey, no lock" when Keybinds is absent.
internal static class TopAttackKeys
{
    private const string PluginId="sprocket.expandedballistics";
    private const string Group=BallisticsKeybindMetadata.Name;
    private const string Action="topattack-lock";
    private const string Label="Lock top-attack target";
    private static object? key;
    private static PropertyInfo? held;
    private static Type? api;
    private static int warnings;
    private static float retryAt;

    internal static void Configure()
    {
        if(key!=null||Time.unscaledTime<retryAt)return;
        retryAt=Time.unscaledTime+2f;
        try
        {
            var type=AppDomain.CurrentDomain.GetAssemblies().Select(a=>a.GetType("SprocketKeybinds.Keybinds",false)).FirstOrDefault(t=>t!=null);
            if(type==null)return;
            api=type;
            BallisticsKeybindMetadata.Register(type);
            var register=type.GetMethods(BindingFlags.Public|BindingFlags.Static).Single(m=>m.Name=="RegisterButton"&&m.GetParameters().Length==6);
            key=register.Invoke(null,new object?[]{PluginId,Group,Action,Label,"",null});
            if(key==null)return;
            held=key.GetType().GetProperty("IsHeld");
            Plugin.ModLog.LogInfo("[Top-attack keys] Ballistics / Lock top-attack target registered; existing bindings retained.");
        }
        catch(Exception ex){Warn(ex);}
    }

    internal static bool Held
    {
        get
        {
            Configure();
            try{return key!=null&&held!=null&&held.GetValue(key) is true;}
            catch(Exception ex){Warn(ex);return false;}
        }
    }

    internal static void Clear()
    {
        try
        {
            var unregister=api?.GetMethod("Unregister",new[]{typeof(string),typeof(string)});
            unregister?.Invoke(null,new object?[]{PluginId,Action});
        }
        catch(Exception ex){Warn(ex);}
        key=null;held=null;api=null;
    }

    private static void Warn(Exception ex)
    {
        if(warnings++<3)Plugin.ModLog.LogWarning("[Top-attack keys] Optional keybinds unavailable: "+ex.Message);
    }
}
