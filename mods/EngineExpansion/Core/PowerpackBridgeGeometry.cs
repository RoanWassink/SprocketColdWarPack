namespace SprocketEngineExpansion.Core;

public sealed record PowerpackBridgeMesh(Vec3[] Vertices,int[] Triangles);
public static class PowerpackBridgeGeometry
{
    public static PowerpackBridgeMesh Build(double width,double height,double length,double capX,double capZ)
    {
        if(!double.IsFinite(width+height+length+capX+capZ)||width<=0||height<=0||length<=0)throw new ArgumentOutOfRangeException(nameof(width));
        var vertices=new List<Vec3>();var indices=new List<int>();
        void Triangle(Vec3 a,Vec3 b,Vec3 c)
        {int i=vertices.Count;vertices.Add(a);vertices.Add(b);vertices.Add(c);indices.Add(i);indices.Add(i+1);indices.Add(i+2);}
        void Quad(Vec3 a,Vec3 b,Vec3 c,Vec3 d){Triangle(a,b,c);Triangle(a,c,d);}
        double w=width*.5;
        var profile=new[]{new Vec3(-w,0,0),new Vec3(w,0,0),new Vec3(w,height*.74,0),new Vec3(w*.72,height,0),new Vec3(-w*.72,height,0),new Vec3(-w,height*.74,0)};
        for(int i=0;i<profile.Length;i++)
        {
            var a=profile[i];var b=profile[(i+1)%profile.Length];var dz=new Vec3(0,0,length);
            Quad(a,b,b+dz,a+dz);Triangle(new Vec3(0,height*.5,0),b,a);Triangle(new Vec3(0,height*.5,length),a+dz,b+dz);
        }
        double radius=Math.Min(width*.24,length*.20);capZ=Math.Clamp(capZ,radius,length-radius);capX=Math.Clamp(capX,-w+radius,w-radius);
        for(int i=0;i<12;i++)
        {
            double t=i*Math.PI/6,u=(i+1)*Math.PI/6;
            Vec3 P(double angle,double r,double y)=>new(capX+Math.Cos(angle)*r,y,capZ+Math.Sin(angle)*r);
            var a=P(t,radius,height*.65);var b=P(u,radius,height*.65);var c=P(u,radius*.72,height*1.08);var d=P(t,radius*.72,height*1.08);
            Quad(a,d,c,b);Triangle(new Vec3(capX,height*1.08,capZ),c,d);Triangle(new Vec3(capX,height*.65,capZ),a,b);
        }
        return new(vertices.ToArray(),indices.ToArray());
    }
}
