using HarmonyLib;
using Sprocket.DamageModelling;
using Sprocket.UI;
using Sprocket.VehicleDesigner.ArmourEditing;
using Sprocket.VehicleDesigner.UI;
using Sprocket.Vehicles;
namespace SprocketExpandedBallistics;
[HarmonyPatch]
internal static class RuntimeArmourSimulator
{
    private sealed record Choice(ArmourOverlay Overlay,string Id);
    private static readonly Dictionary<IntPtr,Choice> Choices=new();
    private static readonly Il2CppSystem.Collections.Generic.List<string> Labels=new();
    [ThreadStatic] private static ArmourOverlay? activeOverlay;
    [ThreadStatic] internal static bool DrawingShellChoice;
    private static IReadOnlyList<ShellProfile> SimulatorProfiles(ArmourOverlay? overlay)
    {
        var keep=overlay!=null&&Choices.TryGetValue(overlay.Pointer,out var choice)?new[]{choice.Id}:null;
        return RuntimeShellEra.Selectable(RuntimeShellSelection.Profiles,RuntimeShellEra.PreviewOwner(overlay),keep,null)
            .Where(p=>p.Behavior!="he"&&(p.TandemPrecursorPenetrationMm<=0||RuntimeTandem.Ready)).ToArray();
    }
    internal static void Refresh()
    {
        RuntimeShellEra.InvalidateMenu();
        foreach(var choice in Choices.Values)
            try { if(choice.Overlay!=null)choice.Overlay.RedrawRequired=true; } catch(Exception ex) { Plugin.ModLog.LogWarning("[Shell editor] Simulator refresh: "+ex.Message); }
    }
    internal static void Configure()
    {
        Labels.Clear();Labels.Add("Vanilla ammunition");
        // Labels are rebuilt from the actual preview design when drawing.
    }
    private static ShellProfile? Selected(ArmourOverlay? overlay) => RuntimeShellSelection.Enabled && overlay!=null && Choices.TryGetValue(overlay.Pointer,out var choice)
        ? RuntimeShellEra.Resolve(RuntimeShellSelection.Profiles.FirstOrDefault(p=>p.Id==choice.Id&&(p.TandemPrecursorPenetrationMm<=0||RuntimeTandem.Ready)),RuntimeShellEra.PreviewOwner(overlay)) : null;
    private static float EffectivePenetration(ShellProfile profile,ArmourOverlay overlay) => profile.Behavior is "heat" or "hesh" or "atgm" or "atgm_gun"
        ? Math.Min(overlay.Penetration,(float)RuntimeShellEra.ChemicalBudget(profile,overlay.Calibre,RuntimeShellEra.PreviewOwner(overlay))) : overlay.Penetration;
    [HarmonyPrefix,HarmonyPatch(typeof(ArmourOverlayConfig),nameof(ArmourOverlayConfig.Draw))]
    private static void Limits(ArmourOverlayConfig __instance)
    {
        RuntimeShellEra.BindConfig(__instance);
        __instance.Penetration.SetLimits(__instance.Penetration.Min,2000f);
        __instance.Penetration.softMax=2000f;
    }
    [HarmonyPrefix,HarmonyPatch(typeof(ArmourOverlayConfig),nameof(ArmourOverlayConfig.Draw))]
    private static bool DrawSimulationControls(IGUILayout __0)=>!RuntimeShellSelection.Enabled||!RuntimeDamageFeed.Ready||!ArmourComparisonMenu.Focused||__0.TryCast<IGUIElementDrawer>()==null;
    [HarmonyPostfix,HarmonyPatch(typeof(ArmourOverlayConfig),nameof(ArmourOverlayConfig.Draw))]
    private static void Draw(ArmourOverlayConfig __instance,IGUILayout __0)
    {
        if(!RuntimeShellSelection.Enabled) return;
        try
        {
            RuntimeShellEra.BeginInspectorRedraw();
            var layout=__0;
            var ui=layout.TryCast<IGUIElementDrawer>();if(ui==null)return;
            if(ArmourComparisonMenu.Focused){ArmourComparisonMenu.Draw(layout,ui,()=>__instance.GUIRedrawRequired=true);return;}
            layout.EndAllDropdowns();
            layout.BeginDropdown("Simulator shell profile",true,Ui.BoolCallback(_=>{}));
            try
            {
                var selected=Selected(__instance.overlay);
                var available=SimulatorProfiles(__instance.overlay);
                var labels=RuntimeShellEra.Labels(available);
                if(selected==null&&Choices.TryGetValue(__instance.overlay.Pointer,out var storedChoice)&&storedChoice.Id!=ShellProfiles.Vanilla)
                    ui.InfoField("Stored simulator profile unavailable. Select another shell; choice retained.",2);
                ui.InfoField($"Scenario: {RuntimeShellEra.Era(RuntimeShellEra.PreviewOwner(__instance.overlay))??"unknown; native ammunition only"}",1);
                var previousDrawing=DrawingShellChoice;
                DrawingShellChoice=true;
                try {ui.Dropdown("Shell type",labels.Cast<Il2CppSystem.Collections.Generic.IReadOnlyList<string>>(),
                    selected==null?0:available.ToList().FindIndex(p=>p.Id==selected.Id)+1,
                    Ui.IntCallback(index=>{
                        if(index<0||index>available.Count)return;
                        if(index>0&&!RuntimeShellEra.Allowed(available[index-1],RuntimeShellEra.PreviewOwner(__instance.overlay)))return;
                        Choices[__instance.overlay.Pointer]=new(__instance.overlay,index==0?ShellProfiles.Vanilla:available[index-1].Id);
                        // Redraw overlay and invalidate the pointer simulation when profile changes.
                        RuntimeShellEra.InvalidateMenu(__instance.overlay);
                    }),"Calibre is the full gun calibre. Penetration is the manually chosen RHA target, not a cannon performance prediction.");}
                finally{DrawingShellChoice=previousDrawing;}
                if(selected!=null)
                    ui.InfoField("Penetration is set by the simulator slider.",1);
                if(selected?.Behavior is "heat" or "hesh" or "atgm" or "atgm_gun")
                    {
                    ui.InfoField(RuntimeShellEra.ChemicalDescription(selected,RuntimeShellEra.PreviewOwner(__instance.overlay)),2);
                    if(selected.Behavior=="hesh")ui.InfoField("HESH is a broad spall proxy after native perforation, not a true back-face shock/scab simulation.",2);
                }
            }
            finally{layout.EndAllDropdowns();}
            ArmourComparisonMenu.Draw(layout,ui,()=>__instance.GUIRedrawRequired=true);
        }
        catch(Exception ex){Plugin.ModLog.LogError("[Armour Simulator] UI: "+ex);}
    }
    private sealed record UpdateState(ArmourOverlay? PreviousOverlay,ShellImpactContext? PreviousImpact);
    internal static Sprocket.Vehicles.IVehicleGateway? CurrentOwner=>RuntimeArmourComparisonRunner.CurrentOwner??RuntimeShellEra.PreviewOwner(activeOverlay,requireActive:true);
    private static readonly Dictionary<IntPtr,string> LastChoices=new();
    [HarmonyPrefix,HarmonyPatch(typeof(ArmourOverlayPointerOperator),nameof(ArmourOverlayPointerOperator.Update))]
    private static void Begin(ArmourOverlayPointerOperator __instance,out UpdateState __state)
    {
        __state=new(activeOverlay,RuntimeSpall.Impact);
        activeOverlay=__instance.overlay.TryCast<ArmourOverlay>();
        var owner=RuntimeShellEra.PreviewOwner(activeOverlay,requireActive:true);
        var selected=owner==null?null:Selected(activeOverlay);
        var id=(selected?.Id??ShellProfiles.Vanilla)+":"+RuntimeShellEra.PreviewSignature(activeOverlay);
        if(!LastChoices.TryGetValue(__instance.Pointer,out var last)||last!=id){__instance.Clear();LastChoices[__instance.Pointer]=id;}
        var era=RuntimeShellEra.Era(owner);
        RuntimeSpall.Impact=selected==null?null:new(){ProfileId=selected.Id,Profile=selected,ThreatClass=selected.ThreatClass,GunDiameter=activeOverlay!.Calibre*.001f,FiringEra=era,ChemicalBudget=RuntimeShellEra.ChemicalBudget(selected,activeOverlay.Calibre,owner)};
    }
    [HarmonyFinalizer,HarmonyPatch(typeof(ArmourOverlayPointerOperator),nameof(ArmourOverlayPointerOperator.Update))]
    private static void End(UpdateState __state){activeOverlay=__state.PreviousOverlay;RuntimeSpall.Impact=__state.PreviousImpact;}
    [HarmonyPostfix,HarmonyPatch(typeof(ArmourOverlay),"get_SampleSettings")]
    private static void Sample(ArmourOverlay __instance,ref ArmourOverlaySampleSettings __result)
    {
        var profile=Selected(__instance);if(profile==null)return;
        // The native colour sampler only supports integer calibre/AP mass. Keep its chosen RHA penetration equal,
        // then replace the exact geometry/mass at the detailed Penetrate call below.
        var mm=(ushort)Math.Clamp(Math.Round(__instance.Calibre*profile.Settings.DiameterRatio),1,ushort.MaxValue);
        __result=new ArmourOverlaySampleSettings(mm,PenetrationUtils.ComputeRequiredPenetrationSpeed(mm,(float)(1.59e-5*mm*mm*mm),EffectivePenetration(profile,__instance),__instance.PenetratorConstant));
    }
    [HarmonyPrefix,HarmonyPatch(typeof(CompoundStructure),nameof(CompoundStructure.Penetrate))]
    private static void Projectile(ref PenetratorInfo penetrator)
    {
        if(RuntimeArmourComparisonRunner.IsProbing)return;
        if(RuntimeTandem.PreviewInsidePrecursor)return;
        if(!RuntimeTandem.Ready&&activeOverlay!=null&&Choices.TryGetValue(activeOverlay.Pointer,out var stored)&&RuntimeShellSelection.Profiles.Any(p=>p.Id==stored.Id&&p.TandemPrecursorPenetrationMm>0))
            throw new InvalidOperationException("Tandem simulator unavailable; preview withheld. Select another shell profile.");
        var overlay=activeOverlay;if(RuntimeShellEra.PreviewOwner(overlay,requireActive:true)==null)return;
        var profile=Selected(overlay);if(profile==null)return;
        try
        {
            var dart=ShellBalance.Calculate(overlay!.Calibre,800,overlay.PenetratorConstant,profile);
            if(ShellBalance.ImpactBehavior(profile)=="heat") dart=dart with {Diameter=overlay.Calibre*.001f*.05f,Mass=dart.Mass*.05f};
            var speed=PenetrationUtils.ComputeRequiredPenetrationSpeed(dart.Diameter*1000,dart.Mass,EffectivePenetration(profile,overlay),dart.PenetratorConstant);
            if(RuntimeSpall.Impact is {} impact){impact.Geometry=new(dart.Length,dart.Diameter,dart.Mass,overlay.Calibre*.001);impact.ArmourSnapshot=RuntimeArmourResponses.Snapshot;impact.Experimental=impact.ArmourSnapshot.Experimental;impact.ChemicalBudget=EffectivePenetration(profile,overlay);impact.ChemicalInitialized=true;}
            var direction=penetrator.Velocity.normalized;
            if(!float.IsFinite(speed)||speed<=0||direction.sqrMagnitude<.5f)throw new InvalidOperationException("Invalid synthetic simulator shot.");
            // Preserve position and normalization; the manual slider fixes penetration, so speed is inverted from it.
            penetrator=new PenetratorInfo(dart.Mass,dart.Diameter,penetrator.Density,penetrator.MaxNormalizationAngle,dart.PenetratorConstant,penetrator.Position,direction*speed);
            if(ReleaseDiagnostics.Enabled)Plugin.ModLog.LogInfo($"[Armour Simulator] {profile.Id} scenarioEra={RuntimeShellEra.Era(RuntimeShellEra.PreviewOwner(overlay))} gun={overlay.Calibre}mm dart={dart.Diameter*1000:0.0}mm requested={overlay.Penetration:0}mm effective={EffectivePenetration(profile,overlay):0.0}mm equivalentSpeed={speed:0}m/s");
        }
        catch(Exception ex){Plugin.ModLog.LogError("[Armour Simulator] Shot: "+ex);if(profile.TandemPrecursorPenetrationMm>0)throw;}
    }
}

