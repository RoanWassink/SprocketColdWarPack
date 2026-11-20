using BepInEx.Configuration;
using HarmonyLib;
using Sprocket.DamageModelling;
using Sprocket.Vehicles;
using Sprocket.Vehicles.Cannons;
using Sprocket.Vehicles.Weapons.Cannons;
using UnityEngine;
using UnityEngine.InputSystem;
using NVector = System.Numerics.Vector3;
using PlayerVehicleController = Sprocket.Gameplay.VehicleControl.VehicleController;

namespace SprocketExpandedBallistics;

// Separate optional Harmony owner: native collision/impact evaluation stays in the game.
[HarmonyPatch]
internal static class RuntimeAtgm
{
    private sealed record AimPoint(Vector3 Origin, Vector3 Direction, float Time, IntPtr Controller, float? Range = null, Vector3? NativeTarget = null);
    private sealed class Flight
    {
        internal readonly CannonBehaviour Weapon;
        internal readonly IntPtr Register;
        internal readonly ProjectileRegister Owner;
        internal readonly IntPtr NativeTransform;
        internal readonly int Id;
        internal readonly ShellProfile Profile;
        internal readonly string Definition;
        internal readonly float SpawnTime;
        internal AtgmLaunchCapture? Capture;
        internal int CaptureReports;
        internal double InitialSpeed;
        internal bool MotorStarted;
        internal Vector3 Heading;
        internal readonly Vector3 LaunchHeading;
        internal float LastLog;
        internal bool Report;
        internal Vector3 BeforePosition;
        internal bool Guide = true;
        // Extended flight tuning is snapshotted at launch. Range is the exact-sight
        // convergence or AI estimate at launch, never a hidden live target lookup.
        internal AtgmFlightProfile Tuning = AtgmFlightProfile.Legacy;
        internal AtgmFlightState Dynamics = new(0);
        internal float? LaunchRange;
        internal string LaunchRangeSource="unavailable";
        internal Vector3 LaunchPosition;
        internal double RangeProgress;
        internal AtgmRangePattern RangePattern = AtgmRangePattern.Normal;
        // Top attack: a consumed player lock and the current climb/cruise/dive phase. The
        // lock point follows its collider transform while that stays alive, then freezes.
        internal bool TopLocked;
        internal int TopPhase;
        internal Vector3 TopPoint;
        internal Vector3 TopLocal;
        internal Transform? TopTarget;
        internal int TopTargetId;
        internal Vector3 TopVelocity;
        internal float TopObservedAt;
        internal string? TopStatus;
        internal readonly IntPtr Vehicle;
        internal readonly float Calibre;
        internal Flight(CannonBehaviour weapon,ShellProfile profile,ProjectileInstance p,ProjectileRegister register)
        {Owner=register;NativeTransform=p.transform?.Pointer ?? IntPtr.Zero;Register=register.Pointer;Id=p.ID;Weapon=weapon;Profile=profile;Definition=p.Definition.Guid.ToString();SpawnTime=p.spawnTime;Heading=p.velocity.normalized;LaunchHeading=Heading;InitialSpeed=Math.Clamp(p.velocity.magnitude,10,profile.Atgm!.FlightSpeed);Vehicle=weapon.mount?.TryCast<Cannon>()?.Vehicle?.Behaviour?.Pointer ?? IntPtr.Zero;Calibre=weapon.mount?.TryCast<Cannon>()?.Blueprint.Caliber??135;}
    }
    private static readonly Dictionary<(IntPtr Register,int Id),Flight> Flights=new();
    internal static bool HasFlights => Flights.Count!=0;
    internal static string? TopAttackStatus()
    {
        if(!Ready||!Enabled)return null;
        Flight? latest=null;var now=Time.time;
        foreach(var f in Flights.Values)
            if(f.Vehicle==currentVehicle&&f.TopLocked&&f.TopStatus!=null&&now-f.SpawnTime>=0&&now-f.SpawnTime<f.Profile.Atgm!.MaximumFlightTime&&(latest==null||f.SpawnTime>latest.SpawnTime))latest=f;
        return latest?.TopStatus;
    }
    private static readonly Dictionary<IntPtr,AimPoint> Aims=new();
    private static readonly Dictionary<IntPtr,AimPoint> PlayerAims=new();
    private static readonly HashSet<string> Warnings=new();
    private static IntPtr currentVehicle;
    private static readonly HashSet<IntPtr> LockedControllers=new();
    private static ConfigEntry<bool> enabled=null!;
    private static ConfigEntry<bool> diagnostics=null!;
    private static ConfigEntry<double> topAttackLockSeconds=null!;
    internal static bool Enabled => enabled?.Value ?? false;
    internal static double TopAttackLockSeconds => topAttackLockSeconds?.Value is {} value&&double.IsFinite(value)?Math.Clamp(value,.1,5):1d;
    internal static bool Ready;
    internal static void Configure(ConfigFile config)
    {
        IReadOnlyDictionary<string,string> legacy=new Dictionary<string,string>();
        try{if(File.Exists(config.ConfigFilePath)&&new FileInfo(config.ConfigFilePath).Length<1024*1024)legacy=TopAttackLockPolicy.LegacyConfig(File.ReadAllText(config.ConfigFilePath));}
        catch(Exception ex){Warn("Legacy guidance settings",ex);}
        bool OldBool(string key,bool fallback)=>legacy.TryGetValue(key,out var raw)&&bool.TryParse(raw,out var value)?value:fallback;
        var oldDwell=legacy.TryGetValue("TopAttackLockSeconds",out var rawDwell)&&double.TryParse(rawDwell,System.Globalization.NumberStyles.Float,System.Globalization.CultureInfo.InvariantCulture,out var parsed)&&double.IsFinite(parsed)?Math.Clamp(parsed,.1,5):1d;
        // Existing new-section values win through Config.Bind; legacy values seed missing entries only.
        enabled=config.Bind("ATGM guidance","Enabled",OldBool("Enabled",true),"Enable ATGM powered flight and guidance. Other shells are unaffected.");
        diagnostics=config.Bind("ATGM guidance","DiagnosticLogging",OldBool("DiagnosticLogging",false),"Log commanded and native movement once per second for guidance troubleshooting.");
        topAttackLockSeconds=config.Bind("ATGM guidance","TopAttackLockSeconds",oldDwell,new ConfigDescription("Seconds the top-attack lock key must stay on a target before the lock completes (0.1-5).",new AcceptableValueRange<double>(.1,5)));
    }
    private static bool Finite(Vector3 v)=>float.IsFinite(v.x)&&float.IsFinite(v.y)&&float.IsFinite(v.z);
    internal static void EndModularFlight(ProjectileInstance projectile)
    {
        RuntimeWire.Hit(projectile);
        // Collision hands ownership back to native penetration. Never restore motor
        // energy or steer a continuing penetrator after its first contact.
        var nativeTransform=projectile.transform?.Pointer ?? IntPtr.Zero;
        var impactVisual=false;
        foreach(var pair in Flights.Where(e=>nativeTransform!=IntPtr.Zero&&e.Value.NativeTransform==nativeTransform&&e.Key.Id==projectile.ID&&e.Value.SpawnTime==projectile.spawnTime&&e.Value.Definition==projectile.Definition.Guid.ToString()).ToArray())
        {
            // One optional visual for this exact tracked missile's first contact.
            // The match plus the removal below prevent repeat impacts; expiry,
            // release, reset and air destruction never reach this branch.
            if(!impactVisual)
            {
                impactVisual=true;
                try{RuntimeAtgmTrail.SpawnImpact(pair.Key.Register,projectile.position,projectile.Definition.Diameter);}
                catch(Exception ex){Warn("Impact visual",ex);}
            }
            Flights.Remove(pair.Key);
        }
    }
    private static void Warn(string area,Exception ex)
    {if(Warnings.Add(area))Plugin.ModLog.LogWarning($"[ATGM] {area}: {ex.Message}");}
    private static float? ScopeRange(ScopeController scope)
    {
        try
        {
            var range=scope.GunConverganceDistance;
            return AtgmFlightDynamics.ValidLaunchRange(range)?(float)range:null;
        }
        catch(Exception ex){Warn("Scope range",ex);return null;}
    }
    // Correlate only this weapon's exact sight sample, then the fresh AI estimate.
    private static float? ExactLaunchRange(Flight flight,float now)
    {
        try
        {
            var sight=flight.Weapon.Sight;
            // One read-only visible-surface query at player Kobra launch. A scope
            // convergence setting is not proof of the target's actual distance.
            if(flight.Tuning.RangePattern=="hump"&&IsPlayerVehicle(flight.Vehicle))
            {
                AimPoint? sample=null;
                if(sight!=null&&Aims.TryGetValue(sight.Pointer,out var optical)&&AtgmGuidance.FreshAim(now,optical.Time))sample=optical;
                else if(PlayerAims.TryGetValue(flight.Vehicle,out var player)&&AtgmGuidance.FreshAim(now,player.Time))sample=player;
                var owner=flight.Weapon.mount?.TryCast<Cannon>()?.Vehicle?.Behaviour;
                if(sample!=null&&owner!=null&&RuntimeTopAttackLock.TryAcquire(owner,new Ray(sample.Origin,sample.Direction),out var point,out _))
                {
                    var distance=Vector3.Distance(flight.LaunchPosition,point);
                    if(AtgmFlightDynamics.ValidLaunchRange(distance)){flight.LaunchRangeSource="visible-ray";return distance;}
                }
                flight.LaunchRangeSource="direct-no-visible-range";return null;
            }
            if(sight!=null && Aims.TryGetValue(sight.Pointer,out var aim) &&
                AtgmGuidance.FreshAim(now,aim.Time) && aim.Range is {} range &&
                AtgmFlightDynamics.ValidLaunchRange(range))return range;
            // Non-hump profiles retain the existing scope/AI range contract.
            // A player without a fresh exact-scope sample uses the documented
            // direct fallback rather than borrowing an unrelated AI cache.
            if(!IsPlayerVehicle(flight.Vehicle) && RuntimeAtgmAi.TryAimRange(flight.Weapon.Pointer,now,out var aiRange))
                return aiRange;
        }
        catch(Exception ex){Warn("Launch range",ex);}
        return null;
    }

    internal static bool IsPlayerVehicle(IntPtr vehicle) => vehicle!=IntPtr.Zero && vehicle==currentVehicle;
    internal static bool TryPlayerAim(IntPtr vehicle,out Vector3 direction)
    {
        direction=default;
        if(!IsPlayerVehicle(vehicle)||!PlayerAims.TryGetValue(vehicle,out var aim)||!AtgmGuidance.FreshAim(Time.time,aim.Time))return false;
        direction=aim.Direction;return true;
    }
    internal static bool WireOwner(IntPtr register,int id)=>Flights.TryGetValue((register,id),out var flight)&&flight.Guide;
    private static bool WireIntact(Flight flight)=>flight.Profile.Wire?.Transport!="wire"||RuntimeWire.Connected(flight.Register,flight.Id);
    internal static IEnumerable<(int Id, float Spawn, Vector3 Position, Vector3 Direction, float Calibre, bool Burning, Transform Native)> VisualFlights(ProjectileRegister register)
    {
        foreach(var flight in VisualFlightDetails(register))
            yield return (flight.Id,flight.Spawn,flight.Position,flight.Direction,flight.Calibre,flight.Burning,flight.Native);
    }
    // Extended read-only view for the optional tracer; the legacy view above is unchanged.
    internal static IEnumerable<(int Id, float Spawn, Vector3 Position, Vector3 Direction, float Calibre, bool Burning, float Age, bool FlareEligible, Transform Native)> VisualFlightDetails(ProjectileRegister register)
    {
        foreach(var entry in Flights.Where(e=>e.Key.Register==register.Pointer).ToArray())
        {
            var flight=entry.Value;
            if(!ShellBalance.Powered(flight.Profile))continue;
            if(!register.activeIdMap.TryGetValue(entry.Key.Id,out var index))continue;
            var p=register.pool[index];
            if(p.spawnTime!=flight.SpawnTime || p.Definition.Guid.ToString()!=flight.Definition)continue;
            var settings=flight.Profile.Atgm!;
            var age=Time.time-flight.SpawnTime;
            var powered=ShellBalance.Powered(flight.Profile);
            var burning=powered&&age>=settings.MotorDelay && (settings.MotorBurnTime<=0 || age<settings.MotorDelay+settings.MotorBurnTime);
            var calibre=flight.Calibre;
            // A physical tail flare belongs to the live missile, not to whichever
            // flight currently owns command guidance. Impact/release/reset/reuse
            // are still handled by the flight list and the effect cleanup keys.
            var flareEligible=AtgmTracerPolicy.FlareEligible(true,powered,settings.GuidanceMode);
            yield return (entry.Key.Id,flight.SpawnTime,p.position,p.velocity.normalized,calibre*.001f,burning,age,flareEligible,p.transform);
        }
    }
    private static bool KeyboardOwned(Flight flight, float now) => Ready && Enabled && flight.Profile.Atgm?.GuidanceMode == "keyboard" && flight.Guide && WireIntact(flight) && flight.Vehicle != IntPtr.Zero && flight.Vehicle == currentVehicle &&
        AtgmGuidance.KeyboardWindow(now,flight.SpawnTime,flight.Profile.Atgm.MaximumFlightTime) &&
        flight.Weapon.mount?.TryCast<Cannon>() is {} cannon && cannon.HealthFraction > 0;

    private static void PruneTerminated()
    {
        foreach(var pair in Flights.ToArray())
        {
            try
            {
                var flight=pair.Value;var register=flight.Owner;
                if(!register.activeIdMap.TryGetValue(flight.Id,out var index))
                {Flights.Remove(pair.Key);continue;}
                var p=register.pool[index];
                if(!AtgmGuidance.LiveFlight(true,flight.Id,p.ID,flight.SpawnTime,p.spawnTime,
                    flight.Definition,p.Definition.Guid.ToString(),(int)p.flags))Flights.Remove(pair.Key);
            }
            catch(Exception ex){Flights.Remove(pair.Key);Warn("Input flight liveness",ex);}
        }
    }

    // Block only the native vehicle command dispatch. Camera and general UI input remain
    // in the player state; no InputAction maps are disabled, so no persistent restore is needed.
    [HarmonyPrefix,HarmonyPatch(typeof(PlayerVehicleController),nameof(PlayerVehicleController.UpdateControl))]
    private static bool VehicleInput(PlayerVehicleController __instance)
    {
        try
        {
            currentVehicle=__instance.ControlledVehicle?.Pointer ?? IntPtr.Zero;
            PruneTerminated();
            var locked=Flights.Values.Any(f=>KeyboardOwned(f,Time.time));
            if(!locked)
            {
                if(LockedControllers.Remove(__instance.Pointer))Plugin.ModLog.LogInfo("[ATGM] INPUT unlocked");
                return true;
            }
            __instance.ResetDriveInputs();
            __instance.cruiseControlLevel=0;
            __instance.firing=false;
            if(__instance.driver!=null)__instance.driver.Solution=Sprocket.VehicleControl.DriveSolution.Idle;
            if(__instance.gunners!=null)foreach(var gun in __instance.gunners)if(gun!=null)gun.Firing=false;
            if(LockedControllers.Add(__instance.Pointer))
            {
                if(__instance.gunLayers!=null)foreach(var layer in __instance.gunLayers)if(layer!=null)layer.ClearTarget();
                Plugin.ModLog.LogInfo("[ATGM] INPUT locked: keyboard missile; use the Guide ATGM actions in Settings/keybinds (factory defaults W/S and A/D)");
            }
            return false;
        }
        catch(Exception ex){LockedControllers.Remove(__instance.Pointer);Warn("Vehicle input lock",ex);return true;}
    }

    // Capture the actual player control ray. This supports both scope and third-person aiming,
    // and binds commands to the launcher's vehicle rather than relying on sight wrapper identity.
    [HarmonyPostfix,HarmonyPatch(typeof(PlayerVehicleController),nameof(PlayerVehicleController.UpdateControl))]
    private static void PlayerAim(PlayerVehicleController __instance,Ray __1)
    {
        if(!Enabled)return;
        try
        {
            var vehicle=__instance.ControlledVehicle;
            if(vehicle!=null && Finite(__1.origin) && Finite(__1.direction) && __1.direction.sqrMagnitude>.5f)
            {
                PlayerAims[vehicle.Pointer]=new(__1.origin,__1.direction.normalized,Time.time,__instance.Pointer,NativeTarget:__instance.TargetPoint);
                RuntimeAtgmGuidanceSight.PlayerAim(vehicle.Pointer);
                CaptureOpticalAim(__instance);
                // Top-attack lock acquisition shares the player control ray; the lock store
                // ignores it unless the optional lock keybind is held.
                RuntimeTopAttackLock.Track(vehicle.Pointer,vehicle,__1);
            }
        }
        catch(Exception ex){Warn("Player aim ray",ex);}
    }

    private static void CaptureOpticalAim(PlayerVehicleController controller)
    {
        try
        {
                // Native ScopeController.Update/SetAimPoint can be inlined. Read the
                // actual active scope here, on the verified player control path.
                var scope=controller.ScopeControl?.TryCast<ScopeController>();
                var optical=scope?.ControlledScope;
                if(scope!=null&&optical!=null)
                {
                    if(scope.Scoped&&scope.HasActive&&scope.controller!=null&&optical.SightParent!=null)
                    {
                        var origin=optical.SightParent.TransformPoint(optical.ObjectiveLensLocalPosition);
                        var direction=scope.controller.TargetRotation*Vector3.forward;
                        if(Finite(origin)&&Finite(direction)&&direction.sqrMagnitude>.5f)
                            Aims[optical.Pointer]=new(origin,direction.normalized,Time.time,scope.Pointer,ScopeRange(scope));
                    }
                    else Aims.Remove(optical.Pointer);
                }
        }
        catch(Exception ex){Warn("Optical aim fallback",ex);}
    }

    [HarmonyPostfix,HarmonyPatch(typeof(ScopeController),nameof(ScopeController.Update))]
    private static void Sight(ScopeController __instance)
    {
        if(!Enabled)return;
        try
        {
            var scope=__instance.ControlledScope;
            if(scope==null)return;
            if(!__instance.Scoped || !__instance.HasActive){Aims.Remove(scope.Pointer);return;}
            // Fallback for callers where the simple SetAimPoint method was inlined.
            // Convert the native convergence point back to a ray; never chase that fixed point.
            if(!Aims.TryGetValue(scope.Pointer,out var existing) || Time.time-existing.Time>.1f)
            {
                var parent=scope.SightParent;
                if(parent==null)return;
                var origin=parent.TransformPoint(scope.ObjectiveLensLocalPosition);
                var direction=__instance.ScopeAimPoint-origin;
                if(Finite(origin)&&Finite(direction)&&direction.sqrMagnitude>1)
                    Aims[scope.Pointer]=new(origin,direction.normalized,Time.time,__instance.Pointer,ScopeRange(__instance));
            }
        }
        catch(Exception ex){Warn("Scope lookup",ex);}
    }
    [HarmonyPostfix,HarmonyPatch(typeof(ScopeController),nameof(ScopeController.SetAimPoint),new[]{typeof(Vector3),typeof(Vector3)})]
    private static void AimRay(ScopeController __instance,Vector3 __0,Vector3 __1)
    {
        if(!Enabled)return;
        try
        {
            var scope=__instance.ControlledScope;
            if(scope!=null && __instance.Scoped && __instance.HasActive && Finite(__0) && Finite(__1) && __1.sqrMagnitude>.5f)
                Aims[scope.Pointer]=new(__0,__1.normalized,Time.time,__instance.Pointer,ScopeRange(__instance));
        }
        catch(Exception ex){Warn("Aim ray",ex);}
    }
    [HarmonyPrefix,HarmonyPatch(typeof(ScopeController),nameof(ScopeController.Dispose))]
    private static void DisposeSight(ScopeController __instance)
    {foreach(var key in Aims.Where(p=>p.Value.Controller==__instance.Pointer).Select(p=>p.Key).ToArray())Aims.Remove(key);}

    [HarmonyPostfix,HarmonyPatch(typeof(ProjectileRegister),nameof(ProjectileRegister.Launch))]
    private static void Launched(ProjectileRegister __instance,Il2CppSystem.Object source,ref ProjectileFireInfo fireInfo)
    {
        if(!Ready || !Enabled || !RuntimeShellSelection.LaunchAccepted)return;
        try
        {
            var weapon=source?.TryCast<CannonBehaviour>();
            if(weapon==null || !__instance.activeIdMap.TryGetValue(fireInfo.ProjectileID,out var index))return;
            var p=__instance.pool[index];
            var profile=RuntimeShellSelection.ProjectileProfile(p);
            if(profile==null)return;
            if(!RuntimeShellEra.Allowed(profile,weapon.mount?.TryCast<Cannon>()?.Vehicle))return;
            RuntimeWire.Attach(__instance,p,weapon,profile);
            if(!ShellBalance.FlightEnabled(profile)||profile.Atgm==null)return;
            var flight=new Flight(weapon,profile,p,__instance);
            flight.LaunchPosition=p.position;
            if(profile.Atgm.GuidanceMode is "sight" or "keyboard" && weapon.mount?.TryCast<Cannon>() is {} launchCannon &&
               RuntimeAtgmGuidanceSight.TryAim(launchCannon,out _,out var captureAim,out _))
                flight.Capture=new AtgmLaunchCapture(new NVector(flight.LaunchHeading.x,flight.LaunchHeading.y,flight.LaunchHeading.z),new NVector(captureAim.x,captureAim.y,captureAim.z));
            flight.Tuning=profile.Atgm.GuidanceMode=="topattack"?AtgmFlightProfile.Legacy:profile.Atgm.FlightProfile??AtgmFlightProfile.Legacy;
            flight.LaunchRange=ExactLaunchRange(flight,Time.time);
            flight.RangePattern=AtgmFlightDynamics.SelectPattern(flight.Tuning,flight.LaunchRange);
            flight.Dynamics=new AtgmFlightState(AtgmFlightDynamics.Seed(p.ID,p.spawnTime,profile.Id));
            if(flight.Tuning.RangePattern=="hump"&&flight.LaunchRange is null&&Warnings.Add("hump-range"))
                Plugin.ModLog.LogWarning("[ATGM] Hump profile launched without a fresh exact-sight/AI range; using direct guidance without the long lockout.");
            foreach(var old in Flights.Values.Where(f=>TopAttackLockPolicy.Supersede(f.Profile.Atgm!.GuidanceMode)&&(f.Weapon.Pointer==weapon.Pointer || flight.Vehicle!=IntPtr.Zero && f.Vehicle==flight.Vehicle)&&!RuntimeAtgmGuidanceSight.GuidesTogether(f.Weapon,f.Profile.Atgm!.GuidanceMode,weapon,profile.Atgm.GuidanceMode)))old.Guide=false; // 0.22.87 ATGM group replaced
            if(profile.Atgm.GuidanceMode=="topattack")
            {
                var vehicle=weapon.mount?.TryCast<Cannon>()?.Vehicle?.Behaviour;
                Vector3 point=default;Transform? target=null;var acquired=false;
                if(vehicle!=null)
                {
                    if(IsPlayerVehicle(vehicle.Pointer))acquired=RuntimeTopAttackLock.TryConsume(vehicle.Pointer,vehicle,out point,out target);
                    else if(RuntimeAtgmAi.TryAim(weapon.Pointer,Time.time,out var origin,out var direction))
                        acquired=RuntimeTopAttackLock.TryAcquire(vehicle,new Ray(origin,direction),out point,out target);
                }
                if(acquired)
                {
                    // A real roof surface on this same rigidbody, not a guessed weak spot.
                    RuntimeTopAttackAim.Prepare(ref point,ref target);
                    flight.TopLocked=true;flight.TopPoint=point;flight.TopTarget=target;flight.TopObservedAt=Time.time;
                    flight.TopPhase=AtgmGuidance.TopAttackCanLoft(new(p.position.x,p.position.y,p.position.z),new(point.x,point.y,point.z),new(flight.Heading.x,flight.Heading.y,flight.Heading.z),profile.Atgm,p.velocity.magnitude)?AtgmGuidance.TopAttackClimb:AtgmGuidance.TopAttackDirect;
                    if(flight.TopPhase==AtgmGuidance.TopAttackDirect)
                    {
                        var range=new NVector(point.x-p.position.x,0,point.z-p.position.z).Length();
                        RuntimeTopAttackLock.DirectApproach(flight.Vehicle,range<AtgmGuidance.TopAttackMinimumRange(p.position.y-point.y,profile.Atgm,p.velocity.magnitude));
                    }
                    if(target!=null){try{flight.TopLocal=target.InverseTransformPoint(point);flight.TopTargetId=target.GetInstanceID();}catch{flight.TopTarget=null;}}
                }
                // No later acquisition: an unlocked launch remains straight for its whole flight.
            }
            Flights[(__instance.Pointer,p.ID)]=flight;
            var launchLabel=flight.LaunchRange is {} launchValue ? launchValue.ToString("0") : "unavailable";
            Plugin.ModLog.LogInfo($"[ATGM] LAUNCH id={p.ID} profile={profile.Id} guidance={profile.Atgm.GuidanceMode} law={flight.Tuning.GuidanceLaw} pattern={flight.RangePattern} launchRange={launchLabel} rangeSource={flight.LaunchRangeSource} guidanceDelay={AtgmFlightDynamics.EffectiveGuidanceDelay(profile.Atgm.GuidanceDelay,flight.Tuning,flight.LaunchRange):0.00}s speed={p.velocity.magnitude:0.0}m/s cruise={profile.Atgm.FlightSpeed:0}m/s acceleration={profile.Atgm.Acceleration:0}m/s2 vehicle={flight.Vehicle}; command-guided missiles share vehicle input; locked top-attack flights remain independent.");
        }
        catch(Exception ex){Plugin.ModLog.LogError("[ATGM] Launch tracking: "+ex);}
    }

    [HarmonyPrefix,HarmonyPatch(typeof(ProjectileRegister),nameof(ProjectileRegister.FixedUpdate))]
    private static void BeforeTick(ProjectileRegister __instance)
    {
        if(!Ready || !Enabled){RuntimeWire.Clear(__instance.Pointer);return;}
        RuntimeWire.Tick(__instance);
        if(Flights.Count==0)return;
        var now=Time.time;var dt=Time.fixedDeltaTime;
        if(!float.IsFinite(dt)||dt<=0)return;
        foreach(var pair in Flights.Where(p=>p.Key.Register==__instance.Pointer).ToArray())
        {
            var key=pair.Key;var flight=pair.Value;
            try
            {
                if(!__instance.activeIdMap.TryGetValue(key.Id,out var index)){Flights.Remove(key);continue;}
                var p=__instance.pool[index];
                // Guard against pooled slots being reused or a projectile already terminated.
                if(p.ID!=key.Id || p.spawnTime!=flight.SpawnTime || p.Definition.Guid.ToString()!=flight.Definition ||
                    // Projectiles.dll Active=1, Release=2; another game assembly exposes a different enum with the same name.
                    ((int)p.flags & 1)==0 || ((int)p.flags & 14)!=0)
                {Flights.Remove(key);continue;}
                var settings=flight.Profile.Atgm!;var age=now-flight.SpawnTime;
                if(age>=settings.MaximumFlightTime){__instance.Release(key.Id);Flights.Remove(key);Plugin.ModLog.LogInfo($"[ATGM] EXPIRED id={key.Id}; no detonation.");continue;}
                var powered=ShellBalance.Powered(flight.Profile);
                if(powered&&age<settings.MotorDelay)
                {
                    // Let native gravity/drag integrate the actual ejection velocity.
                    flight.Heading=p.velocity.normalized;
                    continue;
                }
                if(powered&&!flight.MotorStarted)
                {
                    flight.MotorStarted=true;
                    flight.InitialSpeed=Math.Clamp(p.velocity.magnitude,10,settings.FlightSpeed);
                    flight.Heading=p.velocity.normalized;
                    Plugin.ModLog.LogInfo($"[ATGM] MOTOR id={key.Id} age={age:0.00}s speed={p.velocity.magnitude:0.0}m/s heading={flight.Heading}");
                }
                var speed=powered?AtgmFlightDynamics.FlightSpeed(settings,flight.Tuning,flight.InitialSpeed,Math.Max(0,age)):p.velocity.magnitude;
                if(!powered)flight.Heading=p.velocity.normalized;
                if(!float.IsFinite((float)speed)||speed<.01)continue;
                var steeringSettings=settings with {FlightSpeed=speed};
                var delay=AtgmFlightDynamics.EffectiveGuidanceDelay(settings.GuidanceDelay,flight.Tuning,flight.LaunchRange);
                Vector3? target=null;
                var top=settings.GuidanceMode=="topattack";
                var cannon=top?null:flight.Weapon.mount?.TryCast<Cannon>();
                var sight=top?null:flight.Weapon.Sight;
                var wireIntact=top||WireIntact(flight);
                // Optional initial elevation relative to the launch heading. It is
                // active only during the initial lockout, never in the direct
                // short-range fallback, and obeys the configured turn cap.
                if(!top && flight.Guide && wireIntact && age<delay && flight.Tuning.InitialElevationDegrees>0)
                {
                    var elevated=AtgmFlightDynamics.InitialElevationHeading(
                        new NVector(flight.LaunchHeading.x,flight.LaunchHeading.y,flight.LaunchHeading.z),
                        flight.Tuning.InitialElevationDegrees,settings.MaxTurnRate,dt,
                        new NVector(flight.Heading.x,flight.Heading.y,flight.Heading.z),
                        true,flight.RangePattern==AtgmRangePattern.Direct);
                    var elevatedVector=new Vector3(elevated.X,elevated.Y,elevated.Z);
                    if(Finite(elevatedVector)&&elevatedVector.sqrMagnitude>1e-8f)flight.Heading=elevatedVector.normalized;
                }
                AimPoint? aim=null;
                var reason=!flight.Guide ? "superseded" : age<delay ? "arming" : top ? "topattack-no-lock" : settings.GuidanceMode!="sight" ? "unguided" :
                    cannon==null || cannon.HealthFraction<=0 ? "launcher-unavailable" : "no-fresh-player-ray";
                if(!wireIntact)reason="wire-disconnected";
                if(!top && flight.Guide && wireIntact && age>=delay && settings.GuidanceMode=="sight" && cannon!=null && cannon.HealthFraction>0)
                {
                    if(RuntimeAtgmGuidanceSight.UsesIndependentSight(cannon))
                    {
                        if(RuntimeAtgmGuidanceSight.TryAim(cannon,out var opticalOrigin,out var opticalDirection,out var opticalRange))
                        {aim=new(opticalOrigin,opticalDirection,now,IntPtr.Zero,opticalRange);reason="independent-sight";}
                        else reason="independent-sight-inactive";
                    }
                    else if(flight.Tuning.RangePattern=="hump"&&sight!=null&&Aims.TryGetValue(sight.Pointer,out var exactOptical)&&AtgmGuidance.FreshAim(now,exactOptical.Time))
                    {aim=exactOptical;reason="scope-ray";}
                    else if(flight.Vehicle!=IntPtr.Zero && PlayerAims.TryGetValue(flight.Vehicle,out var playerAim) && AtgmGuidance.FreshAim(now,playerAim.Time))
                    {
                        aim=playerAim;reason="player-ray";
                        if(flight.Tuning.RangePattern=="hump"&&sight?.SightParent is {} parent&&playerAim.NativeTarget is {} nativeTarget)
                        {
                            var lens=parent.TransformPoint(sight.ObjectiveLensLocalPosition);
                            if(AtgmFlightDynamics.TryOpticalAim(new NVector(lens.x,lens.y,lens.z),new NVector(nativeTarget.x,nativeTarget.y,nativeTarget.z),out var opticalDirection))
                            {aim=new(lens,new Vector3(opticalDirection.X,opticalDirection.Y,opticalDirection.Z),playerAim.Time,playerAim.Controller);reason="player-point-optical";}
                        }
                    }
                    else if(sight!=null && Aims.TryGetValue(sight.Pointer,out var scopeAim) && AtgmGuidance.FreshAim(now,scopeAim.Time))
                    {aim=scopeAim;reason="scope-ray";}
                }
                if(!top && flight.Guide && wireIntact && age>=delay && settings.GuidanceMode is "sight" or "keyboard" &&
                    !IsPlayerVehicle(flight.Vehicle) && cannon!=null && cannon.HealthFraction>0 &&
                    RuntimeAtgmAi.TryAim(flight.Weapon.Pointer,now,out var aiOrigin,out var aiDirection))
                {
                    // Command-guided AI retains its accepted direct-ray steering.
                    aim=new(aiOrigin,aiDirection,now,IntPtr.Zero);reason="ai-ray";
                    steeringSettings=steeringSettings with {GuidanceMode="sight"};
                }
                if(aim!=null && !top)
                {
                    var haveTarget=false;
                    var aimTarget=default(Vector3);
                    if(flight.Tuning.GuidanceLaw=="losLead" &&
                        AtgmFlightDynamics.TryAimAlongSightRay(new NVector(aim.Origin.x,aim.Origin.y,aim.Origin.z),
                            new NVector(aim.Direction.x,aim.Direction.y,aim.Direction.z),
                            new NVector(p.position.x,p.position.y,p.position.z),flight.Tuning.LeadDistanceM,out var losPoint))
                        {aimTarget=new Vector3(losPoint.X,losPoint.Y,losPoint.Z);haveTarget=true;}
                    if(!haveTarget)
                    {
                        var legacy=AtgmGuidance.AimAlongRay(new(aim.Origin.x,aim.Origin.y,aim.Origin.z),
                            new(aim.Direction.x,aim.Direction.y,aim.Direction.z),new(p.position.x,p.position.y,p.position.z),speed);
                        aimTarget=new Vector3(legacy.X,legacy.Y,legacy.Z);
                    }
                    if(flight.RangePattern==AtgmRangePattern.Hump && flight.LaunchRange is {} launchRange)
                    {
                        flight.RangeProgress=AtgmFlightDynamics.RangeProgress(
                            new NVector(flight.LaunchPosition.x,flight.LaunchPosition.y,flight.LaunchPosition.z),
                            new NVector(p.position.x,p.position.y,p.position.z),flight.RangeProgress);
                        var current=flight.RangeProgress;
                        var hump=AtgmFlightDynamics.HumpOffset(flight.Tuning.HumpHeightM,launchRange,current,
                            flight.Tuning.HumpTerminalDistanceM,flight.Tuning.HumpRampDistanceM);
                        if(hump>0 && float.IsFinite((float)hump))aimTarget+=Vector3.up*(float)hump;
                    }
                    var direction=aimTarget-p.position;
                    if(Finite(direction) && direction.sqrMagnitude>1)
                    {
                        var offset=AtgmFlightDynamics.Disturbance(flight.Tuning,age,flight.Dynamics,
                            new NVector(direction.x,direction.y,direction.z));
                        if(offset.LengthSquared()>0)
                        {
                            direction+=new Vector3(offset.X,offset.Y,offset.Z);
                            if(Finite(direction) && direction.sqrMagnitude>1)target=direction;
                        }
                        else target=direction;
                    }
                }
                if(top && flight.TopLocked && age>=delay)
                {
                    if(flight.TopTarget!=null)
                    {
                        try
                        {
                            if(flight.TopTarget.GetInstanceID()!=flight.TopTargetId)flight.TopTarget=null;
                            else
                            {
                                var tracked=flight.TopTarget.TransformPoint(flight.TopLocal);
                                if(Finite(tracked))
                                {
                                    var elapsed=now-flight.TopObservedAt;
                                    if(elapsed>.0001f && elapsed<.5f)
                                    {
                                        var observed=(tracked-flight.TopPoint)/elapsed;
                                        flight.TopVelocity=Finite(observed)&&observed.sqrMagnitude<=3600?Vector3.Lerp(flight.TopVelocity,observed,Math.Clamp(elapsed/.15f,0,1)):Vector3.zero;
                                    }
                                    else flight.TopVelocity=Vector3.zero;
                                    flight.TopPoint=tracked;flight.TopObservedAt=now;
                                }
                                else flight.TopTarget=null;
                            }
                        }
                        catch{flight.TopTarget=null;}
                    }
                    if(flight.TopTarget==null)flight.TopVelocity=Vector3.zero;
                    var led=AtgmGuidance.TopAttackLead(new(flight.TopPoint.x,flight.TopPoint.y,flight.TopPoint.z),new(flight.TopVelocity.x,flight.TopVelocity.y,flight.TopVelocity.z),new(p.position.x,p.position.y,p.position.z),speed);
                    var point=new Vector3(led.X,led.Y,led.Z);var have=Finite(point);
                    if(have)
                    {
                        var steer=AtgmGuidance.TopAttackSteer(new(p.position.x,p.position.y,p.position.z),new(point.x,point.y,point.z),
                            new(flight.Heading.x,flight.Heading.y,flight.Heading.z),speed,flight.TopPhase,settings);
                        if(steer is {} steerCommand)
                        {
                            flight.TopPhase=steerCommand.Phase;
                            var phaseLabel=flight.TopPhase==AtgmGuidance.TopAttackClimb?"CLIMB":flight.TopPhase==AtgmGuidance.TopAttackCruise?"CRUISE":flight.TopPhase==AtgmGuidance.TopAttackDive?"DIVE":"DIRECT";
                            flight.TopStatus=$"TOP ATTACK | {phaseLabel} | {Vector3.Distance(p.position,flight.TopPoint):0} m";
                            target=new Vector3(steerCommand.Direction.X,steerCommand.Direction.Y,steerCommand.Direction.Z);
                            reason=steerCommand.Phase==AtgmGuidance.TopAttackDirect?"topattack-direct-close":steerCommand.Phase==AtgmGuidance.TopAttackClimb?"topattack-climb":steerCommand.Phase==AtgmGuidance.TopAttackCruise?"topattack-cruise":"topattack-dive";
                        }
                    }
                    else reason="topattack-no-lock";
                }
                var heading=new NVector(flight.Heading.x,flight.Heading.y,flight.Heading.z);
                var desired=target is {} t ? new NVector(t.x,t.y,t.z) : (NVector?)null;
                var velocity=AtgmGuidance.Step(heading,desired,steeringSettings,dt);
                var gathering=false;
                if(flight.Capture is {Enabled:true,Finished:false} capture)
                {
                    NVector? captureDirection=null;bool gathered=false;
                    if(flight.Guide&&wireIntact&&cannon!=null&&cannon.HealthFraction>0&&
                        RuntimeAtgmGuidanceSight.TryAim(cannon,out var captureOrigin,out var captureRay,out _))
                    {
                        var origin=new NVector(captureOrigin.x,captureOrigin.y,captureOrigin.z);
                        var ray=new NVector(captureRay.x,captureRay.y,captureRay.z);
                        var position=new NVector(p.position.x,p.position.y,p.position.z);
                        var along=NVector.Dot(position-origin,ray);
                        var cross=position-origin-ray*along;
                        gathered=cross.Length()<4;
                        captureDirection=origin+ray*AtgmLaunchCapture.GatherLead(along,speed,cross.Length())-position;
                    }
                    var previous=capture.Status;
                    velocity=capture.Step(heading,captureDirection,age,speed,dt,gathered);
                    gathering=!capture.Finished;reason="capture-"+capture.Status;
                    if(previous!=capture.Status&&flight.CaptureReports++<8)Plugin.ModLog.LogInfo($"[ATGM capture] id={key.Id} phase={capture.Status} age={age:0.00}s speed={speed:0.0}m/s");
                }
                if(!gathering && KeyboardOwned(flight,now) && age>=delay &&
                    PlayerAims.TryGetValue(flight.Vehicle,out var owner) && AtgmGuidance.FreshAim(now,owner.Time))
                {
                    var (yaw,pitch)=AtgmGuidanceKeys.Poll();
                    // One combined input/disturbance command under a single
                    // turn cap. Zero tuning is the exact legacy command.
                    if(AtgmFlightDynamics.ManualCommand(heading,yaw,pitch,steeringSettings,flight.Tuning,
                        age,flight.Dynamics,dt,true) is {} keyboardVelocity)
                    {
                        velocity=keyboardVelocity;
                        reason=$"keyboard yaw={yaw} pitch={pitch}";
                    }
                }
                var command=new Vector3(velocity.X,velocity.Y,velocity.Z);
                flight.Heading=command.normalized;
                // pool[index] is a boxed value copy: commit it back before native movement/collisions.
                // Native drag/gravity still act within this step; powered flight restores command next tick.
                p.velocity=command;
                __instance.pool[index]=p;
                if(diagnostics.Value && now-flight.LastLog>=1){flight.LastLog=now;flight.Report=true;flight.BeforePosition=p.position;Plugin.ModLog.LogInfo($"[ATGM] COMMAND id={key.Id} age={age:0.0}s speed={command.magnitude:0.0}m/s guidance={(reason.StartsWith("keyboard")?"keyboard":reason.StartsWith("topattack")?reason:target.HasValue?reason:"straight")} reason={reason} playerCache={PlayerAims.Count} scopeCache={Aims.Count} vehicle={flight.Vehicle} heading={flight.Heading} position={p.position}");}
            }
            catch(Exception ex){Flights.Remove(key);Plugin.ModLog.LogError($"[ATGM] Flight id={key.Id} reverted to native ballistic movement: {ex}");}
        }
    }
    [HarmonyPostfix,HarmonyPatch(typeof(ProjectileRegister),nameof(ProjectileRegister.FixedUpdate))]
    private static void AfterTick(ProjectileRegister __instance)
    {
        foreach(var pair in Flights.Where(p=>p.Key.Register==__instance.Pointer && p.Value.Report).ToArray())
        {
            var flight=pair.Value;flight.Report=false;
            try
            {
                if(!__instance.activeIdMap.TryGetValue(pair.Key.Id,out var index))continue;
                var p=__instance.pool[index];
                if(p.spawnTime!=flight.SpawnTime || p.Definition.Guid.ToString()!=flight.Definition)continue;
                Plugin.ModLog.LogInfo($"[ATGM] NATIVE id={pair.Key.Id} speed={p.velocity.magnitude:0.0}m/s moved={Vector3.Distance(p.position,flight.BeforePosition):0.00}m per tick headingDot={Vector3.Dot(p.velocity.normalized,flight.Heading):0.000}");
            }
            catch(Exception ex){Warn("Native movement diagnostics",ex);}
        }
    }
    [HarmonyPostfix,HarmonyPatch(typeof(ProjectileRegister),nameof(ProjectileRegister.Release))]
    private static void Released(ProjectileRegister __instance,int __0){RuntimeWire.Release(__instance.Pointer,__0);Flights.Remove((__instance.Pointer,__0));}
    [HarmonyPrefix,HarmonyPatch(typeof(ProjectileRegister),nameof(ProjectileRegister.DestroyAll))]
    private static void Clear(ProjectileRegister __instance)
    {RuntimeWire.Clear(__instance.Pointer);foreach(var key in Flights.Keys.Where(k=>k.Register==__instance.Pointer).ToArray())Flights.Remove(key);Aims.Clear();PlayerAims.Clear();Warnings.Clear();LockedControllers.Clear();currentVehicle=IntPtr.Zero;RuntimeAtgmAi.Clear();RuntimeTopAttackLock.Clear();RuntimeAtgmGuidanceSight.Clear();}
}
