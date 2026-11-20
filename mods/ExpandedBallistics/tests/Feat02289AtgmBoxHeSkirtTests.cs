using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml.Linq;
namespace SprocketExpandedBallistics;

/// <summary>
/// 0.22.89 (brief FEAT-0.22.89-ATGM-BOX-GUARD-HE-SKIRT; user 16-11 "ja bouw het vangnet exact hetzelfde", 15-11 "Nu installeren" with the skirt rule announced
/// for a later version). (A) The ATGM ammo box: the safety net of Carousel 0.2.28 on every one of its 17 hooks, the automatic feed on by default under
/// [ATGM ammo box] AutomaticFeed, the old switch no longer read (one log line), the nesting with Carousel's loader-role check. The hook bodies are IL2CPP code
/// and are checked on the source (one expected shape per hook); the rules (reach, matching, loader waiver, the old-switch reader) are the production code.
/// (B) HE: the spaced-armour (skirt) rule. The look-ahead, the decision, the plan of the blast filter, the settings and the texts are the production code
/// (ShellBalance, HitPanelModel and the report code, linked); the intersection lists are the brief's cases.
/// </summary>
internal static class Feat02289AtgmBoxHeSkirtTests
{
    /// <summary>The production files 0.22.89 may change on top of 0.22.88.</summary>
    internal static bool IsPlanned(string rel)=>rel is "RuntimeAtgmAmmoBox.cs" or "ShellFeature.cs" or "AtgmAmmoBoxRules.cs" or "ShellBalance.cs" or "RuntimeArmourResponses.cs" or "RuntimeDamageFeed.cs" or "HitPanelModel.cs"
        or "ATGM.md" or "docs/SHELL-USAGE.md" or "Plugin.cs" or "SprocketExpandedBallistics.csproj" or "RELEASE-NOTES.md";
    private static string F(double v,string f="0.###")=>double.IsPositiveInfinity(v)?"none":v.ToString(f,CultureInfo.InvariantCulture);
    private static string Tsv(string s)=>s.Replace('\t',' ').Replace('\n',' ').Replace('\r',' ');

    /// <summary>One hook of the box: its method, the native target, before (0.22.88), after (0.22.89), the Carousel 0.2.28 counterpart, and the shape its body must have.</summary>
    private sealed record Hook(string Method,string Target,string Before,string After,string Carousel,string Shape,string[] Contains);
    private static readonly Hook[] Hooks=
    {
        new("SelectSource","LoadTask.UpdateRequest prefix (Priority.Last)","cannon TryCast and launcher test outside the guard; Choose in Guard","count test, then the whole body (TryCast included) in Guard \"Select source\"",
            "SelectSupply prefix (Normal, HarmonyAfter EB ammoloadout): try/catch, HookGuard.Warn, native fallback",
            @"^\{\s*if\(loaders\.Count==0\)return;\s*Guard\(""Select source"",\(\)=>\{.*Choose\(__instance,cannon\);\s*\}\);\s*\}$",new[]{"!loaders.ContainsKey(cannon.Pointer))return;"}),
        new("Speed","LoadTask.Update prefix","try/catch (one warning); no early exit","count test inside the same try/catch","Speed prefix: try/catch, native speeds on error",
            @"^\{\s*try\s*\{\s*if\(loaders\.Count==0\)return;.*\}\s*catch\(Exception ex\)\{Guard\(""Native cycle speed"",\(\)=>throw ex\);\}\s*\}$",new[]{"!loaders.TryGetValue(cannon.Pointer,out var binding))return;"}),
        new("Timings","LoadTask.UpdateRequest postfix","no guard","count test, body in Guard \"Loading phases\"","RequestUpdated postfix: try/catch on the index test, Timings in Guard",
            @"^\{if\(loaders\.Count==0\)return;Guard\(""Loading phases"",\(\)=>\{.*\}\);\}$",new[]{"loaders.TryGetValue(c.Pointer,out var b)&&Active(b)"}),
        new("Attach","Cannon.EnableBehaviour postfix","Guard; a failed contributor write was left in place","Guard; a failed write is undone (array back, no binding, contributor idle), then the one warning","AttachNativeLoader postfix: Guard; failed write restored, contributor switch off",
            @"^\{\s*Guard\(""Native automatic contributor"",\(\)=>\s*\{.*\}\);\s*\}$",new[]{"if(!Ready||!RuntimeAtgmLauncher.IsLauncher(__instance)||!HasDesign(__instance))return;","if(written){try{weapon.LoadTaskContributors=old;}","loaders.Remove(__instance.Pointer);Idle(contributor);","throw;"}),
        new("BeforeController","WeaponLoadControllerAssemblyStage.Execute prefix","Guard","unchanged (Guard)","AttachBeforeControllerAssembly prefix: Guard",
            @"^\{if\(Ready&&boxes\.Count>0\)Guard\(""Controller assembly"",\(\)=>\{.*\}\);\}$",Array.Empty<string>()),
        new("Refresh","VehicleWeaponLoadController.Update prefix","per binding Guard; a failing binding kept its contributor active; loop set-up unguarded","whole body in try/catch; a failing binding idles its contributor (native loading)","RefreshNativeLoaders prefix: try/catch + Guard",
            @"^\{\s*try\s*\{.*\}\s*catch\(Exception ex\)\{Warn\(""VehicleWeaponLoadController\.Update"",ex\);\}\s*\}$",new[]{"if(!Guarded(\"Contributor eligibility\",()=>","}))Idle(binding.Contributor);"}),
        new("LoaderFraction","WeaponBehaviour.LoaderEnableFraction getter prefix","try/catch, native on error","count test inside the same try/catch (true = native)","AutomaticLoaderEnabled prefix: try/catch, native on error",
            @"^\{\s*try\s*\{\s*if\(loaders\.Count==0\)return true;.*\}\s*catch\(Exception ex\)\{Guard\(""Loader availability"",\(\)=>throw ex\);return true;\}\s*\}$",new[]{"if(c==null||!loaders.TryGetValue(c.Pointer,out var binding))return true;"}),
        new("Validate","AllOperablesValidCriterion.Validate prefix","try/catch; Normal priority like Carousel (the two removals did not nest)","try/catch; Priority.Last (after Carousel's prefix); remembers its own list","ValidateAutomaticLoaders prefix (Normal): try/catch",
            @"^\{\s*__state=null;if\(!Ready\|\|boxes\.Count==0\)return;\s*try\s*\{.*\}\s*catch\(Exception ex\)\{Guard\(""Loader validation"",\(\)=>throw ex\);\}\s*\}$",new[]{"AtgmAmmoBoxRules.WaivesLoader(","swap.Kept=list.Pointer;"}),
        new("Restore","AllOperablesValidCriterion.Validate finalizer","no guard; restored unconditionally","try/catch; Priority.First (before Carousel's finalizer); restores only while its own list is in place","RestoreOperables finalizer (Normal): try/catch",
            @"^\{try\{if\(__state is \{Original:\{\} original\} swap&&__instance\.operables\?\.Pointer==swap\.Kept\)__instance\.operables=original;\}catch\(Exception ex\)\{Warn\(""AllOperablesValidCriterion\.Validate restore"",ex\);\}\}$",Array.Empty<string>()),
        new("BeforeRack","AmmoRack.Build prefix","own-part test outside the guard; a failing ammunition sync skipped the mass reset","own-part test and sync in Guard \"Box design\"; the mass reset in its own guard","Bustle PrepareRack prefix: try/catch on the part test, then Guard",
            @"^\{\s*bool own=false;Guard\(""Box design"",\(\)=>\{own=Own\(__instance\);if\(own\)Sync\(State\(__instance\)\);\}\);if\(!own\)return;Guard\(""Box mass reset"",\(\)=>\s*\{.*\}\);\s*\}$",Array.Empty<string>()),
        new("AfterRack","AmmoRack.Build postfix","own-part test outside the guard","own-part test inside Guard \"Box mass/model\"","Bustle RackMass postfix: try/catch on the part test, then Guard",
            @"^\{\s*Guard\(""Box mass/model"",\(\)=>\s*\{\s*if\(!Own\(__instance\)\)return;.*\}\);\s*\}$",Array.Empty<string>()),
        new("CannonChanged","Cannon.Build postfix","launcher test and box lookup outside the guard","nothing without a box; launcher test and lookup in Guard","CannonBuilt postfix: Guard",
            @"^\{if\(boxes\.Count==0\)return;Guard\(""Sync box ammunition"",\(\)=>\{if\(RuntimeAtgmLauncher\.IsLauncher\(__instance\)\).*\}\);\}$",Array.Empty<string>()),
        new("Draw","AmmoRackEditor.OnGUI postfix","component lookup and own-part test outside the guard","lookup and test in try/catch (warning, return), then Guard \"Box inspector\"","Bustle inspector OnGUI postfix: try/catch on the lookup, then Guard",
            @"^\{\s*AmmoRack rack;try\{var component=__instance\.Component;if\(!Own\(component\)\)return;rack=component!;\}catch\(Exception ex\)\{Warn\(""Box inspector"",ex\);return;\}\s*Guard\(""Box inspector"",\(\)=>\s*\{.*\}\);\s*\}$",new[]{"ui.ToggleField(\"Automatic feed\",state.Enabled,"}),
        new("Saved","VehicleObjectSerialization.ToBlueprint postfix","no guard: an exception left the save","the whole body (TryCast included) in Guard \"Save box\"","Saved postfix: Guard \"Save basket\" with the TryCast inside",
            @"^\{Guard\(""Save box"",\(\)=>\{var parts=__0\.Components;.*\}\);\}$",new[]{"__result.State.AddValue(EnabledKey,b.Enabled);"}),
        new("Loaded","VehicleComponent.LoadData postfix","no guard (TryCast outside): an exception left the load","the whole body (TryCast included) in try/catch with one warning","Loaded postfix: try/catch per part, TryCast inside",
            @"^\{\s*try\s*\{\s*var r=__instance\.TryCast<AmmoRack>\(\);.*\}\s*catch\(Exception ex\)\{Warn\(""VehicleComponent\.LoadData box"",ex\);\}\s*\}$",Array.Empty<string>()),
        new("Disabled","Cannon.DisableBehaviour prefix","no guard","try/catch","CannonDisabled prefix: try/catch per part",
            @"^\{try\{.*\}catch\(Exception ex\)\{Warn\(""Cannon\.DisableBehaviour"",ex\);\}\}$",Array.Empty<string>()),
        new("Released","VehicleComponent.ReleaseInternal prefix","no guard","try/catch","Bustle Release prefix: try/catch per part",
            @"^\{try\{.*\}catch\(Exception ex\)\{Warn\(""VehicleComponent\.ReleaseInternal"",ex\);\}\}$",Array.Empty<string>()),
    };
    /// <summary>The Harmony patch methods of a source file: method name, its attribute lines, and its body from the opening to the matching closing brace.</summary>
    internal static List<(string Method,string Attributes,string Body)> Patches(string text)
    {
        var lines=text.Replace("\r\n","\n").Split('\n');var result=new List<(string,string,string)>();
        for(int i=0;i<lines.Length;i++)
        {
            if(!lines[i].TrimStart().StartsWith("[Harmony",StringComparison.Ordinal)||!lines[i].Contains("HarmonyPatch(",StringComparison.Ordinal))continue;
            var attributes=new StringBuilder(Regex.Replace(lines[i].Trim(),@"\s*//.*$",""));int j=i+1;
            while(j<lines.Length&&lines[j].TrimStart().StartsWith("[",StringComparison.Ordinal)){attributes.Append(' ').Append(Regex.Replace(lines[j].Trim(),@"\s*//.*$",""));j++;}
            var m=Regex.Match(lines[j],@"static\s+\w+\??\s+(\w+)\(");if(!m.Success)continue;
            var rest=string.Join("\n",lines.Skip(j));var start=rest.IndexOf('{');int depth=0,end=-1;
            for(int k=start;k<rest.Length;k++){if(rest[k]=='{')depth++;else if(rest[k]=='}'&&--depth==0){end=k;break;}}
            if(start<0||end<0)continue;
            result.Add((m.Groups[1].Value,attributes.ToString(),rest[start..(end+1)]));
            i=j;
        }
        return result;
    }
    private static string NoComments(string body)=>Regex.Replace(body,@"(^|\s)//[^\n]*",m=>m.Groups[1].Value,RegexOptions.Multiline);

    /// <summary>The blast filter's view of one vehicle: the value each pool is given (the game's per-pool call), by owner class.</summary>
    private sealed class Recorder:IHeBlastPools
    {
        internal readonly string[]?[] Owners;internal readonly float[] Passed;
        internal Recorder(string[]?[] owners){Owners=owners;Passed=new float[owners.Length];}
        public int Count=>Owners.Length;
        public IReadOnlyList<string>? Types(int index)=>Owners[index];
        public void Apply(int index,float value)=>Passed[index]=value;
    }
    private static readonly (string Name,string[]? Types)[] Vehicle=
    {
        ("crew (gunner)",new[]{"CrewSeat","VehicleComponent"}),("ammunition",new[]{"AmmoRack","VehicleComponent"}),("engine",new[]{"CombustionEngine","VehicleComponent"}),
        ("track",new[]{"TrackAssembly","VehicleComponent"}),("main gun",new[]{"Cannon","VehicleComponent"}),("radio (on neither list)",new[]{"Radio","VehicleComponent"}),
    };
    /// <summary>One intersection of the line of fire: distance along the path (m), Enter hit, armour of the same vehicle (a plate or the composite module).</summary>
    private readonly record struct Hit(double M,bool Enter,bool Armour,string What);

    internal static void Run(Action<bool,string> sink)
    {
        int checks=0,failures=0;void Check(bool ok,string message){checks++;if(!ok)failures++;sink(ok,"FEAT89: "+message);}
        string root=Fix02228Tests.SourceRoot(),output=Fix02228Tests.OutputRoot(),parent=Fix02228Tests.SessionRoot();Directory.CreateDirectory(output);
        string Read(string f)=>File.ReadAllText(Path.Combine(root,f)).Replace("\r\n","\n");
        var box=Read("RuntimeAtgmAmmoBox.cs");var feature=Read("ShellFeature.cs");

        // ================= A1. the 17 hooks of the box: every body in a guard (the shape of Carousel 0.2.28) =================
        var hookRows=new StringBuilder("hook\tnative method\tbefore (0.22.88)\tafter (0.22.89)\tCarousel 0.2.28\tbody checked\n");
        var patches=Patches(box);
        Check(patches.Count==17&&patches.Select(p=>p.Method).OrderBy(x=>x,StringComparer.Ordinal).SequenceEqual(Hooks.Select(h=>h.Method).OrderBy(x=>x,StringComparer.Ordinal)),$"the box has 17 patch methods, all in the table ({string.Join(",",patches.Select(p=>p.Method))})");
        foreach(var h in Hooks)
        {
            var p=patches.FirstOrDefault(x=>x.Method==h.Method);var body=NoComments(p.Body??"");
            var shape=p.Body!=null&&Regex.IsMatch(body,h.Shape,RegexOptions.Singleline);var contains=h.Contains.All(c=>body.Contains(c,StringComparison.Ordinal));
            Check(shape&&contains,$"{h.Method} ({h.Target}): {h.After}");
            hookRows.AppendLine($"{h.Method}\t{h.Target}\t{h.Before}\t{h.After}\t{h.Carousel}\t{(shape&&contains?"yes":"NO")}");
        }
        var validate=patches.FirstOrDefault(p=>p.Method=="Validate");var restore=patches.FirstOrDefault(p=>p.Method=="Restore");
        Check(validate.Attributes!=null&&validate.Attributes.Contains("[HarmonyPriority(Priority.Last)]",StringComparison.Ordinal)&&restore.Attributes!=null&&restore.Attributes.Contains("[HarmonyPriority(Priority.First)]",StringComparison.Ordinal),
            "loader-role check: the box's prefix runs after Carousel's (Priority.Last), its finalizer before Carousel's (Priority.First): last in, first out");
        Check(Regex.IsMatch(box,@"private static void Warn\(string area,Exception ex\)\s*\{try\{if\(warnings\.Add\(area\)\)Plugin\.ModLog\.LogWarning\(")&&box.Contains("private static void Guard(string area,Action action)\n    {try{action();}catch(Exception ex){if(warnings.Add(area))",StringComparison.Ordinal),
            "one warning per area (the shared set of Guard and Warn)");
        // the only places that write a load task, a contributor array or a result are reached with a binding; bindings are made only for a dedicated launcher with a matching box
        Check(Regex.Matches(box,@"loaders\[__instance\.Pointer\]=").Count==1&&box.Contains("if(!Ready||!RuntimeAtgmLauncher.IsLauncher(__instance)||!HasDesign(__instance))return;",StringComparison.Ordinal),
            "a binding (the automatic feed of one launcher) is made in one place only, for a dedicated launcher with a box that passes Eligible (same vehicle, profile, calibre, propellant, within 2 m)");
        Check(box.Contains("AtgmAmmoBoxRules.Matches(rack.Vehicle.Pointer==cannon.Vehicle.Pointer,box.Profile,RuntimeShellSelection.CannonProfile(cannon)?.Id,",StringComparison.Ordinal)&&box.Contains("RuntimeAtgmLauncher.IsLauncher(cannon)&&AtgmLauncherModel.Allows(",StringComparison.Ordinal),
            "Eligible: dedicated launcher part, ATGM profile allowed, and the pure match rule");
        if(Directory.Exists(Path.Combine(parent,"src-0.22.88")))
        {
            var oldPatches=Patches(File.ReadAllText(Path.Combine(parent,"src-0.22.88","RuntimeAtgmAmmoBox.cs")));
            static string Attr(string a)=>string.Join(" ",a.Split(' ').Where(x=>!x.StartsWith("[HarmonyPriority(Priority.Last)]",StringComparison.Ordinal)&&!x.StartsWith("[HarmonyPriority(Priority.First)]",StringComparison.Ordinal)));
            Check(oldPatches.Count==17&&oldPatches.Select(p=>p.Method+"|"+p.Attributes).SequenceEqual(patches.Select(p=>p.Method+"|"+Attr(p.Attributes))),"the same 17 patch targets as 0.22.88; the only attribute change is the two priorities of the loader-role check");
        }

        // ================= A2. pure rules: the box only feeds a dedicated launcher with a matching box within 2 m; nothing else is waived =================
        var rules=new StringBuilder("case\tsame vehicle\tbox profile\tlauncher profile\tbox calibre/propellant\tlauncher calibre/propellant\tdistance m\tfeeds (Matches)\n");
        foreach(var (name,same,bp,lp,bc,lc,bpr,lpr,d,expect) in new (string,bool,string,string?,int,int,int,int,double,bool)[]{
            ("TOW box 1.0 m from its launcher",true,"atgm_saclos_direct_v1","atgm_saclos_direct_v1",120,120,440,440,1.0,true),
            ("at exactly 2.0 m",true,"atgm_saclos_direct_v1","atgm_saclos_direct_v1",120,120,440,440,2.0,true),
            ("2.001 m",true,"atgm_saclos_direct_v1","atgm_saclos_direct_v1",120,120,440,440,2.001,false),
            ("another vehicle",false,"atgm_saclos_direct_v1","atgm_saclos_direct_v1",120,120,440,440,0.5,false),
            ("other profile",true,"atgm_saclos_direct_v1","atgm_mclos_v1",120,120,440,440,0.5,false),
            ("a gun without an ATGM profile",true,"atgm_saclos_direct_v1",null,120,120,440,440,0.5,false),
            ("calibre differs",true,"atgm_saclos_direct_v1","atgm_saclos_direct_v1",120,135,440,440,0.5,false),
            ("propellant differs",true,"atgm_saclos_direct_v1","atgm_saclos_direct_v1",120,120,440,500,0.5,false),
            ("unknown distance",true,"atgm_saclos_direct_v1","atgm_saclos_direct_v1",120,120,440,440,double.NaN,false)})
        {
            var got=AtgmAmmoBoxRules.Matches(same,bp,lp,bc,lc,bpr,lpr,d);
            rules.AppendLine($"{name}\t{same}\t{bp}\t{lp??"(none)"}\t{bc}/{bpr}\t{lc}/{lpr}\t{F(d)}\t{got}");
            Check(got==expect,$"Matches: {name} -> {(expect?"feeds":"no feed")}");
        }
        Check(AtgmAmmoBoxRules.WaivesLoader(true,true,334,334,true)&&!AtgmAmmoBoxRules.WaivesLoader(true,false,88,88,true)&&!AtgmAmmoBoxRules.WaivesLoader(true,true,334,449,true)&&!AtgmAmmoBoxRules.WaivesLoader(false,true,334,334,true)&&!AtgmAmmoBoxRules.WaivesLoader(true,true,334,334,false),
            "loader role: waived only for the launcher's own breech with a compatible box; an ordinary gun (the Bradley's 25 mm), another launcher, another vehicle, an incompatible box: never");

        // ================= A3. default on; the old switch no longer read, one log line =================
        var config=new StringBuilder("case\tvalue of the old switch read from the file\tlog line\n");
        Check(AtgmAmmoBoxRules.Section=="ATGM ammo box"&&AtgmAmmoBoxRules.Key=="AutomaticFeed"&&AtgmAmmoBoxRules.DefaultOn,"[ATGM ammo box] AutomaticFeed, default true");
        Check(feature.Contains("        var automaticBox=Config.Bind(AtgmAmmoBoxRules.Section,AtgmAmmoBoxRules.Key,AtgmAmmoBoxRules.DefaultOn,",StringComparison.Ordinal)&&feature.Contains("RuntimeAtgmAmmoBox.NoteLegacyKey(Config.ConfigFilePath,Log); // 0.22.89 replaced\n        try{if(automaticBox.Value){boxHarmony.PatchAll(typeof(RuntimeAtgmAmmoBox));RuntimeAtgmAmmoBox.Ready=true;",StringComparison.Ordinal),
            "ShellFeature: one line changed (the bind of the new key and the note); the patch line after it is 0.22.88's");
        var binds=Directory.EnumerateFiles(root,"*.cs",SearchOption.AllDirectories).Where(f=>!f.Replace('\\','/').Contains("/tests/",StringComparison.Ordinal)&&!f.Replace('\\','/').Contains("/obj/",StringComparison.Ordinal)).Sum(f=>Regex.Matches(File.ReadAllText(f),@"Bind\([^;]*""AutomaticAmmoBox""").Count);
        Check(binds==0,"the old key AutomaticAmmoBox is bound nowhere (no longer read)");
        Check(box.Contains("internal bool Enabled=true,Syncing;",StringComparison.Ordinal),"the per-box switch \"Automatic feed\" stays, default on");
        var userCfg="## Settings file was created by plugin Sprocket Expanded Ballistics v0.22.86\r\n\r\n[ATGM Launcher Tests]\r\n\r\n## Enable automatic loading from the dedicated finite ATGM ammunition box.\r\n# Setting type: Boolean\r\n# Default value: false\r\nAutomaticAmmoBox = false\r\n\r\n## One finite native initial chamber round\r\nInitialReadyMissile = true\r\n";
        foreach(var (name,text,expect) in new (string,string?,string?)[]{
            ("the user's file as BepInEx writes it (false, CRLF)",userCfg,"false"),
            ("true, no spaces","[ATGM Launcher Tests]\nAutomaticAmmoBox=true\n","true"),
            ("only the new key","[ATGM ammo box]\nAutomaticFeed = true\n",null),
            ("the key in another section","[ATGM ammo box]\nAutomaticAmmoBox = false\n",null),
            ("commented out","[ATGM Launcher Tests]\n# AutomaticAmmoBox = false\n",null),
            ("no file text",null,null)})
        {
            var got=AtgmAmmoBoxRules.LegacySwitch(text);var line=got==null?"":AtgmAmmoBoxRules.LegacyNote(got);
            config.AppendLine($"{name}\t{got??"(absent)"}\t{line}");
            Check(got==expect,$"old switch: {name} -> {expect??"absent"}");
        }
        Check(AtgmAmmoBoxRules.LegacyNote("false")=="[ATGM ammo box] The old setting [ATGM Launcher Tests] AutomaticAmmoBox = false is no longer read; the automatic feed follows [ATGM ammo box] AutomaticFeed (default true). The old line may be removed from the file.","the one log line names the old line and the new key");
        Check(Regex.IsMatch(box,@"internal static void NoteLegacyKey\(string\? configPath,BepInEx\.Logging\.ManualLogSource log\)\s*\{try\{.*\}catch\{/\* a note only, never blocks loading \*/\}\}"),"the note reads the file only, never throws (startup is never blocked)");

        // ================= A4. Carousel: the methods both mods patch, read from Carousel's source when it is next to this tree =================
        static string Effect(string method)=>method switch
        {
            "SelectSource" or "Speed" or "Timings"=>"returns after the count test, or without a binding for this cannon: no write",
            "LoaderFraction"=>"returns true (the game's own value) without a binding for this cannon",
            "Attach"=>"binds only a dedicated launcher with a matching box within 2 m; nothing written otherwise",
            "BeforeController"=>"runs only while a box exists; then as Attach",
            "Refresh"=>"iterates the bound launchers only; none bound: returns",
            "Validate"=>"waives only a launcher's own loader with a compatible box; otherwise the list is not replaced",
            "Restore"=>"restores only the list it set itself",
            "CannonChanged"=>"returns at once without a box; otherwise syncs only the boxes of the same vehicle for a launcher",
            "BeforeRack" or "AfterRack" or "Draw" or "Saved" or "Loaded"=>"acts only on its own box part (GUID "+AtgmAmmoBoxRules.Guid+")",
            _=>"removes only its own dictionary entries"
        };
        var carouselRows=new StringBuilder("native method\tCarousel 0.2.28 (attributes as in its source)\tEB box 0.22.89\twithout a box binding the EB hook\n");
        var carouselDir=new[]{Path.Combine(root,"..","carousel"),Path.Combine(parent,"..","..","2026-11-13","herpak","src","carousel")}.Select(Path.GetFullPath).FirstOrDefault(Directory.Exists);
        if(carouselDir!=null)
        {
            var carousel=Directory.EnumerateFiles(carouselDir,"*.cs").SelectMany(f=>Patches(File.ReadAllText(f)).Select(p=>(File:Path.GetFileName(f),p.Method,p.Attributes))).ToList();
            string Target(string a){var m=Regex.Match(a,@"HarmonyPatch\(typeof\(([\w\.:]+)\)\s*,\s*nameof\(([\w\.:]+)\)");var kind=Regex.Match(a,@"Harmony(Prefix|Postfix|Finalizer)");return m.Success?m.Groups[1].Value.Split('.').Last()+"."+m.Groups[2].Value.Split('.').Last()+" "+kind.Groups[1].Value.ToLowerInvariant():"";}
            var shared=0;
            foreach(var p in patches)
            {
                var t=Target(p.Attributes);var c=carousel.Where(x=>Target(x.Attributes)==t).ToList();if(c.Count==0)continue;shared++;
                var eb=Regex.Match(p.Attributes,@"Priority\.(\w+)");
                carouselRows.AppendLine($"{t}\t{string.Join(" | ",c.Select(x=>x.File+":"+x.Method+(Regex.Match(x.Attributes,@"Priority\.(\w+)") is {Success:true} pr?" "+pr.Value:" Normal")+(x.Attributes.Contains("HarmonyAfter",StringComparison.Ordinal)?" HarmonyAfter("+Regex.Match(x.Attributes,@"HarmonyAfter\(""([^""]+)""\)").Groups[1].Value+")":"")))}\t{p.Method}{(eb.Success?" "+eb.Value:" Normal")}\t{Effect(p.Method)}");
            }
            Check(shared>=10,$"Carousel's source read ({Path.GetFileName(carouselDir)}): {shared} methods patched by both mods are listed");
            var select=carousel.FirstOrDefault(x=>x.Method=="SelectSupply");
            Check(select.Attributes!=null&&select.Attributes.Contains("HarmonyAfter(",StringComparison.Ordinal)&&select.Attributes.Contains("ammoloadout",StringComparison.Ordinal)&&patches.First(p=>p.Method=="SelectSource").Attributes.Contains("HarmonyPriority(Priority.Last)",StringComparison.Ordinal),
                "UpdateRequest: Carousel's supply prefix runs after EB's ammo loadout (HarmonyAfter ...ammoloadout), the box's prefix last (Priority.Last); without a binding the box's prefix returns after the count test");
        }
        else carouselRows.AppendLine("(Carousel source not found next to this tree; nothing compared)");

        // ================= B1. HE spaced-armour rule: settings =================
        var on=HeSettings.Default;
        Check(on.SpacedArmourRule&&on.SpacedArmourGapMm==750,"skirt rule on by default, 750 mm");
        var heConfig=new StringBuilder("case\tspacedArmourRule\tspacedArmourGapMm\tlog lines\tlog\n");
        {
            var baseJson=SpallBalance.ToJson(new SpallSettings{SpacedKinetic=SpacedKineticSettings.Default,FragmentRicochet=FragmentRicochetSettings.Default});
            string With(string he)=>baseJson.TrimEnd().TrimEnd('}')+",\n  \"he\": "+he+"\n}";
            (HeSettings S,List<string> Log) R(string json){var log=new List<string>();var s=HeRules.Read(json,log.Add,out _);return (s,log);}
            foreach(var (name,json,rule,gap,lines,text) in new (string,string,bool,double,int,string)[]{
                ("key missing",baseJson,true,750,0,""),
                ("other keys only",With("{\"blastFilter\": true}"),true,750,0,""),
                ("spacedArmourGapMm 500",With("{\"spacedArmourGapMm\": 500}"),true,500,0,""),
                ("spacedArmourGapMm -5",With("{\"spacedArmourGapMm\": -5}"),true,0,1,"spacedArmourGapMm -5 is below 0, using 0"),
                ("spacedArmourGapMm 5000",With("{\"spacedArmourGapMm\": 5000}"),true,3000,1,"spacedArmourGapMm 5000 is above 3000, using 3000"),
                ("spacedArmourGapMm text",With("{\"spacedArmourGapMm\": \"x\"}"),true,750,1,"spacedArmourGapMm is not a number, using 750"),
                ("spacedArmourRule false",With("{\"spacedArmourRule\": false}"),false,750,0,""),
                ("spacedArmourRule 1",With("{\"spacedArmourRule\": 1}"),true,750,1,"spacedArmourRule is not true or false, using true"),
                ("two problems",With("{\"minAngleDegrees\": 3, \"spacedArmourGapMm\": 9000}"),true,3000,1,"minAngleDegrees 3 is below 10, using 10; spacedArmourGapMm 9000 is above 3000, using 3000")})
            {
                var r=R(json);heConfig.AppendLine($"{name}\t{r.S.SpacedArmourRule}\t{F(r.S.SpacedArmourGapMm)}\t{r.Log.Count}\t{string.Join(" | ",r.Log)}");
                Check(r.S.SpacedArmourRule==rule&&r.S.SpacedArmourGapMm==gap&&r.Log.Count==lines&&(lines==0||r.Log[0].Contains(text,StringComparison.Ordinal)),$"he.{name}: rule {rule}, gap {gap} mm, {lines} log line(s)");
            }
            Check(R(With("{\"spacedArmourGapMm\": 500}")).S==on with{SpacedArmourGapMm=500},"the other HE settings keep their values when only the gap is given");
            Check(HeRules.Describe(on).EndsWith("; spaced-armour rule on (armour within 750 mm behind a perforated plate: detonation in the gap, interior shock only)",StringComparison.Ordinal)&&HeRules.Describe(on).Contains("blast filter on",StringComparison.Ordinal),"the diagnostic description names the rule");
        }

        // ================= B2. the brief's cases on the sorted intersection list =================
        var profile=ShellProfiles.Parse(ReleaseProfiles.Defaults()).First(p=>p.Id=="he");
        const double calibre=152;var power=(float)ShellBalance.BlastPower(profile,calibre);var value=-Math.Min(power,power/(0f*0f));var shock=HeRules.Shock(on,calibre);
        var cases=new StringBuilder("case\tintersections (m along the path)\texit index\tnext armour within the gap mm\tnext armour at all mm\tarmour tests\trule\tdecision\tinterior factor\texterior factor\treport line\n");
        string[]?[] owners=Vehicle.Select(v=>v.Types).ToArray();
        (double Next,double Any,int Tests,bool Spaced,HeBlastPlan Plan,Recorder Rec) Evaluate(Hit[] hits,int exit,HeSettings s)
        {
            int tests=0;
            // RuntimeArmourResponses.HeBlock: the look-ahead runs only with the rule on
            var next=s.SpacedArmourRule?HeSpaced.NextArmourMm(hits.Length,k=>hits[k].M,k=>hits[k].Enter,k=>{tests++;return hits[k].Armour;},exit,s.SpacedArmourGapMm):double.PositiveInfinity;
            var any=HeSpaced.NextArmourMm(hits.Length,k=>hits[k].M,k=>hits[k].Enter,k=>hits[k].Armour,exit,1e9);
            var spaced=HeSpaced.Applies(s,next);
            var rec=new Recorder(owners);var plan=HeRules.Run(rec,value,!spaced,shock,s); // a spaced perforation does not mark the vehicle perforated
            return (next,any,tests,spaced,plan,rec);
        }
        static string Path_(Hit[] h)=>string.Join(" ",h.Select(x=>$"{x.What}{(x.Enter?">":"<")}{x.M.ToString("0.###",CultureInfo.InvariantCulture)}"));
        var skirt=new Hit[]{new(0,true,true,"skirt"),new(.008,false,true,"skirt"),new(.2,true,false,"module"),new(.25,false,false,"module"),new(.508,true,true,"hull"),new(.588,false,true,"hull"),new(3.1,true,true,"far side"),new(3.18,false,true,"far side")};
        var hull40=new Hit[]{new(0,true,true,"hull side"),new(.04,false,true,"hull side"),new(.6,true,false,"crew"),new(.9,false,false,"crew"),new(2.04,true,true,"far side"),new(2.08,false,true,"far side")};
        var bulkhead=new Hit[]{new(0,true,true,"hull side"),new(.04,false,true,"hull side"),new(1.24,true,true,"bulkhead"),new(1.25,false,true,"bulkhead"),new(3,true,true,"far side"),new(3.04,false,true,"far side")};
        var era=new Hit[]{new(0,true,true,"ERA box"),new(.03,false,true,"ERA box"),new(.13,true,true,"hull"),new(.21,false,true,"hull")};
        var expectations=new (string Name,Hit[] Hits,double Normal,double Next,bool Spaced)[]
        {
            ("skirt 8 mm, 500 mm gap, 80 mm hull",skirt,8,500,true),
            ("hull side 40 mm, next armour at 2 m",hull40,40,double.PositiveInfinity,false),
            ("hull side 40 mm, bulkhead at 1.2 m",bulkhead,40,double.PositiveInfinity,false),
            ("ERA box 30 mm, 100 mm space",era,30,100,true),
        };
        var reportLines=new StringBuilder();
        foreach(var c in expectations)
        {
            var r=Evaluate(c.Hits,1,on);
            var interior=r.Plan.Factors.Where((f,i)=>r.Plan.Classes[i]==HePoolClass.Interior).Distinct().ToArray();var exterior=r.Plan.Factors.Where((f,i)=>r.Plan.Classes[i]==HePoolClass.Exterior).Distinct().ToArray();
            var line=r.Spaced?HeSpaced.Text(c.Normal,r.Next,shock):"(perforation: "+HeRules.PerforationText(c.Normal,c.Normal,0,32)+")";
            cases.AppendLine($"{c.Name}\t{Path_(c.Hits)}\t1\t{F(r.Next,"0.#")}\t{F(r.Any,"0.#")}\t{r.Tests}\ton ({on.SpacedArmourGapMm:0} mm)\t{(r.Spaced?"spaced armour: only the shock":"perforation: full blast")}\t{string.Join("/",interior.Select(x=>F(x,"0.####")))}\t{string.Join("/",exterior.Select(x=>F(x,"0.####")))}\t{line}");
            var nextOk=double.IsPositiveInfinity(c.Next)?double.IsPositiveInfinity(r.Next):Math.Abs(r.Next-c.Next)<1e-6;
            var factorsOk=r.Spaced?interior.Length==1&&Math.Abs(interior[0]-shock)<1e-12&&exterior.SequenceEqual(new[]{1d}):interior.SequenceEqual(new[]{1d})&&exterior.SequenceEqual(new[]{1d});
            Check(nextOk&&r.Spaced==c.Spaced&&factorsOk&&r.Plan.Outcome==HeBlastOutcome.Applied,$"{c.Name}: next armour within the gap {F(r.Next,"0.#")} mm -> {(c.Spaced?"only the shock ("+HeRules.Percent(shock)+" %) for crew and interior modules":"perforation, the full blast")}; exterior modules always the full blast");
            // the module hits in the gap are not armour and the armour test runs only for Enter hits inside the window
            var inWindow=c.Hits.Skip(2).Count(h=>h.Enter&&(h.M-c.Hits[1].M)*1000<=on.SpacedArmourGapMm);
            Check(r.Tests<=inWindow,$"{c.Name}: the armour test runs for {r.Tests} hit(s), only Enter hits within {on.SpacedArmourGapMm:0} mm ({inWindow})");
        }
        // the switch: off gives the 0.22.88 decision in every case (a perforation of armour always counts), the plan bit for bit
        foreach(var c in expectations)
        {
            var off=Evaluate(c.Hits,1,on with{SpacedArmourRule=false});var perforated=Evaluate(c.Hits,1,on with{SpacedArmourGapMm=-1});
            var bits88=new Recorder(owners);var plan88=HeRules.Run(bits88,value,true,shock,on with{SpacedArmourRule=false}); // 0.22.88: the vehicle is perforated
            Check(!off.Spaced&&off.Rec.Passed.Select(BitConverter.SingleToInt32Bits).SequenceEqual(bits88.Passed.Select(BitConverter.SingleToInt32Bits))&&off.Plan.Factors.SequenceEqual(plan88.Factors)&&off.Tests==0,
                $"{c.Name}, rule off: the 0.22.88 decision (perforation, full blast), every pool's value bit for bit, no look-ahead");
            cases.AppendLine($"{c.Name}\t{Path_(c.Hits)}\t1\t(not looked up)\t{F(off.Any,"0.#")}\t{off.Tests}\toff\tperforation: full blast (as 0.22.88)\t1\t1\t");
            Check(!perforated.Spaced,$"{c.Name}: a negative gap never makes a plate spaced");
        }
        // the gap is a setting
        Check(!Evaluate(skirt,1,on with{SpacedArmourGapMm=400}).Spaced&&Evaluate(bulkhead,1,on with{SpacedArmourGapMm=3000}).Spaced&&HeSpaced.Applies(on with{SpacedArmourGapMm=500},500),
            "the gap is the setting: skirt 500 mm with 400 -> perforation; bulkhead 1.2 m with 3000 -> spaced; exactly at the limit (500 with 500) -> spaced");
        // edge cases of the look-ahead
        {
            var touching=new Hit[]{new(0,true,true,"layer 1"),new(.01,false,true,"layer 1"),new(.01,true,true,"layer 2"),new(.05,false,true,"layer 2")};
            var behind=new Hit[]{new(0,true,true,"plate"),new(.02,false,true,"plate"),new(-.5,true,true,"earlier plate")};
            Check(Evaluate(touching,1,on).Next==0&&Evaluate(touching,1,on).Spaced,"a layer that touches the plate (0 mm): spaced, the round meets armour at once");
            Check(double.IsPositiveInfinity(Evaluate(behind,1,on).Next),"a hit before the exit along the path is never the next armour");
            Check(double.IsPositiveInfinity(HeSpaced.NextArmourMm(2,k=>k,k=>true,k=>true,5,750))&&double.IsPositiveInfinity(HeSpaced.NextArmourMm(3,k=>k==1?double.NaN:k,k=>true,k=>true,1,750)),"an exit index outside the list, or no finite exit distance: no armour behind (the perforation of 0.22.88)");
            Check(!HeSpaced.Applies(on,double.NaN)&&!HeSpaced.Applies(on,double.PositiveInfinity)&&HeSpaced.Applies(on,750)&&!HeSpaced.Applies(on,750.001),"the decision: at most the gap, never for an unknown distance");
        }

        // ================= B3. the report: the brief's sentence, the hit panel's short form, no game jargon =================
        {
            var text=HeSpaced.Text(8,500,shock);
            Check(text=="HE perforated 8 mm of spaced armour; detonation in the gap (next armour 500 mm behind); interior modules and crew received a shock of 9.2 %","report line: "+text);
            Check(HeSpaced.Is(text)&&!HeSpaced.Is(HeRules.PerforationText(15,15,0,32))&&!HeSpaced.Is(HeRules.OutsideText(shock))&&HeSpaced.Token(text)=="HE in the gap behind 8 mm spaced armour, shock 9.2 %","the hit panel's short form; the 0.22.86 lines are not taken for it");
            var skirtPlate=new CombatLayerPart(31,"Plate #31","rha","Rolled homogeneous armour",8,0,true);var hullPlate=new CombatLayerPart(12,"Plate #12","rha","Rolled homogeneous armour",80,0,true);
            const string s1="9:0:1:2",s2="9:1:5:6";
            var events=new List<CombatReportEvent>
            {
                new("layerStart","","Reached layer",46.2,null,s1,new CombatLayerInfo(s1,new[]{skirtPlate},false)),
                new("plateResidual","","Traversed Plate #31 [Material: Rolled homogeneous armour]; block exit includes native resistance and any listed extra reactions.",46.2,38.2,s1),
                new("materialNote","",text,HitId:s1),
                new("layerStart","","Reached layer",38.2,null,s2,new CombatLayerInfo(s2,new[]{hullPlate},false)),
                new("nativeStop","","Native stop in Plate #12 [Material: Rolled homogeneous armour]; no outgoing original core recorded.",38.2,null,s2),
                new("interaction","","Crew member Gunner [shock 9.2 %] wounded",RelatedEntity:21),
                new("interaction","",CombatReportLanguage.Module("trackAssembly")+" [exterior] destroyed",RelatedEntity:31),
            };
            var shot=new CombatReportShot(1,"Outgoing","HE",650,events.ToArray(),0,46.2,"Target tank",4,3,"Own tank");
            var block=HitBlockBuilder.Build(shot);var view=CombatReportView.Build(new[]{shot})[0];var nl=CombatHitSummaryModel.Build(shot);var forensic=CombatForensics.Build(shot);
            var history=new CombatReportHistory();history.Begin();history.Add(shot);var html=CombatReportHtml.Render(history);
            Check(block.Result==HitBlockBuilder.StoppedBlast&&block.Detail.Contains("HE in the gap behind 8 mm spaced armour, shock 9.2 %",StringComparison.Ordinal),$"hit panel: {block.Result} · {block.Detail}");
            Check(view.Layers.Any(l=>l.Reaction.Contains(text,StringComparison.Ordinal))&&(html.Contains(System.Net.WebUtility.HtmlEncode(text),StringComparison.Ordinal)||html.Contains(text,StringComparison.Ordinal)),"report row (Reaction of the skirt layer) and HTML carry the line");
            var exempt=new[]{"Plate #31","Plate #12","Rolled homogeneous armour","Target tank","Own tank","HE","Gunner"};
            var texts=new List<string>{block.Result,block.Detail,block.Values,block.DamageShort,block.DamageExpanded,nl.Header,nl.Detail,forensic.Outcome,forensic.Explanation,view.SummaryLine,view.Head,text};
            texts.AddRange(block.Breakdown);texts.AddRange(forensic.Findings);texts.AddRange(forensic.Advice);
            foreach(var l in view.Layers){texts.Add(l.Name);texts.Add(l.Material);texts.Add(l.Reaction);texts.Add(l.BeforeAfter);texts.Add(l.Note);}
            var doc=XDocument.Parse(html);foreach(var d in doc.Descendants("details").ToArray())d.Remove();foreach(var s in doc.Descendants("style").ToArray())s.Remove();
            texts.AddRange(doc.Root!.DescendantNodes().OfType<XText>().Select(t=>t.Value));
            var jargon=texts.Where(t=>!string.IsNullOrEmpty(t)).SelectMany(t=>CombatReportLanguage.JargonIn(t,exempt).Select(j=>j+" in: "+t)).ToArray();
            Check(jargon.Length==0&&CombatReportLanguage.JargonIn(text).Count==0&&CombatReportLanguage.JargonIn(HeSpaced.Token(text)).Count==0,"no game jargon in the visible texts"+(jargon.Length>0?": "+string.Join(" | ",jargon.Take(4)):""));
            reportLines.AppendLine("== 152 mm HE on an 8 mm skirt, 500 mm in front of 80 mm hull ==");
            reportLines.AppendLine("report line: "+text);
            reportLines.AppendLine("hit panel: "+block.Result+" · "+block.Values+" · "+block.Detail);
            reportLines.AppendLine("hit panel damage: "+block.DamageExpanded);
            reportLines.AppendLine("report: "+view.Head);
            foreach(var l in view.Layers)reportLines.AppendLine("report row: "+l.Name+" | "+l.Material+" | "+l.Thickness+" | "+l.Angle+" | "+l.BeforeAfter+" | "+l.Reaction);
            reportLines.AppendLine("Dutch: "+nl.Header+" | "+nl.Detail);
            reportLines.AppendLine("forensic: "+forensic.Outcome+": "+forensic.Explanation);
            // a shot without the line keeps the 0.22.86 words (the two inserted lines of HitPanelModel test only for the new line)
            var outside=new List<CombatReportEvent>{new("nativeStop","","Native stop in Plate #12; no outgoing original core recorded.",50,null,"1:0:1:2"),new("materialNote","",HeRules.OutsideText(shock),HitId:"1:0:1:2")};
            Check(HeReportText.Token(outside)=="HE outside the armour, shock 9.2 %"&&HeReportText.OutsideBlast(outside)&&HeReportText.Token(new[]{new CombatReportEvent("materialNote","",HeRules.PerforationText(15,15,0,32))})=="HE perforated 15 mm, 32 fragments","the 0.22.86 lines keep their hit-panel words");
        }

        // ================= B4. the hook bodies: only inside the HE part's existing paths; the rule off is the 0.22.88 path =================
        {
            var responses=Read("RuntimeArmourResponses.cs");var feed=Read("RuntimeDamageFeed.cs");var hit=Read("HitPanelModel.cs");
            Check(responses.Contains("        if(scope is {He:true}&&__result.Valid&&__result.MaterialIndex>=0)HeBlock(scope,__result,__0,__1,__5);\n",StringComparison.Ordinal)&&responses.Contains("            if(current is {He:true})HeEnd(current);\n",StringComparison.Ordinal),
                "the block postfix and the fragment finalizer reach the new code only for the HE part (one property test, as 0.22.86)");
            Check(responses.Contains("            if(scope.HeArmour&&RuntimeSpall.He.SpacedArmourRule)scope.HeNextMm=HeNextArmour(scope,hits,count,exit); // 0.22.89",StringComparison.Ordinal),"the look-ahead runs only for a block of armour and only with the rule on");
            var next=responses[responses.IndexOf("private static double HeNextArmour(",StringComparison.Ordinal)..];next=next[..next.IndexOf("    // 0.22.89 end",StringComparison.Ordinal)];
            Check(Regex.IsMatch(next,@"^private static double HeNextArmour\(Scope scope,Il2CppStructArray<StructureIntersection> hits,int count,int exit\)\s*\{\s*try\s*\{.*\}\s*catch\(Exception ex\)\{.*return double\.PositiveInfinity;\}\s*\}",RegexOptions.Singleline)&&!next.Contains("Raycast",StringComparison.Ordinal)&&!next.Contains("Penetrate(",StringComparison.Ordinal)&&!next.Contains("GetAllIntersections",StringComparison.Ordinal),
                "the look-ahead reads the existing intersection list only (no ray, no probe), and a failure counts as 0.22.88's perforation");
            var heEnd=responses[responses.IndexOf("private static void HeEnd(Scope scope)",StringComparison.Ordinal)..];heEnd=heEnd[..heEnd.IndexOf("    // 0.22.86 HE end",StringComparison.Ordinal)];
            Check(Regex.IsMatch(heEnd,@"if\(HeSpaced\.Applies\(RuntimeSpall\.He,scope\.HeNextMm\)\)\s*\{\s*HeBlast\.Spaced\(new HeSpacedPerforation\(applier\.Pointer,normal,rhaEq,angle,scope\.HeNextMm,scope\.HeHit\)\);.*\}\s*else\s*\{\s*// 0\.22\.89 end\s*HeBlast\.Perforated\(new HePerforation\(applier\.Pointer,normal,rhaEq,angle,scope\.HeHit\)\);",RegexOptions.Singleline),
                "HeEnd: spaced armour is recorded instead of the perforation; otherwise 0.22.88's two lines, unchanged");
            Check(feed.Contains("var spaced=scope.Spaced is {} s&&s.Register==pools.Register?s:null;RuntimeDamageFeed.HeNote(spaced!=null?HeSpaced.Text(spaced.NormalMm,spaced.NextArmourMm,plan.Shock):HeRules.OutsideText(plan.Shock),spaced!=null?spaced.HitId:scope.LastHitId);",StringComparison.Ordinal),
                "the report line: the spaced-armour line only for the vehicle whose plate was spaced armour; otherwise 0.22.86's line, unchanged");
            Check(hit.Contains("        foreach(var e in events)if(e?.Kind==\"materialNote\"&&HeSpaced.Is(e.Text))return true; // 0.22.89",StringComparison.Ordinal)&&hit.Contains("            if(HeSpaced.Is(e.Text))return HeSpaced.Token(e.Text); // 0.22.89\n",StringComparison.Ordinal),
                "hit panel: two inserted lines that match only the new line");
        }

        // ================= C. the files: every 0.22.89 change is marked and reverses to 0.22.88 byte for byte; no hook added; the protected files untouched =================
        var files=new StringBuilder("file\tchanged in 0.22.89\tUn89 equals 0.22.88\n");
        var old88=Path.Combine(parent,"src-0.22.88");
        Check(Feat02289Un.Replaced.Select(r=>r.New).Distinct().Count()==Feat02289Un.Replaced.Length&&Feat02289Un.Replaced.All(r=>Read(r.File).Split('\n').Count(l=>l.TrimEnd('\r').StartsWith(r.New+" // 0.22.89 replaced",StringComparison.Ordinal))==1),
            $"every one of the {Feat02289Un.Replaced.Length} declared replaced lines is in its file exactly once, marked");
        if(Directory.Exists(old88))
        {
            bool Skip(string rel)=>rel.Split('/').Any(p=>p is "bin" or "obj" or "tests" or ".git");
            Dictionary<string,string> Inventory(string dir)=>Directory.EnumerateFiles(dir,"*",SearchOption.AllDirectories).Select(f=>(Rel:Path.GetRelativePath(dir,f).Replace('\\','/'),Full:f)).Where(x=>!Skip(x.Rel)).ToDictionary(x=>x.Rel,x=>x.Full);
            var a=Inventory(old88);var b=Inventory(root);
            var changed=a.Keys.Union(b.Keys).Where(k=>!a.ContainsKey(k)||!b.ContainsKey(k)||!File.ReadAllBytes(a[k]).SequenceEqual(Feat02290Un.Un90Bytes(b[k]))).OrderBy(k=>k,StringComparer.Ordinal).ToArray(); // 0.22.90 adapted: the 0.22.89 bytes
            var reversed=changed.Where(k=>a.ContainsKey(k)&&b.ContainsKey(k)&&Feat02289Un.Un89Bytes(b[k]).SequenceEqual(File.ReadAllBytes(a[k]))).ToArray();
            foreach(var k in changed)files.AppendLine($"{k}\tyes\t{reversed.Contains(k)}");
            Check(changed.All(IsPlanned)&&changed.Length==12&&a.Keys.ToHashSet().SetEquals(b.Keys),$"only the planned production files differ from 0.22.88: {string.Join(", ",changed)} ({a.Count-changed.Length} files byte for byte)");
            Check(reversed.Length==changed.Length,"every changed file is 0.22.88 byte for byte without its marked change (Un89)");
            static int Hooks_(string s)=>s.Split('\n').Count(l=>l.Contains("[HarmonyPatch",StringComparison.Ordinal));
            var before=a.Where(p=>p.Key.EndsWith(".cs",StringComparison.Ordinal)).Sum(p=>Hooks_(File.ReadAllText(p.Value)));var after=b.Where(p=>p.Key.EndsWith(".cs",StringComparison.Ordinal)).Sum(p=>Hooks_(File.ReadAllText(p.Value)));
            Check(before==47&&after==47,$"[HarmonyPatch annotations {before} -> {after}");
            var hookLines=b.Where(p=>p.Key.EndsWith(".cs",StringComparison.Ordinal)).SelectMany(p=>File.ReadAllText(p.Value).Replace("\r\n","\n").Split('\n').Where(l=>Regex.IsMatch(l,@"^\s*\[Harmony(Prefix|Postfix|Finalizer|Transpiler)")).Select(l=>p.Key+"|"+l.Trim())).OrderBy(x=>x,StringComparer.Ordinal).ToArray();
            var hookLines88=a.Where(p=>p.Key.EndsWith(".cs",StringComparison.Ordinal)).SelectMany(p=>File.ReadAllText(p.Value).Replace("\r\n","\n").Split('\n').Where(l=>Regex.IsMatch(l,@"^\s*\[Harmony(Prefix|Postfix|Finalizer|Transpiler)")).Select(l=>p.Key+"|"+l.Trim())).OrderBy(x=>x,StringComparer.Ordinal).ToArray();
            Check(hookLines.SequenceEqual(hookLines88),$"no patch method added or retargeted ({hookLines.Length} patch attributes, as 0.22.88)");
            var protectedFiles=b.Keys.Where(k=>k.StartsWith("Material/",StringComparison.Ordinal)||Regex.IsMatch(k,@"^(CombatForensic|NativeMaterial)[^/]*\.cs$")||Regex.IsMatch(k,@"^[^/]*(Composer|Solid|Overlay)[^/]*\.cs$")||
                k is "RuntimeGroupedProtection.cs" or "MaterialIntervalLedger.cs" or "CompositePlate.cs" or "LayeredPlateReport.cs" or "RuntimeAtgmGuidanceSight.cs" or "AtgmIndependentSightPolicy.cs" or "RuntimeAtgm.cs" or "RuntimeSpall.cs" or "RuntimeShellSelection.cs").OrderBy(k=>k,StringComparer.Ordinal).ToArray();
            Check(protectedFiles.Length>100&&protectedFiles.All(k=>File.ReadAllBytes(a[k]).SequenceEqual(Feat02290Un.Un90Bytes(b[k]))),$"the protected files of the brief are 0.22.88 byte for byte ({protectedFiles.Length}: hit path, Material, CombatForensic*, LayeredPlateReport, composer, Solid, overlay, the ATGM sight files)");
            var featureOld=File.ReadAllText(Path.Combine(old88,"ShellFeature.cs"));var featureNew=File.ReadAllText(Path.Combine(root,"ShellFeature.cs"));
            Check(featureOld.Split('\n').Zip(featureNew.Split('\n')).Count(p=>p.First!=p.Second)==1&&featureOld.Split('\n').Length==featureNew.Split('\n').Length,"ShellFeature.cs: one line differs from 0.22.88 (the configuration line)");
        }
        else {static int H(string s)=>s.Split('\n').Count(l=>l.Contains("[HarmonyPatch",StringComparison.Ordinal));var hooks=Directory.EnumerateFiles(root,"*.cs",SearchOption.AllDirectories).Where(f=>!System.Text.RegularExpressions.Regex.IsMatch(f.Replace('\\','/'),"/(tests|bin|obj)/")).Sum(f=>H(File.ReadAllText(f)));Check(hooks==47,"[HarmonyPatch annotations in this tree: "+hooks+" (47)");} // herpak: the old source tree is not here; the hook count of this tree

        File.WriteAllText(Path.Combine(output,"ATGM-BOX-HOOKS.tsv"),hookRows.ToString());
        File.WriteAllText(Path.Combine(output,"ATGM-BOX-RULES.tsv"),rules.ToString());
        File.WriteAllText(Path.Combine(output,"ATGM-BOX-CONFIG.tsv"),config.ToString());
        File.WriteAllText(Path.Combine(output,"ATGM-BOX-CAROUSEL.tsv"),carouselRows.ToString());
        File.WriteAllText(Path.Combine(output,"HE-SKIRT-CONFIG.tsv"),heConfig.ToString());
        File.WriteAllText(Path.Combine(output,"HE-SKIRT-CASES.tsv"),cases.ToString());
        File.WriteAllText(Path.Combine(output,"HE-SKIRT-REPORT-LINES.txt"),reportLines.ToString());
        File.WriteAllText(Path.Combine(output,"UN89-FILES.tsv"),files.ToString());
        File.WriteAllText(Path.Combine(output,"FEAT89-RESULT.json"),JsonSerializer.Serialize(new{passed=failures==0,checks,failures,hooks=Hooks.Length,shock152=shock,defaults=new{automaticFeed=AtgmAmmoBoxRules.DefaultOn,spacedArmourRule=on.SpacedArmourRule,spacedArmourGapMm=on.SpacedArmourGapMm},
            replacedLines=Feat02289Un.Replaced.Length},new JsonSerializerOptions{WriteIndented=true}));
        Console.WriteLine($"FEAT89 PASS: {checks} checks, {failures} failures");
    }
}
