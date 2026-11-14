using System.Reflection;
using UnityEngine;

namespace SprocketExpandedBallistics;

internal enum KeyCommand { None,Next,Previous,Slot }
internal readonly record struct KeyPress(KeyCommand Command,int Slot);

/// <summary>
/// Optional Sprocket Keybinds buttons for the carried ammunition mix. Same soft
/// dependency and reflection route as the combat report keybind; unbound by default.
/// Configuration is retried lazily because the keybinds plugin may load after this one.
/// </summary>
internal static class AmmoLoadoutKeys
{
    private const string PluginId="sprocket.expandedballistics";
    private const string Group=BallisticsKeybindMetadata.Name;
    private static readonly string[] Actions={"ammo-next","ammo-prev","ammo-slot-1","ammo-slot-2","ammo-slot-3","ammo-slot-4","ammo-slot-5","ammo-slot-6","ammo-slot-7","ammo-slot-8"};
    private static readonly string[] Labels={"Next shell type","Previous shell type","Shell type slot 1","Shell type slot 2","Shell type slot 3","Shell type slot 4","Shell type slot 5","Shell type slot 6","Shell type slot 7","Shell type slot 8"};

    private static object?[] keys=Array.Empty<object?>();
    private static PropertyInfo? pressed;
    private static Type? api;
    private static int warnings;
    private static float retryAt;

    internal static void Configure()
    {
        if(keys.Length>0||Time.unscaledTime<retryAt)return;
        retryAt=Time.unscaledTime+2f;
        try
        {
            var type=AppDomain.CurrentDomain.GetAssemblies().Select(a=>a.GetType("SprocketKeybinds.Keybinds",false)).FirstOrDefault(t=>t!=null);
            if(type==null)return;
            api=type;
            BallisticsKeybindMetadata.Register(type);
            var register=type.GetMethods(BindingFlags.Public|BindingFlags.Static).Single(m=>m.Name=="RegisterButton"&&m.GetParameters().Length==6);
            var list=new List<object?>();
            for(var i=0;i<Actions.Length;i++)list.Add(register.Invoke(null,new object?[]{PluginId,Group,Actions[i],Labels[i],"",null}));
            keys=list.ToArray();
            pressed=keys[0]?.GetType().GetProperty("WasPressedThisFrame");
            Plugin.ModLog.LogInfo("[Ammo loadout keys] Optional keybinds registered (unbound by default).");
        }
        catch(Exception ex){Warn(ex);}
    }

    private static void Warn(Exception ex)
    {
        if(warnings++<3)Plugin.ModLog.LogWarning("[Ammo loadout keys] Optional keybinds unavailable: "+ex.Message);
    }

    private static bool Down(int index)
    {
        if(pressed==null||index>=keys.Length||keys[index]==null)return false;
        return pressed.GetValue(keys[index]) is true;
    }

    internal static KeyPress Poll()
    {
        if(keys.Length==0)return default;
        try
        {
            if(Down(0))return new KeyPress(KeyCommand.Next,0);
            if(Down(1))return new KeyPress(KeyCommand.Previous,0);
            for(var i=2;i<keys.Length;i++)if(Down(i))return new KeyPress(KeyCommand.Slot,i-2);
        }
        catch(Exception ex){Warn(ex);}
        return default;
    }

    internal static void Clear()
    {
        try
        {
            var unregister=api?.GetMethod("Unregister",new[]{typeof(string),typeof(string)});
            if(unregister!=null)foreach(var action in Actions)unregister.Invoke(null,new object[]{PluginId,action});
        }
        catch(Exception ex){Warn(ex);}
        keys=Array.Empty<object?>();
        pressed=null;
        api=null;
    }
}
