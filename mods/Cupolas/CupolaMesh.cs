using UnityEngine;
namespace SprocketCupolas;
internal static class CupolaMesh
{
    internal static Mesh Create(CupolaData data)
    {
        var g=MeshGeometry.Create(data);
        var mesh=new Mesh {name="Cupola "+data.Name+" "+data.ObjSha256, hideFlags=HideFlags.HideAndDontSave};
        try {
            mesh.vertices=g.Positions.Select(v=>new Vector3(v.X,v.Y,v.Z)).ToArray();
            mesh.normals=g.Normals.Select(v=>new Vector3(v.X,v.Y,v.Z)).ToArray();
            mesh.uv=g.UV.Select(v=>new Vector2(v.X,v.Y)).ToArray();
            mesh.tangents=g.Tangents.Select(v=>new Vector4(v.X,v.Y,v.Z,v.W)).ToArray();
            mesh.triangles=g.Indices;
            mesh.RecalculateBounds();
            return mesh;
        } catch { UnityEngine.Object.Destroy(mesh); throw; }
    }
}
