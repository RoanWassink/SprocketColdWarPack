using UnityEngine;
namespace SprocketTelescopicMast;

internal static class NativeUv
{
    // Sprocket 0.2.56.2 UVSettings.Default.Scale = 1 (RVA 0x1E68130).
    // PlanarProject uses an orthonormal basis; UnwrapMesh multiplies both axes by Scale.
    internal const float PerMetre = 1f;
    internal static Vector2 Plane(Vector3 p, Vector3 normal)
    {
        float nx = normal.x, ny = normal.y, nz = normal.z;
        float ax = MathF.Abs(nx), ay = MathF.Abs(ny), az = MathF.Abs(nz);
        float ux, uy, uz;
        if (ax <= ay && ax <= az) { ux = 0; uy = -nz; uz = ny; }
        else if (ay <= az) { ux = nz; uy = 0; uz = -nx; }
        else { ux = -ny; uy = nx; uz = 0; }
        float length = MathF.Sqrt(ux*ux + uy*uy + uz*uz);
        ux /= length; uy /= length; uz /= length;
        float vx = ny*uz - nz*uy, vy = nz*ux - nx*uz, vz = nx*uy - ny*ux;
        return new((p.x*ux+p.y*uy+p.z*uz)*PerMetre, (p.x*vx+p.y*vy+p.z*vz)*PerMetre);
    }
}
