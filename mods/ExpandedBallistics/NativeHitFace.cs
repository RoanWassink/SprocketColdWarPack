using Sprocket.DamageModelling;
using Sprocket.Vehicles.PlateStructures;
using Sprocket.PlateMesh;
using UnityEngine;
namespace SprocketExpandedBallistics;

internal static class NativeHitFace
{
    // Report-only fallback. It does not grant material/ERA eligibility.
    internal static double? ReportThickness(PenetrationSimulation sim,StructureIntersection hit,PlateStructure plate)
    {
        if(TryResolve(sim,hit,plate,out var face,out _,allowPolygons:true)||TryResolveUniformBacking(sim,hit,plate,out face))
            return CombatReportMeasurement.NormalThickness(face.NormalThicknessMm,0,0,0);
        if(hit.structureIndex>=sim.meshes.Length)return null;
        var mesh=sim.meshes[hit.structureIndex];var offset=hit.triangleIndex;
        if(mesh.triangles==null||mesh.thickness==null||offset<0||offset%3!=0||offset>mesh.triangles.Length-3)return null;
        var a=mesh.triangles[offset];var b=mesh.triangles[offset+1];var c=mesh.triangles[offset+2];
        if(a<0||b<0||c<0||a>=mesh.thickness.Length||b>=mesh.thickness.Length||c>=mesh.thickness.Length)return null;
        return CombatReportMeasurement.NormalThickness(null,mesh.thickness[a],mesh.thickness[b],mesh.thickness[c]);
    }
    internal static bool TryResolve(PenetrationSimulation sim,StructureIntersection hit,PlateStructure plate,out DeclaredFace face)
        =>TryResolve(sim,hit,plate,out face,out _);
    internal static bool TryResolve(PenetrationSimulation sim,StructureIntersection hit,PlateStructure plate,out DeclaredFace face,out string reason,bool allowPolygons=false)
    {
        face=default;reason="hit-face mapping unavailable";
        if(hit.structureIndex>=sim.meshes.Length||plate.meshReference?.mesh?.editMesh?.faces is not {} faces)return false;
        var mesh=sim.meshes[hit.structureIndex];
        if(mesh.triangles==null||mesh.thickness==null){reason="native face thickness unavailable";return false;}
        var widths=new int[faces.Count];
        for(int i=0;i<widths.Length;i++)
        {
            var f=faces[i];
            if(f==null)return false;
            widths[i]=f.HasFlag(ElementFlags.CullTessellation)?0:f.vertexCount;
        }
        var offset=hit.triangleIndex;
        if(offset<0||offset%3!=0||offset>mesh.triangles.Length-3)return false;
        var a=mesh.triangles[offset];var b=mesh.triangles[offset+1];var c=mesh.triangles[offset+2];
        if(a<0||b<0||c<0||a>=mesh.thickness.Length||b>=mesh.thickness.Length||c>=mesh.thickness.Length)return false;
        if(mesh.meshMarkers!=null&&mesh.meshMarkerTypes!=null&&DeclaredFacePolicy.ResolveSamples(offset,mesh.triangles.Length,mesh.thickness.Length,a,b,c,
            mesh.thickness[a],mesh.thickness[b],mesh.thickness[c],mesh.meshMarkers.ToArray(),mesh.meshMarkerTypes.ToArray(),widths,out face,allowNativeSentinel:true,allowPolygons:allowPolygons,allowTapered:true))
        {
            // Preserve 0.22.16: exact equality with the truncated configured corner average. 0.22.24: a tapered face (the three samples differ)
            // keeps the face identity of the marker range and takes the configured face average, if every sample lies within the corner range.
            if(!ConfiguredThickness(faces[face.FaceIndex],out var configured,allowPolygons,out var cornerMin,out var cornerMax)){reason="configured face thickness unavailable";return false;}
            if(face.Tapered)
            {
                var low=Math.Min(mesh.thickness[a],Math.Min(mesh.thickness[b],mesh.thickness[c]));var high=Math.Max(mesh.thickness[a],Math.Max(mesh.thickness[b],mesh.thickness[c]));
                if(low<cornerMin-1||high>cornerMax+1){reason=$"tapered samples {mesh.thickness[a]}/{mesh.thickness[b]}/{mesh.thickness[c]} outside the corner range {cornerMin:0.#}-{cornerMax:0.#}";return false;}
            }
            else if(!DeclaredFacePolicy.MatchesConfiguredThickness(configured,face.NormalThicknessMm)){reason=$"{configured:0.##}mm vs mesh {mesh.thickness[a]}/{mesh.thickness[b]}/{mesh.thickness[c]}";return false;}
            face=face with{NormalThicknessMm=configured};reason="";return true;
        }
        if(faces.Count!=1){reason=$"face markers unresolved; {faces.Count} faces; samples {mesh.thickness[a]}/{mesh.thickness[b]}/{mesh.thickness[c]}";return false;}
        var sourceFace=faces[0];
        if(!ConfiguredThickness(sourceFace,out var configuredThickness,allowPolygons)){reason="configured face thickness unavailable";return false;}
        var configuredNormal=plate.transform.localToWorldMatrix.inverse.transpose.MultiplyVector(sourceFace.normal).normalized;
        if(sim.spaceFrames==null||hit.structureIndex>=sim.spaceFrames.Length)return false;
        var simNormal=sim.spaceFrames[hit.structureIndex].localToSimSpace.MultiplyVector(mesh.GetHitNormal(offset,false));
        var hitNormal=sim.SimToWorldSpaceDirection(simNormal).normalized;
        if(configuredNormal.sqrMagnitude<.5f||hitNormal.sqrMagnitude<.5f)return false;
        var alignment=Vector3.Dot(configuredNormal,hitNormal);
        if(Math.Abs(alignment)<.9999){reason="hit does not match the plate front or back face";return false;}
        var resolved=DeclaredFacePolicy.ResolveSinglePlanarFace(faces.Count,sourceFace.vertexCount,
            sourceFace.HasFlag(ElementFlags.CullTessellation),configuredThickness,
            alignment,mesh.thickness[a],mesh.thickness[b],mesh.thickness[c],out face,allowPolygons);
        if(!resolved)reason=$"{configuredThickness:0.##}mm vs mesh {mesh.thickness[a]}/{mesh.thickness[b]}/{mesh.thickness[c]}";
        return resolved;
    }
    // Drawn face thickness without a hit on that plate. Used when a cassette is
    // a co-located member of another object's native block (shot #8).
    internal static bool TryAuthoredFaceThickness(PlateStructure plate,int faceIndex,out double thickness)
    {
        thickness=0;
        var faces=plate.meshReference?.mesh?.editMesh?.faces;
        if(faces==null||faceIndex<0||faceIndex>=faces.Count)return false;
        return ConfiguredThickness(faces[faceIndex],out thickness,allowPolygons:true);
    }
    internal static bool TryResolveUniformBacking(PenetrationSimulation sim,StructureIntersection hit,PlateStructure plate,out DeclaredFace face)
    {
        face=default;
        if(hit.structureIndex>=sim.meshes.Length||sim.spaceFrames==null||hit.structureIndex>=sim.spaceFrames.Length||
            plate.meshReference?.mesh?.editMesh?.faces is not {} faces)return false;
        var mesh=sim.meshes[hit.structureIndex];var offset=hit.triangleIndex;
        if(mesh.triangles==null||mesh.thickness==null||offset<0||offset%3!=0||offset>mesh.triangles.Length-3)return false;
        var a=mesh.triangles[offset];var b=mesh.triangles[offset+1];var c=mesh.triangles[offset+2];
        if(a<0||b<0||c<0||a>=mesh.thickness.Length||b>=mesh.thickness.Length||c>=mesh.thickness.Length)return false;
        var simNormal=sim.spaceFrames[hit.structureIndex].localToSimSpace.MultiplyVector(mesh.GetHitNormal(offset,false));
        var normal=sim.SimToWorldSpaceDirection(simNormal).normalized;
        if(!float.IsFinite(normal.sqrMagnitude)||normal.sqrMagnitude<.5f)return false;
        var thicknesses=new List<double>();var alignments=new List<double>();
        var normalMatrix=plate.transform.localToWorldMatrix.inverse.transpose;
        for(int i=0;i<faces.Count;i++)
        {
            var source=faces[i];if(source==null)return false;
            if(source.HasFlag(ElementFlags.CullTessellation))continue;
            if(!ConfiguredThickness(source,out var thickness))return false;
            var sourceNormal=normalMatrix.MultiplyVector(source.normal).normalized;
            if(!float.IsFinite(sourceNormal.sqrMagnitude)||sourceNormal.sqrMagnitude<.5f)return false;
            thicknesses.Add(thickness);alignments.Add(Vector3.Dot(sourceNormal,normal));
        }
        return DeclaredFacePolicy.ResolveUniformBacking(thicknesses,alignments,mesh.thickness[a],mesh.thickness[b],mesh.thickness[c],out face);
    }
    private static bool ConfiguredThickness(Face source,out double thickness,bool allowPolygons=false)=>ConfiguredThickness(source,out thickness,allowPolygons,out _,out _);
    private static bool ConfiguredThickness(Face source,out double thickness,bool allowPolygons,out double cornerMin,out double cornerMax)
    {
        thickness=0;cornerMin=0;cornerMax=0;
        if(source==null||source.vertexCount<3||!allowPolygons&&source.vertexCount>4)return false;
        var first=source.firstLoop;var loop=first;
        if(first==null)return false;
        double sum=0;cornerMin=double.MaxValue;cornerMax=0;
        for(int i=0;i<source.vertexCount;i++)
        {
            if(loop==null||i>0&&loop.Pointer==first.Pointer)return false;
            sum+=loop.thickness;cornerMin=Math.Min(cornerMin,loop.thickness);cornerMax=Math.Max(cornerMax,loop.thickness);loop=loop.next;
        }
        if(loop==null||loop.Pointer!=first.Pointer)return false;
        // 0.22.16.1 fork: ToBackPlane varies per corner. Native tessellation collapses a face to one
        // averaged declared value (DeclaredFacePolicy.cs:42), so the cassette depth uses that same face
        // average rather than interpolating a value native itself never evaluates.
        thickness=sum/source.vertexCount;
        return double.IsFinite(thickness)&&thickness>0&&thickness<=short.MaxValue;
    }

}
