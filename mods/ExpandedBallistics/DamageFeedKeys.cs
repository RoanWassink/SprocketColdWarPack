using System.Reflection;
using UnityEngine;

namespace SprocketExpandedBallistics;

/// <summary>
/// Global damage-feed controls registered through Sprocket Keybinds. The feed is
/// no longer toggled from the cannon inspector: these shared actions keep the
/// existing "Damage feed" configuration entries and are unbound by default.
/// Registration is lazy so Keybinds may load after this plugin.
/// </summary>
internal static class DamageFeedKeys
{
    private const string PluginId=BallisticsKeybindMetadata.Owner;
    private const string Group=BallisticsKeybindMetadata.Name;
    private static readonly string[] Actions=DamageFeedKeyPolicy.Actions;
    private static readonly string[] Labels=DamageFeedKeyPolicy.Labels;

    private static object?[] keys=Array.Empty<object?>();
    private static PropertyInfo? pressed;
    private static Type? api;
    private static int warnings;
    private static float retryAt;

    /// <summary>Stable action IDs, exposed for synthetic registration checks.</summary>
    internal static IReadOnlyList<string> ActionIds=>Actions;

    internal static void Configure()
    {
        try
        {
            if((keys.Length==Actions.Length&&keys.All(k=>k!=null))||Time.unscaledTime<retryAt)return;
            retryAt=Time.unscaledTime+2f;
            var type=AppDomain.CurrentDomain.GetAssemblies().Select(a=>a.GetType("SprocketKeybinds.Keybinds",false)).FirstOrDefault(t=>t!=null);
            if(type==null)return;
            api=type;
            BallisticsKeybindMetadata.Register(type);
            var register=type.GetMethods(BindingFlags.Public|BindingFlags.Static).Single(m=>m.Name=="RegisterButton"&&m.GetParameters().Length==6);
            var registered=new object?[Actions.Length];
            try
            {
                for(var i=0;i<Actions.Length;i++)
                    registered[i]=register.Invoke(null,new object?[]{PluginId,Group,Actions[i],Labels[i],"",null});
            }
            catch
            {
                // Do not leave half a pair registered when the second action fails.
                Unregister();
                throw;
            }
            if(registered.Any(k=>k==null)){Unregister();return;}
            keys=registered;
            pressed=keys[0]?.GetType().GetProperty("WasPressedThisFrame");
            Plugin.ModLog.LogInfo("[Damage feed keys] Global feed toggle and corner cycle registered (unbound by default).");
        }
        catch(Exception ex){Warn(ex);}
    }

    internal static (bool Toggle,bool Corner) Poll()
    {
        if(keys.Length!=Actions.Length)return default;
        try{return (Down(0),Down(1));}
        catch(Exception ex){Warn(ex);return default;}
    }

    private static bool Down(int index)
    {
        if(pressed==null||index>=keys.Length||keys[index]==null)return false;
        return pressed.GetValue(keys[index]) is true;
    }

    private static void Unregister()
    {
        try
        {
            var unregister=api?.GetMethod("Unregister",new[]{typeof(string),typeof(string)});
            if(unregister!=null)foreach(var action in Actions)unregister.Invoke(null,new object[]{PluginId,action});
        }
        catch(Exception ex){Warn(ex);}
    }

    internal static void Clear()
    {
        Unregister();
        keys=Array.Empty<object?>();
        pressed=null;
        api=null;
        retryAt=0;
    }

    private static void Warn(Exception ex)
    {
        if(warnings++<3)Plugin.ModLog.LogWarning("[Damage feed keys] Optional global keybinds unavailable: "+ex.Message);
    }
}
