using Sprocket.Vehicles.PlateStructures;
using NMatrix=System.Numerics.Matrix4x4;
namespace SprocketExpandedBallistics;
internal static class NativePlateMirror
{
    internal static NMatrix Read(PlateStructure plate)
    {
        if(!plate.Mirrored)return NMatrix.Identity;
        plate.GetMirrorMatrix(out var m);
        // Unity uses column vectors; Numerics uses row vectors.
        return new(m.m00,m.m10,m.m20,m.m30,m.m01,m.m11,m.m21,m.m31,m.m02,m.m12,m.m22,m.m32,m.m03,m.m13,m.m23,m.m33);
    }
}
