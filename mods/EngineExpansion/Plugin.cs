using BepInEx;
using BepInEx.Unity.IL2CPP;
using BepInEx.Logging;
using HarmonyLib;
using System.Security.Cryptography;
namespace SprocketEngineExpansion;

[BepInPlugin(Guid, "Sprocket Engine Expansion", "0.7.3")]
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
        Detailed=Config.Bind("Diagnostics","DetailedLogging",false,"Verbose trace lines for testing. Warnings and errors are always logged.").Value;
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
        var shaftAngle=Config.Bind<double>("Drivetrain","DriveshaftAngleMultiplier",2,"Native shaft connection-angle limit multiplier (1..2, capped at 180 degrees) when UnlimitedDriveshaftAngle is false. Restart after changing.");
        var unlimitedAngle=Config.Bind<bool>("Drivetrain","UnlimitedDriveshaftAngle",true,"Allow driveshaft connections at any angle (180 degrees). False falls back to DriveshaftAngleMultiplier.");
        var repairShift=Config.Bind<bool>("Drivetrain","RepairInvalidShiftRPM",true,"Repair out-of-range stored shift RPM through the active native engine editor. Valid player shift choices and custom limiter remain unchanged.");
        Native.DrivetrainAdapter.Volume=()=>drivetrainVolume.Value;
        Native.DrivetrainAdapter.AngleMultiplier=()=>shaftAngle.Value;
        Native.DrivetrainAdapter.UnlimitedAngle=()=>unlimitedAngle.Value;
        Editor.EngineEditor.ShiftRepairEnabled=repairShift.Value;
        Install("drivetrain",typeof(Native.DrivetrainAdapter));
        var powerpackFrom=Config.Bind<string>("Powerpack","AvailableFrom","1965.01.01","Earliest vehicle date (yyyy.MM.dd) for the Powerpack option. Provisional. Vehicles on the final-era date 9999.12.31 always allow it.");
        var housingMass=Config.Bind<double>("Powerpack","HousingMassFraction",.10,"Powerpack housing mass as a fraction of the engine mass (0..1).");
        var housingCost=Config.Bind<double>("Powerpack","HousingCostFraction",.15,"Powerpack housing cost as a fraction of the engine material cost (0..1).");
        Core.EngineDates.PowerpackDate=Core.EngineDates.ParseDate(powerpackFrom.Value,Core.EngineDates.DefaultPowerpackDate);
        Native.PowerpackLayout.MassFraction=Checked(housingMass,.10);
        Native.PowerpackLayout.CostFraction=Checked(housingCost,.15);
        Native.PowerpackLayout.CollapseJunctions=Config.Bind("Powerpack.Layout","CollapseJunctions",true,"Collapse one-input/one-output junction chains in the powerpack undo transaction. False restores 0.4.1 eligibility.").Value;
        var collapseDistance=Config.Bind("Powerpack.Layout","MaxCollapseDistance",10.0,new BepInEx.Configuration.ConfigDescription("Maximum engine-output to gearbox-input distance in layout metres for newly collapsed chains.",new BepInEx.Configuration.AcceptableValueRange<double>(.1,20)));
        Native.PowerpackLayout.MaxCollapseDistance=collapseDistance.Value;
        var monoblockOverlap=Config.Bind("Powerpack.Layout","MonoblockOverlap",Core.PowerpackRules.DefaultMonoblockOverlap,new BepInEx.Configuration.ConfigDescription("How deeply (metres) the transmission tucks flush into the engine rear when Powerpack.Collision.Enabled is true.",new BepInEx.Configuration.AcceptableValueRange<double>(0,.25)));
        Native.PowerpackLayout.MonoblockOverlap=monoblockOverlap.Value;
        Native.PowerpackCollision.Enabled=Config.Bind("Powerpack.Collision","Enabled",true,"Ignore false obstruction inside an active powerpack (engine <-> own transmission(s) and twin left <-> right). External collisions with hull, crew, ammo, fuel and other engines stay active.").Value;
        Native.PowerpackCollision.DriveshaftMode=Config.Bind("Powerpack.Collision","DriveshaftMode",Core.PowerpackDriveshaftCollisionMode.SamePowertrain,"Ignore obstruction between driveshafts and an active powerpack: SamePowertrain (any shaft of the same powertrain), ConnectedOnly (shafts directly attached to the powerpack), or None.").Value;
        Native.PowerpackCollision.Installed=Install("powerpack-collision",typeof(Native.PowerpackCollision));
        Native.PowerpackLayout.Installed=Install("powerpack",typeof(Native.PowerpackLayout));
        Native.PowerpackBridge.CasingEnabled=Config.Bind("Powerpack.Model","Casing",false,"Render the continuous powerpack casing. Visual only; off by default because flush monoblock placement already joins the engine and transmission. Restart after changing.").Value;
        var modelEnabled=Config.Bind("Powerpack.Model","Enabled",true,"Use the MTU/Renk powerpack model. Restart after changing model settings.");
        var size=Config.Bind("Powerpack.Model","SizeFactor",1d,new BepInEx.Configuration.ConfigDescription("All engine dimensions.",new BepInEx.Configuration.AcceptableValueRange<double>(.8,1.25)));
        var length=Config.Bind("Powerpack.Model","LengthFactor",.9,new BepInEx.Configuration.ConfigDescription("Whole crankshaft length: start, pairs and output.",new BepInEx.Configuration.AcceptableValueRange<double>(.8,1.25)));
        var height=Config.Bind("Powerpack.Model","HeightFactor",1d,new BepInEx.Configuration.ConfigDescription("Engine height, including crankshaft and centre of mass.",new BepInEx.Configuration.AcceptableValueRange<double>(.8,1.25)));
        var turbo=Config.Bind("Powerpack.Model","TurboLayout",Core.PowerpackTurboLayout.Compact,"Compact (inside width) or Outboard.");
        var transVolume=Config.Bind("Powerpack.Model","TransmissionVolumeFactor",1d,new BepInEx.Configuration.ConfigDescription("Powerpack transmission volume factor, on top of the global drivetrain factor.",new BepInEx.Configuration.AcceptableValueRange<double>(.5,1.5)));
        var turbineScale=Config.Bind("Powerpack.Model","TurbineScale",Core.PowerpackModelSettings.DefaultTurbineScale,new BepInEx.Configuration.ConfigDescription("Uniform mesh scale factor for gas turbine powerpack models (V layout).",new BepInEx.Configuration.AcceptableValueRange<double>(.6,1)));
        Native.PowerpackModel.TurbineScale=turbineScale.Value;
        Native.PowerpackModel.Settings=new Core.PowerpackModelSettings(modelEnabled.Value,size.Value,length.Value,height.Value,turbo.Value,transVolume.Value).Validate();
        Native.PowerpackModel.Installed=Install("powerpack-model",typeof(Native.PowerpackModel),typeof(Native.PowerpackTransmission),typeof(Native.PowerpackBridge));
        Native.EngineCharacterHooks.Enabled=Config.Bind("EngineCharacter","Enabled",true,"Enable engine type character traits (inertia, engine braking, spool lag, idle/load fuel burn, fire risk, exhaust, audio).").Value;
        Native.EngineCharacterHooks.SpoolAndFuel=Config.Bind("EngineCharacter","SpoolAndFuel",true,"Simulate turbine/diesel throttle spool lag and idle/part-load fuel consumption.").Value;
        Native.EngineCharacterHooks.FireRisk=Config.Bind("EngineCharacter","FireRisk",true,"Differentiate engine fire ignition probability and duration across Petrol, Diesel and Gas Turbine.").Value;
        Native.EngineCharacterHooks.Exhaust=Config.Bind("EngineCharacter","Exhaust",true,"Adjust exhaust smoke soot and airflow per engine type.").Value;
        Native.EngineAudio.Enabled=Config.Bind("EngineCharacter","Audio",true,"Synthesize gas turbine whine and diesel turbo whistle/rumble audio overrides.").Value;
        var audioVol=Config.Bind("EngineCharacter","AudioVolume",0.45,new BepInEx.Configuration.ConfigDescription("Master multiplier for procedural turbine whine and diesel turbo whistle audio.",new BepInEx.Configuration.AcceptableValueRange<double>(0,1.5)));
        Native.EngineAudio.AuxVolume=(float)audioVol.Value;
        Install("engine-character",typeof(Native.EngineCharacterHooks));
        Install("engine-audio",typeof(Native.EngineAudio));
        Log.LogInfo($"Engine Expansion 0.7.3 loaded. Powerpack from {Core.EngineDates.PowerpackDate:yyyy-MM-dd}. CollisionFilter={(Native.PowerpackCollision.Active?Native.PowerpackCollision.DriveshaftMode.ToString():"off")}. EngineCharacter={(Native.EngineCharacterHooks.Enabled?"on":"off")}. Turbo functionality is disabled; existing saved parts and metadata are retained.");
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

