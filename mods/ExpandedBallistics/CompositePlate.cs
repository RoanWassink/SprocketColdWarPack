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
    // 0.22.82: Solid block is a mode of its own, so Apply, Undo/Redo and the renaming after every step keep the mark (EnsureName names a mesh by its mode).
    internal static string NameForMode(ThicknessMode mode)=>mode==ThicknessMode.FillToWall?FillName:mode==ThicknessMode.ToBackPlane?BackplaneName:mode==ThicknessMode.Layers?LayersName:mode==ThicknessMode.LayerBlocks?LayerBlocksName:mode==ThicknessMode.SolidBlock?SolidBlockName:Name;
    internal static ThicknessMode ModeFromName(string? name)=>string.Equals(PlainName(name),FillName,StringComparison.Ordinal)?ThicknessMode.FillToWall:string.Equals(PlainName(name),BackplaneName,StringComparison.Ordinal)?ThicknessMode.ToBackPlane:
        string.Equals(PlainName(name),LayersName,StringComparison.Ordinal)?ThicknessMode.Layers:string.Equals(PlainName(name),LayerBlocksName,StringComparison.Ordinal)?ThicknessMode.LayerBlocks:
        string.Equals(PlainName(name),SolidBlockName,StringComparison.Ordinal)?ThicknessMode.SolidBlock:ThicknessMode.Uniform;
    /// <summary>0.22.60: the layout of a plate in layers, from its mesh name; null for every other plate.</summary>
    internal static LayerLayoutKind? LayoutFromName(string? name)=>string.Equals(PlainName(name),LayersName,StringComparison.Ordinal)?LayerLayoutKind.PerLayer:string.Equals(PlainName(name),LayerBlocksName,StringComparison.Ordinal)?LayerLayoutKind.PerBlock:null;
    // 0.22.77 (proef): a closed block that the hit counts as one solid shape. The marker lives in the mesh name, as [Layers] does; no geometry in the name.
    internal const string SolidBlockName=Name+" [Solid block]";
    internal static bool IsSolidBlock(string? name)=>string.Equals(PlainName(name),SolidBlockName,StringComparison.Ordinal);
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
internal enum ThicknessMode { Uniform=0, ToBackPlane=1, FillToWall=2, Layers=3, LayerBlocks=4, SolidBlock=5 }

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

// ======== 0.22.77 proef "Solid block": a closed block counts as one solid shape (pure; the hit runs in RuntimeGroupedProtection.cs) ========
/// <summary>One stretch of the shot line, metres on the persistent ray.</summary>
internal readonly record struct SolidSpan(double StartM,double EndM)
{
    internal double LengthMm=>(EndM-StartM)*1000;
}
/// <summary>One closed passage through a solid block: the front skin, the back skin, and between them the cavity. Inside runs from the entry of the front skin to the exit of the back skin.</summary>
internal readonly record struct SolidPassage(SolidSpan Front,SolidSpan Back)
{
    internal SolidSpan Cavity=>new(Front.EndM,Back.StartM);
    internal SolidSpan Inside=>new(Front.StartM,Back.EndM);
}
/// <summary>What one shot line sees of one plate marked [Solid block]: the surfaces it crosses, its skins (merged pieces) and the passages (pairs of skins). Odd: the skins cannot be paired.</summary>
internal sealed record SolidLine(int Surfaces,SolidSpan[] Skins,SolidPassage[] Passages,bool Odd);
/// <summary>One cavity this block pays: the passage, its place in the line, and the ledger plan of what the line has not paid yet.</summary>
internal sealed record SolidCharge(SolidPassage Passage,int Number,int Count,double Factor,double Angle,MaterialIntervalPlan Plan)
{
    internal double CavityMm=>Plan.Pieces.Sum(p=>p.LengthMm);
    internal double RhaMm=>Plan.RhaMm;
}

/// <summary>
/// 0.22.77 (proef, user decision 14-11: "Een dichte doos voor complexe vormen"). A plate whose mesh name carries [Solid block] and whose material is a recipe counts as one
/// solid shape: what lies between its front and back skin is paid as the recipe, at the table factor of the recipe x the path (not the uniform layer scale).
/// The game hands every block the whole intersection list of the shot (not sorted along the line; Enter/Exit come from the winding of the triangle, and the user's mesh has
/// reversed faces), so the surfaces of this plate are sorted by distance and paired on that order: 1-2, 3-4, ... are its skins. Skins that overlap or lie less than
/// 1 mm apart are one skin. The skins are paired on parity again: inside runs from the entry of the first to the exit of the second skin. An odd count (an open shape,
/// a seam or a glancing shot) pays the skins only. Pure and read-only: the ledger is planned, never committed here.
/// </summary>
internal static class SolidBlock
{
    /// <summary>Skins closer than this are one skin (1 mm).</summary>
    internal const double MergeGapM=.001;
    /// <summary>Surfaces closer than this along the line are one surface when the count is odd (a shot along an edge shared by two triangles reports it twice).</summary>
    internal const double DuplicateM=1e-6;
    /// <summary>The surfaces of one plate on the shot line (distances in metres on the persistent ray, any order, flags ignored) as skins and passages.</summary>
    internal static SolidLine Analyse(IEnumerable<double> distancesM)
    {
        var raw=distancesM.Where(double.IsFinite).OrderBy(d=>d).ToList();
        var surfaces=raw;
        if(raw.Count%2!=0)
        {
            surfaces=new List<double>(raw.Count);
            foreach(var d in raw)if(surfaces.Count==0||d-surfaces[^1]>DuplicateM)surfaces.Add(d);
        }
        if(surfaces.Count==0)return new(0,Array.Empty<SolidSpan>(),Array.Empty<SolidPassage>(),false);
        if(surfaces.Count%2!=0)return new(surfaces.Count,Array.Empty<SolidSpan>(),Array.Empty<SolidPassage>(),true);
        var skins=new List<SolidSpan>();
        for(int i=0;i+1<surfaces.Count;i+=2)
        {
            var piece=new SolidSpan(surfaces[i],surfaces[i+1]);
            if(skins.Count>0&&piece.StartM-skins[^1].EndM<MergeGapM)skins[^1]=new(skins[^1].StartM,Math.Max(skins[^1].EndM,piece.EndM));
            else skins.Add(piece);
        }
        if(skins.Count%2!=0)return new(surfaces.Count,skins.ToArray(),Array.Empty<SolidPassage>(),true);
        var passages=new SolidPassage[skins.Count/2];
        for(int i=0;i<passages.Length;i++)passages[i]=new(skins[2*i],skins[2*i+1]);
        return new(surfaces.Count,skins.ToArray(),passages,false);
    }
    /// <summary>The passages whose front skin lies in this block: the block of the front skin pays the cavity. A block that starts behind the front skin (a child that starts inside) never does.</summary>
    internal static IEnumerable<(SolidPassage Passage,int Number)> Due(SolidLine line,double blockStartM,double blockEndM)
    {
        if(line.Odd||!(blockEndM>blockStartM))yield break;
        for(int i=0;i<line.Passages.Length;i++)
        {
            var p=line.Passages[i];
            if(p.Front.StartM<blockEndM&&p.Front.EndM>blockStartM+DuplicateM&&p.Cavity.EndM>p.Cavity.StartM)yield return(p,i+1);
        }
    }
    /// <summary>
    /// The cavities this block pays, each planned on the ledger of the ray (what the line already paid, for instance by the parent of a child fragment, stays out).
    /// The factor is the table factor of the recipe at the entry angle of the front skin (<paramref name="angleOf"/>, NaN: that passage is not paid).
    /// </summary>
    internal static List<SolidCharge> Plan(SolidLine line,double blockStartM,double blockEndM,MaterialIntervalLedger ledger,int entity,string material,string label,
        Func<SolidPassage,double> angleOf,Func<double,double> factorAt)
    {
        var result=new List<SolidCharge>();
        foreach(var (p,number) in Due(line,blockStartM,blockEndM))
        {
            var angle=angleOf(p);if(!double.IsFinite(angle))continue;
            var factor=factorAt(angle);if(!double.IsFinite(factor)||factor<0)continue;
            var plan=ledger.Plan(p.Cavity.StartM,p.Cavity.EndM,new[]{new MaterialInterval(entity,material,label,p.Cavity.StartM,p.Cavity.EndM,factor,angle)});
            if(plan==null||plan.Pieces.Length==0)continue;
            result.Add(new(p,number,line.Passages.Length,factor,angle,plan));
        }
        return result;
    }
    /// <summary>
    /// Runs the new path for one plate; an exception falls back to the old calculation for that block (the caller then pays the skins only) with one log line per plate per
    /// session. Never throws.
    /// </summary>
    internal static bool Guard(int plate,ISet<int> failed,Action<string> log,Action body)
    {
        try{body();return true;}
        catch(Exception error)
        {
            try{if(failed.Count<4096&&failed.Add(plate))log("[Solid block] Plate #"+plate.ToString(System.Globalization.CultureInfo.InvariantCulture)+": the solid-block calculation failed ("+error.GetType().Name+": "+error.Message+"); this block keeps the calculation without the cavity (skins only). Logged once per plate per session.");}
            catch(Exception){}
            return false;
        }
    }
    /// <summary>The line in the combat report (a materialReaction note in the block of the front skin): front skin, cavity x factor = steel equivalent, back skin.</summary>
    internal static string ReportText(string label,SolidCharge c,string threat)=>string.Create(System.Globalization.CultureInfo.InvariantCulture,
        $"{label}: solid block{(c.Count>1?$", passage {c.Number} of {c.Count}":"")}: front skin {c.Passage.Front.LengthMm:0.#} mm, cavity {c.CavityMm:0.#} mm × {c.Factor:0.00} ({threat} @{c.Angle:0}°) = {c.RhaMm:0} mm RHA-eq, back skin {c.Passage.Back.LengthMm:0.#} mm");
    /// <summary>The report line of an odd count of skins: only the skins are paid.</summary>
    internal static string OddText(string label,SolidLine line)=>string.Create(System.Globalization.CultureInfo.InvariantCulture,
        $"{label}: solid block, {line.Surfaces} surfaces on this shot line (odd): the shape is not closed here (open side, seam or glancing shot); only the skins are paid.");
    /// <summary>The log line (Composite fill / Layer hit log): every number of the cavity this block pays, and the budget before and after.</summary>
    internal static string LogText(long entity,SolidCharge c,double beforeMm,double afterMm)=>string.Create(System.Globalization.CultureInfo.InvariantCulture,
        $"[Solid block] plate #{entity}: passage {c.Number} of {c.Count}: front skin [{c.Passage.Front.StartM:0.######},{c.Passage.Front.EndM:0.######}] {c.Passage.Front.LengthMm:0.###} mm; cavity [{c.Passage.Cavity.StartM:0.######},{c.Passage.Cavity.EndM:0.######}] {c.CavityMm:0.###} mm x {c.Factor:0.####} = {c.RhaMm:0.###} mm RHA-eq at {c.Angle:0.#}°; back skin [{c.Passage.Back.StartM:0.######},{c.Passage.Back.EndM:0.######}] {c.Passage.Back.LengthMm:0.###} mm; budget {beforeMm:0.#} -> {afterMm:0.#} mm.");
    /// <summary>The log line of an odd count of skins.</summary>
    internal static string OddLogText(long entity,SolidLine line)=>string.Create(System.Globalization.CultureInfo.InvariantCulture,
        $"[Solid block] plate #{entity}: {line.Surfaces} surfaces, {line.Skins.Length} skins on this line (odd): skins only.");
}

// ======== 0.22.82 "Solid block" as a choice of Apply (pure; the runtime glue is RuntimeCompositePlateEdit.TrySolidBlock and RuntimeLayerFaces.Weigh) ========
/// <summary>One gap of a shape that is not closed: its open edges (one connected boundary), the faces they belong to (1-based), its centre (metres, plate frame) and its piece.</summary>
internal sealed record SolidGap(int Edges,int[] Faces,NVector CentreM,int Piece);
/// <summary>
/// What a plate mesh is as a closed shape: per piece (faces joined by edges) its enclosed volume, the faces that had to be turned to face outward, and, when it is not
/// closed, the gaps (open edges), the edges shared by more than two faces, and the refusal. <see cref="ClosestOpenPointMm"/>: the smallest distance from a point of an open
/// edge to any other point of the mesh; <see cref="ToleranceGap"/>: the mesh would be closed if points closer than 0.1 mm were one point (reported, never repaired).
/// </summary>
internal sealed record SolidShapeResult(int Faces,double[] PieceVolumesM3,int[] PieceFaces,int ReversedFaces,SolidGap[] Gaps,int OpenEdges,int SharedEdges,double ClosestOpenPointMm,bool ToleranceGap,string? Refusal)
{
    internal bool Closed=>Refusal==null;
    internal int Pieces=>PieceVolumesM3.Length;
    internal double VolumeM3=>PieceVolumesM3.Sum();
}
/// <summary>
/// 0.22.82 (point A and C of the brief): the closed volume of a plate as the user drew it. Every edge must belong to exactly two faces; per piece the faces are turned to one
/// side along their shared edges (a user's mesh can hold faces that wind the other way: Object 292) and the whole piece outward when its volume is negative; the volume is
/// the divergence theorem over the fan triangles of every face (corner 0 to j to j+1), in double precision. The geometry is only read, never changed.
/// </summary>
internal static class SolidShape
{
    /// <summary>Points closer than this are reported as a tolerance gap (0.1 mm).</summary>
    internal const double ToleranceM=1e-4;
    internal const string NotClosed="This plate is not a closed shape: close it, or use Thickness only / Layers.";
    private static string I(double v,string f)=>v.ToString(f,System.Globalization.CultureInfo.InvariantCulture);
    private static string List(IEnumerable<int> faces){var a=faces.Distinct().OrderBy(x=>x).ToArray();return string.Join(", ",a.Take(8).Select(x=>x.ToString(System.Globalization.CultureInfo.InvariantCulture)))+(a.Length>8?$" and {a.Length-8} more":"");}
    private static SolidShapeResult Refused(int faces,string why)=>new(faces,Array.Empty<double>(),Array.Empty<int>(),0,Array.Empty<SolidGap>(),0,0,double.NaN,false,why);
    /// <summary>The saved mesh (<c>PlateStructureMesh.ToJson</c>): "vertices" and the corner indices "v" of every face.</summary>
    internal static SolidShapeResult FromJson(string json)
    {
        using var doc=System.Text.Json.JsonDocument.Parse(json);var root=doc.RootElement;
        var flat=root.GetProperty("vertices").EnumerateArray().Select(x=>x.GetSingle()).ToArray();
        var points=new NVector[flat.Length/3];for(int i=0;i<points.Length;i++)points[i]=new(flat[3*i],flat[3*i+1],flat[3*i+2]);
        var faces=root.GetProperty("faces").EnumerateArray().Select(f=>f.GetProperty("v").EnumerateArray().Select(x=>x.GetInt32()).ToArray()).ToArray();
        return Analyse(points,faces);
    }
    /// <summary>The gated snapshot of the designer (the composer note); a face with a hole is refused (a hole is an opening of the shape).</summary>
    internal static SolidShapeResult FromSnapshot(PlateMeshSnapshot snapshot)
    {
        for(int f=0;f<snapshot.Faces.Length;f++)if(snapshot.Faces[f].HoleLoops>0)return Refused(snapshot.Faces.Length,NotClosed+$" Face {f+1} has a hole.");
        return Analyse(snapshot.Vertices,snapshot.Faces.Select(f=>f.VertexIds).ToArray());
    }
    internal static SolidShapeResult Analyse(IReadOnlyList<NVector> vertices,IReadOnlyList<int[]> faces)
    {
        int n=faces.Count;
        if(n==0)return Refused(0,"This plate has no faces.");
        for(int f=0;f<n;f++)
        {
            var v=faces[f];
            if(v.Length<3||v.Distinct().Count()!=v.Length||v.Any(i=>i<0||i>=vertices.Count)||v.Any(i=>!float.IsFinite(vertices[i].X)||!float.IsFinite(vertices[i].Y)||!float.IsFinite(vertices[i].Z)))
                return Refused(n,NotClosed+$" Face {f+1} is not a proper polygon.");
        }
        var users=new Dictionary<(int,int),List<(int Face,bool Forward)>>();
        for(int f=0;f<n;f++)for(int j=0;j<faces[f].Length;j++)
        {
            int a=faces[f][j],b=faces[f][(j+1)%faces[f].Length];var key=(Math.Min(a,b),Math.Max(a,b));
            if(!users.TryGetValue(key,out var list))users[key]=list=new();list.Add((f,a<b));
        }
        // pieces: faces joined by an edge
        var parent=Enumerable.Range(0,n).ToArray();int Find(int i){while(parent[i]!=i)i=parent[i]=parent[parent[i]];return i;}
        foreach(var list in users.Values)for(int k=1;k<list.Count;k++)parent[Find(list[k].Face)]=Find(list[0].Face);
        var pieceOf=new int[n];var ids=new Dictionary<int,int>();for(int f=0;f<n;f++){var r=Find(f);if(!ids.TryGetValue(r,out var id))ids[r]=id=ids.Count;pieceOf[f]=id;}
        int pieces=ids.Count;var pieceFaces=new int[pieces];for(int f=0;f<n;f++)pieceFaces[pieceOf[f]]++;
        var open=users.Where(u=>u.Value.Count==1).ToList();var shared=users.Where(u=>u.Value.Count>2).ToList();
        if(open.Count>0)
        {
            // one gap per connected run of open edges (joined at their points)
            var gp=new Dictionary<int,int>();int G(int v){if(!gp.ContainsKey(v))gp[v]=v;while(gp[v]!=v){gp[v]=gp[gp[v]];v=gp[v];}return v;}
            foreach(var e in open)gp[G(e.Key.Item1)]=G(e.Key.Item2);
            var gaps=open.GroupBy(e=>G(e.Key.Item1)).Select(g=>
            {
                var pts=g.SelectMany(e=>new[]{e.Key.Item1,e.Key.Item2}).Distinct().ToArray();var c=NVector.Zero;foreach(var p in pts)c+=vertices[p];
                return new SolidGap(g.Count(),g.Select(e=>e.Value[0].Face+1).Distinct().OrderBy(x=>x).ToArray(),c/pts.Length,pieceOf[g.First().Value[0].Face]);
            }).OrderBy(g=>g.Piece).ThenBy(g=>g.Faces[0]).ToArray();
            // the size of the opening: the nearest other point of the mesh to a point of an open edge
            var openPoints=open.SelectMany(e=>new[]{e.Key.Item1,e.Key.Item2}).Distinct().ToArray();double closest=double.PositiveInfinity;
            foreach(var p in openPoints)for(int q=0;q<vertices.Count;q++)if(q!=p){var d=(double)NVector.Distance(vertices[p],vertices[q]);if(d<closest)closest=d;}
            bool tolerance=closest<ToleranceM&&WeldedOpen(vertices,faces)==0;
            var where=string.Join("; ",gaps.Take(4).Select(g=>$"a gap of {g.Edges} edge{(g.Edges==1?"":"s")} at face{(g.Faces.Length==1?"":"s")} {List(g.Faces)} (around x {I(g.CentreM.X*1000,"0")}, y {I(g.CentreM.Y*1000,"0")}, z {I(g.CentreM.Z*1000,"0")} mm)"))+(gaps.Length>4?$"; and {gaps.Length-4} more gaps":"");
            var why=NotClosed+$" {open.Count} open edge{(open.Count==1?"":"s")}: {where}."+(tolerance?$" These points lie {I(closest*1000,"0.###")} mm apart (less than 0.1 mm): merge them in the designer.":"");
            return new(n,Array.Empty<double>(),pieceFaces,0,gaps,open.Count,shared.Count,closest*1000,tolerance,why);
        }
        if(shared.Count>0)
            return new(n,Array.Empty<double>(),pieceFaces,0,Array.Empty<SolidGap>(),0,shared.Count,double.NaN,false,
                NotClosed+$" {shared.Count} edge{(shared.Count==1?"":"s")} belong{(shared.Count==1?"s":"")} to more than two faces (a wall inside the shape) at faces {List(shared.SelectMany(s=>s.Value.Select(x=>x.Face+1)))}.");
        // turn the faces of each piece to one side along their shared edges
        var flip=new bool?[n];var queue=new Queue<int>();
        for(int start=0;start<n;start++)
        {
            if(flip[start]!=null)continue;flip[start]=false;queue.Enqueue(start);
            while(queue.Count>0)
            {
                int f=queue.Dequeue();
                for(int j=0;j<faces[f].Length;j++)
                {
                    int a=faces[f][j],b=faces[f][(j+1)%faces[f].Length];var list=users[(Math.Min(a,b),Math.Max(a,b))];
                    var mine=list[0].Face==f?list[0]:list[1];var other=list[0].Face==f?list[1]:list[0];
                    bool want=(mine.Forward^flip[f]!.Value)==other.Forward;
                    if(flip[other.Face]==null){flip[other.Face]=want;queue.Enqueue(other.Face);}
                    else if(flip[other.Face]!=want)return new(n,Array.Empty<double>(),pieceFaces,0,Array.Empty<SolidGap>(),0,0,double.NaN,false,NotClosed+$" Its faces cannot all face outward (around faces {f+1} and {other.Face+1}).");
                }
            }
        }
        var volumes=new double[pieces];
        for(int f=0;f<n;f++)volumes[pieceOf[f]]+=Signed(vertices,faces[f],flip[f]==true);
        int reversed=0;
        for(int f=0;f<n;f++){if(volumes[pieceOf[f]]<0)flip[f]=!flip[f];if(flip[f]==true)reversed++;}
        for(int p=0;p<pieces;p++)
        {
            volumes[p]=Math.Abs(volumes[p]);
            if(!(volumes[p]>1e-9))return new(n,Array.Empty<double>(),pieceFaces,0,Array.Empty<SolidGap>(),0,0,double.NaN,false,NotClosed+$" Piece {p+1} encloses no volume.");
        }
        return new(n,volumes,pieceFaces,reversed,Array.Empty<SolidGap>(),0,0,double.NaN,false,null);
    }
    /// <summary>
    /// A skin thicker than this is reported (not changed): the back of such a face can reach past the other walls of the shape, and a shot line that crosses it
    /// then shows surfaces that cannot be paired as skins (an odd count, or a skin longer than the shape), so that line pays only the skins.
    /// </summary>
    internal const int SkinWarningMm=50;
    /// <summary>The faces with a corner thicker than <see cref="SkinWarningMm"/>, and the thickest corner (mm).</summary>
    internal static (int Faces,int MaxMm) ThickFaces(IEnumerable<IReadOnlyList<ushort>> thickness)
    {
        int faces=0,max=0;foreach(var t in thickness){int m=t.Count==0?0:t.Max(x=>(int)x);if(m>SkinWarningMm)faces++;max=Math.Max(max,m);}
        return (faces,max);
    }
    internal static (int Faces,int MaxMm) ThickFaces(string json)
    {
        using var doc=System.Text.Json.JsonDocument.Parse(json);
        return ThickFaces(doc.RootElement.GetProperty("faces").EnumerateArray().Select(f=>(IReadOnlyList<ushort>)(f.TryGetProperty("t",out var t)?t.EnumerateArray().Select(x=>x.GetUInt16()).ToArray():Array.Empty<ushort>())).ToArray());
    }
    /// <summary>The signed volume of one face's fan triangles with the origin (divergence theorem), in double precision.</summary>
    private static double Signed(IReadOnlyList<NVector> v,int[] face,bool reversed)
    {
        double s=0;int k=face.Length;
        int At(int j)=>reversed?face[(k-j)%k]:face[j];
        var p0=v[At(0)];
        for(int j=1;j+1<k;j++)
        {
            var a=v[At(j)];var b=v[At(j+1)];
            s+=((double)p0.X*((double)a.Y*b.Z-(double)a.Z*b.Y)-(double)p0.Y*((double)a.X*b.Z-(double)a.Z*b.X)+(double)p0.Z*((double)a.X*b.Y-(double)a.Y*b.X))/6;
        }
        return s;
    }
    /// <summary>The open edges left when points closer than <see cref="ToleranceM"/> are one point (only to tell a tolerance gap; the mesh is not changed).</summary>
    private static int WeldedOpen(IReadOnlyList<NVector> vertices,IReadOnlyList<int[]> faces)
    {
        var map=Enumerable.Range(0,vertices.Count).ToArray();
        for(int i=0;i<vertices.Count;i++)for(int j=0;j<i;j++)if(map[j]==j&&NVector.Distance(vertices[i],vertices[j])<ToleranceM){map[i]=j;break;}
        var count=new Dictionary<(int,int),int>();
        foreach(var f in faces)for(int j=0;j<f.Length;j++)
        {
            int a=map[f[j]],b=map[f[(j+1)%f.Length]];if(a==b)continue;var key=(Math.Min(a,b),Math.Max(a,b));count[key]=count.GetValueOrDefault(key)+1;
        }
        return count.Values.Count(c=>c==1);
    }
}

/// <summary>0.22.82: the texts of the choice "Solid block" and the weight it gives (V x the density of the recipe over its nominal depth, air included: CassetteModel.Passive.Density).</summary>
internal static class SolidBlockApply
{
    internal const string Label="Solid block";
    internal const string Help="The whole closed shape counts as the recipe: the path of the shot through it × the factor. For cast or curved shapes.";
    internal const string ThinShell="The game still sees a thin shell: fragments cross the inside freely.";
    internal const string SolidCollider="Native solid damage collider: shells, fragments and armour probes see the full volume.";
    internal static bool NativeCollider=true;
    internal static string ColliderSummary=>NativeCollider?SolidCollider:ThinShell;
    internal const string InLayers="This plate is in layers: a plate in layers cannot be a solid block at the same time. Choose Back to box first, then Solid block.";
    internal const string Leaves="This plate is a Solid block: this Apply removes the mark [Solid block] (the plate is paid as its skins again).";
    /// <summary>The armour mass of a solid block: closed volume x the density of the recipe over its nominal depth (air included: CassetteModel.Passive.Density).</summary>
    internal static double MassKg(double volumeM3,double densityKgM3)=>volumeM3*densityKgM3;
    /// <summary>"2 closed pieces, 369.3 l, new weight 2905 kg (7866 kg/m³ incl. air)." without a density: pieces and volume only.</summary>
    internal static string Shape(int pieces,double volumeM3,double? densityKgM3)
    {
        var c=System.Globalization.CultureInfo.InvariantCulture;
        var weight=densityKgM3 is {} d&&double.IsFinite(d)&&d>0?string.Create(c,$", new weight {MassKg(volumeM3,d):0} kg ({d:0} kg/m³ incl. air)"):"";
        return string.Create(c,$"{pieces} closed piece{(pieces==1?"":"s")}, {volumeM3*1000:0.#} l{weight}.");
    }
    /// <summary>The line of the composer note: the shape, then the collider note; a warning when faces are thicker than a skin.</summary>
    internal static string Note(int pieces,double volumeM3,double? densityKgM3,int thickFaces=0,int maxMm=0)=>"Solid block: "+Shape(pieces,volumeM3,densityKgM3)+" "+ColliderSummary+(thickFaces>0?" "+Thick(thickFaces,maxMm):"");
    /// <summary>The warning for faces thicker than a skin (the shape is applied as drawn; nothing is changed).</summary>
    internal static string Thick(int faces,int maxMm)=>string.Create(System.Globalization.CultureInfo.InvariantCulture,
        $"Warning: {faces} face{(faces==1?" is":"s are")} thicker than {SolidShape.SkinWarningMm} mm (up to {maxMm} mm): the back of such a face can reach past the other walls, and a shot through it pays only the skins. Make them a thin skin first (Thickness only, for example 5 mm), then Solid block.");
}
