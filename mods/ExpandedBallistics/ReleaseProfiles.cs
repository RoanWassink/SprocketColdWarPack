using System.Text.Json;
using System.Text.Json.Nodes;
namespace SprocketExpandedBallistics;

internal static class ReleaseProfiles
{
    internal static string Defaults()
    {
        using var stream=typeof(ReleaseProfiles).Assembly.GetManifestResourceStream("ExpandedBallistics.Defaults")
            ?? throw new InvalidOperationException("Missing built-in shell profiles.");
        using var reader=new StreamReader(stream);
        return reader.ReadToEnd();
    }
    // Frozen 0.21.0 catalogue. Used only to decide whether a live entry is still
    // the shipped default and may be additively replaced by the 0.21.1 default.
    internal static string LegacyDefaults()=>LegacyShellDefaults.Json;
    internal static bool Upgrade(string path)
    {
        var original=File.ReadAllText(path);
        var parsed=ShellProfiles.Parse(original); // Never overwrite invalid custom JSON.
        var frozen=ShellProfiles.Parse(LegacyDefaults()).ToDictionary(p=>p.Id,StringComparer.Ordinal);
        var root=JsonNode.Parse(original)!;
        var entries=root["profiles"]!.AsArray();
        var changed=false;
        foreach(var old in entries.ToArray())
            if(old!["id"]!.GetValue<string>()=="apfsds" && old["label"]!.GetValue<string>() is "APFSDS (beta)" or "APFSDS standard" or "APFSDS")
            {entries.Remove(old);changed=true;}
        var presets=JsonNode.Parse(Defaults())!["profiles"]!.AsArray();
        // Additive replacement: only an entry that still equals the frozen 0.21.0
        // profile is replaced. User-edited labels, numbers and classes stay.
        foreach(var preset in presets)
        {
            var id=preset!["id"]!.GetValue<string>();
            var existing=entries.FirstOrDefault(p=>p!["id"]!.GetValue<string>()==id);
            if(existing==null)continue;
            var live=parsed.FirstOrDefault(p=>p.Id==id);
            if(live==null||!frozen.TryGetValue(id,out var old)||!ShellProfiles.SameProfile(live,old))continue;
            if(ModularShellDraft.SameJson(existing.ToJsonString(),preset.ToJsonString()))continue;
            var copy=JsonNode.Parse(preset!.ToJsonString())!;
            copy["label"]=UniqueLabel(entries,copy["label"]!.GetValue<string>(),existing);
            var index=entries.IndexOf(existing);
            entries[index]=copy;changed=true;
        }
        foreach(var preset in presets)
        {
            if(entries.Any(p=>p!["id"]!.GetValue<string>()==preset!["id"]!.GetValue<string>()) || entries.Count>=ShellProfiles.MaximumProfiles)continue;
            var copy=JsonNode.Parse(preset!.ToJsonString())!;
            copy["label"]=UniqueLabel(entries,copy["label"]!.GetValue<string>(),null);
            entries.Add(copy);changed=true;
        }
        // 0.22.2 -> retiredAfter fields: only a live entry that still exactly equals
        // the frozen 0.22.2 default receives the new default. User-edited entries
        // keep their values, including an explicit keepAfterRetirement.
        var frozen0222=ShellProfiles.Parse(LegacyShellDefaults0222.Json).ToDictionary(p=>p.Id,StringComparer.Ordinal);
        foreach(var entry in entries.ToArray())
        {
            var id=entry!["id"]!.GetValue<string>();
            if(!frozen0222.TryGetValue(id,out var old0222))continue;
            var live=parsed.FirstOrDefault(p=>p.Id==id);
            if(live==null||!ShellProfiles.SameProfile(live,old0222))continue;
            var preset=presets.FirstOrDefault(p=>p!["id"]!.GetValue<string>()==id);
            if(preset==null||ModularShellDraft.SameJson(entry.ToJsonString(),preset.ToJsonString()))continue;
            var copy=JsonNode.Parse(preset!.ToJsonString())!;
            copy["label"]=UniqueLabel(entries,copy["label"]!.GetValue<string>(),entry);
            entries[entries.IndexOf(entry)]=copy;changed=true;
        }
        // Expose new motor controls without changing established custom ATGM flight.
        foreach(var entry in entries)
        {
            var profile=parsed.FirstOrDefault(p=>p.Id==entry!["id"]!.GetValue<string>());
            if(profile==null || !ShellBalance.IsAtgm(profile.Behavior) || profile.Atgm is not {} flight)continue;
            foreach(var setting in new Dictionary<string,JsonNode?> {
                ["launchSpeed"]=JsonValue.Create(flight.LaunchSpeed ?? flight.FlightSpeed),
                ["launchSpeedMode"]=JsonValue.Create(flight.LaunchSpeedMode),
                ["launchSpeedMultiplier"]=JsonValue.Create(flight.LaunchSpeedMultiplier),
                ["acceleration"]=JsonValue.Create(flight.Acceleration), ["motorDelay"]=JsonValue.Create(flight.MotorDelay), ["motorBurnTime"]=JsonValue.Create(flight.MotorBurnTime),
                ["coastDeceleration"]=JsonValue.Create(flight.CoastDeceleration)})
                if(!entry!.AsObject().ContainsKey(setting.Key)){entry[setting.Key]=setting.Value;changed=true;}
        }
        // Guidance-only stock migration; independently customized warheads are retained.
        foreach(var entry in entries)if(entry is JsonObject obj&&TopAttackStockMigration.Apply(obj))changed=true;
        // Rename recognized stock experimental labels only; preserve custom names and settings.
        var labelUpdates=new Dictionary<string,(string Label,string[] Old)> {
            ["he"]=("HE",new[]{"HE (native blast experiment)"}),
            ["heat"]=("HEAT / HEAT-FS",new[]{"HEAT (jet proxy experiment)"}),
            ["hesh"]=("HESH",new[]{"HESH (spall proxy experiment)"}),
            ["atgm_konkurs_test"]=("SACLOS ATGM",new[]{"Konkurs-like ATGM","Konkurs-like ATGM (TEST)"}),
            ["atgm_mclos_test"]=("MCLOS ATGM",new[]{"MCLOS keyboard ATGM","MCLOS keyboard ATGM (TEST)"})
        };
        foreach(var entry in entries)
            if(labelUpdates.TryGetValue(entry!["id"]!.GetValue<string>(),out var update) &&
                update.Old.Contains(entry["label"]!.GetValue<string>()) &&
                !entries.Any(other=>other!=entry&&string.Equals(other!["label"]!.GetValue<string>(),update.Label,StringComparison.OrdinalIgnoreCase)))
            {entry["label"]=update.Label;changed=true;}
        if(!changed)return false;
        var json=root.ToJsonString(new JsonSerializerOptions{WriteIndented=true});
        _=ShellProfiles.Parse(json);
        var backup=path+".pre-v094-backup";
        for(var suffix=2;File.Exists(backup);suffix++)backup=path+$".pre-v094-backup-{suffix}";
        File.Copy(path,backup,false);
        var temp=path+"."+Guid.NewGuid().ToString("N")+".tmp";
        try{File.WriteAllText(temp,json);File.Move(temp,path,true);}
        finally{if(File.Exists(temp))File.Delete(temp);}
        return true;
    }
    private static string UniqueLabel(JsonArray entries,string label,JsonNode? replacing)
    {
        var used=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach(var entry in entries)
            if(entry!=replacing&&entry is JsonObject)
                used.Add(entry["label"]!.GetValue<string>());
        if(!used.Contains(label))return label;
        for(var suffix=2;;suffix++)
        {
            var candidate=label+" "+suffix;
            if(!used.Contains(candidate))return candidate;
        }
    }
}
