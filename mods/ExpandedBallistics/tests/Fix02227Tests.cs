using System.Numerics;
namespace SprocketExpandedBallistics;

/// <summary>
/// 0.22.27: EditMesh.elementIndexDirtyFlags only marks a stale Element.index (native Delete* sets it, a spawned plate keeps 3 = Vertex|Edge).
/// The reader works by list position and loop pointers, so those flags alone no longer refuse a read; every 0.22.24 guard still does.
/// The composer gets a target for a new plate and says why when a selected plate cannot be read.
/// </summary>
internal static class Fix02227Tests
{
    // ---- simulated native mesh (the same shape as the 0.22.24 fakes) ----
    private sealed class FLoop:IRawLoop
    {
        public long Key{get;init;}public long VertexKey{get;init;}public Vector3 Position{get;set;}public ushort Thickness{get;set;}public bool AlongFaceNormal{get;set;}=true;
        public FLoop? NextLoop;public IRawLoop? Next=>NextLoop;
    }
    private sealed class FFace:IRawFace
    {
        public int Errors{get;set;}public int VertexCount{get;set;}public bool Selected{get;set;}public bool Culled{get;set;}public Vector3 Normal{get;set;}=Vector3.UnitY;
        public FLoop? First;public IRawLoop? FirstLoop=>First;
    }
    private sealed class FMesh:IRawMesh
    {
        public long Pointer{get;set;}=0x192b54006c0;public bool Released{get;set;}public int DirtyFlags{get;set;}public int IndexDirtyFlags{get;set;}
        public int FaceCount{get;set;}public int FaceListCount{get;set;}public int LoopCount{get;set;}
        public List<FFace?> Faces=new();public int FaceReads;
        public IRawFace? Face(int index){FaceReads++;return Faces[index];}
    }
    private static long key=1;
    private static FFace Ring(float x,ushort t,int corners,long vertexBase,bool closed=true)
    {
        var loops=Enumerable.Range(0,corners).Select(i=>new FLoop{Key=key++,VertexKey=vertexBase+i,Position=new Vector3(x+MathF.Cos(i*MathF.Tau/corners),0,MathF.Sin(i*MathF.Tau/corners)),Thickness=t}).ToArray();
        for(int i=0;i<corners;i++)loops[i].NextLoop=i<corners-1?loops[i+1]:closed?loops[0]:null;
        return new FFace{VertexCount=corners,First=loops[0]};
    }
    /// <summary>The spawned plate of the 03-11 log: plate #464, faces 7, loops 24, dirty 0/3 (3 quads and 4 triangles).</summary>
    private static FMesh Spawned(int indexFlags=3)
    {
        var faces=new List<FFace>();long v=100;
        for(int i=0;i<3;i++){faces.Add(Ring(i*3,20,4,v));v+=10;}
        for(int i=0;i<4;i++){faces.Add(Ring(10+i*3,20,3,v));v+=10;}
        return new FMesh{Faces=faces.Cast<FFace?>().ToList(),FaceCount=7,FaceListCount=7,LoopCount=24,IndexDirtyFlags=indexFlags};
    }

    internal static void Run(Action<bool,string> check)
    {
        int count=0;var assert=check;check=(ok,text)=>{assert(ok,"fix02227: "+text);count++;};
        IndexFlagsRead(check);StillRefused(check);NewPlate(check);StatusText(check);Source(check);
        Console.WriteLine($"0.22.27 checks PASS: {count} checks.");
    }

    // ---- 1: index dirty flags with a clean mesh are read, for Display and for Fresh ----
    private static void IndexFlagsRead(Action<bool,string> check)
    {
        var mesh=Spawned();
        check(PlateMeshReader.TryRead(mesh,out var s,out var why)&&s!=null,"index-dirty 3 with dirty 0 and a clean mesh: TryRead reads ("+why+")");
        check(s!=null&&s.FaceCount==7&&s.LoopCount==24&&s.Faces.Length==7&&s.IndexDirtyFlags==3,"the log case reads 7 faces and 24 loops and carries index flags 3");
        check(s!=null&&s.Signature.StartsWith("7:24:",StringComparison.Ordinal),"the signature is the plate's content (7:24:...)");
        foreach(var flags in new[]{1,2,4,8,15})
        {
            var m=Spawned(flags);
            check(PlateMeshReader.TryRead(m,out var each,out var r)&&each!=null&&each.IndexDirtyFlags==flags,$"index flags {flags} ({PlateMeshReader.IndexFlagNames(flags)}) alone never refuse ({r})");
        }
        float now=10;var gate=new PlateMeshGate(()=>now,()=>false);
        var display=gate.Get(Spawned(),"composer target",SnapshotUse.Display,out var reason);
        check(display!=null&&reason.Length==0,"Display: a fresh gate reads the index-dirty plate ("+reason+")");
        var gate2=new PlateMeshGate(()=>now,()=>false);
        var fresh=gate2.Get(Spawned(),"apply",SnapshotUse.Fresh,out reason);
        check(fresh!=null&&reason.Length==0,"Fresh (Apply/Convert, To back plane): reads the index-dirty plate ("+reason+")");
        check(gate2.TakeSkipped().Length==0,"no reader is recorded as skipped");
        var cached=Spawned();var gate3=new PlateMeshGate(()=>now,()=>false);
        var first=gate3.Get(cached,"inspector faces",SnapshotUse.Display,out _);var reads=cached.FaceReads;
        var again=gate3.Get(cached,"inspector faces",SnapshotUse.Display,out _);
        check(first!=null&&ReferenceEquals(first,again)&&cached.FaceReads==reads,"the cache hit no longer requires index flags 0 (no second walk within a second)");
        check(PlateMeshReader.IndexFlagNames(3)=="Vertex|Edge"&&PlateMeshReader.IndexFlagNames(0)=="none"&&PlateMeshReader.IndexFlagNames(15)=="Vertex|Edge|Face|Loop","flag names for the log");
    }

    // ---- 2: the 0.22.24 crash guards still refuse, also with index flags set ----
    private static void StillRefused(Action<bool,string> check)
    {
        void Refuse(Action<FMesh> damage,string part,string text)
        {
            var mesh=Spawned();damage(mesh);
            var ok=PlateMeshReader.TryRead(mesh,out var s,out var reason);
            check(!ok&&s==null&&reason.Contains(part,StringComparison.OrdinalIgnoreCase),text+" ("+reason+")");
            float now=10;var gate=new PlateMeshGate(()=>now,()=>false);
            check(gate.Get(mesh,"apply",SnapshotUse.Fresh,out _)==null&&gate.Get(mesh,"composer target",SnapshotUse.Display,out _)==null,text+": Fresh and Display (no cache) get nothing");
        }
        Refuse(m=>m.DirtyFlags=2,"dirty flags","dirty flags 2 with index 3");
        Refuse(m=>m.DirtyFlags=255,"dirty flags","dirty flags All (DeleteMarked) with index 3");
        Refuse(m=>m.Released=true,"released","released mesh with index 3");
        Refuse(m=>m.FaceListCount=6,"faces list","half-deleted: faces list differs from FaceCount");
        Refuse(m=>m.Faces[4]=null,"null","half-deleted: a null face");
        Refuse(m=>m.Faces[2]!.Errors=1<<3,"validate","half-deleted: a face that does not validate");
        Refuse(m=>m.Faces[1]=Ring(50,20,4,900,closed:false),"ring does not close","an open ring");
        Refuse(m=>m.Faces[1]!.VertexCount=PlateMeshReader.MaxCorners+1,"corners","a ring longer than the bound");

        float t=10;bool mouse=false;var gate=new PlateMeshGate(()=>t,()=>mouse);var mesh=Spawned();
        gate.NoteModified(mesh.Pointer);var reads=mesh.FaceReads;
        check(gate.Get(mesh,"composer target",SnapshotUse.Display,out var reason)==null&&reason=="quiet period"&&mesh.FaceReads==reads,"quiet period after OnAssignedMeshModified: a new plate without cache gets nothing and the mesh is not touched");
        check(gate.Get(mesh,"apply",SnapshotUse.Fresh,out reason)==null&&reason=="quiet period","quiet period: Fresh gets nothing");
        t+=PlateMeshGate.QuietSeconds+.01f;mouse=true;
        check(gate.Get(mesh,"apply",SnapshotUse.Fresh,out reason)==null&&reason=="mouse button held"&&mesh.FaceReads==reads,"mouse button held: Fresh gets nothing, the mesh is not touched");
        mouse=false;
        check(gate.Get(mesh,"apply",SnapshotUse.Fresh,out reason)!=null,"quiet period over, no button: the index-dirty plate is read ("+reason+")");
    }

    // ---- 3: a new plate without a cached snapshot gets a composer target ----
    private static void NewPlate(Action<bool,string> check)
    {
        float now=10;var gate=new PlateMeshGate(()=>now,()=>false);var mesh=Spawned();
        check(gate.CacheCount==0&&!gate.HasSnapshot(mesh.Pointer),"the spawned plate has no cached snapshot");
        var snap=gate.Get(mesh,"composer target",SnapshotUse.Display,out var reason);
        check(snap!=null&&gate.HasSnapshot(mesh.Pointer),"the first composer read fills the cache ("+reason+")");
        // The same target the panel builds from the snapshot (ReadTarget).
        double min=double.PositiveInfinity,max=0;foreach(var f in snap!.Faces){var mt=f.MeanThickness();min=Math.Min(min,mt);max=Math.Max(max,mt);}
        var model=Model();
        model.SetTarget(new ComposerTarget("Plate #464",snap.Faces.Length,"rha","RHA",false,min,max),null);
        check(model.Target is {Faces:7,Name:"Plate #464"}&&model.TargetChip.StartsWith("Plate #464 · 7 faces",StringComparison.Ordinal),"the composer has a target for the new plate ("+model.TargetChip+")");
        check(model.ApplyBlocker!="Select a plate in the designer."&&model.TargetUnavailable==null,"the composer no longer says 'Select a plate in the designer.' ("+model.ApplyBlocker+")");
        check(model.ApplyBlocker!=null&&!model.ApplyBlocker.StartsWith("Plate busy",StringComparison.Ordinal),"and not 'Plate busy' either");
        var plain=gate.Get(Spawned(),"selection",SnapshotUse.Fresh,out _);
        check(plain!=null&&plain.SelectedFlags()==null,"the selection reader also reads it (nothing selected: Apply treats every face)");
    }

    // ---- 4: the status text with the reason ----
    private static void StatusText(Action<bool,string> check)
    {
        var model=Model();
        check(model.ApplyBlocker=="Select a plate in the designer."&&model.TargetChip=="No plate selected","no plate: the old text stays");
        var v=model.TargetVersion;
        model.SetTarget(null,CassetteComposerModel.PlateBusy("Plate #464","dirty flags 2"));
        check(model.ApplyBlocker=="Plate busy: Plate #464 is not readable yet (dirty flags 2). Try again in a moment."&&model.TargetVersion==v+1,"a selected but unreadable plate: 'Plate busy' with the reason ("+model.ApplyBlocker+")");
        check(model.TargetChip=="Plate selected · not readable yet"&&model.ApplyNote==model.ApplyBlocker&&!model.CanApply,"chip and note follow; Apply stays off");
        model.SetTarget(null,CassetteComposerModel.PlateBusy("Plate #464","dirty flags 2"));
        check(model.TargetVersion==v+1,"the same reason again is no new version");
        check(CassetteComposerModel.PlateBusy("Plate #464","quiet period").Contains("(it was just edited)")&&CassetteComposerModel.PlateBusy("Plate #464","mouse button held").Contains("(a mouse button is held)")
            &&CassetteComposerModel.PlateBusy("Plate #464","face 3 ring does not close after 4 corners").Contains("(face 3 ring does not close after 4 corners)"),"gate reasons in words");
        model.SetTarget(new ComposerTarget("Plate #464",7,"rha","RHA",true,20,20),"ignored");
        check(model.TargetUnavailable==null&&model.Target!=null,"a target clears the reason");
        model.SetTarget(null);
        check(model.ApplyBlocker=="Select a plate in the designer."&&model.TargetUnavailable==null,"deselected: back to 'Select a plate in the designer.'");
    }
    private static CassetteComposerModel Model()=>new(null,new[]{new ComposerMaterial("rha","Steel (RHA)","Steel")},Array.Empty<ComposerMaterial>(),()=>Array.Empty<CassettePreset>(),_=>false,_=>true,false);

    // ---- 5: source checks ----
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
        var snapshot=Text("PlateMeshSnapshot.cs");var access=Text("PlateMeshAccess.cs");var panel=Text("CassetteComposerPanel.cs");
        check(!snapshot.Contains("reason=\"index dirty flags \"",StringComparison.Ordinal)&&!snapshot.Contains("mesh.IndexDirtyFlags==0",StringComparison.Ordinal),"the reader and the cache no longer refuse on index flags");
        check(snapshot.Contains("if(requireClean&&mesh.DirtyFlags!=0)",StringComparison.Ordinal)&&snapshot.Contains("ring does not close",StringComparison.Ordinal)&&snapshot.Contains("does not validate",StringComparison.Ordinal),"dirty flags, ring and validation still refuse");
        var mod=Directory.GetFiles(root,"*.cs",SearchOption.AllDirectories).Where(p=>!p.Contains(Path.DirectorySeparatorChar+"tests"+Path.DirectorySeparatorChar)&&!p.Contains(Path.DirectorySeparatorChar+"obj"+Path.DirectorySeparatorChar)&&!p.Contains(Path.DirectorySeparatorChar+"bin"+Path.DirectorySeparatorChar));
        check(mod.All(p=>!File.ReadAllText(p).Contains(".ReIndex(",StringComparison.Ordinal)),"the mod calls no ReIndex itself");
        check(access.Contains("public IRawFace? Face(int index){var face=Edit?.GetFace(index);",StringComparison.Ordinal),"faces are read with GetFace(i) = faces[i] by list position (disassembly 0.2.56.2)");
        check(access.Contains("read although index dirty flags are",StringComparison.Ordinal)&&access.Contains("read despite index flags",StringComparison.Ordinal),"[Composite plate edit] says when it reads despite index flags");
        check(panel.Contains("unavailable=CassetteComposerModel.PlateBusy(name,reason);",StringComparison.Ordinal)&&panel.Contains("model?.SetTarget(ReadTarget(out var unavailable),unavailable);",StringComparison.Ordinal),"the composer passes the gate's reason to the model");
    }
}
