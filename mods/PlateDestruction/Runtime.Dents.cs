using System.Text;
using Sprocket.Vehicles;
using Sprocket.Vehicles.PlateStructures;
using UnityEngine;
using UnityEngine.InputSystem;
using Object=UnityEngine.Object;
namespace PlateDestructionP0;

// 0.5.0: Adaptive local subdivision, realistic kinetic/blast/tear deformation, spatial normals, lazy LODs, and warped debris.
public sealed partial class Runtime
{
    private enum DentSrc{Kin,Blast,Test}
    private sealed class DentReq
    {
        internal Tracked T=null!;internal VehicleComponent Comp=null!;internal int Vuid;internal PlateSide Side;internal DentSrc Src;
        internal DentStyle Style;internal int CalibreMm;internal bool IsNonDestructible,UseExplicitDir;internal double BlastDmg,DamageScale=1.0;
        internal Vector3 Pos,Dir;internal double DepthMm,RadiusM,Q,EnergyMJ,ThicknessMm;
    }
    private sealed class DebrisItem
    {
        internal GameObject Pivot=null!;internal DebrisBody Body=null!;internal readonly List<Mesh> Meshes=new();
        internal int Vuid;internal string Side="";internal Vector3 Axis;internal double SpinDeg;internal bool Landed;
    }
    private readonly List<DentReq> dentQueue=new();
    private readonly Dictionary<(long,int),DentSet> dents=new();
    private LogBudget dentBudget=new(150,50);
    private Hit? lastDestructible;
    private Key keyDent=Key.End;
    private DebrisBook<DebrisItem> debris=new(16,120);
    private readonly UnityGround ground=new();
    private int debrisSeq;
    private int dentSeq;
    private int flyingDebris;
    private float nextDebrisExpireAt;
    private const int MaxDentEvents=256;
    private void DLine(string s){if(dentBudget.TryLine())Plugin.Info(s);}

    private void StartDents()
    {
        var p=Plugin.Instance;
        if(!Enum.TryParse(p.KeyDent.Value,true,out keyDent)){Plugin.Warn("bad key "+p.KeyDent.Value+", using End");keyDent=Key.End;}
        dentBudget=new LogBudget(Math.Max(0,p.MaxLines.Value),Math.Max(1,p.SummaryEvery.Value));
        debris=new DebrisBook<DebrisItem>(Math.Max(1,p.MaxCount.Value),Math.Max(1f,p.LifetimeS.Value));
        flyingDebris=0;nextDebrisExpireAt=0;
    }

    private double QuickThicknessMm(long root,VehicleComponent comp,int vuid)
    {
        if(stats.TryGetValue((root,vuid),out var st)&&st.ThicknessMm>0)return st.ThicknessMm;
        try
        {
            var tv=comp.TryCast<PlateStructure>()?.Mesh?.ThicknessVectors;
            if(tv!=null&&tv.Length>0)
            {
                int stride=Math.Max(1,tv.Length/64);
                double sum=0;int n=0;
                for(int i=0;i<tv.Length;i+=stride)
                {
                    float m=tv[i].magnitude;
                    if(m>1e-6f&&!float.IsNaN(m)&&!float.IsInfinity(m)){sum+=m;n++;}
                }
                if(n>0){double th=BlastMath.ThicknessMm(sum/n);if(th>0)return th;}
            }
        }catch{}
        return 20.0;
    }

    // ---- dents -----------------------------------------------------------------------------------------------
    private void EnqueueDent(DentReq req)
    {
        for(int i=0;i<dentQueue.Count;i++)
        {
            var old=dentQueue[i];
            if(old.T.Pointer==req.T.Pointer&&old.Vuid==req.Vuid&&old.Side==req.Side&&old.Src==req.Src)
            {
                if(req.DepthMm>old.DepthMm)dentQueue[i]=req;
                return;
            }
        }
        if(dentQueue.Count>=DentQueuePolicy.MaxQueueSize)
        {
            int drop=dentQueue.FindIndex(d=>d.IsNonDestructible&&d.Src==DentSrc.Blast);
            dentQueue.RemoveAt(drop>=0?drop:0);
        }
        dentQueue.Add(req);
    }
    private void ProcessDents()
    {
        if(dentQueue.Count==0)return;
        var p=Plugin.Instance;
        int maxPerFrame=Math.Clamp(p.MaxDentsPerFrame.Value,1,16);
        double budgetMs=Math.Clamp(p.DentFrameBudgetMs.Value,0.5,50.0);
        var sw=System.Diagnostics.Stopwatch.StartNew();
        int processed=0;
        while(dentQueue.Count>0&&DentQueuePolicy.ShouldContinueFrame(processed,maxPerFrame,sw.Elapsed.TotalMilliseconds,budgetMs))
        {
            var r=dentQueue[0];
            dentQueue.RemoveAt(0);
            Safe("dent",()=>ApplyDent(r));
            processed++;
        }
    }
    private void QueueKineticDent(Tracked t,VehicleComponent comp,int vuid,PlateSide side,Hit h,double energyMJ,DentStyle style=DentStyle.Kinetic,bool isNonDestructible=false)
    {
        var p=Plugin.Instance;if(!p.DentEnabled.Value)return;
        double th=QuickThicknessMm(t.Root,comp,vuid);
        double vScale=isNonDestructible?p.HullTurretDentScale.Value:1.0;
        double depth=DentMath.KineticDepthMm(p.KineticDepthMm.Value,energyMJ,th,style,vScale);
        if(depth<0.5)return;
        double r=DentMath.KineticRadiusM(p.KineticRadiusM.Value,h.CalibreMm,energyMJ,style);
        EnqueueDent(new DentReq{T=t,Comp=comp,Vuid=vuid,Side=side,Src=DentSrc.Kin,Style=style,CalibreMm=h.CalibreMm,IsNonDestructible=isNonDestructible,Pos=h.Pos,Dir=h.Dir,DepthMm=depth,RadiusM=r,EnergyMJ=energyMJ,ThicknessMm=th});
    }
    private static Vector3 BlastPushDir(BlastEvent e)
    {
        if(e.HasSnap&&e.SnapDir.sqrMagnitude>1e-8f)return e.SnapDir.normalized;
        if(e.Velocity.sqrMagnitude>1e-4f)return e.Velocity.normalized;
        float nSq=e.Normal.sqrMagnitude;
        if(nSq>=0.25f&&nSq<=2.25f)return -e.Normal.normalized;
        return Vector3.down;
    }
    private static bool IsDirectSnap(BlastEvent e,long root,int vuid)
        =>e.HasSnap&&e.SnapRoot==root&&e.SnapVuid==vuid&&e.SnapDir.sqrMagnitude>1e-8f;

    // A blast that damages a destructible side but does not tear it off: q = this blast's damage / strength of the side.
    private void QueueBlastDent(BlastTarget x,BlastEvent e,double dmg,double strength)
    {
        var p=Plugin.Instance;if(!p.DentEnabled.Value)return;
        double q=DentMath.BlastRatio(dmg,strength);
        double depth=DentMath.BlastDepthMm(q,p.BlastMaxDepthMm.Value);
        if(depth<0.5)return;
        double r=DentMath.BlastRadiusM(BlastMath.Radius(p.BlastRadiusPer100mm.Value,e.Calibre),p.BlastRadiusFactor.Value);
        double th=QuickThicknessMm(x.T.Root,x.Comp,x.Vuid);
        bool direct=IsDirectSnap(e,x.T.Root,x.Vuid);
        EnqueueDent(new DentReq{T=x.T,Comp=x.Comp,Vuid=x.Vuid,Side=x.Side,Src=DentSrc.Blast,Style=DentStyle.Blast,CalibreMm=e.Calibre,UseExplicitDir=direct,Pos=direct?e.SnapHitPos:e.Pos,Dir=BlastPushDir(e),DepthMm=depth,RadiusM=r,Q=q,ThicknessMm=th});
    }
    private void QueueNonDestructibleBlastDent(Tracked t,VehicleComponent comp,int vuid,BlastEvent e,double dist,double thicknessMm,double damageScale=1.0)
    {
        var p=Plugin.Instance;if(!p.DentEnabled.Value)return;
        double dmg=BlastMath.Damage(p.BlastBase.Value*damageScale,e.Calibre,dist,p.BlastRadiusPer100mm.Value,p.MinCalibreMm.Value);
        if(dmg<=0)return;
        double effTh=Math.Min(thicknessMm,55.0);
        double strength=BlastMath.Strength(p.BlastStrengthPerMm.Value,effTh,p.DefaultPlateStrength.Value)/Math.Max(0.1,p.HullTurretBlastScale.Value);
        double q=DentMath.BlastRatio(dmg,strength);
        double depth=DentMath.BlastDepthMm(q,p.BlastMaxDepthMm.Value);
        if(depth<1.0)return;
        double r=DentMath.BlastRadiusM(BlastMath.Radius(p.BlastRadiusPer100mm.Value,e.Calibre),p.BlastRadiusFactor.Value);
        bool direct=IsDirectSnap(e,t.Root,vuid);
        EnqueueDent(new DentReq{T=t,Comp=comp,Vuid=vuid,Side=PlateSide.Whole,Src=DentSrc.Blast,Style=DentStyle.Blast,CalibreMm=e.Calibre,IsNonDestructible=true,UseExplicitDir=direct,BlastDmg=dmg,DamageScale=damageScale,Pos=direct?e.SnapHitPos:e.Pos,Dir=BlastPushDir(e),DepthMm=depth,RadiusM=r,Q=q,ThicknessMm=thicknessMm});
    }
    private void TestDent()
    {
        if(!inSession||inPhotomode)return;
        var h=lastDestructible;
        if(h==null||h.Comp==null||h.Comp.WasCollected||!tracked.ContainsKey(h.T.Pointer))
        {
            if(last!=null&&last.Comp!=null&&!last.Comp.WasCollected&&tracked.ContainsKey(last.T.Pointer)&&last.Comp.TryCast<PlateStructure>()!=null)h=last;
            else{Plugin.Info("[DENT] test key: no shot on a plate yet (fire one shot at a plate first).");return;}
        }
        var p=Plugin.Instance;
        bool nonDestr=!IsDestructible(h.Comp);
        var plan=nonDestr?null:PlanFor(h.T,h.Comp,h.Vuid);
        var side=plan==null?PlateSide.Whole:SideOfPoint(h.Comp,plan,h.Pos);
        double th=QuickThicknessMm(h.T.Root,h.Comp,h.Vuid);
        EnqueueDent(new DentReq{T=h.T,Comp=h.Comp,Vuid=h.Vuid,Side=side,Src=DentSrc.Test,Style=DentStyle.Blast,IsNonDestructible=nonDestr,Pos=h.Pos,Dir=h.Dir,DepthMm=DentMath.BlastDepthMm(0.5,p.BlastMaxDepthMm.Value),RadiusM=0.155,Q=0.5,ThicknessMm=th});
    }
    private static float MeanScale(Transform tf)
    {
        var s=tf.lossyScale;float m=(Mathf.Abs(s.x)+Mathf.Abs(s.y)+Mathf.Abs(s.z))/3f;return m<1e-3f?1f:m;
    }
    private void KeepFor(long root,int vuid,MeshFilter f,Mesh want,string side)
    {
        keepMeshes.RemoveAll(x=>x.K.F==null||x.K.F.Pointer==f.Pointer);
        keepMeshes.Add((root,new KeepMesh{F=f,Want=want,Vuid=vuid,Side=side}));
    }
    private static float MeshScale(DentTarget tg){try{return MeanScale(tg.Filter.transform);}catch{return 1f;}}

    private static void SubdivStretch(DentEvent ev,out V3 stretchDir,out double stretchHalfLen)
    {
        if(ev.Style==DentStyle.Ricochet)
        {
            var sdProj=VM.Sub(ev.ShotDir,VM.Mul(ev.Normal,VM.Dot(ev.ShotDir,ev.Normal)));
            double sinTheta=Math.Min(0.95,VM.Len(sdProj));
            stretchDir=sinTheta>1e-4?VM.Mul(sdProj,1.0/sinTheta):default;
            stretchHalfLen=ev.RadiusM*(1.10+0.65*sinTheta);
        }
        else{stretchDir=default;stretchHalfLen=0;}
    }

    private static void ApplyHoleEvent(DentTarget tg,HoleEvent h,bool doSubdiv,ref bool topoChanged,List<int>? movedVerts,out int remTris,out int totTris)
    {
        remTris=0;totTris=0;
        var p=Plugin.Instance;
        var sm=tg.Mesh!;
        Func<int,bool>? isBrk=tg.AppliedBroken.Count>0?c=>tg.AppliedBroken.Contains(c):null;
        if(doSubdiv&&p.SubdivEnabled.Value)
        {
            double rMax=h.RadiusM+h.CurlWidthM*0.75;
            int passes=Math.Min(p.SubdivMaxPasses.Value,tg.IsLow?3:p.SubdivMaxPasses.Value);
            double core=Math.Max(0.036/tg.Scale,p.SubdivCoreEdgeM.Value/tg.Scale);
            int added=LocalSubdiv.RefineGraded(sm,h.Center,rMax,h.Normal,DentMath.NormalReach(rMax),p.SubdivTargetEdgeM.Value/tg.Scale,core,passes,p.SubdivMaxVertices.Value,h.Side,isBrk);
            if(added>0){topoChanged=true;tg.Accel.Invalidate();}
        }
        int hp=HolePunch.Punch(sm,h.Center,h.Normal,h.RadiusM,h.Side,h.HoleId,tg.AppliedBroken,h.Seed,out remTris,out totTris);
        if(hp>0)topoChanged=true;
        tg.AppliedBroken.Add(h.HoleId);
        sm.Alive=new List<bool>(CellMask.AliveVertices(sm.FlatBasis.Length,sm.FlatTris,sm.FlatTriCell!,c=>tg.AppliedBroken.Contains(c)));
        if(h.CurlDepthM>0&&h.CurlWidthM>0)
        {
            var curlSet=new HashSet<int>{h.HoleId};
            int curled=TearCurl.Apply(sm,curlSet,c=>tg.AppliedBroken.Contains(c),h.BlastLocal,h.CurlDepthM,h.CurlWidthM,tg.ThicknessMm/1000.0,sm.MaxDepthM,h.Seed,movedVerts);
            if(curled>0)topoChanged=true;
        }
    }

    private void MaterializeTarget(DentTarget tg,Mesh baseMesh,SidePlan? plan,PlateCells? pc,long root,int vuid,PlateStructure? plate=null)
    {
        var p=Plugin.Instance;
        tg.Base=baseMesh;
        tg.Scale=MeshScale(tg);
        tg.PC=pc;
        double maxDepthLocal=p.MaxDepthMm.Value/1000.0/tg.Scale;
        tg.Mesh=MeshOps.FromUnityMesh(baseMesh,plan,pc,maxDepthLocal,plate);
        tg.Accel.Invalidate();
        tg.Work=MeshOps.Clone(baseMesh,"PD_dent_"+tg.Name);
        tg.LowDirty=false;
        tg.HasRimCaps=false;

        bool haveApplied=tg.AppliedBroken.Count>0&&tg.Mesh.SubTriCell!=null;
        if(haveApplied)
            tg.Mesh.Alive=new List<bool>(CellMask.AliveVertices(tg.Mesh.FlatBasis.Length,tg.Mesh.FlatTris,tg.Mesh.FlatTriCell!,c=>tg.AppliedBroken.Contains(c)));

        bool topologyChanged=false;
        bool needNormals=false;
        if(tg.Events.Count>0)
        {
            Func<int,bool>? isBrk=haveApplied?c=>tg.AppliedBroken.Contains(c):null;
            foreach(var ev in tg.Events)
            {
                if(!tg.IsLow&&p.SubdivEnabled.Value)
                {
                    SubdivStretch(ev,out var sDir,out double sLen);
                    int added=LocalSubdiv.RefineGraded(tg.Mesh,ev.Center,ev.RadiusM,ev.Normal,DentMath.NormalReach(ev.RadiusM),p.SubdivTargetEdgeM.Value/tg.Scale,p.SubdivCoreEdgeM.Value/tg.Scale,p.SubdivMaxPasses.Value,p.SubdivMaxVertices.Value,ev.Side,isBrk,sDir,sLen);
                    if(added>0)topologyChanged=true;
                }
                DentField.ApplyRich(ev,tg.Mesh);
            }
            needNormals=true;
        }
        if(tg.Holes.Count>0)
        {
            foreach(var h in tg.Holes)
            {
                ApplyHoleEvent(tg,h,true,ref topologyChanged,null,out _,out _);
                needNormals=true;
            }
            tg.AppliedHoles=tg.Holes.Count;
        }
        if(needNormals)
            SmartNormals.Compute(tg.Mesh,null,p.SmoothAngleDeg.Value,tg.AppliedBroken.Count>0?c=>tg.AppliedBroken.Contains(c):null);

        if(topologyChanged||haveApplied||tg.Holes.Count>0)MeshOps.Upload(tg,true);
        else if(tg.Events.Count>0)MeshOps.Upload(tg,false);

        tg.Filter.sharedMesh=tg.Work;
        KeepFor(root,vuid,tg.Filter,tg.Work,"dent");
        int o=0,m=0;if(tg.Mesh.FlatTriSide!=null)foreach(var s in tg.Mesh.FlatTriSide){if(s==(byte)PlateSide.Mirror)m++;else o++;}
        if(p.DetailedLogging.Value)
            Plugin.Info($"[DENTMESH] vuid={vuid} {tg.Name}: mesh '{baseMesh.name}' vertices={tg.Mesh.Basis.Count} triangles={tg.Mesh.FlatTris.Length/3}{(tg.Mesh.FlatTriSide!=null?$" (original {o}, mirror {m})":" (no side split)")} scale={tg.Scale:0.###} readable=True; private copy shown.");
    }

    private DentSet? EnsureDents(Tracked t,VehicleComponent comp,int vuid,SidePlan? plan)
    {
        long root=t.Root;
        if(dents.TryGetValue((root,vuid),out var existing))return existing;
        var plate=comp.TryCast<PlateStructure>();if(plate==null)return null;
        BuilderPatches.Cells.TryGetValue((root,vuid),out var pcells);
        double th=QuickThicknessMm(root,comp,vuid);
        var set=new DentSet{Root=root,Vuid=vuid,Plan=plan,Cells=pcells,Plate=plate};
        try{var sh=plate.shadowProxyRenderer;set.ShadowFilter=sh?.Filter;set.ShadowRend=sh?.Renderer;set.ShadowOrig=set.ShadowFilter==null?null:set.ShadowFilter.sharedMesh;}catch{}
        foreach(var (name,vr,isLow) in new[]{("high",plate.highDetailRenderer,false),("low",plate.lowDetailRenderer,true)})
        {
            MeshFilter? f=null;Renderer? rend=null;Mesh? cur=null;
            try{f=vr?.Filter;rend=vr?.Renderer;cur=f==null?null:f.sharedMesh;}catch{}
            if(f==null||cur==null){if(Plugin.Instance.DetailedLogging.Value)Plugin.Info($"[DENTMESH] vuid={vuid} {name}: no renderer or mesh, skipped.");continue;}
            if(!cur.isReadable){if(Plugin.Instance.DetailedLogging.Value)Plugin.Info($"[DENTMESH] vuid={vuid} {name}: mesh '{cur.name}' ({cur.vertexCount} vertices) is not readable, no dents on this renderer.");continue;}
            if(!isLow&&set.OrigTris==0)set.OrigTris=(int)(IndexCount(cur)/3);
            var pair=plan?.Pairs.FirstOrDefault(x=>x.Filter!=null&&x.Filter.Pointer==f.Pointer);
            var tg=new DentTarget{Name=name,Filter=f,Rend=rend,Original=pair?.WasMesh??cur,Base=cur,IsLow=isLow,ThicknessMm=th,PC=pcells};
            bool lazy=isLow&&Plugin.Instance.LazyLowDetail.Value&&(rend==null||!rend.enabled);
            if(!lazy)MaterializeTarget(tg,cur,plan,pcells,root,vuid,plate);
            set.Targets.Add(tg);
        }
        dents[(root,vuid)]=set;
        return set;
    }

    private void SyncShadowProxy(DentSet set)
    {
        if(set.ShadowFilter==null)return;
        var low=set.Targets.FirstOrDefault(x=>x.IsLow);
        var high=set.Targets.FirstOrDefault(x=>!x.IsLow);
        Mesh? show=low?.Work;
        if(show==null&&low!=null&&low.Base!=null)
        {
            try
            {
                if(high!=null&&high.AppliedBroken.Count>0)foreach(var c in high.AppliedBroken)low.AppliedBroken.Add(c);
                if(high!=null&&high.Holes.Count>low.Holes.Count)
                    for(int i=low.Holes.Count;i<high.Holes.Count;i++)low.Holes.Add(high.Holes[i]);
                MaterializeTarget(low,low.Base,set.Plan,set.Cells,set.Root,set.Vuid,set.Plate);
                show=low.Work;
            }catch{}
        }
        if(high!=null&&high.Holes.Count>0&&high.Work!=null&&(low==null||low.Holes.Count==0||show==null))show=high.Work;
        else show??=high?.Work;
        if(show!=null)
        {
            try{set.ShadowFilter.sharedMesh=show;KeepFor(set.Root,set.Vuid,set.ShadowFilter,show,"shadow");}catch{}
        }
    }
    private void SyncShadowProxy(long root,int vuid){if(dents.TryGetValue((root,vuid),out var set))SyncShadowProxy(set);}

    private void ApplyDent(DentReq r)
    {
        var p=Plugin.Instance;if(!p.DentEnabled.Value)return;
        if(r.Comp==null||r.Comp.WasCollected||!tracked.ContainsKey(r.T.Pointer))return;
        long root=r.T.Root;
        if(led.IsGone(root,r.Vuid)||led.IsBroken(root,r.Vuid,r.Side)||(r.Side!=PlateSide.Whole&&State.IsSideBroken(root,r.Vuid,r.Side)))return;
        Plans.TryGetValue((root,r.Vuid),out var plan);
        if(plan==null&&IsDestructible(r.Comp))plan=PlanFor(r.T,r.Comp,r.Vuid);
        var set=EnsureDents(r.T,r.Comp,r.Vuid,plan);
        if(set==null||set.Targets.Count==0)return;

        var counts=new StringBuilder();int total=0;string note="";
        DentEvent? sharedEvent=null;
        DentEvent? lastEv=null;double lastTh=r.ThicknessMm,logQ=r.Q,logDepth=r.DepthMm,logRadius=r.RadiusM;
        foreach(var tg in set.Targets)
        {
            if(tg.Filter==null)continue;
            if(tg.IsLow&&p.LazyLowDetail.Value&&tg.Work==null&&(tg.Rend==null||!tg.Rend.enabled))
            {
                if(sharedEvent is {} se)
                {
                    if(tg.Events.Count<MaxDentEvents)tg.Events.Add(se);
                    tg.LowDirty=true;
                    if(counts.Length>0)counts.Append(' ');
                    counts.Append("low=deferred");
                    continue;
                }
                else if(counts.Length>0)continue;
            }
            if(tg.Work==null||tg.Mesh==null)
                MaterializeTarget(tg,tg.Base,plan,set.Cells,root,r.Vuid,set.Plate);

            int n=DentOne(tg,r,out var ev,out string how,out bool topoChanged,out double thMm,out double effQ);
            if(sharedEvent==null&&n>0)sharedEvent=ev;
            if(counts.Length>0)counts.Append(' ');
            counts.Append($"{tg.Name}={n}/{tg.Mesh!.Basis.Count}");total+=n;
            if(note.Length==0&&how.Length>0)note=how;
            if(n>0)
            {
                lastEv=ev;lastTh=thMm;logQ=effQ;logDepth=ev.DepthM*tg.Scale*1000.0;logRadius=ev.RadiusM*tg.Scale;
                if(tg.Events.Count>=MaxDentEvents){if(!set.CapWarned){set.CapWarned=true;Plugin.Warn($"[DENT] vuid={r.Vuid}: {MaxDentEvents} dents recorded on '{tg.Name}'; further dents are applied but not replayed after a tear-off.");}}
                else tg.Events.Add(ev);
                MeshOps.Upload(tg,topoChanged);
            }
        }
        string src=r.Src==DentSrc.Kin?"kin":r.Src==DentSrc.Blast?"blast":"test";
        string stTag=lastEv is {} le?$" style={le.Style} th={lastTh:0.0}mm":"";
        DLine($"[DENT] vuid={r.Vuid} side={SideName(r.Side)} src={src}{stTag}{(r.Src==DentSrc.Blast?$" q={logQ:0.00}":"")} depth={logDepth:0.0}mm R={logRadius:0.000}m verts={total} ({counts}){(note.Length>0?" "+note:"")}");
        if(total>0)
        {
            SnapMarksFor(root,r.Comp,r.Vuid);
            QueueMarkSweep(root,r.Comp,r.Vuid);
        }
    }

    private bool IsBlastOccluded(Tracked t,int targetVuid,Vector3 blastW,Vector3 targetW)
    {
        Vector3 segW=targetW-blastW;
        if(segW.magnitude<0.04f)return false;
        var segBounds=new Bounds((blastW+targetW)*0.5f,new Vector3(Mathf.Abs(segW.x)+0.04f,Mathf.Abs(segW.y)+0.04f,Mathf.Abs(segW.z)+0.04f));
        long root=t.Root;
        foreach(var pe in t.AllPlates)
        {
            if(pe.Vuid==targetVuid||pe.Plate==null||pe.Plate.WasCollected)continue;
            if(led.IsGone(root,pe.Vuid)||State.IsHidden(root,pe.Vuid)||led.IsBroken(root,pe.Vuid,PlateSide.Whole))continue;
            bool hitsBounds=false;
            for(int i=0;i<pe.Rends.Length;i++)
            {
                var r=pe.Rends[i];
                if(r==null)continue;
                try{if(r.bounds.Intersects(segBounds)){hitsBounds=true;break;}}catch{}
            }
            if(!hitsBounds)continue;

            Transform? otf=null;V3[]? ov=null;int[]? ot=null;int over=0;TriAccel? oacc=null;Func<int,bool>? ook=null;
            if(dents.TryGetValue((root,pe.Vuid),out var oset))
            {
                var otg=oset.Targets.FirstOrDefault(x=>!x.IsLow&&x.Mesh!=null&&x.Filter!=null)??oset.Targets.FirstOrDefault(x=>x.Mesh!=null&&x.Filter!=null);
                if(otg!=null&&otg.Mesh!=null&&otg.Filter!=null)
                {
                    otf=otg.Filter.transform;ov=otg.Mesh.FlatBasis;ot=otg.Mesh.FlatTris;over=otg.Mesh.TopologyVersion;oacc=otg.Accel;
                    var otcl=otg.Mesh.FlatTriCell;var ots=otg.Mesh.FlatTriSide;
                    bool brkCell=otg.AppliedBroken.Count>0&&otcl!=null;
                    bool brkSide=State.AnySideBroken(root,pe.Vuid)&&ots!=null;
                    if(brkCell||brkSide)
                    {
                        int ovuid=pe.Vuid;var ab=otg.AppliedBroken;
                        ook=ti=>
                        {
                            if(brkCell&&ti<otcl!.Length&&ab.Contains(otcl[ti]))return false;
                            if(brkSide&&ti<ots!.Length&&State.IsSideBroken(root,ovuid,(PlateSide)ots[ti]))return false;
                            return true;
                        };
                    }
                }
            }
            if(oacc==null)
            {
                MeshFilter? of=null;Mesh? om=null;
                try
                {
                    of=pe.Plate.lowDetailRenderer?.Filter;
                    om=of==null?null:of.sharedMesh;
                    if(om==null||!om.isReadable||om.vertexCount==0)
                    {
                        of=pe.Plate.highDetailRenderer?.Filter;
                        om=of==null?null:of.sharedMesh;
                    }
                }catch{continue;}
                if(of==null||om==null||!om.isReadable||om.vertexCount==0)continue;
                IntPtr omPtr=om.Pointer;
                if(pe.OccAccel==null||pe.OccMeshPtr!=omPtr||pe.OccVerts==null||pe.OccTris==null)
                {
                    pe.OccFilter=of;pe.OccMeshPtr=omPtr;
                    pe.OccVerts=MeshOps.Verts(om);pe.OccTris=MeshOps.Tris(om);
                    pe.OccAccel=new TriAccel();
                }
                otf=of.transform;ov=pe.OccVerts;ot=pe.OccTris;over=0;oacc=pe.OccAccel;
            }
            if(otf==null||ov==null||ot==null||oacc==null)continue;
            try
            {
                float oSc=MeanScale(otf);
                var sL=otf.InverseTransformPoint(blastW);
                var eL=otf.InverseTransformPoint(targetW);
                if(BlastOcclusion.SegmentBlocked(oacc,ov,ot,over,new V3(sL.x,sL.y,sL.z),new V3(eL.x,eL.y,eL.z),0.015/oSc,0.025/oSc,ook))
                    return true;
            }catch{}
        }
        return false;
    }

    private int DentOne(DentTarget tg,DentReq r,out DentEvent ev,out string how,out bool topologyChanged,out double localThMm,out double effQ)
    {
        how="";ev=default;topologyChanged=false;localThMm=r.ThicknessMm;effQ=r.Q;
        var sm=tg.Mesh!;
        var p=Plugin.Instance;
        var tf=tg.Filter.transform;float sc=tg.Scale;
        var ts=sm.FlatTriSide;
        var tcl=sm.FlatTriCell;
        bool sideF=r.Side!=PlateSide.Whole&&ts!=null;byte sbyte_=(byte)r.Side;
        bool cellF=tg.AppliedBroken.Count>0&&tcl!=null;
        Func<int,bool>? ok=null;
        if(sideF||cellF)ok=i=>
        {
            if(sideF&&!(i<ts!.Length&&ts[i]==sbyte_))return false;
            if(cellF&&i<tcl!.Length&&tg.AppliedBroken.Contains(tcl[i]))return false;
            return true;
        };

        var posL=tf.InverseTransformPoint(r.Pos);
        var posV=new V3(posL.x,posL.y,posL.z);
        V3 center,normal,away,shotDirL;
        double actualDistM=-1.0;
        if(r.Src==DentSrc.Blast||r.Src==DentSrc.Test)
        {
            if(r.UseExplicitDir&&r.Dir.sqrMagnitude>1e-8f)
            {
                var dirW=r.Dir.normalized;
                var dirL=tf.InverseTransformDirection(dirW).normalized;
                away=VM.Norm(new V3(dirL.x,dirL.y,dirL.z),new V3(0,-1,0));
                shotDirL=away;
                var o=tf.InverseTransformPoint(r.Pos-dirW*0.25f);
                if(tg.Accel.RayFirst(sm.FlatBasis,sm.FlatTris,sm.TopologyVersion,new V3(o.x,o.y,o.z),away,0.75/sc,ok,out double dist,out normal,out _))
                {
                    center=VM.Add(new V3(o.x,o.y,o.z),VM.Mul(away,dist));
                }
                else if(!tg.Accel.Closest(sm.FlatBasis,sm.FlatTris,sm.TopologyVersion,posV,ok,out center,out normal))
                {how="no triangle on that side";return 0;}
            }
            else
            {
                if(!tg.Accel.ClosestBlast(sm.FlatBasis,sm.FlatTris,sm.TopologyVersion,sm.Normals,posV,ok,out center,out var outN,out _,out double frontCos,0.005/sc)){how="no triangle on that side";return 0;}
                actualDistM=VM.Len(VM.Sub(center,posV))*sc;
                if(r.Src==DentSrc.Blast&&actualDistM>0.04&&frontCos<=0.0){how="(back-facing blast)";return 0;}
                if(r.Src==DentSrc.Blast&&actualDistM>0.06)
                {
                    var centerW=tf.TransformPoint(new Vector3(center.X,center.Y,center.Z));
                    if(IsBlastOccluded(r.T,r.Vuid,r.Pos,centerW)){how="(occluded by another plate)";return 0;}
                }
                normal=outN;
                var fbL=r.Dir.sqrMagnitude>1e-8f?tf.InverseTransformDirection(r.Dir.normalized).normalized:Vector3.zero;
                away=DentMath.ResolveBlastAway(posV,center,outN,new V3(fbL.x,fbL.y,fbL.z));
                shotDirL=away;
            }
        }
        else
        {
            var dirL=tf.InverseTransformDirection(r.Dir.sqrMagnitude>1e-8f?r.Dir.normalized:Vector3.down).normalized;
            away=new V3(dirL.x,dirL.y,dirL.z);
            shotDirL=away;
            bool isExit=r.Style==DentStyle.ExitBlowout;
            var rayOrigW=isExit?r.Pos+r.Dir.normalized*0.15f:r.Pos-r.Dir.normalized*0.20f;
            var o=tf.InverseTransformPoint(rayOrigW);
            var rayDir=isExit?VM.Mul(away,-1.0):away;
            if(tg.Accel.RayFirst(sm.FlatBasis,sm.FlatTris,sm.TopologyVersion,new V3(o.x,o.y,o.z),rayDir,0.55/sc,ok,out double dist,out normal,out _))
            {
                center=VM.Add(new V3(o.x,o.y,o.z),VM.Mul(rayDir,dist));
            }
            else if(tg.Accel.Closest(sm.FlatBasis,sm.FlatTris,sm.TopologyVersion,posV,ok,out center,out normal))
            {
                var dv=VM.Sub(center,posV);
                double maxMissM=Math.Max(0.35,1.2*r.RadiusM);
                if(VM.Len(dv)*sc>maxMissM){how=$"(entry point is {VM.Len(dv)*sc:0.00}m from the nearest intact surface: a gap, no dent)";return 0;}
                how=$"(ray missed the mesh; closest point {VM.Len(dv)*sc:0.00}m from the entry point)";
            }
            else{how="no triangle on that side";return 0;}
        }

        normal=DentMath.Orient(normal,away);
        double probeSpanMm=0;
        var probeDir=r.Style==DentStyle.ExitBlowout?VM.Mul(normal,-1.0):normal;
        if(tg.Accel.RayFirst(sm.FlatBasis,sm.FlatTris,sm.TopologyVersion,VM.Add(center,VM.Mul(probeDir,0.002/sc)),probeDir,6.0/sc,ok,out double bdist,out _,out _))
        {
            double lm=bdist*sc+0.002;
            probeSpanMm=lm*1000.0;
            if(lm>=0.0025&&lm<=0.352)localThMm=probeSpanMm;
        }
        bool edgeOn=r.Src==DentSrc.Blast&&!r.UseExplicitDir&&DentMath.IsEdgeOnRim(probeSpanMm,r.ThicknessMm);

        DentStyle style=r.Src==DentSrc.Kin?r.Style:DentStyle.Blast;
        double radiusM=r.RadiusM,depthMm=r.DepthMm;
        if(r.Src==DentSrc.Kin)
        {
            radiusM=DentMath.KineticRadiusM(p.KineticRadiusM.Value,r.CalibreMm,r.EnergyMJ,style);
            depthMm=DentMath.KineticDepthMm(p.KineticDepthMm.Value,r.EnergyMJ,localThMm,style,r.IsNonDestructible?p.HullTurretDentScale.Value:1.0);
        }
        else if(r.Src==DentSrc.Blast&&r.IsNonDestructible&&r.BlastDmg>0)
        {
            double useDmg=r.BlastDmg;
            if(!r.UseExplicitDir&&actualDistM>0)
            {
                double realDmg=BlastMath.Damage(p.BlastBase.Value*r.DamageScale,r.CalibreMm,actualDistM,p.BlastRadiusPer100mm.Value,p.MinCalibreMm.Value);
                useDmg=Math.Min(useDmg,realDmg);
            }
            double effTh=Math.Min(localThMm,55.0);
            double strength=BlastMath.Strength(p.BlastStrengthPerMm.Value,effTh,p.DefaultPlateStrength.Value)/Math.Max(0.1,p.HullTurretBlastScale.Value);
            effQ=DentMath.BlastRatio(useDmg,strength);
            depthMm=DentMath.BlastDepthMm(effQ,p.BlastMaxDepthMm.Value);
            if(edgeOn){depthMm*=0.15;if(how.Length==0)how=$"(edge-on rim span={probeSpanMm:0}mm)";}
            if(depthMm<1.0){how=$"(local {localThMm:0.0}mm too thick for blast depth)";return 0;}
        }
        else if(edgeOn)
        {
            depthMm*=0.15;
            if(depthMm<0.5){how=$"(edge-on rim span={probeSpanMm:0}mm)";return 0;}
            if(how.Length==0)how=$"(edge-on rim span={probeSpanMm:0}mm)";
        }

        double radiusLocal=radiusM/sc;
        double depthLocal=depthMm/1000.0/sc;
        double thickLocal=localThMm/1000.0/sc;
        uint seed=unchecked((uint)r.Vuid*7919u+(uint)(++dentSeq)*2654435761u);
        ev=new DentEvent(center,normal,depthLocal,radiusLocal,r.Side,style,shotDirL,thickLocal,p.CraterLipRatio.Value,p.BlastBuckling.Value,seed);

        Func<int,bool>? isBrokenCell=tg.AppliedBroken.Count>0?c=>tg.AppliedBroken.Contains(c):null;
        if(!tg.IsLow&&p.SubdivEnabled.Value)
        {
            SubdivStretch(ev,out var sDir,out double sLen);
            int added=LocalSubdiv.RefineGraded(sm,center,radiusLocal,normal,DentMath.NormalReach(radiusLocal),p.SubdivTargetEdgeM.Value/sc,p.SubdivCoreEdgeM.Value/sc,p.SubdivMaxPasses.Value,p.SubdivMaxVertices.Value,r.Side,isBrokenCell,sDir,sLen);
            if(added>0){topologyChanged=true;tg.Accel.Invalidate();}
        }

        var movedVerts=new List<int>(64);
        int moved=DentField.ApplyRich(ev,sm,movedVerts);
        if(moved>0)SmartNormals.Compute(sm,movedVerts,p.SmoothAngleDeg.Value,isBrokenCell);
        return moved;
    }

    private void FlushLowDetail(DentSet set)
    {
        var p=Plugin.Instance;
        var high=set.Targets.FirstOrDefault(x=>!x.IsLow);
        bool flushedBroken=false;
        foreach(var tg in set.Targets)
        {
            if(!tg.IsLow||!tg.LowDirty)continue;
            if(tg.Rend==null||!tg.Rend.enabled)continue;
            if(high!=null&&high.AppliedBroken.Count>0)
                foreach(var c in high.AppliedBroken)tg.AppliedBroken.Add(c);
            if(high!=null&&high.Holes.Count>tg.Holes.Count)
                for(int i=tg.Holes.Count;i<high.Holes.Count;i++)tg.Holes.Add(high.Holes[i]);
            if(tg.AppliedBroken.Count>0||tg.Holes.Count>0)flushedBroken=true;
            if(tg.Work==null)
            {
                MaterializeTarget(tg,tg.Base,set.Plan,set.Cells,set.Root,set.Vuid,set.Plate);
            }
            else
            {
                tg.LowDirty=false;
                bool topoChanged=false;
                var moved=new List<int>(32);
                for(int i=tg.AppliedHoles;i<tg.Holes.Count;i++)
                    ApplyHoleEvent(tg,tg.Holes[i],true,ref topoChanged,moved,out _,out _);
                tg.AppliedHoles=tg.Holes.Count;
                if(moved.Count>0)
                    SmartNormals.Compute(tg.Mesh!,moved,p.SmoothAngleDeg.Value,tg.AppliedBroken.Count>0?c=>tg.AppliedBroken.Contains(c):null);
                if(tg.AppliedBroken.Count>0&&tg.Mesh!=null&&tg.Mesh.SubTriCell!=null)
                    tg.Mesh.Alive=new List<bool>(CellMask.AliveVertices(tg.Mesh.FlatBasis.Length,tg.Mesh.FlatTris,tg.Mesh.FlatTriCell!,c=>tg.AppliedBroken.Contains(c)));
                MeshOps.Upload(tg,true);
            }
        }
        if(flushedBroken)SyncShadowProxy(set);
    }

    // A side broke: the renderer shows a different pristine variant (source half or our private mirror half); rebuild the dented copy on it.
    private Mesh RebaseDents(long root,int vuid,MeshFilter f,Mesh show,SidePlan plan)
    {
        if(!dents.TryGetValue((root,vuid),out var set))return show;
        var tg=set.Targets.FirstOrDefault(x=>x.Filter!=null&&x.Filter.Pointer==f.Pointer);if(tg==null)return show;
        try
        {
            var old=tg.Work;
            if(tg.IsLow&&Plugin.Instance.LazyLowDetail.Value&&tg.Work==null&&(tg.Rend==null||!tg.Rend.enabled))
            {
                tg.Base=show;tg.LowDirty=true;
                return show;
            }
            MaterializeTarget(tg,show,plan,set.Cells,root,vuid,set.Plate);
            try{if(old!=null)Object.Destroy(old);}catch{}
            Plugin.Info($"[DENT] vuid={vuid} {tg.Name}: dented copy rebuilt on the surviving half ({tg.Events.Count} dent(s) replayed, v={tg.Work?.vertexCount ?? 0}).");
            return tg.Work??show;
        }
        catch(Exception e){Plugin.Warn("rebase dents: "+e.Message);return show;}
    }

    // Restores the original meshes and destroys every private dent copy; returns how many were destroyed.
    private int DisposeDents(Func<(long,int),bool> pick)
    {
        int n=0;
        foreach(var key in dents.Keys.Where(pick).ToArray())
        {
            var set=dents[key];
            try{if(set.ShadowFilter!=null&&set.ShadowOrig!=null)set.ShadowFilter.sharedMesh=set.ShadowOrig;}catch{}
            foreach(var tg in set.Targets)
            {
                try{if(tg.Filter!=null&&tg.Original!=null)tg.Filter.sharedMesh=tg.Original;}catch{}
                try{if(tg.Work!=null){Object.Destroy(tg.Work);n++;}}catch{}
                tg.Work=null;tg.Mesh=null;tg.VertBuf=null;tg.NormBuf=null;tg.TanBuf=null;
            }
            dents.Remove(key);
        }
        return n;
    }

    // ---- debris ----------------------------------------------------------------------------------------------
    private static string PartKey(int partVuid,Renderer r)
    {
        long gPtr=0;
        try{var g=r.GetComponentInParent<VehicleRendererGroup>();if(g!=null)gPtr=g.Pointer.ToInt64();}catch{}
        int pid=0;try{pid=r.transform.parent==null?0:r.transform.parent.GetInstanceID();}catch{}
        return PartKeyRule.Make(partVuid,gPtr,pid,r.gameObject.name);
    }
    // Called from HideAttached before the renderer is disabled: remember the visible mesh of the attached part (one per part+renderer group, the one with the most vertices).
    private static void CollectPart(int partVuid,BreakJob j,Renderer r)
    {
        try
        {
            if(r.shadowCastingMode==UnityEngine.Rendering.ShadowCastingMode.ShadowsOnly||!r.gameObject.activeInHierarchy)return;
            var f=r.GetComponent<MeshFilter>();var m=f==null?null:f.sharedMesh;if(m==null||IndexCount(m)==0)return;
            string key=PartKey(partVuid,r);
            if(j.Parts.TryGetValue(key,out var old)&&old.Verts>=m.vertexCount)return;
            j.Parts[key]=(r,m.vertexCount);
        }
        catch{}
    }
    private static void ReplayOnto(Mesh m,DentTarget tg,SidePlan? plan,V3? blastLocal,double warpDepthM,uint seed)
    {
        var p=Plugin.Instance;
        var sm=MeshOps.FromUnityMesh(m,plan,null,tg.Mesh?.MaxDepthM ?? (p.MaxDepthMm.Value/1000.0/tg.Scale));
        foreach(var e in tg.Events)
        {
            if(p.SubdivEnabled.Value)
            {
                SubdivStretch(e,out var sDir,out double sLen);
                LocalSubdiv.RefineGraded(sm,e.Center,e.RadiusM,e.Normal,DentMath.NormalReach(e.RadiusM),p.SubdivTargetEdgeM.Value/tg.Scale,p.SubdivCoreEdgeM.Value/tg.Scale,2,p.SubdivMaxVertices.Value,e.Side,null,sDir,sLen);
            }
            DentField.ApplyRich(e,sm);
        }
        var verts=new V3[sm.Basis.Count];
        for(int i=0;i<verts.Length;i++)verts[i]=sm.Position(i);
        if(blastLocal is {} bl&&warpDepthM>0)DebrisWarp.Apply(verts,bl,warpDepthM,seed);
        var norms=SmartNormals.ComputeSimple(verts,sm.FlatTris,p.SmoothAngleDeg.Value);
        if(verts.Length>65535&&m.indexFormat!=UnityEngine.Rendering.IndexFormat.UInt32){m.Clear(false);m.indexFormat=UnityEngine.Rendering.IndexFormat.UInt32;}
        var uv=new Vector3[verts.Length];for(int i=0;i<uv.Length;i++)uv[i]=new Vector3(verts[i].X,verts[i].Y,verts[i].Z);
        var un=new Vector3[norms.Length];for(int i=0;i<un.Length;i++)un[i]=new Vector3(norms[i].X,norms[i].Y,norms[i].Z);
        m.vertices=uv;m.normals=un;
        if(sm.UV.Count==verts.Length){var a=new Vector2[verts.Length];for(int i=0;i<a.Length;i++)a[i]=new Vector2(sm.UV[i].X,sm.UV[i].Y);m.uv=a;}
        if(sm.UV2.Count==verts.Length){var a=new Vector2[verts.Length];for(int i=0;i<a.Length;i++)a[i]=new Vector2(sm.UV2[i].X,sm.UV2[i].Y);m.uv2=a;}
        if(sm.Colors.Count==verts.Length){var a=new Color32[verts.Length];for(int i=0;i<a.Length;i++){var q=sm.Colors[i];a[i]=new Color32(q.R,q.G,q.B,q.A);}m.colors32=a;}
        m.subMeshCount=sm.SubTris.Length;
        for(int s=0;s<sm.SubTris.Length;s++)m.SetTriangles(sm.SubTris[s].ToArray(),s,false);
        m.RecalculateBounds();
    }
    private void DebrisLine(string s){if(dentBudget.TryLine())Plugin.Info(s);}
    // Spawns ONE debris object for a torn-off side or plate: a copy of its (dented & warped) mesh plus the visible meshes of its attached parts.
    private void SpawnDebris(BreakJob j)
    {
        var p=Plugin.Instance;if(!p.DebrisEnabled.Value)return;
        if(j.Cells!=null){SpawnCellDebris(j);return;}
        var plate=j.Comp.TryCast<PlateStructure>();if(plate==null){DebrisLine($"[DEBRIS] vuid={j.Vuid}: not a PlateStructure, no debris.");return;}
        var vr=plate.highDetailRenderer??plate.lowDetailRenderer;
        MeshFilter? fil=null;Renderer? rend=null;Mesh? cur=null;
        try{fil=vr?.Filter;rend=vr?.Renderer;cur=fil==null?null:fil.sharedMesh;}catch{}
        if(fil==null||rend==null){DebrisLine($"[DEBRIS] vuid={j.Vuid}: plate renderer not reachable, no debris.");return;}
        long root=j.T.Root;
        float sc=MeanScale(fil.transform);
        V3? bl=j.HasBlast?j.BlastLocal:(V3?)null;
        uint seed=unchecked((uint)j.Vuid*7919u+(uint)j.Side*104729u+(uint)Time.frameCount*31u);
        Mesh mesh;
        if(j.Side!=PlateSide.Whole&&j.Plan is {PerSide:true} pl)
        {
            var pr=pl.Pairs.FirstOrDefault(x=>x.Valid&&x.Filter!=null&&x.Filter.Pointer==fil.Pointer);
            if(pr?.Source==null||pl.Mirror==null){DebrisLine($"[DEBRIS] vuid={j.Vuid} side={SideName(j.Side)}: no valid source mesh for the torn half, no debris.");return;}
            mesh=j.Side==PlateSide.Mirror?SideOps.BuildHalf(pr.Source,pl.Mirror):MeshOps.Clone(pr.Source,"PD_debris");
            mesh.name="PD_debris_"+j.Vuid;
            if(dents.TryGetValue((root,j.Vuid),out var ds)){var tg=ds.Targets.FirstOrDefault(x=>x.Filter!=null&&x.Filter.Pointer==fil.Pointer);if(tg!=null)ReplayOnto(mesh,tg,pl,bl,p.DebrisWarpMm.Value/1000.0/sc,seed);}
        }
        else
        {
            if(cur==null||!cur.isReadable){DebrisLine($"[DEBRIS] vuid={j.Vuid}: the plate mesh is not readable, no debris.");return;}
            mesh=MeshOps.Clone(cur,"PD_debris_"+j.Vuid);
        }
        var item=new DebrisItem{Vuid=j.Vuid,Side=SideName(j.Side)};
        item.Meshes.Add(mesh);
        var tr=fil.transform;
        var holder=new GameObject("PD_debris_"+j.Vuid);
        holder.layer=fil.gameObject.layer;
        var ht=holder.transform;ht.position=tr.position;ht.rotation=tr.rotation;ht.localScale=tr.lossyScale;
        holder.AddComponent<MeshFilter>().sharedMesh=mesh;
        holder.AddComponent<MeshRenderer>().sharedMaterials=rend.sharedMaterials;
        int parts=0,cap=Math.Max(0,p.MaxAttachedParts.Value);
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
        FinishDebris(j,item,ht,mesh,parts,"whole side or plate");
    }
    private void DestroyDebris(DebrisItem d,string reason)
    {
        if(d.Body.State==DebrisState.Flying&&flyingDebris>0)flyingDebris--;
        try{if(d.Pivot!=null)Object.Destroy(d.Pivot);}catch{}
        foreach(var m in d.Meshes)try{if(m!=null)Object.Destroy(m);}catch{}
        d.Meshes.Clear();
        DebrisLine($"[DEBRIS] vuid={d.Vuid} side={d.Side} removed reason={reason}");
    }
    private void StepDebris()
    {
        if(debris.Count==0)return;
        float rdt=Time.deltaTime;
        if(rdt<=0f)return;
        if(flyingDebris>0)
        {
            float dt=Mathf.Min(rdt,0.05f);
            List<DebrisItem>? lost=null;
            foreach(var d in debris.Items)
            {
                if(d.Body.State!=DebrisState.Flying)continue;
                if(d.Pivot==null){(lost??=new()).Add(d);continue;}
                var st=d.Body.Step(dt,ground);
                var bp=d.Body.Pos;var pt=d.Pivot.transform;pt.position=new Vector3(bp.X,bp.Y,bp.Z);
                if(st==DebrisState.Flying)pt.Rotate(d.Axis,(float)(d.SpinDeg*dt),Space.World);
                else if(st==DebrisState.Landed)
                {
                    d.Landed=true;if(flyingDebris>0)flyingDebris--;
                    double dx=d.Body.Pos.X-d.Body.Start.X,dz=d.Body.Pos.Z-d.Body.Start.Z;
                    DebrisLine($"[DEBRIS] vuid={d.Vuid} side={d.Side} landed t={d.Body.LandedAt:0.00}s at dist={Math.Sqrt(dx*dx+dz*dz):0.0}m (height change {d.Body.Pos.Y-d.Body.Start.Y:0.0}m)");
                }
                else if(st==DebrisState.Lost){(lost??=new()).Add(d);}
            }
            if(lost!=null)foreach(var d in lost){debris.Remove(d);DestroyDebris(d,"lost (fell below the start height minus 50 m)");}
        }
        float now=Time.time;
        if(now>=nextDebrisExpireAt)
        {
            nextDebrisExpireAt=now+1f;
            foreach(var d in debris.Expire(now))DestroyDebris(d,"age ("+Plugin.Instance.LifetimeS.Value.ToString("0")+" s)");
        }
    }
    private int ClearDebris(string reason)
    {
        var all=debris.Clear();
        foreach(var d in all)DestroyDebris(d,reason);
        flyingDebris=0;nextDebrisExpireAt=0;
        return all.Count;
    }

    // ---- 0.7.1: impact mark flame snapping onto dented surface -----------------------------------------------
    private sealed class SnappedMark
    {
        internal long Root;internal int Vuid;internal Transform Tr=null!;internal Transform? Parent;
        internal Vector3 OrigLocal,LastApplied;internal bool Logged;
    }
    private sealed class MarkSweep
    {
        internal long Root;internal VehicleComponent Comp=null!;internal int Vuid;internal int StartFrame,UntilFrame;
    }
    private readonly Dictionary<IntPtr,SnappedMark> snappedMarks=new();
    private readonly List<MarkSweep> markSweeps=new();

    private void QueueMarkSweep(long root,VehicleComponent comp,int vuid)
    {
        int now=Time.frameCount,until=now+30;
        for(int i=0;i<markSweeps.Count;i++)
        {
            var s=markSweeps[i];
            if(s.Root==root&&s.Vuid==vuid){s.Comp=comp;s.StartFrame=now;s.UntilFrame=until;return;}
        }
        markSweeps.Add(new MarkSweep{Root=root,Comp=comp,Vuid=vuid,StartFrame=now,UntilFrame=until});
    }

    private void SnapMarks()
    {
        if(markSweeps.Count==0)return;
        int f=Time.frameCount;
        for(int i=markSweeps.Count-1;i>=0;i--)
        {
            var ms=markSweeps[i];
            if(f>ms.UntilFrame||ms.Comp==null||ms.Comp.WasCollected){markSweeps.RemoveAt(i);continue;}
            if(f-ms.StartFrame>=6&&(f&1)!=0)continue;
            Safe("snap marks",()=>SnapMarksFor(ms.Root,ms.Comp,ms.Vuid));
        }
    }

    private void SnapMarksFor(long root,VehicleComponent comp,int vuid)
    {
        if(comp==null||comp.WasCollected)return;
        if(!dents.TryGetValue((root,vuid),out var set))return;
        var tg=set.Targets.FirstOrDefault(x=>!x.IsLow&&x.Mesh!=null&&x.Filter!=null);
        if(tg==null||tg.Mesh==null||tg.Filter==null)return;
        var go=comp.gameObject;if(go==null)return;

        var tf=tg.Filter.transform;float sc=tg.Scale;var sm=tg.Mesh;
        Func<int,bool>? ok=tg.AppliedBroken.Count>0&&sm.FlatTriCell!=null
            ?(ti=>!(ti<sm.FlatTriCell.Length&&tg.AppliedBroken.Contains(sm.FlatTriCell[ti])))
            :null;

        foreach(var tr in go.GetComponentsInChildren<Transform>(true))
        {
            if(tr==null||tr.gameObject==go||!tr.gameObject.activeInHierarchy||!tr.name.Contains("ProjectileImpactMark"))continue;
            var par=tr.parent;if(par==null)continue;
            IntPtr ptr=tr.Pointer;
            if(!snappedMarks.TryGetValue(ptr,out var rec))
            {
                rec=new SnappedMark{Root=root,Vuid=vuid,Tr=tr,Parent=par,OrigLocal=tr.localPosition,LastApplied=tr.localPosition};
                snappedMarks[ptr]=rec;
            }
            else if(rec.Parent==null||rec.Parent.Pointer!=par.Pointer
                 ||((tr.localPosition-rec.LastApplied).sqrMagnitude>4e-4f&&(tr.localPosition-rec.OrigLocal).sqrMagnitude>4e-4f))
            {
                rec.Root=root;rec.Vuid=vuid;rec.Parent=par;
                rec.OrigLocal=tr.localPosition;rec.LastApplied=tr.localPosition;rec.Logged=false;
            }

            Vector3 origWorld=par.TransformPoint(rec.OrigLocal);
            Vector3 origL=tf.InverseTransformPoint(origWorld);
            if(!SurfaceSnap.DeformedPoint(sm,tg.Accel,new V3(origL.x,origL.y,origL.z),0.12/sc,ok,out var snMesh,out var dispMesh))continue;
            double dispM=VM.Len(dispMesh)*sc;
            if(dispM<0.002)continue;

            Vector3 snWorld=tf.TransformPoint(new Vector3(snMesh.X,snMesh.Y,snMesh.Z));
            Vector3 snLocal=par.InverseTransformPoint(snWorld);
            if((tr.localPosition-snLocal).sqrMagnitude>1e-7f)tr.localPosition=snLocal;
            rec.LastApplied=snLocal;
            if(!rec.Logged&&Plugin.Instance.DetailedLogging.Value)
            {
                rec.Logged=true;
                DLine($"[MARK] vuid={vuid} snapped impact mark {dispM*1000.0:0.0}mm onto dented surface");
            }
        }
    }

    private void RestoreSnappedMarks(long root)
    {
        markSweeps.RemoveAll(s=>s.Root==root);
        foreach(var ptr in snappedMarks.Keys.ToArray())
        {
            var rec=snappedMarks[ptr];
            if(rec.Root!=root)continue;
            try
            {
                if(rec.Tr!=null&&!rec.Tr.WasCollected&&rec.Parent!=null&&rec.Tr.parent!=null&&rec.Tr.parent.Pointer==rec.Parent.Pointer
                   &&(rec.Tr.localPosition-rec.LastApplied).sqrMagnitude<4e-4f)
                    rec.Tr.localPosition=rec.OrigLocal;
            }catch{}
            snappedMarks.Remove(ptr);
        }
    }

    private void RestoreAllSnappedMarks()
    {
        markSweeps.Clear();
        foreach(var rec in snappedMarks.Values)
        {
            try
            {
                if(rec.Tr!=null&&!rec.Tr.WasCollected&&rec.Parent!=null&&rec.Tr.parent!=null&&rec.Tr.parent.Pointer==rec.Parent.Pointer
                   &&(rec.Tr.localPosition-rec.LastApplied).sqrMagnitude<4e-4f)
                    rec.Tr.localPosition=rec.OrigLocal;
            }catch{}
        }
        snappedMarks.Clear();
    }
}
