// CPU-only storage for testing the actual generators' render attributes.
namespace UnityEngine;
public struct Vector3
{
    public float x, y, z;
    public Vector3(float x, float y, float z) { this.x = x; this.y = y; this.z = z; }
    public static Vector3 up => new(0,1,0);
    public static Vector3 down => new(0,-1,0);
    public static Vector3 forward => new(0,0,1);
    public static Vector3 back => new(0,0,-1);
    public static Vector3 right => new(1,0,0);
    public static Vector3 left => new(-1,0,0);
    public static Vector3 operator -(Vector3 v) => new(-v.x,-v.y,-v.z);
}
public enum HideFlags { HideAndDontSave }
public sealed class Mesh
{
    public string name = "";
    public HideFlags hideFlags;
    public Vector3[] vertices = Array.Empty<Vector3>(), normals = Array.Empty<Vector3>();
    public int[] triangles = Array.Empty<int>();
    public Vector2[] uv = Array.Empty<Vector2>();
    public void RecalculateBounds() { }
    public void RecalculateTangents() { }
}
