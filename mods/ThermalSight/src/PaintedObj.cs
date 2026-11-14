using System.Globalization;
using System.Numerics;

namespace SprocketThermalSight;

// Explicit surface contracts are shared by the runtime and pure package tests.
public sealed record ObjSurface(string Name, int Triangles, bool Paint);
public sealed record ObjLayout(int Positions, ObjSurface[] Surfaces);
public static class CamoSurfaces
{
    public static ObjLayout Layout(string file) => file switch
    {
        "thermal-head.obj" or "thermal-head-vertical.obj" => new(82,new[]{new ObjSurface("Paint",52,true),new("Dark",40,false),new("Lens",16,false)}),
        "automatic-lrf.obj" => new(48,new[]{new ObjSurface("Paint",72,true)}),
        "lrf-concept.obj" => new(80,new[]{new ObjSurface("Paint",24,true),new("PaintLight",24,true),new("Glass",12,false),new("Mount",60,true)}),
        "optical-rangefinder.obj" => new(178,new[]{new ObjSurface("Paint",168,true),new("Tube",120,true),new("Glass",24,false)}),
        _ => throw new InvalidDataException("Unknown painted OBJ: "+file)
    };
}

public sealed class PaintedObj
{
    public Vector3[] Vertices { get; private init; } = Array.Empty<Vector3>();
    public Vector2[] Uv { get; private init; } = Array.Empty<Vector2>();
    public Vector3[] Normals { get; private init; } = Array.Empty<Vector3>();
    public int[][] Triangles { get; private init; } = Array.Empty<int[]>();
    public int PositionCount { get; private init; }
    public static PaintedObj Read(string path) => Parse(File.ReadLines(path),CamoSurfaces.Layout(Path.GetFileName(path)));
    public static PaintedObj Parse(IEnumerable<string> lines,ObjLayout layout)
    {
        var positions=new List<Vector3>();var texcoords=new List<Vector2>();var sourceNormals=new List<Vector3>();
        var vertices=new List<Vector3>();var uv=new List<Vector2>();var normals=new List<Vector3>();
        var faces=layout.Surfaces.Select(_=>new List<int>()).ToArray();
        int surface=Array.FindIndex(layout.Surfaces,s=>s.Name=="Paint");
        float F(string text)
        {
            float value=float.Parse(text,CultureInfo.InvariantCulture);
            if(!float.IsFinite(value))throw new InvalidDataException("Non-finite OBJ attribute");
            return value;
        }
        foreach(var line in lines)
        {
            var f=line.Split((char[]?)null,StringSplitOptions.RemoveEmptyEntries);
            if(f.Length==0 || f[0].StartsWith('#'))continue;
            if(f[0]=="v" && f.Length==4)positions.Add(new(F(f[1]),F(f[2]),F(f[3])));
            else if(f[0]=="vt" && f.Length==3)
            {
                var value=new Vector2(F(f[1]),F(f[2]));
                if(Math.Abs(value.X)>100 || Math.Abs(value.Y)>100)throw new InvalidDataException("OBJ UV range");
                texcoords.Add(value);
            }
            else if(f[0]=="vn" && f.Length==4)
            {
                var value=new Vector3(F(f[1]),F(f[2]),F(f[3]));
                if(Math.Abs(value.LengthSquared()-1)> .001f)throw new InvalidDataException("OBJ normal length");
                sourceNormals.Add(value);
            }
            else if(f[0]=="usemtl" && f.Length==2)
            {
                surface=Array.FindIndex(layout.Surfaces,s=>s.Name==f[1]);
                if(surface<0)throw new InvalidDataException("Unknown OBJ surface");
            }
            else if(f[0]=="f")
            {
                if(f.Length!=4 || surface<0)throw new InvalidDataException("OBJ must be triangulated");
                for(int i=1;i<4;i++)
                {
                    var p=f[i].Split('/');
                    if(p.Length!=3)throw new InvalidDataException("OBJ v/vt/vn required");
                    int a=int.Parse(p[0],CultureInfo.InvariantCulture)-1,b=int.Parse(p[1],CultureInfo.InvariantCulture)-1,c=int.Parse(p[2],CultureInfo.InvariantCulture)-1;
                    if(a<0 || a>=positions.Count || b<0 || b>=texcoords.Count || c<0 || c>=sourceNormals.Count)throw new InvalidDataException("OBJ tuple out of bounds");
                    faces[surface].Add(vertices.Count);vertices.Add(positions[a]);uv.Add(texcoords[b]);normals.Add(sourceNormals[c]);
                }
            }
            else if(f[0] is "o" or "g" or "mtllib" or "s")continue;
            else throw new InvalidDataException("Unexpected OBJ record: "+f[0]);
        }
        if(positions.Count!=layout.Positions || faces.Where((f,i)=>f.Count!=layout.Surfaces[i].Triangles*3).Any()
            || vertices.Count!=uv.Count || vertices.Count!=normals.Count || texcoords.Count!=vertices.Count || sourceNormals.Count!=vertices.Count/3)
            throw new InvalidDataException("Unexpected painted OBJ counts");
        return new(){Vertices=vertices.ToArray(),Uv=uv.ToArray(),Normals=normals.ToArray(),Triangles=faces.Select(f=>f.ToArray()).ToArray(),PositionCount=positions.Count};
    }
}
