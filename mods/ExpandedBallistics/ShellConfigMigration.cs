namespace SprocketExpandedBallistics;
// The destination's existence is the one-time creation marker: the numeric CFG values (kept for installs that set them) seed the first file.
internal static class ShellConfigMigration
{
    internal static string EnsureCurrentProfileFile(string configDirectory,DartSettings fallback)
    {
        var destination=Path.Combine(configDirectory,"sprocket.expandedballistics.shells.json");
        EnsureProfileFile(destination,fallback);
        EnsureApheProfile(destination);
        return destination;
    }
    internal static bool EnsureApheProfile(string path)
    {
        var original = File.ReadAllText(path);
        var profiles = ShellProfiles.Parse(original);
        if (profiles.Any(p => p.Id == "aphe") || profiles.Count >= ShellProfiles.MaximumProfiles) return false;
        var root = System.Text.Json.Nodes.JsonNode.Parse(original)!.AsObject();
        var defaults = System.Text.Json.Nodes.JsonNode.Parse(ShellProfiles.CreateDefault(new DartSettings()))!;
        var aphe = defaults["profiles"]![1]!;
        var label = "APHE (enhanced spall)";
        for (var suffix = 2; profiles.Any(p => string.Equals(p.Label,label,StringComparison.OrdinalIgnoreCase)); suffix++)
            label = $"APHE (enhanced spall) {suffix}";
        var copy = System.Text.Json.Nodes.JsonNode.Parse(aphe.ToJsonString())!;
        copy["label"] = label;
        root["profiles"]!.AsArray().Add(copy);
        var repaired = root.ToJsonString(new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
        _ = ShellProfiles.Parse(repaired);
        var backup = path + ".pre-aphe-backup";
        for (var suffix = 2; File.Exists(backup); suffix++) backup = path + $".pre-aphe-backup-{suffix}";
        File.Copy(path, backup, false);
        var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try { File.WriteAllText(temporary,repaired); File.Move(temporary,path,true); }
        finally { if(File.Exists(temporary)) File.Delete(temporary); }
        return true;
    }
    internal static void EnsureProfileFile(string destination, DartSettings fallback)
    {
        if (File.Exists(destination)) return;
        var json = ShellProfiles.CreateDefault(new(fallback.DiameterRatio, fallback.LengthInCalibres, fallback.Density,
            fallback.VelocityEfficiency, fallback.MaxVelocityFactor, fallback.MaxVelocity, fallback.PenetrationQuality,
            fallback.DamageMultiplier, fallback.VelocityMultiplier));
        _ = ShellProfiles.Parse(json);
        using var stream = new FileStream(destination, FileMode.CreateNew, FileAccess.Write);
        using var writer = new StreamWriter(stream);
        writer.Write(json);
    }
}
