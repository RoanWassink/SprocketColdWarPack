using BepInEx;
using BepInEx.Unity.IL2CPP;
using BepInEx.Logging;
using HarmonyLib;
using System.Security.Cryptography;
namespace SprocketEngineExpansion;

[BepInPlugin(Guid, "Sprocket Engine Expansion", "0.5.0")]
public sealed class Plugin : BasePlugin
{
    public const string Guid="sprocket.engineexpansion";
    internal static ManualLogSource ModLog=null!;
    internal static bool Detailed;
    private readonly List<Harmony> owners=new();
    internal static int MainThreadId;
    private static readonly HashSet<string> warnings=new();
    public override void Load()
    {
        ModLog=Log;
        MainThreadId=Environment.CurrentManagedThreadId;
        Detailed=Config.Bind("Diagnostics","DetailedLogging",true,"Verbose trace lines for testing. Warnings and errors are always logged.").Value;
        string assembly=Path.Combine(Paths.GameRootPath,"GameAssembly.dll");
        string hash="unreadable";
        try { using var file=File.OpenRead(assembly);using var sha=SHA256.Create();hash=Convert.ToHexString(sha.ComputeHash(file)); }
        catch { /* The same single safe-mode warning covers unreadable images. */ }
        if(!Core.StartupSafety.InstallHooks(hash)){Log.LogWarning(Core.StartupSafety.Warning);return;}
        if(!Install("persistence",typeof(Native.Persistence)))return;
        if(!Install("profiles",typeof(Native.BlueprintHooks),typeof(Native.RuntimeHooks)))
        {foreach(var owner in owners)owner.UnpatchSelf();owners.Clear();return;}
        // Dormant compatibility saves existing object metadata; no turbo lifecycle or geometry hooks.
        Install("legacy-turbo-data",typeof(Geometry.PartState),typeof(Geometry.AssemblyState),typeof(Geometry.PartPersistence));
        Install("editor",typeof(Editor.EngineEditor),typeof(Editor.EditorScope));
        var drivetrainVolume=Config.Bind<double>("Drivetrain","TransmissionVolumeMultiplier",.5,"Transmission physical volume at unchanged rated torque, mass, cost and ratios (0 < value <= 1; 1 restores vanilla size). Restart after changing.");
        var shaftAngle=Config.Bind<double>("Drivetrain","DriveshaftAngleMultiplier",2,"Native shaft connection-angle limit multiplier (1..2, capped at 180 degrees). Restart after changing.");
        var repairShift=Config.Bind<bool>("Drivetrain","RepairInvalidShiftRPM",true,"Repair out-of-range stored shift RPM through the active native engine editor. Valid player shift choices and custom limiter remain unchanged.");
        Native.DrivetrainAdapter.Volume=()=>drivetrainVolume.Value;
        Native.DrivetrainAdapter.AngleMultiplier=()=>shaftAngle.Value;
        Editor.EngineEditor.ShiftRepairEnabled=repairShift.Value;
        Install("drivetrain",typeof(Native.DrivetrainAdapter));
        var powerpackFrom=Config.Bind<string>("Powerpack","AvailableFrom","1965.01.01","Earliest vehicle date (yyyy.MM.dd) for the Powerpack option. Provisional. Vehicles on the final-era date 9999.12.31 always allow it.");
        var housingMass=Config.Bind<double>("Powerpack","HousingMassFraction",.10,"Powerpack housing mass as a fraction of the engine mass (0..1).");
        var housingCost=Config.Bind<double>("Powerpack","HousingCostFraction",.15,"Powerpack housing cost as a fraction of the engine material cost (0..1).");
        Core.EngineDates.PowerpackDate=Core.EngineDates.ParseDate(powerpackFrom.Value,Core.EngineDates.DefaultPowerpackDate);
        Native.PowerpackLayout.MassFraction=Checked(housingMass,.10);
        Native.PowerpackLayout.CostFraction=Checked(housingCost,.15);
        Native.PowerpackLayout.CollapseJunctions=Config.Bind("Powerpack.Layout","CollapseJunctions",true,"Collapse one-input/one-output junction chains in the powerpack undo transaction. False restores 0.4.1 eligibility.").Value;
        var collapseDistance=Config.Bind("Powerpack.Layout","MaxCollapseDistance",Core.LayoutCollapse.DefaultMaxDistance,new BepInEx.Configuration.ConfigDescription("Maximum engine-output to gearbox-input distance in layout metres. 1.75 includes Challenger 1 (1.701 m); excludes front-drive Panzer IV.",new BepInEx.Configuration.AcceptableValueRange<double>(.1,10)));
        Native.PowerpackLayout.MaxCollapseDistance=collapseDistance.Value;
        // Approved: native engine/gearbox collision and 1 mm placement; casing covers the seam.
        Native.PowerpackLayout.Installed=Install("powerpack",typeof(Native.PowerpackLayout));
        Native.PowerpackBridge.CasingEnabled=Config.Bind("Powerpack.Model","Casing",true,"Render the continuous powerpack casing. Visual only, native collision and 1 mm clearance remain. Restart after changing.").Value;
        var modelEnabled=Config.Bind("Powerpack.Model","Enabled",true,"Use the MTU/Renk powerpack model. Restart after changing model settings.");
        var size=Config.Bind("Powerpack.Model","SizeFactor",1d,new BepInEx.Configuration.ConfigDescription("All engine dimensions.",new BepInEx.Configuration.AcceptableValueRange<double>(.8,1.25)));
        var length=Config.Bind("Powerpack.Model","LengthFactor",.9,new BepInEx.Configuration.ConfigDescription("Whole crankshaft length: start, pairs and output.",new BepInEx.Configuration.AcceptableValueRange<double>(.8,1.25)));
        var height=Config.Bind("Powerpack.Model","HeightFactor",1d,new BepInEx.Configuration.ConfigDescription("Engine height, including crankshaft and centre of mass.",new BepInEx.Configuration.AcceptableValueRange<double>(.8,1.25)));
        var turbo=Config.Bind("Powerpack.Model","TurboLayout",Core.PowerpackTurboLayout.Compact,"Compact (inside width) or Outboard.");
        var transVolume=Config.Bind("Powerpack.Model","TransmissionVolumeFactor",1d,new BepInEx.Configuration.ConfigDescription("Powerpack transmission volume factor, on top of the global drivetrain factor.",new BepInEx.Configuration.AcceptableValueRange<double>(.5,1.5)));
        Native.PowerpackModel.Settings=new Core.PowerpackModelSettings(modelEnabled.Value,size.Value,length.Value,height.Value,turbo.Value,transVolume.Value).Validate();
        Native.PowerpackModel.Installed=Install("powerpack-model",typeof(Native.PowerpackModel),typeof(Native.PowerpackTransmission),typeof(Native.PowerpackBridge));
        Log.LogInfo($"Engine Expansion 0.5.0 loaded. Powerpack from {Core.EngineDates.PowerpackDate:yyyy-MM-dd}. Turbo functionality is disabled; existing saved parts and metadata are retained.");
    }
    private Func<double> Checked(BepInEx.Configuration.ConfigEntry<double> entry,double fallback)
    {
        double value=fallback;
        try{value=Core.PowerpackRules.Fraction(entry.Value,entry.Definition.Key);}catch(Exception ex){Warn("powerpack configuration",ex);}
        return ()=>value;
    }
    private bool Install(string suffix,params Type[] classes)
    {
        var h=new Harmony(Guid+"."+suffix);
        try{foreach(var type in classes)h.CreateClassProcessor(type).Patch();owners.Add(h);Log.LogInfo("Engine Expansion hooks: "+suffix);return true;}
        catch(Exception ex){h.UnpatchSelf();Warn(suffix,ex);return false;}
    }
    public override bool Unload()
    {
        // Runtime unloading is unsafe while a native job holds an adapted table.
        ModLog.LogWarning("Restart Sprocket to unload Engine Expansion safely.");return false;
    }
    internal static void Warn(string area,Exception ex)
    {
        // Harmony wraps installer failures; retain every cause and stack for diagnosis.
        string detail=ex.ToString();
        if(warnings.Add(area+detail))ModLog.LogWarning("Engine Expansion "+area+": "+detail);
    }
}

