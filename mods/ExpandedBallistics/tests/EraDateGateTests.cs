using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using SprocketExpandedBallistics;

// 0.22.9 acceptance checks: real dates at every gate, the final-era sentinel,
// Konkurs/top-attack/gen-4 links, B14 identities and the gate-code grep.
internal static class EraDateGateTests
{
    private static readonly VehicleEraBoundary[] Timeline =
    {
        new("Coldwar1", new DateTime(1945, 9, 4)),
        new("Coldwar2", new DateTime(1960, 1, 1)),
        new("Coldwar3", new DateTime(1975, 1, 1))
    };

    internal static void Run(Action<bool,string> check)
    {
        var responseJson=File.ReadAllText(Path.Combine(AppContext.BaseDirectory,"sprocket.expandedballistics.responses.json"));
        var catalogue=ArmourResponses.Parse(responseJson);
        var tech=TechnologyDates();
        var defaults=ShellProfiles.Parse(ReleaseProfiles.Defaults()).ToDictionary(p=>p.Id,StringComparer.Ordinal);

        // Every response carries the real date as data and metadata, and the
        // installed native Technology file has exactly that date.
        foreach(var response in catalogue.Responses)
        {
            check(response.HistoricalDate==response.MinimumDate.ToString("yyyy.MM.dd",CultureInfo.InvariantCulture),
                "historicalDate equals minimumDate for "+response.ResponseId);
            foreach(var material in response.CompatibleMaterialIds)
                check(tech.TryGetValue(material,out var date)&&date.Date==response.MinimumDate.Date,
                    "native Technology date "+(tech.TryGetValue(material,out var found)?found.ToString("yyyy.MM.dd",CultureInfo.InvariantCulture):"missing")+
                    " equals response "+response.ResponseId+" ("+response.MinimumDate.ToString("yyyy.MM.dd",CultureInfo.InvariantCulture)+")");
        }

        // Material gate: day before / on 2009 and the Coldwar3 sentinel.
        var duplet=catalogue.Responses.Single(r=>r.ResponseId=="dupletCassette");
        check(!VehicleEraAvailability.AllowsMaterial(new DateTime(2008,12,31),Timeline,duplet.MinimumDate,true,true),
            "material gate rejects one day before Duplet 2009");
        check(VehicleEraAvailability.AllowsMaterial(new DateTime(2009,1,1),Timeline,duplet.MinimumDate,true,true),
            "material gate allows Duplet on 2009.01.01");
        check(VehicleEraAvailability.AllowsMaterial(DateTime.MaxValue,Timeline,duplet.MinimumDate,true,true),
            "material gate sentinel 9999.12.31 allows Duplet 2009");
        check(!VehicleEraAvailability.AllowsMaterial(new DateTime(1974,12,31),Timeline,new DateTime(1975,1,1),true,true),
            "Coldwar2 1974.12.31 cannot use a 1975 material");
        check(VehicleEraAvailability.AllowsMaterial(new DateTime(1975,1,1),Timeline,new DateTime(1975,1,1),true,true),
            "a vehicle on 1975.01.01 can use a 1975 material");

        // Shell gate: the same day-before/on rule and the sentinel.
        var gate=new ShellProfile("gate","Gate",new DartSettings(),"he",MinimumDate:new ShellNativeDate(1975,1,1));
        var before=ShellContext(new ShellNativeDate(1974,12,31),1);
        var on=ShellContext(new ShellNativeDate(1975,1,1),2);
        var sentinel=ShellContext(ShellNativeDate.Maximum,2);
        check(!ShellEraPolicy.Allowed(gate,before),"shell minimumDate rejects 1974.12.31");
        check(ShellEraPolicy.Allowed(gate,on),"shell minimumDate allows 1975.01.01");
        check(ShellEraPolicy.Allowed(gate with{MinimumDate=new ShellNativeDate(2009,1,1)},sentinel),
            "shell sentinel 9999.12.31 allows a 2009 minimumDate");
        check(!ShellEraPolicy.Allowed(gate with{MinimumDate=new ShellNativeDate(2009,1,1)},ShellContext(new ShellNativeDate(2008,12,31),2)),
            "shell finite 2008.12.31 rejects a 2009 minimumDate");

        // B05: own dated Technology links and their real dates.
        check(defaults["atgm_konkurs_9m113"].NativeTechnologyIds?.Contains("expandedBallistics_atgm_konkurs")==true,
            "Konkurs links its own Technology");
        check(defaults["atgm_topattack_test"].NativeTechnologyIds?.Contains("expandedBallistics_atgm_topattack_test")==true,
            "top-attack links its own Technology");
        check(defaults["apfsds_gen4"].NativeTechnologyIds?.Contains("expandedBallistics_apfsds_gen4")==true,
            "APFSDS gen 4 links its own Technology");
        check(Date(tech,"expandedBallistics_atgm_konkurs")==new DateTime(1974,1,1),"Konkurs Technology is dated 1974");
        check(Date(tech,"expandedBallistics_atgm_topattack_test")==new DateTime(1991,1,1),"top-attack Technology is dated 1991");
        check(Date(tech,"expandedBallistics_apfsds_gen4")==new DateTime(2003,3,1),"APFSDS gen 4 Technology is dated 2003.03.01");

        // B14: cassette names/dates and hull consistency.
        var cassettes=CassetteCatalogue.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory,"sprocket.expandedballistics.cassettes.json")));
        CassettePreset Find(string id)=>cassettes.Presets.Single(p=>p.Id==id);
        check(Find("burlington").Label=="Challenger 1 (Burlington)"&&Find("burlington").Date=="1983.01.01",
            "Burlington label/date");
        check(Find("leopard2a4").Date=="1985.01.01"&&Find("leopard2a4_hull").Date=="1985.01.01",
            "Leopard 2A4 turret and hull both 1985");
        check(Find("t72m1").Date=="1982.01.01","T-72M1 is 1982");
        check(Find("challenger1_hull").Label=="Challenger 1 (Burlington) hull"&&Find("challenger1_hull").Date=="1983.01.01",
            "Challenger 1 hull label matches the Burlington family");
        foreach(var preset in cassettes.Presets)
            check(tech.TryGetValue(preset.MaterialId,out var date)&&date.Date==Date(preset.Date),
                "cassette "+preset.Id+" date equals its Technology date");

        // The old field still loads once and maps to the 1945.09.03 floor.
        var legacy=JsonNode.Parse(responseJson)!;
        var first=legacy["responses"]!.AsArray()[0]!.AsObject();
        first.Remove("minimumDate");first["minimumEra"]="coldwar";first["historicalDate"]="1945.09.03";
        var migrated=ArmourResponses.Parse(legacy.ToJsonString());
        check(migrated.Responses[0].MinimumDate==new DateTime(1945,9,3)&&ArmourResponses.LegacyMinimumEraUpgraded,
            "legacy minimumEra migrates to minimumDate 1945.09.03");
        check(migrated.Responses.All(r=>r.MinimumDate.Year is >= 0 and <= 9999),"legacy migration keeps a valid date");
        ArmourResponses.ClearLegacyMinimumEraFlag();

        var legacyShell=ReleaseProfiles.Defaults().Replace("\"behavior\": \"he\"","\"behavior\": \"he\", \"minimumEra\": \"Coldwar\"");
        check(ShellProfiles.Parse(legacyShell).Single(p=>p.Behavior=="he").MinimumDate==new ShellNativeDate(1945,9,3),
            "legacy shell minimumEra migrates to its start date");

        // Gate-code grep: no era name or old field in the runtime decision files.
        var root=SourceRoot();
        var gateFiles=new[]{
            "Material/VehicleEraAvailability.cs","Material/VehicleEraAccess.cs","Material/EraPartAvailability.cs",
            "Material/ResponseUi.cs","ShellEraPolicy.cs","ShellEraSelection.cs","ShellEditorAvailability.cs",
            "RuntimeShellEra.cs"
        };
        foreach(var relative in gateFiles)
        {
            var path=Path.Combine(root,relative);
            check(File.Exists(path),"gate file exists: "+relative);
            var text=File.ReadAllText(path);
            check(!text.Contains("minimumEra",StringComparison.Ordinal),"gate file has no minimumEra: "+relative);
            check(!text.Contains("\"coldwar\"",StringComparison.OrdinalIgnoreCase),"gate file has no literal coldwar: "+relative);
        }
        Console.WriteLine($"ERA DATE GATE PASS: {catalogue.Responses.Count} responses, {cassettes.Presets.Length} cassettes, {gateFiles.Length} gate files.");
    }

    private static DateTime Date(Dictionary<string,DateTime> tech,string id)
        =>tech.TryGetValue(id,out var date)?date:DateTime.MinValue;

    private static DateTime Date(string text)=>DateTime.ParseExact(text,"yyyy.MM.dd",CultureInfo.InvariantCulture,DateTimeStyles.None);

    private static ShellEraContext ShellContext(ShellNativeDate date,int index)
    {
        var names=Timeline.Select(t=>t.Name).ToArray();
        var starts=Timeline.Select(t=>new ShellNativeDate(t.Start.Year,t.Start.Month,t.Start.Day)).ToArray();
        return new(date,names,starts,index,new HashSet<string>(StringComparer.Ordinal){"he"});
    }

    private static Dictionary<string,DateTime> TechnologyDates()
    {
        var directory=Path.Combine(AppContext.BaseDirectory,"NativeData");
        if(!Directory.Exists(directory))directory=Path.Combine(AppContext.BaseDirectory,"NativeData","Technology");
        var result=new Dictionary<string,DateTime>(StringComparer.Ordinal);
        foreach(var file in Directory.GetFiles(directory,"*.json"))
        {
            using var doc=JsonDocument.Parse(File.ReadAllText(file),new JsonDocumentOptions{AllowTrailingCommas=true});
            var root=doc.RootElement;
            if(root.ValueKind!=JsonValueKind.Object||!root.TryGetProperty("type",out var type)||!root.TryGetProperty("date",out var dateNode))continue;
            if(!DateTime.TryParseExact(dateNode.GetString(),"yyyy.MM.dd",CultureInfo.InvariantCulture,DateTimeStyles.None,out var date))continue;
            var id=type.GetString()??"";
            if(id.Length>0&&(!result.TryGetValue(id,out var old)||date<old))result[id]=date;
        }
        return result;
    }

    private static string SourceRoot()
    {
        var directory=new DirectoryInfo(AppContext.BaseDirectory);
        while(directory!=null)
        {
            if(File.Exists(Path.Combine(directory.FullName,"ArmourResponses.cs")))return directory.FullName;
            directory=directory.Parent;
        }
        throw new DirectoryNotFoundException("Could not locate the 0.22.9 source root for the gate grep.");
    }
}
