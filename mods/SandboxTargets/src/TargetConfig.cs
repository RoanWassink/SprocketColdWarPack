using System.Text.Json;

namespace SprocketSandboxTargets;

public sealed record TargetSpec(double Distance, double Lateral, string? Blueprint, double Facing);
public sealed record TargetConfig(bool Enabled, TargetSpec[] Targets)
{
    public const string DefaultJson = """
    {
      "enabled": true,
      "targets": [
        { "distance": 150, "lateral": -15, "blueprint": null, "facing": "towards" },
        { "distance": 250, "lateral": 15, "blueprint": null, "facing": "towards" },
        { "distance": 350, "lateral": -30, "blueprint": null, "facing": "towards" },
        { "distance": 500, "lateral": 30, "blueprint": null, "facing": "towards" }
      ]
    }
    """;

    public static TargetConfig Defaults() => new(true, new[] {
        new TargetSpec(150,-15,null,0), new TargetSpec(250,15,null,0),
        new TargetSpec(350,-30,null,0), new TargetSpec(500,30,null,0) });

    public static TargetConfig Parse(string json, Action<string>? warn = null)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            var enabled = !root.TryGetProperty("enabled", out var on) || on.GetBoolean();
            if (!root.TryGetProperty("targets", out var entries) || entries.ValueKind != JsonValueKind.Array)
                throw new FormatException("targets moet een array zijn");
            if (entries.GetArrayLength() > 64) throw new FormatException("maximaal 64 doelen");
            var result = new List<TargetSpec>();
            foreach (var entry in entries.EnumerateArray())
            {
                var distance = entry.GetProperty("distance").GetDouble();
                var lateral = entry.GetProperty("lateral").GetDouble();
                if (!double.IsFinite(distance) || distance <= 0 || distance > 10000 ||
                    !double.IsFinite(lateral) || Math.Abs(lateral) >= distance)
                    throw new FormatException("0 < distance <= 10000 en abs(lateral) < distance vereist");
                string? blueprint = null;
                if (entry.TryGetProperty("blueprint", out var bp) && bp.ValueKind != JsonValueKind.Null)
                {
                    blueprint = bp.GetString();
                    if (string.IsNullOrWhiteSpace(blueprint)) throw new FormatException("lege blueprintnaam");
                }
                double facing = 0;
                if (entry.TryGetProperty("facing", out var face))
                {
                    if (face.ValueKind == JsonValueKind.Number) facing = face.GetDouble();
                    else facing = face.GetString() switch {
                        "towards" => 0, "side" => 90,
                        _ => throw new FormatException("facing: towards, side of graden verwacht") };
                }
                if (!double.IsFinite(facing)) throw new FormatException("ongeldige facing");
                result.Add(new(distance, lateral, blueprint, facing % 360));
            }
            return new(enabled, result.ToArray());
        }
        catch (Exception ex) when (ex is JsonException or FormatException or InvalidOperationException or KeyNotFoundException or OverflowException)
        {
            warn?.Invoke("Ongeldige JSON/config; standaardwaarden gebruikt, bestand behouden: " + ex.Message);
            return Defaults();
        }
    }

    public static TargetConfig Load(string path, Action<string> warn)
    {
        try
        {
            if (!File.Exists(path))
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                // Never replace a file that another process created in the meantime.
                using var output = new StreamWriter(new FileStream(path, FileMode.CreateNew, FileAccess.Write));
                output.Write(DefaultJson);
            }
            return Parse(File.ReadAllText(path), warn);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        { warn("Config niet leesbaar/schrijfbaar; standaardwaarden: " + ex.Message); return Defaults(); }
    }
}

public readonly record struct TargetPose(double X, double Z, double Yaw);
public static class TargetGeometry
{
    // Unity yaw: 0=+Z, positive turns clockwise towards +X.
    public static TargetPose Place(double x, double z, double heading, TargetSpec target)
    {
        var radians = heading * Math.PI / 180;
        var forward = Math.Sqrt(target.Distance * target.Distance - target.Lateral * target.Lateral);
        var dx = Math.Sin(radians) * forward + Math.Cos(radians) * target.Lateral;
        var dz = Math.Cos(radians) * forward - Math.Sin(radians) * target.Lateral;
        var yaw = Math.Atan2(-dx, -dz) * 180 / Math.PI + target.Facing;
        return new(x + dx, z + dz, (yaw % 360 + 360) % 360);
    }

    public static string BlueprintPath(string factionsRoot, string name, string currentFaction)
    {
        var pieces = name.Replace('\\','/').Split('/');
        if (pieces.Length == 1) pieces = new[] { currentFaction, pieces[0] };
        if (pieces.Length != 2 || pieces.Any(p => string.IsNullOrWhiteSpace(p) || p is "." or ".." ||
            p.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0))
            throw new FormatException("Gebruik faction/naam of een naam binnen de huidige factie");
        var file = pieces[1].EndsWith(".blueprint", StringComparison.OrdinalIgnoreCase) ? pieces[1] : pieces[1] + ".blueprint";
        var root = Path.GetFullPath(factionsRoot) + Path.DirectorySeparatorChar;
        var full = Path.GetFullPath(Path.Combine(root,pieces[0],"Blueprints","Vehicles",file));
        if (!full.StartsWith(root,StringComparison.OrdinalIgnoreCase)) throw new FormatException("Blueprint buiten factiemap");
        return full;
    }
}
