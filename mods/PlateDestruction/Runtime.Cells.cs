using System.Diagnostics;
using System.Text;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using Sprocket.Vehicles;
using Sprocket.Vehicles.PlateStructures;
using UnityEngine;
using Object=UnityEngine.Object;
namespace PlateDestructionP0;

// 0.5.0: Ragged cell destruction with torn-edge curl, debris warp, batched merged-mesh cutting, and instant InPlace armour updates.
public sealed partial class Runtime
{
    private sealed class PartPack{internal Mesh Mesh=null!;internal Il2CppReferenceArray<Material>? Mats;internal Vector3 Pos,Scale;internal Quaternion Rot;internal bool Owned;internal int Layer;}
    private sealed class DebrisPack{internal Mesh? Mesh;internal Il2CppReferenceArray<Material>? Mats;internal Vector3 Pos,Scale;internal Quaternion Rot;internal int Layer;}
    private sealed class MergedCandidate{internal MeshRenderer R=null!;internal MeshFilter F=null!;internal Mesh M=null!;internal Bounds WorldBounds;internal Mat4 L2W=null!;}
    private readonly HashSet<(long,int)> cellFailed=new();
    private readonly Dictionary<(long Root,int Vuid,PlateSide Side,int Gx,int Gy,int Gz),double> holeZoneDmg=new();
    private readonly Dictionary<(long Root,int Vuid),bool> solidBlockCache=new();
    private int holeSeq;
    private bool eraProbed;

    private bool IsSolidBlockPlate(long root,PlateStructure plate,int vuid)
    {
        if(solidBlockCache.TryGetValue((root,vuid),out bool cached))return cached;
        bool isSolid=false;
        try
        {
            if(BuilderPatches.SolidBlockVuids.ContainsKey((root,vuid)))isSolid=true;
            else
            {
                string? mName=plate.Mesh?.ToMeshBlueprint(false)?.Name;
                if(BlastBreachMath.IsSolidBlockName(mName))isSolid=true;
            }
        }catch{}
        solidBlockCache[(root,vuid)]=isSolid;
        return isSolid;
    }

    // ---- 1.0.0/1.0.1: localized ragged blast holes with true local thickness ([Solid block] full depth), EB breach thresholds, and inward breach damage ----
    private void HandleHoleBlast(Tracked t,VehicleComponent comp,int vuid,PlateStructure plate,SidePlan? plan,PlateCells? pcs,BlastEvent e,double damageScale=1.0,HashSet<long>? breachedVehicles=null)
    {
        long root=t.Root;
        var p=Plugin.Instance;
        bool direct=IsDirectSnap(e,root,vuid);
        Vector3 distPos=direct?e.SnapHitPos:e.Pos;
        PlateSide side=PlateSide.Whole;
        double d;
        if(plan is {PerSide:true,Geo:not null})
        {
            side=SideOfPoint(comp,plan,distPos);
            var l=plate.WorldToStructureSpace(distPos);
            d=plan.Geo.Distance(new V3(l.x,l.y,l.z),side);
        }
        else
        {
            d=BoundsDistance(comp,t.V,distPos,out _);
        }
        if(led.IsGone(root,vuid)||led.IsBroken(root,vuid,side)||(side!=PlateSide.Whole&&State.IsSideBroken(root,vuid,side)))return;
        double cal=BlastBreachMath.EffectiveCalibreMm(e.Calibre,e.EbHasData,e.EbCalibreMm);
        double reach=BlastMath.Reach(p.BlastRadiusPer100mm.Value,cal);
        if(!direct&&d>reach)return;

        var set=EnsureDents(t,comp,vuid,plan);
        var tg=set?.Targets.FirstOrDefault(x=>!x.IsLow&&x.Filter!=null)??set?.Targets.FirstOrDefault(x=>x.Filter!=null);
        if(set==null||tg==null||tg.Mesh==null||tg.Filter==null)return;

        var sm=tg.Mesh;
        var tf=tg.Filter.transform;float sc=tg.Scale;
        var posL=tf.InverseTransformPoint(e.Pos);
        var blastLocal=new V3(posL.x,posL.y,posL.z);
        Func<int,bool>? ok=null;
        bool sideF=side!=PlateSide.Whole&&sm.FlatTriSide!=null;byte sbyte_=(byte)side;
        bool brokenF=tg.AppliedBroken.Count>0&&sm.FlatTriCell!=null;
        if(sideF||brokenF)ok=ti=>
        {
            if(sideF&&!(ti<sm.FlatTriSide!.Length&&sm.FlatTriSide[ti]==sbyte_))return false;
            if(brokenF&&ti<sm.FlatTriCell!.Length&&tg.AppliedBroken.Contains(sm.FlatTriCell[ti]))return false;
            return true;
        };

        V3 center,normal;
        double actualDistM;
        if(direct)
        {
            var dirW=e.SnapDir.normalized;
            var dirL=tf.InverseTransformDirection(dirW).normalized;
            var away=VM.Norm(new V3(dirL.x,dirL.y,dirL.z),new V3(0,-1,0));
            var o=tf.InverseTransformPoint(e.SnapHitPos-dirW*0.25f);
            if(tg.Accel.RayFirst(sm.FlatBasis,sm.FlatTris,sm.TopologyVersion,new V3(o.x,o.y,o.z),away,0.75/sc,ok,out double dist,out normal,out _))
                center=VM.Add(new V3(o.x,o.y,o.z),VM.Mul(away,dist));
            else if(!tg.Accel.Closest(sm.FlatBasis,sm.FlatTris,sm.TopologyVersion,blastLocal,ok,out center,out normal))
                return;
            normal=DentMath.Orient(normal,away);
            actualDistM=0.0;
        }
        else
        {
            if(!tg.Accel.ClosestBlast(sm.FlatBasis,sm.FlatTris,sm.TopologyVersion,sm.Normals,blastLocal,ok,out center,out var outN,out _,out double frontCos,0.005/sc))return;
            actualDistM=VM.Len(VM.Sub(center,blastLocal))*sc;
            if(actualDistM>reach)return;
            if(actualDistM>0.04&&frontCos<=0.0)return;
            if(actualDistM>0.06)
            {
                var centerW=tf.TransformPoint(new Vector3(center.X,center.Y,center.Z));
                if(IsBlastOccluded(t,vuid,e.Pos,centerW))return;
            }
            var pushW=BlastPushDir(e);
            var fbL=pushW.sqrMagnitude>1e-8f?tf.InverseTransformDirection(pushW.normalized).normalized:Vector3.zero;
            var away=DentMath.ResolveBlastAway(blastLocal,center,outN,new V3(fbL.x,fbL.y,fbL.z));
            normal=DentMath.Orient(outN,away);
        }

        bool isSolid=IsSolidBlockPlate(root,plate,vuid);
        double fallbackTh=QuickThicknessMm(root,comp,vuid);
        double localThMm=fallbackTh;
        double probeSpanMm=0;
        bool edgeOn=false;
        if(isSolid)
        {
            if(tg.Accel.RayFarthest(sm.FlatBasis,sm.FlatTris,sm.TopologyVersion,VM.Add(center,VM.Mul(normal,0.002/sc)),normal,1.5/sc,ok,out double fdist,out _))
            {
                double lm=fdist*sc+0.002;
                probeSpanMm=lm*1000.0;
                localThMm=probeSpanMm>=25.0?probeSpanMm:Math.Max(fallbackTh,120.0);
            }
            else localThMm=Math.Max(fallbackTh,120.0);
        }
        else
        {
            if(tg.Accel.RayFirst(sm.FlatBasis,sm.FlatTris,sm.TopologyVersion,VM.Add(center,VM.Mul(normal,0.002/sc)),normal,6.0/sc,ok,out double bdist,out _,out _))
            {
                double lm=bdist*sc+0.002;
                probeSpanMm=lm*1000.0;
                if(lm>=0.0025&&lm<=0.600)localThMm=probeSpanMm;
            }
            edgeOn=!direct&&DentMath.IsEdgeOnRim(probeSpanMm,fallbackTh);
        }

        double contactBreachMm=BlastBreachMath.EffectiveBreachMm(e.Kind,cal,e.EbHasData,e.EbBreachMm,p.BreachMmAt100.Value);
        double capMm=edgeOn?0.0:BlastBreachMath.BreachCapacityAtDistanceMm(contactBreachMm,cal,actualDistM);
        var (gx,gy,gz)=BlastBreachMath.ZoneKey(center);
        var zKey=(root,vuid,side,gx,gy,gz);
        holeZoneDmg.TryGetValue(zKey,out double prevCap);
        bool canAccum=!edgeOn&&BlastBreachMath.CanAccumulateBreach(localThMm,contactBreachMm)&&capMm>=localThMm*0.25;
        double totalCapMm=canAccum?prevCap+capMm:capMm;

        if(!p.BreakEnabled.Value||totalCapMm<localThMm)
        {
            if(canAccum)holeZoneDmg[zKey]=totalCapMm;
            if(p.LogDamage.Value&&(capMm>=1.0||direct))
                Plugin.Info($"[DMG] vuid={vuid} side={SideName(side)} cal={cal:0}mm d={actualDistM:0.00}m localTh={localThMm:0.0}mm{(isSolid?" [Solid block]":"")} cap={totalCapMm:0.0}/{localThMm:0.0}mm (dent only{(edgeOn?", edge-on":"")})");
            if((direct||actualDistM<=0.35)&&contactBreachMm>0)
                BallisticsEffectProbe.ReportPlate(e.EbSeq,vuid,localThMm,totalCapMm,false,0.0,isSolid,direct);
            double rawDmg=BlastMath.Damage(p.BlastBase.Value*damageScale,cal,actualDistM,p.BlastRadiusPer100mm.Value,p.MinCalibreMm.Value);
            if(rawDmg>0)
            {
                double depth=BlastBreachMath.RealisticBlastDentDepthMm(rawDmg,localThMm,p.BlastStrengthPerMm.Value,p.DefaultPlateStrength.Value,p.HullTurretBlastScale.Value,p.BlastMaxDepthMm.Value,out double q);
                if(edgeOn)depth*=0.15;
                if(depth>=0.8)
                {
                    double r=DentMath.BlastRadiusM(BlastMath.Radius(p.BlastRadiusPer100mm.Value,cal),p.BlastRadiusFactor.Value);
                    EnqueueDent(new DentReq{T=t,Comp=comp,Vuid=vuid,Side=side,Src=DentSrc.Blast,Style=DentStyle.Blast,CalibreMm=(int)Math.Round(cal),UseExplicitDir=direct,IsSolidBlock=isSolid,Pos=distPos,Dir=BlastPushDir(e),DepthMm=depth,RadiusM=r,Q=q,ThicknessMm=localThMm});
                }
            }
            return;
        }

        holeZoneDmg.Remove(zKey);
        double rHole=BlastBreachMath.BreachHoleRadiusM(totalCapMm,localThMm,cal,p.BlastHoleMaxRadiusM.Value);
        double ratio=totalCapMm/Math.Max(localThMm,1.0);
        if(p.LogDamage.Value)
            Plugin.Info($"[DMG] vuid={vuid} side={SideName(side)} cal={cal:0}mm d={actualDistM:0.00}m localTh={localThMm:0.0}mm{(isSolid?" [Solid block]":"")} cap={totalCapMm:0.0}/{localThMm:0.0}mm -> PUNCH HOLE R={rHole:0.00}m");
        BallisticsEffectProbe.ReportPlate(e.EbSeq,vuid,localThMm,totalCapMm,true,rHole,isSolid,direct);
        ExecuteHolePunch(t,comp,vuid,plate,plan,set,tg,side,e,center,normal,rHole,ratio,cal,totalCapMm,localThMm,breachedVehicles);
    }

    private void HideHoleDecals(VehicleComponent comp,PlateStructure plate,V3 centerStruct,double rHoleM)
    {
        try
        {
            var go=comp.gameObject;if(go==null)return;
            double cut=(rHoleM*1.15)*(rHoleM*1.15);
            foreach(var tr in go.GetComponentsInChildren<Transform>(true))
            {
                if(tr==null||tr.gameObject==go||!tr.name.Contains("ProjectileImpactMark")||!tr.gameObject.activeSelf)continue;
                var lp=plate.WorldToStructureSpace(tr.position);
                var dv=VM.Sub(new V3(lp.x,lp.y,lp.z),centerStruct);
                if(VM.Dot(dv,dv)<=cut){tr.gameObject.SetActive(false);hiddenDecals.Add(tr.gameObject);}
            }
        }catch{}
    }

    private void HideAttachedNearHole(Tracked t,VehicleComponent comp,int vuid,SidePlan? plan,PlateSide side,Vector3 hitW,double rHole,BlastEvent e,double ratio)
    {
        long root=t.Root;
        if(!attached.ContainsKey((root,vuid)))Safe("find attached lazy",()=>FindAttached(t,comp,vuid,plan));
        if(!attached.TryGetValue((root,vuid),out var list)||list.Count==0)return;
        float cutSq=(float)((rHole*1.15+0.12)*(rHole*1.15+0.12));
        bool any=false;
        foreach(var a in list)
        {
            if(a.Done||State.IsHidden(root,a.Vuid)||a.Comp==null||a.Comp.WasCollected)continue;
            if(side!=PlateSide.Whole&&a.Side!=side)continue;
            try{if((a.Comp.transform.position-hitW).sqrMagnitude<=cutSq){any=true;break;}}catch{}
        }
        if(!any)return;
        var attJob=new BreakJob{T=t,Comp=comp,Vuid=vuid,Side=side,Plan=plan,HasBlast=true,BlastPos=e.Pos,Ratio=ratio};
        HideAttached(attJob,root,a=>
        {
            if(side!=PlateSide.Whole&&a.Side!=side)return false;
            try{return a.Comp!=null&&!a.Comp.WasCollected&&(a.Comp.transform.position-hitW).sqrMagnitude<=cutSq;}catch{return false;}
        });
        foreach(int av in attJob.HiddenAttachedVuids)RemovePartArmourInPlace(t,root,av);
    }

    private void ExecuteHolePunch(Tracked t,VehicleComponent comp,int vuid,PlateStructure plate,SidePlan? plan,DentSet set,DentTarget tg,PlateSide side,BlastEvent e,V3 center,V3 normal,double rHole,double ratio,double cal,double breachCapMm,double localThMm,HashSet<long>? breachedVehicles)
    {
        var sw=Stopwatch.StartNew();
        var p=Plugin.Instance;
        long root=t.Root;
        var sm=tg.Mesh!;
        var tf=tg.Filter.transform;float sc=tg.Scale;
        bool direct=IsDirectSnap(e,root,vuid);
        var posL=tf.InverseTransformPoint(e.Pos);
        var blastLocal=new V3(posL.x,posL.y,posL.z);

        double rHoleLocal=rHole/sc;
        double curlWidthLocal=p.TearCurlWidthM.Value/sc;
        double curlDepthLocal=p.TearCurlDepthMm.Value/1000.0/sc;
        int holeId=HoleMath.HoleIdBase+(++holeSeq);
        uint seed=unchecked((uint)vuid*7919u+(uint)holeSeq*2654435761u);

        bool topoChanged=false;
        if(p.SubdivEnabled.Value)
        {
            double rSub=rHoleLocal+curlWidthLocal*0.75;
            double core=Math.Max(0.036/sc,p.SubdivCoreEdgeM.Value/sc);
            Func<int,bool>? isBrk=tg.AppliedBroken.Count>0?c=>tg.AppliedBroken.Contains(c):null;
            int added=LocalSubdiv.RefineGraded(sm,center,rSub,normal,DentMath.NormalReach(rSub),p.SubdivTargetEdgeM.Value/sc,core,p.SubdivMaxPasses.Value,p.SubdivMaxVertices.Value,side,isBrk);
            if(added>0){topoChanged=true;tg.Accel.Invalidate();}
        }

        int punched=HolePunch.Punch(sm,center,normal,rHoleLocal,side,holeId,tg.AppliedBroken,seed,out int remTris,out int totTris);
        if(punched==0)
        {
            double q=DentMath.BlastRatio(ratio,1.0);
            double dMm=DentMath.BlastDepthMm(q,p.BlastMaxDepthMm.Value);
            double r=DentMath.BlastRadiusM(BlastMath.Radius(p.BlastRadiusPer100mm.Value,cal),p.BlastRadiusFactor.Value);
            EnqueueDent(new DentReq{T=t,Comp=comp,Vuid=vuid,Side=side,Src=DentSrc.Blast,Style=DentStyle.Blast,CalibreMm=(int)Math.Round(cal),UseExplicitDir=direct,Pos=direct?e.SnapHitPos:e.Pos,Dir=BlastPushDir(e),DepthMm=dMm,RadiusM=r,Q=q,ThicknessMm=localThMm});
            return;
        }

        if(p.DebrisEnabled.Value)
        {
            var debMesh=MeshOps.ExtractCells(tg,c=>c==holeId,"PD_hole_debris_"+vuid,blastLocal,p.DebrisWarpMm.Value/1000.0/sc,seed,p.SmoothAngleDeg.Value);
            if(debMesh!=null)
            {
                var item=new DebrisItem{Vuid=vuid,Side=SideName(side)};
                item.Meshes.Add(debMesh);
                var holder=new GameObject("PD_hole_debris_"+vuid);
                holder.layer=tg.Filter.gameObject.layer;
                var ht=holder.transform;ht.position=tf.position;ht.rotation=tf.rotation;ht.localScale=tf.lossyScale;
                holder.AddComponent<MeshFilter>().sharedMesh=debMesh;
                var mr=holder.AddComponent<MeshRenderer>();
                try{if(tg.Rend!=null)mr.sharedMaterials=tg.Rend.sharedMaterials;}catch{}
                var nWorld=tf.TransformDirection(new Vector3(-normal.X,-normal.Y,-normal.Z)).normalized;
                var debJob=new BreakJob{T=t,Comp=comp,Vuid=vuid,Side=side,HasBlast=true,BlastPos=e.Pos+nWorld*0.4f,Ratio=ratio};
                FinishDebris(debJob,item,ht,debMesh,0,$"hole R={rHole:0.00}m tris={punched}");
            }
        }

        var hev=new HoleEvent{Center=center,Normal=normal,BlastLocal=blastLocal,RadiusM=rHoleLocal,CurlDepthM=curlDepthLocal,CurlWidthM=curlWidthLocal,Side=side,HoleId=holeId,Seed=seed};
        tg.AppliedBroken.Add(holeId);
        tg.Holes.Add(hev);
        tg.AppliedHoles=tg.Holes.Count;
        sm.Alive=new List<bool>(CellMask.AliveVertices(sm.FlatBasis.Length,sm.FlatTris,sm.FlatTriCell!,c=>tg.AppliedBroken.Contains(c)));
        var movedVerts=new List<int>(64);
        if(p.TearCurlDepthMm.Value>0)
        {
            TearCurl.Apply(sm,new HashSet<int>{holeId},c=>tg.AppliedBroken.Contains(c),blastLocal,curlDepthLocal,curlWidthLocal,localThMm/1000.0,sm.MaxDepthM,seed,movedVerts);
            if(movedVerts.Count>0)SmartNormals.Compute(sm,movedVerts,p.SmoothAngleDeg.Value,c=>tg.AppliedBroken.Contains(c));
        }
        MeshOps.Upload(tg,true);
        tg.Accel.Invalidate();

        foreach(var ltg in set.Targets)
        {
            if(ltg==tg)continue;
            ltg.AppliedBroken.Add(holeId);
            ltg.Holes.Add(hev);
            ltg.LowDirty=true;
        }
        SyncShadowProxy(set);

        var hitW=tf.TransformPoint(new Vector3(center.X,center.Y,center.Z));
        var hitStruct=plate.WorldToStructureSpace(hitW);
        var centerStruct=new V3(hitStruct.x,hitStruct.y,hitStruct.z);
        HideHoleDecals(comp,plate,centerStruct,rHole);
        HideAttachedNearHole(t,comp,vuid,plan,side,hitW,rHole,e,ratio);
        var inwardW=tf.TransformDirection(new Vector3(normal.X,normal.Y,normal.Z)).normalized;
        var nrmW=-inwardW;
        if(p.EnableEffects.Value)
            PlayExplosion(new Hit{T=t,Comp=comp,Vuid=vuid,Pos=hitW,Normal=nrmW,Dir=inwardW,CalibreMm=(int)Math.Round(cal)});
        ScheduleArmourHoleUpdate(t,root,vuid,plate,side,centerStruct,rHole);
        ApplyBreachInternalDamage(t,vuid,hitW,inwardW,rHole,cal,breachCapMm,localThMm,e,breachedVehicles);

        sw.Stop();
        Plugin.Info($"[HOLE] vuid={vuid} side={SideName(side)} R={rHole:0.00}m localTh={localThMm:0.0}mm punched={punched} tris remaining={remTris}/{totTris} curled={movedVerts.Count}{(topoChanged?" subdiv":"")} ms={sw.Elapsed.TotalMilliseconds:0.0}");

        // Only collapse small plates wholesale when >80% of triangles are gone; never vanish a large turret/hull.
        if(totTris>0&&totTris<180&&remTris<totTris*0.20)
        {
            var h=new Hit{T=t,Comp=comp,Vuid=vuid,Pos=hitW,Normal=nrmW,Dir=inwardW,CalibreMm=(int)Math.Round(cal)};
            if(side==PlateSide.Whole)led.ForceBreak(root,vuid,PlateSide.Whole);
            else led.ForceBreak(root,vuid,side);
            QueueBreak(t,comp,vuid,side,plan,1,h,e.Pos,ratio);
        }
    }

    // ---- adoption: cell plan of one destructible plate -----------------------------------------------------------
    private PlateCells? CellsFor(Tracked t,VehicleComponent c,int vuid,SidePlan? plan)
    {
        long root=t.Root;
        if(BuilderPatches.Cells.TryGetValue((root,vuid),out var ex))return ex;
        if(cellFailed.Contains((root,vuid)))return null;
        var p=Plugin.Instance;
        var plate=c.TryCast<PlateStructure>();if(plate==null)return null;
        try
        {
            Mesh? mesh=plan?.Pairs.FirstOrDefault(x=>x.Name=="high")?.WasMesh;
            if(mesh==null){var f=plate.highDetailRenderer?.Filter;mesh=f==null?null:f.sharedMesh;}
            if(mesh==null||!mesh.isReadable||mesh.vertexCount==0)
            {
                cellFailed.Add((root,vuid));
                Plugin.Info($"[CELLS] vuid={vuid}: the high detail mesh is missing or not readable; no cells, the plate breaks per side as in 0.4.0.");
                return null;
            }
            var v=MeshOps.Verts(mesh);var tris=MeshOps.Tris(mesh);
            uint seed=unchecked((uint)vuid*2654435761u)^0x9E3779B9u;
            var geo=plan is {PerSide:true,Geo:not null}?plan.Geo:null;
            var cp=CellPlan.Build(v,tris,geo,p.CellSizeM.Value,p.EdgeJitterM.Value,seed);
            var cellOf=cp.Assign(v,tris);
            var info=new Dictionary<int,CellInfo>();
            var thickSum=new Dictionary<int,double>();
            var areaSum=new Dictionary<int,double>();
            Il2CppStructArray<Vector3>? tv=null;
            try{tv=plate.Mesh?.ThicknessVectors;}catch{}
            bool canPerCell=p.PerCellThickness.Value&&tv!=null&&tv.Length>0&&(tv.Length==v.Length||v.Length==tv.Length*2);
            int tvLen=tv?.Length??0;
            for(int i=0;i<cellOf.Length;i++)
            {
                int cell=cellOf[i];if(cell<0)continue;
                if(!info.TryGetValue(cell,out var ci)){ci=new CellInfo{Min=new V3(float.MaxValue,float.MaxValue,float.MaxValue),Max=new V3(float.MinValue,float.MinValue,float.MinValue)};info[cell]=ci;}
                int ia=tris[i*3],ib=tris[i*3+1],ic=tris[i*3+2];
                V3 qa=v[ia],qb=v[ib],qc=v[ic];
                ci.Min=new V3(Math.Min(ci.Min.X,Math.Min(qa.X,Math.Min(qb.X,qc.X))),Math.Min(ci.Min.Y,Math.Min(qa.Y,Math.Min(qb.Y,qc.Y))),Math.Min(ci.Min.Z,Math.Min(qa.Z,Math.Min(qb.Z,qc.Z))));
                ci.Max=new V3(Math.Max(ci.Max.X,Math.Max(qa.X,Math.Max(qb.X,qc.X))),Math.Max(ci.Max.Y,Math.Max(qa.Y,Math.Max(qb.Y,qc.Y))),Math.Max(ci.Max.Z,Math.Max(qa.Z,Math.Max(qb.Z,qc.Z))));
                ci.Tris++;
                if(canPerCell)
                {
                    double tSum=0;int tN=0;
                    foreach(int vi in new[]{ia,ib,ic})
                    {
                        float m=tv![vi%tvLen].magnitude;
                        if(m>1e-6f&&!float.IsNaN(m)&&!float.IsInfinity(m)){tSum+=m;tN++;}
                    }
                    if(tN>0)
                    {
                        double triThMm=BlastMath.ThicknessMm(tSum/tN);
                        if(triThMm>0)
                        {
                            double area=0.5*VM.Len(VM.Cross(VM.Sub(qb,qa),VM.Sub(qc,qa)));
                            if(!(area>1e-10)||double.IsNaN(area))area=1e-6;
                            thickSum[cell]=thickSum.TryGetValue(cell,out double ts)?ts+triThMm*area:triThMm*area;
                            areaSum[cell]=areaSum.TryGetValue(cell,out double a2)?a2+area:area;
                        }
                    }
                }
            }
            if(info.Count==0){cellFailed.Add((root,vuid));Plugin.Info($"[CELLS] vuid={vuid}: no usable triangle; no cells.");return null;}
            var st=StatsFor(root,c,vuid);
            foreach(var kv in info)
            {
                double thMm=(canPerCell&&areaSum.TryGetValue(kv.Key,out double asum)&&asum>1e-12)?thickSum[kv.Key]/asum:st.ThicknessMm;
                kv.Value.ThicknessMm=thMm;
                kv.Value.Strength=BlastMath.Strength(p.BlastStrengthPerMm.Value,thMm,p.DefaultPlateStrength.Value);
            }
            var pc=new PlateCells(cp,info,st.Strength);
            BuilderPatches.Cells[(root,vuid)]=pc;
            foreach(var side in cp.Sides)
            {
                var f=cp.Frames[(int)side]!;
                var sideCis=info.Where(kv=>CellPlan.SideOf(kv.Key)==side).Select(kv=>kv.Value).ToList();
                var counts=sideCis.Select(ci=>ci.Tris).ToList();
                double thMin=sideCis.Min(ci=>ci.ThicknessMm),thMax=sideCis.Max(ci=>ci.ThicknessMm);
                double strMin=sideCis.Min(ci=>ci.Strength),strMax=sideCis.Max(ci=>ci.Strength);
                string thTxt=Math.Abs(thMax-thMin)>=0.2?$"{thMin:0.0}..{thMax:0.0}mm":$"{thMin:0.0}mm";
                string strTxt=Math.Abs(strMax-strMin)>=0.5?$"{strMin:0.0}..{strMax:0.0}":$"{strMin:0.0}";
                string axes="xyz";
                Plugin.Info($"[CELLS] vuid={vuid} {SideName(side)} cells={counts.Count} tris/cell {counts.Min()}..{counts.Max()} (grid {f.Nu}x{f.Nv} on axes {axes[f.AxisU]}{axes[f.AxisV]}, cell {f.SizeU:0.00}x{f.SizeV:0.00} m, jitter {p.EdgeJitterM.Value:0.###} m, th={thTxt}, strength per cell {strTxt})");
            }
            return pc;
        }
        catch(Exception e)
        {
            cellFailed.Add((root,vuid));Plugin.Warn($"[CELLS] vuid={vuid}: cell plan failed ({e.Message}); the plate breaks per side as in 0.4.0.");return null;
        }
    }

    // ---- blast on the cells of one plate -------------------------------------------------------------------------
    private sealed class CellPlate{internal Tracked T=null!;internal VehicleComponent Comp=null!;internal int Vuid;internal PlateCells PC=null!;internal SidePlan? Plan;internal PlateStructure Plate=null!;internal V3 Local;internal double Nearest;}

    private void HandleCellBlast(CellPlate x,BlastEvent e,double damageScale=1.0)
    {
        var p=Plugin.Instance;
        var br=x.PC.Blast(x.Local,e.Calibre,p.BlastBase.Value*damageScale,p.BlastRadiusPer100mm.Value,p.MinCalibreMm.Value,p.BreakEnabled.Value);
        if(br.Reached==0)return;
        if(p.LogDamage.Value)
        {
            string sTxt=Math.Abs(x.PC.MaxStrength-x.PC.MinStrength)>=0.5?$"{x.PC.MinStrength:0.0}..{x.PC.MaxStrength:0.0}":$"{x.PC.Strength:0.0}";
            Plugin.Info($"[DMG] vuid={x.Vuid} blast cal={e.Calibre}mm: {br.Reached} cell(s) in reach, max D_cell {br.MaxDmg:0.0} (strength per cell {sTxt}), {br.NewBroken.Count} cell(s) reach their strength");
        }
        foreach(var side in x.PC.Plan.Sides)
        {
            var newInSide=br.NewBroken.Where(c=>CellPlan.SideOf(c)==side).ToList();
            if(newInSide.Count>0)
            {
                double dmin=newInSide.Min(c=>BoxMath.Distance(x.Local,x.PC.Info[c].Min,x.PC.Info[c].Max));
                Plugin.Info($"[CELL] vuid={x.Vuid} side={SideName(side)} broken={x.PC.BrokenIn(side)}/{x.PC.CountIn(side)} (new {newInSide.Count}) by blast {e.Calibre}mm d={dmin:0.00}m");
            }
            if(br.DentQ.TryGetValue(side,out double q)&&q>0)QueueCellBlastDent(x,side,e,q);
            if(newInSide.Count>0)
            {
                double ratio=br.BrokenRatio.TryGetValue(side,out var rr)?rr:1;
                QueueCellBreak(x.T,x.Comp,x.Vuid,side,x.Plan,x.PC,newInSide,e.Pos,ratio,x.Plate,x.Local);
            }
        }
    }
    // One dent at the closest surface point of the unbroken cells of that side; q = max D_cell/strength over the unbroken cells in reach.
    private void QueueCellBlastDent(CellPlate x,PlateSide side,BlastEvent e,double q)
    {
        var p=Plugin.Instance;if(!p.DentEnabled.Value)return;
        double depth=DentMath.BlastDepthMm(q,p.BlastMaxDepthMm.Value);
        if(depth<0.5)return;
        double r=DentMath.BlastRadiusM(BlastMath.Radius(p.BlastRadiusPer100mm.Value,e.Calibre),p.BlastRadiusFactor.Value);
        double th=QuickThicknessMm(x.T.Root,x.Comp,x.Vuid);
        bool direct=IsDirectSnap(e,x.T.Root,x.Vuid);
        EnqueueDent(new DentReq{T=x.T,Comp=x.Comp,Vuid=x.Vuid,Side=side,Src=DentSrc.Blast,Style=DentStyle.Blast,CalibreMm=e.Calibre,UseExplicitDir=direct,Pos=direct?e.SnapHitPos:e.Pos,Dir=BlastPushDir(e),DepthMm=depth,RadiusM=r,Q=q,ThicknessMm=th});
    }

    // The new cells of one blast on one side. All cells of the side gone: the 0.2.0 / 0.3.0 side or whole-plate path takes over (hides the rest, drops the side from the armour).
    private void QueueCellBreak(Tracked t,VehicleComponent comp,int vuid,PlateSide side,SidePlan? plan,PlateCells pc,List<int> cells,Vector3 blast,double ratio,PlateStructure plate,V3 blastLocal)
    {
        long root=t.Root;
        float x0=float.MaxValue,y0=float.MaxValue,z0=float.MaxValue,x1=float.MinValue,y1=float.MinValue,z1=float.MinValue;
        foreach(var c in cells){var i=pc.Info[c];x0=Math.Min(x0,i.Min.X);y0=Math.Min(y0,i.Min.Y);z0=Math.Min(z0,i.Min.Z);x1=Math.Max(x1,i.Max.X);y1=Math.Max(y1,i.Max.Y);z1=Math.Max(z1,i.Max.Z);}
        var cl=BoxMath.Closest(blastLocal,new V3(x0,y0,z0),new V3(x1,y1,z1));
        var closest=plate.StructureToWorldSpace(new Vector3(cl.X,cl.Y,cl.Z));
        var normal=blast-closest;normal=normal.sqrMagnitude>1e-8f?normal.normalized:Vector3.up;
        var h=new Hit{T=t,Comp=comp,Vuid=vuid,Pos=closest,Normal=normal,Dir=-normal,CalibreMm=0};
        var job=new BreakJob{T=t,Comp=comp,Vuid=vuid,Hits=cells.Count,H=h,Side=side,Plan=plan,HasBlast=true,BlastPos=blast,BlastLocal=blastLocal,Ratio=ratio,Cells=new HashSet<int>(cells),PC=pc,CellOnly=true};
        if(pc.AllBroken(side))
        {
            job.CellOnly=false;
            if(side==PlateSide.Whole)led.ForceBreak(root,vuid,PlateSide.Whole);
            else
            {
                led.ForceBreak(root,vuid,side);
                var resolved=SideBreak.Resolve(plan is {PerSide:true},side,led.IsBroken(root,vuid,SideBreak.Other(side)));
                if(resolved==PlateSide.Whole)led.ForceBreak(root,vuid,PlateSide.Whole);
                job.Side=resolved;
            }
        }
        breakQueue.Add(job);
    }

    // ---- break of some cells -------------------------------------------------------------------------------------
    private void ExecuteCellBreak(BreakJob j)
    {
        var clock=Stopwatch.StartNew();long root=j.T.Root;
        PrepareDebris(j);
        var failed=BreakPipeline.Run(step=>
        {
            switch(step)
            {
                case BreakStep.HideCellDecals:HideCellDecals(j);break;
                case BreakStep.HideCellMesh:HideCellMesh(j,root);break;
                case BreakStep.HideAttached:HideAttached(j,root);break;
                case BreakStep.PlayExplosion:if(Plugin.Instance.EnableEffects.Value)PlayExplosion(j.H);break;
                case BreakStep.RaiseCells:RaiseCells(j,root);break;
                case BreakStep.Log:clock.Stop();Plugin.Info($"[BREAK] vuid={j.Vuid} side={SideName(j.Side)} {j.Cells!.Count} cell(s) torn off (broken {j.PC!.BrokenIn(j.Side)}/{j.PC.CountIn(j.Side)} on this side), ms={clock.Elapsed.TotalMilliseconds:0.0}");break;
            }
        },BreakPipeline.CellOrder);
        foreach(var f in failed)Plugin.Warn($"[BREAK] cell step {f.Step} failed: {f.Error}");
        Safe("debris",()=>SpawnDebris(j));
    }

    // The working copies (high and low) drop broken cell triangles, curl surviving borders, and update normals.
    private void HideCellMesh(BreakJob j,long root)
    {
        var set=EnsureDents(j.T,j.Comp,j.Vuid,j.Plan);
        if(set==null||set.Targets.Count==0)throw new InvalidOperationException("no readable renderer mesh: the visual of the cells could not be removed");
        var p=Plugin.Instance;
        var sb=new StringBuilder();
        uint seed=unchecked((uint)j.Vuid*7919u+(uint)j.Side*104729u+(uint)Time.frameCount*17u);
        foreach(var tg in set.Targets)
        {
            foreach(int c in j.Cells!)tg.AppliedBroken.Add(c);
            if(tg.IsLow&&p.LazyLowDetail.Value&&tg.Work==null&&(tg.Rend==null||!tg.Rend.enabled))
            {
                tg.LowDirty=true;
                sb.Append(" low=deferred");
                continue;
            }
            if(tg.Work==null||tg.Mesh==null)
                MaterializeTarget(tg,tg.Base,set.Plan,set.Cells,root,j.Vuid,set.Plate);

            var sm=tg.Mesh!;
            if(sm.SubTriCell==null)continue;
            int before=(int)(IndexCount(tg.Work)/3);

            Func<int,bool> isBrk=c=>tg.AppliedBroken.Contains(c);
            var movedVerts=new List<int>(64);
            if(!tg.IsLow&&p.SubdivEnabled.Value&&p.TearCurlDepthMm.Value>0)
            {
                int added=LocalSubdiv.RefineBoundary(sm,j.Cells!,isBrk,p.TearCurlWidthM.Value/tg.Scale,p.SubdivTargetEdgeM.Value/tg.Scale,2,p.SubdivMaxVertices.Value);
                if(added>0)tg.Accel.Invalidate();
            }
            sm.Alive=new List<bool>(CellMask.AliveVertices(sm.FlatBasis.Length,sm.FlatTris,sm.FlatTriCell!,isBrk));
            if(p.TearCurlDepthMm.Value>0)
            {
                TearCurl.Apply(sm,j.Cells!,isBrk,j.BlastLocal,p.TearCurlDepthMm.Value/1000.0/tg.Scale,p.TearCurlWidthM.Value/tg.Scale,tg.ThicknessMm/1000.0,sm.MaxDepthM,seed,movedVerts);
                if(movedVerts.Count>0)SmartNormals.Compute(sm,movedVerts,p.SmoothAngleDeg.Value,isBrk);
            }

            MeshOps.Upload(tg,true);

            int after=(int)(IndexCount(tg.Work)/3);
            sb.Append($" {tg.Name} tris {before}->{after}");
        }
        SyncShadowProxy(set);
        Plugin.Info($"[BREAK] plate vuid={j.Vuid} side={SideName(j.Side)} cells: mesh updated:{sb}");
    }
    private void RaiseCells(BreakJob j,long root)
    {
        var plate=j.Comp.TryCast<PlateStructure>();
        ScheduleArmourUpdate(j.T,root,j.Vuid,plate,j.Plan,j.PC,j.Side,j.PC!.Broken,j.HiddenAttachedVuids,wholePlate:false);
    }
    // Decals the game attached to the plate (ProjectileImpactMark(Clone)) that sit on one of the new cells.
    private void HideCellDecals(BreakJob j)
    {
        var go=j.Comp.gameObject;if(go==null||go==j.T.V.gameObject)return;
        var plate=j.Comp.TryCast<PlateStructure>();if(plate==null||j.PC==null)return;
        int n=0;
        foreach(var tr in go.GetComponentsInChildren<Transform>(true))
        {
            if(tr==null||tr.gameObject==go||!tr.name.Contains("ProjectileImpactMark")||!tr.gameObject.activeSelf)continue;
            var l=plate.WorldToStructureSpace(tr.position);
            int c=j.PC.Plan.CellOfPoint(new V3(l.x,l.y,l.z));
            if(c<0||!j.Cells!.Contains(c))continue;
            tr.gameObject.SetActive(false);hiddenDecals.Add(tr.gameObject);n++;
        }
        if(n>0||Plugin.Instance.DetailedLogging.Value)
            Plugin.Info($"[BREAK] vuid={j.Vuid} cells: {n} decal(s) hidden");
    }

    // ---- debris of the cells ----------------------------------------------------------------------------------------
    // Taken BEFORE HideCellMesh removes the broken cells: the dented, warped triangles of the cells that broke in this blast.
    private void PrepareDebris(BreakJob j)
    {
        var p=Plugin.Instance;if(!p.DebrisEnabled.Value||j.Cells==null)return;
        var plate=j.Comp.TryCast<PlateStructure>();if(plate==null)return;
        var set=EnsureDents(j.T,j.Comp,j.Vuid,j.Plan);
        var tg=set?.Targets.FirstOrDefault(x=>!x.IsLow&&x.Work!=null)??set?.Targets.FirstOrDefault(x=>x.Work!=null);
        if(tg==null||tg.Mesh==null){DebrisLine($"[DEBRIS] vuid={j.Vuid}: no readable plate mesh, no debris.");return;}
        var cells=j.Cells;
        uint seed=unchecked((uint)j.Vuid*7919u+(uint)j.Side*104729u+(uint)Time.frameCount*31u);
        var mesh=MeshOps.ExtractCells(tg,c=>cells.Contains(c),"PD_debris_"+j.Vuid,j.BlastLocal,p.DebrisWarpMm.Value/1000.0/tg.Scale,seed,p.SmoothAngleDeg.Value);
        if(mesh==null){DebrisLine($"[DEBRIS] vuid={j.Vuid}: the torn cells have no triangle on the '{tg.Name}' mesh, no debris.");return;}
        var vr=plate.highDetailRenderer??plate.lowDetailRenderer;
        Renderer? rend=null;try{rend=vr?.Renderer;}catch{}
        var tr=tg.Filter.transform;
        j.Pack=new DebrisPack{Mesh=mesh,Mats=rend==null?null:rend.sharedMaterials,Pos=tr.position,Rot=tr.rotation,Scale=tr.lossyScale,Layer=tg.Filter.gameObject.layer};
    }
    private void SpawnCellDebris(BreakJob j)
    {
        var p=Plugin.Instance;if(!p.DebrisEnabled.Value)return;
        var pk=j.Pack;
        if(pk?.Mesh==null){DebrisLine($"[DEBRIS] vuid={j.Vuid} side={SideName(j.Side)}: no mesh for the torn cells, no debris.");return;}
        var item=new DebrisItem{Vuid=j.Vuid,Side=SideName(j.Side)};
        item.Meshes.Add(pk.Mesh);
        var holder=new GameObject("PD_debris_"+j.Vuid);
        holder.layer=pk.Layer;
        var ht=holder.transform;ht.position=pk.Pos;ht.rotation=pk.Rot;ht.localScale=pk.Scale;
        holder.AddComponent<MeshFilter>().sharedMesh=pk.Mesh;
        var mr=holder.AddComponent<MeshRenderer>();if(pk.Mats!=null)mr.sharedMaterials=pk.Mats;
        int parts=AddParts(j,item,ht);
        FinishDebris(j,item,ht,pk.Mesh,parts,$"cells={j.Cells?.Count??0}");
    }
    private int AddParts(BreakJob j,DebrisItem item,Transform ht)
    {
        int parts=0,cap=Math.Max(0,Plugin.Instance.MaxAttachedParts.Value);
        foreach(var kv in j.Parts.Values)
        {
            if(parts>=cap)break;
            var r=kv.R;if(r==null)continue;
            var pmf=r.GetComponent<MeshFilter>();var pm=pmf==null?null:pmf.sharedMesh;if(pm==null)continue;
            var g=new GameObject("PD_part");g.layer=r.gameObject.layer;
            var gt=g.transform;var rt=r.transform;gt.position=rt.position;gt.rotation=rt.rotation;gt.localScale=rt.lossyScale;
            g.AddComponent<MeshFilter>().sharedMesh=pm;
            g.AddComponent<MeshRenderer>().sharedMaterials=r.sharedMaterials;
            gt.SetParent(ht,true);parts++;
        }
        foreach(var x in j.Extra)
        {
            if(parts>=cap){if(x.Owned)try{Object.Destroy(x.Mesh);}catch{}continue;}
            var g=new GameObject("PD_part_era");g.layer=x.Layer;
            var gt=g.transform;gt.position=x.Pos;gt.rotation=x.Rot;gt.localScale=x.Scale;
            g.AddComponent<MeshFilter>().sharedMesh=x.Mesh;
            var mr=g.AddComponent<MeshRenderer>();if(x.Mats!=null)mr.sharedMaterials=x.Mats;
            gt.SetParent(ht,true);parts++;
            if(x.Owned)item.Meshes.Add(x.Mesh);
        }
        return parts;
    }
    // Pivot at the centroid, launch away from the blast, register the object (shared tail of both debris routes).
    private void FinishDebris(BreakJob j,DebrisItem item,Transform ht,Mesh mesh,int parts,string what)
    {
        var p=Plugin.Instance;
        var centre=ht.TransformPoint(mesh.bounds.center);
        var pivot=new GameObject("PD_debris");pivot.transform.position=centre;pivot.transform.rotation=Quaternion.identity;
        ht.SetParent(pivot.transform,true);
        item.Pivot=pivot;
        var blast=j.HasBlast?j.BlastPos:centre-Vector3.up;
        var dir=Ballistics.Direction(new V3(blast.x,blast.y,blast.z),new V3(centre.x,centre.y,centre.z));
        double speed=Ballistics.Speed(p.SpeedMin.Value,p.SpeedMax.Value,p.SpeedPerRatio.Value,j.Ratio);
        var vel=VM.Mul(dir,speed);
        item.Body=new DebrisBody(new V3(centre.x,centre.y,centre.z),vel);
        flyingDebris++;
        var sp=Ballistics.Spin(unchecked(j.Vuid*7919+(int)j.Side*104729+Time.frameCount*31+(debrisSeq++)));
        item.Axis=new Vector3(sp.Axis.X,sp.Axis.Y,sp.Axis.Z);item.SpinDeg=sp.DegPerS;
        foreach(var old in debris.Add(item,Time.time))DestroyDebris(old,"cap");
        DebrisLine($"[DEBRIS] vuid={j.Vuid} side={SideName(j.Side)} parts={parts} v0={speed:0.0}m/s ({what}, vertices={mesh.vertexCount}, D/strength={j.Ratio:0.00}, spin {sp.DegPerS:0}deg/s, live={debris.Count})");
    }

    // ---- ERA: how are the attached parts drawn? ----------------------------------------------------------------------
    private sealed class MergedCopy{internal long Root;internal MeshFilter F=null!;internal Mesh Orig=null!,Work=null!;internal int[][] Subs=Array.Empty<int[]>();internal V3[]? Verts;}
    private readonly Dictionary<long,MergedCopy> merged=new();

    private static List<Renderer> ModelRenderers(VehicleComponent comp)
    {
        var res=new List<Renderer>();
        try
        {
            var vom=comp.TryCast<VehicleObjectModel>();var model=vom?.Model;if(model==null)return res;
            var group=model.TryCast<VehicleRendererGroup>();if(group==null)return res;
            void Add(VehicleRenderer? vr){var r=vr?.Renderer;if(r!=null&&!res.Any(x=>x.Pointer==r.Pointer))res.Add(r);}
            if(group.lods!=null)foreach(var l in group.lods)Add(l);
            Add(group.shadow);
        }
        catch{}
        return res;
    }
    private static bool PartBox(VehicleComponent comp,out Transform tr,out Bounds lb,out string how)
    {
        tr=comp.transform;lb=default;how="no model";
        try
        {
            var vom=comp.TryCast<VehicleObjectModel>();var model=vom?.Model;if(model==null)return false;
            Transform? mt=null;try{mt=model.transform;}catch{}
            if(mt==null){how="model has no transform";return false;}
            tr=mt;
            try{var b=model.LocalBounds;if(b.size.sqrMagnitude>1e-8f){lb=b;how="model LocalBounds";return true;}}catch{}
            try{var m=model.InteractionMesh;if(m!=null&&m.bounds.size.sqrMagnitude>1e-8f){lb=m.bounds;how="interaction mesh bounds";return true;}}catch{}
            try{var m=model.CollisionMesh;if(m!=null&&m.bounds.size.sqrMagnitude>1e-8f){lb=m.bounds;how="collision mesh bounds";return true;}}catch{}
            how="model bounds empty";return false;
        }
        catch(Exception e){how="error "+e.Message;return false;}
    }
    private static int[] SubTris(Mesh m,int s){var a=m.GetTriangles(s);var r=new int[a.Length];for(int i=0;i<r.Length;i++)r[i]=a[i];return r;}
    private MergedCopy? CopyOf(long root,int vuid,MeshFilter f,Mesh current)
    {
        long key=f.Pointer.ToInt64();
        if(merged.TryGetValue(key,out var mc)){if(mc.F!=null&&mc.Work!=null)return mc;merged.Remove(key);}
        var work=MeshOps.Clone(current,"PD_era_"+current.name);
        mc=new MergedCopy{Root=root,F=f,Orig=current,Work=work,Verts=SideOps.ToV3(work.vertices)};
        int subs=Math.Max(1,current.subMeshCount);mc.Subs=new int[subs][];
        for(int s=0;s<subs;s++)mc.Subs[s]=SubTris(current,s);
        f.sharedMesh=work;KeepFor(root,vuid,f,work,"era-merged");
        merged[key]=mc;return mc;
    }
    private static List<MergedCandidate> BuildMergedCandidates(BreakJob j,HashSet<IntPtr> plateOwn,HashSet<IntPtr> otherSide)
    {
        if(j.MergedCandidates!=null)return j.MergedCandidates;
        var list=new List<MergedCandidate>();
        foreach(var r in j.T.V.gameObject.GetComponentsInChildren<MeshRenderer>(false))
        {
            if(r==null||plateOwn.Contains(r.Pointer)||otherSide.Contains(r.Pointer))continue;
            if(r.gameObject.name.Contains("PlateStructure"))continue;
            var f=r.GetComponent<MeshFilter>();var mesh=f==null?null:f.sharedMesh;
            if(f==null||mesh==null||!mesh.isReadable||mesh.vertexCount==0)continue;
            if(mesh.name.StartsWith("PlateStructure")||(mesh.name.StartsWith("PD_")&&!mesh.name.StartsWith("PD_era_")))continue;
            var rb=r.bounds;rb.Expand(0.05f);
            list.Add(new MergedCandidate{R=r,F=f,M=mesh,WorldBounds=rb,L2W=SideOps.ToMat4(r.transform.localToWorldMatrix)});
        }
        j.MergedCandidates=list;
        return list;
    }
    // Path (b): batched across all parts in the same BreakJob; SetTriangles + RecalculateBounds run once per modified MergedCopy at the end of HideAttached.
    private int HideMerged(Att a,BreakJob j,long root,HashSet<IntPtr> plateOwn,HashSet<IntPtr> otherSide,out string note)
    {
        note="";
        if(!PartBox(a.Comp,out var tr,out var lb,out string how)){note="no part bounds ("+how+")";return 0;}
        var centreW=tr.TransformPoint(lb.center);
        var toWorldLess=SideOps.ToMat4(tr.worldToLocalMatrix);
        var bmin=new V3(lb.min.x,lb.min.y,lb.min.z);var bmax=new V3(lb.max.x,lb.max.y,lb.max.z);
        int total=0,seen=0;var sb=new StringBuilder();
        var candidates=BuildMergedCandidates(j,plateOwn,otherSide);
        foreach(var cand in candidates)
        {
            if(!cand.WorldBounds.Contains(centreW))continue;
            seen++;
            var r=cand.R;var f=cand.F;var mesh=cand.M;
            var l2w=cand.L2W;
            Func<V3,V3> toBox=pt=>toWorldLess.Point(l2w.Point(pt));
            var copy=CopyOf(root,a.Vuid,f,mesh);if(copy==null)continue;
            var verts=copy.Verts??=(SideOps.ToV3(copy.Work.vertices));
            int removed=0;var take=new List<int[]>(copy.Subs.Length);
            for(int s=0;s<copy.Subs.Length;s++)
            {
                var sub=copy.Subs[s];
                var inside=BoxCull.Inside(verts,sub,toBox,bmin,bmax,0.005f);
                if(inside.Count==0){take.Add(Array.Empty<int>());continue;}
                var drop=new HashSet<int>(inside);
                var kept=new List<int>(sub.Length);var gone=new List<int>(inside.Count*3);
                for(int i=0;i<sub.Length/3;i++){var dst=drop.Contains(i)?gone:kept;dst.Add(sub[i*3]);dst.Add(sub[i*3+1]);dst.Add(sub[i*3+2]);}
                removed+=gone.Count/3;
                copy.Subs[s]=kept.ToArray();take.Add(gone.ToArray());
            }
            if(removed>0)
            {
                j.DirtyMerged.Add(copy);
                var piece=MeshOps.Compact(copy.Work,take,"PD_era_piece");
                if(piece!=null)j.Extra.Add(new PartPack{Mesh=piece,Mats=r.sharedMaterials,Pos=r.transform.position,Rot=r.transform.rotation,Scale=r.transform.lossyScale,Owned=true,Layer=r.gameObject.layer});
                total+=removed;
            }
            if(Plugin.Instance.DetailedLogging.Value)
                sb.Append($" '{r.gameObject.name}'(mesh '{mesh.name}', {mesh.vertexCount} v, removed {removed} tris)");
        }
        note=$"bounds from {how}; {seen} merged renderer(s) around the part:{sb}";
        return total;
    }
    private static void FlushMerged(BreakJob j)
    {
        foreach(var copy in j.DirtyMerged)
        {
            try
            {
                if(copy.Work==null)continue;
                for(int s=0;s<copy.Subs.Length;s++)copy.Work.SetTriangles(copy.Subs[s],s,false);
                copy.Work.RecalculateBounds();
            }catch{}
        }
        j.DirtyMerged.Clear();
    }
    private void RestoreMerged(Func<MergedCopy,bool> pick)
    {
        foreach(var kv in merged.Where(k=>pick(k.Value)).ToArray())
        {
            var mc=kv.Value;
            try{if(mc.F!=null&&mc.Work!=null&&mc.F.sharedMesh!=null&&mc.F.sharedMesh.Pointer==mc.Work.Pointer)mc.F.sharedMesh=mc.Orig;}catch{}
            try{if(mc.Work!=null)Object.Destroy(mc.Work);}catch{}
            merged.Remove(kv.Key);
        }
    }

    // ---- [ERAPROBE]: once per session when DetailedLogging is on --------------------------------------------------------
    private static string PathOf(Transform? tr)
    {
        var parts=new List<string>();int n=0;
        while(tr!=null&&n++<10){parts.Insert(0,tr.name);tr=tr.parent;}
        return string.Join("/",parts);
    }
    private static string Types(GameObject go){try{return string.Join(",",go.GetComponents<Component>().Where(c=>c!=null).Select(c=>ClassName(c)));}catch{return "?";}}
    private static string P3(Vector3 v)=>$"({v.x:0.00},{v.y:0.00},{v.z:0.00})";
    private void EraProbe(Tracked t,Att a)
    {
        if(eraProbed||!Plugin.Instance.DetailedLogging.Value)return;
        eraProbed=true;
        var comp=a.Comp;
        try
        {
            var go=comp.gameObject;var pos=comp.transform.position;
            Plugin.Info($"[ERAPROBE] part vuid={a.Vuid} type={a.Type} side={SideName(a.Side)} cell={a.Cell} source={a.Source} GO='{go.name}' path={PathOf(go.transform)} active={go.activeInHierarchy} pos={P3(pos)} children={go.transform.childCount} components=[{Types(go)}]");
            for(int i=0;i<go.transform.childCount&&i<12;i++){var ch=go.transform.GetChild(i);Plugin.Info($"[ERAPROBE]   child '{ch.name}' active={ch.gameObject.activeSelf} components=[{Types(ch.gameObject)}] children={ch.childCount}");}
        }
        catch(Exception e){Plugin.Warn("[ERAPROBE] part: "+e.Message);}
        try{var vo=comp.vehicleObject;Plugin.Info($"[ERAPROBE] vehicleObject GO='{vo?.gameObject?.name}' path={PathOf(vo?.gameObject?.transform)}; VehicleTransform GO='{comp.VehicleTransform?.gameObject?.name}' path={PathOf(comp.VehicleTransform?.gameObject?.transform)}");}catch(Exception e){Plugin.Info("[ERAPROBE] vehicleObject: "+e.Message);}
        try
        {
            var vom=comp.TryCast<VehicleObjectModel>();
            if(vom==null){Plugin.Info("[ERAPROBE] part is not a VehicleObjectModel.");}
            else
            {
                var model=vom.Model;
                Plugin.Info($"[ERAPROBE] VehicleObjectModel HasModel={vom.HasModel} Model={ClassName(model)} staticRenderer={vom.staticRenderer} visible={vom.visible} rendererFlags={vom.rendererFlags} LODCount={vom.LODCount} modelScale={P3(vom.ModelScale)} defaultMesh='{vom.defaultMeshGuid}'");
                var group=model?.TryCast<VehicleRendererGroup>();
                if(group==null)Plugin.Info("[ERAPROBE] Model is not a VehicleRendererGroup.");
                else
                {
                    string lb="n/a";try{var b=group.LocalBounds;lb=$"center={P3(b.center)} size={P3(b.size)}";}catch{}
                    Plugin.Info($"[ERAPROBE] VehicleRendererGroup id={group.id.Value} GO='{group.gameObject.name}' path={PathOf(group.transform)} LODCount={group.LODCount} lods={(group.lods==null?-1:group.lods.Length)} shadow={(group.shadow==null?"null":"set")} LocalBounds {lb} flags={group.Flags} partType={group.PartType} markedForDestroy={group.markedForDestroy}");
                    void One(string tag,VehicleRenderer? vr)
                    {
                        if(vr==null){Plugin.Info($"[ERAPROBE]   {tag}: null");return;}
                        var r=vr.Renderer;string mesh="n/a";try{var f=vr.TryCast<VehicleMeshRenderer>()?.Filter;var m=f==null?null:f.sharedMesh;mesh=m==null?"none":$"'{m.name}' v={m.vertexCount}";}catch{}
                        Plugin.Info($"[ERAPROBE]   {tag}: {ClassName(vr)} Renderer={(r==null?"null (destroyed)":r.gameObject.name)} enabled={(r!=null&&r.enabled)} activeInHierarchy={(r!=null&&r.gameObject.activeInHierarchy)} mesh={mesh} markedForDestroy={VehicleRenderer.IsMarkedForDestroy(vr)}");
                    }
                    if(group.lods!=null)for(int i=0;i<group.lods.Length;i++)One("lod"+i,group.lods[i]);
                    One("shadow",group.shadow);
                }
            }
        }
        catch(Exception e){Plugin.Warn("[ERAPROBE] model: "+e.Message);}
        try
        {
            var pos=comp.transform.position;int all=0,hit=0;
            foreach(var r in t.V.gameObject.GetComponentsInChildren<Renderer>(true))
            {
                if(r==null)continue;all++;
                var rb=r.bounds;rb.Expand(0.05f);if(!rb.Contains(pos))continue;
                if(++hit>16)continue;
                var f=r.GetComponent<MeshFilter>();var m=f==null?null:f.sharedMesh;
                string mesh=m==null?"no mesh":$"'{m.name}' v={m.vertexCount} readable={m.isReadable} subMeshes={m.subMeshCount}";
                var g=r.GetComponentInParent<VehicleRendererGroup>();
                Plugin.Info($"[ERAPROBE] renderer around the part: {ClassName(r)} '{r.gameObject.name}' path={PathOf(r.transform)} enabled={r.enabled} active={r.gameObject.activeInHierarchy} shadow={r.shadowCastingMode} mesh={mesh} bounds center={P3(r.bounds.center)} size={P3(r.bounds.size)} group={(g==null?"none":g.id.Value.ToString())}");
            }
            Plugin.Info($"[ERAPROBE] renderers under the vehicle: {all}; containing the part position {P3(pos)}: {hit}{(hit>16?" (16 listed)":"")}");
        }
        catch(Exception e){Plugin.Warn("[ERAPROBE] renderers: "+e.Message);}
    }
}
