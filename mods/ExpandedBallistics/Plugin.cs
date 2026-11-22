using BepInEx;
using BepInEx.Logging;
using BepInEx.Unity.IL2CPP;
using HarmonyLib;
using Il2CppInterop.Runtime;
using UnityEngine.Events;
using SprocketUiKit;
namespace SprocketExpandedBallistics;
[BepInPlugin("sprocket.expandedballistics", "Sprocket Expanded Ballistics", "0.22.109")]
[BepInDependency("sprocket.jsoneditor", ">=0.1.5 <0.2.0")]
[BepInDependency(UiKit.PluginGuid, ">=0.1.7 <0.2.0")]
[BepInDependency("sprocket.keybinds", BepInDependency.DependencyFlags.SoftDependency)]
public sealed class Plugin : BasePlugin
{
    internal static ManualLogSource ModLog=null!;
    internal static bool ShellReady=>ShellFeature.Ready;
    internal void AddDamageFeed()=>AddComponent<DamageFeedBehaviour>();
    public static void RefreshArmourResponses()
    {
        var enabled=RuntimeArmourResponses.Configure();
        const string owner="sprocket.expandedballistics.armourresponses";
        if(enabled&&GameBuildGate.AllowHotPath&&!Harmony.HasAnyPatches(owner))new Harmony(owner).PatchAll(typeof(RuntimeArmourResponses));
        if(SprocketExpandedBallistics.Materials.MaterialFeature.Ready)SprocketExpandedBallistics.Materials.ResponseUi.Reload();
    }
    public override void Load()
    {
        ModLog=Log;
        // 0.22.23: the Kit table and charts exist from UI Kit 0.1.6/0.1.7; with an older Kit the plugin refuses to load, with a clear line.
        var kitVersion=typeof(UiKit).Assembly.GetName().Version?.ToString(3);
        if(!KitVersionGate.Supported(kitVersion)){Log.LogError(KitVersionGate.Message(kitVersion));return;}
        GameBuildGate.Configure(Config,Log);
        RuntimeGroupedProtection.Configure(Config);
        RuntimeCompositePlate.Configure(Config);
        RuntimeSolidCollider.Configure(Config);
        SprocketExpandedBallistics.Materials.MaterialEditorPanel.Configure(Config);
        RuntimeCassettePlateVisual.Configure(Config);
        CompositeModule.Load(BepInEx.Paths.ConfigPath);
        ShellFeature.Load(this);
        var trajectoryOwner=new Harmony("sprocket.expandedballistics.original-trajectory");
        try
        {
            if(RuntimeOriginalTrajectory.Install(trajectoryOwner))
                Log.LogInfo("HEAT/APFSDS coherent perforation paths loaded; native ricochets and stops retained.");
        }
        catch(Exception ex)
        {
            trajectoryOwner.UnpatchSelf();RuntimeOriginalTrajectory.Clear();
            Log.LogWarning("Coherent perforation paths unavailable; native trajectories retained: "+ex.Message);
        }
        try{SprocketExpandedBallistics.Materials.MaterialFeature.Initialize(Log,()=>ShellReady,RefreshArmourResponses,()=>RuntimeArmourResponses.Snapshot);}
        catch(Exception ex){Log.LogError("Material module unavailable; shell module retained: "+ex);}
        try{new Harmony("sprocket.expandedballistics.designer.independent-structure").PatchAll(typeof(RuntimeIndependentStructureUi));}
        catch(Exception ex){Log.LogWarning("Independent structure editor unavailable: "+ex.Message);}
        try{new Harmony("sprocket.expandedballistics.designer.plate-stack").PatchAll(typeof(RuntimePlateStackUi));}
        catch(Exception ex){Log.LogWarning("Plate stack editor unavailable: "+ex.Message);}
        try{new Harmony("sprocket.expandedballistics.cassette.missing-preset").PatchAll(typeof(RuntimeMissingCassette));}
        catch(Exception ex){new Harmony("sprocket.expandedballistics.cassette.missing-preset").UnpatchSelf();Log.LogWarning("Missing user cassette recovery unavailable: "+ex.Message);}
        if(GameBuildGate.AllowHotPath)try{new Harmony("sprocket.expandedballistics.cassette.hit-geometry").PatchAll(typeof(NativeCassetteGeometry));}
        catch(Exception ex){new Harmony("sprocket.expandedballistics.cassette.hit-geometry").UnpatchSelf();NativeCassetteGeometry.Clear();Log.LogWarning("Cassette face geometry unavailable: "+ex.Message);}
        try{new Harmony("sprocket.expandedballistics.cassette.live-technology").PatchAll(typeof(RuntimeLiveTechnology));RuntimeLiveTechnology.Patched=true;}
        catch(Exception ex){new Harmony("sprocket.expandedballistics.cassette.live-technology").UnpatchSelf();RuntimeLiveTechnology.Patched=false;Log.LogWarning("Live cassette technology for Play unavailable; a newly saved cassette needs a restart: "+ex.Message);}
        if(GameBuildGate.AllowHotPath)try{new Harmony("sprocket.expandedballistics.designer.composite-plate").PatchAll(typeof(RuntimeCompositePlate));}
        catch(Exception ex){new Harmony("sprocket.expandedballistics.designer.composite-plate").UnpatchSelf();Log.LogWarning("Composite plate inspector unavailable: "+ex.Message);}
        if(GameBuildGate.AllowHotPath)try{new Harmony("sprocket.expandedballistics.designer.cassette-visual").PatchAll(typeof(RuntimeCassettePlateVisual));}
        catch(Exception ex){new Harmony("sprocket.expandedballistics.designer.cassette-visual").UnpatchSelf();RuntimeCassettePlateVisual.Clear();Log.LogWarning("Cassette visual unavailable: "+ex.Message);}
        if(GameBuildGate.AllowHotPath)try{new Harmony("sprocket.expandedballistics.solid-collider").PatchAll(typeof(RuntimeSolidCollider));}
        catch(Exception ex){new Harmony("sprocket.expandedballistics.solid-collider").UnpatchSelf();RuntimeSolidCollider.Clear();Log.LogWarning("Solid block native collider unavailable; thin-shell cavity fallback retained: "+ex.Message);}
    }
    public override bool Unload(){try{new Harmony("sprocket.expandedballistics.solid-collider").UnpatchSelf();RuntimeSolidCollider.Clear();SprocketExpandedBallistics.Materials.MaterialEditorPanel.Clear();new Harmony("sprocket.expandedballistics.cassette.missing-preset").UnpatchSelf();RuntimeMissingCassette.Clear();new Harmony("sprocket.expandedballistics.cassette.hit-geometry").UnpatchSelf();NativeCassetteGeometry.Clear();new Harmony("sprocket.expandedballistics.designer.cassette-visual").UnpatchSelf();RuntimeCassettePlateVisual.Clear();new Harmony("sprocket.expandedballistics.designer.composite-plate").UnpatchSelf();RuntimeCompositePlate.Clear();new Harmony("sprocket.expandedballistics.cassette.live-technology").UnpatchSelf();RuntimeLiveTechnology.Clear();new Harmony("sprocket.expandedballistics.original-trajectory").UnpatchSelf();RuntimeOriginalTrajectory.Clear();new Harmony("sprocket.expandedballistics.designer.plate-stack").UnpatchSelf();RuntimePlateStackUi.Clear();new Harmony("sprocket.expandedballistics.designer.independent-structure").UnpatchSelf();RuntimeIndependentStructureUi.Clear();SprocketExpandedBallistics.Materials.MaterialFeature.Unload();}finally{try{RuntimeCassettePlateVisual.Clear();}finally{ShellFeature.Unload();}}return base.Unload();}
}
internal static class Ui
{
    internal static UnityAction<int> IntCallback(Action<int> action) => DelegateSupport.ConvertDelegate<UnityAction<int>>(action)!;
    internal static Il2CppSystem.Action<bool> BoolCallback(Action<bool> action) => DelegateSupport.ConvertDelegate<Il2CppSystem.Action<bool>>(action)!;
}













