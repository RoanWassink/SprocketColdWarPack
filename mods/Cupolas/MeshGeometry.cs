using System.Numerics;
using System.Security.Cryptography;
namespace SprocketCupolas;

// Managed math is tested without Unity. UV charts may split render vertices at
// seams; positions, authored normals and triangle winding stay unchanged.
internal static class MeshGeometry
{
    internal sealed record Geometry(Vector3[] Positions, Vector3[] Normals, Vector2[] UV, Vector4[] Tangents, int[] Indices);
    internal static Geometry Create(CupolaData d)
    {
        if (d.Positions.Length % 3 != 0 || d.Normals.Length != d.Positions.Length || d.Indices.Length % 3 != 0 || d.Bounds.Length != 6) throw new InvalidDataException("array lengths");
        using var stream = new MemoryStream();
        using (var writer = new BinaryWriter(stream, System.Text.Encoding.UTF8, true)) {
            foreach (float x in d.Positions) writer.Write(x);
            foreach (float x in d.Normals) writer.Write(x);
            foreach (int x in d.Indices) writer.Write(x);
        }
        if (Convert.ToHexString(SHA256.HashData(stream.ToArray())) != d.DataSha256) throw new InvalidDataException("embedded data hash");
        int n = d.Positions.Length / 3;
        var src = new Vector3[n]; var normals = new Vector3[n];
        Vector3 lo = new(float.PositiveInfinity), hi = new(float.NegativeInfinity);
        for (int i = 0; i < n; i++) {
            src[i] = new(d.Positions[3*i], d.Positions[3*i+1], d.Positions[3*i+2]);
            normals[i] = new(d.Normals[3*i], d.Normals[3*i+1], d.Normals[3*i+2]);
            if (!Finite(src[i]) || !Finite(normals[i]) || MathF.Abs(normals[i].Length()-1) > .002f) throw new InvalidDataException("nonfinite/invalid normal");
            lo = Vector3.Min(lo, src[i]); hi = Vector3.Max(hi, src[i]);
        }
        if (Vector3.Distance(lo, new(d.Bounds[0],d.Bounds[1],d.Bounds[2])) > 1e-6f || Vector3.Distance(hi, new(d.Bounds[3],d.Bounds[4],d.Bounds[5])) > 1e-6f || (hi-lo).Length() > 2 || (hi-lo).Length() < .1f) throw new InvalidDataException("bounds/units");
        var points = new List<Vector3>(); var ns = new List<Vector3>(); var uvs = new List<Vector2>(); var indices = new List<int>();
        var map = new Dictionary<(int, int, bool), int>();
        double volume = 0;
        float cz = d.Name == "door" ? .27f : 0;
        for (int t = 0; t < d.Indices.Length; t += 3) {
            var ids = d.Indices.Skip(t).Take(3).ToArray();
            if (ids.Any(i => i < 0 || i >= n)) throw new InvalidDataException("index range");
            Vector3 a=src[ids[0]], b=src[ids[1]], c=src[ids[2]], cross=Vector3.Cross(b-a,c-a);
            if (cross.LengthSquared() < 1e-20f || ids.Any(i => Vector3.Dot(cross, normals[i]) <= 0)) throw new InvalidDataException("zero area/winding");
            volume += Vector3.Dot(a,Vector3.Cross(b,c))/6.0;
            Vector3 face=Vector3.Normalize(cross), centre=(a+b+c)/3, radial=new(centre.X,0,centre.Z-cz);
            bool cylinder = radial.LengthSquared()>.04f && MathF.Abs(face.Y)<.7f && MathF.Abs(Vector3.Dot(Vector3.Normalize(radial),face))>.80f;
            int chart = cylinder ? 6 : MathF.Abs(face.Y)>=MathF.Abs(face.X)&&MathF.Abs(face.Y)>=MathF.Abs(face.Z) ? (face.Y>=0?0:1) : MathF.Abs(face.X)>=MathF.Abs(face.Z) ? (face.X>=0?2:3) : (face.Z>=0?4:5);
            float[] angles=ids.Select(i=>MathF.Atan2(src[i].Z-cz,src[i].X)).ToArray();
            bool seam=cylinder && angles.Max()-angles.Min()>MathF.PI;
            for (int k=0;k<3;k++) {
                int i=ids[k]; bool shift=seam&&angles[k]<0;
                var key=(i,chart,shift);
                if(!map.TryGetValue(key,out int target)) {
                    target=points.Count; map.Add(key,target); points.Add(src[i]); ns.Add(normals[i]);
                    var p=src[i];
                    // Cylinder arc length, and orthonormal axis planes, 1 UV/metre.
                    var uv=chart==6 ? new Vector2((angles[k]+(shift?2*MathF.PI:0))*MathF.Sqrt(p.X*p.X+(p.Z-cz)*(p.Z-cz)),p.Y)
                        : chart<2 ? new Vector2(p.X,chart==0?-p.Z:p.Z)
                        : chart<4 ? new Vector2(p.Z,chart==2?-p.Y:p.Y)
                        : new Vector2(p.X,chart==4?p.Y:-p.Y);
                    uvs.Add(uv);
                }
                indices.Add(target);
            }
        }
        if(volume<=0)throw new InvalidDataException("inward volume");
        var ts=new Vector3[points.Count]; var bs=new Vector3[points.Count];
        for(int t=0;t<indices.Count;t+=3) {
            int a=indices[t],b=indices[t+1],c=indices[t+2];
            Vector3 e1=points[b]-points[a],e2=points[c]-points[a]; Vector2 u1=uvs[b]-uvs[a],u2=uvs[c]-uvs[a];
            float det=u1.X*u2.Y-u1.Y*u2.X;
            if(!float.IsFinite(det)||MathF.Abs(det)<1e-14f)throw new InvalidDataException("degenerate UV");
            Vector3 tangent=(e1*u2.Y-e2*u1.Y)/det, bitangent=(e2*u1.X-e1*u2.X)/det;
            foreach(int i in new[]{a,b,c}){ts[i]+=tangent;bs[i]+=bitangent;}
        }
        var tangents=new Vector4[points.Count];
        for(int i=0;i<points.Count;i++) {
            Vector3 tangent=ts[i]-ns[i]*Vector3.Dot(ns[i],ts[i]);
            if(!Finite(tangent)||tangent.LengthSquared()<1e-14f)throw new InvalidDataException("invalid tangent");
            tangent=Vector3.Normalize(tangent);
            tangents[i]=new(tangent,Vector3.Dot(Vector3.Cross(ns[i],tangent),bs[i])<0?-1:1);
            if(!float.IsFinite(uvs[i].X)||!float.IsFinite(uvs[i].Y))throw new InvalidDataException("nonfinite UV");
        }
        return new(points.ToArray(),ns.ToArray(),uvs.ToArray(),tangents,indices.ToArray());
    }
    private static bool Finite(Vector3 p)=>float.IsFinite(p.X)&&float.IsFinite(p.Y)&&float.IsFinite(p.Z);
}
