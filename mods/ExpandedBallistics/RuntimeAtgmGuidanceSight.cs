using BepInEx.Configuration;
using HarmonyLib;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using Sprocket;
using Sprocket.Vehicles;
using Sprocket.Vehicles.AttachedBehaviours;
using Sprocket.Vehicles.Cannons;
using Sprocket.Vehicles.CrewSystems;
using Sprocket.WeaponFramework;
using UnityEngine;
namespace SprocketExpandedBallistics;

// Reuses the native AssignedSightVUID save/remap path. References below are runtime only.
[HarmonyPatch]
internal static class RuntimeAtgmGuidanceSight
{
    internal const string Guid="cd1dfaf1-4ce6-4d8d-a1b5-fc0d09f35001";
    internal const string Owner="sprocket.expandedballistics.atgm.guidance-sight";
    internal static bool Ready;
    private static ConfigEntry<bool>? enabled;
    private static ConfigEntry<float>? headYawRate,headPitchRate,minYaw,maxYaw,minPitch,maxPitch;
    private sealed class Head
    {
        internal readonly GunnerSight Sight;
        internal readonly Cannon Cannon;
        internal AtgmHeadAngles Angles;
        internal int Revision,Frame=-1;
        internal Head(GunnerSight sight,Cannon cannon){Sight=sight;Cannon=cannon;Revision=revision;}
    }
    private static readonly Dictionary<IntPtr,Head> heads=new();
    private static readonly Dictionary<IntPtr,int> nativeSights=new();
    private static readonly Dictionary<IntPtr,Cannon> launchers=new();
    private sealed class Session
    {
        internal readonly ScopeController Scope;
        internal readonly GunnerSight Sight;
        internal readonly Cannon Cannon;
        internal Transform? Previous;
        internal readonly AtgmSightReferenceState Reference;
        internal int Revision;
        internal HashSet<IntPtr> ExclusiveAxes;
        internal bool Initialized;
        internal ScopeCameraController? Camera;
        internal readonly AtgmSightElevationState Elevation=new();
        internal readonly Dictionary<IntPtr,(GunLayer Layer,IntPtr Axis,Vector3 Direction)> Driven=new();
        internal HashSet<IntPtr> SightAxes;
        internal Session(ScopeController scope,GunnerSight sight,Cannon cannon,int revision,HashSet<IntPtr> exclusiveAxes)
        {Scope=scope;Sight=sight;Cannon=cannon;Previous=sight.LinkedWeapon;Reference=new(Previous?.Pointer??IntPtr.Zero);Revision=revision;ExclusiveAxes=exclusiveAxes;SightAxes=RuntimeAtgmSightMount.Axes(sight,cannon);}
    }
    private static readonly Dictionary<IntPtr,Session> sessions=new();
    private static int revision;
    private static readonly HashSet<string> warnings=new();
    internal static void Configure(ConfigFile config)
    {
        const string section="ATGM guidance sight";
        enabled=config.Bind(section,"Enabled",true,"Independent camera and fixed tube control for the dedicated ATGM guidance sight assigned to one dedicated launcher. Native ammunition and reloading remain. Restart to change.");
        headYawRate=config.Bind(section,"HeadYawRate",60f,new ConfigDescription("Self-aiming head yaw speed in degrees/second.",new AcceptableValueRange<float>(0,720)));
        headPitchRate=config.Bind(section,"HeadPitchRate",40f,new ConfigDescription("Self-aiming head pitch speed in degrees/second.",new AcceptableValueRange<float>(0,720)));
        minYaw=config.Bind(section,"HeadMinYaw",-170f,new ConfigDescription("Minimum head yaw relative to the sight part, degrees.",new AcceptableValueRange<float>(-180,0)));
        maxYaw=config.Bind(section,"HeadMaxYaw",170f,new ConfigDescription("Maximum head yaw relative to the sight part, degrees.",new AcceptableValueRange<float>(0,180)));
        minPitch=config.Bind(section,"HeadMinPitch",-20f,new ConfigDescription("Minimum head elevation relative to the sight part, degrees.",new AcceptableValueRange<float>(-89,0)));
        maxPitch=config.Bind(section,"HeadMaxPitch",60f,new ConfigDescription("Maximum head elevation relative to the sight part, degrees.",new AcceptableValueRange<float>(0,89)));
    }
    private static float Setting(ConfigEntry<float>? entry,float fallback)=>entry!=null&&float.IsFinite(entry.Value)?entry.Value:fallback;
    private static AtgmHeadLimits Limits=>new(Setting(minYaw,-170),Setting(maxYaw,170),Setting(minPitch,-20),Setting(maxPitch,60));
    internal static bool Enabled=>Ready&&enabled?.Value==true&&RuntimeAtgm.Ready&&RuntimeAtgm.Enabled;
    private static bool Own(GunnerSight? sight)=>sight?.VehicleObject?.GUID==Guid;
    private static int Count<T>(Il2CppSystem.Collections.Generic.IReadOnlyList<T> items)=>items.Cast<Il2CppSystem.Collections.Generic.IReadOnlyCollection<T>>().Count;
    private static IEnumerable<Cannon> Cannons(GunnerSight sight)
    {
        var objects=sight.Vehicle.ObjectReader.Items;
        for(int i=0;i<Count(objects);i++){var parts=objects[i].Components;
            for(int j=0;j<Count(parts);j++)if(parts[j].TryCast<Cannon>() is {} cannon)yield return cannon;}
    }
    private static bool Resolve(GunnerSight sight,out Cannon? result,out HashSet<IntPtr> exclusiveAxes)
    {
        result=null;exclusiveAxes=new();
        if(!Enabled||!Own(sight)||sight.Vehicle?.Behaviour==null||!RuntimeAtgm.IsPlayerVehicle(sight.Vehicle.Behaviour.Pointer))return false;
        int matches=0;bool ordinary=false;var all=Cannons(sight).ToArray();
        foreach(var cannon in all)
        {
            if(!cannon.TryGetAssignedSight(out var assigned)||assigned?.Pointer!=sight.Pointer)continue;
            if(RuntimeAtgmLauncher.IsLauncher(cannon)){matches++;result=cannon;}else ordinary=true;
        }
        if(!AtgmIndependentSightPolicy.Allows(true,matches,ordinary,result!=null&&RuntimeAtgmLauncher.CanLaunch(result))||result==null||result.HealthFraction<=0)
        {
            if(warnings.Add("assignment:"+sight.Pointer))Plugin.ModLog.LogWarning($"[ATGM guidance sight] SKIP sight={sight.VUID.Value}: requires exactly one healthy compatible dedicated launcher; launcherMatches={matches} ordinaryShared={ordinary}.");
            return false;
        }
        var own=new AtgmAimingAxes(result.HorizontalAimingMechanism?.Pointer??IntPtr.Zero,result.VerticalAimingMechanism?.Pointer??IntPtr.Zero);
        var owner=result;
        var others=all.Where(c=>c.Pointer!=owner.Pointer).Select(c=>new AtgmAimingAxes(
            c.HorizontalAimingMechanism?.Pointer??IntPtr.Zero,c.VerticalAimingMechanism?.Pointer??IntPtr.Zero)).ToArray();
        if(AtgmGuidanceAxisOwnership.SharedElevation(own,others))
        {
            if(warnings.Add("shared-elevation:"+sight.Pointer))Plugin.ModLog.LogWarning($"[ATGM guidance sight] SKIP sight={sight.VUID.Value}: launcher shares elevation with another cannon; use a separate elevation mount or a fixed tube.");
            return false;
        }
        exclusiveAxes=AtgmGuidanceAxisOwnership.ExclusiveAxes(own,others);
        if(own.Horizontal!=IntPtr.Zero&&!exclusiveAxes.Contains(own.Horizontal)&&warnings.Add("shared-traverse:"+sight.Pointer))
            Plugin.ModLog.LogInfo($"[ATGM guidance sight] Shared turret traverse retained for sight={sight.VUID.Value}; native yaw commands remain available to other weapons.");
        return true;
    }
    internal static bool UsesIndependentSight(Cannon? cannon)
    {
        if(!Enabled||!RuntimeAtgmLauncher.IsLauncher(cannon)||cannon==null)return false;
        return cannon.TryGetAssignedSight(out var sight)&&Own(sight?.TryCast<GunnerSight>());
    }
    private static bool Valid(Session s)
    {
        if(!Enabled||!Own(s.Sight)||s.Sight.SightParent==null||s.Cannon.Vehicle?.Pointer!=s.Sight.Vehicle?.Pointer||
           s.Cannon.Vehicle?.Behaviour==null||!RuntimeAtgm.IsPlayerVehicle(s.Cannon.Vehicle.Behaviour.Pointer)||
           !RuntimeAtgmLauncher.CanLaunch(s.Cannon)||s.Cannon.HealthFraction<=0||
           !s.Cannon.TryGetAssignedSight(out var assigned)||assigned?.Pointer!=s.Sight.Pointer)return false;
        if(s.Revision!=revision)
        {
            if(!Resolve(s.Sight,out var cannon,out var axes)||cannon?.Pointer!=s.Cannon.Pointer)return false;
            ClearDriven(s);
            s.ExclusiveAxes=axes;
            s.SightAxes=RuntimeAtgmSightMount.Axes(s.Sight,s.Cannon);
            s.Revision=revision;
        }
        return true;
    }
    private static Session? Ensure(ScopeController scope,IGunnerSight? sight)
    {
        foreach(var pair in sessions.Where(p=>p.Key==scope.Pointer&&p.Value.Sight.Pointer!=sight?.Pointer).ToArray())End(pair.Key);
        if(sessions.TryGetValue(scope.Pointer,out var prior))
        {
            if(Valid(prior)){Redirect(prior);return prior;}
            End(scope.Pointer);
        }
        var optical=sight?.TryCast<GunnerSight>();
        if(optical==null||!Resolve(optical,out var cannon,out var axes)||cannon==null||optical.SightParent==null)return null;
        var session=new Session(scope,optical,cannon,revision,axes);sessions[scope.Pointer]=session;
        Redirect(session);
        Plugin.ModLog.LogInfo($"[ATGM guidance sight] BIND launcher={cannon.VUID.Value} sight={optical.VUID.Value}; native ammo/reload retained; optical aim independent from tube.");
        if(ReleaseDiagnostics.Enabled)try{Plugin.ModLog.LogInfo($"[ATGM guidance sight] AXES sight={optical.VUID.Value} driven={session.SightAxes.Count} exclusive={session.ExclusiveAxes.Count} {RuntimeAtgmSightMount.Describe(optical,cannon)}");}
        catch(Exception ex){Warn("Axis diagnostics",ex);}
        return session;
    }
    private static void Redirect(Session s)
    {
        var current=s.Sight.LinkedWeapon;var target=s.Sight.SightParent;
        // The native scope mask follows the linked weapon. A gimbal head turns
        // without its part, so link the head's own line of sight instead.
        if(s.SightAxes.Count==0&&TryHead(s.Cannon,out var head)&&head!=null)target=HeadAim(head);
        if(s.Reference.Redirecting(current?.Pointer??IntPtr.Zero,target.Pointer))s.Previous=current;
        s.Sight.LinkedWeapon=target;
    }
    // Detached and kept until Clear/Release, so a linked weapon never dangles.
    private static readonly Dictionary<IntPtr,Transform> aims=new();
    private static Transform HeadAim(Head head)
    {
        if(!aims.TryGetValue(head.Sight.Pointer,out var aim)||aim==null||aim.WasCollected)
            aims[head.Sight.Pointer]=aim=new GameObject("ATGM sight head aim"){hideFlags=HideFlags.HideAndDontSave}.transform;
        aim.SetPositionAndRotation(HeadOrigin(head),Quaternion.LookRotation(HeadDirection(head),head.Sight.SightParent.up));
        return aim;
    }
    private static void DropAim(IntPtr sight)
    {if(aims.Remove(sight,out var aim)&&aim!=null&&!aim.WasCollected)UnityEngine.Object.Destroy(aim.gameObject);}
    private static void End(IntPtr controller)
    {
        if(!sessions.Remove(controller,out var s))return;
        ClearDriven(s);
        try
        {
            if(s.Camera!=null)
            {
                s.Camera.minElevation=s.Elevation.RestoreMin(s.Camera.minElevation);
                s.Camera.maxElevation=s.Elevation.RestoreMax(s.Camera.maxElevation);
            }
        }
        catch(Exception ex){Warn("Restore camera limits",ex);}
        try{if(s.Sight!=null&&s.Reference.CanRestore(s.Sight.LinkedWeapon?.Pointer??IntPtr.Zero))s.Sight.LinkedWeapon=s.Previous!;}
        catch(Exception ex){Warn("Restore sight",ex);}
    }
    private static void ClearDriven(Session s)
    {
        foreach(var entry in s.Driven.Values)
            try{if(entry.Layer.ControlTarget?.Pointer==entry.Axis&&entry.Layer.mode==GunLayer.AimMode.Direction&&
                (entry.Layer.TargetDirection-entry.Direction).sqrMagnitude<.00000001f)entry.Layer.ClearTarget();}
            catch(Exception ex){Warn("Release sight motor",ex);}
        s.Driven.Clear();
    }
    internal static void Clear(){foreach(var key in sessions.Keys.ToArray())End(key);heads.Clear();foreach(var key in aims.Keys.ToArray())DropAim(key);nativeSights.Clear();warnings.Clear();revision++;}
    private static void Warn(string area,Exception ex){if(warnings.Add(area))Plugin.ModLog.LogWarning($"[ATGM guidance sight] {area}: {ex.Message}");}
    private static bool Finite(Vector3 v)=>float.IsFinite(v.x)&&float.IsFinite(v.y)&&float.IsFinite(v.z);
    private static bool TryHead(Cannon cannon,out Head? head)
    {
        head=null;
        if(!UsesIndependentSight(cannon)||cannon.WasCollected||cannon.HealthFraction<=0||
           !RuntimeAtgmLauncher.CanLaunch(cannon)||!RuntimeAtgm.IsPlayerVehicle(cannon.Vehicle?.Behaviour?.Pointer??IntPtr.Zero)||
           !cannon.TryGetAssignedSight(out var assigned)||assigned?.TryCast<GunnerSight>() is not {} sight||sight.WasCollected||
           sight.SightParent==null||sight.Vehicle?.Pointer!=cannon.Vehicle?.Pointer)return false;
        if(nativeSights.TryGetValue(sight.Pointer,out var nativeRevision)&&nativeRevision==revision)return false;
        if(heads.TryGetValue(sight.Pointer,out var prior)&&prior.Cannon.Pointer==cannon.Pointer&&prior.Revision==revision){head=prior;return true;}
        if(!Resolve(sight,out var owner,out _)||owner?.Pointer!=cannon.Pointer)
        {heads.Remove(sight.Pointer);return false;}
        if(RuntimeAtgmSightMount.Axes(sight,cannon).Count!=0)
        {nativeSights[sight.Pointer]=revision;heads.Remove(sight.Pointer);return false;}
        head=prior!=null&&prior.Cannon.Pointer==cannon.Pointer?prior:new Head(sight,cannon);
        head.Revision=revision;heads[sight.Pointer]=head;return true;
    }
    private static bool Gunner(Head head)
    {
        var op=head.Cannon.operable;
        var valid=AtgmSightHead.CanMove(op?.OperatorAssigned==true,op?.Valid==true);
        if(!valid&&warnings.Add("gunner:"+head.Cannon.Pointer))
            Plugin.ModLog.LogInfo($"[ATGM guidance sight] No gunner on launcher {head.Cannon.VUID.Value}: sight head locked");
        return valid;
    }
    // Gimbal centre in the part frame: above the pedestal, mid-length of the head
    // (housing spans z -0.35..0.026). The lens at +Z must swing around it.
    internal static readonly Vector3 HeadPivot=new(0,0,-.16f);
    private static Quaternion LocalRotation(Head head)=>Quaternion.Euler(-head.Angles.Pitch,head.Angles.Yaw,0);
    private static Vector3 HeadDirection(Head head)=>head.Sight.SightParent.rotation*LocalRotation(head)*Vector3.forward;
    private static Vector3 HeadOrigin(Head head)
    {
        var parent=head.Sight.SightParent;var pivot=Vector3.Scale(parent.lossyScale,HeadPivot);
        return parent.position+parent.rotation*(pivot+LocalRotation(head)*(Vector3.Scale(parent.lossyScale,head.Sight.ObjectiveLensLocalPosition)-pivot));
    }
    private static float nextHeadLog;private static int headLogs;
    internal static Quaternion VisualRotation(IntPtr part,Quaternion rest)
    {
        Head? found=null;
        if(Enabled)foreach(var head in heads.Values)if(head.Sight.VehicleObject?.Pointer==part){found=head;break;}
        if(headLogs<12&&Time.unscaledTime>=nextHeadLog&&(found!=null||heads.Count>0))
        {
            nextHeadLog=Time.unscaledTime+3;headLogs++;
            Plugin.ModLog.LogInfo(found!=null?$"[ATGM sight] HEAD part={part.ToInt64():X} yaw={found.Angles.Yaw:0.0} pitch={found.Angles.Pitch:0.0}"
                :$"[ATGM sight] HEAD part={part.ToInt64():X} not matched; heads={heads.Count} ({string.Join(",",heads.Values.Select(h=>h.Sight.VehicleObject?.Pointer.ToInt64().ToString("X")))})");
        }
        if(found==null)return rest;
        var frame=found.Sight.SightParent.rotation;return frame*LocalRotation(found)*Quaternion.Inverse(frame)*rest;
    }
    private static void Step(Head head,Vector3 direction,float dt)
    {
        var gunner=Gunner(head);
        if(head.Frame==Time.frameCount)return;
        head.Frame=Time.frameCount;
        if(!Finite(direction)||direction.sqrMagnitude<.5f)return;
        var local=Quaternion.Inverse(head.Sight.SightParent.rotation)*direction.normalized;
        var command=new AtgmHeadAngles(Mathf.Atan2(local.x,local.z)*Mathf.Rad2Deg,
            Mathf.Atan2(local.y,Mathf.Sqrt(local.x*local.x+local.z*local.z))*Mathf.Rad2Deg);
        head.Angles=AtgmSightHead.Slew(head.Angles,command,Setting(headYawRate,60),Setting(headPitchRate,40),dt,gunner,Limits);
        if(aims.ContainsKey(head.Sight.Pointer))HeadAim(head);
    }
    // Called by the existing player-ray capture, including when no scope is open.
    internal static void PlayerAim(IntPtr vehicle)
    {
        if(!Enabled)return;
        try
        {
            foreach(var pair in launchers.ToArray())
            {
                try
                {
                    var cannon=pair.Value;
                    if(cannon.WasCollected){launchers.Remove(pair.Key);continue;}
                    if(cannon.Vehicle?.Behaviour?.Pointer!=vehicle||!TryHead(cannon,out var head)||head==null)continue;
                    if(sessions.Values.Any(s=>s.Cannon.Pointer==cannon.Pointer&&s.Initialized&&s.Scope.Scoped&&s.Scope.HasActive&&s.Scope.ControlledScope?.Pointer==s.Sight.Pointer))continue;
                    if(RuntimeAtgm.TryPlayerAim(vehicle,out var direction))Step(head,direction,Time.deltaTime);
                }
                catch(Exception ex){launchers.Remove(pair.Key);Warn("Head launcher",ex);}
            }
            foreach(var pair in heads.ToArray())
                if(!TryHead(pair.Value.Cannon,out var current)||current!=pair.Value)heads.Remove(pair.Key);
        }
        catch(Exception ex){Warn("Head aim",ex);}
    }
    private static void HeadCamera(Head head,ScopeCameraController camera)
    {
        // Native camera angles are world yaw/elevation, even on a rolled part.
        var rotation=Quaternion.LookRotation(HeadDirection(head),Vector3.up);var euler=rotation.eulerAngles;
        camera.targetAngles=ScopeCameraController.EulerToCameraAngle(new(euler.x,euler.y));
        camera.targetRotation=rotation;
        // The native input step may integrate from its own current angles rather
        // than targetAngles; left at the activation pose, it pulls the head back.
        camera.EulerAngles=euler;
    }
    internal static bool TryAim(Cannon cannon,out Vector3 origin,out Vector3 direction,out float range)
    {
        origin=direction=default;range=0;
        if(!UsesIndependentSight(cannon))return false;
        try
        {
            if(TryHead(cannon,out var head)&&head!=null)
            {
                origin=HeadOrigin(head);direction=HeadDirection(head);range=300;
                foreach(var session in sessions.Values)if(session.Cannon.Pointer==cannon.Pointer&&session.Scope.Scoped){range=session.Scope.GunConverganceDistance;break;}
                return Finite(origin)&&Finite(direction)&&direction.sqrMagnitude>.5f;
            }
            foreach(var s in sessions.Values)
            {
                if(s.Cannon.Pointer!=cannon.Pointer||!Valid(s)||!s.Initialized||!s.Scope.Scoped||!s.Scope.HasActive||
                   s.Scope.ControlledScope?.Pointer!=s.Sight.Pointer)continue;
                var camera=s.Scope.controller;if(camera==null)continue;
                origin=s.Sight.SightParent.TransformPoint(s.Sight.ObjectiveLensLocalPosition);
                direction=camera.TargetRotation*Vector3.forward;range=s.Scope.GunConverganceDistance;
                return Finite(origin)&&Finite(direction)&&direction.sqrMagnitude>.5f;
            }
        }
        catch(Exception ex){Warn("Aim",ex);}
        return false;
    }
    [HarmonyPrefix,HarmonyPatch(typeof(ScopeController),nameof(ScopeController.ActivateSight))]
    private static void BeforeActivate(ScopeController __instance,IGunnerSight __0)
    {try{Ensure(__instance,__0);}catch(Exception ex){End(__instance.Pointer);Warn("Activate",ex);}}
    [HarmonyPostfix,HarmonyPatch(typeof(ScopeController),nameof(ScopeController.ActivateSight))]
    private static void AfterActivate(ScopeController __instance)
    {
        try
        {
            if(!sessions.TryGetValue(__instance.Pointer,out var s)||!Valid(s)||__instance.ControlledScope?.Pointer!=s.Sight.Pointer)return;
            var camera=__instance.controller;if(camera==null)return;
            var angles=s.Sight.SightParent.rotation.eulerAngles;if(!Finite(angles))return;
            s.Camera=camera;ApplyLimits(s);
            camera.EulerAngles=angles;s.Initialized=true;
            if(s.SightAxes.Count==0&&TryHead(s.Cannon,out var head)&&head!=null){HeadCamera(head,camera);SightAimPoint(s);}
        }
        catch(Exception ex){End(__instance.Pointer);Warn("Initial view",ex);}
    }
    [HarmonyPrefix,HarmonyPatch(typeof(ScopeController),nameof(ScopeController.Update))]
    private static void Update(ScopeController __instance)
    {
        try
        {
            // On exit/switch restore the native reference; head state survives unscoping.
            if(!__instance.Scoped&&!__instance.ScopeRequested){End(__instance.Pointer);return;}
            var s=Ensure(__instance,__instance.ControlledScope);
            if(s!=null&&!s.Initialized&&__instance.Scoped)AfterActivate(__instance);
        }
        catch(Exception ex){End(__instance.Pointer);Warn("Update",ex);}
    }
    [HarmonyPrefix,HarmonyPatch(typeof(ScopeController),nameof(ScopeController.SyncToSight))]
    private static bool Sync(ScopeController __instance,IGunnerSight __0)
    {
        try
        {
            // A Sync call must never create a redirect before native activation.
            if(!sessions.TryGetValue(__instance.Pointer,out var s)||!s.Initialized||!__instance.Scoped||
               __instance.ControlledScope?.Pointer!=s.Sight.Pointer||__0?.Pointer!=s.Sight.Pointer)return true;
            if(!Valid(s)){End(__instance.Pointer);return true;}
            Redirect(s);
            return false;
        }
        catch(Exception ex){End(__instance.Pointer);Warn("Sync",ex);return true;}
    }
    [HarmonyPostfix,HarmonyPatch(typeof(Sprocket.Gameplay.VehicleControl.VehicleController),nameof(Sprocket.Gameplay.VehicleControl.VehicleController.UpdateControl))]
    private static void PlayerChanged(Sprocket.Gameplay.VehicleControl.VehicleController __instance)
    {
        foreach(var pair in sessions.ToArray())
            try{if(pair.Value.Cannon.Vehicle?.Behaviour?.Pointer!=__instance.ControlledVehicle?.Pointer||!Valid(pair.Value))End(pair.Key);}
            catch(Exception ex){End(pair.Key);Warn("Player lifecycle",ex);}
        // Every vehicle controller (AI too) runs this; only the player's own
        // controller switching vehicles may drop head state.
        var controlled=__instance.ControlledVehicle?.Pointer??IntPtr.Zero;
        if(controlled!=IntPtr.Zero&&RuntimeAtgm.IsPlayerVehicle(controlled))
            foreach(var pair in heads.ToArray())
                if(pair.Value.Cannon.Vehicle?.Behaviour?.Pointer!=controlled)heads.Remove(pair.Key);
    }
    // The native scope mask is centred on the projected ScopeAimPoint, which the
    // gunner pipeline sets from the weapon (here the fixed tube). Keep it on the
    // guidance sight's own line of sight. (A hook on the overlay plugin's
    // ControlLateUpdate crashed natively; these two hooks are plain methods.)
    private static void SightAimPoint(Session s)
    {
        if(TryAim(s.Cannon,out var origin,out var direction,out var range))
            s.Scope.scopeAimPoint=origin+direction.normalized*(range>1?range:300);
    }
    [HarmonyPostfix,HarmonyPatch(typeof(ScopeController),nameof(ScopeController.SetAimPoint),new[]{typeof(Vector3),typeof(Vector3)})]
    private static void AimPoint(ScopeController __instance)
    {
        try{if(sessions.TryGetValue(__instance.Pointer,out var s))SightAimPoint(s);}
        catch(Exception ex){Warn("Scope aim point",ex);}
    }
    [HarmonyPrefix,HarmonyPatch(typeof(ScopeController),nameof(ScopeController.Dispose))]
    private static void Dispose(ScopeController __instance)=>End(__instance.Pointer);
    [HarmonyPostfix,HarmonyPatch(typeof(Cannon),nameof(Cannon.UpdateAssociatedSight))]
    private static void ChangedSight(Cannon __instance)=>ChangedBuild(__instance);
    [HarmonyPostfix,HarmonyPatch(typeof(Cannon),nameof(Cannon.Build))]
    private static void ChangedBuild(Cannon __instance)
    {revision++;if(RuntimeAtgmLauncher.IsLauncher(__instance))launchers[__instance.Pointer]=__instance;}
    [HarmonyPostfix,HarmonyPatch(typeof(Cannon),nameof(Cannon.OnVehicleBuilt))]
    private static void ChangedVehicle(Cannon __instance)=>ChangedBuild(__instance);
    [HarmonyPrefix,HarmonyPatch(typeof(Cannon),nameof(Cannon.Release))]
    private static void Released(Cannon __instance)
    {
        revision++;launchers.Remove(__instance.Pointer);
        var released=heads.Where(p=>p.Value.Cannon.Pointer==__instance.Pointer).Select(p=>p.Key).ToArray();
        foreach(var key in released)heads.Remove(key);
        foreach(var pair in sessions.Where(p=>p.Value.Cannon.Pointer==__instance.Pointer).ToArray())End(pair.Key);
        foreach(var key in released)DropAim(key);
    }
    private static bool Hold(GunLayer layer)
    {
        var target=layer.ControlTarget;if(target==null)return false;
        foreach(var s in sessions.Values)
        {
            if(!Valid(s)||!s.Initialized||!s.Scope.Scoped||s.Scope.ControlledScope?.Pointer!=s.Sight.Pointer)continue;
            if(s.SightAxes.Contains(target.Pointer))return false;
            if(s.Cannon.HorizontalAimingMechanism?.Pointer!=target.Pointer&&s.Cannon.VerticalAimingMechanism?.Pointer!=target.Pointer)continue;
            // Shared yaw belongs to the native controller. Hold only an axis proved
            // exclusive after the latest native build/assignment/lifecycle change.
            return s.ExclusiveAxes.Contains(target.Pointer);
        }
        return false;
    }
    [HarmonyPrefix,HarmonyPatch(typeof(GunLayer),nameof(GunLayer.AimAtPosition))]
    private static bool AimLayer(GunLayer __instance)
    {try{if(Hold(__instance)){__instance.ClearTarget();return false;}}catch(Exception ex){Warn("Layer",ex);}return true;}
    [HarmonyPrefix,HarmonyPatch(typeof(GunLayer),nameof(GunLayer.AimAtDirection))]
    private static bool DirectionLayer(GunLayer __instance)=>AimLayer(__instance);
    [HarmonyPrefix,HarmonyPatch(typeof(GunLayer),nameof(GunLayer.ExecuteUpdateEnd))]
    private static void LayerEnd(GunLayer __instance)
    {
        try
        {
            var axis=__instance.ControlTarget?.Pointer??IntPtr.Zero;
            bool trace=ReleaseDiagnostics.Enabled&&sessions.Count>0;
            if(trace)Count(layersSeen,axis);
            foreach(var s in sessions.Values)
            {
                if(!Valid(s)||!s.Initialized||!s.Scope.Scoped||s.Scope.ControlledScope?.Pointer!=s.Sight.Pointer||
                   !s.SightAxes.Contains(axis))continue;
                if(!TryAim(s.Cannon,out _,out var direction,out _)){if(trace)Count(aimFailed,axis);continue;}
                if(trace)Count(layersDriven,axis);
                // Only set the target of the existing native crew task. Its
                // ExecutePerform/UpdateEnd retain efficiency, motor physics,
                // health, limits and zero movement without assigned crew.
                __instance.AimAtDirection(direction,(Sprocket.VehicleControl.GunLayerAimFlags)0);
                s.Driven[__instance.Pointer]=(__instance,axis,direction);return;
            }
            if(Hold(__instance))__instance.ClearTarget();
        }
        catch(Exception ex){Warn("Sight motor",ex);}
    }
    private static void ApplyLimits(Session s)
    {
        var camera=s.Camera;if(camera==null)return;
        // Gimbal stops are in the part frame. Native world elevation must not
        // cut them short on tilted vehicles; the head math applies actual stops.
        float low=s.SightAxes.Count==0?-90:AtgmSightElevationState.Minimum;
        float high=s.SightAxes.Count==0?90:AtgmSightElevationState.Maximum;
        s.Elevation.BeforeWrite(camera.minElevation,camera.maxElevation,low,high);
        camera.minElevation=low;camera.maxElevation=high;
    }
    [HarmonyPostfix,HarmonyPatch(typeof(Sprocket.Vehicles.Turrets.TraverseMotor),nameof(Sprocket.Vehicles.Turrets.TraverseMotor.EnableBehaviour))]
    private static void MotorLifecycle()=>revision++;
    [HarmonyPostfix,HarmonyPatch(typeof(Sprocket.Vehicles.Turrets.TraverseMotor),nameof(Sprocket.Vehicles.Turrets.TraverseMotor.DisableBehaviour))]
    private static void MotorDisabled()=>revision++;
    [HarmonyPrefix,HarmonyPatch(typeof(ScopeCameraController),nameof(ScopeCameraController.ProcessInputs))]
    private static void CameraInput(ScopeCameraController __instance,CameraInputProccessingFlags __1)
    {
        try
        {
            foreach(var s in sessions.Values)
                if(s.Camera?.Pointer==__instance.Pointer&&s.Initialized&&s.Scope.Scoped&&
                   s.Scope.ControlledScope?.Pointer==s.Sight.Pointer&&Valid(s))
                {
                    ApplyLimits(s);
                    // Discard any pending camera lead before native input. The
                    // same-target postfix bounds the freshly consumed mouse delta.
                    if(s.SightAxes.Count==0&&TryHead(s.Cannon,out var head)&&head!=null)HeadCamera(head,__instance);
                    SightAimPoint(s);Diagnose(s,__instance,__1);return;
                }
        }
        catch(Exception ex){Warn("Camera elevation",ex);}
    }
    private static float nextHeadScopeLog;private static int headScopeLogs;
    [HarmonyPostfix,HarmonyPatch(typeof(ScopeCameraController),nameof(ScopeCameraController.ProcessInputs))]
    private static void CameraHead(ScopeCameraController __instance,float __0)
    {
        try
        {
            foreach(var s in sessions.Values)
            {
                if(s.Camera?.Pointer!=__instance.Pointer||!s.Initialized||!s.Scope.Scoped||!s.Scope.HasActive||
                   s.Scope.ControlledScope?.Pointer!=s.Sight.Pointer||!Valid(s)||s.SightAxes.Count!=0||!TryHead(s.Cannon,out var head)||head==null)continue;
                var euler=ScopeCameraController.CameraAngleToEuler(__instance.targetAngles);
                var before=head.Angles;var stepped=head.Frame!=Time.frameCount;
                Step(head,Quaternion.Euler(euler.x,euler.y,0)*Vector3.forward,Time.deltaTime);
                if(ReleaseDiagnostics.Enabled&&headScopeLogs<30&&Time.realtimeSinceStartup>=nextHeadScopeLog)
                {
                    nextHeadScopeLog=Time.realtimeSinceStartup+1;headScopeLogs++;
                    Plugin.ModLog.LogInfo($"[ATGM guidance sight] HEADSCOPE arg={__0:0.####} dt={Time.deltaTime:0.####} stepped={stepped} gunner={Gunner(head)} target={euler} head={before.Yaw:0.0}/{before.Pitch:0.0}->{head.Angles.Yaw:0.0}/{head.Angles.Pitch:0.0} linked={s.Sight.LinkedWeapon?.name} linkedForward={s.Sight.LinkedWeapon?.forward} cameraForward={__instance.TargetRotation*Vector3.forward}");
                }
                HeadCamera(head,__instance);SightAimPoint(s);return;
            }
        }
        catch(Exception ex){Warn("Head camera",ex);}
    }
    // Bounded scoped-aim trace (Diagnostics > Detailed logging): which crew layers run,
    // which ones this sight drives, and what the native camera does with pitch.
    private static readonly Dictionary<IntPtr,int> layersSeen=new(),layersDriven=new(),aimFailed=new();
    private static float nextDiagnostic;private static int diagnostics;
    private static void Count(Dictionary<IntPtr,int> counts,IntPtr axis)=>counts[axis]=counts.TryGetValue(axis,out var n)?n+1:1;
    private static string Counts(Dictionary<IntPtr,int> counts)=>counts.Count==0?"none":string.Join(",",counts.Select(p=>$"{p.Key.ToInt64():X}x{p.Value}"));
    private static void Diagnose(Session s,ScopeCameraController camera,CameraInputProccessingFlags flags)
    {
        if(!ReleaseDiagnostics.Enabled||diagnostics>=20||Time.realtimeSinceStartup<nextDiagnostic)return;
        nextDiagnostic=Time.realtimeSinceStartup+2;diagnostics++;
        var parent=s.Sight.SightParent;
        Plugin.ModLog.LogInfo($"[ATGM guidance sight] SCOPED sight={s.Sight.VUID.Value} flags={flags} euler={camera.EulerAngles} limits={camera.minElevation:0.#}/{camera.maxElevation:0.#} sens={camera.horizontalSensitivity:0.###}/{camera.verticalSensitivity:0.###} input={camera.movementInput} cameraForward={camera.TargetRotation*Vector3.forward} sightForward={parent?.forward} sightAxes={string.Join(",",s.SightAxes.Select(a=>a.ToInt64().ToString("X")))} layersSeen={Counts(layersSeen)} driven={Counts(layersDriven)} aimFailed={Counts(aimFailed)}");
        layersSeen.Clear();layersDriven.Clear();aimFailed.Clear();
    }
    [HarmonyPrefix,HarmonyPatch(typeof(GameIcons),nameof(GameIcons.GetOrDefault))]
    private static bool Icon(string __0,ref Sprite __result)
    {
        if(__0!=Guid&&__0!="atgmGuidanceSight")return true;
        try
        {
            __result=AtgmGuidanceSightVisuals.Icon();return false;
        }
        catch(Exception ex){Warn("Icon",ex);}
        return true;
    }
}
