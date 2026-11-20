using System.Collections.Concurrent;
using System.Text;
using HarmonyLib;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using Sprocket.Vehicles;
using Sprocket.Vehicles.Colliders;
using Sprocket.Vehicles.DamageModels;
using Sprocket.Vehicles.PlateStructures;
using UnityEngine;
namespace PlateDestructionP0;

/// <summary>The destructible plate whose BuildDamageModelColliders is running right now (set by the plate prefix in Patches.cs, cleared by its finalizer).</summary>
internal sealed class PlateCtx{internal long Root;internal int Vuid;}

// Armour of one half of a mirrored plate. The concrete class behind IVehicleComponentDamageModelBuilder is
// Sprocket.Vehicles.DamageModels.VehicleDamageModelGeneration.DamageModelStructureBuilder (the only implementation that builds the vehicle damage model).
// Prefixes, patched in their own Harmony instance so a failure here never disables the rest of the mod:
//   AddMesh (6 arguments, own native address, verified by checks\icf-check.py) and AddMesh (9 arguments, 0.3.0, thickness + markers; its address is shared ONLY with its
//   interface-explicit twin, which is the same logical method): both log every call of a destructible plate and, for a plate with one broken half, skip the call
//   (separate call per half) or replace the triangle list by a filtered copy (combined mesh). Vertices and thickness are never touched.
//   AddBox: log only.
// Never a ref to a non-blittable struct: only Matrix4x4 (blittable) is taken by ref; the ref DamageModelParameters is left out of the prefix signatures.
internal static class BuilderPatches
{
    [ThreadStatic] internal static PlateCtx? Ctx;
    internal static bool Installed;
    internal static readonly ConcurrentDictionary<(long,int),SidePlan> Plans=new();
    /// <summary>0.4.1: cells (broken state and plan) of every adopted destructible plate; the armour prefix reads Broken from here.</summary>
    internal static readonly ConcurrentDictionary<(long,int),PlateCells> Cells=new();
    internal static int Calls,Skipped,Filtered,Logged;
    private const int MaxLogLines=400;

    internal static void Enter(PlateStructure plate)
    {
        Ctx=null;
        if(!Installed)return;
        var root=plate.VehicleRoot;if(root==null)return;
        if(!DestructibleFlag.Has(plate.Blueprint?.Description))return;
        Ctx=new PlateCtx{Root=root.Pointer.ToInt64(),Vuid=plate.VUID.Value};
    }
    private static void Log(string s){if(Logged<MaxLogLines){Logged++;Plugin.Info(s);}else if(Logged==MaxLogLines){Logged++;Plugin.Info("[ARMOUR] log limit reached ("+MaxLogLines+" lines); further calls are still processed.");}}
    internal static float Det3(Matrix4x4 m)=>SideOps.ToMat4(m).Det3();

    // ICF: DamageModelStructureBuilder::AddMesh/6
    [HarmonyPrefix,HarmonyPatch(typeof(VehicleDamageModelGeneration.DamageModelStructureBuilder),nameof(VehicleDamageModelGeneration.DamageModelStructureBuilder.AddMesh),new[]{typeof(VUID),typeof(Matrix4x4),typeof(int),typeof(Il2CppStructArray<Vector3>),typeof(Il2CppStructArray<int>),typeof(DamageModelParameters)},new[]{ArgumentType.Normal,ArgumentType.Ref,ArgumentType.Normal,ArgumentType.Normal,ArgumentType.Normal,ArgumentType.Ref})]
    private static bool AddMesh6(VUID __0,ref Matrix4x4 __1,int __2,Il2CppStructArray<Vector3> __3,ref Il2CppStructArray<int> __4)
    {
        Il2CppStructArray<uint>? mk=null;Il2CppStructArray<byte>? mt=null;
        return Process("AddMesh/6",__0,__1,__2,__3,ref __4,ref mk,ref mt,-1);
    }

    // ICF-TWIN: DamageModelStructureBuilder::AddMesh/9
    // The 9-argument overload (thickness + markers) is the one the plates really use (0.2.0 test: AddMesh/6 got no calls). Its native address is shared ONLY with its own
    // explicit-interface twin IVehicleComponentDamageModelBuilder.AddMesh (the same logical method, see icf-check.txt); any other sharer makes the gate fail.
    // thickness is per vertex and is not touched (vertices are never touched). meshMarkers/meshMarkersTypes are filtered together with the triangles only when their length
    // proves they are per triangle; per-vertex, independent (matching neither count) or absent markers stay as they are; only an inconsistent mixture means: no triangle filtering (pass, logged).
    [HarmonyPrefix,HarmonyPatch(typeof(VehicleDamageModelGeneration.DamageModelStructureBuilder),nameof(VehicleDamageModelGeneration.DamageModelStructureBuilder.AddMesh),new[]{typeof(VUID),typeof(Matrix4x4),typeof(int),typeof(Il2CppStructArray<Vector3>),typeof(Il2CppStructArray<short>),typeof(Il2CppStructArray<int>),typeof(DamageModelParameters),typeof(Il2CppStructArray<uint>),typeof(Il2CppStructArray<byte>)},new[]{ArgumentType.Normal,ArgumentType.Ref,ArgumentType.Normal,ArgumentType.Normal,ArgumentType.Normal,ArgumentType.Normal,ArgumentType.Ref,ArgumentType.Normal,ArgumentType.Normal})]
    private static bool AddMesh9(VUID __0,ref Matrix4x4 __1,int __2,Il2CppStructArray<Vector3> __3,Il2CppStructArray<short> __4,ref Il2CppStructArray<int> __5,ref Il2CppStructArray<uint>? __7,ref Il2CppStructArray<byte>? __8)
    {
        int thick=__4==null?0:__4.Length;
        return Process("AddMesh/9",__0,__1,__2,__3,ref __5,ref __7,ref __8,thick);
    }

    // Shared decision for both overloads. thicknessLen -1 = the overload has no thickness/markers.
    // 0.4.1: besides the 0.3.1 side rule, the triangles of broken CELLS are dropped (same centroid rule as the render meshes: CellPlan.CellOfTriangle). The result is the intersection of both rules.
    private static bool Process(string tag,VUID id,Matrix4x4 m,int entity,Il2CppStructArray<Vector3> vertices,ref Il2CppStructArray<int> triangles,ref Il2CppStructArray<uint>? markers,ref Il2CppStructArray<byte>? markerTypes,int thicknessLen)
    {
        try
        {
            var ctx=Ctx;if(ctx==null)return true;
            Calls++;
            int vuid=id.Value;
            Plans.TryGetValue((ctx.Root,ctx.Vuid),out var plan);
            Cells.TryGetValue((ctx.Root,ctx.Vuid),out var pc);
            bool bo=Patches.State.IsSideBroken(ctx.Root,ctx.Vuid,PlateSide.Original),bm=Patches.State.IsSideBroken(ctx.Root,ctx.Vuid,PlateSide.Mirror);
            var broken=pc?.Broken;bool cellDrop=pc!=null&&broken!=null&&broken.Count>0;
            bool sideLogic=plan is {PerSide:true,Geo:not null};
            bool sideDrop=sideLogic&&(bo||bm);
            int vc=vertices==null?0:vertices.Length,tc=triangles==null?0:triangles.Length;
            if(vertices==null||triangles==null||vc==0||tc==0||(!sideDrop&&!cellDrop))
            {
                if(Plugin.Instance.DetailedLogging.Value)
                {
                    int sign0=Mat4.Sign(Det3(m));
                    int mk0=markers==null?0:markers.Length,mt0=markerTypes==null?0:markerTypes.Length;
                    var ly0=thicknessLen<0?MarkerLayout.None:MarkerFilter.Classify(mk0,mt0,vc,tc);
                    var sb0=new StringBuilder($"[ARMOUR] {tag} vuid={vuid}{(vuid!=ctx.Vuid?" (plate ctx "+ctx.Vuid+")":"")} entity={entity} detSign={(sign0<0?"-1":"+1")} vertices={vc} triangles={tc} ({tc/3} tris)");
                    if(thicknessLen>=0)sb0.Append($" thickness={thicknessLen} markers={mk0} markerTypes={mt0} markerLayout={ly0}");
                    else sb0.Append(" thickness=none markers=none");
                    sb0.Append($" brokenOriginal={bo} brokenMirror={bm} brokenCells={(cellDrop?broken!.Count:0)}");
                    sb0.Append(plan==null&&!cellDrop?" action=pass (no plan yet)":plan!=null&&!plan.Mirrored&&!cellDrop?" action=pass (plate not mirrored)":plan!=null&&!plan.PerSide&&!cellDrop?" action=pass (per-side not possible: "+plan.Why+")":" action=pass");
                    Log(sb0.ToString());
                }
                return true;
            }
            int sign=Mat4.Sign(Det3(m));
            int mkLen=markers==null?0:markers.Length,mtLen=markerTypes==null?0:markerTypes.Length;
            var layout=thicknessLen<0?MarkerLayout.None:MarkerFilter.Classify(mkLen,mtLen,vc,tc);
            var sb=new StringBuilder($"[ARMOUR] {tag} vuid={vuid}{(vuid!=ctx.Vuid?" (plate ctx "+ctx.Vuid+")":"")} entity={entity} detSign={(sign<0?"-1":"+1")} vertices={vc} triangles={tc} ({tc/3} tris)");
            if(thicknessLen>=0)sb.Append($" thickness={thicknessLen} markers={mkLen} markerTypes={mtLen} markerLayout={layout}");
            else sb.Append(" thickness=none markers=none");
            sb.Append($" brokenOriginal={bo} brokenMirror={bm} brokenCells={(cellDrop?broken!.Count:0)}");
            var geo=plan?.Geo;
            var verts=SideOps.ToV3(vertices);
            var tris=new int[tc];for(int i=0;i<tc;i++)tris[i]=triangles[i];
            // vertex box in the array's own space, to prove it is the structure space of the plan
            float x0=float.MaxValue,y0=float.MaxValue,z0=float.MaxValue,x1=float.MinValue,y1=float.MinValue,z1=float.MinValue;
            foreach(var v in verts){x0=Math.Min(x0,v.X);y0=Math.Min(y0,v.Y);z0=Math.Min(z0,v.Z);x1=Math.Max(x1,v.X);y1=Math.Max(y1,v.Y);z1=Math.Max(z1,v.Z);}
            float tol=Math.Max(0.05f,0.05f*Math.Max(x1-x0,Math.Max(y1-y0,z1-z0)));
            bool covers=sideLogic?geo!.Covers(new V3(x0,y0,z0),new V3(x1,y1,z1),tol):pc!.Plan.Covers(new V3(x0,y0,z0),new V3(x1,y1,z1),tol);
            var cnt=sideLogic&&covers?SideTriangles.Count(verts,tris,geo!):(0,0);
            sb.Append($" vertexBox=[{SideOps.F(x0)},{SideOps.F(y0)},{SideOps.F(z0)}..{SideOps.F(x1)},{SideOps.F(y1)},{SideOps.F(z1)}] inStructureSpace={covers}");
            if(sideLogic)sb.Append($" trianglesOnOriginal={cnt.Item1} onMirror={cnt.Item2}");
            bool mirrorHanded=sideLogic&&sign!=plan!.PlateDetSign;
            List<int>? keep=null;string dropped="";
            if(sideDrop)
            {
                dropped=bo&&bm?"both":bo?"original":"mirror";
                if(!mirrorHanded&&!covers){sb.Append(" action=pass (WARNING: vertex space does not match the structure space; the triangle side cannot be decided, armour of the broken half stays)");Log(sb.ToString());return true;}
                var d=ArmourRule.Decide(mirrorHanded,cnt.Item1,cnt.Item2,bo,bm);
                if(d.Action==ArmourAction.Skip)
                {
                    Skipped++;sb.Append($" side={dropped} tris {tc/3}->0 action=SKIP call ({(mirrorHanded?"separate mirrored call":"all triangles on the broken half")})");Log(sb.ToString());return false;
                }
                if(d.Action==ArmourAction.Filter)
                {
                    if(layout==MarkerLayout.Unknown){sb.Append($" side={dropped} action=pass (WARNING: marker arrays are inconsistent (one per triangle, the other not), triangles are not filtered; armour of the broken half stays)");Log(sb.ToString());return true;}
                    keep=SideTriangles.KeepIndices(verts,tris,geo!,d.Keep);
                    sb.Append($" side={dropped} keep={d.Keep}");
                }
            }
            if(cellDrop)
            {
                if(mirrorHanded)sb.Append(" cells=not applied to a separate mirrored call (its cell frame is not proven)");
                else if(!covers)sb.Append(" cells=NOT applied (WARNING: vertex space does not match the structure space)");
                else if(layout==MarkerLayout.Unknown)sb.Append(" cells=NOT applied (WARNING: marker arrays are inconsistent)");
                else
                {
                    var before=keep?.Count??-1;
                    keep=ArmourCells.Keep(verts,tris,pc!.Plan,broken!,keep);
                    sb.Append($" cells: {(before<0?tc/3:before)}->{keep.Count} tris");
                }
            }
            if(keep==null){sb.Append(" action=pass");Log(sb.ToString());return true;}
            if(keep.Count==0){Skipped++;sb.Append($" tris {tc/3}->0 action=SKIP call (the filters left no triangle)");Log(sb.ToString());return false;}
            if(keep.Count==tc/3){sb.Append(" action=pass (nothing to drop in this call)");Log(sb.ToString());return true;}
            var kept=SideTriangles.Gather(tris,keep);
            string mk="";
            if(layout==MarkerLayout.PerTriangle)
            {
                if(mkLen>0&&markers!=null){var src=new uint[mkLen];for(int i=0;i<mkLen;i++)src[i]=markers[i];markers=new Il2CppStructArray<uint>(MarkerFilter.Pick(src,keep));}
                if(mtLen>0&&markerTypes!=null){var src=new byte[mtLen];for(int i=0;i<mtLen;i++)src[i]=markerTypes[i];markerTypes=new Il2CppStructArray<byte>(MarkerFilter.Pick(src,keep));}
                mk=$" markers {mkLen}->{(mkLen>0?keep.Count:0)} markerTypes {mtLen}->{(mtLen>0?keep.Count:0)} (per triangle, filtered with the triangles)";
            }
            else if(layout==MarkerLayout.PerVertex)mk=" markers per vertex: kept as they are (vertices unchanged)";
            else if(layout==MarkerLayout.Independent)mk=$" markers independent ({mkLen} markers, {mtLen} types): kept unchanged, only the triangles are filtered";
            triangles=new Il2CppStructArray<int>(kept);Filtered++;
            sb.Append($" tris {tc/3}->{keep.Count} action=FILTER (vertices unchanged){mk}");Log(sb.ToString());return true;
        }
        catch(Exception e){Plugin.Warn(tag+" prefix: "+e.Message);}
        return true;
    }

    // ICF: DamageModelStructureBuilder::AddBox/7
    [HarmonyPrefix,HarmonyPatch(typeof(VehicleDamageModelGeneration.DamageModelStructureBuilder),nameof(VehicleDamageModelGeneration.DamageModelStructureBuilder.AddBox),new[]{typeof(VUID),typeof(Matrix4x4),typeof(int),typeof(Vector3),typeof(Quaternion),typeof(Vector3),typeof(DamageModelParameters)},new[]{ArgumentType.Normal,ArgumentType.Ref,ArgumentType.Normal,ArgumentType.Normal,ArgumentType.Normal,ArgumentType.Normal,ArgumentType.Ref})]
    private static void AddBox7(VUID __0,ref Matrix4x4 __1,int __2,Vector3 __3,Vector3 __5)
    {
        try
        {
            var ctx=Ctx;if(ctx==null)return;
            int sign=Mat4.Sign(Det3(__1));
            Log($"[ARMOUR] AddBox vuid={__0.Value}{(__0.Value!=ctx.Vuid?" (plate ctx "+ctx.Vuid+")":"")} entity={__2} detSign={(sign<0?"-1":"+1")} position=({SideOps.F(__3.x)},{SideOps.F(__3.y)},{SideOps.F(__3.z)}) size=({SideOps.F(__5.x)},{SideOps.F(__5.y)},{SideOps.F(__5.z)}) action=log only");
        }
        catch(Exception e){Plugin.Warn("AddBox prefix: "+e.Message);}
    }
}
