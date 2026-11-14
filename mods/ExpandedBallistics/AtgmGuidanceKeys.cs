using System.Reflection;
using UnityEngine;
using UnityEngine.InputSystem;
namespace SprocketExpandedBallistics;
internal static class AtgmGuidanceKeys
{
    private static readonly string[] Actions={"atgm-left","atgm-right","atgm-up","atgm-down"};
    private static readonly string[] Labels={"Guide ATGM left","Guide ATGM right","Guide ATGM up","Guide ATGM down"};
    private static readonly string[] Defaults={"<Keyboard>/a","<Keyboard>/d","<Keyboard>/w","<Keyboard>/s"};
    private static object?[] keys=Array.Empty<object?>();
    private static PropertyInfo? held;
    private static Type? api;
    private static float retry;
    private static bool warned;
    internal static void Configure()
    {
        if(keys.Length==4||Time.unscaledTime<retry)return;retry=Time.unscaledTime+2;
        try
        {
            api=AppDomain.CurrentDomain.GetAssemblies().Select(a=>a.GetType("SprocketKeybinds.Keybinds",false)).FirstOrDefault(t=>t!=null);
            if(api==null)return;BallisticsKeybindMetadata.Register(api);
            var register=api.GetMethods(BindingFlags.Public|BindingFlags.Static).Single(m=>m.Name=="RegisterButton"&&m.GetParameters().Length==6);
            var result=new object?[4];
            for(int i=0;i<4;i++)result[i]=register.Invoke(null,new object?[]{"sprocket.expandedballistics",BallisticsKeybindMetadata.Name,Actions[i],Labels[i],Defaults[i],null});
            if(result.Any(k=>k==null))return;
            held=result[0]!.GetType().GetProperty("IsHeld");if(held==null)return;keys=result;
            Plugin.ModLog.LogInfo("[ATGM keys] Direction controls registered in vanilla Settings/keybinds; existing bindings retained.");
        }
        catch(Exception ex){if(!warned){warned=true;Plugin.ModLog.LogWarning("[ATGM keys] "+ex.Message);}}
    }
    internal static (int Yaw,int Pitch) Poll()
    {
        Configure();
        if(keys.Length==4&&held!=null)
        {
            try{bool Down(int n)=>held!.GetValue(keys[n]) is true;return ((Down(1)?1:0)-(Down(0)?1:0),(Down(2)?1:0)-(Down(3)?1:0));}
            catch{return (0,0);}
        }
        // Preserve the existing controls only when the optional shared API is absent.
        if(api==null){var k=Keyboard.current;return ((k?.dKey.isPressed==true?1:0)-(k?.aKey.isPressed==true?1:0),(k?.wKey.isPressed==true?1:0)-(k?.sKey.isPressed==true?1:0));}
        return (0,0);
    }
    internal static void Clear()
    {
        try{var unregister=api?.GetMethod("Unregister",new[]{typeof(string),typeof(string)});foreach(var action in Actions)unregister?.Invoke(null,new object[]{"sprocket.expandedballistics",action});}catch{}
        keys=Array.Empty<object?>();held=null;api=null;retry=0;warned=false;
    }
}
