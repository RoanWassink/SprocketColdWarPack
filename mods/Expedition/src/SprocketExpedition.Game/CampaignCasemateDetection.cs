using System.Text.Json;

namespace SprocketExpedition.Game;

// Pure v0.2-blueprint detection. A casemate has no rotating turret ring between
// the main gun and the hull, or the whole azimuth chain is smaller than 60 deg.
// Ambiguous or unreadable content is deliberately not a casemate.
internal static class CampaignCasemateDetection
{
    internal const float MinimumTraverseDegrees = 60f;
    private const float Epsilon = .01f;

    internal enum Source { None, Detected, Config }

    internal readonly record struct Decision(bool IsAmbusher, Source Origin)
    {
        internal string SourceName => Origin switch
        {
            Source.Detected => "detected",
            Source.Config => "config",
            _ => "none"
        };
    }

    internal static IReadOnlyList<string> ParseNames(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return Array.Empty<string>();
        var result = new List<string>();
        foreach (var part in text.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (part.Length == 0) continue;
            if (!result.Contains(part, StringComparer.OrdinalIgnoreCase)) result.Add(part);
        }
        return result;
    }

    // Config wins over detection. NotAmbusherDesigns is checked first so a name
    // in both lists stays conservative and cannot become an ambusher by typo.
    internal static Decision Decide(string? designName, byte[]? bytes,
        IReadOnlyCollection<string>? forceAmbusher, IReadOnlyCollection<string>? denyAmbusher)
    {
        if (!string.IsNullOrWhiteSpace(designName) && Contains(denyAmbusher, designName))
            return new Decision(false, Source.Config);
        if (!string.IsNullOrWhiteSpace(designName) && Contains(forceAmbusher, designName))
            return new Decision(true, Source.Config);
        return new Decision(IsCasemate(bytes), Source.Detected);
    }

    internal static bool IsCasemate(byte[]? bytes)
    {
        if (bytes is not { Length: > 0 }) return false;
        try
        {
            using var document = JsonDocument.Parse(bytes);
            return IsCasemate(document.RootElement);
        }
        catch (JsonException) { return false; }
        catch (ArgumentException) { return false; }
    }

    private static bool IsCasemate(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Object
            || !root.TryGetProperty("objects", out var objectsElement)
            || objectsElement.ValueKind != JsonValueKind.Array
            || !root.TryGetProperty("blueprints", out var blueprintsElement)
            || blueprintsElement.ValueKind != JsonValueKind.Array) return false;

        var objects = new Dictionary<int, JsonElement>();
        foreach (var item in objectsElement.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object || !TryInt(item, "vuid", out var vuid)) continue;
            objects[vuid] = item;
        }
        var components = new Dictionary<int, JsonElement>();
        var turretArcs = new List<float>();
        foreach (var item in blueprintsElement.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object || !TryInt(item, "id", out var id)
                || !item.TryGetProperty("blueprint", out var blueprint) || blueprint.ValueKind != JsonValueKind.Object)
                continue;
            components[id] = blueprint;
            if (item.TryGetProperty("type", out var type) && type.ValueKind == JsonValueKind.String
                && string.Equals(type.GetString(), "turret", StringComparison.Ordinal)
                && TryFloat(blueprint, "minRot", out var minRot)
                && TryFloat(blueprint, "maxRot", out var maxRot))
                turretArcs.Add(Math.Abs(maxRot - minRot));
        }
        if (objects.Count == 0 || components.Count == 0) return false;

        // Main gun = largest native caliber. Equal-caliber guns are only
        // unambiguous when they share one mantlet/turret-ring mount.
        var cannons = new List<(int Vuid, double Caliber)>();
        foreach (var (vuid, item) in objects)
        {
            if (!item.TryGetProperty("cannon", out _)
                || !TryInt(item, "cannonBlueprintVuid", out var cannonId)
                || !components.TryGetValue(cannonId, out var cannon)
                || !TryDouble(cannon, "caliber", out var caliber)
                || !(caliber > 0)) continue;
            cannons.Add((vuid, caliber));
        }
        if (cannons.Count == 0) return false;
        var maxCaliber = cannons.Max(candidate => candidate.Caliber);
        var main = cannons.Where(candidate => Math.Abs(candidate.Caliber - maxCaliber) <= Epsilon).ToArray();
        if (main.Length == 0) return false;
        var mounts = new List<(int Mantlet, int Ring)>();
        foreach (var candidate in main)
        {
            // A broken or unprovable mount on the largest gun is ambiguity,
            // not a reason to fall back to a smaller auxiliary weapon.
            if (!TryMount(candidate.Vuid, objects, out var mantlet, out var ring)) return false;
            mounts.Add((mantlet, ring));
        }
        if (mounts.Distinct().Count() != 1 || mounts[0].Mantlet == 0) return false;

        var mount = mounts[0];
        if (!TryLayingDriveArc(mount.Mantlet, objects, components, out var driveArc)) return false;

        // No turret ring on the gun's ancestor chain is a hull-mounted gun.
        if (mount.Ring == 0) return true;
        // A ring whose native turret cannot rotate is also a fixed mount. If
        // several different turret components exist, the association is not
        // provable, so conservative non-casemate wins.
        if (turretArcs.Count == 0) return false;
        if (turretArcs.Distinct().Count() != 1) return false;
        var turretArc = turretArcs[0];
        if (turretArc <= .5f) return true;
        return driveArc + turretArc < MinimumTraverseDegrees;
    }

    private static bool TryMount(int cannonVuid, IReadOnlyDictionary<int, JsonElement> objects,
        out int mantletVuid, out int ringVuid)
    {
        mantletVuid = 0;
        ringVuid = 0;
        var current = cannonVuid;
        for (var guard = 0; guard < 64; guard++)
        {
            if (!objects.TryGetValue(current, out var item)) return false;
            if (mantletVuid == 0 && item.TryGetProperty("mantlet", out _)) mantletVuid = current;
            if (ringVuid == 0 && item.TryGetProperty("turretRing", out _)) ringVuid = current;
            if (!TryInt(item, "pvuid", out current) || current == 0) return true;
        }
        // A cycle or an implausibly deep chain is not trustworthy.
        return false;
    }

    private static bool TryLayingDriveArc(int mantletVuid, IReadOnlyDictionary<int, JsonElement> objects,
        IReadOnlyDictionary<int, JsonElement> components, out float arc)
    {
        arc = 0;
        if (!objects.TryGetValue(mantletVuid, out var mantlet)
            || !mantlet.TryGetProperty("mantletBlueprint", out var link)
            || link.ValueKind != JsonValueKind.Object
            || !TryInt(link, "drive_vuid", out var driveVuid)) return false;
        JsonElement? driveObject = null;
        foreach (var item in objects.Values)
            if (TryInt(item, "layingDrive", out var value) && value == driveVuid) { driveObject = item; break; }
        if (driveObject is not { } drive
            || !TryInt(drive, "layingDriveBlueprintVuid", out var driveId)
            || !components.TryGetValue(driveId, out var driveBlueprint)
            || !driveBlueprint.TryGetProperty("azimuth", out var azimuth)
            || azimuth.ValueKind != JsonValueKind.Object
            || !TryFloat(azimuth, "min", out var min)
            || !TryFloat(azimuth, "max", out var max)) return false;
        arc = Math.Abs(max - min);
        return float.IsFinite(arc);
    }

    private static bool Contains(IReadOnlyCollection<string>? names, string value) =>
        names != null && names.Any(name => string.Equals(name, value, StringComparison.OrdinalIgnoreCase));

    private static bool TryInt(JsonElement item, string name, out int value)
    {
        value = 0;
        return item.TryGetProperty(name, out var property) && property.ValueKind == JsonValueKind.Number
            && property.TryGetInt32(out value);
    }

    private static bool TryDouble(JsonElement item, string name, out double value)
    {
        value = 0;
        return item.TryGetProperty(name, out var property) && property.ValueKind == JsonValueKind.Number
            && property.TryGetDouble(out value) && double.IsFinite(value);
    }

    private static bool TryFloat(JsonElement item, string name, out float value)
    {
        value = 0;
        if (!TryDouble(item, name, out var number)) return false;
        value = (float)number;
        return float.IsFinite(value);
    }
}
