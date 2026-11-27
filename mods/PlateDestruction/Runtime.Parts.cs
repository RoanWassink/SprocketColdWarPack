using System.Diagnostics;
using Sprocket.Vehicles;
using Sprocket.Vehicles.PlateStructures;
using UnityEngine;
using Object=UnityEngine.Object;
namespace PlateDestructionP0;

// 0.9.0: Destructible loose exterior parts (smoke launchers, lamps, stowage, tools, antennas)
// under nearby blasts (with inter-plate occlusion) and direct kinetic hits, plus core/cannon hierarchy guards.
public sealed partial class Runtime
{
    private sealed class LoosePart
    {
        internal VehicleComponent Comp=null!;
        internal int Vuid;
        internal string Type="",CompId="",GoName="";
        internal bool HasCollider,Broken;
        internal double Strength,Damage;
        internal Transform? BoxTf;
        internal Bounds LocalBounds;
        internal double HalfSizeM,RadiusM;
        internal List<Renderer>? CachedRenderers;

        internal bool TryWorldCenter(out Vector3 centerW)
        {
            centerW=Vector3.zero;
            if(Comp==null||Comp.WasCollected)return false;
            var tf=BoxTf;
            if(tf==null||tf.WasCollected)
            {
                try{tf=Comp.transform;}catch{return false;}
            }
            if(tf==null)return false;
            try{centerW=tf.TransformPoint(LocalBounds.center);return true;}
            catch{return false;}
        }
    }

    private static bool HasCoreBehaviour(VehicleObject? obj,bool includePlateStructure=true,bool includeOpticsAsCore=false)
    {
        if(obj==null)return false;
        try
        {
            var comps=obj.behaviours;
            if(comps==null)return false;
            for(int i=0;i<comps.Count;i++)
            {
                var b=comps[i];
                if(b==null||b.WasCollected)continue;
                string tn=ClassName(b);
                if(!includePlateStructure&&tn=="PlateStructure")continue;
                if(PartFilter.IsCoreComponent(tn,b.ComponentID))return true;
                if(includeOpticsAsCore&&PartFilter.IsOpticsOrHatch(tn,b.ComponentID,null))return true;
            }
        }catch{}
        return false;
    }

    /// <summary>True if any ancestor VehicleObject above comp (stopping at a PlateStructure or stopAt) carries a core component such as Cannon, Mantlet, WheelMount, Suspension, Track, Engine, etc.</summary>
    private static bool IsUnderCoreObject(VehicleComponent comp,VehicleComponent? stopAt=null)
    {
        try
        {
            IntPtr stopPtr=stopAt==null?IntPtr.Zero:stopAt.Pointer;
            var cur=comp.VehicleTransform?.Parent;
            int depth=0;
            while(cur!=null&&depth++<12)
            {
                var vo=cur.VehicleObject;
                if(vo!=null)
                {
                    bool hitPlate=false;
                    var comps=vo.behaviours;
                    if(comps!=null)
                    {
                        for(int i=0;i<comps.Count;i++)
                        {
                            var b=comps[i];
                            if(b==null||b.WasCollected)continue;
                            if(stopPtr!=IntPtr.Zero&&b.Pointer==stopPtr)return false;
                            string tn=ClassName(b);
                            if(tn=="PlateStructure"){hitPlate=true;continue;}
                            if(PartFilter.IsCoreComponent(tn,b.ComponentID))return true;
                        }
                    }
                    if(hitPlate)return false;
                }
                cur=cur.Parent;
            }
        }catch{}
        return false;
    }

    private static bool LoosePartBox(VehicleComponent comp,Vehicle v,out Transform boxTf,out Bounds localBounds)
    {
        if(PartBox(comp,out boxTf,out localBounds,out _))return true;
        boxTf=comp.transform;localBounds=default;
        try
        {
            var vgo=v.gameObject;
            var go=comp.gameObject;
            if(go==null||go==vgo||boxTf==null)return false;
            Bounds wb=default;bool have=false;
            foreach(var r in go.GetComponentsInChildren<Renderer>(true))
            {
                if(r==null||r.gameObject==vgo||r.gameObject.name.Contains("PlateStructure"))continue;
                if(!have){wb=r.bounds;have=true;}else wb.Encapsulate(r.bounds);
            }
            if(!have||wb.size.sqrMagnitude<1e-8f)return false;
            var cL=boxTf.InverseTransformPoint(wb.center);
            float sc=MeanScale(boxTf);
            localBounds=new Bounds(cL,wb.size/Math.Max(sc,1e-3f));
            return true;
        }
        catch{return false;}
    }

    private void DiscoverLooseParts(Tracked t)
    {
        t.LooseParts.Clear();
        t.LooseByVuid.Clear();
        var p=Plugin.Instance;
        if(!p.LoosePartsEnabled.Value)return;
        long root=t.Root;
        var vgo=t.V.gameObject;
        bool incOptics=p.LoosePartsIncludeOptics.Value;
        bool incEra=p.LoosePartsIncludeEra.Value;
        double baseStr=p.LoosePartStrength.Value;

        foreach(var kv in t.Map)
        {
            var comp=kv.Value;
            if(comp==null||comp.WasCollected)continue;
            int vuid=kv.Key;
            if(t.DestrSet.Contains(vuid))continue;
            GameObject? go=null;
            try{go=comp.gameObject;}catch{}
            if(go==null||go==vgo)continue;

            string tn=ClassName(comp);
            string cid="";string gname="";
            try{cid=comp.ComponentID??"";gname=go.name??"";}catch{}
            if(!PartFilter.IsLoosePartCandidate(tn,cid,gname,incOptics,incEra))continue;

            // Reject if sibling on the same VehicleObject is a core part (e.g. Cannon, Mantlet, TraverseMotor, WheelMount) or optics/ERA when disabled.
            var vo=comp.vehicleObject;
            if(HasCoreBehaviour(vo,includePlateStructure:true,includeOpticsAsCore:!incOptics))continue;
            if(!incEra&&vo!=null)
            {
                bool eraSib=false;
                try
                {
                    var sibs=vo.behaviours;
                    if(sibs!=null)for(int i=0;i<sibs.Count;i++){var sb=sibs[i];if(sb!=null&&PartFilter.IsEraComponent(ClassName(sb),sb.ComponentID,null)){eraSib=true;break;}}
                }catch{}
                if(eraSib)continue;
            }
            if(IsUnderCoreObject(comp))continue;

            if(!LoosePartBox(comp,t.V,out var btf,out var lb))continue;
            double sizeM=lb.size.magnitude*MeanScale(btf);
            if(sizeM<0.02||sizeM>LoosePartMath.MaxPartSizeM)continue;

            bool hasCol=false;
            try{var vom=comp.TryCast<VehicleObjectModel>();hasCol=vom!=null&&vom.generateDamageColliders;}catch{}
            var lp=new LoosePart
            {
                Comp=comp,Vuid=vuid,Type=tn,CompId=cid,GoName=gname,
                HasCollider=hasCol,Broken=State.IsHidden(root,vuid),
                Strength=LoosePartMath.Strength(baseStr,sizeM),
                BoxTf=btf,LocalBounds=lb,HalfSizeM=sizeM*0.5,RadiusM=Math.Max(0.06,sizeM*0.55)
            };
            t.LooseParts.Add(lp);
            t.LooseByVuid[vuid]=lp;
        }
        if(p.DetailedLogging.Value||t.LooseParts.Count>0)
            Plugin.Info($"[PARTS] {t.Name}: {t.LooseParts.Count} destructible loose exterior part(s) tracked");
    }

    private void DiscoverEraParts(Tracked t)
    {
        t.EraParts.Clear();
        var p=Plugin.Instance;
        long root=t.Root;
        var vgo=t.V.gameObject;
        foreach(var kv in t.Map)
        {
            var comp=kv.Value;
            if(comp==null||comp.WasCollected)continue;
            int vuid=kv.Key;
            if(t.DestrSet.Contains(vuid))continue;
            string tn=ClassName(comp);
            if(tn!="VehicleObjectModel")continue;
            GameObject? go=null;
            try{go=comp.gameObject;}catch{}
            if(go==null||go==vgo)continue;
            string cid="",gname="";
            try{cid=comp.ComponentID??"";gname=go.name??"";}catch{}
            if(!PartFilter.IsEraComponent(tn,cid,gname))continue;
            if(PartFilter.IsCoreComponent(tn,cid)||HasCoreBehaviour(comp.vehicleObject,includePlateStructure:true,includeOpticsAsCore:false)||IsUnderCoreObject(comp))continue;
            if(!LoosePartBox(comp,t.V,out var btf,out var lb))continue;
            double sizeM=lb.size.magnitude*MeanScale(btf);
            if(sizeM<0.02||sizeM>LoosePartMath.MaxPartSizeM)continue;
            bool hasCol=false;
            try{var vom=comp.TryCast<VehicleObjectModel>();hasCol=vom!=null&&vom.generateDamageColliders;}catch{}
            t.EraParts.Add(new LoosePart
            {
                Comp=comp,Vuid=vuid,Type=tn,CompId=cid,GoName=gname,
                HasCollider=hasCol,Broken=State.IsHidden(root,vuid),
                Strength=25.0,
                BoxTf=btf,LocalBounds=lb,HalfSizeM=sizeM*0.5,RadiusM=Math.Max(0.06,sizeM*0.55)
            });
        }
        if(p.DetailedLogging.Value&&t.EraParts.Count>0)
            Plugin.Info($"[ERA] {t.Name}: {t.EraParts.Count} ERA part(s) tracked for blast stripping");
    }

    private void HandleEraBlastStrip(BlastEvent e,double cal)
    {
        var p=Plugin.Instance;
        if(!p.StripEraOnBlast.Value||!p.BreakEnabled.Value)return;
        string kind=(e.Kind??"").Trim().ToLowerInvariant();
        if(kind is "heat" or "era" or "aphe")return;
        double rEra=BlastBreachMath.EraStripRadiusM(cal,p.EraStripRadiusPer100mm.Value);
        if(rEra<=0)return;
        double vehicleCutoffSq=(rEra+45.0)*(rEra+45.0);
        int totalStrippedBlocks=0;

        foreach(var t in tracked.Values)
        {
            if(t.EraParts.Count==0)continue;
            try{if((t.V.transform.position-e.Pos).sqrMagnitude>vehicleCutoffSq)continue;}catch{}
            var toStrip=new List<(LoosePart Lp,double Dist)>();
            for(int i=0;i<t.EraParts.Count;i++)
            {
                var lp=t.EraParts[i];
                if(lp.Broken||State.IsHidden(t.Root,lp.Vuid))continue;
                if(!lp.TryWorldCenter(out var cW))continue;
                float dist=(cW-e.Pos).magnitude;
                double effDist=Math.Max(0.0,dist-lp.HalfSizeM);
                if(effDist>rEra)continue;
                if(dist>0.14f&&IsBlastOccluded(t,-1,e.Pos,cW))continue;
                toStrip.Add((lp,effDist));
            }
            if(toStrip.Count==0)continue;
            toStrip.Sort((a,b)=>a.Dist.CompareTo(b.Dist));
            int cap=Math.Min(24,toStrip.Count);
            var batch=new List<(LoosePart Lp,Vector3 BlastOrHitPos,Vector3 PushDir,double Ratio,bool IsKinetic)>(cap);
            int housingCount=0;
            for(int i=0;i<cap;i++)
            {
                var x=toStrip[i];
                if(!PartFilter.IsEraActiveCell(x.Lp.CompId,x.Lp.GoName))housingCount++;
                x.Lp.TryWorldCenter(out var cW);
                var dir=cW-e.Pos;
                double ratio=Math.Clamp(1.0+(rEra-x.Dist)/Math.Max(rEra,0.2),1.0,2.5);
                x.Lp.Damage=x.Lp.Strength*ratio;
                batch.Add((x.Lp,e.Pos,dir.sqrMagnitude>1e-6f?dir.normalized:Vector3.up,ratio,false));
            }
            BreakLooseParts(t,batch);
            int blocks=housingCount>0?housingCount:Math.Max(1,(batch.Count+2)/3);
            totalStrippedBlocks+=blocks;
            Plugin.Info($"[ERA] {t.Name}: {cal:0}mm blast stripped {blocks} ERA block(s) ({batch.Count} components) within R={rEra:0.00}m");
        }
        if(totalStrippedBlocks>0)
            BallisticsEffectProbe.ReportEraStripped(e.EbSeq,totalStrippedBlocks,rEra);
    }

    private void HandleLoosePartBlast(BlastEvent e,double effBase,double reach)
    {
        var p=Plugin.Instance;
        if(!p.LoosePartsEnabled.Value)return;
        double vehicleCutoffSq=(reach+45.0)*(reach+45.0);
        foreach(var t in tracked.Values)
        {
            if(t.LooseParts.Count==0)continue;
            try{if((t.V.transform.position-e.Pos).sqrMagnitude>vehicleCutoffSq)continue;}catch{}
            var toBreak=new List<(LoosePart Lp,double Dist,double Ratio)>();
            for(int i=0;i<t.LooseParts.Count;i++)
            {
                var lp=t.LooseParts[i];
                if(lp.Broken||State.IsHidden(t.Root,lp.Vuid))continue;
                if(!lp.TryWorldCenter(out var cW))continue;
                float dist=(cW-e.Pos).magnitude;
                double effDist=Math.Max(0.0,dist-lp.HalfSizeM);
                if(effDist>reach)continue;
                double dmg=BlastMath.Damage(effBase,e.Calibre,effDist,p.BlastRadiusPer100mm.Value,p.MinCalibreMm.Value);
                if(dmg<=0)continue;
                if(dist>0.12f&&IsBlastOccluded(t,-1,e.Pos,cW))continue;
                lp.Damage+=dmg;
                if(lp.Damage>=lp.Strength)
                    toBreak.Add((lp,effDist,lp.Strength>0?lp.Damage/lp.Strength:1.0));
            }
            if(toBreak.Count==0)continue;
            toBreak.Sort((a,b)=>a.Dist.CompareTo(b.Dist));
            int cap=Math.Min(LoosePartMath.MaxPartsPerBlast,toBreak.Count);
            var batch=new List<(LoosePart Lp,Vector3 BlastOrHitPos,Vector3 PushDir,double Ratio,bool IsKinetic)>(cap);
            for(int i=0;i<cap;i++)
            {
                var x=toBreak[i];
                x.Lp.TryWorldCenter(out var cW);
                var dir=cW-e.Pos;
                batch.Add((x.Lp,e.Pos,dir.sqrMagnitude>1e-6f?dir.normalized:Vector3.up,x.Ratio,false));
            }
            BreakLooseParts(t,batch);
        }
    }

    private void HandleLoosePartKinetic(Tracked t,bool haveSeg,Vector3 seg0,Vector3 seg1,List<PathHit> path)
    {
        var p=Plugin.Instance;
        if(!p.LoosePartsEnabled.Value||t.LooseParts.Count==0)return;
        var hitVuids=new HashSet<int>();
        var toBreak=new List<(LoosePart Lp,Vector3 BlastOrHitPos,Vector3 PushDir,double Ratio,bool IsKinetic)>();

        // Route 1: exact DamageModel collider hits from SimulationRan
        for(int i=0;i<path.Count;i++)
        {
            var ph=path[i];
            if(!t.LooseByVuid.TryGetValue(ph.Vuid,out var lp)||lp.Broken||State.IsHidden(t.Root,lp.Vuid))continue;
            if(!hitVuids.Add(lp.Vuid))continue;
            double dmg=LoosePartMath.KineticDamage(DamageFormula.KineticMJ(ph.Mass,ph.Speed),ph.CalibreMm);
            lp.Damage+=dmg;
            if(lp.Damage>=lp.Strength)
            {
                var dir=ph.Dir.sqrMagnitude>1e-6f?ph.Dir.normalized:Vector3.forward;
                toBreak.Add((lp,IsFinite(ph.End)?ph.End:seg1,dir,lp.Strength>0?lp.Damage/lp.Strength:1.0,true));
            }
        }

        // Route 2: ray-box hit check along the projectile path for cosmetic loose parts without damage colliders
        if(haveSeg&&path.Count>0)
        {
            var p0=path[0];
            double energyMJ=DamageFormula.KineticMJ(p0.Mass,p0.Speed);
            int cal=p0.CalibreMm;
            double dmg=LoosePartMath.KineticDamage(energyMJ,cal);
            if(dmg>0)
            {
                float projR=Math.Max(0.025f,cal/2000f);
                var s0=new V3(seg0.x,seg0.y,seg0.z);
                var s1=new V3(seg1.x,seg1.y,seg1.z);
                var dir=(seg1-seg0);
                dir=dir.sqrMagnitude>1e-6f?dir.normalized:(p0.Dir.sqrMagnitude>1e-6f?p0.Dir.normalized:Vector3.forward);
                for(int i=0;i<t.LooseParts.Count;i++)
                {
                    var lp=t.LooseParts[i];
                    if(lp.Broken||hitVuids.Contains(lp.Vuid)||State.IsHidden(t.Root,lp.Vuid))continue;
                    if(!lp.TryWorldCenter(out var cW))continue;
                    double rSum=lp.RadiusM+projR;
                    if(LoosePartMath.SegmentDistSq(new V3(cW.x,cW.y,cW.z),s0,s1)>rSum*rSum)continue;
                    var tf=lp.BoxTf;
                    if(tf==null||tf.WasCollected)continue;
                    Vector3 aL,bL;
                    try{aL=tf.InverseTransformPoint(seg0);bL=tf.InverseTransformPoint(seg1);}
                    catch{continue;}
                    float pad=projR/Math.Max(MeanScale(tf),1e-3f);
                    var bmin=new V3(lp.LocalBounds.min.x-pad,lp.LocalBounds.min.y-pad,lp.LocalBounds.min.z-pad);
                    var bmax=new V3(lp.LocalBounds.max.x+pad,lp.LocalBounds.max.y+pad,lp.LocalBounds.max.z+pad);
                    if(!SegmentBox.Intersects(new V3(aL.x,aL.y,aL.z),new V3(bL.x,bL.y,bL.z),bmin,bmax))continue;
                    hitVuids.Add(lp.Vuid);
                    lp.Damage+=dmg;
                    if(lp.Damage>=lp.Strength)
                        toBreak.Add((lp,cW,dir,lp.Strength>0?lp.Damage/lp.Strength:1.0,true));
                }
            }
        }

        if(toBreak.Count>0)BreakLooseParts(t,toBreak);
    }

    private static List<Renderer> LoosePartRenderers(LoosePart lp,Vehicle v)
    {
        if(lp.CachedRenderers!=null)return lp.CachedRenderers;
        var res=new List<Renderer>();
        var vgo=v.gameObject;
        void AddR(Renderer? r){if(r==null||r.gameObject==vgo||r.gameObject.name.Contains("PlateStructure")||res.Any(x=>x.Pointer==r.Pointer))return;res.Add(r);}
        void AddTree(GameObject? go){if(go==null||go==vgo)return;foreach(var r in go.GetComponentsInChildren<Renderer>(true))AddR(r);}
        foreach(var r in ModelRenderers(lp.Comp))AddR(r);
        try{AddTree(lp.Comp.gameObject);}catch{}
        try{AddTree(lp.Comp.vehicleObject?.gameObject);}catch{}
        lp.CachedRenderers=res;
        return res;
    }

    private void BreakLooseParts(Tracked t,List<(LoosePart Lp,Vector3 BlastOrHitPos,Vector3 PushDir,double Ratio,bool IsKinetic)> items)
    {
        long root=t.Root;
        var p=Plugin.Instance;
        var plateOwn=new HashSet<IntPtr>();
        foreach(var pe in t.AllPlates)
            foreach(var pr in pe.Rends)
                if(pr!=null)plateOwn.Add(pr.Pointer);
        var emptyOther=new HashSet<IntPtr>();
        var batchJob=new BreakJob{T=t,Vuid=0,Side=PlateSide.Whole};

        foreach(var entry in items)
        {
            var lp=entry.Lp;
            if(lp.Broken||lp.Comp==null||lp.Comp.WasCollected)continue;
            lp.Broken=true;
            State.SetHidden(root,lp.Vuid,true);

            var key=(root,lp.Vuid);
            if(!hideRecords.TryGetValue(key,out var rec)){rec=new HideRecord();hideRecords[key]=rec;}
            var ownMeshes=new List<(Renderer R,Mesh M)>();
            int allHidden=0;
            foreach(var r in LoosePartRenderers(lp,t.V))
            {
                if(plateOwn.Contains(r.Pointer))continue;
                if(ScreenVisible(r))
                {
                    var mf=r.GetComponent<MeshFilter>();
                    var m=mf==null?null:mf.sharedMesh;
                    if(m!=null&&m.vertexCount>0)ownMeshes.Add((r,m));
                }
                if(!rec.Enabled.Any(x=>x.R!=null&&x.R.Pointer==r.Pointer))
                    rec.Enabled.Add((r,r.enabled));
                r.enabled=false;
                allHidden++;
            }

            if(ownMeshes.Count==0&&lp.TryWorldCenter(out var cW))
            {
                double maxDiam=Math.Max(0.30,lp.HalfSizeM*3.2+0.12);
                double maxOff=Math.Max(0.12,lp.HalfSizeM*0.95);
                foreach(var mr in t.V.gameObject.GetComponentsInChildren<MeshRenderer>(false))
                {
                    if(mr==null||plateOwn.Contains(mr.Pointer)||mr.gameObject.name.Contains("PlateStructure"))continue;
                    if(mr.gameObject.name.StartsWith("PD_"))continue;
                    var b=mr.bounds;
                    if(b.size.magnitude>maxDiam||(b.center-cW).magnitude>maxOff)continue;
                    var mf=mr.GetComponent<MeshFilter>();
                    var m=mf==null?null:mf.sharedMesh;
                    if(m!=null&&m.name.StartsWith("PD_"))continue;
                    if(m!=null&&m.vertexCount>0)ownMeshes.Add((mr,m));
                    if(!rec.Enabled.Any(x=>x.R!=null&&x.R.Pointer==mr.Pointer))rec.Enabled.Add((mr,mr.enabled));
                    mr.enabled=false;
                    allHidden++;
                }
            }

            int removedTris=0;
            int extraStart=batchJob.Extra.Count;
            if(ownMeshes.Count==0)
            {
                var att=new Att{Comp=lp.Comp,Vuid=lp.Vuid,Type=lp.Type};
                try{removedTris=HideMerged(att,batchJob,root,plateOwn,emptyOther,out _);}catch{}
            }

            bool spawnedDebris=false;
            bool skipDebris=PartFilter.IsEraActiveCell(lp.CompId,lp.GoName);
            if(p.DebrisEnabled.Value&&!skipDebris)
            {
                if(ownMeshes.Count>0)
                {
                    ownMeshes.Sort((a,b)=>b.M.vertexCount.CompareTo(a.M.vertexCount));
                    var primary=ownMeshes[0];
                    var item=new DebrisItem{Vuid=lp.Vuid,Side="part"};
                    var holder=new GameObject("PD_part_debris_"+lp.Vuid);
                    holder.layer=primary.R.gameObject.layer;
                    var ht=holder.transform;var pt=primary.R.transform;
                    ht.position=pt.position;ht.rotation=pt.rotation;ht.localScale=pt.lossyScale;
                    holder.AddComponent<MeshFilter>().sharedMesh=primary.M;
                    holder.AddComponent<MeshRenderer>().sharedMaterials=primary.R.sharedMaterials;
                    int subParts=0;
                    for(int i=1;i<ownMeshes.Count&&subParts<6;i++)
                    {
                        var sub=ownMeshes[i];
                        var g=new GameObject("PD_part_sub");g.layer=sub.R.gameObject.layer;
                        var gt=g.transform;var st=sub.R.transform;
                        gt.position=st.position;gt.rotation=st.rotation;gt.localScale=st.lossyScale;
                        g.AddComponent<MeshFilter>().sharedMesh=sub.M;
                        g.AddComponent<MeshRenderer>().sharedMaterials=sub.R.sharedMaterials;
                        gt.SetParent(ht,true);subParts++;
                    }
                    var centre=ht.TransformPoint(primary.M.bounds.center);
                    var blastPos=entry.IsKinetic?centre-entry.PushDir*0.5f:entry.BlastOrHitPos;
                    var debJob=new BreakJob{T=t,Comp=lp.Comp,Vuid=lp.Vuid,Side=PlateSide.Whole,HasBlast=true,BlastPos=blastPos,Ratio=Math.Min(entry.Ratio,2.5)};
                    FinishDebris(debJob,item,ht,primary.M,subParts+1,$"part={lp.CompId}");
                    spawnedDebris=true;
                }
                else if(batchJob.Extra.Count>extraStart)
                {
                    var pk=batchJob.Extra[extraStart];
                    var item=new DebrisItem{Vuid=lp.Vuid,Side="part"};
                    if(pk.Owned)item.Meshes.Add(pk.Mesh);
                    var holder=new GameObject("PD_part_debris_"+lp.Vuid);
                    holder.layer=pk.Layer;
                    var ht=holder.transform;ht.position=pk.Pos;ht.rotation=pk.Rot;ht.localScale=pk.Scale;
                    holder.AddComponent<MeshFilter>().sharedMesh=pk.Mesh;
                    var mr=holder.AddComponent<MeshRenderer>();if(pk.Mats!=null)mr.sharedMaterials=pk.Mats;
                    int subParts=0;
                    for(int i=extraStart+1;i<batchJob.Extra.Count;i++)
                    {
                        var x=batchJob.Extra[i];
                        if(subParts>=6){if(x.Owned)try{Object.Destroy(x.Mesh);}catch{}continue;}
                        var g=new GameObject("PD_part_sub");g.layer=x.Layer;
                        var gt=g.transform;gt.position=x.Pos;gt.rotation=x.Rot;gt.localScale=x.Scale;
                        g.AddComponent<MeshFilter>().sharedMesh=x.Mesh;
                        var smr=g.AddComponent<MeshRenderer>();if(x.Mats!=null)smr.sharedMaterials=x.Mats;
                        gt.SetParent(ht,true);subParts++;
                        if(x.Owned)item.Meshes.Add(x.Mesh);
                    }
                    batchJob.Extra.RemoveRange(extraStart,batchJob.Extra.Count-extraStart);
                    var centre=ht.TransformPoint(pk.Mesh.bounds.center);
                    var blastPos=entry.IsKinetic?centre-entry.PushDir*0.5f:entry.BlastOrHitPos;
                    var debJob=new BreakJob{T=t,Comp=lp.Comp,Vuid=lp.Vuid,Side=PlateSide.Whole,HasBlast=true,BlastPos=blastPos,Ratio=Math.Min(entry.Ratio,2.5)};
                    FinishDebris(debJob,item,ht,pk.Mesh,subParts+1,$"part={lp.CompId}");
                    spawnedDebris=true;
                }
            }
            else if(batchJob.Extra.Count>extraStart)
            {
                for(int i=extraStart;i<batchJob.Extra.Count;i++)if(batchJob.Extra[i].Owned)try{Object.Destroy(batchJob.Extra[i].Mesh);}catch{}
                batchJob.Extra.RemoveRange(extraStart,batchJob.Extra.Count-extraStart);
            }

            bool colRemoved=false;
            if(lp.HasCollider)colRemoved=RemovePartArmourInPlace(t,root,lp.Vuid);
            if(p.LogDamage.Value||p.DetailedLogging.Value)
                Plugin.Info($"[PART] vuid={lp.Vuid} ({(lp.CompId.Length>0?lp.CompId:lp.GoName)}/{lp.Type}) destroyed by {(entry.IsKinetic?"kinetic":"blast")} ({lp.Damage:0.0}/{lp.Strength:0.0}): rends={allHidden} mergedTris={removedTris} debris={spawnedDebris} colliderRemoved={colRemoved}");
        }
        FlushMerged(batchJob);
    }
}
