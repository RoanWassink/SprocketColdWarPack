using System.Text;
using NVector=System.Numerics.Vector3;
namespace SprocketExpandedBallistics;

// 0.22.24, pure (no native types, so the tests can feed it a simulated dirty or half-deleted mesh).
// CRASH-MAP.md section 3B/3C: every designer reader of a PlateMesh went straight at GetFace/firstLoop/next while a native delete or hole cut
// was still rebuilding the mesh. Everything now goes through ONE gate: a mesh is only read when it is not released, has no dirty flags, the
// face counts agree, every face validates and every ring walk is bounded. Otherwise the reader gets the last good snapshot (display) or nothing.

/// <summary>One corner of a face: a thin view on the native loop. Implemented by the Il2Cpp adapter and by the test fakes.</summary>
internal interface IRawLoop
{
    long Key{get;}
    long VertexKey{get;}
    NVector Position{get;}
    ushort Thickness{get;}
    bool AlongFaceNormal{get;}
    IRawLoop? Next{get;}
}

internal interface IRawFace
{
    /// <summary>Face.Validate result; 0 is clean.</summary>
    int Errors{get;}
    int VertexCount{get;}
    bool Selected{get;}
    bool Culled{get;}
    NVector Normal{get;}
    IRawLoop? FirstLoop{get;}
}

internal interface IRawMesh
{
    long Pointer{get;}
    bool Released{get;}
    int DirtyFlags{get;}
    /// <summary>
    /// EditMesh.elementIndexDirtyFlags (ElementType bits Vertex 1, Edge 2, Face 4, Loop 8): Element.index of that type may differ from the
    /// list position. Native Delete* sets it and leaves it until an operation or Build renumbers. 0.22.27: not a reason to refuse a read.
    /// </summary>
    int IndexDirtyFlags{get;}
    int FaceCount{get;}
    /// <summary>Count of the native faces list (can differ from <see cref="FaceCount"/> in the middle of DeleteMarked/ReIndex).</summary>
    int FaceListCount{get;}
    int LoopCount{get;}
    IRawFace? Face(int index);
}

internal sealed record SnapFace(int[] VertexIds,NVector[] Corners,ushort[] Thickness,bool[] Along,NVector Normal,bool Selected,bool Culled,int HoleLoops=0)
{
    /// <summary>Corners of the outer ring (the loops of hole boundaries are counted in <see cref="HoleLoops"/>).</summary>
    internal int VertexCount=>VertexIds.Length;
    internal double MeanThickness(){double t=0;foreach(var v in Thickness)t+=v;return Thickness.Length==0?0:t/Thickness.Length;}
}

/// <summary>A value copy of the authored faces of a plate mesh, taken while the mesh was quiet.</summary>
/// <param name="IndexDirtyFlags">0.22.27: the index flags the mesh had at the read (non-zero: read by list position despite them).</param>
internal sealed record PlateMeshSnapshot(long Pointer,int FaceCount,int LoopCount,SnapFace[] Faces,NVector[] Vertices,string Signature,int IndexDirtyFlags=0)
{
    internal int SelectedCount=>Faces.Count(f=>f.Selected);
    /// <summary>The selection as one flag per face; null when nothing is selected (Apply then treats every face).</summary>
    internal bool[]? SelectedFlags()=>SelectedCount>0?Faces.Select(f=>f.Selected).ToArray():null;
    internal List<ushort[]> ThicknessList()=>Faces.Select(f=>f.Thickness.ToArray()).ToList();
}

/// <summary>
/// 0.22.24: the content signature "faces:loops:positions:thickness" is the cache key of everything that is built from a plate mesh
/// (the layer overlay, the hit-geometry cache). It does not depend on the OnAssignedMeshModified event: an edit by the edge tool, a vertex move,
/// a thickness edit or an extrude that keeps the counts still changes the position or thickness part.
/// </summary>
internal static class MeshSignature
{
    /// <summary>Null when equal; otherwise what changed: "counts 2/8 -> 2/12", "positions", "thickness or direction".</summary>
    internal static string? Change(string? before,string after)
    {
        if(before==null)return "first read";
        if(before==after)return null;
        var a=before.Split(':');var b=after.Split(':');
        if(a.Length!=4||b.Length!=4)return "signature format";
        if(a[0]!=b[0]||a[1]!=b[1])return $"counts {a[0]}/{a[1]} -> {b[0]}/{b[1]}";
        if(a[2]!=b[2])return "positions";
        return "thickness or direction";
    }
}

internal enum SnapshotUse
{
    /// <summary>Display and polling readers: may get the last good snapshot while the mesh is busy.</summary>
    Display,
    /// <summary>Apply/Convert: only a fresh clean read; a stale snapshot of another topology must never drive a write.</summary>
    Fresh
}

internal static class PlateMeshReader
{
    /// <summary>Ring bound. A native hole boundary adds loops to the face ring, so this is larger than a quad.</summary>
    internal const int MaxCorners=256;

    /// <summary>"Vertex|Edge" for index flags 3; "none" for 0.</summary>
    internal static string IndexFlagNames(int flags)
    {
        if(flags==0)return "none";
        var names=new List<string>();
        if((flags&1)!=0)names.Add("Vertex");if((flags&2)!=0)names.Add("Edge");if((flags&4)!=0)names.Add("Face");if((flags&8)!=0)names.Add("Loop");
        if((flags&~15)!=0)names.Add("0x"+(flags&~15).ToString("x"));
        return string.Join("|",names);
    }

    /// <summary>The state check and a bounded read. False with a reason; never throws for a bad mesh.</summary>
    internal static bool TryRead(IRawMesh mesh,out PlateMeshSnapshot? snapshot,out string reason,bool requireClean=true)
    {
        snapshot=null;reason="";
        try
        {
            if(mesh.Released){reason="mesh released";return false;}
            if(requireClean&&mesh.DirtyFlags!=0){reason="dirty flags "+mesh.DirtyFlags;return false;}
            // 0.22.27: elementIndexDirtyFlags only says Element.index is stale (native Delete* sets it; a spawned or edited plate keeps 3 =
            // Vertex|Edge until an operation renumbers). GetFace(i) is faces[i] by list position and the ring walk uses loop pointers, so
            // none of this read depends on an element index: the flags no longer refuse it (0.22.24-0.22.26 did, so a new plate never got read).
            var indexFlags=mesh.IndexDirtyFlags;
            int faces=mesh.FaceCount,loops=mesh.LoopCount;
            if(faces<=0||loops<=0){reason="empty mesh";return false;}
            // (0.22.27 disassembly: native get_FaceCount returns faces._size, so in the game this cannot differ; kept as a cheap guard.)
            if(mesh.FaceListCount!=faces){reason="faces list "+mesh.FaceListCount+" != FaceCount "+faces;return false;}
            var vertexIds=new Dictionary<long,int>();var vertices=new List<NVector>();var result=new SnapFace[faces];
            var hp=new Fnv();var ht=new Fnv();
                        for(int f=0;f<faces;f++)
            {
                var face=mesh.Face(f);
                if(face==null){reason="face "+f+" is null";return false;}
                if(face.Errors!=0){reason="face "+f+" does not validate ("+face.Errors+")";return false;}
                int n=face.VertexCount;
                if(n<3||n>MaxCorners){reason="face "+f+" has "+n+" corners";return false;}
                var first=face.FirstLoop;
                if(first==null){reason="face "+f+" has no first loop";return false;}
                var ids=new List<int>(n);var corners=new List<NVector>(n);var t=new List<ushort>(n);var along=new List<bool>(n);
                var loop=first;var closedEarly=false;
                for(int j=0;j<n;j++)
                {
                    if(loop==null){reason="face "+f+" ring ends after "+j+" of "+n+" corners";return false;}
                    // The outer ring closed before vertexCount: the face has further boundary rings (holes). Only the outer ring is read.
                    if(j>0&&loop.Key==first.Key){closedEarly=true;break;}
                    if(!Finite(loop.Position)){reason="face "+f+" corner "+j+" is not finite";return false;}
                    if(!vertexIds.TryGetValue(loop.VertexKey,out var id)){id=vertices.Count;vertexIds[loop.VertexKey]=id;vertices.Add(loop.Position);}
                    ids.Add(id);corners.Add(loop.Position);t.Add(loop.Thickness);along.Add(loop.AlongFaceNormal);
                    ht.Add((int)loop.Thickness);ht.Add(loop.AlongFaceNormal?1:0);hp.Add(loop.Position.X);hp.Add(loop.Position.Y);hp.Add(loop.Position.Z);
                    loop=loop.Next;
                }
                // A ring of exactly vertexCount corners must close on its first loop; one that ends in nothing or elsewhere is not a cycle.
                if(!closedEarly&&(loop==null||loop.Key!=first.Key)){reason="face "+f+" ring does not close after "+n+" corners";return false;}
                if(ids.Count<3){reason="face "+f+" ring has "+ids.Count+" corners";return false;}
                ht.Add(n);ht.Add(ids.Count);
                result[f]=new SnapFace(ids.ToArray(),corners.ToArray(),t.ToArray(),along.ToArray(),face.Normal,face.Selected,face.Culled,n-ids.Count);
            }
            snapshot=new(mesh.Pointer,faces,loops,result,vertices.ToArray(),$"{faces}:{loops}:{hp.Value:x16}:{ht.Value:x16}",indexFlags);
            return true;
        }
        catch(Exception ex){snapshot=null;reason="read failed: "+ex.GetType().Name;return false;}
    }
    private static bool Finite(NVector v)=>float.IsFinite(v.X)&&float.IsFinite(v.Y)&&float.IsFinite(v.Z);

    private struct Fnv
    {
        private ulong h;
        internal ulong Value=>h==0?14695981039346656037UL:h;
        private void Byte(byte b){if(h==0)h=14695981039346656037UL;h^=b;h*=1099511628211UL;}
        internal void Add(int v){for(int i=0;i<4;i++)Byte((byte)(v>>(8*i)));}
        internal void Add(float v)=>Add(BitConverter.SingleToInt32Bits(v));
    }
}

/// <summary>
/// The quiet period and the cache. Time and the mouse state are injected. <see cref="NoteModified"/> is the OnAssignedMeshModified
/// postfix: from then on, for <see cref="QuietSeconds"/> and while a mouse button is held in the designer, nothing touches the mesh.
/// </summary>
internal sealed class PlateMeshGate
{
    internal const float QuietSeconds=.5f;
    /// <summary>A cached clean snapshot is trusted this long; counts alone cannot see a thickness drag.</summary>
    internal const float MaxAgeSeconds=1f;
    private readonly Func<float> clock;
    private readonly Func<bool> mouseHeld;
    private readonly Func<bool>? ignoreDirty;
    private float quietUntil=float.NegativeInfinity;
    private sealed class Entry{internal PlateMeshSnapshot? Snapshot;internal int Faces,Loops;internal float Stamp;internal bool Stale;}
    private readonly Dictionary<long,Entry> cache=new();
    private readonly SortedSet<string> skipped=new(StringComparer.Ordinal);
    internal PlateMeshGate(Func<float> clock,Func<bool> mouseHeld,Func<bool>? ignoreDirty=null){this.clock=clock;this.mouseHeld=mouseHeld;this.ignoreDirty=ignoreDirty;}
    private bool RequireClean=>ignoreDirty?.Invoke()!=true;

    internal int CacheCount=>cache.Count;
    internal bool Quiet=>clock()<quietUntil;
    internal bool Busy
    {
        get{if(Quiet)return true;try{return mouseHeld();}catch(Exception){return false;}}
    }
    /// <summary>The native mesh changed: every cached snapshot of it is stale (kept for display only) and the quiet period starts.</summary>
    internal void NoteModified(long pointer)
    {
        quietUntil=clock()+QuietSeconds;
        if(cache.TryGetValue(pointer,out var entry))entry.Stale=true;
    }
    internal void Forget(long pointer)=>cache.Remove(pointer);
    internal void Clear(){cache.Clear();skipped.Clear();quietUntil=float.NegativeInfinity;}
    /// <summary>Readers that were turned away since the last call (for the one-line edit log).</summary>
    internal string[] TakeSkipped(){var all=skipped.ToArray();skipped.Clear();return all;}
    internal bool HasSnapshot(long pointer)=>cache.TryGetValue(pointer,out var e)&&e.Snapshot!=null;

    /// <summary>
    /// A snapshot for <paramref name="reader"/>, or null. Busy (quiet period or mouse held): Display gets the cached one, Fresh gets nothing.
    /// Not busy: a clean read (cache hit by pointer, face count and loop count), and if the mesh is not clean Display falls back to the cache.
    /// </summary>
    internal PlateMeshSnapshot? Get(IRawMesh mesh,string reader,SnapshotUse use,out string reason)
    {
        reason="";
        long pointer;
        try{pointer=mesh.Pointer;}catch(Exception){reason="mesh unreadable";skipped.Add(reader);return null;}
        cache.TryGetValue(pointer,out var entry);
        if(Busy)
        {
            skipped.Add(reader);reason=Quiet?"quiet period":"mouse button held";
            return use==SnapshotUse.Display?entry?.Snapshot:null;
        }
        var now=clock();
        try
        {
            if(entry!=null&&!entry.Stale&&entry.Snapshot!=null&&now-entry.Stamp<MaxAgeSeconds&&!mesh.Released
                &&(!RequireClean||mesh.DirtyFlags==0)&&mesh.FaceCount==entry.Faces&&mesh.LoopCount==entry.Loops)return entry.Snapshot;
        }
        catch(Exception){entry=null;}
        if(PlateMeshReader.TryRead(mesh,out var fresh,out reason,RequireClean)&&fresh!=null)
        {
            cache[pointer]=new Entry{Snapshot=fresh,Faces=fresh.FaceCount,Loops=fresh.LoopCount,Stamp=now,Stale=false};
            return fresh;
        }
        skipped.Add(reader);
        return use==SnapshotUse.Display?entry?.Snapshot:null;
    }
}

/// <summary>"To back plane" and the thickness direction (0.22.24): which corners of which faces the step sets to Along face normal.</summary>
internal sealed record ThickenModePlan(bool[][] Writes,bool[][] OldAlong,bool[][] NewAlong,int FacesChanged,int CornersChanged)
{
    internal bool Any=>CornersChanged>0;
    /// <summary>Along-flags after Execute (true) or after Revert (false): undo gives the old per-corner modes back.</summary>
    internal bool[][] After(bool execute)=>execute?NewAlong:OldAlong;
    /// <summary>
    /// Only To back plane touches the direction, and only on the faces that take part (the selection, or every face without one).
    /// Uniform never changes a mode. Faces that already are Along face normal stay as they are.
    /// </summary>
    internal static ThickenModePlan Create(ThicknessMode mode,IReadOnlyList<bool[]> along,IReadOnlyList<bool>? selected)
    {
        var faces=along.Count;var oldAlong=along.Select(a=>a.ToArray()).ToArray();var next=oldAlong.Select(a=>a.ToArray()).ToArray();
        var writes=along.Select(a=>new bool[a.Length]).ToArray();int facesChanged=0,corners=0;
        if(mode==ThicknessMode.ToBackPlane)
        {
            var only=CompositeThicknessPlan.SelectedCount(selected,faces)>0;
            for(int f=0;f<faces;f++)
            {
                if(only&&!(f<selected!.Count&&selected[f]))continue;
                bool changed=false;
                for(int j=0;j<along[f].Length;j++)
                    if(!along[f][j]){writes[f][j]=true;next[f][j]=true;corners++;changed=true;}
                if(changed)facesChanged++;
            }
        }
        return new(writes,oldAlong,next,facesChanged,corners);
    }
    /// <summary>"3 faces set to Along face normal" or null when nothing changes.</summary>
    internal string? Note=>CornersChanged==0?null:$"{FacesChanged} face{(FacesChanged==1?"":"s")} set to Along face normal (undo restores the old direction)";
}

/// <summary>
/// 0.22.24, pure: before the step is built, the mesh JSON the native serializer writes for the new copy is checked for the stored thickness
/// direction ("tm" per face, as in <see cref="CompositePlate.QuadMeshJson"/>): if the written modes are not what the plan says, the step is refused
/// and nothing has changed (the copy is discarded; the old mesh was never touched).
/// </summary>
internal static class ThickenModeJson
{
    /// <summary>Null when every face's stored direction agrees with <paramref name="expectedAlong"/>; otherwise the first disagreement.</summary>
    internal static string? Verify(string json,IReadOnlyList<bool[]> expectedAlong)
    {
        try
        {
            using var doc=System.Text.Json.JsonDocument.Parse(json);
            if(!doc.RootElement.TryGetProperty("faces",out var faces)||faces.ValueKind!=System.Text.Json.JsonValueKind.Array)return "the mesh JSON has no faces";
            if(faces.GetArrayLength()!=expectedAlong.Count)return $"the mesh JSON has {faces.GetArrayLength()} faces, expected {expectedAlong.Count}";
            int f=0;
            foreach(var face in faces.EnumerateArray())
            {
                var expected=expectedAlong[f];var all=expected.All(a=>a);var none=expected.All(a=>!a);
                if(!face.TryGetProperty("tm",out var tm))
                {if(!all)return $"face {f+1}: no stored thickness direction";f++;continue;} // absent = the default (Along face normal)
                if(tm.ValueKind==System.Text.Json.JsonValueKind.Number)
                {
                    var value=tm.GetInt32();
                    if(all&&value!=0)return $"face {f+1}: stored direction {value}, expected Along face normal";
                    if(!all&&!none)return $"face {f+1}: native stores one direction per face, mixed corners cannot be kept";
                }
                else if(tm.ValueKind==System.Text.Json.JsonValueKind.Array)
                {
                    int j=0;
                    foreach(var item in tm.EnumerateArray())
                    {
                        if(j>=expected.Length||(item.GetInt32()==0)!=expected[j])return $"face {f+1}, corner {j+1}: stored direction does not match";
                        j++;
                    }
                }
                f++;
            }
            return null;
        }
        catch(Exception ex){return "the mesh JSON could not be checked: "+ex.Message;}
    }
}

/// <summary>0.22.24: what the user may tune about the native thickener's limits for To back plane.</summary>
internal sealed record BackplaneLimits(double MinimumMm,double MaxCornerDeltaMm)
{
    /// <summary>No limit beyond 1 mm: the 0.22.23 behaviour, kept for callers that do not pass limits.</summary>
    internal static readonly BackplaneLimits None=new(BackplaneThickness.MinimumMm,double.PositiveInfinity);
    internal bool Valid=>double.IsFinite(MinimumMm)&&MinimumMm>=1&&MaxCornerDeltaMm>=1;
}

/// <summary>Result of <see cref="BackplaneThickness.Limit"/>: the limited thicknesses and how many corners had to move.</summary>
internal sealed record BackplaneLimitResult(ushort[][] Thicknesses,int Limited,int LimitedFaces);
