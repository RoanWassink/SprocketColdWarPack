using BepInEx;
using BepInEx.Logging;
using BepInEx.Unity.IL2CPP;
using HarmonyLib;
using Il2CppInterop.Runtime;
using UnityEngine.Events;
namespace SprocketExpandedBallistics;
internal static class ShellFeature
{
    internal static bool Ready {get;private set;}
    internal static void Unload()
    {
        Ready=false;
        ArmourComparisonMenu.Clear();RuntimeArmourComparisonCapture.Clear();RuntimeArmourComparisonRunner.Clear();
        foreach(var owner in Owners)new Harmony(owner).UnpatchSelf();
        RuntimeTandem.Clear();RuntimeWire.Clear();RuntimeDamageFeed.Clear();ShellEditorPanel.Clear();CassetteComposerPanel.Clear();
        RuntimeAmmoLoadout.Clear();AmmoLoadoutKeys.Clear();AmmoLoadoutHud.Clear();RuntimeSightIntegration.Clear();SightUiIntegration.Clear();RuntimeTopAttackLock.Clear();TopAttackKeys.Clear();RuntimeTopAttackHud.Clear();RuntimeAtgmGuidanceSight.Clear();AtgmGuidanceKeys.Clear();AtgmGuidanceSightVisuals.Clear();RuntimeAtgmGimbalPreset.Clear();AtgmLoadedRoundVisual.Clear();
        RuntimeArmourResponses.ReachedBlock=null;NativeMaterialReactions.Reset();
    }
    internal static readonly string[] Owners=new[]{"sprocket.expandedballistics","sprocket.expandedballistics.tandemprobe","sprocket.expandedballistics.damagefeed","sprocket.expandedballistics.damagefeed.health","sprocket.expandedballistics.armourresponses","sprocket.expandedballistics.atgm","sprocket.expandedballistics.atgmlauncher","sprocket.expandedballistics.atgmlauncher.optics","sprocket.expandedballistics.atgmlauncher.initialround","sprocket.expandedballistics.atgmammobox","sprocket.expandedballistics.atgmammobox.visuals","sprocket.expandedballistics.atgmaudio","sprocket.expandedballistics.atgmai","sprocket.expandedballistics.atgmtrail","sprocket.expandedballistics.simulator","sprocket.expandedballistics.simulatorscroll","sprocket.expandedballistics.effects","sprocket.expandedballistics.ammoloadout","sprocket.expandedballistics.dynamicsight","sprocket.expandedballistics.sightui",RuntimeAtgmGuidanceSight.Owner,AtgmGuidanceSightVisuals.Owner,RuntimeAtgmGimbalPreset.Owner};
    internal static void Load(Plugin host)
    {
        var Log=host.Log;var Config=host.Config;Ready=false;
        ReleaseDiagnostics.Configure(Config);
        try
        {
            RuntimeDamageFeed.Configure(Config);
            if(GameBuildGate.AllowHotPath)new Harmony("sprocket.expandedballistics.damagefeed").PatchAll(typeof(RuntimeDamageFeed));
            Il2CppInterop.Runtime.Injection.ClassInjector.RegisterTypeInIl2Cpp<DamageFeedBehaviour>();
            host.AddDamageFeed();
            if(GameBuildGate.AllowHotPath)RuntimeDamageFeed.Ready=true;
            else Log.LogInfo("[Damage feed] Hit-path hooks off on an unaudited game build (see the game build gate warning); the HUD stays empty.");
            try{new Harmony("sprocket.expandedballistics.damagefeed.health").PatchAll(typeof(RuntimeDamageHealth));}
            catch(Exception healthError){new Harmony("sprocket.expandedballistics.damagefeed.health").UnpatchSelf();Log.LogWarning("[Damage feed] Native health reporting unavailable; penetration HUD retained: "+healthError.Message);}
            Log.LogInfo("[Damage feed] Optional Play HUD hooks loaded; default off, global Keybinds toggle and corner-cycle actions.");
        }
        catch(Exception ex){RuntimeDamageFeed.Ready=false;DamageFeedKeys.Clear();new Harmony("sprocket.expandedballistics.damagefeed.health").UnpatchSelf();new Harmony("sprocket.expandedballistics.damagefeed").UnpatchSelf();Log.LogWarning("[Damage feed] Optional feature unavailable: "+ex.Message);}

        var harmony = new Harmony(PluginIdentity.PluginId);
        try
        {
            RuntimeShellSelection.Configure(Config);
            ShellEditorPanel.Configure(Config);
            RuntimeSpall.Configure();
            if(GameBuildGate.AllowHotPath)harmony.PatchAll(typeof(RuntimeShellSelection));
            if(GameBuildGate.AllowHotPath)harmony.PatchAll(typeof(RuntimeSpall));

        }
        catch (Exception ex)
        {
            harmony.UnpatchSelf();
            Log.LogError($"Shell module disabled: {ex}");
            return;
        }
        Ready=true;
        var tandemHarmony=new Harmony(RuntimeTandem.Owner);
        try
        {
            RuntimeTandem.Configure();
            if(GameBuildGate.AllowHotPath){tandemHarmony.PatchAll(typeof(RuntimeTandem));RuntimeTandem.VerifyOrdering();}
        }
        catch(Exception ex)
        {
            tandemHarmony.UnpatchSelf();RuntimeTandem.Clear();
            Log.LogWarning("[Tandem probe] Optional prototype unavailable; tandem simulator profiles withheld: "+ex.Message);
        }
        Log.LogInfo("Shell module loaded.");
        var armourHarmony=new Harmony("sprocket.expandedballistics.armourresponses");
        try
        {
            RuntimeArmourResponses.ReachedBlock=NativeMaterialReactions.ReachedWithLayers; // 0.22.60: Reached, plus the hit per layer for plates in layers
            if(RuntimeArmourResponses.Configure()&&GameBuildGate.AllowHotPath)armourHarmony.PatchAll(typeof(RuntimeArmourResponses));
        }
        catch(Exception ex){RuntimeArmourResponses.ReachedBlock=null;armourHarmony.UnpatchSelf();Log.LogWarning("[Armour response] Optional adapter disabled: "+ex.Message);}
        var atgmHarmony=new Harmony("sprocket.expandedballistics.atgm");
        try
        {
            RuntimeAtgm.Configure(Config);
            if(RuntimeAtgm.Enabled)
            {
                if(GameBuildGate.AllowHotPath){atgmHarmony.PatchAll(typeof(RuntimeAtgm));RuntimeAtgm.Ready=true;TopAttackKeys.Configure();Log.LogInfo("[ATGM] Powered missile flight and guidance loaded. Top-attack lock keybind registered (unbound by default).");}
                else Log.LogInfo("[ATGM] Missile firing left off on an unaudited game build: it depends on the shell-selection shot hooks (see the game build gate warning).");
            }
        }
        catch(Exception ex){atgmHarmony.UnpatchSelf();RuntimeAtgm.Ready=false;Log.LogError("[ATGM] Optional flight guidance unavailable; other shell hooks remain active: "+ex);}
        var launcherHarmony=new Harmony("sprocket.expandedballistics.atgmlauncher");
        try{launcherHarmony.PatchAll(typeof(RuntimeAtgmLauncher));RuntimeAtgmLauncher.Ready=true;Log.LogInfo("[ATGM launcher] Native cannon adapter, ATGM-only gate and optional model/icon hooks loaded.");}
        catch(Exception ex){launcherHarmony.UnpatchSelf();RuntimeAtgmLauncher.Ready=false;Log.LogWarning("[ATGM launcher] Adapter unavailable; dedicated launcher shots withheld: "+ex.Message);}
        var opticsHarmony=new Harmony("sprocket.expandedballistics.atgmlauncher.optics");
        var optics=Config.Bind("ATGM Launcher Tests","OpticalSightDirection",false,"Initialize the dedicated launcher optical sight direction after native activation. Restart to change; retains native mouse control and physical ejection.");
        try{if(optics.Value){opticsHarmony.PatchAll(typeof(RuntimeAtgmLauncherOptics));Log.LogInfo("[ATGM optics] Dedicated launcher optical sight initialization enabled.");}else Log.LogInfo("[ATGM optics] Disabled; native scope initialization retained.");}
        catch(Exception ex){opticsHarmony.UnpatchSelf();Log.LogWarning("[ATGM optics] Optional initialization unavailable: "+ex.Message);}
        var guidanceHarmony=new Harmony(RuntimeAtgmGuidanceSight.Owner);
        try
        {
            RuntimeAtgmGuidanceSight.Configure(Config);
            guidanceHarmony.PatchAll(typeof(RuntimeAtgmGuidanceSight));RuntimeAtgmGuidanceSight.Ready=true;
            AtgmGuidanceKeys.Configure();
            Log.LogInfo("[ATGM guidance sight] Independent optics prototype loaded; explicit dedicated sight required; native ammo/reload retained.");
        }
        catch(Exception ex){guidanceHarmony.UnpatchSelf();RuntimeAtgmGuidanceSight.Clear();RuntimeAtgmGuidanceSight.Ready=false;Log.LogWarning("[ATGM guidance sight] Prototype unavailable: "+ex.Message);}
        var sightVisualHarmony=new Harmony(AtgmGuidanceSightVisuals.Owner);
        try{sightVisualHarmony.PatchAll(typeof(AtgmGuidanceSightVisuals));Log.LogInfo("[ATGM sight] Modern dual-aperture model and own icon loaded.");}
        catch(Exception ex){sightVisualHarmony.UnpatchSelf();Log.LogWarning("[ATGM sight] Optional model unavailable: "+ex.Message);}
        var gimbalHarmony=new Harmony(RuntimeAtgmGimbalPreset.Owner);
        try{gimbalHarmony.PatchAll(typeof(RuntimeAtgmGimbalPreset));Log.LogInfo("[ATGM mount] Combined native yaw/elevation preset loaded; requires active Oscillating Turret.");}
        catch(Exception ex){gimbalHarmony.UnpatchSelf();Log.LogWarning("[ATGM mount] Optional preset unavailable: "+ex.Message);}
        var initialHarmony=new Harmony("sprocket.expandedballistics.atgmlauncher.initialround");
        var initial=Config.Bind("ATGM Launcher Tests","InitialReadyMissile",true,"One finite native initial chamber round per dedicated launcher per new combat instance. No reserve refill; gunner/readiness retained. Restart to change.");
        try{if(initial.Value){initialHarmony.PatchAll(typeof(RuntimeAtgmInitialRound));RuntimeAtgmInitialRound.Ready=true;Log.LogInfo("[ATGM initial] One finite native initial missile enabled.");}}
        catch(Exception ex){initialHarmony.UnpatchSelf();RuntimeAtgmInitialRound.Ready=false;Log.LogWarning("[ATGM initial] Optional starting missile unavailable: "+ex.Message);}
        var boxHarmony=new Harmony("sprocket.expandedballistics.atgmammobox");
        var automaticBox=Config.Bind(AtgmAmmoBoxRules.Section,AtgmAmmoBoxRules.Key,AtgmAmmoBoxRules.DefaultOn,"Automatic loading of a dedicated ATGM launcher from the finite ATGM ammo box within 2 m of its loading point: the launcher then needs no crew loader (a gunner still fires it). On by default; each box keeps its own Automatic feed switch in the designer. Replaces [ATGM Launcher Tests] AutomaticAmmoBox, which is no longer read. Restart to change.");RuntimeAtgmAmmoBox.NoteLegacyKey(Config.ConfigFilePath,Log); // 0.22.89 replaced
        try{if(automaticBox.Value){boxHarmony.PatchAll(typeof(RuntimeAtgmAmmoBox));RuntimeAtgmAmmoBox.Ready=true;Log.LogInfo("[ATGM ammo box] Finite automatic feed enabled.");}else Log.LogInfo("[ATGM ammo box] Native rack and container visuals enabled; automatic feed disabled.");}
        catch(Exception ex){boxHarmony.UnpatchSelf();RuntimeAtgmAmmoBox.Ready=false;Log.LogWarning("[ATGM ammo box] Automatic feed unavailable: "+ex.Message);}
        var boxVisualHarmony=new Harmony("sprocket.expandedballistics.atgmammobox.visuals");
        try{boxVisualHarmony.PatchAll(typeof(AtgmAmmoBoxVisuals));}
        catch(Exception ex){boxVisualHarmony.UnpatchSelf();Log.LogWarning("[ATGM ammo box] Optional container visuals unavailable: "+ex.Message);}
        Log.LogInfo("[ATGM launcher] Dedicated launcher uses native sight association and physical ejection.");
        var audioHarmony=new Harmony("sprocket.expandedballistics.atgmaudio");
        try
        {
            RuntimeLaunchAudio.Configure(Config);
            if(RuntimeLaunchAudio.Enabled){RuntimeLaunchAudio.PreloadClips();audioHarmony.PatchAll(typeof(RuntimeLaunchAudio));Log.LogInfo("[ATGM audio] Supplied TOW launch sound enabled for launcher and gun-launched missiles.");}
        }
        catch(Exception ex){audioHarmony.UnpatchSelf();Log.LogWarning("[ATGM audio] Optional launch audio unavailable; native sounds retained: "+ex.Message);}
        var aiHarmony=new Harmony("sprocket.expandedballistics.atgmai");
        try{aiHarmony.PatchAll(typeof(RuntimeAtgmAi));Log.LogInfo("[ATGM AI] Scoped native aiming hooks loaded.");}
        catch(Exception ex){aiHarmony.UnpatchSelf();Log.LogWarning("[ATGM AI] Optional AI guidance unavailable: "+ex.Message);}
        var trailHarmony=new Harmony("sprocket.expandedballistics.atgmtrail");
        try{RuntimeAtgmTrail.Configure(Config);trailHarmony.PatchAll(typeof(RuntimeAtgmTrail));Log.LogInfo("[ATGM trail] Motor flame and smoke hooks loaded.");}
        catch(Exception ex){trailHarmony.UnpatchSelf();Log.LogWarning("[ATGM trail] Optional visuals unavailable: "+ex.Message);}
        var simulatorHarmony = new Harmony("sprocket.expandedballistics.simulator");
        try
        {
            RuntimeArmourSimulator.Configure();
            simulatorHarmony.PatchAll(typeof(RuntimeShellEra));
            if(GameBuildGate.AllowHotPath)simulatorHarmony.PatchAll(typeof(RuntimeArmourSimulator));
            RuntimeArmourComparisonCapture.Configure();
            RuntimeArmourComparisonRunner.Configure();
            if(GameBuildGate.AllowHotPath)simulatorHarmony.PatchAll(typeof(RuntimeArmourComparisonCapture));
            try
            {
                if(GameBuildGate.AllowHotPath)RuntimeTandem.VerifyOrdering(requireSimulator:true);
            }
            catch(Exception ex)
            {
                tandemHarmony.UnpatchSelf();RuntimeTandem.Clear();
                Log.LogWarning("Tandem preview unavailable; tandem profiles withheld. Other simulator profiles remain available: "+ex.Message);
            }
            Log.LogInfo("[Armour Simulator] Patches loaded.");
        }
        catch (Exception ex)
        {
            simulatorHarmony.UnpatchSelf();
            RuntimeArmourComparisonCapture.Clear();RuntimeArmourComparisonRunner.Clear();ArmourComparisonMenu.Clear();
            Log.LogError($"[Armour Simulator] Disabled; shell and spall/APHE patches remain active: {ex}");
        }
        var effectsHarmony=new Harmony("sprocket.expandedballistics.effects");
        var scrollHarmony=new Harmony("sprocket.expandedballistics.simulatorscroll");
        try{scrollHarmony.PatchAll(typeof(RuntimeSimulatorScroll));Log.LogInfo("[Simulator scroll] Native shell list scrollbar hook loaded.");}
        catch(Exception ex){scrollHarmony.UnpatchSelf();Log.LogError($"[Simulator scroll] Optional scrollbar disabled: {ex}");}
        try
        {
            effectsHarmony.PatchAll(typeof(RuntimeShellEffects));
            Log.LogInfo("[APHE Effect] Optional native explosion visual hook loaded.");
        }
        catch(Exception ex)
        {
            effectsHarmony.UnpatchSelf();
            Log.LogError($"[APHE Effect] Visual hook disabled; shell behaviour remains active: {ex}");
        }
        var loadoutHarmony=new Harmony(RuntimeAmmoLoadout.Owner);
        try
        {
            RuntimeAmmoLoadout.Configure(Config);
            loadoutHarmony.PatchAll(typeof(RuntimeAmmoLoadout));
            RuntimeAmmoLoadout.Ready=true;
            AmmoLoadoutKeys.Configure();
            Log.LogInfo("[Ammo loadout] Carried ammunition mix loaded; cannon panel section, battle switching and HUD.");
        }
        catch(Exception ex)
        {
            loadoutHarmony.UnpatchSelf();
            RuntimeAmmoLoadout.Ready=false;RuntimeAmmoLoadout.Clear();
            Log.LogWarning("[Ammo loadout] Optional carried ammunition mix unavailable: "+ex.Message);
        }
        try
        {
            RuntimeSightIntegration.Load(Config,Log);
        }
        catch(Exception ex)
        {
            new Harmony(RuntimeSightIntegration.Owner).UnpatchSelf();
            RuntimeSightIntegration.Clear();
            Log.LogWarning("[Dynamic sight] Optional integration unavailable; Ballistics remains active: "+ex.Message);
        }
        try
        {
            new Harmony(SightUiIntegration.Owner).PatchAll(typeof(SightUiIntegration));
            Log.LogInfo("[Sight picker] Native sight icon composer and Expedition-only vanilla picker filter loaded.");
        }
        catch(Exception ex)
        {
            new Harmony(SightUiIntegration.Owner).UnpatchSelf();
            SightUiIntegration.Clear();
            Log.LogWarning("[Sight picker] Optional icon/filter integration unavailable; native picker retained: "+ex.Message);
        }
    }
}

