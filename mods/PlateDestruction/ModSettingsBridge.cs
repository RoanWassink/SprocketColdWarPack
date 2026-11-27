using System.Reflection;
using BepInEx.Configuration;
namespace PlateDestructionP0;

// 1.0.1: Soft-dependency reflection bridge into SprocketKeybinds.ModSettings so key Plate Destruction
// settings appear in the in-game Mod Settings UI without requiring a hard assembly reference.
internal static class ModSettingsBridge
{
    internal const string OwnerId="nero.sprocket.platedestruction.p0";
    internal const string Author="Nero";
    internal const string DisplayName="Plate Destruction";

    internal static void RegisterAll(Plugin p)
    {
        try
        {
            Type? kbType=null,msType=null,optType=null;
            foreach(var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                if(kbType==null)kbType=asm.GetType("SprocketKeybinds.Keybinds",false);
                if(msType==null)msType=asm.GetType("SprocketKeybinds.ModSettings",false);
                if(optType==null)optType=asm.GetType("SprocketKeybinds.SettingOptions",false);
                if(kbType!=null&&msType!=null&&optType!=null)break;
            }
            if(msType==null||optType==null)return;

            try
            {
                kbType?.GetMethod("RegisterModMetadata",BindingFlags.Public|BindingFlags.Static,null,new[]{typeof(string),typeof(string),typeof(string)},null)
                    ?.Invoke(null,new object[]{OwnerId,Author,DisplayName});
            }
            catch(Exception ex){Plugin.Warn("[SETTINGS] metadata registration failed: "+ex.Message);}

            var reg=msType.GetMethod("Register",BindingFlags.Public|BindingFlags.Static,null,new[]{typeof(string),typeof(ConfigEntryBase),optType},null);
            if(reg==null)return;

            var pLabel=optType.GetProperty("Label");
            var pGroup=optType.GetProperty("Group");
            var pStep=optType.GetProperty("Step");
            var pAdv=optType.GetProperty("Advanced");
            var pVis=optType.GetProperty("VisibleWhen");

            int count=0;
            void Add(ConfigEntryBase entry,string group,string label,float? step=null,bool advanced=false,Func<bool>? visibleWhen=null)
            {
                var opt=Activator.CreateInstance(optType);
                if(opt==null)return;
                pGroup?.SetValue(opt,group);
                pLabel?.SetValue(opt,label);
                if(step.HasValue)pStep?.SetValue(opt,step.Value);
                if(advanced)pAdv?.SetValue(opt,true);
                if(visibleWhen!=null)pVis?.SetValue(opt,visibleWhen);
                if(reg.Invoke(null,new object[]{OwnerId,entry,opt})!=null)count++;
            }

            // Destruction
            Add(p.BreakEnabled,"Destruction","Destruction enabled");
            Add(p.WholeTankDestructible,"Destruction","Whole tank destructible by default");
            Add(p.BlastHoleMode,"Destruction","Blast hole mode");
            Add(p.BlastHoleMaxRadiusM,"Destruction","Max blast hole radius (m)",step:0.01f);
            Add(p.BreachCrewDamageEnabled,"Destruction","Behind-breach crew & module damage");
            Add(p.BreachDamageScale,"Destruction","Behind-breach damage multiplier",step:0.1f,visibleWhen:()=>p.BreachCrewDamageEnabled.Value);
            Add(p.StripEraOnBlast,"Destruction","Strip ERA blocks on HE/HESH blast");
            Add(p.EraStripRadiusPer100mm,"Destruction","ERA strip radius per 100 mm (m)",step:0.05f,advanced:true,visibleWhen:()=>p.StripEraOnBlast.Value);
            Add(p.BreachMmAt100,"Destruction","Fallback 100 mm HE breach (mm RHA)",step:1f,advanced:true);
            Add(p.HeatBlastScale,"Destruction","HEAT visual blast scale",step:0.05f,advanced:true);

            // Visuals & Debris
            Add(p.DentEnabled,"Visuals & Debris","Visual dents & craters");
            Add(p.DentNonDestructiblePlates,"Visuals & Debris","Dent non-destructible plates",visibleWhen:()=>p.DentEnabled.Value);
            Add(p.RimCapsEnabled,"Visuals & Debris","Solid torn armor cross-sections");
            Add(p.DebrisEnabled,"Visuals & Debris","Flying debris pieces");
            Add(p.TearCurlDepthMm,"Visuals & Debris","Torn edge curl depth (mm)",step:5f,advanced:true);
            Add(p.DebrisWarpMm,"Visuals & Debris","Debris bend & warp (mm)",step:5f,advanced:true);

            // Loose Parts
            Add(p.LoosePartsEnabled,"Loose Parts","Breakable loose exterior parts");
            Add(p.LoosePartsIncludeOptics,"Loose Parts","Include optics & hatches",visibleWhen:()=>p.LoosePartsEnabled.Value);
            Add(p.LoosePartsIncludeEra,"Loose Parts","Include standalone ERA",visibleWhen:()=>p.LoosePartsEnabled.Value);
            Add(p.LoosePartStrength,"Loose Parts","Loose part base strength",step:5f,advanced:true,visibleWhen:()=>p.LoosePartsEnabled.Value);

            // Diagnostics
            Add(p.LogDamage,"Diagnostics","Log blast & breach damage");
            Add(p.DetailedLogging,"Diagnostics","Detailed diagnostic logging",advanced:true);

            Plugin.Info($"[SETTINGS] registered {count} setting(s) in Sprocket Mod Settings.");
        }
        catch(Exception ex)
        {
            Plugin.Warn("[SETTINGS] Mod Settings registration skipped: "+ex.Message);
        }
    }
}
