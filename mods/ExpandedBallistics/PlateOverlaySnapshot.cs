using Sprocket.PlateMesh;
using Sprocket.Vehicles.PlateStructures;
using NVector=System.Numerics.Vector3;
namespace SprocketExpandedBallistics;

internal sealed record PlateOverlaySnapshot(OverlaySurface Front,OverlaySurface Back,OverlaySurface Sides,string Signature,System.Numerics.Matrix4x4 Mirror);
internal static class PlateOverlaySnapshotReader
{
    // Read-only native adapter. Never use ToUnityMesh: it writes into its argument.
    // Native frontMesh is the authored entry surface; thickenFaceMesh is its built back,
    // thickenEdgeMesh closes the solid, including the edge tool's real displacement.
    internal static PlateOverlaySnapshot? Read(PlateStructure plate,string reader)
    {
        var authored=PlateMeshAccess.Read(plate,reader,SnapshotUse.Fresh);
        if(authored==null||PlateMeshAccess.Gate.Busy)return null;
        var native=plate.Mesh;
        if(native.Released||plate.meshRefresh||native.RebuildRequired)return null;
        var front=Copy(native.frontMesh);var back=Copy(native.thickenFaceMesh);var sides=Copy(native.thickenEdgeMesh);
        if(front==null||back==null||sides==null||front.Vertices.Length<3||back.Vertices.Length<3)return null;
        if(PlateMeshAccess.Gate.Busy||native.Released||plate.meshRefresh||native.RebuildRequired||plate.Mesh.Pointer!=native.Pointer)return null;
        var mirror=NativePlateMirror.Read(plate);
        return new(front,back,sides,authored.Signature+"/"+CassetteNativeOverlay.Fingerprint(front,back,sides)+"/"+mirror,mirror);
    }
    internal static OverlaySurface? Copy(TessellatedMesh mesh)
    {
        var vertices=mesh.vertices;var normals=mesh.normals;var triangles=mesh.triangles;
        if(vertices==null||normals==null||triangles==null)return null;
        var v=new NVector[vertices.Length];var n=new NVector[normals.Length];var t=new int[triangles.Length];
        for(int i=0;i<v.Length;i++){var p=vertices[i];v[i]=new(p.x,p.y,p.z);}
        for(int i=0;i<n.Length;i++){var p=normals[i];n[i]=new(p.x,p.y,p.z);}
        for(int i=0;i<t.Length;i++)t[i]=triangles[i];
        return new(v,t,n);
    }
}
