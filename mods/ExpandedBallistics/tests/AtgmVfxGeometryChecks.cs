using System.Numerics;
namespace SprocketExpandedBallistics;
internal static class AtgmVfxGeometryChecks
{
    internal static void Run(Action<bool,string> check)
    {
        foreach(var speed in new[]{115f,278f,375f})foreach(var dt in new[]{.01f,.04f,.1f})
        {
            var length=speed*dt;
            check(AtgmVfxGeometry.TrySpan(Vector3.Zero,new(0,0,length),.135f,out var span),"bounded observed ATGM motion has smoke span");
            check(Vector3.Distance(span.Centre-span.Axis*span.Length*.5f,Vector3.Zero)<.001f&&
                  Vector3.Distance(span.Centre+span.Axis*span.Length*.5f,new(0,0,length))<.001f,"smoke span reconstructs both observed endpoints");
            check(AtgmVfxGeometry.TryView(span,span.Centre,new(100,0,length*.5f),0,out var side)&&side.Length>=length,
                "side-view segment covers the distance instead of isolated sub-metre dots");
            check(AtgmVfxGeometry.TryView(span,span.Centre,new(0,0,-100),0,out var rear)&&Math.Abs(Vector3.Dot(rear.Up,rear.Facing))<.0001f&&
                  rear.Length<=rear.Width*1.01f,"rear-view trail foreshortens without a stretched camera-facing streak");
        }
        check(!AtgmVfxGeometry.TrySpan(Vector3.Zero,new(0,0,500),.135f,out _),"teleport does not create a long smoke bridge");
        check(!AtgmVfxGeometry.TrySpan(new(float.NaN,0,0),Vector3.Zero,.135f,out _),"invalid positions rejected");
        check(AtgmVfxGeometry.TrySpan(Vector3.Zero,Vector3.Zero,.135f,out var stationary)&&
              AtgmVfxGeometry.TryView(stationary,Vector3.Zero,new(0,-1,0),1,out var vertical)&&
              Math.Abs(Vector3.Dot(vertical.Up,vertical.Facing))<.0001f,"stationary and overhead view remain finite and orthogonal");
        check(AtgmVfxGeometry.ImpactFlash(.135f,0)>.8f&&AtgmVfxGeometry.ImpactDust(.135f,1)>5,
            "135 mm impact has an independently readable world scale");
        check(AtgmVfxGeometry.ImpactDust(.45f,1)<=8&&AtgmVfxGeometry.ImpactFlash(.45f,1)<=6.8,
            "impact visual world scales remain bounded");
    }
}
