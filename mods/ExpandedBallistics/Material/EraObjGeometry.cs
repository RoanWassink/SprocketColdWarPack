using System.Globalization;

namespace SprocketExpandedBallistics.Materials;

// Shared by the native mesh adapter and managed loader regression checks.
internal static class EraObjGeometry
{
    internal const int MaximumExpandedVertices = 20_000;

    internal static List<T> Read<T>(IEnumerable<string> lines, string group,
        Func<float, float, float, T> vertex, Func<T, bool> finite,
        Func<T, T, T, bool>? skipTriangle = null)
    {
        var positions = new List<T>();
        var expanded = new List<T>();
        string activeGroup = "";
        foreach (string line in lines)
        {
            var fields = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (fields.Length == 0) continue;
            if (fields[0] == "g") activeGroup = fields[1];
            if (fields[0] == "v") positions.Add(vertex(
                float.Parse(fields[1], CultureInfo.InvariantCulture),
                float.Parse(fields[2], CultureInfo.InvariantCulture),
                float.Parse(fields[3], CultureInfo.InvariantCulture)));
            if (fields[0] != "f" || activeGroup != group) continue;
            if (fields.Length != 4) throw new InvalidDataException("Relikt triangle required");
            var triangle = new int[3];
            for (int i = 1; i < 4; i++)
            {
                int index = int.Parse(fields[i], CultureInfo.InvariantCulture) - 1;
                if (index < 0 || index >= positions.Count) throw new InvalidDataException("Relikt vertex index");
                triangle[i - 1] = index;
            }
            var a = positions[triangle[0]]; var b = positions[triangle[1]]; var c = positions[triangle[2]];
            if (skipTriangle?.Invoke(a, b, c) == true) continue;
            expanded.Add(a); expanded.Add(b); expanded.Add(c);
        }
        if (expanded.Count == 0 || expanded.Count > MaximumExpandedVertices || expanded.Any(v => !finite(v)))
            throw new InvalidDataException("Relikt geometry invalid");
        return expanded;
    }
}
