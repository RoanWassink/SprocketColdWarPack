using UnityEngine;
using Sprocket.Vehicles;
namespace SprocketExpandedBallistics;

// Locks belong to one exact battle-vehicle transform and are consumed at successful launch.
internal static class RuntimeTopAttackLock
{
    private sealed class Entry
    {
        internal bool Held,Complete;
        internal double Dwell;
        internal float Seen,Completed;
        internal int OwnerId,IdentityId,TargetId;
        internal Vector3 Point,Local;
        internal Transform? Target,Identity,Owner;
    }
    private static readonly Dictionary<IntPtr,Entry> Entries=new();
    private static readonly HashSet<string> Warnings=new();
    private static IntPtr activeVehicle;
    private static float activeAt;
    private static IntPtr noticeVehicle;
    private static float noticeUntil;
    private static int directWarnings;
    private static string noticeText="";
    internal static void Track(IntPtr vehicle,IVehicleBehaviour vehicleOwner,Ray ray)
    {
        if(vehicle==IntPtr.Zero||vehicleOwner==null)return;
        try
        {
            var owner=vehicleOwner.Rigidbody?.transform;
            if(owner==null){Entries.Remove(vehicle);return;}
            var now=Time.unscaledTime;activeVehicle=vehicle;activeAt=now;
            Prune(now);
            var held=TopAttackKeys.Held;
            if(!Entries.TryGetValue(vehicle,out var entry))
            {
                if(!held)return;
                if(Entries.Count>=16)return;
                Entries[vehicle]=entry=new(){Owner=owner,OwnerId=owner.GetInstanceID()};
            }
            if(entry.Owner==null||entry.Owner.Pointer!=owner.Pointer||entry.OwnerId!=owner.GetInstanceID()){Entries.Remove(vehicle);return;}
            entry.Seen=now;
            if(!held){entry.Held=false;if(!entry.Complete)entry.Dwell=0;return;}
            if(!entry.Held){entry.Held=true;entry.Complete=false;entry.Dwell=0;}
            if(entry.Complete)return;
            if(!TryAcquire(vehicleOwner!,ray,out var point,out var target,out var identity))
            {entry.Dwell=0;entry.Target=null;entry.Identity=null;return;}
            var local=identity.InverseTransformPoint(point);
            if(!Finite(local)){entry.Dwell=0;return;}
            if(entry.Dwell>0&&!TopAttackLockPolicy.SameTarget((IntPtr)entry.IdentityId,(IntPtr)identity.GetInstanceID(),Vector3.Distance(entry.Local,local)))entry.Dwell=0;
            entry.Target=target;entry.Identity=identity;entry.IdentityId=identity.GetInstanceID();entry.Point=point;entry.Local=local;
            entry.Dwell=AtgmGuidance.LockDwell(entry.Dwell,Time.deltaTime);
            if(entry.Dwell>=RuntimeAtgm.TopAttackLockSeconds)
            {
                entry.Complete=true;entry.Completed=now;
                // Acquisition tolerance uses the target root; tracking uses the exact hit transform.
                entry.Local=target.InverseTransformPoint(point);entry.TargetId=target.GetInstanceID();
            }
        }
        catch(Exception ex){Entries.Remove(vehicle);Warn("Target lock",ex);}
    }
    internal static bool TryConsume(IntPtr vehicle,IVehicleBehaviour vehicleOwner,out Vector3 point,out Transform? target)
    {
        point=default;target=null;
        try{return Consume(vehicle,vehicleOwner,out point,out target);}
        catch(Exception ex){Entries.Remove(vehicle);Warn("Lock consumption",ex);return false;}
    }
    private static bool Consume(IntPtr vehicle,IVehicleBehaviour vehicleOwner,out Vector3 point,out Transform? target)
    {
        point=default;target=null;
        var owner=vehicleOwner?.Rigidbody?.transform;
        Prune(Time.unscaledTime);
        if(vehicle==IntPtr.Zero||owner==null||!Entries.TryGetValue(vehicle,out var entry)||!entry.Complete||entry.Owner==null||entry.Owner.Pointer!=owner.Pointer||entry.OwnerId!=owner.GetInstanceID())return false;
        Entries.Remove(vehicle); // One acquisition is consumed once, including an invalid expired target.
        point=entry.Point;target=entry.Target;
        if(target!=null&&target.GetInstanceID()!=entry.TargetId)target=null;
        if(target!=null){try{point=target.TransformPoint(entry.Local);}catch{target=null;}}
        if(!Finite(point)||!TopAttackLockPolicy.ValidHit(Vector3.Distance(owner.position,point),point.x,point.y,point.z)){point=default;target=null;return false;}
        return true;
    }
    internal static bool TryAcquire(IVehicleBehaviour vehicleOwner,Ray ray,out Vector3 point,out Transform target)
    {
        point=default;target=null!;
        try{return TryAcquire(vehicleOwner,ray,out point,out target,out _);}
        catch(Exception ex){Warn("Target acquisition",ex);return false;}
    }
    private static bool TryAcquire(IVehicleBehaviour vehicleOwner,Ray ray,out Vector3 point,out Transform target,out Transform identity)
    {
        point=default;target=null!;identity=null!;
        var owner=vehicleOwner?.Rigidbody?.transform;
        if(owner==null||!Finite(ray.origin)||!Finite(ray.direction)||ray.direction.sqrMagnitude<.5f)return false;
        var axis=ray.direction.normalized;
        if(!Finite(axis)||axis.sqrMagnitude<.5f)return false;
        var hits=Physics.RaycastAll(ray.origin,axis,(float)TopAttackLockPolicy.RangeM,Physics.DefaultRaycastLayers,QueryTriggerInteraction.Ignore);
        if(hits==null||hits.Length>4096)return false;
        var compartments=vehicleOwner!.Compartments;
        if(compartments==null)return false;
        var count=compartments.Cast<Il2CppSystem.Collections.Generic.IReadOnlyCollection<VehicleCompartment>>().Count;
        if(count<0||count>256)return false;
        var ownRoots=new List<Transform>{owner};
        for(int i=0;i<count;i++)if(compartments[i]?.Root is {} root)ownRoots.Add(root);
        var best=float.PositiveInfinity;
        foreach(var hit in hits)
        {
            var collider=hit.collider;
            if(collider==null||!TopAttackLockPolicy.ValidHit(hit.distance,hit.point.x,hit.point.y,hit.point.z)||hit.distance>=best)continue;
            var transform=collider.transform;
            if(transform==null||transform.Pointer==owner.Pointer||transform.IsChildOf(owner))continue;
            if(ownRoots.Any(root=>transform.Pointer==root.Pointer||transform.IsChildOf(root)))continue;
            best=hit.distance;point=hit.point;target=transform;
            identity=collider.attachedRigidbody?.transform??transform.root;
        }
        return target!=null&&identity!=null&&Finite(point);
    }
    internal static void DirectApproach(IntPtr vehicle,bool tooClose)
    {
        if(RuntimeAtgm.IsPlayerVehicle(vehicle)){noticeVehicle=vehicle;noticeUntil=Time.unscaledTime+4;noticeText=tooClose?"Direct approach: target too close for loft":"Direct approach: target outside loft alignment";}
        try{if(directWarnings++<8)Plugin.ModLog.LogInfo("[ATGM] Direct approach: target too close or misaligned for the configured loft; real turn-rate and native collision still apply.");}catch{/* Optional feedback must not cancel a registered flight. */}
    }
    internal static string? Status()
    {
        var now=Time.unscaledTime;Prune(now);
        if(!RuntimeAtgm.Enabled||!RuntimeAtgm.IsPlayerVehicle(activeVehicle)||now-activeAt>.5f||Cursor.visible||RuntimeDamageFeed.IsEditor)return null;
        if(activeVehicle==noticeVehicle&&now<noticeUntil)return noticeText;
        if(!Entries.TryGetValue(activeVehicle,out var entry))return null;
        return entry.Complete?"TOP ATTACK  •  LOCK READY":entry.Held?entry.Dwell>0?$"TOP ATTACK  •  LOCKING {Math.Clamp(entry.Dwell/RuntimeAtgm.TopAttackLockSeconds,0,1):P0}":"TOP ATTACK  •  AIM AT TARGET":null;
    }
    private static void Prune(float now)
    {
        foreach(var pair in Entries.ToArray())
        {
            try{if(pair.Value.Owner==null||pair.Value.Owner.GetInstanceID()!=pair.Value.OwnerId||!TopAttackLockPolicy.Fresh(now,pair.Value.Complete?pair.Value.Completed:pair.Value.Seen))Entries.Remove(pair.Key);}
            catch{Entries.Remove(pair.Key);}
        }
    }
    internal static void Clear(){Entries.Clear();Warnings.Clear();activeVehicle=IntPtr.Zero;activeAt=0;noticeVehicle=IntPtr.Zero;noticeUntil=0;directWarnings=0;noticeText="";RuntimeTopAttackHud.Clear();}
    private static bool Finite(Vector3 v)=>float.IsFinite(v.x)&&float.IsFinite(v.y)&&float.IsFinite(v.z);
    private static void Warn(string area,Exception ex){if(Warnings.Add(area))Plugin.ModLog.LogWarning($"[ATGM] {area}: {ex.Message}");}
}
