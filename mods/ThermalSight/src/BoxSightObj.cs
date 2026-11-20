using System.Globalization;
using System.Numerics;

namespace SprocketThermalSight;

// Pure OBJ reader shared by the runtime and managed package checks.
public sealed class BoxSightObj
{
    public const string Id = "laserBoxSight";
    public const string Guid = "b1da088b-dbc0-4a49-8af0-3dec94d8ebe5";
    public Vector3[] Vertices { get; private init; } = Array.Empty<Vector3>();
    public Vector2[] Uv { get; private init; } = Array.Empty<Vector2>();
    public Vector3[] Normals { get; private init; } = Array.Empty<Vector3>();
    public int[][] Triangles { get; private init; } = Array.Empty<int[]>();
    public Vector3 Min { get; private init; }
    public Vector3 Max { get; private init; }
    public int PositionCount { get; private init; }
    public int TexcoordCount { get; private init; }
    public int NormalCount { get; private init; }
    public static BoxSightObj Read(string path) => Parse(File.ReadLines(path));
    public static BoxSightObj Parse(IEnumerable<string> lines)
    {
        var positions = new List<Vector3>(); var texcoords = new List<Vector2>(); var sourceNormals = new List<Vector3>();
        var vertices = new List<Vector3>(); var uv = new List<Vector2>(); var normals = new List<Vector3>();
        var faces = new[] { new List<int>(), new List<int>(), new List<int>(), new List<int>() };
        int material = -1, nextMaterial = 0;
        float F(string value)
        {
            float result = float.Parse(value, CultureInfo.InvariantCulture);
            if (!float.IsFinite(result)) throw new InvalidDataException("Box sight non-finite attribute");
            return result;
        }
        foreach (var line in lines)
        {
            var f = line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            if (f.Length == 0 || f[0].StartsWith('#')) continue;
            if (f[0] == "v" && f.Length == 4) positions.Add(new(F(f[1]), F(f[2]), F(f[3])));
            else if (f[0] == "vt" && f.Length == 3) texcoords.Add(new(F(f[1]), F(f[2])));
            else if (f[0] == "vn" && f.Length == 4) sourceNormals.Add(new(F(f[1]), F(f[2]), F(f[3])));
            else if (f[0] == "usemtl" && f.Length == 2)
            {
                material = f[1] switch { "paint" => 0, "steel" => 1, "dark" => 2, "lens" => 3, _ => -1 };
                if (material != nextMaterial++) throw new InvalidDataException("Box sight material order");
            }
            else if (f[0] == "f")
            {
                if (f.Length != 4 || material < 0) throw new InvalidDataException("Box sight must have triangulated surfaces");
                for (int i = 1; i < 4; i++)
                {
                    var p = f[i].Split('/');
                    if (p.Length != 3) throw new InvalidDataException("Box sight v/vt/vn missing");
                    int a = int.Parse(p[0], CultureInfo.InvariantCulture) - 1,
                        b = int.Parse(p[1], CultureInfo.InvariantCulture) - 1,
                        c = int.Parse(p[2], CultureInfo.InvariantCulture) - 1;
                    if (a < 0 || a >= positions.Count || b < 0 || b >= texcoords.Count || c < 0 || c >= sourceNormals.Count)
                        throw new InvalidDataException("Box sight index out of bounds");
                    faces[material].Add(vertices.Count); vertices.Add(positions[a]); uv.Add(texcoords[b]); normals.Add(sourceNormals[c]);
                }
            }
            else throw new InvalidDataException("Unexpected box sight OBJ record: " + f[0]);
        }
        int[] expected = { 2700, 914, 748, 672 };
        if (positions.Count != 2668 || texcoords.Count != 15102 || sourceNormals.Count != 1113 || nextMaterial != 4
            || faces.Where((f, i) => f.Count != expected[i] * 3).Any())
            throw new InvalidDataException("Unexpected box sight geometry counts");
        var min = positions.Aggregate(Vector3.Min); var max = positions.Aggregate(Vector3.Max);
        if (Vector3.Distance(min, new(-.186f, 0, -.345458f)) > .000002f
            || Vector3.Distance(max, new(.186f, .273f, .468883f)) > .000002f)
            throw new InvalidDataException("Unexpected box sight bounds (scene or wrong orientation)");
        return new() { Vertices = vertices.ToArray(), Uv = uv.ToArray(), Normals = normals.ToArray(),
            Triangles = faces.Select(f => f.ToArray()).ToArray(), Min = min, Max = max,
            PositionCount = positions.Count, TexcoordCount = texcoords.Count, NormalCount = sourceNormals.Count };
    }
}
