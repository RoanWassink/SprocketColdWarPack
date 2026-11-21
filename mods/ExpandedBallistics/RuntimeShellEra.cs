using HarmonyLib;
using BepInEx;
using Sprocket.Vehicles;
using Sprocket.Vehicles.Cannons;
using Sprocket.VehicleDesigner;
using Sprocket.VehicleDesigner.ArmourEditing;
using Sprocket.VehicleDesigner.Access;
using Sprocket.VehicleDesigner.UI;
using System.Runtime.CompilerServices;
namespace SprocketExpandedBallistics;
[HarmonyPatch]
internal static class RuntimeShellEra
{
    private static readonly HashSet<string> warnings=new();
    internal static void WarnCached(IntPtr source)
    {
        if(warnings.Add("cached:"+source))Plugin.ModLog.LogWarning("[Shell era] Cached custom projectile has no native input for this source; shot withheld.");
    }
    private sealed class PreviewBinding
    {
        internal readonly IVehicleEditor Editor;internal readonly IVehicleOverlayApplier Applier;
        internal readonly IntPtr Owner;internal string Signature;internal long NextCheck;internal bool ConfigBound;
        internal PreviewBinding(IVehicleEditor editor,IVehicleOverlayApplier applier,IntPtr owner,string signature,long nextCheck)
        {Editor=editor;Applier=applier;Owner=owner;Signature=signature;NextCheck=nextCheck;}
    }
    private static readonly Dictionary<IntPtr,PreviewBinding> previewOwners=new();
    private static readonly PreviewBindingGate previewChanges=new();
    private sealed record ConfigBinding(IVehicleEditor Editor,IntPtr Overlay,ArmourOverlayConfig Config);
    private static readonly Dictionary<IntPtr,ConfigBinding> configOwners=new();
    private static readonly string[] behaviors={"ap","he","aphe","heat","hesh","apfsds","atgm","atgm_gun"};
    private sealed class CachedContext
    {
        internal readonly ShellEraContext Context;
        internal readonly HashSet<string> Technologies;
        internal readonly HashSet<string> Checked=new(StringComparer.Ordinal);
        internal CachedContext(ShellEraContext context,HashSet<string> technologies){Context=context;Technologies=technologies;}
    }
    // Built once per (vehicle, date, native tech frame). The inspector clears this
    // at the start of a redraw so repeated profile queries share one build.
    private static readonly Dictionary<(IntPtr Owner,int Date,IntPtr Tech,int EraHash,int EraCount),CachedContext> contexts=new();
    private static bool technologyDatesConfigured;
    private static TechnologyFilesStatus? cachedTechFiles;
    private static string TechnologyDirectory()=>Path.Combine(Paths.GameRootPath,"Sprocket_Data","StreamingAssets","Technology");
    internal static void InvalidateContexts(){contexts.Clear();cachedTechFiles=null;}
    internal static void RefreshDiagnostics()=>cachedTechFiles=null;
    internal static void BeginInspectorRedraw()=>contexts.Clear();
    private static void ConfigureTechnologyDates()
    {
        if(technologyDatesConfigured)return;
        technologyDatesConfigured=true;
        try{ShellTechnologyDates.Configure(TechnologyDirectory());}catch{}
    }
    internal static string? DiagnosticWarning(IVehicleGateway? owner)
    {
        try
        {
            ConfigureTechnologyDates();
            var files=cachedTechFiles??=ShellEraDiagnostics.CheckTechnologyDirectory(TechnologyDirectory());
            var eras=VehicleClassifications.eras;
            string?[]? names=null;ShellNativeDate[]? starts=null;
            if(eras!=null)
            {
                names=new string?[eras.Length];starts=new ShellNativeDate[eras.Length];
                for(var i=0;i<eras.Length;i++)
                {
                    if(eras[i]==null)continue;
                    names[i]=eras[i].Name;var start=eras[i].StartDate;starts[i]=new(start.Year,start.Month,start.Day);
                }
            }
            ShellNativeDate? vehicleDate=null;int resolvedIndex=-1;
            if(owner?.DesignInfo is {} info)
            {
                var raw=info.Date;
                var date=new ShellNativeDate(raw.Year,raw.Month,raw.Day);
                vehicleDate=date;
                var count=eras?.Length??0;
                resolvedIndex=ShellEraPolicy.ResolveIndex(date,count,
                    date==ShellNativeDate.Maximum?-1:VehicleClassifications.GetEraIndex(info.Date));
            }
            var timelineWarning=ShellEraDiagnostics.DiagnoseTimeline(vehicleDate,names,starts,resolvedIndex);
            var context=owner!=null?Context(owner):null;
            ShellNativeDate? earliest=null;
            foreach(var behavior in behaviors)
                if(ShellTechnologyDates.Date("expandedBallistics_"+behavior) is {} d&&(earliest==null||d<earliest.Value))earliest=d;
            var warning=ShellEraDiagnostics.Diagnose(files,timelineWarning,context,earliest);
            if(!string.IsNullOrEmpty(warning)&&warnings.Add("diag:"+warning))
                Plugin.ModLog.LogWarning("[Shell era] "+warning);
            return warning;
        }
        catch{return null;}
    }
    internal static ShellEraContext? Context(IVehicleGateway? owner,IEnumerable<string>? requirements=null)
    {
        try
        {
            ConfigureTechnologyDates();
            if(owner?.DesignInfo is not {} info||owner.Tech is not {} tech||VehicleClassifications.eras is not {} eras)return null;
            var raw=info.Date;var date=new ShellNativeDate(raw.Year,raw.Month,raw.Day);
            var key=(owner.Pointer,date.Key,tech.Pointer,RuntimeHelpers.GetHashCode(eras),eras.Length);
            if(!contexts.TryGetValue(key,out var cached))
            {
                var names=new string[eras.Length];var starts=new ShellNativeDate[eras.Length];
                for(var i=0;i<eras.Length;i++)
                {
                    if(eras[i]==null)return null;
                    names[i]=eras[i].Name;var start=eras[i].StartDate;starts[i]=new(start.Year,start.Month,start.Day);
                }
                // Own safety net for the open-ended final-era sentinel 9999.12.31 so EB works
                // standalone on Sandbox/late vehicles even without an external era boundary patch.
                var index=ShellEraPolicy.ResolveIndex(date,eras.Length,
                    date==ShellNativeDate.Maximum?-1:VehicleClassifications.GetEraIndex(info.Date));
                var available=new HashSet<string>(StringComparer.Ordinal);double? heatFactor=null;
                foreach(var behavior in behaviors)
                    if(tech.TryGetTech("expandedBallistics_"+behavior,out var shellTech)&&shellTech!=null&&shellTech.GetBool("enabled",false))
                    {
                        available.Add(behavior);
                        if(behavior=="heat")
                        {
                            var factor=shellTech.GetFloat("penetrationPerCalibre",float.NaN);
                            if(float.IsFinite(factor)&&factor>0&&factor<=20)heatFactor=factor;
                        }
                    }
                var enabledTechnologies=available.Select(b=>"expandedBallistics_"+b).ToHashSet(StringComparer.Ordinal);
                var context=new ShellEraContext(date,names,starts,index,available,heatFactor,enabledTechnologies);
                if(!ShellEraPolicy.Valid(context))return null;
                cached=new CachedContext(context,enabledTechnologies);
                contexts[key]=cached;
            }
            if(requirements!=null)
                foreach(var id in requirements)
                {
                    if(string.IsNullOrWhiteSpace(id)||!cached.Checked.Add(id))continue;
                    if(tech.TryGetTech(id,out var required)&&required!=null&&required.GetBool("enabled",false))
                        cached.Technologies.Add(id);
                }
            return cached.Context;
        }
        catch{return null;}
    }
    private static IEnumerable<string> Requirements(IReadOnlyList<ShellProfile> profiles)
    {
        for(var i=0;i<profiles.Count;i++)
            foreach(var id in ShellBalance.RequiredTechnologyIds(profiles[i]))yield return id;
    }
    internal static string? Era(IVehicleGateway? owner)=>Context(owner) is {} c?c.Names[c.Index]:null;
    internal static bool HasNativeContext(IVehicleGateway? owner)=>Context(owner)!=null;
    internal static bool Allowed(ShellProfile profile,IVehicleGateway? owner)
    {
        var c=Context(owner,ShellBalance.RequiredTechnologyIds(profile));
        return ShellEraPolicy.Allowed(profile,c)&&(!ShellChemicalBudget.IsStock(profile)||c?.HeatFactor!=null);
    }
    internal static double ChemicalBudget(ShellProfile p,double calibre,IVehicleGateway? owner)
    {
        var c=Context(owner,ShellBalance.RequiredTechnologyIds(p));
        return ShellChemicalBudget.Resolve(p,calibre,c?.HeatFactor,ShellEraPolicy.Allowed(p,c));
    }
    internal static string ChemicalDescription(ShellProfile p,IVehicleGateway? owner)=>ShellChemicalBudget.Description(p,Context(owner)?.HeatFactor);
    internal static ShellProfile? Resolve(ShellProfile? stored,IVehicleGateway? owner)
    {
        var era=Era(owner);var effective=stored!=null&&Allowed(stored,owner)?stored:null;
        if(stored!=null&&effective==null&&warnings.Add(stored.Id+":"+era))
            Plugin.ModLog.LogWarning($"[Shell era] Stored '{stored.Id}' unavailable in '{era??"unknown"}'; native ammunition used, stored ID retained.");
        return effective;
    }
    internal static IReadOnlyList<ShellProfile> Available(IReadOnlyList<ShellProfile> all,IVehicleGateway? owner)
    {
        // One union context per redraw: every profile's requirement is checked
        // against the same native tech frame instead of rebuilding it per profile.
        var c=Context(owner,Requirements(all));
        return all.Where(p=>ShellEraPolicy.Allowed(p,c)&&(!ShellChemicalBudget.IsStock(p)||c?.HeatFactor!=null)).ToArray();
    }
    /// <summary>
    /// List visibility: hides retired profiles and launcher ATGMs on an ordinary
    /// cannon, but keeps a profile already selected or carried in the mixed loadout.
    /// </summary>
    internal static IReadOnlyList<ShellProfile> Selectable(IReadOnlyList<ShellProfile> all,IVehicleGateway? owner,IEnumerable<string>? keep,Cannon? cannon)
    {
        var isLauncher=cannon==null?(bool?)null:RuntimeAtgmLauncher.IsLauncher(cannon);
        HashSet<string>? retained=keep is null?null:new HashSet<string>(keep,StringComparer.Ordinal);
        return ShellEraSelection.Selectable(all,Context(owner,Requirements(all)),isLauncher,retained);
    }
    /// <summary>
    /// Backend availability for the material editor. One shared native context per
    /// call; no per-frame rebuild. Does not include the selected-profile keep exception.
    /// </summary>
    internal static ShellEraAvailability Availability(ShellProfile p,Cannon? cannon)
    {
        var isLauncher=cannon==null?(bool?)null:RuntimeAtgmLauncher.IsLauncher(cannon);
        return ShellEraSelection.Availability(p,Context(cannon?.Vehicle,ShellBalance.RequiredTechnologyIds(p)),isLauncher);
    }
    internal static Il2CppSystem.Collections.Generic.List<string> Labels(IReadOnlyList<ShellProfile> profiles)
    {
        var labels=new Il2CppSystem.Collections.Generic.List<string>();labels.Add("Vanilla ammunition");
        foreach(var p in profiles)labels.Add(p.Label);return labels;
    }
    internal static IVehicleGateway? PreviewOwner(ArmourOverlay? overlay,bool requireActive=false)
    {
        try{return overlay!=null&&previewOwners.TryGetValue(overlay.Pointer,out var binding)&&PreviewBindingGate.OwnerAllowed(binding.ConfigBound,overlay.Pointer,binding.Applier.Current?.Pointer??IntPtr.Zero,requireActive,binding.Owner,binding.Editor.Target?.Pointer??IntPtr.Zero)?binding.Editor.Target:null;}
        catch{return null;}
    }
    internal static bool IsActivePreviewOwner(IVehicleGateway owner)
    {
        try{return previewOwners.Any(pair=>PreviewBindingGate.OwnerAllowed(pair.Value.ConfigBound,pair.Key,
            pair.Value.Applier.Current?.Pointer??IntPtr.Zero,true,pair.Value.Owner,pair.Value.Editor.Target?.Pointer??IntPtr.Zero)
            &&pair.Value.Editor.Target?.Pointer==owner.Pointer);}
        catch{return false;}
    }
    internal static string PreviewSignature(ArmourOverlay? overlay)=>overlay!=null&&previewOwners.TryGetValue(overlay.Pointer,out var binding)?binding.Signature:"unbound";
    internal static void InvalidateMenu(ArmourOverlay? overlay=null)
    {
        if(overlay!=null)overlay.RedrawRequired=true;
        foreach(var binding in configOwners.Values)
            if(overlay==null||binding.Overlay==overlay.Pointer)
            {binding.Config.GUIRedrawRequired=true;if(overlay==null)binding.Config.overlay.RedrawRequired=true;}
    }
    [HarmonyPrefix,HarmonyPatch(typeof(VehicleEditor),nameof(VehicleEditor.Detach))]
    private static void Detach(){previewOwners.Clear();previewChanges.Clear();configOwners.Clear();InvalidateContexts();}
    // Factory.Create has the exact native editor argument. OperationExecuter is a proxy,
    // so it cannot be cast back to VehicleEditor. Bind before the first menu draw/activation.
    [HarmonyPostfix,HarmonyPatch(typeof(ArmourOverlayConfigFactory),nameof(ArmourOverlayConfigFactory.Create))]
    private static void ConfigCreated(VehicleOverlay __0,IVehicleEditor __1,OverlayInstanceConfig __result)
    {
        var config=__result?.TryCast<ArmourOverlayConfig>();var overlay=__0?.TryCast<ArmourOverlay>();
        if(config==null||overlay==null||__1==null||config.overlay?.Pointer!=overlay.Pointer)return;
        if(configOwners.Count>=64)configOwners.Clear();
        configOwners[config.Pointer]=new(__1,overlay.Pointer,config);
        BindConfig(config);
    }
    // Native LateUpdate checks GUIRedrawRequired before drawing. Refresh exact current config
    // first, including while menus block pointer operators; unchanged bindings stay throttled.
    [HarmonyPrefix,HarmonyPatch(typeof(OverlayConfigPlugin),nameof(OverlayConfigPlugin.LateUpdate))]
    private static void BeforeConfigUpdate(OverlayConfigPlugin __instance)
    {
        var config=__instance.overlayDrawer?.TryCast<ArmourOverlayConfig>();
        if(config!=null)BindConfig(config);
    }
    internal static void BindConfig(ArmourOverlayConfig config)
    {
        if(!configOwners.TryGetValue(config.Pointer,out var binding)||config.overlay?.Pointer!=binding.Overlay)return;
        var editor=binding.Editor.TryCast<VehicleEditor>();
        var applier=editor?.overlayControlPointerOperator?.overlayApplier;
        if(applier==null)return;
        // The factory proves ownership even when Current has not been assigned yet.
        Bind(binding.Editor,applier,config.overlay,configBound:true);
    }
    [HarmonyPostfix,HarmonyPatch(typeof(VehicleEditor),"ApplyOverlay")]
    private static void Applied(VehicleEditor __instance,VehicleOverlay __0)
    {
        var overlay=__0?.TryCast<ArmourOverlay>();
        if(overlay==null)return;
        Bind(__instance.Cast<IVehicleEditor>(),__instance.overlayControlPointerOperator?.overlayApplier,overlay);
    }
    [HarmonyPostfix,HarmonyPatch(typeof(VehicleEditorOverlayControlPointerOperator),nameof(VehicleEditorOverlayControlPointerOperator.Update))]
    private static void OverlayOwner(VehicleEditorOverlayControlPointerOperator __instance)
    {
        var applier=__instance.overlayApplier;
        Bind(__instance.editor,applier,applier?.Current?.TryCast<ArmourOverlay>());
    }
    private static void Bind(IVehicleEditor? editor,IVehicleOverlayApplier? applier,ArmourOverlay? overlay,bool configBound=false)
    {
        if(overlay==null)return;
        if(editor?.Target==null||applier==null){previewOwners.Remove(overlay.Pointer);if(previewChanges.Changed(overlay.Pointer,null))InvalidateMenu(overlay);return;}
        // A config can exist before activation; do not borrow the current overlay's owner for another one.
        if(!configBound&&!PreviewBindingGate.ExactOverlay(overlay.Pointer,applier.Current?.Pointer??IntPtr.Zero))return;
        var owner=editor.Target;
        var now=Environment.TickCount64;
        previewOwners.TryGetValue(overlay.Pointer,out var previous);
        var identityChanged=previous==null||previous.Owner!=owner.Pointer||previous.Editor.Pointer!=editor.Pointer||previous.Applier.Pointer!=applier.Pointer;
        if(!identityChanged&&configBound)previous!.ConfigBound=true;
        if(!PreviewBindingGate.Inspect(now,previous?.NextCheck??0,identityChanged))return;
        var nextCheck=PreviewBindingGate.NextCheck(now);
        var requirements=RuntimeShellSelection.Profiles.SelectMany(ShellBalance.RequiredTechnologyIds).Distinct();
        var context=Context(owner,requirements);
        var signature=owner.Pointer+":"+(context==null?"unknown":context.Date+":"+context.Index+":"+context.HeatFactor+":"+string.Join(",",context.AvailableTechnologies!.OrderBy(x=>x)));
        signature+=":"+applier.Pointer;
        if(previewChanges.Changed(overlay.Pointer,signature))
        {
            InvalidateMenu(overlay);
        }
        if(identityChanged)previewOwners[overlay.Pointer]=new(editor,applier,owner.Pointer,signature,nextCheck){ConfigBound=configBound};
        else{previous!.Signature=signature;previous.NextCheck=nextCheck;}
    }
}
