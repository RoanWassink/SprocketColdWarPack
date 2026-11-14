using UnityEngine;
namespace SprocketExpandedBallistics;
// Launch-only physical aimpoint preparation. No armour/health lookup or new acquisition.
internal static class RuntimeTopAttackAim
{
    internal static void Prepare(ref Vector3 point,ref Transform? target)
    {
        try
        {
            var collider=target?.GetComponent<Collider>();var body=collider?.attachedRigidbody;
            if(body==null)return; // Static scenery keeps the actual player lock point.
            var root=body.transform;var colliders=root.GetComponentsInChildren<Collider>(false);
            if(colliders==null||colliders.Length==0||colliders.Length>256)return;
            Bounds bounds=default;var count=0;
            foreach(var c in colliders)
            {
                if(c==null||!c.enabled||c.isTrigger||c.attachedRigidbody?.Pointer!=body.Pointer)continue;
                var b=c.bounds;if(!Finite(b.center)||!Finite(b.size)||b.size.sqrMagnitude<.0001f||b.size.sqrMagnitude>1600)return;
                if(count++==0)bounds=b;else bounds.Encapsulate(b);
            }
            if(count==0||!Finite(bounds.size)||bounds.size.x>40||bounds.size.y>20||bounds.size.z>40)return;
            var start=new Vector3(bounds.center.x,bounds.max.y+.5f,bounds.center.z);
            var hits=Physics.RaycastAll(start,Vector3.down,bounds.size.y+1,Physics.DefaultRaycastLayers,QueryTriggerInteraction.Ignore);
            if(hits==null||hits.Length>4096)return;
            var nearest=float.PositiveInfinity;Vector3 roof=default;var found=false;
            foreach(var hit in hits)
            {
                var c=hit.collider;
                if(c==null||!c.enabled||c.isTrigger||c.attachedRigidbody?.Pointer!=body.Pointer||!Finite(hit.point)||!float.IsFinite(hit.distance)||hit.distance<0||hit.distance>=nearest)continue;
                // The native ray proves this exact surface exists, including nonconvex
                // meshes where Collider.ClosestPoint is unsupported. No guessed roof height.
                nearest=hit.distance;roof=hit.point;found=true;
            }
            if(found){point=roof;target=root;}
        }
        catch{/* Optional aim preparation never cancels a consumed valid lock. */}
    }
    private static bool Finite(Vector3 v)=>float.IsFinite(v.x)&&float.IsFinite(v.y)&&float.IsFinite(v.z);
}
