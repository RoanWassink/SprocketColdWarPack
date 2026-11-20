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
            mesh.uv=data.Uvs!=null?ToVec2(data.Uvs):g.UV.Select(v=>new Vector2(v.X,v.Y)).ToArray();
            mesh.triangles=g.Indices;
            mesh.RecalculateTangents();
            mesh.RecalculateBounds();
            return mesh;
        } catch { UnityEngine.Object.Destroy(mesh); throw; }
    }

    internal static Mesh CreateWithGlass(CupolaData armor, CupolaData glass)
    {
        int aCount = armor.Positions.Length / 3;
        int gCount = glass.Positions.Length / 3;
        int total = aCount + gCount;
        var verts = new Vector3[total];
        var norms = new Vector3[total];
        var uvs = new Vector2[total];
        for (int i = 0; i < aCount; i++) {
            verts[i] = new Vector3(armor.Positions[3*i], armor.Positions[3*i+1], armor.Positions[3*i+2]);
            norms[i] = new Vector3(armor.Normals[3*i], armor.Normals[3*i+1], armor.Normals[3*i+2]);
            uvs[i] = armor.Uvs != null ? new Vector2(armor.Uvs[2*i], armor.Uvs[2*i+1]) : new Vector2(verts[i].x, verts[i].z);
        }
        for (int i = 0; i < gCount; i++) {
            int j = aCount + i;
            verts[j] = new Vector3(glass.Positions[3*i], glass.Positions[3*i+1], glass.Positions[3*i+2]);
            norms[j] = new Vector3(glass.Normals[3*i], glass.Normals[3*i+1], glass.Normals[3*i+2]);
            uvs[j] = glass.Uvs != null ? new Vector2(glass.Uvs[2*i], glass.Uvs[2*i+1]) : Vector2.zero;
        }
        var glassIndices = new int[glass.Indices.Length];
        for (int i = 0; i < glass.Indices.Length; i++) glassIndices[i] = glass.Indices[i] + aCount;

        var mesh = new Mesh { name = "Cupola " + armor.Name + "+glass", hideFlags = HideFlags.HideAndDontSave };
        try {
            mesh.vertices = verts;
            mesh.normals = norms;
            mesh.uv = uvs;
            mesh.subMeshCount = 2;
            mesh.SetTriangles(armor.Indices, 0);
            mesh.SetTriangles(glassIndices, 1);
            mesh.RecalculateTangents();
            mesh.RecalculateBounds();
            return mesh;
        } catch { UnityEngine.Object.Destroy(mesh); throw; }
    }

    private static Vector2[] ToVec2(float[] flat)
    {
        var arr = new Vector2[flat.Length / 2];
        for (int i = 0; i < arr.Length; i++) arr[i] = new Vector2(flat[2 * i], flat[2 * i + 1]);
        return arr;
    }
}
