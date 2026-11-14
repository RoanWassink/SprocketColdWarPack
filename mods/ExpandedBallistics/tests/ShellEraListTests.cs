using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using SprocketExpandedBallistics;

// Era/retirement visibility for the three choice lists. All policy here is pure
// managed code; RuntimeShellEra is a thin native-context wrapper.
internal static class ShellEraListTests
{
    private static int checks;
    private static void Check(bool condition,string message)
    {
        checks++;
        if(!condition)throw new Exception(message);
    }
    private sealed record TechRecord(string Type,ShellNativeDate Date,bool Enabled,double? Factor);
    private static readonly (ShellNativeDate Start,string Name)[] Timeline =
    {
        (new ShellNativeDate(1900,1,1),"Early"),(new ShellNativeDate(1945,9,3),"Coldwar"),
        (new ShellNativeDate(1960,1,1),"Coldwar2"),(new ShellNativeDate(1975,1,1),"Coldwar3")
    };

    internal static int Run()
    {
        var defaults=ShellProfiles.Parse(ReleaseProfiles.Defaults());
        Check(ShellProfiles.Parse(ShellProfiles.CreateDefault(new DartSettings())).Single(p=>p.Id=="aphe").RetiredAfter==new ShellNativeDate(1960,1,1),
            "fresh-install APHE repair keeps the retirement default");
        var tech=ReadTech();
        ShellEraContext Context(ShellNativeDate date)
        {
            var names=Timeline.Select(x=>x.Name).ToArray();
            var starts=Timeline.Select(x=>x.Start).ToArray();
            var index=Array.FindLastIndex(starts,s=>date>=s);
            Check(index>=0,"test timeline covers "+date.Key);
            var available=new HashSet<string>(StringComparer.Ordinal);
            foreach(var record in tech)if(record.Enabled&&record.Date<=date)available.Add(record.Type);
            var behaviors=new HashSet<string>(StringComparer.Ordinal);
            foreach(var behavior in new[]{"ap","he","aphe","heat","hesh","apfsds","atgm","atgm_gun"})
                if(available.Contains("expandedBallistics_"+behavior))behaviors.Add(behavior);
            var heat=Latest("expandedBallistics_heat",date)?.Factor;
            var context=new ShellEraContext(date,names,starts,index,behaviors,heat,available);
            Check(ShellEraPolicy.Valid(context),"test context valid for "+date.Key);
            return context;
        }
        TechRecord? Latest(string type,ShellNativeDate date)
        {
            TechRecord? best=null;
            foreach(var record in tech)
                if(record.Type==type&&record.Enabled&&record.Date<=date&&(best==null||record.Date>best.Date))best=record;
            return best;
        }
        string[] Visible(int year,bool launcher,params string[] keep)
        {
            var kept=new HashSet<string>(keep,StringComparer.Ordinal);
            return ShellEraSelection.Selectable(defaults,Context(new ShellNativeDate(year,1,1)),launcher,kept)
                .Select(p=>p.Id).ToArray();
        }
        ShellEraAvailability Reason(string id,int year,bool launcher)
        {
            var profile=defaults.Single(p=>p.Id==id);
            return ShellEraSelection.Availability(profile,Context(new ShellNativeDate(year,1,1)),launcher);
        }
        static string Ids(IEnumerable<string> ids)=>string.Join(",",ids);
        void Expect(int year,string[] expected)
        {
            var actual=Visible(year,false);
            Check(actual.SequenceEqual(expected),$"visible ordinary list {year}: {Ids(actual)}");
        }

        // Documented expected lists, in default-catalogue order.
        Expect(1943,new[]{"he","heat","aphe","apcbc","apcr"});
        Expect(1950,new[]{"he","heat","aphe","apcbc","apcr","apds"});
        Expect(1965,new[]{"apfsds_short","he","heat","hesh","apds","apds_late"});
        Expect(1975,new[]{"apfsds_short","he","heat","hesh","apds_late"});
        Expect(1980,new[]{"apfsds_short","apfsds_gen2","he","heat","hesh","apds_late","atgm_gun_hump_v1","atgm_kobra_9m112m"});
        Expect(1990,new[]{"apfsds_long","apfsds_gen2","he","heat","heat_tandem","hesh","atgm_gun_hump_v1","atgm_kobra_9m112m"});
        var launcher1975=Visible(1975,true);
        Check(launcher1975.SequenceEqual(new[]{"atgm_tow_bgm71a","atgm_mclos_manual_v1","atgm_konkurs_9m113","atgm_swingfire"}),
            "visible launcher list 1975: "+Ids(launcher1975));
        Check(!Visible(1975,false).Contains("atgm_tow_bgm71a")&&!Visible(1975,false).Contains("atgm_topattack_test"),
            "launcher ATGMs hidden on an ordinary cannon");
        Check(Visible(1980,false).Contains("atgm_gun_hump_v1")&&Visible(1980,false).Contains("atgm_kobra_9m112m"),
            "gun-launched ATGMs remain visible on an ordinary cannon");

        // Retirement, archive and explicit keep behaviour.
        var retired=Reason("aphe",1975,false);
        Check(!retired.Selectable&&retired.Reason=="Retired 1960"&&retired.Until==new ShellNativeDate(1960,1,1),"retired APHE reason");
        var archived=defaults.Single(p=>p.Id=="aphe") with{KeepAfterRetirement=true};
        var archive=ShellEraSelection.Availability(archived,Context(new ShellNativeDate(1975,1,1)),false);
        Check(archive.Selectable&&archive.Reason=="Archive (retired 1960)","archive APHE reason");
        Check(!Visible(1975,false).Contains("aphe")&&Visible(1975,false,"aphe").Contains("aphe"),"saved retired profile stays visible");
        Check(Visible(1975,false,"atgm_tow_bgm71a").Contains("atgm_tow_bgm71a"),"saved launcher ATGM stays visible on an ordinary cannon");
        var retiredStored=defaults.Single(p=>p.Id=="aphe");
        var retiredContext=Context(new ShellNativeDate(1975,1,1));
        Check(ShellEraPolicy.Allowed(retiredStored,retiredContext)&&ShellEraPolicy.Effective(retiredStored,retiredContext)==retiredStored,
            "retired stored choice remains allowed and resolvable");
        Check(ShellEraSelection.Selectable(retiredStored,retiredContext,false,true).Selectable,"keep keeps the stored retired choice selectable");

        // Reasons and boundary cases.
        Check(Reason("apds",1943,false).Reason=="From 1944"&&Reason("apds",1943,false).From==new ShellNativeDate(1944,6,1),"APDS starts in 1944");
        Check(Reason("apfsds_gen4",1975,false).Reason=="From 2003","gen 4 starts in 2003");
        Check(Reason("atgm_gun_hump_v1",1975,false).Reason=="From 1976","Kobra starts in 1976");
        Check(Reason("atgm_kobra_9m112m",1975,false).Reason=="From 1978","9M112M starts in 1978");
        Check(Reason("heat_tandem",1975,false).Reason=="From 1987","tandem HEAT starts in 1987");
        Check(Reason("hesh",1950,false).Reason=="From 1955","HESH starts in 1955");
        Check(Reason("apds_late",1990,false).Reason=="Retired 1985","APDS late retired in 1985");
        Check(Reason("atgm_tow_bgm71a",1975,false).Reason=="Launcher only","launcher ATGM reports launcher only");
        Check(Reason("atgm_tow_bgm71a",1975,true).Selectable&&Reason("atgm_tow_bgm71a",1975,true).Reason=="","launcher ATGM selectable on a launcher");
        Check(Reason("he",1975,true).Reason=="Unavailable","ordinary gun shell not selectable on a launcher");
        var missingTech=defaults.Single(p=>p.Id=="he") with{NativeTechnologyIds=new[]{"missing_shell_tech"}};
        Check(ShellEraSelection.Availability(missingTech,Context(new ShellNativeDate(1975,1,1)),false).Reason=="Unavailable","unknown technology reports unavailable");
        var futureDate=defaults.Single(p=>p.Id=="he") with{MinimumDate=new ShellNativeDate(2100,1,1)};
        Check(ShellEraSelection.Availability(futureDate,Context(new ShellNativeDate(1975,1,1)),false).Reason=="From 2100","future minimumDate reports unavailable");

        // Strict field validation: only yyyy.MM.dd and a JSON boolean.
        var apheNode=JsonNode.Parse(ReleaseProfiles.Defaults())!["profiles"]!.AsArray()
            .Single(p=>p!["id"]!.GetValue<string>()=="aphe")!;
        bool Reject(JsonNode profile)
        {
            try
            {
                var copy=JsonNode.Parse(profile.ToJsonString())!;
                _=ShellProfiles.Parse(new JsonObject{["schemaVersion"]=1,["profiles"]=new JsonArray(copy)}.ToJsonString());
            }
            catch(FormatException){return true;}
            return false;
        }
        var badKeep=JsonNode.Parse(apheNode.ToJsonString())!;badKeep["keepAfterRetirement"]="yes";
        Check(Reject(badKeep),"keepAfterRetirement rejects a string");
        var badDate=JsonNode.Parse(apheNode.ToJsonString())!;badDate["retiredAfter"]="1960-01-01";
        Check(Reject(badDate),"retiredAfter rejects a non-strict date");
        var beforeStart=JsonNode.Parse(apheNode.ToJsonString())!;beforeStart["retiredAfter"]="1930.01.01";
        Check(Reject(beforeStart),"retiredAfter must be later than the earliest availability");
        var validKeep=JsonNode.Parse(apheNode.ToJsonString())!;validKeep["keepAfterRetirement"]=true;
        Check(!Reject(validKeep),"keepAfterRetirement accepts a JSON boolean");
        Check(ShellProfiles.Parse(new JsonObject{["schemaVersion"]=1,["profiles"]=new JsonArray(validKeep)}.ToJsonString())[0].KeepAfterRetirement,
            "keepAfterRetirement parses true");

        // Editor draft yes/no round trip and preservation on compile.
        var draft=new ModularShellDraft(ReleaseProfiles.Defaults());
        int IndexOf(string id)=>Enumerable.Range(0,draft.Profiles.Count)
            .Single(i=>draft.Profiles[i]!["id"]!.GetValue<string>()==id);
        var apheIndex=IndexOf("aphe");var heIndex=IndexOf("he");
        Check(draft.Active(apheIndex,"keepAfterRetirement"),"archive choice visible for a retired profile");
        Check(!draft.Active(heIndex,"keepAfterRetirement"),"archive choice hidden without retiredAfter");
        Check(draft.Value(apheIndex,"modifiers.keepAfterRetirement").GetValue<string>()=="no","draft defaults to no");
        draft.Set(apheIndex,"modifiers.keepAfterRetirement","yes");
        var compiled=draft.Compile();
        Check(compiled.Ready,"draft with archive choice compiles");
        var compiledAphe=ShellProfiles.Parse(compiled.RuntimeJson!).Single(p=>p.Id=="aphe");
        Check(compiledAphe.KeepAfterRetirement&&compiledAphe.RetiredAfter==new ShellNativeDate(1960,1,1),"draft yes compiles to true and keeps retiredAfter");
        var reopened=new ModularShellDraft(compiled.RuntimeJson!);
        var reopenedIndex=Enumerable.Range(0,reopened.Profiles.Count).Single(i=>reopened.Profiles[i]!["id"]!.GetValue<string>()=="aphe");
        Check(reopened.Value(reopenedIndex,"modifiers.keepAfterRetirement").GetValue<string>()=="yes","compiled true loads as yes");
        reopened.Set(reopenedIndex,"modifiers.keepAfterRetirement","no");
        var noCompiled=reopened.Compile();
        Check(noCompiled.Ready&&!ShellProfiles.Parse(noCompiled.RuntimeJson!).Single(p=>p.Id=="aphe").KeepAfterRetirement,"draft no compiles to false");

        // 0.22.2 -> retiredAfter migration: exact defaults gain the field, edited entries do not.
        var temp=Path.Combine(SprocketExpandedBallistics.Fix02228Tests.FixtureRoot(),"shell-era-list-"+Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temp);
        try
        {
            var path=Path.Combine(temp,"shells.json");
            File.WriteAllText(path,LegacyShellDefaults0222.Json);
            Check(ReleaseProfiles.Upgrade(path),"0.22.2 default catalogue migrates");
            var migrated=ShellProfiles.Parse(File.ReadAllText(path));
            Check(migrated.Count==21,"migration keeps 21 profiles");
            Check(migrated.Single(p=>p.Id=="aphe").RetiredAfter==new ShellNativeDate(1960,1,1),"APHE gains 1960 retirement");
            Check(migrated.Single(p=>p.Id=="apcbc").RetiredAfter==new ShellNativeDate(1960,1,1),"APCBC gains 1960 retirement");
            Check(migrated.Single(p=>p.Id=="apcr").RetiredAfter==new ShellNativeDate(1960,1,1),"APCR gains 1960 retirement");
            Check(migrated.Single(p=>p.Id=="apds").RetiredAfter==new ShellNativeDate(1970,1,1),"APDS gains 1970 retirement");
            Check(migrated.Single(p=>p.Id=="apds_late").RetiredAfter==new ShellNativeDate(1985,1,1),"APDS late gains 1985 retirement");
            Check(migrated.Single(p=>p.Id=="apfsds_short").RetiredAfter==new ShellNativeDate(1985,1,1),"APFSDS gen 1 gains 1985 retirement");
            Check(migrated.Single(p=>p.Id=="heat").RetiredAfter==null&&migrated.Single(p=>p.Id=="atgm_tow_bgm71a").RetiredAfter==null,"HEAT and ATGM have no retirement");
            var afterFirst=File.ReadAllText(path);
            Check(!ReleaseProfiles.Upgrade(path)&&File.ReadAllText(path)==afterFirst,"retirement migration is idempotent");

            var customPath=Path.Combine(temp,"custom.json");
            var custom=JsonNode.Parse(LegacyShellDefaults0222.Json)!;
            var customAphe=custom["profiles"]!.AsArray().Single(p=>p!["id"]!.GetValue<string>()=="aphe")!;
            customAphe["penetrationQuality"]=1.5;customAphe["keepAfterRetirement"]=true;
            File.WriteAllText(customPath,custom.ToJsonString());
            Check(ReleaseProfiles.Upgrade(customPath),"custom catalogue still gains other defaults");
            var customAfter=ShellProfiles.Parse(File.ReadAllText(customPath));
            Check(customAfter.Single(p=>p.Id=="aphe").RetiredAfter==null,"edited APHE does not gain retiredAfter");
            Check(customAfter.Single(p=>p.Id=="aphe").KeepAfterRetirement,"edited APHE keeps its archive flag");
            Check(Math.Abs(customAfter.Single(p=>p.Id=="aphe").Settings.PenetrationQuality-1.5)<1e-9,"edited APHE keeps its tuning");
            Check(customAfter.Single(p=>p.Id=="apcbc").RetiredAfter==new ShellNativeDate(1960,1,1),"unchanged APCBC still gains retirement");
        }
        finally{try{Directory.Delete(temp,true);}catch{}}

        Console.WriteLine("ERA VISIBLE LISTS (ordinary cannon, default order)");
        foreach(var year in new[]{1943,1950,1965,1975,1980,1990})
            Console.WriteLine($"{year}: "+string.Join(", ",Visible(year,false).Select(id=>defaults.Single(p=>p.Id==id).Label)));
        Console.WriteLine("1975 launcher: "+string.Join(", ",Visible(1975,true).Select(id=>defaults.Single(p=>p.Id==id).Label)));
        Console.WriteLine($"SHELL ERA LIST PASS: {checks} checks.");
        return checks;
    }

    private static List<TechRecord> ReadTech()
    {
        var roots=new[]{Path.Combine(AppContext.BaseDirectory,"NativeData","Technology"),Path.Combine(AppContext.BaseDirectory,"NativeData")};
        var directory=roots.FirstOrDefault(Directory.Exists)??throw new DirectoryNotFoundException("NativeData/Technology is not copied for the tests.");
        var records=new List<TechRecord>();
        foreach(var file in Directory.GetFiles(directory,"*.json"))
        {
            using var document=JsonDocument.Parse(File.ReadAllText(file),new JsonDocumentOptions{AllowTrailingCommas=true});
            var root=document.RootElement;
            if(root.ValueKind!=JsonValueKind.Object||!root.TryGetProperty("type",out var type)||
                !root.TryGetProperty("date",out var dateNode))continue;
            if(!DateTime.TryParseExact(dateNode.GetString(),"yyyy.MM.dd",CultureInfo.InvariantCulture,DateTimeStyles.None,out var date))continue;
            var properties=root.TryGetProperty("properties",out var node)&&node.ValueKind==JsonValueKind.Object?node:default;
            var enabled=properties.ValueKind==JsonValueKind.Object&&properties.TryGetProperty("enabled",out var enabledNode)&&enabledNode.ValueKind==JsonValueKind.True;
            double? factor=properties.ValueKind==JsonValueKind.Object&&properties.TryGetProperty("penetrationPerCalibre",out var factorNode)&&factorNode.TryGetDouble(out var value)?value:null;
            records.Add(new(type.GetString()??"",new ShellNativeDate(date.Year,date.Month,date.Day),enabled,factor));
        }
        Check(records.Count>=20,"dated shell technology catalogue copied for visibility test");
        return records;
    }
}
