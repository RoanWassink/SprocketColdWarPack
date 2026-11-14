using System.Numerics;
namespace SprocketExpandedBallistics;

/// <summary>
/// 0.22.24: the snapshot gate against a simulated dirty or half-deleted mesh (non-cyclic ring, hole), the quiet period, the To back plane
/// requirement per involved face, the thickness-direction plan with undo, the stored-direction check and the limits on corner deltas.
/// </summary>
internal static class Fix02224Tests
{
    // ---- simulated native mesh ----
    private sealed class FLoop:IRawLoop
    {
        public long Key{get;init;}public long VertexKey{get;init;}public Vector3 Position{get;set;}public ushort Thickness{get;set;}public bool AlongFaceNormal{get;set;}=true;
        public FLoop? NextLoop;public IRawLoop? Next=>NextLoop;
    }
    private sealed class FFace:IRawFace
    {
        public int Errors{get;set;}public int VertexCount{get;set;}public bool Selected{get;set;}public bool Culled{get;set;}public Vector3 Normal{get;set;}=Vector3.UnitY;
        public FLoop? First;public IRawLoop? FirstLoop=>First;public bool Throws;
    }
    private sealed class FMesh:IRawMesh
    {
        public long Pointer{get;set;}=0x1000;public bool Released{get;set;}public int DirtyFlags{get;set;}public int IndexDirtyFlags{get;set;}
        public int FaceCount{get;set;}public int FaceListCount{get;set;}public int LoopCount{get;set;}
        public List<FFace?> Faces=new();public int FaceReads;
        public IRawFace? Face(int index){FaceReads++;var f=Faces[index];if(f!=null&&f.Throws)throw new InvalidOperationException("native fault stand-in");return f;}
    }
    private static long key=1;
    // A closed ring of n corners on a unit grid; vertex keys start at vertexBase so two faces can share corners.
    private static FFace Quad(float x,float z,ushort t,long vertexBase=100,bool closed=true,float y=0)
    {
        var pos=new[]{new Vector3(x,y,z),new Vector3(x+1,y,z),new Vector3(x+1,y,z+1),new Vector3(x,y,z+1)};
        var loops=pos.Select((p,i)=>new FLoop{Key=key++,VertexKey=vertexBase+i,Position=p,Thickness=t}).ToArray();
        for(int i=0;i<4;i++)loops[i].NextLoop=i<3?loops[i+1]:closed?loops[0]:null;
        return new FFace{VertexCount=4,First=loops[0]};
    }
    private static FMesh Mesh(params FFace[] faces)=>new(){Faces=faces.Cast<FFace?>().ToList(),FaceCount=faces.Length,FaceListCount=faces.Length,LoopCount=faces.Sum(f=>f.VertexCount)};
    private static bool Read(IRawMesh mesh,out PlateMeshSnapshot? snap,out string reason)=>PlateMeshReader.TryRead(mesh,out snap,out reason);

    internal static void Run(Action<bool,string> check)
    {
        int count=0;var assert=check;check=(ok,text)=>{assert(ok,"fix02224: "+text);count++;};
        Snapshot(check);Quiet(check);BackPlaneFaces(check);ThickenPlan(check);StoredDirection(check);Limits(check);Signature(check);Tapered(check);Source(check);
        Console.WriteLine($"0.22.24 checks PASS: {count} checks.");
    }

    // ---- 1: the snapshot helper against a dirty, half-deleted or damaged mesh ----
    private static void Snapshot(Action<bool,string> check)
    {
        var mesh=Mesh(Quad(0,0,20),Quad(1,0,30,vertexBase:102));
        check(Read(mesh,out var s,out var why)&&s!=null,"a clean mesh is read ("+why+")");
        check(s!=null&&s.FaceCount==2&&s.LoopCount==8&&s.Faces.Length==2&&s.Faces[1].Thickness.All(t=>t==30),"counts and thickness are copied");
        check(s!=null&&s.Vertices.Length==6,"two quads sharing an edge have 6 distinct vertices (vertex keys, not loops)");
        check(s!=null&&s.Signature.StartsWith("2:8:"),"the signature starts with the counts");
        var sig=s!.Signature;
        ((FLoop)mesh.Faces[0]!.First!).Thickness=21;Read(mesh,out var changed,out _);
        check(changed!=null&&changed.Signature!=sig,"the checksum sees a thickness change that keeps the counts");
        ((FLoop)mesh.Faces[0]!.First!).Thickness=20;
        ((FLoop)mesh.Faces[0]!.First!).AlongFaceNormal=false;Read(mesh,out changed,out _);
        check(changed!=null&&changed.Signature!=sig&&!changed.Faces[0].Along[0],"the checksum sees a thickness direction change");
        ((FLoop)mesh.Faces[0]!.First!).AlongFaceNormal=true;

        Reject(check,m=>m.DirtyFlags=2,"dirty","dirty flags: no read");
        // 0.22.27: index dirty flags only mark a stale Element.index (the read is by list position); a clean mesh with them is read.
        var indexed=Mesh(Quad(0,0,20),Quad(1,0,30,vertexBase:102));indexed.IndexDirtyFlags=4;
        check(Read(indexed,out var withIndex,out why)&&withIndex!=null&&withIndex.IndexDirtyFlags==4,"index dirty flags alone: read (0.22.27), the flags are carried on the snapshot ("+why+")");
        Reject(check,m=>m.Released=true,"released","released mesh: no read");
        Reject(check,m=>m.FaceListCount=1,"faces list","faces list differs from FaceCount (half-finished delete): no read");
        Reject(check,m=>m.FaceCount=0,"empty","empty mesh: no read");
        Reject(check,m=>m.Faces[1]=null,"null","a null face (deleted, pooled): no read");
        Reject(check,m=>m.Faces[1]!.Errors=1<<3,"validate","a face that does not validate: no read");
        Reject(check,m=>m.Faces[0]!.First=null,"first loop","a face without a first loop: no read");
        Reject(check,m=>m.Faces[0]!.VertexCount=2,"corners","a face of two corners: no read");
        Reject(check,m=>m.Faces[0]!.VertexCount=PlateMeshReader.MaxCorners+1,"corners","a ring longer than the bound: no read");
        Reject(check,m=>m.Faces[0]!.Throws=true,"read failed","a native fault stand-in is caught inside the helper (no exception leaves it)");

        // non-cyclic ring: the last corner points at nothing (a hole ring that was cut loose) while vertexCount still says 4
        var broken=Mesh(Quad(0,0,20,closed:false));
        check(!Read(broken,out var none,out why)&&none==null&&why.Contains("ring does not close"),"a non-cyclic ring stops the walk without running off it ("+why+")");
        // a ring that closes after 3 corners while vertexCount says 5: the face has a hole boundary behind its outer ring
        var holed=Quad(0,0,20);holed.VertexCount=7;
        check(Read(Mesh(holed),out var hole,out why)&&hole!=null&&hole.Faces[0].VertexCount==4&&hole.Faces[0].HoleLoops==3,"a face with a hole boundary reads its outer ring and counts the hole loops ("+why+")");
        var nan=Mesh(Quad(0,0,20));((FLoop)nan.Faces[0]!.First!.NextLoop!).NextLoop=new FLoop{Key=key++,VertexKey=7,Position=new Vector3(float.NaN,0,0),Thickness=1,NextLoop=nan.Faces[0]!.First};
        check(!Read(nan,out _,out why),"a corner with a NaN position is refused ("+why+")");
        // a ring shorter than three: face of vertexCount 4 whose ring closes after 2
        var tiny=Quad(0,0,20);((FLoop)tiny.First!.NextLoop!).NextLoop=tiny.First;
        check(!Read(Mesh(tiny),out _,out why)&&why.Contains("ring has 2"),"a ring that closes after two corners is refused ("+why+")");
    }
    private static void Reject(Action<bool,string> check,Action<FMesh> damage,string reasonPart,string text)
    {
        var mesh=Mesh(Quad(0,0,20),Quad(1,0,30,vertexBase:102));damage(mesh);
        var ok=Read(mesh,out var s,out var reason);
        check(!ok&&s==null&&reason.Contains(reasonPart,StringComparison.OrdinalIgnoreCase),text+" ("+reason+")");
    }

    // ---- 2: quiet period, mouse button, cache ----
    private static void Quiet(Action<bool,string> check)
    {
        float now=10;bool mouse=false;var gate=new PlateMeshGate(()=>now,()=>mouse);
        var mesh=Mesh(Quad(0,0,20),Quad(1,0,30,vertexBase:102));
        var first=gate.Get(mesh,"inspector",SnapshotUse.Fresh,out _);
        check(first!=null&&gate.HasSnapshot(mesh.Pointer),"outside any edit a Fresh read works and fills the cache");
        var reads=mesh.FaceReads;
        var again=gate.Get(mesh,"inspector",SnapshotUse.Display,out _);
        check(ReferenceEquals(first,again)&&mesh.FaceReads==reads,"a second read with the same counts is a cache hit: the native faces are not walked again");
        now+=PlateMeshGate.MaxAgeSeconds+.1f;
        first=gate.Get(mesh,"inspector",SnapshotUse.Display,out _);
        check(mesh.FaceReads>reads,"a cached snapshot older than a second is read again (a thickness drag keeps the counts)");

        reads=mesh.FaceReads;
        gate.NoteModified(mesh.Pointer);
        check(gate.Quiet&&gate.Busy,"OnAssignedMeshModified starts the quiet period");
        mesh.DirtyFlags=1;mesh.FaceListCount=3; // the native delete is half way
        var during=gate.Get(mesh,"composer target",SnapshotUse.Display,out var reason);
        check(during!=null&&ReferenceEquals(during,first)&&reason=="quiet period"&&mesh.FaceReads==reads,"in the quiet period a display reader gets the last good snapshot and the mesh is not touched");
        check(gate.Get(mesh,"apply",SnapshotUse.Fresh,out _)==null&&mesh.FaceReads==reads,"in the quiet period a Fresh reader (Apply) gets nothing and the mesh is not touched");
        var skipped=gate.TakeSkipped();
        check(skipped.SequenceEqual(new[]{"apply","composer target"}),"the skipped readers are recorded for the edit log ("+string.Join(",",skipped)+")");
        check(gate.TakeSkipped().Length==0,"the record is taken once");

        now+=.49f;
        check(gate.Busy,"0.49 s after the edit it is still quiet");
        now+=.02f;
        check(!gate.Busy&&!gate.Quiet,"after 0.5 s the quiet period is over");
        // still dirty: display falls back to the old snapshot, Fresh gets nothing
        reads=mesh.FaceReads;
        check(ReferenceEquals(gate.Get(mesh,"inspector",SnapshotUse.Display,out reason),first)&&reason.Contains("dirty"),"a mesh that is still dirty after the quiet period: display gets the last good snapshot ("+reason+")");
        check(gate.Get(mesh,"apply",SnapshotUse.Fresh,out _)==null,"a mesh that is still dirty: Apply gets nothing");
        // the edit finished: topology changed (a face was deleted), the stale snapshot must not be returned
        mesh.DirtyFlags=0;mesh.FaceListCount=1;mesh.FaceCount=1;mesh.LoopCount=4;mesh.Faces.RemoveAt(1);
        var fresh=gate.Get(mesh,"apply",SnapshotUse.Fresh,out _);
        check(fresh!=null&&fresh.FaceCount==1&&!ReferenceEquals(fresh,first),"after the edit a Fresh read returns the new one-face topology, never the stale two-face snapshot");

        mouse=true;var held=gate.Get(mesh,"inspector",SnapshotUse.Display,out reason);
        check(gate.Busy&&reason=="mouse button held"&&ReferenceEquals(held,fresh),"a held mouse button in the designer is busy: the cache is used");
        check(gate.Get(mesh,"apply",SnapshotUse.Fresh,out _)==null,"a held mouse button: Apply gets nothing");
        mouse=false;
        check(gate.Get(mesh,"apply",SnapshotUse.Fresh,out _)!=null,"mouse released: Apply reads again");
        // a stale entry is never trusted as a hit, even with equal counts
        gate.NoteModified(mesh.Pointer);now+=1;
        reads=mesh.FaceReads;gate.Get(mesh,"inspector",SnapshotUse.Display,out _);
        check(mesh.FaceReads>reads,"an entry marked stale by an edit is read again once it is quiet, even with equal counts");
        // a read that throws never leaves the gate and never poisons it
        var bad=Mesh(Quad(0,0,20));bad.Pointer=0x2000;bad.Faces[0]!.Throws=true;
        check(gate.Get(bad,"inspector",SnapshotUse.Display,out reason)==null&&reason.Contains("read failed"),"a throwing reader result is null with a reason, not an exception ("+reason+")");
        gate.Forget(mesh.Pointer);check(!gate.HasSnapshot(mesh.Pointer),"Forget drops the cache of a mesh");
    }

    // ---- 3: To back plane asks AlongFaceNormal only of the faces that take part ----
    private static void BackPlaneFaces(Action<bool,string> check)
    {
        // face 0 front, face 1 side wall; the wall is the only face that is not Along face normal
        var along=new List<bool[]>{new[]{true,true,true,true},new[]{false,false,true,true},new[]{true,true,true,true}};
        string Refusal(bool[]? selected,ThicknessMode mode)
        {
            for(int f=0;f<along.Count;f++)for(int j=0;j<along[f].Length;j++)
            {
                var only=CompositeThicknessPlan.SelectedCount(selected,along.Count)>0;
                try{BackplaneThickness.RequireFaceNormal(mode,!only||selected![f],along[f][j],f,j);}
                catch(InvalidOperationException ex){return ex.Message;}
            }
            return "";
        }
        check(Refusal(new[]{true,false,true},ThicknessMode.ToBackPlane)=="","a face that does not take part may keep its direction: selecting only the front faces is no refusal");
        var all=Refusal(null,ThicknessMode.ToBackPlane);
        check(all.Contains("face 2, corner 1")&&all.Contains("AlongFaceNormal"),"without a selection the wall counts: refusal names its face and corner ("+all+")");
        check(Refusal(new[]{false,true,false},ThicknessMode.ToBackPlane).Contains("face 2"),"a selected wall is refused by name");
        check(Refusal(null,ThicknessMode.Uniform)=="","Uniform never asks for a direction");
        // and the 0.22.24 route: the mod sets it itself, per involved face
        var plan=ThickenModePlan.Create(ThicknessMode.ToBackPlane,along,new[]{true,true,false});
        check(plan.FacesChanged==1&&plan.CornersChanged==2&&plan.Writes[1][0]&&plan.Writes[1][1]&&!plan.Writes[1][2]&&!plan.Writes[0].Any(w=>w)&&!plan.Writes[2].Any(w=>w),"only the selected wall's two spiky corners are written");
        var every=ThickenModePlan.Create(ThicknessMode.ToBackPlane,along,null);
        check(every.FacesChanged==1&&every.CornersChanged==2,"without a selection every face takes part, and only the corners that are not Along face normal are written");
        check(ThickenModePlan.Create(ThicknessMode.ToBackPlane,along,new[]{true,false,true}).Any==false,"a selection that leaves the wall out writes no direction at all");
    }

    // ---- 4: the thickness-direction plan and its undo ----
    private static void ThickenPlan(Action<bool,string> check)
    {
        var along=new List<bool[]>{new[]{true,true,true,true},new[]{false,true,false,true}};
        var plan=ThickenModePlan.Create(ThicknessMode.ToBackPlane,along,null);
        check(plan.NewAlong.All(f=>f.All(a=>a)),"after Execute every treated corner is Along face normal");
        check(plan.After(false)[1].SequenceEqual(new[]{false,true,false,true})&&plan.After(true)[1].All(a=>a),"After(false) is the old per-corner directions, After(true) the new ones");
        check(plan.OldAlong[1][0]==false&&along[1][0]==false,"the plan holds copies: the old arrays are not changed by the plan");
        var thick=new ushort[][]{new ushort[]{40,40,40,40},new ushort[]{10,200,10,200}};
        var thicknessPlan=new CompositeThicknessPlan("ebCassette_test",new ushort[][]{new ushort[]{40,40,40,40},new ushort[]{40,40,40,40}});
        var snapshot=new CompositeUndoSnapshot("rha","ebCassette_test",true,thick,thicknessPlan,plan.OldAlong,plan.NewAlong);
        check(snapshot.AlongAfter(false)![1].SequenceEqual(new[]{false,true,false,true})&&snapshot.AlongAfter(true)![1].All(a=>a),"the undo snapshot restores the old directions and thicknesses together");
        check(snapshot.After(false).Thicknesses[1].SequenceEqual(new ushort[]{10,200,10,200})&&snapshot.After(true).Thicknesses[1].All(t=>t==40),"the undo snapshot still carries both thickness sets");
        check(new CompositeUndoSnapshot("a","b",false,thick,thicknessPlan).AlongAfter(true)==null,"a step without recorded directions (Uniform, or a snapshot of 0.22.23) has none");
        check(ThickenModePlan.Create(ThicknessMode.Uniform,along,null).Any==false,"Uniform leaves every direction alone");
        check(plan.Note!=null&&plan.Note.Contains("1 face set to Along face normal")&&plan.Note.Contains("undo"),"the note names the number of faces and undo ("+plan.Note+")");
        check(ThickenModePlan.Create(ThicknessMode.ToBackPlane,new List<bool[]>{new[]{true,true,true,true}},null).Note==null,"nothing to change: no note");
    }

    // ---- 5: the stored direction in the mesh JSON ----
    private static void StoredDirection(Action<bool,string> check)
    {
        var expected=new List<bool[]>{new[]{true,true,true,true},new[]{true,true,true,true}};
        check(ThickenModeJson.Verify("{\"faces\":[{\"v\":[0,1,2,3],\"t\":[1,1,1,1],\"tm\":0,\"te\":0},{\"v\":[0,1,2,3],\"t\":[1,1,1,1],\"tm\":0,\"te\":0}]}",expected)==null,"stored 0 (Along face normal) agrees with the plan");
        check(ThickenModeJson.Verify("{\"faces\":[{\"tm\":0},{\"tm\":2}]}",expected)!.Contains("face 2"),"a face that stored another direction is named");
        check(ThickenModeJson.Verify("{\"faces\":[{\"tm\":0},{}]}",expected)==null,"an absent direction is the default, Along face normal");
        check(ThickenModeJson.Verify("{\"faces\":[{\"tm\":0}]}",expected)!.Contains("1 faces"),"a face count that differs is refused");
        check(ThickenModeJson.Verify("{\"faces\":[{\"tm\":[0,0,0,0]},{\"tm\":[0,0,1,0]}]}",expected)!.Contains("corner 3"),"a per-corner list is checked corner by corner");
        var mixed=new List<bool[]>{new[]{true,false,true,true}};
        check(ThickenModeJson.Verify("{\"faces\":[{\"tm\":0}]}",mixed)!.Contains("one direction per face"),"mixed corners cannot live in a single stored direction: refused");
        check(ThickenModeJson.Verify("not json",expected)!.Contains("could not be checked"),"unreadable JSON is a refusal, not an exception");
        check(ThickenModeJson.Verify("{\"vertices\":[]}",expected)!.Contains("no faces"),"JSON without faces is refused");
    }

    // ---- 6: limits on corner differences and the minimum ----
    private static Vector3[] Corners(float x,float y)=>new[]{new Vector3(x,y,0),new Vector3(x+1,y,0),new Vector3(x+1,y,1),new Vector3(x,y,1)};
    private static void Limits(Action<bool,string> check)
    {
        var verts=new List<Vector3[]>{Corners(0,0),Corners(5,0)};
        var thick=new[]{new ushort[]{10,500,10,10},new ushort[]{40,40,40,40}};
        var r=BackplaneThickness.Limit(thick,verts,new[]{true,true},100);
        check(r.Thicknesses[0].SequenceEqual(new ushort[]{10,110,10,10})&&r.Limited==1&&r.LimitedFaces==1,"a 500 mm corner next to 10 mm is lowered to 10 + 100 mm; one corner limited");
        check(thick[0][1]==500,"the input arrays are not changed");
        check(r.Thicknesses[1].SequenceEqual(thick[1]),"a face within the limit is untouched");
        var ring=BackplaneThickness.Limit(new[]{new ushort[]{10,200,400,200}},new List<Vector3[]>{Corners(0,0)},new[]{true},50);
        check(ring.Thicknesses[0].SequenceEqual(new ushort[]{10,60,110,60})&&ring.Limited==3,"the limit spreads along the ring until every neighbour pair is within it (10,60,110,60)");
        var skip=BackplaneThickness.Limit(thick,verts,new[]{false,true},100);
        check(skip.Thicknesses[0].SequenceEqual(thick[0])&&skip.Limited==0,"a face that does not take part is never changed");
        // corners of two faces at one point count as neighbours: face 1 sits on top of face 0
        var stacked=BackplaneThickness.Limit(new[]{new ushort[]{10,10,10,10},new ushort[]{300,300,300,300}},new List<Vector3[]>{Corners(0,0),Corners(0,0)},new[]{true,true},100);
        check(stacked.Thicknesses[1].All(t=>t==110)&&stacked.Limited==4,"corners of two treated faces at the same point may not differ by more than the limit either");

        // through the plan itself: a wedge (front plane plus a steep wall) reaches 591 mm with the old rules
        var wedge=new List<Vector3[]>{new[]{new Vector3(0,0,0),new Vector3(1,0,0),new Vector3(1,0,1),new Vector3(0,0,1)},new[]{new Vector3(0,0,0),new Vector3(0,.5f,0),new Vector3(0,.5f,1),new Vector3(0,0,1)}};
        var normals=new List<Vector3>{Vector3.UnitY,-Vector3.UnitX};
        var old=new List<ushort[]>{new ushort[]{10,10,10,10},new ushort[]{10,10,10,10}};
        var legacy=BackplaneThickness.Create("m",old,wedge,normals,null,100);
        var limited=BackplaneThickness.Create("m",old,wedge,normals,null,100,new BackplaneLimits(5,60));
        check(limited.MaxMm<=legacy.MaxMm&&limited.MinMm>=5,"limits never widen the range ("+legacy.MinMm+"-"+legacy.MaxMm+" -> "+limited.MinMm+"-"+limited.MaxMm+")");
        var spread=limited.Plan.Thicknesses.SelectMany((f,i)=>f.Select(v=>(int)v)).ToArray();
        check(limited.Plan.Thicknesses.All(f=>Enumerable.Range(0,f.Length).All(j=>Math.Abs(f[j]-f[(j+1)%f.Length])<=60)),"no neighbouring corners of a face differ by more than 60 mm after the limit");
        check(limited.Plan.Thicknesses.All(f=>f.All(v=>v>=5)),"no corner is thinner than the minimum 5 mm");
        check(legacy.LimitedCorners==0&&!legacy.Summary.Contains("limited"),"without limits the old summary is unchanged ("+legacy.Summary+")");
        check(limited.LimitedCorners>0&&limited.Summary.EndsWith(", "+limited.LimitedCorners+" limited"),"with limits the summary reports the number of limited corners ("+limited.Summary+")");
        var min=BackplaneThickness.Create("m",new List<ushort[]>{new ushort[]{10,10,10,10}},new List<Vector3[]>{Corners(0,0)},new List<Vector3>{Vector3.UnitY},null,40,new BackplaneLimits(8,double.PositiveInfinity));
        check(min.Plan.Thicknesses[0].All(v=>v==40),"a back plane that needs no limit gives the same result with limits");
        bool bad=false;try{BackplaneThickness.Create("m",old,wedge,normals,null,100,new BackplaneLimits(0,60));}catch(ArgumentOutOfRangeException){bad=true;}
        check(bad,"a minimum below 1 mm is refused");
        // the user config: 5000 mm and more means no limit
        check(BackplaneLimits.None.MaxCornerDeltaMm==double.PositiveInfinity&&BackplaneLimits.None.MinimumMm==BackplaneThickness.MinimumMm,"BackplaneLimits.None is the 0.22.23 behaviour");
    }

    // ---- 8 (item 9): the content signature catches an edit that the event never announced ----
    private static void Signature(Action<bool,string> check)
    {
        float now=10;var gate=new PlateMeshGate(()=>now,()=>false);
        var mesh=Mesh(Quad(0,0,20),Quad(1,0,30,vertexBase:102));
        var first=gate.Get(mesh,"overlay signature",SnapshotUse.Display,out _)!;
        check(MeshSignature.Change(first.Signature,first.Signature)==null,"an unchanged mesh has an unchanged signature");
        // an edge-tool-like edit: same counts, one vertex moved, no OnAssignedMeshModified (no NoteModified call)
        var loop=(FLoop)mesh.Faces[0]!.First!;loop.Position=new Vector3(loop.Position.X-.2f,loop.Position.Y,loop.Position.Z);
        now+=PlateMeshGate.MaxAgeSeconds+.1f;
        var moved=gate.Get(mesh,"overlay signature",SnapshotUse.Display,out _)!;
        check(!ReferenceEquals(moved,first)&&moved.FaceCount==first.FaceCount&&moved.LoopCount==first.LoopCount,"a moved vertex with equal counts and no event is read again by the next poll");
        check(MeshSignature.Change(first.Signature,moved.Signature)=="positions","the signature change is reported as positions");
        // a thickness edit
        loop.Position=new Vector3(loop.Position.X+.2f,loop.Position.Y,loop.Position.Z);now+=1.2f;
        mesh.Faces[1]!.First!.NextLoop!.Thickness=55;
        var thick=gate.Get(mesh,"overlay signature",SnapshotUse.Display,out _)!;
        check(MeshSignature.Change(moved.Signature,thick.Signature)=="positions"||MeshSignature.Change(first.Signature,thick.Signature)=="thickness or direction","a thickness edit is a different signature");
        check(MeshSignature.Change(first.Signature,thick.Signature)=="thickness or direction","back at the first positions, only the thickness differs: reported as thickness or direction");
        // an extrude: the counts change
        var more=Mesh(Quad(0,0,20),Quad(1,0,30,vertexBase:102),Quad(2,0,30,vertexBase:104));
        now+=1.2f;var changed=PlateMeshReader.TryRead(more,out var three,out _);
        check(changed&&MeshSignature.Change(first.Signature,three!.Signature)=="counts 2/8 -> 3/12","an extrude is reported as a count change");
        check(MeshSignature.Change(null,first.Signature)=="first read","a first read has no earlier signature");
        // the cache of the hit model is keyed on the same signature: simulate the NativeCassetteGeometry rule
        string? cached=null;int rebuilds=0;
        void Use(PlateMeshSnapshot s){if(MeshSignature.Change(cached,s.Signature)!=null){cached=s.Signature;rebuilds++;}}
        Use(first);Use(first);Use(moved);Use(moved);Use(thick);
        check(rebuilds==3,"a cache keyed on the signature rebuilds exactly when the content changed (3 builds for 3 contents)");
    }

    // ---- 9 (item 7): the face the shot line crosses, not the nearest infinite plane ----
    // ---- 10: tapered faces in the declared-face policy ----
    private static void Tapered(Action<bool,string> check)
    {
        var ranges=new uint[]{4};var types=new byte[]{1};var widths=new[]{4};
        check(!DeclaredFacePolicy.ResolveSamples(0,3,4,0,1,2,100,150,200,ranges,types,widths,out _,allowNativeSentinel:true,allowPolygons:true),"three different vertex samples are still refused by default (0.22.23 behaviour)");
        check(DeclaredFacePolicy.ResolveSamples(0,3,4,0,1,2,100,150,200,ranges,types,widths,out var tapered,allowNativeSentinel:true,allowPolygons:true,allowTapered:true)&&tapered.Tapered&&tapered.FaceIndex==0&&tapered.NormalThicknessMm==150,"a tapered back-plane face keeps its marker identity and is flagged tapered (mean 150)");
        check(!DeclaredFacePolicy.ResolveSamples(0,3,4,0,1,2,100,-1,200,ranges,types,widths,out _,allowNativeSentinel:true,allowPolygons:true,allowTapered:true),"a tapered face with a sentinel or non-positive sample is refused");
        check(DeclaredFacePolicy.ResolveSamples(0,3,4,0,1,2,100,100,100,ranges,types,widths,out var plain,allowNativeSentinel:true,allowPolygons:true,allowTapered:true)&&!plain.Tapered&&plain.NormalThicknessMm==100,"equal samples are not tapered");
        check(!DeclaredFacePolicy.ResolveSamples(0,3,4,0,1,2,100,150,200,ranges,new byte[]{3},widths,out _,allowTapered:true,allowPolygons:true),"a flank triangle (marker type 3) still has no face contract, tapered or not");
    }

    // ---- 11 (item 8): the implied native L ----
    // ---- 7: source checks (every reader goes through the gate; the lifetime rules) ----
    private static string? SourceRoot()
    {
        for(var dir=new DirectoryInfo(AppContext.BaseDirectory);dir!=null;dir=dir.Parent)
            if(File.Exists(Path.Combine(dir.FullName,"SprocketExpandedBallistics.csproj")))return dir.FullName;
        return null;
    }
    private static void Source(Action<bool,string> check)
    {
        var root=SourceRoot();if(root==null){check(false,"source root not found");return;}
        string Text(string name)=>File.ReadAllText(Path.Combine(root,name));
        var edit=Text("RuntimeCompositePlateEdit.cs");var plate=Text("RuntimeCompositePlate.cs");var visual=Text("RuntimeCassettePlateVisual.cs");
        var panel=Text("CassetteComposerPanel.cs");var composer=Text("RuntimeCassetteComposer.cs");var stack=Text("RuntimePlateStack.cs");var access=Text("PlateMeshAccess.cs");
        check(!edit.Contains("PurgeUnused()")&&!plate.Contains("PurgeUnused()"),"no callback of ours calls PurgeUnused");
        foreach(var (name,text) in new[]{("RuntimeCompositePlate",plate),("CassetteComposerPanel",panel),("RuntimeCassettePlateVisual",visual),("RuntimeCassetteComposer",composer),("RuntimePlateStack",stack)})
        {
            var own=string.Join(" ",text.Split((char)10).Where(l=>!l.Contains("fresh.editMesh")));
            check(!own.Contains(".GetFace(")&&!own.Contains(".firstLoop")&&!own.Contains(".editMesh"),name+" reads no native face, loop or edit mesh itself: only the snapshot");
        }
        check(System.Text.RegularExpressions.Regex.Matches(edit,@"\.GetFace\(").Count==1,"the Apply step writes the faces of its own fresh copy only (one GetFace for the write)");
        check(!visual.Contains("ToJson(true)"),"the plate visual no longer serialises the native mesh");
        check(visual.Contains("catch(Exception ex){if(warnings++<12)Plugin.ModLog.LogWarning(\"[Plate visual] Enqueue")&&visual.Contains("private static void TickCore()")&&visual.Contains("[Plate visual] Tick:"),"Enqueue and Tick have a try/catch");
        check(edit.Contains("PlateMeshDeferred.NextFrame(\"editor refresh\"")&&!edit.Contains("active.RequestEditorRefresh();\n            }\n        }\n        /// <summary>After"),"the editor refresh runs one frame later");
        check(edit.Contains("PlateMeshDeferred.WhenReady(\"mesh lease release\",EditorCaughtUp"),"the mesh leases are kept until the editor mesh is the component mesh again");
        check(access.Contains("[Composite plate edit]")&&access.Contains("ReleaseDiagnostics.Enabled"),"the per-edit log line exists and only with detailed logging");
        check(panel.Contains("Finish the shape (holes, deleted faces) before To back plane"),"the composer carries the hint");
        check(edit.Contains("var selected=SelectedFaces(editor,SnapshotUse.Fresh,out var unreadable);"),"Apply reads the selection and the mesh fresh and refuses when it cannot");
        var reactions=Text("NativeMaterialReactions.cs");var geometry=Text("NativeCassetteGeometry.cs");
        check(geometry.Contains("MeshSignature.Change")&&geometry.Contains("[Composite plate cache] invalidated by signature")&&visual.Contains("PollSignatures"),"the hit geometry and the overlay are keyed on the content signature");
    }
}
