using UnityEngine;
namespace SprocketTelescopicMast;

internal static class RotatorMesh
{
    // Native model scaling is per-axis. Precompensate Z in OUR mesh; never change native scales.
    internal static float ZCorrection(float sx, float sz) => Travel.ScaleAxis(sx) / Travel.ScaleAxis(sz);
    internal static Mesh Create(bool plate, float sx, float sz)
    {
        byte[] data = plate ? RotatorMeshData.Plate() : RotatorMeshData.Base();
        byte[] indexData = plate ? RotatorMeshData.PlateIndices() : RotatorMeshData.BaseIndices();
        int count = data.Length / 32;
        var vertices = new Vector3[count]; var normals = new Vector3[count]; var uv = new Vector2[count];
        float correction = ZCorrection(sx, sz);
        for (int i = 0; i < count; i++)
        {
            int p = i * 32;
            vertices[i] = new(BitConverter.ToSingle(data, p), BitConverter.ToSingle(data, p + 4), BitConverter.ToSingle(data, p + 8) * correction);
            float x = BitConverter.ToSingle(data, p + 12), y = BitConverter.ToSingle(data, p + 16), z = BitConverter.ToSingle(data, p + 20) / correction;
            float length = MathF.Sqrt(x*x + y*y + z*z);
            normals[i] = new(x/length, y/length, z/length);
            uv[i] = new(BitConverter.ToSingle(data, p + 24), BitConverter.ToSingle(data, p + 28));
        }
        var indices = new int[indexData.Length / 4];
        for (int i = 0; i < indices.Length; i++) indices[i] = BitConverter.ToInt32(indexData, i * 4);
        var mesh = new Mesh { name = plate ? "Rotator plate" : "Rotator base", hideFlags = HideFlags.HideAndDontSave };
        mesh.vertices = vertices; mesh.normals = normals; mesh.triangles = indices;
        mesh.uv = uv;
        mesh.RecalculateBounds(); mesh.RecalculateTangents(); return mesh;
    }
    internal static float Mass(bool plate, float sx, float sy) => (plate ? RotatorMeshData.PlateVolume : RotatorMeshData.BaseVolume)
        * Travel.ScaleAxis(sx) * Travel.ScaleAxis(sx) * Travel.ScaleAxis(sy) * HingeMath.SteelDensity;
}
