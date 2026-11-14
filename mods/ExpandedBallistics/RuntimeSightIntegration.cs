using System.Collections;
using System.Reflection;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using Il2CppInterop.Runtime;
using Sprocket.Vehicles.Cannons;
using Sprocket.Vehicles.AttachedBehaviours;
using UnityEngine;
using PlayerVehicleController=Sprocket.Gameplay.VehicleControl.VehicleController;

namespace SprocketExpandedBallistics;

/// <summary>
/// Optional interoperability with Dynamic Gun Sight 1.5.0. The third-party plugin is
/// not modified or bundled. Its documented internal bridge is redirected only while a
/// Ballistics carried mix is active, its HUD list is replaced with canonical identity,
/// and its zoom is fixed only for the two additive WW sights that the active cannon has
/// actually been assigned.
/// </summary>
internal static class RuntimeSightIntegration
{
    internal const string Owner="sprocket.expandedballistics.dynamicsight";
    private static ConfigEntry<bool>? enabled;
    private static ManualLogSource? log;
    private static Harmony? harmony;
    private static bool installed;
    private static bool installFailed;
    private static bool absenceLogged;
    private static float nextInstall;
    private static SightSpec? activeSight;
    private static Cannon? activeCannon;
    private static float lastFillAt=-999f;
    private static float nextFallbackRefresh;
    private static string? forcedZoomKey;
    private static readonly HashSet<IntPtr> mixCannons=new();
    private static int warnings;

    private static Type? zoomType, readerType, bridgeType, frameType, ammoEntryType, catalogueType, settingsType;
    private static MethodInfo? fillMethod, forCannonMethod, configureZoomMethod, recalibrateMethod, settingsF, settingsIs;
    private static FieldInfo? levelField, levelsField, respectNativeField, desiredFovField, parsedFromField;
    private static FieldInfo? availableField, searchedField, readerCannonField;
    private static FieldInfo? ammoListField, ammoTotalField, hasAmmoField, loadedLabelField, catalogueField, primaryField;
    private static FieldInfo? typeIdField, labelField, countField, loadedField, requestedField, selectedField, maxVelocityField, catalogueLabelField, catalogueCodeField;
    private static FieldInfo? ladderLoadedField, ladderRequestedField, ladderLabelField;

    internal static void Load(ConfigFile config, ManualLogSource hostLog)
    {
        log=hostLog;
        enabled=config.Bind("Dynamic sight integration","Enabled",true,
            "Optional interoperability with Dynamic Gun Sight 1.5.0: canonical Ballistics ammunition identity and fixed WW1/WW2 sight magnification. Native/vanilla sights and all other Dynamic Sight options are unchanged. Restart to change.");
        if(!enabled.Value){hostLog.LogInfo("[Dynamic sight] Integration disabled by configuration.");return;}
        try
        {
            harmony=new Harmony(Owner);
            harmony.PatchAll(typeof(RuntimeSightIntegration));
            hostLog.LogInfo("[Dynamic sight] Optional bridge owner loaded; waiting for Dynamic Gun Sight 1.5.0.");
        }
        catch(Exception ex)
        {
            harmony?.UnpatchSelf();harmony=null;
            hostLog.LogWarning("[Dynamic sight] Optional bridge unavailable; Ballistics remains active: "+ex.Message);
        }
    }

    internal static void Clear()
    {
        try{harmony?.UnpatchSelf();}catch{}
        harmony=null;installed=false;installFailed=false;absenceLogged=false;nextInstall=0;
        activeSight=null;activeCannon=null;lastFillAt=-999f;nextFallbackRefresh=0;forcedZoomKey=null;mixCannons.Clear();
        zoomType=readerType=bridgeType=frameType=ammoEntryType=catalogueType=settingsType=null;
        fillMethod=forCannonMethod=configureZoomMethod=recalibrateMethod=settingsF=settingsIs=null;
        levelField=levelsField=respectNativeField=desiredFovField=parsedFromField=null;
        availableField=searchedField=readerCannonField=null;
        ammoListField=ammoTotalField=hasAmmoField=loadedLabelField=catalogueField=primaryField=null;
        typeIdField=labelField=countField=loadedField=requestedField=selectedField=maxVelocityField=catalogueLabelField=catalogueCodeField=null;
        ladderLoadedField=ladderRequestedField=ladderLabelField=null;
    }

    [HarmonyPostfix,HarmonyPatch(typeof(PlayerVehicleController),nameof(PlayerVehicleController.UpdateControl))]
    private static void Tick(PlayerVehicleController __instance)
    {
        if(enabled?.Value!=true)return;
        try
        {
            if(installed)_=ActiveSight();
            if(!installed&&!installFailed&&Time.unscaledTime>=nextInstall)TryInstall();
        }
        catch(Exception ex){Warn("Tick",ex);}
    }

    private static void Warn(string area,Exception ex)
    {
        if(warnings++<8)log?.LogWarning($"[Dynamic sight] {area}: {ex.GetType().Name}: {ex.Message}");
    }

    private static Type? FindType(string fullName) =>
        AppDomain.CurrentDomain.GetAssemblies().Select(a=>a.GetType(fullName,false)).FirstOrDefault(t=>t!=null);

    private static void TryInstall()
    {
        nextInstall=Time.unscaledTime+5f;
        zoomType=FindType("SprocketDynamicSight.Game.Zoom");
        readerType=FindType("SprocketDynamicSight.Game.GameReader");
        bridgeType=FindType("SprocketDynamicSight.Game.ShellSelectorBridge");
        frameType=FindType("SprocketDynamicSight.Core.SightFrame");
        ammoEntryType=FindType("SprocketDynamicSight.Core.AmmoEntry");
        catalogueType=FindType("SprocketDynamicSight.Core.CatalogueEntry");
        settingsType=FindType("SprocketDynamicSight.Core.SightSettings");
        var typesPresent=zoomType!=null&&readerType!=null&&bridgeType!=null&&frameType!=null&&ammoEntryType!=null&&catalogueType!=null&&settingsType!=null;
        if(BridgeInstallPolicy.Decide(typesPresent,false,installed)==BridgeInstallAction.Wait)
        {
            if(!absenceLogged){absenceLogged=true;log?.LogInfo("[Dynamic sight] Dynamic Gun Sight 1.5.0 not present; optional integration idle; retrying while the adapter is waiting.");}
            return;
        }
        try
        {
            const BindingFlags any=BindingFlags.Static|BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic;
            fillMethod=readerType!.GetMethod("Fill",any,null,new[]{frameType!,settingsType!,typeof(float)},null);
            forCannonMethod=bridgeType!.GetMethod("ForCannon",any,null,new[]{typeof(Cannon)},null);
            configureZoomMethod=zoomType!.GetMethod("Configure",any,null,new[]{settingsType!},null);
            recalibrateMethod=zoomType!.GetMethod("Recalibrate",any,null,Type.EmptyTypes,null);
            settingsF=settingsType!.GetMethod("F",any,null,new[]{typeof(string)},null);
            settingsIs=settingsType!.GetMethod("Is",any,null,new[]{typeof(string),typeof(string)},null);
            levelField=zoomType!.GetField("Level",any);
            levelsField=zoomType!.GetField("Levels",any);
            respectNativeField=zoomType!.GetField("RespectNative",any);
            desiredFovField=zoomType!.GetField("DesiredFov",any);
            parsedFromField=zoomType!.GetField("parsedFrom",any);
            availableField=bridgeType!.GetField("available",any);
            searchedField=bridgeType!.GetField("searched",any);
            readerCannonField=readerType!.GetField("cannon",any);
            ammoListField=frameType!.GetField("Ammo",any);
            ammoTotalField=frameType!.GetField("AmmoTotal",any);
            hasAmmoField=frameType!.GetField("HasAmmoData",any);
            loadedLabelField=frameType!.GetField("LoadedLabel",any);
            catalogueField=frameType!.GetField("Catalogue",any);
            primaryField=frameType!.GetField("Primary",any);
            typeIdField=ammoEntryType!.GetField("TypeId",any);
            labelField=ammoEntryType!.GetField("Label",any);
            countField=ammoEntryType!.GetField("Count",any);
            loadedField=ammoEntryType!.GetField("Loaded",any);
            requestedField=ammoEntryType!.GetField("Requested",any);
            selectedField=catalogueType!.GetField("Selected",any);
            maxVelocityField=catalogueType!.GetField("MaxVelocity",any);
            catalogueLabelField=catalogueType!.GetField("Label",any);
            catalogueCodeField=catalogueType!.GetField("Code",any);
            var ladderType=FindType("SprocketDynamicSight.Core.Ladder");
            ladderLoadedField=ladderType?.GetField("IsLoaded",any);
            ladderRequestedField=ladderType?.GetField("IsRequested",any);
            ladderLabelField=ladderType?.GetField("Label",any);
            var shapeValid=fillMethod!=null&&forCannonMethod!=null&&configureZoomMethod!=null&&levelField!=null&&levelsField!=null&&
               respectNativeField!=null&&desiredFovField!=null&&parsedFromField!=null&&availableField!=null&&searchedField!=null&&
               ammoListField!=null&&ammoTotalField!=null&&hasAmmoField!=null&&loadedLabelField!=null&&
               catalogueField!=null&&primaryField!=null&&typeIdField!=null&&labelField!=null&&countField!=null&&loadedField!=null&&
               requestedField!=null&&selectedField!=null&&maxVelocityField!=null&&catalogueLabelField!=null&&catalogueCodeField!=null&&settingsF!=null&&settingsIs!=null;
            if(BridgeInstallPolicy.Decide(true,shapeValid,installed)==BridgeInstallAction.Disable)
                throw new MissingMethodException("Dynamic Gun Sight 1.5.0 bridge layout differs from the inspected build.");
            harmony ??= new Harmony(Owner);
            // Prefer the exact GameReader.cannon field. If a future shape omits it,
            // use a controller-resolved fallback without failing the whole bridge.
            try
            {
                if(readerCannonField==null)throw new MissingFieldException("GameReader.cannon");
                harmony.Patch(fillMethod,postfix:new HarmonyMethod(typeof(RuntimeSightIntegration).GetMethod(nameof(FillPostfix),BindingFlags.Static|BindingFlags.NonPublic)));
            }
            catch
            {
                try{harmony.Unpatch(fillMethod,HarmonyPatchType.Postfix,Owner);}catch{}
                harmony.Patch(fillMethod,postfix:new HarmonyMethod(typeof(RuntimeSightIntegration).GetMethod(nameof(FillPostfixFallback),BindingFlags.Static|BindingFlags.NonPublic)));
            }
            harmony.Patch(forCannonMethod,prefix:new HarmonyMethod(typeof(RuntimeSightIntegration).GetMethod(nameof(ForCannonPrefix),BindingFlags.Static|BindingFlags.NonPublic)));
            harmony.Patch(configureZoomMethod,
                prefix:new HarmonyMethod(typeof(RuntimeSightIntegration).GetMethod(nameof(ZoomPrefix),BindingFlags.Static|BindingFlags.NonPublic)),
                postfix:new HarmonyMethod(typeof(RuntimeSightIntegration).GetMethod(nameof(ZoomPostfix),BindingFlags.Static|BindingFlags.NonPublic)));
            installed=true;installFailed=false;
            log?.LogInfo("[Dynamic sight] Dynamic Gun Sight 1.5.0 bridge active: canonical ammo HUD, chambered/next separation, WW1/WW2 fixed zoom. Other sights and user options retained.");
        }
        catch(Exception ex)
        {
            installFailed=true;installed=false;
            try{harmony?.UnpatchSelf();}catch{}
            Warn("patch install",ex);
        }
    }

    // ------------------------------------------------------------ active fitted sight
    private static Cannon? ActiveCannon()
    {
        EnsureFallback();
        return activeCannon;
    }

    private static SightSpec? ActiveSight()
    {
        EnsureFallback();
        return activeSight;
    }

    private static void EnsureFallback()
    {
        if(Time.unscaledTime-lastFillAt<=.5f||Time.unscaledTime<nextFallbackRefresh)return;
        nextFallbackRefresh=Time.unscaledTime+.25f;
        try
        {
            var controller=UnityEngine.Object.FindObjectOfType<PlayerVehicleController>();
            var weapon=controller?.activeGunner?.WeaponGameObject;
            if(weapon==null){activeCannon=null;activeSight=null;return;}
            var cannon=weapon.GetComponent<Cannon>()??weapon.GetComponentInParent<Cannon>();
            if(cannon==null){activeCannon=null;activeSight=null;return;}
            Remember(cannon);
        }
        catch(Exception ex){Warn("active sight",ex);}
    }

    private static void Remember(Cannon cannon)
    {
        activeCannon=cannon;
        activeSight=null;
        if(!cannon.TryGetAssignedSight(out var sight)||sight==null)return;
        var optical=sight.TryCast<GunnerSight>();
        var guid=optical?.VehicleObject?.GUID;
        if(SightCatalog.TryResolve(guid,out var spec))activeSight=spec;
    }

    // --------------------------------------------------------- canonical ammo HUD patch
    private static void FillPostfix(object __instance,object[] __args)
    {
        if(!installed||__args==null||__args.Length==0)return;
        try
        {
            var cannon=readerCannonField?.GetValue(__instance) as Cannon;
            if(cannon==null)cannon=ActiveCannon();
            if(cannon==null)return;
            lastFillAt=Time.unscaledTime;
            Remember(cannon);
            ApplyFrameFor(cannon,__args);
        }
        catch(Exception ex){Warn("ammo HUD",ex);}
    }

    // Fallback for a future Dynamic Sight shape without the private GameReader.cannon field.
    private static void FillPostfixFallback(object[] __args)
    {
        if(!installed||__args==null||__args.Length==0)return;
        try
        {
            var cannon=ActiveCannon();
            if(cannon==null)return;
            lastFillAt=Time.unscaledTime;
            Remember(cannon);
            ApplyFrameFor(cannon,__args);
        }
        catch(Exception ex){Warn("ammo HUD",ex);}
    }

    private static void ApplyFrameFor(Cannon cannon,object[] __args)
    {
        var frame=__args[0];
        if(frame==null)return;
        if(!BallisticsAmmoApi.TryGetState(cannon,out var state)||!state.HasCarriedMix)
        {
            if(mixCannons.Remove(cannon.Pointer))ClearFrame(frame);
            return;
        }
        mixCannons.Add(cannon.Pointer);
        ApplyFrame(frame,__args.Length>1?__args[1]:null,state);
    }

    private static void ApplyFrame(object frame,object? settings,BallisticsAmmoState state)
    {
        if(ammoListField?.GetValue(frame) is not IList list)return;
        list.Clear();
        if(state.Frozen)
        {
            hasAmmoField?.SetValue(frame,false);ammoTotalField?.SetValue(frame,0);loadedLabelField?.SetValue(frame,"");
            ClearPrimary(frame);
            return;
        }
        foreach(var round in state.Rounds)
        {
            var entry=Activator.CreateInstance(ammoEntryType!);
            typeIdField?.SetValue(entry,-1);
            labelField?.SetValue(entry,round.Label);
            countField?.SetValue(entry,round.Rounds);
            loadedField?.SetValue(entry,round.Chambered);
            requestedField?.SetValue(entry,round.Requested);
            list.Add(entry);
        }
        hasAmmoField?.SetValue(frame,true);
        ammoTotalField?.SetValue(frame,state.TotalRounds);
        loadedLabelField?.SetValue(frame,state.ChamberedConfirmed?state.ChamberedLabel??"":"");
        if(primaryField?.GetValue(frame) is {} primary)
        {
            ladderLabelField?.SetValue(primary,state.ChamberedConfirmed?state.ChamberedLabel??"":state.NextLabel??"");
            ladderLoadedField?.SetValue(primary,state.ChamberedConfirmed);
            ladderRequestedField?.SetValue(primary,!state.ChamberedConfirmed&&state.NextProfileId!=null);
        }
        FillCatalogue(frame,settings,state);
    }

    private static void FillCatalogue(object frame,object? settings,BallisticsAmmoState state)
    {
        if(settings==null||settingsIs==null||settingsIs.Invoke(settings,new object[]{"catalogue-slot","Off"}) is true)return;
        if(catalogueField?.GetValue(frame) is not IList catalogue)return;
        catalogue.Clear();
        var stock=Activator.CreateInstance(catalogueType!);
        catalogueLabelField?.SetValue(stock,"Stock ammunition");
        catalogueCodeField?.SetValue(stock,"STOCK");
        selectedField?.SetValue(stock,state.NextProfileId==null||state.NextProfileId==ShellProfiles.Vanilla);
        maxVelocityField?.SetValue(stock,0f);
        catalogue.Add(stock);
        foreach(var profile in RuntimeShellSelection.Profiles)
        {
            var entry=Activator.CreateInstance(catalogueType!);
            catalogueLabelField?.SetValue(entry,profile.Label);
            catalogueCodeField?.SetValue(entry,AmmoSnapshotFormat.CodeFor(profile.Behavior,profile.Label));
            selectedField?.SetValue(entry,state.NextProfileId==profile.Id);
            maxVelocityField?.SetValue(entry,0f);
            catalogue.Add(entry);
        }
    }

    private static void ClearFrame(object frame)
    {
        if(ammoListField?.GetValue(frame) is IList list)list.Clear();
        hasAmmoField?.SetValue(frame,false);
        ammoTotalField?.SetValue(frame,0);
        loadedLabelField?.SetValue(frame,"");
        ClearPrimary(frame);
    }

    private static void ClearPrimary(object frame)
    {
        if(primaryField?.GetValue(frame) is not {} primary)return;
        ladderLoadedField?.SetValue(primary,false);
        ladderRequestedField?.SetValue(primary,false);
    }

    // Dynamic Sight's single-profile path is correct for one selected shell. For a
    // carried mix it would collapse several native types into one profile, so disable
    // only that path while the mix is active; the Fill postfix supplies canonical data.
    private static void ForCannonPrefix(Cannon __0)
    {
        if(!installed||__0==null)return;
        try
        {
            if(BallisticsAmmoApi.HasCarriedMix(__0))
            {
                mixCannons.Add(__0.Pointer);
                availableField?.SetValue(null,false);
            }
            else if(mixCannons.Remove(__0.Pointer))
            {
                // Re-run the untouched detector once; original profile/settings then resume.
                searchedField?.SetValue(null,false);
                availableField?.SetValue(null,false);
            }
        }
        catch(Exception ex){Warn("profile bridge",ex);}
    }

    // ---------------------------------------------------------------- zoom mapping
    private static void ZoomPrefix()
    {
        if(!installed)return;
        try
        {
            var spec=ActiveSight();
            if(forcedZoomKey==null)return;
            if(spec is { } current&&current.Key==forcedZoomKey)return;
            // Leaving or changing the special sight: let Dynamic Sight re-read the
            // user's own zoom list and reset the prior level on its next tick.
            parsedFromField?.SetValue(null,null);
            levelField?.SetValue(null,-1);
            forcedZoomKey=null;
            Recalibrate();
        }
        catch(Exception ex){Warn("zoom restore",ex);}
    }

    private static void ZoomPostfix(object[] __args)
    {
        if(!installed)return;
        try
        {
            var spec=ActiveSight();
            if(spec is not { } current)return;
            var reference=60f;
            if(__args!=null&&__args.Length>0&&__args[0]!=null&&settingsF!=null&&settingsF.Invoke(__args[0],new object[]{"mag-ref-fov"}) is {} raw)
                reference=Convert.ToSingle(raw);
            var desired=SightCatalog.SafeVerticalFieldOfView(reference,current.Magnification);
            levelsField?.SetValue(null,new[]{current.Magnification});
            levelField?.SetValue(null,0);
            respectNativeField?.SetValue(null,false);
            desiredFovField?.SetValue(null,desired);
            if(forcedZoomKey!=current.Key)
            {
                forcedZoomKey=current.Key;
                Recalibrate();
            }
        }
        catch(Exception ex){Warn("zoom apply",ex);}
    }

    private static void Recalibrate()
    {
        try{recalibrateMethod?.Invoke(null,null);}catch{}
    }
}
