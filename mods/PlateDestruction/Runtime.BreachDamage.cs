using Sprocket;
using Sprocket.DamageModelling;
using Sprocket.Vehicles;
using Sprocket.Vehicles.CrewSystems;
using UnityEngine;
namespace PlateDestructionP0;

// 1.0.0: Coordinated behind-breach overpressure & fragmentation damage to exposed crew and internal modules
// via VehicleHealthRegister / HealthPool.HealthDelta (zero new Harmony patches; skips when EB already scored perforation).
public sealed partial class Runtime
{
    private void ApplyBreachInternalDamage(
        Tracked t,
        int plateVuid,
        Vector3 holeWorld,
        Vector3 inwardWorld,
        double holeRadiusM,
        double calibreMm,
        double breachCapMm,
        double localThMm,
        BlastEvent e,
        HashSet<long>? breachedVehicles)
    {
        var p=Plugin.Instance;
        if(!p.BreachCrewDamageEnabled.Value||p.BreachDamageScale.Value<=0)return;
        if(breachedVehicles!=null&&breachedVehicles.Contains(t.Root))
        {
            if(p.DetailedLogging.Value)
                Plugin.Info($"[BREACH] vuid={plateVuid}: skipped interior breach damage (vehicle already took breach damage from this blast)");
            return;
        }
        try
        {
            var applier=t.Model?.TryCast<CompoundStructure>()?.DamageApplier;
            var health=applier?.TryCast<VehicleHealthRegister>();
            if(health==null)return;
            long regPtr=health.Pointer.ToInt64();
            if(e.EbHasData&&e.EbPerforated&&(e.EbPerforatedRegister==0||e.EbPerforatedRegister==regPtr))
            {
                if(p.DetailedLogging.Value)
                    Plugin.Info($"[BREACH] vuid={plateVuid}: skipped interior breach damage (EB already applied full HE perforation damage on register 0x{regPtr:X})");
                return;
            }

            double excessMm=BreachDamageMath.ExcessMm(breachCapMm,localThMm);
            double coneR=BreachDamageMath.ConeRadiusM(calibreMm,breachCapMm,localThMm);
            if(excessMm<=1.0||coneR<=0.1)return;

            var inDir=inwardWorld.sqrMagnitude>1e-6f?inwardWorld.normalized:Vector3.down;
            float behindOffset=(float)Math.Clamp(localThMm/1000.0+0.06,0.06,0.28);
            Vector3 behindW=holeWorld+inDir*behindOffset;
            var holeV=new V3(holeWorld.x,holeWorld.y,holeWorld.z);
            var inV=new V3(inDir.x,inDir.y,inDir.z);

            var entries=health.entries;
            if(entries==null||entries.Count==0)return;
            var mods=new DamageModifiers(0.5f,0.5f,1.0f,0.25f);
            double scale=Math.Clamp(p.BreachDamageScale.Value,0.0,3.0);
            int hitCount=0;
            var reported=new List<(int EntityVuid,string Type,double Dist,double Frac,bool Alive)>();

            for(int i=0;i<entries.Count;i++)
            {
                var entry=entries[i];
                if(entry==null)continue;
                var owner=entry.Owner;
                var pool=entry.HealthPool?.TryCast<HealthPool>();
                if(owner==null||owner.WasCollected||pool==null||!pool.Alive)continue;

                string tn=ClassName(owner);
                if(tn is "TrackAssembly" or "WheelMount" or "WheelArray" or "Axle" or "AxleRod" or "Suspension" or "DriveSprocket" or "IdlerWheel")
                    continue;

                bool isCrew=owner.TryCast<CrewSeat>()!=null;
                Vector3 compW;
                try
                {
                    var pos0=owner.transform.position;
                    if(isCrew)
                    {
                        var posHead=pos0+owner.transform.up*0.45f;
                        compW=(posHead-holeWorld).sqrMagnitude<(pos0-holeWorld).sqrMagnitude?posHead:pos0;
                    }
                    else compW=pos0;
                }
                catch{continue;}

                double dist=(compW-holeWorld).magnitude;
                if(dist>=coneR)continue;
                if(!BreachDamageMath.IsBehindHole(holeV,inV,new V3(compW.x,compW.y,compW.z),Math.Max(0.35,holeRadiusM*1.1)))
                    continue;
                if(dist>0.18&&IsBlastOccluded(t,plateVuid,behindW,compW))continue;

                double frac=Math.Clamp(BreachDamageMath.ModuleDamageFraction(dist,coneR,calibreMm,excessMm,breachCapMm,isCrew)*scale,0.0,1.0);
                if(frac<0.03)continue;

                float cur=pool.Current;
                float poolFrac=pool.Fraction;
                float maxHp=poolFrac>1e-4f?cur/poolFrac:Math.Max(cur,1f);
                float delta=(float)(maxHp*frac);
                if(delta<=0.01f)continue;

                pool.HealthDelta(-delta,mods);
                hitCount++;
                bool stillAlive=pool.Alive;
                int ov=owner.VUID.Value;
                reported.Add((ov,tn,dist,frac,stillAlive));
                Plugin.Info($"[BREACH] vuid={plateVuid} hole R={holeRadiusM:0.00}m -> {tn} (vuid={ov}) d={dist:0.00}m dmg={frac:P0} (cal={calibreMm:0}mm excess={excessMm:0.0}mm, alive={stillAlive})");
            }
            if(hitCount>0)
            {
                breachedVehicles?.Add(t.Root);
                int crewTotal=0,crewAlive=0;
                for(int i=0;i<entries.Count;i++)
                {
                    var entry=entries[i];if(entry==null)continue;
                    if(entry.Owner?.TryCast<CrewSeat>()==null)continue;
                    var cp=entry.HealthPool?.TryCast<HealthPool>();if(cp==null)continue;
                    crewTotal++;if(cp.Alive)crewAlive++;
                }
                foreach(var rep in reported)
                    BallisticsEffectProbe.ReportBreachDamage(e.EbSeq,plateVuid,rep.EntityVuid,rep.Type,rep.Dist,rep.Frac,rep.Alive,crewAlive,crewTotal);
            }
            else if(p.DetailedLogging.Value)
                Plugin.Info($"[BREACH] vuid={plateVuid} hole R={holeRadiusM:0.00}m: no exposed interior modules within cone R={coneR:0.00}m (excess={excessMm:0.0}mm)");
        }
        catch(Exception ex){Plugin.Warn("[BREACH] interior damage failed: "+ex.Message);}
    }
}
