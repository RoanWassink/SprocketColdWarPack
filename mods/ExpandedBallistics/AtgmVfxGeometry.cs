using System.Numerics;
namespace SprocketExpandedBallistics;

internal readonly record struct AtgmSmokeSpan(Vector3 Centre,Vector3 Axis,float Length,float Width);
internal readonly record struct AtgmSmokeView(Vector3 Facing,Vector3 Up,float Length,float Width);

// Geometry only: observes movement, never changes a projectile or its dynamics.
internal static class AtgmVfxGeometry
{
    private static bool Finite(Vector3 v)=>float.IsFinite(v.X)&&float.IsFinite(v.Y)&&float.IsFinite(v.Z);
    internal static bool TrySpan(Vector3 from,Vector3 to,float calibreM,out AtgmSmokeSpan span)
    {
        span=default;
        if(!Finite(from)||!Finite(to)||!float.IsFinite(calibreM)||calibreM<=0)return false;
        var delta=to-from;var length=delta.Length();
        if(!float.IsFinite(length)||length>50)return false; // Never connect a teleport/reused position.
        var axis=length>.001f?delta/length:Vector3.UnitY;
        span=new(from+delta*.5f,axis,length,Math.Clamp(calibreM*5f,.45f,1.4f));
        return Finite(span.Centre);
    }
    internal static bool TryView(AtgmSmokeSpan span,Vector3 position,Vector3 camera,float ageFraction,out AtgmSmokeView view)
    {
        view=default;
        if(!Finite(position)||!Finite(camera)||!Finite(span.Axis)||!float.IsFinite(span.Length)||span.Length<0||span.Length>50||
           !float.IsFinite(span.Width)||span.Width<.45f||span.Width>1.4f||!float.IsFinite(ageFraction))return false;
        if(Math.Abs(span.Axis.LengthSquared()-1)> .001f)return false;
        var away=position-camera;var distance=away.Length();if(!float.IsFinite(distance)||distance<.001f)return false;
        var facing=away/distance;
        var along=span.Axis-facing*Vector3.Dot(span.Axis,facing);var projection=along.Length();
        if(!float.IsFinite(projection))return false;
        Vector3 up;
        if(projection>.001f)up=along/projection;
        else
        {
            var reference=Math.Abs(facing.Y)<.95f?Vector3.UnitY:Vector3.UnitX;
            up=Vector3.Normalize(reference-facing*Vector3.Dot(reference,facing));
        }
        var width=span.Width*(1+Math.Clamp(ageFraction,0,1)*2.6f);
        // Overlap compensates for the transparent edge of the actual smoke sprite.
        var length=Math.Max(width,span.Length*projection*2.1f+width*.3f);
        view=new(facing,up,length,width);
        return Finite(up)&&float.IsFinite(length);
    }
    private static float Diameter(float value)=>float.IsFinite(value)&&value>0?Math.Clamp(value,.03f,.45f):.12f;
    private static float Fraction(float value)=>float.IsFinite(value)?Math.Clamp(value,0,1):0;
    internal static float ImpactFlash(float diameter,float fraction)=>
        Math.Clamp(Diameter(diameter)*14f,.8f,4.5f)*(.7f+Fraction(fraction)*.8f);
    internal static float ImpactDust(float diameter,float fraction)=>
        Math.Min(8,Math.Clamp(Diameter(diameter)*16f,1.1f,5f)*(1+Fraction(fraction)*1.8f));
}
