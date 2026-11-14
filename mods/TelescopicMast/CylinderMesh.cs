using UnityEngine;
namespace SprocketTelescopicMast;

internal static class CylinderMesh
{
    internal static Mesh Create(bool housing, bool separate = false)
    {
        // All dimensions are metres. Root origin is the bottom of the fixed sleeve.
        const int segments = 96;
        var vertices = new List<Vector3>(); var triangles = new List<int>(); var normals = new List<Vector3>();
        var uv = new List<Vector2>();
        float Seam(float angle) => angle < MathF.PI ? angle + MathF.PI : angle - MathF.PI;
        void Cylinder(float radius, float low, float high)
        {
            const int n = segments;
            for (int i = 0; i < n; i++)
            {
                float a = i * MathF.PI * 2 / n, b = (i + 1) * MathF.PI * 2 / n;
                var p = new Vector3(radius * MathF.Cos(a), low, radius * MathF.Sin(a));
                var q = new Vector3(radius * MathF.Cos(b), low, radius * MathF.Sin(b));
                var r = new Vector3(q.x, high, q.z); var s = new Vector3(p.x, high, p.z);
                int start = vertices.Count;
                vertices.AddRange(new[] { p, s, r, q });
                float ua = Seam(a), ub = ua + (b-a);
                uv.AddRange(new[] { new Vector2(ua*radius, low), new Vector2(ua*radius, high), new Vector2(ub*radius, high), new Vector2(ub*radius, low) });
                var normalA = new Vector3(MathF.Cos(a),0,MathF.Sin(a));
                var normalB = new Vector3(MathF.Cos(b),0,MathF.Sin(b));
                normals.AddRange(new[] { normalA,normalA,normalB,normalB });
                triangles.AddRange(new[] { start, start + 1, start + 2, start, start + 2, start + 3 });
                start = vertices.Count;
                vertices.AddRange(new[] { new Vector3(0, low, 0), p, q, new Vector3(0, high, 0), r, s });
                uv.AddRange(new[] { new Vector2(0,0), new Vector2(p.x,p.z), new Vector2(q.x,q.z), new Vector2(0,0), new Vector2(r.x,r.z), new Vector2(s.x,s.z) });
                normals.AddRange(new[] { Vector3.down,Vector3.down,Vector3.down,Vector3.up,Vector3.up,Vector3.up });
                triangles.AddRange(new[] { start, start + 1, start + 2, start + 3, start + 4, start + 5 });
            }
        }
        void Ring(float outer, float inner, float low, float high)
        {
            void Quad(Vector3 a, Vector3 b, Vector3 c, Vector3 d,Vector3 na,Vector3 nb,Vector3 nc,Vector3 nd)
            {
                int start = vertices.Count; vertices.AddRange(new[] { a, b, c, d });
                normals.AddRange(new[] {na,nb,nc,nd});
                if (MathF.Abs(na.y) > .5f)
                    uv.AddRange(new[] { new Vector2(a.x,a.z), new Vector2(b.x,b.z), new Vector2(c.x,c.z), new Vector2(d.x,d.z) });
                else
                {
                    float radius = MathF.Sqrt(a.x*a.x+a.z*a.z);
                    float first = Seam(MathF.Atan2(a.z,a.x) < 0 ? MathF.Atan2(a.z,a.x)+2*MathF.PI : MathF.Atan2(a.z,a.x));
                    Vector2 Map(Vector3 p)
                    {
                        float angle = MathF.Atan2(p.z,p.x); if (angle < 0) angle += 2*MathF.PI;
                        float u = Seam(angle);
                        if (u-first > MathF.PI) u -= 2*MathF.PI;
                        if (first-u > MathF.PI) u += 2*MathF.PI;
                        return new(u*radius,p.y);
                    }
                    uv.AddRange(new[] { Map(a),Map(b),Map(c),Map(d) });
                }
                triangles.AddRange(new[] { start, start+1, start+2, start, start+2, start+3 });
            }
            for (int i=0;i<segments;i++)
            {
                float a=i*MathF.PI*2/segments, b=(i+1)*MathF.PI*2/segments;
                Vector3 Point(float r,float y,float angle) => new(r*MathF.Cos(angle),y,r*MathF.Sin(angle));
                var p=Point(outer,low,a); var q=Point(outer,low,b); var r=Point(outer,high,b); var s=Point(outer,high,a);
                var u=Point(inner,low,a); var v=Point(inner,low,b); var w=Point(inner,high,b); var x=Point(inner,high,a);
                var na=new Vector3(MathF.Cos(a),0,MathF.Sin(a));
                var nb=new Vector3(MathF.Cos(b),0,MathF.Sin(b));
                Quad(p,s,r,q,na,na,nb,nb); Quad(u,v,w,x,-na,-nb,-nb,-na);
                Quad(s,x,w,r,Vector3.up,Vector3.up,Vector3.up,Vector3.up);
                Quad(p,q,v,u,Vector3.down,Vector3.down,Vector3.down,Vector3.down);
            }
        }
        if (separate && housing) { Ring(.07f,.049f,.07f,1.23f); Cylinder(.085f,0,.07f); Ring(.0775f,.049f,1.18f,1.25f); }
        else if (separate) { Cylinder(.045f,-1.23f,.03f); Cylinder(.085f,0,.07f); }
        else if (housing) { Cylinder(.07f, 0, 1.23f); Cylinder(.085f, 0, .07f); Cylinder(.0775f, 1.18f, 1.25f); }
        else { Cylinder(.045f, .02f, 1.28f); Cylinder(.085f, 1.25f, 1.32f); }
        var mesh = new Mesh { name = housing ? "Telescopic mast sleeve" : "Telescopic mast piston", hideFlags = HideFlags.HideAndDontSave };
        mesh.vertices = vertices.ToArray(); mesh.triangles = triangles.ToArray();
        mesh.uv = uv.Select(p => new Vector2(p.x*NativeUv.PerMetre,p.y*NativeUv.PerMetre)).ToArray();
        mesh.normals = normals.ToArray(); mesh.RecalculateBounds(); mesh.RecalculateTangents(); return mesh;
    }
}
