using Sprocket.PlateMesh;
using Sprocket.Vehicles.PlateStructures;
using NVector=System.Numerics.Vector3;
namespace SprocketExpandedBallistics;

// 0.22.24: the Il2Cpp side of PlateMeshSnapshot.cs. ALL designer readers of a plate mesh go through Read(): DrawInMaterialBlock, the composer's
// ReadTarget and SelectedFaces, the plate visual (Geometry, change signature) and the ctor/ComputePlan of the Apply step. Nothing else may call
// GetFace/firstLoop/next on a designer plate (CRASH-MAP.md 3B/3C).
internal static class PlateMeshAccess
{
    internal static readonly PlateMeshGate Gate=new(()=>UnityEngine.Time.unscaledTime,MouseHeld,()=>RuntimeCompositePlate.IgnoreDirtyFlags);
    private static readonly Dictionary<IntPtr,(PlateStructure Plate,int Number)> edited=new();
    private static readonly HashSet<string> reasons=new(StringComparer.Ordinal);
    private static int editNumber;

    private static bool MouseHeld()
    {
        try
        {
            var mouse=UnityEngine.InputSystem.Mouse.current;
            return mouse!=null&&(mouse.leftButton.isPressed||mouse.rightButton.isPressed||mouse.middleButton.isPressed);
        }
        catch(Exception){return false;}
    }

    /// <summary>The mesh, as a value snapshot, or null while it is quiet/busy/unclean. Display callers may get the last good snapshot.</summary>
    internal static PlateMeshSnapshot? Read(PlateStructureMesh? mesh,string reader,SnapshotUse use=SnapshotUse.Display)=>Read(mesh,reader,use,out _);
    /// <summary>As <see cref="Read(PlateStructureMesh?,string,SnapshotUse)"/>, with the gate's reason when it returns null (0.22.27: the composer shows it).</summary>
    internal static PlateMeshSnapshot? Read(PlateStructureMesh? mesh,string reader,SnapshotUse use,out string reason)
    {
        reason="";
        if(mesh==null){reason="no mesh";return null;}
        try
        {
            var snapshot=Gate.Get(new NativeRawMesh(mesh),reader,use,out reason);
            if(snapshot==null&&reason.Length>0&&reason!="quiet period"&&reason!="mouse button held"&&reasons.Add(reader+": "+reason))
                Plugin.ModLog.LogInfo($"[Composite plate edit] {reader}: mesh not readable ({reason}); skipped.");
            // 0.22.27: say once per reader and flag value that the read went ahead although the index flags are set.
            if(snapshot!=null&&snapshot.IndexDirtyFlags!=0&&reasons.Add(reader+": index "+snapshot.IndexDirtyFlags))
                Plugin.ModLog.LogInfo($"[Composite plate edit] {reader}: read although index dirty flags are {snapshot.IndexDirtyFlags} ({PlateMeshReader.IndexFlagNames(snapshot.IndexDirtyFlags)}): only Element.index is stale; faces by list position, rings by loop pointers; dirty 0, ring closed, faces validate.");
            return snapshot;
        }
        catch(Exception ex){reason=ex.GetType().Name;Plugin.ModLog.LogWarning($"[Composite plate edit] {reader}: {ex.GetType().Name}: {ex.Message}");return null;}
    }
    internal static PlateMeshSnapshot? Read(PlateStructure? plate,string reader,SnapshotUse use=SnapshotUse.Display)=>Read(plate,reader,use,out _);
    internal static PlateMeshSnapshot? Read(PlateStructure? plate,string reader,SnapshotUse use,out string reason)
    {
        reason="";
        try{if(plate==null||!plate.HasMesh){reason="no mesh";return null;}return Read(plate.Mesh,reader,use,out reason);}catch(Exception ex){reason=ex.GetType().Name;return null;}
    }
    /// <summary>A read of a mesh the mod made itself and has just rebuilt (the copy of an Apply step): no gate, no quiet period.</summary>
    internal static PlateMeshSnapshot? ReadOwn(PlateStructureMesh mesh,out string reason)
    {
        reason="";
        try{return PlateMeshReader.TryRead(new NativeRawMesh(mesh),out var snapshot,out reason,false)?snapshot:null;}
        catch(Exception ex){reason=ex.GetType().Name;return null;}
    }
    /// <summary>
    /// Combat (Play): a mesh nobody is editing. No gate, no quiet period and no dirty-flag condition (a built mesh keeps flags nobody clears),
    /// but the same bounded ring walk, validation and counts check. Used for the hit-geometry cache and its content signature.
    /// </summary>
    internal static PlateMeshSnapshot? ReadUngated(PlateStructureMesh mesh,out string reason)
    {
        reason="";
        try{return PlateMeshReader.TryRead(new NativeRawMesh(mesh),out var snapshot,out reason,false)?snapshot:null;}
        catch(Exception ex){reason=ex.GetType().Name;return null;}
    }
    /// <summary>Why a Fresh read failed, in words for the user (the step is refused, nothing is touched).</summary>
    internal static string Busy=>Gate.Busy?"The plate mesh is still being edited. Release the mouse button and try again in a moment.":"The plate mesh is not in a readable state yet. Try again in a moment.";

    /// <summary>OnAssignedMeshModified postfix: the quiet period starts, and a cassette plate gets one log line when it is over.</summary>
    internal static void Modified(PlateStructure plate)
    {
        try
        {
            if(plate==null||!plate.HasMesh)return;
            var pointer=plate.Mesh.Pointer;
            Gate.NoteModified(pointer.ToInt64());
            RuntimeCassettePlateVisual.BeginEdit();
            if(ReleaseDiagnostics.Enabled&&CompositePlate.IsCassette(plate.armourTechID)&&!edited.ContainsKey(plate.Pointer))edited[plate.Pointer]=(plate,++editNumber);
        }
        catch(Exception){}
    }
    internal static void Forget(PlateStructure plate)
    {
        try{edited.Remove(plate.Pointer);}catch(Exception){}
    }
    /// <summary>Once per frame: when the quiet period of an edit is over, the one-line evidence record of that edit.</summary>
    internal static void Tick()
    {
        PlateMeshDeferred.Update();
        if(edited.Count==0||Gate.Busy)return;
        foreach(var item in edited.ToArray())
        {
            edited.Remove(item.Key);
            try
            {
                var plate=item.Value.Plate;
                if(plate==null||!plate.HasMesh)continue;
                var mesh=plate.Mesh;var raw=new NativeRawMesh(mesh);
                var snapshot=Gate.Get(raw,"edit-log",SnapshotUse.Display,out var reason);
                var skipped=Gate.TakeSkipped().Where(s=>s!="edit-log").ToArray();
                Plugin.ModLog.LogInfo($"[Composite plate edit] #{item.Value.Number} plate #{plate.VUID.Value}: faces {raw.FaceCount}, loops {raw.LoopCount}, dirty {raw.DirtyFlags}/{raw.IndexDirtyFlags}, mesh 0x{raw.Pointer:x}, "
                    +(snapshot!=null?"snapshot "+snapshot.Signature:"no snapshot ("+reason+")")
                    +(snapshot!=null&&snapshot.IndexDirtyFlags>0?$", read despite index flags {snapshot.IndexDirtyFlags} ({PlateMeshReader.IndexFlagNames(snapshot.IndexDirtyFlags)}; by list position)":"")
                    +", readers skipped during the edit: "+(skipped.Length==0?"none":string.Join(", ",skipped))+".");
            }
            catch(Exception ex){Plugin.ModLog.LogWarning("[Composite plate edit] log failed: "+ex.GetType().Name);}
        }
    }
    internal static void Clear(){Gate.Clear();edited.Clear();reasons.Clear();PlateMeshDeferred.Clear();}

    // ---- the native views ----
    private static NVector V(UnityEngine.Vector3 v)=>new(v.x,v.y,v.z);

    internal sealed class NativeRawMesh:IRawMesh
    {
        private readonly PlateStructureMesh mesh;
        private EditMesh? edit;
        internal NativeRawMesh(PlateStructureMesh mesh){this.mesh=mesh;}
        private EditMesh? Edit=>edit??=mesh.editMesh;
        public long Pointer=>mesh.Pointer.ToInt64();
        public bool Released=>mesh.Released||Edit==null;
        public int DirtyFlags=>Edit==null?-1:(int)Edit.DirtyFlags;
        public int IndexDirtyFlags=>Edit==null?-1:(int)Edit.elementIndexDirtyFlags;
        public int FaceCount=>Edit?.FaceCount??0;
        public int FaceListCount=>Edit?.faces?.Count??-1;
        public int LoopCount=>Edit?.LoopCount??0;
        public IRawFace? Face(int index){var face=Edit?.GetFace(index);return face==null?null:new NativeFace(face);}
    }
    private sealed class NativeFace:IRawFace
    {
        private readonly Face face;
        internal NativeFace(Face face){this.face=face;}
        public int Errors=>(int)Face.Validate(face);
        public int VertexCount=>face.vertexCount;
        public bool Selected=>(face.flags&ElementFlags.Selected)!=0;
        public bool Culled=>face.HasFlag(ElementFlags.CullTessellation);
        public NVector Normal=>V(face.normal);
        public IRawLoop? FirstLoop{get{var loop=face.firstLoop;return loop==null?null:new NativeLoop(loop);}}
    }
    private sealed class NativeLoop:IRawLoop
    {
        private readonly Loop loop;
        internal NativeLoop(Loop loop){this.loop=loop;}
        public long Key=>loop.Pointer.ToInt64();
        public long VertexKey=>loop.vertex.Pointer.ToInt64();
        public NVector Position=>V(loop.vertex.position);
        public ushort Thickness=>loop.thickness;
        public bool AlongFaceNormal=>loop.thickenMode==ThickenMode.AlongFaceNormal;
        public IRawLoop? Next{get{var next=loop.next;return next==null?null:new NativeLoop(next);}}
    }
}

/// <summary>
/// 0.22.24 mesh lifetime (CRASH-MAP.md D): work that must not run inside a native operation, or until the native editor has caught up.
/// Nothing here is called from a native callback; <see cref="Update"/> runs once per frame from the behaviour.
/// </summary>
internal static class PlateMeshDeferred
{
    private sealed record Job(string Name,Func<bool> Ready,Action Run,int DueFrame,float Deadline);
    private static readonly List<Job> jobs=new();
    /// <summary>Run <paramref name="run"/> in a later frame (the editor refresh after an operation, not inside it).</summary>
    internal static void NextFrame(string name,Action run)=>jobs.Add(new(name,()=>true,run,UnityEngine.Time.frameCount+1,float.MaxValue));
    /// <summary>
    /// Run <paramref name="run"/> once <paramref name="ready"/> says so (the native editor mesh is the component mesh again), or after
    /// <paramref name="timeoutSeconds"/> at the latest, so a lease can never be held forever.
    /// </summary>
    internal static void WhenReady(string name,Func<bool> ready,Action run,float timeoutSeconds=5f)=>
        jobs.Add(new(name,ready,run,UnityEngine.Time.frameCount+1,UnityEngine.Time.unscaledTime+timeoutSeconds));
    internal static int Pending=>jobs.Count;
    internal static void Update()
    {
        if(jobs.Count==0)return;
        var frame=UnityEngine.Time.frameCount;var now=UnityEngine.Time.unscaledTime;
        foreach(var job in jobs.ToArray())
        {
            if(job.DueFrame>frame)continue;
            bool ready;
            try{ready=job.Ready();}catch(Exception){ready=true;}
            if(!ready&&now<job.Deadline)continue;
            jobs.Remove(job);
            try
            {
                if(!ready&&ReleaseDiagnostics.Enabled)Plugin.ModLog.LogInfo($"[Composite plate edit] {job.Name}: the editor did not catch up within the limit; released anyway.");
                job.Run();
            }
            catch(Exception ex){Plugin.ModLog.LogWarning($"[Composite plate edit] {job.Name} failed: {ex.Message}");}
        }
    }
    internal static void Clear()=>jobs.Clear();
}
