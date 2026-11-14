using System.Numerics;
namespace SprocketVisualEffects;

// 0.4.5 (route B, user decision 2026-11-12): the lamps are plain static stock-model parts. VE never
// touches the native part; the glow is drawn by own objects placed on the lens of the stock model.
// Pure math (System.Numerics, no Unity types) so LampChecks can test it, including negative scale.
//
// Lens geometry measured from the installed stock LOD0 meshes (result kept in checks/lens/l9-lens.json):
// circle fit through the glass edge ring, disc plane 1.5 mm in front of the frontmost glass vertex,
// radius inside the bezel. Part space of the stock model: +Z is the lens direction.
internal static class LampLens
{
    internal static readonly Vector3[] Centre={new(0f,.1502f,.0946f),new(-.0019f,.0021f,.0235f),new(-.0019f,.0021f,.0235f)};
    internal static readonly float[] Radius={.0724f,.0355f,.0355f};
    internal const float LightOffset=.01f,HaloOffset=.004f;
    internal readonly record struct Pose(Vector3 Centre,Vector3 Normal,Quaternion Rotation,Vector3 DiscScale,Vector3 LightOrigin,Vector3 HaloPoint,float InPlaneScale);

    // The native model transform chain used by the live-accepted Smoke Launchers mod for its mirrored
    // tube mouths: world = ModelPosition + ModelRotation * (local x GetModelScale). GetModelScale is
    // signed (native flip/mirror negates x), so the mirrored twin's lens lands on the twin's lens.
    internal static Vector3 Point(Vector3 position,Quaternion rotation,Vector3 scale,Vector3 local)=>position+Vector3.Transform(local*scale,rotation);

    internal static bool TryWorld(int form,Vector3 position,Quaternion rotation,Vector3 scale,out Pose pose)
    {
        pose=default;
        if(form<0||form>2||!Finite(position)||!Finite(scale)||!float.IsFinite(rotation.X)||!float.IsFinite(rotation.Y)||!float.IsFinite(rotation.Z)||!float.IsFinite(rotation.W))return false;
        if(MathF.Abs(scale.X)<1e-4f||MathF.Abs(scale.Y)<1e-4f||MathF.Abs(scale.Z)<1e-4f||MathF.Abs(scale.X)>100||MathF.Abs(scale.Y)>100||MathF.Abs(scale.Z)>100)return false;
        float length=rotation.Length();
        if(!(length>.5f&&length<2f))return false;
        rotation=Quaternion.Normalize(rotation);
        var c=Centre[form];float r=Radius[form];
        // A normal transforms with the inverse transpose: R * (n / s). For the lens normal (0,0,1)
        // that is R * (0,0,sign(sz)); the x sign of a mirrored part does not change it.
        float sz=MathF.Sign(scale.Z);
        var normal=Vector3.Normalize(Vector3.Transform(new Vector3(0,0,sz),rotation));
        // Disc: unit disc in its XY plane facing +Z. Rotation = model rotation (turned half round Y
        // when z is mirrored so the disc faces along the lens normal); the disc is symmetric, so the
        // x/y sign of the scale only needs its magnitude.
        var discRotation=sz<0?rotation*new Quaternion(0,1,0,0):rotation;
        var discScale=new Vector3(r*MathF.Abs(scale.X),r*MathF.Abs(scale.Y),1);
        pose=new Pose(Point(position,rotation,scale,c),normal,discRotation,discScale,
            Point(position,rotation,scale,c+new Vector3(0,0,LightOffset)),
            Point(position,rotation,scale,c+new Vector3(0,0,HaloOffset)),
            MathF.Sqrt(MathF.Abs(scale.X*scale.Y)));
        return Finite(pose.Centre)&&Finite(pose.Normal)&&Finite(pose.LightOrigin)&&Finite(pose.HaloPoint);
    }
    private static bool Finite(Vector3 v)=>float.IsFinite(v.X)&&float.IsFinite(v.Y)&&float.IsFinite(v.Z);

    // Unit disc mesh (radius 1, +Z normal, fan around the centre). Front faces are clockwise as seen
    // from +Z (Unity convention); the lens material also renders both sides.
    internal const int DiscSegments=32;
    internal static (Vector3[] Vertices,int[] Triangles) Disc()
    {
        var v=new Vector3[DiscSegments+1];var t=new int[DiscSegments*3];
        for(int i=0;i<DiscSegments;i++){float a=i*MathF.Tau/DiscSegments;v[i+1]=new Vector3(MathF.Cos(a),MathF.Sin(a),0);}
        for(int i=0;i<DiscSegments;i++){t[i*3]=0;t[i*3+1]=1+(i+1)%DiscSegments;t[i*3+2]=1+i;}
        return (v,t);
    }
}
