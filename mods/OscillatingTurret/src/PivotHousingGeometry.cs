using System.Numerics;
namespace SprocketOscillatingTurret;

// Original geometry in metres, +Y up, hinge along X. Shared with the preview
// exporter; no saved tank, native mesh, motor or physics data is modified.
internal sealed class PivotMesh
{
    internal readonly List<Vector3> Vertices=new();
    internal readonly List<int> Triangles=new();
    internal void Face(params Vector3[] v)
    {
        int start=Vertices.Count;Vertices.AddRange(v);
        for(int i=1;i<v.Length-1;i++)Triangles.AddRange(new[]{start,start+i,start+i+1});
    }
    internal void Block(Vector3 centre,Vector3 size,float bevel)
    {
        float x=size.X/2,z=size.Z/2,h=size.Y/2,b=Math.Min(bevel,Math.Min(x,z)*.6f);
        var p=new[]{new Vector2(-x+b,-z),new Vector2(x-b,-z),new Vector2(x,-z+b),new Vector2(x,z-b),new Vector2(x-b,z),new Vector2(-x+b,z),new Vector2(-x,z-b),new Vector2(-x,-z+b)};
        var bottom=p.Select(v=>centre+new Vector3(v.X,-h,v.Y)).ToArray();
        var top=p.Select(v=>centre+new Vector3(v.X,h,v.Y)).ToArray();
        Face(bottom);Face(top.Reverse().ToArray());
        for(int i=0;i<8;i++){int n=(i+1)%8;Face(bottom[i],top[i],top[n],bottom[n]);}
    }
    internal void CylinderX(Vector3 centre,float radius,float length,int sides=40)
    {
        var left=new Vector3[sides];var right=new Vector3[sides];
        for(int i=0;i<sides;i++)
        {float a=i*MathF.Tau/sides;var offset=new Vector3(0,MathF.Cos(a)*radius,MathF.Sin(a)*radius);left[i]=centre+offset-new Vector3(length/2,0,0);right[i]=centre+offset+new Vector3(length/2,0,0);}
        Face(left.Reverse().ToArray());Face(right);
        for(int i=0;i<sides;i++){int n=(i+1)%sides;Face(left[i],left[n],right[n],right[i]);}
    }
}
internal sealed class PivotHousingGeometry
{
    internal readonly PivotMesh Body=new(),Inserts=new(),Upper=new();
    internal readonly float Diameter;
    internal PivotHousingGeometry(float diameter)
    {
        Diameter=Math.Clamp(diameter,.12f,3f);
        float d=Diameter,w=d*1.65f,l=d*1.85f;
        Body.Block(new(0,.025f*d,0),new(w,.13f*d,l),.10f*d);
        // Broad cast side cheeks conceal the lateral bearing and actuator.
        foreach(float s in new[]{-1f,1f})
        {
            Body.Block(new(s*w*.405f,.235f*d,0),new(.23f*w,.42f*d,.73f*l),.09f*d);
            Inserts.CylinderX(new(s*w*.523f,.285f*d,.035f*l),.155f*d,.035f*d);
            Body.CylinderX(new(s*w*.545f,.285f*d,.035f*l),.105f*d,.045f*d);
            // Four recessed mounting pads with restrained metallic bolt heads.
            foreach(float f in new[]{-1f,1f})
            {
                Body.Block(new(s*w*.37f,.107f*d,f*l*.405f),new(.18f*d,.08f*d,.20f*d),.025f*d);
                Inserts.Block(new(s*w*.37f,.156f*d,f*l*.405f),new(.07f*d,.018f*d,.07f*d),.015f*d);
            }
        }
        // Enclosed longitudinal actuator can, boot and short exposed ram.
        Body.Block(new(0,.205f*d,-.29f*l),new(.63f*w,.29f*d,.34f*l),.07f*d);
        Inserts.Block(new(0,.235f*d,-.105f*l),new(.22f*w,.14f*d,.12f*l),.025f*d);
        Inserts.Block(new(0,.255f*d,-.005f*l),new(.115f*w,.085f*d,.16f*l),.02f*d);
        Body.Block(new(0,.21f*d,.365f*l),new(.57f*w,.29f*d,.12f*l),.04f*d);
        Inserts.Block(new(0,.245f*d,.431f*l),new(.43f*w,.065f*d,.014f*d),.01f*d);
        // Native controlled structure carries this enclosed upper saddle.
        Upper.Block(new(0,.37f*d,.035f*l),new(.77f*w,.14f*d,.65f*l),.075f*d);
        Upper.Block(new(0,.465f*d,.035f*l),new(.59f*w,.05f*d,.49f*l),.025f*d);
    }
}
