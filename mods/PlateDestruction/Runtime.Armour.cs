using System.Diagnostics;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using Sprocket.DamageModelling;
using Sprocket.Vehicles;
using Sprocket.Vehicles.PlateStructures;
using UnityEngine;
namespace PlateDestructionP0;

// 0.5.0: Zero-rebuild in-place armour updates (degenerating broken triangles directly inside CompoundStructure.meshes)
// with automatic coalesced RaiseDirtyFlags(DamageModel) fallback when InPlace is unavailable or disabled.
public sealed partial class Runtime
{
    private sealed class ArmourMeshRef
    {
        internal int Index;
        internal Il2CppStructArray<Vector3> Verts=null!;
        internal Il2CppStructArray<int> Tris=null!;
        internal Il2CppStructArray<int>? BvhTris;
        internal Transform? Tf;
    }

    private sealed class DamageMeshIndex
    {
        internal IntPtr ModelPtr;
        internal int StructureHash;
        internal CompoundStructure CS=null!;
        internal readonly List<ArmourMeshRef> Meshes=new();
        internal readonly Dictionary<int,List<(int MeshIdx,int Offset)>> ByVuid=new();
        internal readonly List<TriBackup> MainBackups=new();
        internal readonly List<TriBackup> BvhBackups=new();
        internal bool Valid;
        internal string FailReason="";
    }

    private readonly Dictionary<long,DamageMeshIndex> armourIndices=new();
    private readonly RebuildCoalescer coalescer=new();

    private DamageMeshIndex? GetOrBuildArmourIndex(Tracked t,long root)
    {
        var dm=t.V.DamageModel;if(dm==null)return null;
        var cs=dm.TryCast<CompoundStructure>();if(cs==null)return null;
        int hash=dm.StructureHash;
        if(armourIndices.TryGetValue(root,out var cached)&&cached.ModelPtr==dm.Pointer&&cached.StructureHash==hash)
            return cached;

        var idx=new DamageMeshIndex{ModelPtr=dm.Pointer,StructureHash=hash,CS=cs};
        try
        {
            var objs=cs.objects;
            var meshes=cs.meshes;
            if(objs==null||meshes==null||meshes.Length==0){idx.FailReason="objects or meshes array empty";armourIndices[root]=idx;return idx;}
            int objCount=objs.Length;
            for(int mi=0;mi<meshes.Length;mi++)
            {
                var ms=meshes[mi];
                var verts=ms.vertices;var tris=ms.triangles;var mmap=ms.materialMap;
                if(verts==null||tris==null||mmap==null){idx.FailReason=$"mesh[{mi}] null array";armourIndices[root]=idx;return idx;}
                int triCount=tris.Length/3;
                if(mmap.Length!=triCount){idx.FailReason=$"mesh[{mi}] materialMap {mmap.Length} != tris {triCount}";armourIndices[root]=idx;return idx;}
                var bvhTris=ms.bvhTree?.triangles;
                if(bvhTris!=null&&(bvhTris.Pointer==tris.Pointer||bvhTris.Length!=tris.Length))bvhTris=null;
                idx.Meshes.Add(new ArmourMeshRef{Index=mi,Verts=verts,Tris=tris,BvhTris=bvhTris,Tf=ms.transform});
                for(int ti=0;ti<triCount;ti++)
                {
                    int oi=mmap[ti].objectIndex;
                    if(oi<0||oi>=objCount)continue;
                    int id=objs[oi].ID;
                    if(!idx.ByVuid.TryGetValue(id,out var list)){list=new List<(int,int)>(16);idx.ByVuid[id]=list;}
                    list.Add((mi,ti*3));
                }
            }
            idx.Valid=true;
        }
        catch(Exception e){idx.Valid=false;idx.FailReason=e.Message;}
        armourIndices[root]=idx;
        return idx;
    }

    private static bool DegenerateNative(Il2CppStructArray<int> tris,int offset,int meshIdx,List<TriBackup> backups)
    {
        if(offset<0||offset+2>=tris.Length)return false;
        int a=tris[offset],b=tris[offset+1],c=tris[offset+2];
        if(InPlaceArmour.IsDegenerate(a,b,c))return false;
        backups.Add(new TriBackup(meshIdx,offset,a,b,c));
        tris[offset+1]=a;tris[offset+2]=a;
        return true;
    }

    private bool TryApplyArmourInPlace(
        Tracked t,
        long root,
        int plateVuid,
        PlateStructure? plate,
        SidePlan? plan,
        PlateCells? pc,
        PlateSide brokenSide,
        IReadOnlySet<int>? brokenCells,
        IReadOnlyCollection<int>? attachedVuids,
        bool wholePlate,
        out string note)
    {
        note="";
        var sw=Stopwatch.StartNew();
        var idx=GetOrBuildArmourIndex(t,root);
        if(idx==null){note="no CompoundStructure";return false;}
        if(!idx.Valid){note=idx.FailReason;return false;}
        if(!idx.ByVuid.TryGetValue(plateVuid,out var plateTris)||plateTris.Count==0)
        {
            note=$"plate vuid={plateVuid} has 0 indexed triangles";
            return false;
        }

        int plateDeg=0,attDeg=0;
        var csTf=idx.CS.transform;
        bool sideOnly=!wholePlate&&brokenSide!=PlateSide.Whole&&brokenCells==null&&plan is {PerSide:true,Geo:not null};
        bool cellMode=!wholePlate&&brokenCells!=null&&pc!=null;

        for(int i=0;i<plateTris.Count;i++)
        {
            var (mi,off)=plateTris[i];
            var mref=idx.Meshes[mi];
            int a=mref.Tris[off],b=mref.Tris[off+1],c=mref.Tris[off+2];
            if(InPlaceArmour.IsDegenerate(a,b,c))continue;

            bool drop=wholePlate;
            if(!drop&&(sideOnly||cellMode))
            {
                if(plate==null){note="missing PlateStructure for partial filter";return false;}
                var tf=mref.Tf!=null?mref.Tf:csTf;
                if(tf==null){note="missing mesh transform";return false;}
                if(a<0||b<0||c<0||a>=mref.Verts.Length||b>=mref.Verts.Length||c>=mref.Verts.Length)continue;
                Vector3 va=mref.Verts[a],vb=mref.Verts[b],vc=mref.Verts[c];
                Vector3 cenLocal=new((va.x+vb.x+vc.x)/3f,(va.y+vb.y+vc.y)/3f,(va.z+vb.z+vc.z)/3f);
                Vector3 cenWorld=tf.TransformPoint(cenLocal);
                Vector3 cenPlate=plate.WorldToStructureSpace(cenWorld);
                var p=new V3(cenPlate.x,cenPlate.y,cenPlate.z);
                if(cellMode)
                {
                    int cell=pc!.Plan.CellOfPoint(p);
                    if(cell>=0&&brokenCells!.Contains(cell))drop=true;
                }
                else if(sideOnly)
                {
                    if(plan!.Geo!.Choose(p)==brokenSide)drop=true;
                }
            }
            if(!drop)continue;
            if(DegenerateNative(mref.Tris,off,mi,idx.MainBackups))plateDeg++;
            if(mref.BvhTris!=null)DegenerateNative(mref.BvhTris,off,mi,idx.BvhBackups);
        }

        if(attachedVuids!=null)
        {
            foreach(int av in attachedVuids)
            {
                if(!idx.ByVuid.TryGetValue(av,out var attTris))continue;
                for(int i=0;i<attTris.Count;i++)
                {
                    var (mi,off)=attTris[i];
                    var mref=idx.Meshes[mi];
                    if(DegenerateNative(mref.Tris,off,mi,idx.MainBackups))attDeg++;
                    if(mref.BvhTris!=null)DegenerateNative(mref.BvhTris,off,mi,idx.BvhBackups);
                }
            }
        }

        sw.Stop();
        if(probe!=null)probeReady=true;
        Plugin.Info($"[ARMOUR] InPlace vuid={plateVuid} ({SideName(brokenSide)}): degenerated {plateDeg} plate tris + {attDeg} attached tris in {sw.Elapsed.TotalMilliseconds:0.00} ms");
        return true;
    }

    private void ScheduleArmourUpdate(
        Tracked t,
        long root,
        int vuid,
        PlateStructure? plate,
        SidePlan? plan,
        PlateCells? pc,
        PlateSide brokenSide,
        IReadOnlySet<int>? brokenCells,
        IReadOnlyCollection<int>? attachedVuids,
        bool wholePlate)
    {
        var p=Plugin.Instance;
        if(!p.EnableRouteA.Value)
        {
            Plugin.Info($"[BREAK] vuid={vuid}: removal from the damage model switched off (Switches/RouteA=false); visual only.");
            return;
        }
        if(p.ArmourUpdateMode.Value.Equals("InPlace",StringComparison.OrdinalIgnoreCase))
        {
            if(TryApplyArmourInPlace(t,root,vuid,plate,plan,pc,brokenSide,brokenCells,attachedVuids,wholePlate,out string note))
                return;
            Plugin.Info($"[ARMOUR] InPlace fallback for vuid={vuid} ({note}); queueing coalesced DamageModel rebuild.");
        }
        double windowS=Math.Max(0,p.RebuildCoalesceMs.Value)/1000.0;
        coalescer.Request(root,Time.realtimeSinceStartup,windowS);
        if(wholePlate)probe=(root,vuid,false);
        probeReady=false;
    }

    private void PollCoalescedRebuilds()
    {
        if(coalescer.Count==0||pending!=null)return;
        var ready=coalescer.TakeReady(Time.realtimeSinceStartup,null);
        foreach(long root in ready)
        {
            Tracked? found=null;
            foreach(var tr in tracked.Values){if(tr.Root==root){found=tr;break;}}
            if(found==null||found.V==null||found.V.WasCollected)continue;
            pending=new Measure{T=found,Vuid=0,On=true,Clock=Stopwatch.StartNew(),StartFrame=Time.frameCount,HashBefore=found.Model.StructureHash,
                ModelBefore=found.V.DamageModel?.Pointer??IntPtr.Zero,SkipBefore=Patches.PlateSkipped+Patches.ModelSkipped,BuiltBefore=Patches.PlateBuilt+Patches.ModelBuilt};
            Plugin.Info($"[ROUTEA] coalesced DamageModel rebuild on {found.Name} (builder calls={BuilderPatches.Calls}, skipped={BuilderPatches.Skipped}, filtered={BuilderPatches.Filtered})...");
            found.V.RaiseDirtyFlags(VehicleDirtyFlags.DamageModel);
            break; // one pending Measure at a time; remaining roots run next frame after this finishes
        }
    }

    private void RestoreInPlaceArmour(Func<long,bool> pickRoot)
    {
        foreach(var root in armourIndices.Keys.Where(pickRoot).ToArray())
        {
            var idx=armourIndices[root];
            try
            {
                if(idx.CS!=null&&!idx.CS.WasCollected)
                {
                    foreach(var b in idx.MainBackups)
                    {
                        if(b.MeshIndex<idx.Meshes.Count)
                        {
                            var tris=idx.Meshes[b.MeshIndex].Tris;
                            if(tris!=null&&b.Offset+2<tris.Length){tris[b.Offset]=b.A;tris[b.Offset+1]=b.B;tris[b.Offset+2]=b.C;}
                        }
                    }
                    foreach(var b in idx.BvhBackups)
                    {
                        if(b.MeshIndex<idx.Meshes.Count)
                        {
                            var tris=idx.Meshes[b.MeshIndex].BvhTris;
                            if(tris!=null&&b.Offset+2<tris.Length){tris[b.Offset]=b.A;tris[b.Offset+1]=b.B;tris[b.Offset+2]=b.C;}
                        }
                    }
                }
            }
            catch{}
            armourIndices.Remove(root);
        }
    }

    private void ScheduleArmourHoleUpdate(Tracked t,long root,int vuid,PlateStructure plate,PlateSide side,V3 holeCenterPlate,double holeRadiusM)
    {
        var p=Plugin.Instance;
        if(!p.EnableRouteA.Value)return;
        if(p.ArmourUpdateMode.Value.Equals("InPlace",StringComparison.OrdinalIgnoreCase))
        {
            var sw=Stopwatch.StartNew();
            var idx=GetOrBuildArmourIndex(t,root);
            if(idx!=null&&idx.Valid&&idx.ByVuid.TryGetValue(vuid,out var plateTris)&&plateTris.Count>0)
            {
                var csTf=idx.CS.transform;
                double reachSq=(holeRadiusM*1.15)*(holeRadiusM*1.15);
                bool sideFilter=side!=PlateSide.Whole&&Plans.TryGetValue((root,vuid),out var sp)&&sp is {PerSide:true,Geo:not null};
                SideGeometry? geo=sideFilter?Plans[(root,vuid)].Geo:null;
                int plateDeg=0;
                for(int i=0;i<plateTris.Count;i++)
                {
                    var (mi,off)=plateTris[i];
                    var mref=idx.Meshes[mi];
                    int a=mref.Tris[off],b=mref.Tris[off+1],c=mref.Tris[off+2];
                    if(InPlaceArmour.IsDegenerate(a,b,c))continue;
                    if(a<0||b<0||c<0||a>=mref.Verts.Length||b>=mref.Verts.Length||c>=mref.Verts.Length)continue;
                    var tf=mref.Tf!=null?mref.Tf:csTf;
                    if(tf==null)continue;
                    Vector3 paW=tf.TransformPoint(mref.Verts[a]),pbW=tf.TransformPoint(mref.Verts[b]),pcW=tf.TransformPoint(mref.Verts[c]);
                    Vector3 paL=plate.WorldToStructureSpace(paW),pbL=plate.WorldToStructureSpace(pbW),pcL=plate.WorldToStructureSpace(pcW);
                    V3 pa=new(paL.x,paL.y,paL.z),pb=new(pbL.x,pbL.y,pbL.z),pc=new(pcL.x,pcL.y,pcL.z);
                    if(geo!=null)
                    {
                        var cen=new V3((pa.X+pb.X+pc.X)/3f,(pa.Y+pb.Y+pc.Y)/3f,(pa.Z+pb.Z+pc.Z)/3f);
                        if(geo.Choose(cen)!=side)continue;
                    }
                    var q=MeshQuery.ClosestOnTriangle(holeCenterPlate,pa,pb,pc);
                    var dv=VM.Sub(q,holeCenterPlate);
                    if(VM.Dot(dv,dv)>reachSq)continue;
                    if(DegenerateNative(mref.Tris,off,mi,idx.MainBackups))plateDeg++;
                    if(mref.BvhTris!=null)DegenerateNative(mref.BvhTris,off,mi,idx.BvhBackups);
                }
                sw.Stop();
                if(probe!=null)probeReady=true;
                Plugin.Info($"[ARMOUR] InPlace hole vuid={vuid} ({SideName(side)}): degenerated {plateDeg} plate tris in {sw.Elapsed.TotalMilliseconds:0.00} ms");
                return;
            }
        }
        double windowS=Math.Max(0,p.RebuildCoalesceMs.Value)/1000.0;
        coalescer.Request(root,Time.realtimeSinceStartup,windowS);
    }
}
