using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using BepInEx.Unity.IL2CPP;
using HarmonyLib;
using Sprocket.Vehicles.AmmunitionStorage;
using Sprocket.WeaponFramework;
using Sprocket.Vehicles.CrewSystems;

namespace SprocketCarouselAutoloader;

[BepInPlugin(Guid, "Sprocket Carousel Autoloader", PluginVersion)]
// 0.2.26: optional HUD through the UI Kit; Carousel loads and works without it. 0.2.27: no version
// requirement either: rings from kit 0.1.8 on, bars with an older kit (see HudRingGate).
[BepInDependency(UiKitGuid, BepInDependency.DependencyFlags.SoftDependency)]
public sealed class Plugin : BasePlugin
{
    internal static ManualLogSource ModLog = null!;
    internal static bool DetailedLogging;
    internal static bool OwnRounds = true;
    internal const string Guid = "sprocket.carousel", UiKitGuid = "sprocket.uikit";
    /// <summary>0.2.32: the one version constant (BepInPlugin attribute and the load log line).</summary>
    public const string PluginVersion = "0.3.6";
    internal static double MaxFeedDistance;
    internal static bool HudEnabled;
    /// <summary>0.2.27: the loaded UI Kit plugin is 0.1.8 or later, so the HUD draws rings instead of bars.</summary>
    internal static bool HudRings;
    // 0.2.28 (BESLUITEN 2026-11-03): [Damage] NativeCookoff ("Schakelaar, standaard aan") and [Audio] Enabled.
    internal static bool NativeCookoff = true;
    internal static bool AudioEnabled = true;
    private static string? UnityVersion()
    {
        try { return UnityEngine.Application.unityVersion; }
        catch { return null; }
    }
    double Timing(string key, double fallback, string description)
    {
        var raw = Config.Bind("Revolver", key, fallback, description).Value;
        var value = RevolverPolicy.Positive(raw, fallback);
        if (value != raw) Log.LogWarning($"[Revolver] {key} = {raw} must be a finite number above 0; using the default {fallback}.");
        return value;
    }
    private void BindBelt()
    {
        double Number(string key, double fallback, string description) => Belt.Nonnegative(Config.Bind("Belt", key, fallback, description).Value, fallback);
        Belt.Enabled = Config.Bind("Belt", "Enabled", true, "Enable the belt feed box automatic loader.").Value;
        Belt.MaxCalibre = Number("MaxCalibre", 57, "Maximum complete-round diameter in mm. 0 disables the upper limit; there is no lower calibre limit.");
        Belt.MaxFeedDistance = Number("MaxFeedDistance", 1.5, "Maximum feed-box to breech distance in metres. 0 uses native crew hand reach.");
        Belt.BoxMassKg = Number("BoxMassKg", 25, "Fixed belt box mechanism mass in kg, excluding ammunition.");
        Belt.MassPerRoundKg = Number("MassPerRoundKg", .1, "Belt mechanism mass in kg per capacity slot; ammunition mass is counted by the game.");
        Belt.MaxRpm = Belt.Positive(Number("MaxRpm", 1200, "Maximum rounds per minute. The absolute ceiling is always 1200, including when this value is higher."), 1200);
        var rpm = Config.Bind("Belt", "RpmByCalibre", Belt.DefaultTable, "Ascending calibre-mm:rpm pairs separated by commas. The first upper bound at or above the calibre wins; above the last bound use its rpm.").Value;
        if (!Belt.SetTable(rpm)) Log.LogWarning("[Belt] Invalid RpmByCalibre table; using " + Belt.DefaultTable + ".");
        Belt.RefillSecondsPerRound = Number("RefillSecondsPerRound", 1, "Stationary crew refill seconds per round, at least 0.5, divided by loader efficiency.");
        Belt.StationarySeconds = Number("StationarySeconds", 3, "Continuous seconds below StationarySpeed before crew refill is allowed.");
        Belt.StationarySpeed = Belt.Positive(Number("StationarySpeed", .5, "Vehicle speed threshold in metres per second for stationary crew refill."), .5);
    }
    // HUD with the UI Kit only when the kit plugin is loaded; all kit code lives in AutoloaderHudView.
    private void StartHud()
    {
        var enabled = Config.Bind("Hud", "Enabled", true, "Show the autoloader HUD (magazine stock, fire cycle, refill) in Play, only while something is running. Needs the Sprocket UI Kit.").Value;
        var place = AutoloaderHudLayout.ParsePlacement(Config.Bind("Hud", "Placement", "BelowAmmo", new ConfigDescription(
            "BelowAmmo = directly under the Expanded Ballistics AMMO panel (same corner, X and width). Manual = Corner/OffsetX/OffsetY. Without that panel, or with it in a top corner, Corner/OffsetX/OffsetY are used too.",
            new AcceptableValueList<string>("BelowAmmo", "Manual"))).Value);
        var corner = Config.Bind("Hud", "Corner", "BottomRight", new ConfigDescription("Screen corner of the HUD for Manual (and as the fallback): TopLeft, TopRight, BottomLeft or BottomRight.",
            new AcceptableValueList<string>("TopLeft", "TopRight", "BottomLeft", "BottomRight"))).Value;
        var x = Config.Bind("Hud", "OffsetX", 24f, new ConfigDescription("Distance from the left/right screen edge in kit reference pixels (Manual and fallback).", new AcceptableValueRange<float>(0f, 1200f))).Value;
        var y = Config.Bind("Hud", "OffsetY", 230f, new ConfigDescription("Distance from the top/bottom screen edge in kit reference pixels (Manual and fallback).", new AcceptableValueRange<float>(0f, 900f))).Value;
        var gap = Config.Bind("Hud", "Gap", AutoloaderHudLayout.DefaultGap, new ConfigDescription("Space in kit reference pixels between the bottom of the AMMO panel and the top of this HUD (BelowAmmo).", new AcceptableValueRange<float>(0f, 200f))).Value;
        AutoloaderHudFeed.BlockedSeconds = Config.Bind("Hud", "BlockedSeconds", AutoloaderHudLayout.DefaultBlockedSeconds, new ConfigDescription(
            "How long a \"Blocked: <reason>\" line stays visible after it appears or its reason changes; then it hides until the reason changes or something runs again.", new AcceptableValueRange<float>(0f, 120f))).Value;
        HudEnabled = false; HudRings = false;
        if (!enabled) return;
        if (!BepInEx.Unity.IL2CPP.IL2CPPChainloader.Instance.Plugins.TryGetValue(UiKitGuid, out var kit)) { Log.LogInfo("HUD disabled: UI Kit not installed"); return; }
        // Rings need kit 0.1.8; all Radial calls live in AutoloaderHudRing, which is only touched when this is true.
        HudRings = HudRingGate.Supported(kit.Metadata.Version.Major, kit.Metadata.Version.Minor, kit.Metadata.Version.Patch);
        try { CreateHud(place, corner, x, y, gap); HudEnabled = true; }
        catch (Exception ex) { HudRings = false; Log.LogWarning($"HUD disabled: UI Kit HUD unavailable ({ex.Message}); loading is unaffected."); return; }
        Log.LogInfo($"HUD on: UI Kit {kit.Metadata.Version}, {(HudRings ? "rings" : "bars")}; placement {place}.");
    }
    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
    private static void CreateHud(HudPlace place, string corner, float x, float y, float gap) => AutoloaderHudView.Create(place, corner, x, y, gap);
    public override void Load()
    {
        ModLog = Log;
        BindBelt();
        // 0.2.23: static props; an old [Visuals] Animate line in the cfg is left unused.
        OwnRounds=Config.Bind("Visuals","OwnRounds",true,"Replace only bustle/revolver shell renderers with true-size rounds, with native ammunition fallback if unavailable.").Value;
        // 0.2.29 (BESLUITEN 2026-11-05 "Goed, bouw ze in"): carousel basket model; Simple is exactly the 0.2.28 model.
        CarouselModelGeometry.Model=Config.Bind("Visuals","CarouselModel",CarouselVisualModel.Detailed,"Carousel basket model. Detailed = T-72 style cassettes (projectile below, charge lying above), or T-80 style with upright charges for the vertical-charge layout, with hub and cassette lift. Simple = the procedural model of 0.2.28. A basket the detailed parts do not fit (too shallow or too narrow) gets the simple model automatically, with one Info line.").Value;
        // 0.2.32: smooth turn of the ring on a single shot; 0 = jump.
        CarouselModelGeometry.TurnSeconds=CarouselModelGeometry.SanitizeTurnSeconds(Config.Bind("Visuals","CarouselTurnSeconds",CarouselModelGeometry.DefaultTurnSeconds,"Seconds the carousel ring takes to turn to the next cassette after one shot or loading. 0 = the ring jumps. Refills, Play resets and loading a save always jump.").Value);
        CarouselModelGeometry.Lift=Config.Bind("Visuals","CarouselLift",true,"Show the cassette lift of the detailed carousel model behind the slots (local -Z, ram towards the breech). It stands just outside the basket and reaches up into the turret; false hides it if it shows through the hull or turret.").Value;
        CarouselGeometry.StackedCycleFactor=Config.Bind("Carousel","StackedCycleFactor",CarouselGeometry.DefaultStackedCycleFactor,"Fire cycle multiplier for the 2S38-style stacked unitary carousel relative to the standard carousel/bustle curve (0.57 gives ~0.5 s / 120 rpm at 57 mm).").Value;
        DetailedLogging=Config.Bind("Diagnostics","DetailedLogging",false,"Enable detailed loading diagnostics when troubleshooting.").Value;
        // 0.2.24: feed reach for bustle and revolver (Semi keeps the native reach) and the revolver limits.
        MaxFeedDistance=Math.Max(0,Config.Bind("Bustle","MaxFeedDistance",0.0,"Maximum distance in metres from the bustle/revolver feed arm to the breech load point. 0 = native crew hand reach; e.g. 1.5 allows a longer arm. The Semi-autoloader always uses the native reach.").Value);
        RevolverPolicy.MaxCaliberMm=Config.Bind("Revolver","MaxCaliberMm",RevolverPolicy.DefaultMaxCaliberMm,"Largest calibre in mm the revolver drum accepts (155 = the T58 drum).").Value;
        RevolverPolicy.MaxRoundLengthMm=Math.Max(0,Config.Bind("Revolver","MaxRoundLengthMm",RevolverPolicy.DefaultMaxRoundLengthMm,"Longest round (two-piece: longest piece) in mm the revolver accepts. 0 = no fixed limit: the rack length decides through the drum.").Value);
        // 0.2.25 (BESLUITEN 2026-11-02 "Ja, maak hem sneller"): revolver fire cycle 0.6 x bustle; refill from the bustle cycle.
        // A value <= 0, NaN or infinite falls back to the default (RevolverPolicy.Positive).
        RevolverPolicy.CycleFactor=Timing("CycleFactor",RevolverPolicy.DefaultCycleFactor,"Revolver fire cycle as a multiple of the bustle cycle for the same ammunition (0.6 = 40 percent faster than a bustle). Two-piece ammunition still adds x 1.25.");
        RevolverPolicy.RefillCycleFactor=Timing("RefillCycleFactor",RevolverPolicy.DefaultRefillCycleFactor,"Revolver refill time per round (per pair with two-piece) as a multiple of the BUSTLE cycle (two-piece factor included), without a crew loader, only while the vehicle stands still.");
        RevolverPolicy.MinimumRefillSeconds=Timing("MinimumRefillSeconds",RevolverPolicy.DefaultMinimumRefillSeconds,"Shortest revolver refill time in seconds per round: the refill takes max(this, RefillCycleFactor x bustle cycle).");
        // 0.2.28: safety switches, both on by default (BESLUITEN 2026-11-03).
        NativeCookoff=Config.Bind("Damage","NativeCookoff",true,"Carousel basket ammunition can be hit and cook off through the game's damage model (own hit mesh, health pool and fire control). false = the basket keeps the game's own damage behaviour; the carousel autoloader itself keeps working.").Value;
        AudioEnabled=Config.Bind("Audio","Enabled",true,"Own autoloader reload sounds. They use internal Unity audio calls and only run on the Unity version they were made for; on another version they stay off with one Info line. false = no own sound and no internal Unity calls.").Value;
        try
        {
            // Isolated native storage check; no vehicle or projectile is spawned.
            var size = ProjectileSizeID.FromPropellantLength(120, 30);
            if (size.ShellLength != 390 || size.Caliber != 120 || size.PropellantLength != 30)
                throw new InvalidOperationException("Native ammunition-size encoding differs from the inspected version.");
            var rack = new AmmoRackBehaviour("Carousel startup check", default, size, 3);
            rack.FullyLoad();
            if (rack.AmountStored != 3 || !rack.TakeShell() || rack.AmountStored != 2)
                throw new InvalidOperationException("Native finite storage check failed.");
            Log.LogDebug("[Carousel] Native storage check passed: 120 mm / 30 mm propellant = 390 mm complete round; finite supply 3 -> 2.");
            var magazine = new AmmoRackBehaviour("Magazine refill startup check", default, size, 2);
            var total = rack.AmountStored + magazine.AmountStored;
            if (!rack.TakeShell()) throw new InvalidOperationException("Native reserve transfer failed.");
            magazine.LoadAmmo(1);
            if (magazine.AmountStored != 1 || rack.AmountStored + magazine.AmountStored != total)
                throw new InvalidOperationException("Native magazine transfer failed conservation check.");
            magazine.LoadAmmo(10);
            if (magazine.AmountStored != magazine.Capacity)
                throw new InvalidOperationException("Native magazine capacity clamp failed.");
            Log.LogDebug("[Bustle] Native refill check passed: one reserve shot transferred, total stock conserved, magazine capacity clamped.");
            var uncommandedPreparation = LoaderRules.GetPrepareTime(0f);
            var commandedPreparation = LoaderRules.GetPrepareTime(1f);
            if (!float.IsFinite(uncommandedPreparation) || uncommandedPreparation <= 0f || commandedPreparation != 0f)
                throw new InvalidOperationException("Native crew preparation-time check failed.");
            Log.LogDebug($"[Bustle] Native crew preparation check passed: command efficiency 0 adds {uncommandedPreparation:0.00} s; efficiency 1 adds no delay.");
            var loader = new LoadContributeTask { efficiency = 1f, commandEfficiency = 1f, operateCount = 1 };
            var nativeLoader = loader.Cast<ILoadTaskContributor>();
            if (nativeLoader.EnableFraction != 1f)
                throw new InvalidOperationException("Native automatic loader enable check failed.");
            loader.operateCount = 0;
            loader.efficiency = 0f;
            loader.commandEfficiency = 0f;
            // This native task's EnableFraction is constant 1; contribution is gated by efficiency.
            if (loader.efficiency != 0f || loader.commandEfficiency != 0f || loader.operateCount != 0)
                throw new InvalidOperationException("Native automatic loader contribution disable check failed.");
            Log.LogDebug("[Carousel] Native automatic loader check passed: interface enabled; contribution efficiency 1 -> 0.");
            CarouselPersistence.CheckNativeJsonRoundTrip();
            Log.LogDebug("[Carousel] Native blueprint JSON check passed: 12 carousel/bustle settings round-trips, vanilla data and default settings preserved.");
        }
        catch (Exception ex) { Log.LogError($"Carousel disabled before patching: {ex}"); return; }
        var core = new Harmony("sprocket.carousel.core");
        // 0.2.28: NativeCycleDiagnostics, the merged hooks of the audio/visual groups and RevolverCountdown have no
        // patches of their own any more; the merged core hooks call them, only while their group is patched.
        try { core.PatchAll(typeof(VehicleLoadIndex)); VehicleLoadIndex.CheckHooks(core.Id); core.PatchAll(typeof(CarouselRuntime)); core.PatchAll(typeof(BustleRuntime)); core.PatchAll(typeof(BustleClearance)); core.PatchAll(typeof(MagazineRefill)); core.PatchAll(typeof(AutoloaderCycle)); core.PatchAll(typeof(CarouselPlayBoundary)); }
        catch (Exception ex) { core.UnpatchSelf(); Log.LogError($"Carousel disabled: {ex}"); return; }
        var damage=new Harmony("sprocket.carousel.native-damage");
        if (!SafetySwitches.PatchNativeDamage(NativeCookoff))
        { CarouselNativeDamage.Available=false; Log.LogInfo("Carousel native cookoff off (config Damage.NativeCookoff = false): the basket keeps the game's own damage behaviour; loading unchanged."); }
        else
        {
            try { damage.PatchAll(typeof(CarouselNativeDamage)); CarouselNativeDamage.CheckHooks(damage.Id); CarouselNativeDamage.Available=true; }
            catch(Exception ex) { damage.UnpatchSelf(); CarouselNativeDamage.Available=false; Log.LogWarning($"Carousel native cookoff unavailable; existing loading retained: {ex.Message}"); }
        }
        var semi = new Harmony("sprocket.carousel.semi-assist");
        try { semi.PatchAll(typeof(SemiAutoloader)); SemiAutoloader.Available = true; }
        catch (Exception ex) { semi.UnpatchSelf(); SemiAutoloader.Available = false; Log.LogWarning($"Semi-autoloader assistance disabled; existing automatic loaders retained: {ex.Message}"); }
        var display=new Harmony("sprocket.carousel.assisted-display");
        try { display.PatchAll(typeof(SemiCountdown)); }
        catch(Exception ex) { display.UnpatchSelf(); Log.LogWarning($"Assisted countdown correction unavailable; gameplay retained: {ex.Message}"); }
        var ui = new Harmony("sprocket.carousel.ui");
        try { ui.PatchAll(typeof(CarouselInspector)); ui.PatchAll(typeof(BustleInspector)); }
        catch (Exception ex) { ui.UnpatchSelf(); Log.LogError($"Basket inspector disabled: {ex}"); }
        StartHud();
        var visuals = new Harmony("sprocket.carousel.visuals");
        try { visuals.PatchAll(typeof(BustleVisuals)); visuals.PatchAll(typeof(AutoloaderVisuals)); BustleVisuals.Available=true; BustleVisuals.Icon(); BustleVisuals.Icon(BustleRuntime.RevolverPartGuid); Log.LogDebug("[Visuals] Autoloader model and icon hooks ready."); }
        catch (Exception ex) { visuals.UnpatchSelf(); BustleVisuals.Available=false; Log.LogError($"Bustle visuals disabled: {ex}"); }
        var carouselVisuals = new Harmony("sprocket.carousel.carousel-visuals");
        try { carouselVisuals.PatchAll(typeof(CarouselVisuals)); CarouselVisuals.Available=true; }
        catch (Exception ex) { carouselVisuals.UnpatchSelf(); CarouselVisuals.Available=false; Log.LogWarning($"Carousel visuals disabled: {ex.Message}"); }
        var compatibility = new Harmony("sprocket.carousel.compatibility");
        try { compatibility.PatchAll(typeof(TurretAudioGuard)); }
        catch (Exception ex) { compatibility.UnpatchSelf(); Log.LogWarning($"Turret audio guard disabled: {ex}"); }
        // 0.2.28 (M9): the own reload audio uses internal Unity calls; only with [Audio] Enabled and on the Unity
        // version they were made for. Otherwise no clip is uploaded and the audio group is not patched.
        var audio = new Harmony("sprocket.carousel.audio");
        var running = UnityVersion();
        var audioOff = SafetySwitches.AudioOff(AudioEnabled, running);
        AutoloaderAudio.Active = false;
        if (audioOff != null) Log.LogInfo($"Autoloader reload audio off: {audioOff}. Loading unchanged.");
        else
        {
            try { AutoloaderAudio.PreloadClips(); audio.PatchAll(typeof(AutoloaderAudio)); AutoloaderAudio.Active = true; Log.LogDebug("[Autoloaders] Custom reload audio enabled for cycles of at least one second."); }
            catch (Exception ex) { audio.UnpatchSelf(); AutoloaderAudio.Active = false; Log.LogWarning($"Autoloader audio disabled: {ex}"); }
        }
        Log.LogDebug($"[Bustle] Maximum feed distance uses native crew hand reach: {BustleRuntime.MaximumDistance:0.00} m.");
        var reach = MaxFeedDistance > 0 ? $"{MaxFeedDistance:0.00} m (config)" : "native";
        var length = RevolverPolicy.MaxRoundLengthMm > 0 ? $"{RevolverPolicy.MaxRoundLengthMm:0} mm" : "no length limit";
        Log.LogInfo($"Sprocket Autoloaders {PluginVersion} loaded; HUD {(HudEnabled ? (HudRings ? "on (rings)" : "on (bars)") : "off")}; feed arm at the breech side, two-piece option; feed reach {reach}; revolver limit {RevolverPolicy.MaxCaliberMm:0} mm, {length}; revolver cycle {RevolverPolicy.CycleFactor:0.##} x bustle, refill max({RevolverPolicy.MinimumRefillSeconds:0.##} s, {RevolverPolicy.RefillCycleFactor:0.##} x bustle cycle); native cookoff {(CarouselNativeDamage.Available ? "on" : "off")}; reload audio {(AutoloaderAudio.Active ? "on" : "off")}.");
    }
}






