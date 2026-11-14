using System.Diagnostics;
using System.Reflection;
using System.Security.Cryptography;
using BepInEx;
using Il2CppInterop.Runtime;
using Sprocket.UI;
using Sprocket.VehicleDesigner.Modules;
using Sprocket.Vehicles;
using UnityEngine.Events;
namespace SprocketVisualEffects;

// 0.4.4: "Lamp" section in the native part inspector (right-click on the part), the only
// new Harmony hook (user decision 2026-11-11). Pattern: Thermal Sight ProfileInspector.
// Postfix only, no result or ref writes, filtered on ComponentID vfxLamp; any exception
// removes only this section. Values are stored per lamp through the Mast (SetPartData).
internal static class LampInspector
{
    internal const long OnGuiRva=0x1F92E50;
    private static bool open=true;
    private static string status="";
    private static int warnings;
    internal static MethodInfo Method()=>typeof(VehicleObjectEditor).GetMethod(nameof(VehicleObjectEditor.OnGUI),new[]{typeof(IGUILayout)})??throw new MissingMethodException("VehicleObjectEditor.OnGUI(IGUILayout) changed");
    internal static unsafe bool Compatible(out string reason)
    {
        using(var stream=File.OpenRead(Path.Combine(Paths.GameRootPath,"GameAssembly.dll")))
        using(var sha=SHA256.Create())
            if(Convert.ToHexString(sha.ComputeHash(stream))!=HookGuard.GameHash){reason="GameAssembly hash differs from audited build";return false;}
        var m=Method();
        if(m.ReturnType!=typeof(void)||m.GetParameters().Length!=1){reason="OnGUI signature differs";return false;}
        var fields=typeof(VehicleObjectEditor).GetFields(BindingFlags.NonPublic|BindingFlags.Static).Where(f=>f.Name.StartsWith("NativeMethodInfoPtr_OnGUI_")).ToArray();
        if(fields.Length!=1){reason="OnGUI native pointer field missing/ambiguous";return false;}
        var info=(IntPtr)fields[0].GetValue(null)!;
        var native=Il2CppInterop.Runtime.Runtime.UnityVersionHandler.Wrap((Il2CppInterop.Runtime.Runtime.Il2CppMethodInfo*)info).MethodPointer;
        using var process=Process.GetCurrentProcess();
        var module=process.Modules.Cast<ProcessModule>().Single(x=>string.Equals(x.ModuleName,"GameAssembly.dll",StringComparison.OrdinalIgnoreCase));
        if(native.ToInt64()-module.BaseAddress.ToInt64()!=OnGuiRva){reason="OnGUI native address differs from the audited unique RVA";return false;}
        reason="hash, signature and unique native RVA match";return true;
    }
    private static void Warn(string what,Exception ex){if(warnings++<3)Plugin.Instance.Log.LogWarning("Lamp inspector "+what+": "+ex.GetType().Name+": "+ex.Message);}
    private static UnityAction<int> IntAction(Action<int> a)=>DelegateSupport.ConvertDelegate<UnityAction<int>>(a)!;
    private static UnityAction<string> StringAction(Action<string> a)=>DelegateSupport.ConvertDelegate<UnityAction<string>>(a)!;
    private static Il2CppSystem.Action<float> FloatAction(Action<float> a)=>DelegateSupport.ConvertDelegate<Il2CppSystem.Action<float>>(a)!;

    internal static void AfterOnGUI(VehicleObjectEditor __instance,IGUILayout __0)
    {
        try
        {
            var runtime=Plugin.Instance.Lamps;
            var target=__instance.target;
            if(runtime==null||!runtime.IsLampObject(target))return;
            var ui=__0.TryCast<IGUIElementDrawer>();
            if(ui==null)return;
            __0.EndAllDropdowns();
            __0.BeginDropdown("Lamp",open,DelegateSupport.ConvertDelegate<Il2CppSystem.Action<bool>>((Action<bool>)(v=>open=v))!);
            try{Draw(__instance,ui,runtime,target!);}
            finally{__0.EndAllDropdowns();}
        }
        catch(Exception ex){Warn("section skipped",ex);}
    }
    private static void Draw(VehicleObjectEditor editor,IGUIElementDrawer ui,LampRuntime runtime,VehicleObject target)
    {
        var lamp=runtime.Find(target);
        if(lamp==null){ui.InfoField("Lamp settings appear here once the placed lamp is adopted (within 0.5 s).",2);return;}
        if(!runtime.CanSave)ui.InfoField("Per-lamp settings need Telescopic Mast IO-API v1.1; the config shape defaults are active.",2);
        var s=lamp.Settings;
        var labels=new Il2CppSystem.Collections.Generic.List<string>();
        foreach(var p in LampSettings.Presets)labels.Add(p);
        labels.Add("Custom RGB");
        int preset=Array.IndexOf(LampSettings.Colors,s.Color);
        ui.Dropdown("Colour",labels.Cast<Il2CppSystem.Collections.Generic.IReadOnlyList<string>>(),preset<0?LampSettings.Presets.Length:preset,IntAction(index=>
        {
            try
            {
                if(index<0||index>=LampSettings.Colors.Length)return;
                Change(runtime,target,x=>x with {Color=LampSettings.Colors[index]});
                editor.RequestRedraw();
            }
            catch(Exception ex){Warn("colour preset",ex);}
        }),"Red, amber, white, green, blue or IR (no visible emission). The mirrored twin follows.");
        ui.InputField("RGB #RRGGBB",s.Color,StringAction(text=>
        {
            try
            {
                text=(text??"").Trim();if(!text.StartsWith('#'))text="#"+text;
                if(LampSettings.ValidColor(text))Change(runtime,target,x=>x with {Color=text.ToUpperInvariant()});
                else status="Use six hexadecimal digits, for example #FF1808.";
            }
            catch(Exception ex){Warn("RGB",ex);}
        }));
        // Slider callbacks only store values: a redraw during a drag broke native sliders before
        // (skill: suspension-parts-icons, "No edit in progress").
        ui.Slider("Brightness (cd)",Math.Min(s.Intensity,2000f),0f,2000f,FloatAction(v=>{try{Change(runtime,target,x=>x with {Intensity=MathF.Round(v)});}catch(Exception ex){Warn("brightness",ex);}}));
        ui.Slider("Range (m)",Math.Min(s.Range,100f),.25f,100f,FloatAction(v=>{try{Change(runtime,target,x=>x with {Range=MathF.Round(v*4)/4});}catch(Exception ex){Warn("range",ex);}}));
        ui.InfoField($"{LampSettings.Forms[lamp.Form]} lamp | {s.Color} | {s.Intensity:0} cd | {s.Range:0.##} m | lens {LampLook.Brightness(s.Intensity,Plugin.Instance.LensBrightness.Value):0.00}",2);
        ui.InfoField("Designer: lens and glow preview. Play: switch with Lamp on / Lamp off in the Mast I/O flow.",2);
        if(status.Length>0)ui.InfoField(status,2);
    }
    private static void Change(LampRuntime runtime,VehicleObject target,Func<LampSettings,LampSettings> change)
    {
        var lamp=runtime.Find(target);
        if(lamp==null){status="Lamp not adopted yet; nothing stored.";return;}
        var next=change(lamp.Settings);
        if(!runtime.Save(lamp,next)){status="Not stored: Mast IO-API v1.1 missing or refused; previous values kept.";return;}
        bool twin=false;
        try
        {
            var mirror=target.VehicleTransform?.Mirror?.VehicleObject;
            if(mirror!=null&&mirror.Pointer!=target.Pointer&&runtime.Find(mirror) is {} other&&other!=lamp)
                twin=runtime.Save(other,other.Settings with {Color=lamp.Settings.Color,Intensity=lamp.Settings.Intensity,Range=lamp.Settings.Range});
        }
        catch(Exception ex){Warn("mirror twin",ex);}
        status=twin?"Stored on this lamp and its mirrored twin; save the vehicle to keep it.":"Stored on this lamp; save the vehicle to keep it.";
    }
}
