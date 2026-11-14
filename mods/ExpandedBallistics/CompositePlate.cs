using NVector=System.Numerics.Vector3;
namespace SprocketExpandedBallistics;

// Identity is authored and saved by the native mesh. Geometry never participates.
internal static class CompositePlate
{
    internal const string Name="Composite armour plate";
    // The native mesh blueprint saves its name. Keep the fork mode with that mesh so
    // undo/redo, linked copies and reload do not depend on the composer's current choice.
    internal const string BackplaneName=Name+" [To back plane]";
    internal const string FillName=Name+" [Fill to wall]";
    // 0.22.60: a plate in layers (one face per solid layer, or per block of touching layers). The material id stays the recipe.
    internal const string FrontMarker=" [FW75:";
    private static string? PlainName(string? name){if(name==null)return null;int i=name.IndexOf(FrontMarker,StringComparison.Ordinal);return i>=0?name[..i]:name;}
    internal const string LayersName=Name+" [Layers]";
    internal const string LayerBlocksName=Name+" [Layer blocks]";
    internal static string NameForMode(ThicknessMode mode)=>mode==ThicknessMode.FillToWall?FillName:mode==ThicknessMode.ToBackPlane?BackplaneName:mode==ThicknessMode.Layers?LayersName:mode==ThicknessMode.LayerBlocks?LayerBlocksName:Name;
    internal static ThicknessMode ModeFromName(string? name)=>string.Equals(PlainName(name),FillName,StringComparison.Ordinal)?ThicknessMode.FillToWall:string.Equals(PlainName(name),BackplaneName,StringComparison.Ordinal)?ThicknessMode.ToBackPlane:
        string.Equals(PlainName(name),LayersName,StringComparison.Ordinal)?ThicknessMode.Layers:string.Equals(PlainName(name),LayerBlocksName,StringComparison.Ordinal)?ThicknessMode.LayerBlocks:ThicknessMode.Uniform;
    /// <summary>0.22.60: the layout of a plate in layers, from its mesh name; null for every other plate.</summary>
    internal static LayerLayoutKind? LayoutFromName(string? name)=>string.Equals(PlainName(name),LayersName,StringComparison.Ordinal)?LayerLayoutKind.PerLayer:string.Equals(PlainName(name),LayerBlocksName,StringComparison.Ordinal)?LayerLayoutKind.PerBlock:null;
    internal const string PrefabGuid="e229025a-a35f-47df-a018-f0eabc220001";
    internal const string IconFile="compositeArmourPlate-icon.png";
    /// <summary>The native cube add-on prefab, the only prefab the game lists itself.</summary>
    internal const string CubePrefabGuid="9f8a9d20-eb45-482e-b149-014c964c4e2c";
    /// <summary>
    /// Packed <c>PlateStructureMesh.FromJson</c> input: the MeshBlueprint itself, exactly what
    /// <c>PlateStructureMesh.ToJson</c> returns (majorVersion/minorVersion and the edge arrays at the
    /// root, no name/wrapper). A 1×1 m quad, 20 mm, front +Y, authored 20 mm above the mounting plane,
    /// thickening along the face normal. The name is carried by the prefab's
    /// PlateStructureMeshBlueprint and, on the cube route, by <c>ToMeshBlueprint</c>/<c>LoadBlueprint</c>
    /// before the mesh is assigned.
    /// </summary>
    internal const string QuadMeshJson="{\"majorVersion\":0,\"minorVersion\":3,"+
        "\"vertices\":[-0.5,0.02,-0.5,-0.5,0.02,0.5,0.5,0.02,0.5,0.5,0.02,-0.5],"+
        "\"edges\":[0,1,1,2,2,3,3,0],\"edgeFlags\":[0,0,0,0],"+
        "\"faces\":[{\"v\":[0,1,2,3],\"t\":[20,20,20,20],\"tm\":0,\"te\":0}]}";
    /// <summary>Prefab GUID to card icon file under BepInEx/plugins/SprocketExpandedBallistics/era-assets.</summary>
    internal static readonly IReadOnlyDictionary<string,string> IconMap=
        new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase){[PrefabGuid]=IconFile};
    internal static bool IsName(string? name)=>string.Equals(PlainName(name),Name,StringComparison.Ordinal)||string.Equals(PlainName(name),BackplaneName,StringComparison.Ordinal)||string.Equals(PlainName(name),FillName,StringComparison.Ordinal)||
        string.Equals(PlainName(name),LayersName,StringComparison.Ordinal)||string.Equals(PlainName(name),LayerBlocksName,StringComparison.Ordinal);
    internal static bool IsCassette(string? id)=>id?.StartsWith("ebCassette_",StringComparison.Ordinal)==true;
    /// <summary>
    /// 0.22.7 identity: the native mesh name OR a cassette material. A cassette can only ever be set on a
    /// composite plate, and the mesh copy a thickness/material operation makes carries no name, so the
    /// material id is as good a proof as the name (it also heals plates a 0.22.6 Apply left unnamed).
    /// </summary>
    internal static bool IsComposite(string? meshName,string? armourTechId)=>IsName(meshName)||IsCassette(armourTechId);
    /// <summary>
    /// Per-mesh-pointer name cache. A positive result stays until <see cref="Invalidate"/>; a negative one only
    /// lives <see cref="NegativeSeconds"/>, so a name that is applied a moment after the first (unnamed) read
    /// can never stay hidden behind an old <c>false</c>.
    /// </summary>
    internal sealed class Cache
    {
        internal const double NegativeSeconds=1.5;
        private readonly Dictionary<IntPtr,(bool Value,double Stamp)> entries=new();
        private readonly Func<double> clock;
        internal Cache(Func<double>? clock=null){this.clock=clock??(()=>Environment.TickCount64/1000d);}
        internal int Count=>entries.Count;
        internal bool Get(IntPtr pointer,Func<string?> readName)
        {
            if(pointer==IntPtr.Zero)return false;
            var now=clock();
            if(entries.TryGetValue(pointer,out var entry)&&(entry.Value||now-entry.Stamp<NegativeSeconds))return entry.Value;
            var value=IsName(readName());entries[pointer]=(value,now);return value;
        }
        internal void Invalidate(IntPtr pointer)=>entries.Remove(pointer);
        internal void Clear()=>entries.Clear();
    }
}

/// <summary>
/// 0.22.14, pure: which plates "Convert to composite plate" may turn into a composite plate, and the naming rule of its undo step.
/// Only add-on structures (Applique) with a live mesh that are not part of a mirrored pair. A null blocker means eligible.
/// </summary>
internal static class CompositeConversion
{
    /// <summary>Default for a target whose eligibility was not read (keeps the pre-0.22.14 wording).</summary>
    internal const string NotRead="Select a Composite armour plate (Addon Structures).";
    internal const string HullOrTurret="Hull and turret stay native: add a Composite armour plate on top.";
    internal const string Mirrored="Mirrored pair: remove the mirrored pairing first.";
    internal const string NoMesh="This plate has no editable mesh.";
    internal const string Already="Already a composite plate.";
    internal static string? Blocker(bool applique,bool hasMesh,bool released,bool mirrored,bool composite)
    {
        if(composite)return Already;
        if(!applique)return HullOrTurret;
        if(!hasMesh||released)return NoMesh;
        if(mirrored)return Mirrored;
        return null;
    }
    /// <summary>
    /// Whether the mesh a step ends on gets the identity name. Execute and redo always name the new mesh. Undo names the old
    /// mesh only when it already was a composite plate: undoing a conversion must leave a normal plate, and must never name
    /// a mesh an Alt-duplicate still shares.
    /// </summary>
    internal static bool NameAfter(bool execute,bool oldWasComposite)=>execute||oldWasComposite;
    /// <summary>"Unlinks it from 2 linked copies; they stay normal plates." or "" without linked copies.</summary>
    internal static string LinkedNote(int linkedCopies)=>linkedCopies<=0?"":
        $"Unlinks it from {linkedCopies} linked cop{(linkedCopies==1?"y":"ies")}; {(linkedCopies==1?"it stays a normal plate":"they stay normal plates")}.";
}

/// <summary>
/// 0.22.14, pure: what one composite edit step restores on undo. The old mesh is kept unchanged by the step (the edit works
/// on a copy), so undo puts back its name, material and every loop thickness; only the naming rule needs the old identity.
/// </summary>
internal sealed record CompositeUndoSnapshot(string OldMaterial,string NewMaterial,bool OldWasComposite,ushort[][] OldThicknesses,CompositeThicknessPlan Plan,bool[][]? OldAlong=null,bool[][]? NewAlong=null)
{
    /// <summary>0.22.24: per-corner "Along face normal" flags after Execute (true) or after Revert (false); null when the step did not record directions.</summary>
    internal bool[][]? AlongAfter(bool execute)=>execute?NewAlong:OldAlong;
    internal bool NameOnExecute=>CompositeConversion.NameAfter(true,OldWasComposite);
    internal bool NameOnRevert=>CompositeConversion.NameAfter(false,OldWasComposite);
    /// <summary>Material and loop thicknesses after Execute (true) or after Revert (false).</summary>
    internal (string Material,ushort[][] Thicknesses) After(bool execute)=>execute?(NewMaterial,Plan.Thicknesses):(OldMaterial,OldThicknesses);
}

internal sealed record CompositeThicknessPlan(string MaterialId,ushort[][] Thicknesses)
{
    // A value snapshot for Execute and Revert. No shape or uniformity requirement.
    internal static CompositeThicknessPlan Create(string materialId,IReadOnlyList<ushort[]> old,double? depthMm)=>Create(materialId,old,depthMm,null);
    /// <summary>
    /// 0.22.14: <paramref name="selected"/> marks the faces selected in the designer. With at least one selected face only
    /// those get the depth and every other face keeps its own loop thicknesses; with none (or null) every face gets it.
    /// </summary>
    internal static CompositeThicknessPlan Create(string materialId,IReadOnlyList<ushort[]> old,double? depthMm,IReadOnlyList<bool>? selected)
    {
        ushort? thickness=null;
        if(depthMm.HasValue)
        {
            var rounded=Math.Round(depthMm.Value,MidpointRounding.AwayFromZero);
            if(!double.IsFinite(rounded)||rounded<1||rounded>short.MaxValue)throw new ArgumentOutOfRangeException(nameof(depthMm));
            thickness=(ushort)rounded;
        }
        var only=SelectedCount(selected,old.Count)>0;
        return new(materialId,old.Select((face,f)=>!only||f<selected!.Count&&selected[f]?face.Select(t=>thickness??t).ToArray():face.ToArray()).ToArray());
    }
    /// <summary>Selected faces among the first <paramref name="faces"/>; 0 means "all faces".</summary>
    internal static int SelectedCount(IReadOnlyList<bool>? selected,int faces)
    {
        if(selected==null)return 0;
        int n=0;for(int f=0;f<Math.Min(faces,selected.Count);f++)if(selected[f])n++;
        return n;
    }
    /// <summary>"Applies to 4 selected faces" or "Applies to all 16 faces".</summary>
    internal static string Scope(int faces,int selectedFaces)=>selectedFaces>0&&selectedFaces<=faces
        ?$"Applies to {selectedFaces} selected face{(selectedFaces==1?"":"s")}"
        :faces==1?"Applies to the only face":$"Applies to all {faces} faces";
    /// <summary>Faces whose loops change, so a write never flattens the per-corner thickness of a face it should keep.</summary>
    internal bool Changes(IReadOnlyList<ushort[]> old,int face)=>!Thicknesses[face].SequenceEqual(old[face]);
}

/// <summary>0.22.16.1 fork: Uniform writes D everywhere; ToBackPlane derives each corner from one back plane.</summary>
internal enum ThicknessMode { Uniform=0, ToBackPlane=1, FillToWall=2, Layers=3, LayerBlocks=4 }

internal sealed record BackplaneThicknessPlan(CompositeThicknessPlan Plan,double MinMm,double MaxMm,int Faces,int ClampedCorners,int ClampedFaces,double DepthMm,int LimitedCorners=0)
{
    /// <summary>"2 faces, 67.7-532 mm t, 0 clamped": the required Apply message.</summary>
    internal string Summary=>$"{Faces} face{(Faces==1?"":"s")}, {MinMm:0.#}\u2013{MaxMm:0.#} mm t, {ClampedCorners} clamped"+(LimitedCorners>0?$", {LimitedCorners} limited":"");
}

/// <summary>
/// 0.22.16.1 fork, pure. Native extrudes a face corner along that face's outward normal
/// (<c>position - normal * thicknessMm * .001</c>, WAVE2A-NOTES.md). For one flat back plane with normal N
/// at D behind the frontmost involved point, the corner thickness is
/// <c>t = (dot(p,N) - s0) / dot(n,N)</c>, including negative denominators. Near-zero denominators
/// use 2*D; otherwise only out-of-range results are clamped to [1 mm, 2*D]. Unselected faces keep their own
/// per-corner thickness.
/// </summary>
internal static class BackplaneThickness
{
    internal const double MinimumMm=1;
    // Reject other native directions before any mesh copy or undo operation. Merely
    // writing thickenMode would not prove that native's direction caches are invalidated.
    internal static void RequireFaceNormal(ThicknessMode mode,bool involved,bool alongFaceNormal,int face,int corner)
    {
        if(mode==ThicknessMode.ToBackPlane&&involved&&!alongFaceNormal)
            throw new InvalidOperationException($"To back plane requires AlongFaceNormal: face {face+1}, corner {corner+1}. Set its native thickness direction to Along face normal first.");
    }
    internal const string FlatPlateNote="flat plate: applied uniform";
    // 0.22.22: a back plane that has nothing to do uses Uniform silently. That is the case when every involved face lies in one plane (a flat plate:
    // front and back faces cancel, or all faces share one plane), or when the back-plane result is already D on every involved corner. Anything else
    // (a real cancelling normal average of a solid shape, other thickness directions) keeps its error.
    internal static bool IsFlat(IReadOnlyList<NVector[]> vertices,IReadOnlyList<NVector> normals,IReadOnlyList<bool>? selected,double toleranceM=1e-5)
    {
        var faces=vertices.Count;var only=false;
        if(selected!=null)for(int f=0;f<Math.Min(faces,selected.Count);f++)if(selected[f]){only=true;break;}
        NVector? axis=null;NVector origin=NVector.Zero;
        for(int f=0;f<faces;f++)
        {
            if(only&&!(f<selected!.Count&&selected[f]))continue;
            if(vertices[f].Length==0)continue;
            if(axis==null)
            {
                var n=Newell(vertices[f]);if(n.LengthSquared()<.5f)n=Unit(normals[f]);
                if(n.LengthSquared()<.5f)continue;
                axis=n;origin=vertices[f][0];
            }
            foreach(var p in vertices[f])if(Math.Abs(NVector.Dot(p-origin,axis.Value))>toleranceM)return false;
        }
        return axis!=null;
    }
    // Back plane, or Uniform when the plate is flat or the plan is already uniform; flat tells which one happened.
    internal static CompositeThicknessPlan CreateOrUniform(string materialId,IReadOnlyList<ushort[]> old,
        IReadOnlyList<NVector[]> vertices,IReadOnlyList<NVector> normals,IReadOnlyList<bool>? selected,double depthMm,out string summary,out bool flat,BackplaneLimits? limits=null)
    {
        flat=false;summary="";
        if(IsFlat(vertices,normals,selected))
        {
            flat=true;summary=FlatPlateNote;
            return CompositeThicknessPlan.Create(materialId,old,depthMm,selected);
        }
        var result=Create(materialId,old,vertices,normals,selected,depthMm,limits);
        var rounded=Math.Round(depthMm,MidpointRounding.AwayFromZero);
        if(result.ClampedCorners==0&&result.LimitedCorners==0&&result.MinMm==rounded&&result.MaxMm==rounded)
        {
            flat=true;summary=FlatPlateNote;
            return CompositeThicknessPlan.Create(materialId,old,depthMm,selected);
        }
        summary=result.Summary;
        return result.Plan;
    }
    internal static BackplaneThicknessPlan Create(string materialId,IReadOnlyList<ushort[]> old,
        IReadOnlyList<NVector[]> vertices,IReadOnlyList<NVector> normals,IReadOnlyList<bool>? selected,double depthMm,BackplaneLimits? limits=null)
    {
        limits??=BackplaneLimits.None;
        if(!limits.Valid)throw new ArgumentOutOfRangeException(nameof(limits));
        if(!double.IsFinite(depthMm)||depthMm<MinimumMm||depthMm>short.MaxValue/2d)
            throw new ArgumentOutOfRangeException(nameof(depthMm));
        var faces=old.Count;
        if(vertices.Count!=faces||normals.Count!=faces)throw new ArgumentException("Back-plane geometry does not match the face count.");
        var selectedCount=0;
        if(selected!=null)for(int f=0;f<Math.Min(faces,selected.Count);f++)if(selected[f])selectedCount++;
        var only=selectedCount>0;
        var involved=new bool[faces];var involvedCount=0;
        for(int f=0;f<faces;f++){involved[f]=!only||f<selected!.Count&&selected[f];if(involved[f])involvedCount++;}
        if(involvedCount==0)throw new ArgumentException("Back-plane mode has no faces to thicken.",nameof(selected));

        // Surface-area-weighted average outward normal of the involved faces.
        var normalSum=NVector.Zero;
        double totalWeight=0;
        for(int f=0;f<faces;f++)
        {
            if(!involved[f])continue;
            var n=Unit(normals[f]);
            if(n.LengthSquared()<.5f)n=Newell(vertices[f]);
            if(n.LengthSquared()<.5f)continue;
            var area=Area(vertices[f]);
            var weight=area>1e-9f?area:1f;
            normalSum+=n*weight;totalWeight+=weight;
        }
        // A cancelling average has no preferred direction. Refuse it consistently,
        // rather than choosing whichever face happened to be enumerated first.
        if(totalWeight==0||normalSum.Length()<=totalWeight*1e-6)
            throw new ArgumentException("To back plane: the average face normal cancels out. Select faces with a common direction.",nameof(normals));
        var planeNormal=Unit(normalSum);

        var front=-double.MaxValue;
        for(int f=0;f<faces;f++)
        {
            if(!involved[f])continue;
            foreach(var p in vertices[f])front=Math.Max(front,NVector.Dot(p,planeNormal));
        }
        if(!double.IsFinite(front))throw new ArgumentException("Back-plane geometry has no front point.",nameof(vertices));
        var back=front-depthMm*.001;var maxMm=2*depthMm;

        var thicknesses=new ushort[faces][];
        double min=double.MaxValue,max=0;var clampedCorners=0;var clampedFaces=0;
        for(int f=0;f<faces;f++)
        {
            if(!involved[f]){thicknesses[f]=old[f].ToArray();continue;}
            var points=vertices[f];var n=Unit(normals[f]);
            if(n.LengthSquared()<.5f)n=Newell(points);
            if(n.LengthSquared()<.5f)n=planeNormal;
            var denominator=NVector.Dot(n,planeNormal);
            var corners=new ushort[points.Length];var faceClamped=false;
            for(int j=0;j<points.Length;j++)
            {
                var exact=Math.Abs(denominator)>1e-6?((NVector.Dot(points[j],planeNormal)-back)/denominator)*1000:double.PositiveInfinity;
                var clamped=!double.IsFinite(exact)||exact<Math.Min(limits.MinimumMm,Math.Floor(maxMm))||exact>maxMm;
                if(clamped)faceClamped=true;
                if(!double.IsFinite(exact))exact=maxMm;
                var floorMm=Math.Min(limits.MinimumMm,Math.Floor(maxMm));
                var value=Math.Clamp(Math.Round(Math.Clamp(exact,floorMm,maxMm),MidpointRounding.AwayFromZero),floorMm,Math.Floor(maxMm));
                corners[j]=(ushort)value;
                min=Math.Min(min,value);max=Math.Max(max,value);
                if(clamped)clampedCorners++;
            }
            thicknesses[f]=corners;
            if(faceClamped)clampedFaces++;
        }
        var limited=0;
        if(!double.IsPositiveInfinity(limits.MaxCornerDeltaMm))
        {
            var result=Limit(thicknesses,vertices,involved,limits.MaxCornerDeltaMm);
            limited=result.Limited;thicknesses=result.Thicknesses;
            min=double.MaxValue;max=0;
            for(int f=0;f<faces;f++)if(involved[f])foreach(var v in thicknesses[f]){min=Math.Min(min,v);max=Math.Max(max,v);}
        }
        return new(new CompositeThicknessPlan(materialId,thicknesses),min,max,involvedCount,clampedCorners,clampedFaces,depthMm,limited);
    }
    /// <summary>
    /// 0.22.24 mitigation A: the native thickener rebuilds from the per-corner values, and a corner next to one 500 mm thicker is the most
    /// suspect input of the face-delete crash. Neighbouring corners (the next corner in a face ring, and corners of involved faces at the same
    /// position) may differ by at most <paramref name="maxDeltaMm"/>; the thicker one is lowered. Only involved faces change.
    /// </summary>
    internal static BackplaneLimitResult Limit(ushort[][] thicknesses,IReadOnlyList<NVector[]> vertices,IReadOnlyList<bool> involved,double maxDeltaMm)
    {
        var result=thicknesses.Select(f=>f.ToArray()).ToArray();var faces=result.Length;
        var pairs=new List<(int Fa,int Ja,int Fb,int Jb)>();
        var byPosition=new Dictionary<(long,long,long),List<(int F,int J)>>();
        for(int f=0;f<faces;f++)
        {
            if(!involved[f])continue;
            var n=result[f].Length;
            for(int j=0;j<n;j++)
            {
                if(n>1)pairs.Add((f,j,f,(j+1)%n));
                var p=vertices[f][j];var key=((long)Math.Round(p.X*1e5),(long)Math.Round(p.Y*1e5),(long)Math.Round(p.Z*1e5));
                if(!byPosition.TryGetValue(key,out var list))byPosition[key]=list=new();
                list.Add((f,j));
            }
        }
        foreach(var list in byPosition.Values)
            for(int a=0;a<list.Count;a++)for(int b=a+1;b<list.Count;b++)pairs.Add((list[a].F,list[a].J,list[b].F,list[b].J));
        var limit=(int)Math.Min(ushort.MaxValue,Math.Floor(maxDeltaMm));
        bool changed=true;
        for(int pass=0;changed&&pass<64;pass++)
        {
            changed=false;
            foreach(var (fa,ja,fb,jb) in pairs)
            {
                int a=result[fa][ja],b=result[fb][jb];
                if(Math.Abs(a-b)<=limit)continue;
                if(a>b)result[fa][ja]=(ushort)(b+limit);else result[fb][jb]=(ushort)(a+limit);
                changed=true;
            }
        }
        int limited=0,limitedFaces=0;
        for(int f=0;f<faces;f++)
        {
            int c=0;for(int j=0;j<result[f].Length;j++)if(result[f][j]!=thicknesses[f][j])c++;
            limited+=c;if(c>0)limitedFaces++;
        }
        return new(result,limited,limitedFaces);
    }
    private static NVector Unit(NVector v)
    {
        var length=v.Length();
        return float.IsFinite(length)&&length>1e-9f?v/length:NVector.Zero;
    }
    private static NVector Newell(IReadOnlyList<NVector> points)
    {
        if(points.Count<3)return NVector.Zero;
        var sum=NVector.Zero;
        for(int i=0;i<points.Count;i++)
        {
            var a=points[i];var b=points[(i+1)%points.Count];
            sum+=NVector.Cross(a,b);
        }
        return Unit(sum);
    }
    private static float Area(IReadOnlyList<NVector> points)
    {
        if(points.Count<3)return 0;
        var sum=NVector.Zero;
        for(int i=0;i<points.Count;i++)sum+=NVector.Cross(points[i],points[(i+1)%points.Count]);
        return sum.Length()*.5f;
    }
}
