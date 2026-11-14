using System.Globalization;
using System.Text.RegularExpressions;
namespace SprocketExpandedBallistics;

/// <summary>
/// 0.22.19 part A (composer UI): no mojibake anywhere in the C# source, composer messages only when they concretely apply at the
/// selected angle and side, the layer row indicator tied to its message, the protection table column layout and the fit rule.
/// </summary>
internal static class Fix02219Tests
{
    // Typical double-encoding: UTF-8 bytes read as Windows-1252, so a lead character U+00C2..U+00F4 is followed by a cp1252 or
    // C1 character, and the replacement character U+FFFD (a lost character). Written with escapes so this file stays clean itself.
    private static readonly Regex Mojibake=new("[\u00C2-\u00F4][\u0080-\u00BF\u0152\u0153\u0160\u0161\u0178\u017D\u017E\u0192\u02C6\u02DC\u2013\u2014\u2018-\u201E\u2020-\u2022\u2026\u2030\u2039\u203A\u20AC\u2122]|\uFFFD",RegexOptions.Compiled);
    internal static bool LooksDoubleEncoded(string text)=>Mojibake.IsMatch(text);

    private static string? SourceRoot()
    {
        for(var dir=new DirectoryInfo(AppContext.BaseDirectory);dir!=null;dir=dir.Parent)
            if(File.Exists(Path.Combine(dir.FullName,"SprocketExpandedBallistics.csproj")))return dir.FullName;
        return null;
    }
    private static CassettePreset Preset(params CassetteLayer[] layers)=>new("fix02219","0.22.19 test","1980.01.01",1,4000,layers);

    internal static void Run(Action<bool,string> check)
    {
        int count=0;var assert=check;check=(ok,text)=>{assert(ok,"fix02219: "+text);count++;};
        Mojibake_(check);Messages(check);Table(check);Fit(check);Presets(check);
        Console.WriteLine($"0.22.19 checks PASS: {count} checks.");
    }

    // ---------------- point 1: mojibake ----------------
    private static void Mojibake_(Action<bool,string> check)
    {
        // The detector itself.
        check(LooksDoubleEncoded("0\u00C2\u00B0")&&LooksDoubleEncoded("30 \u00C2\u00B0")&&LooksDoubleEncoded("4\u00C3\u2014/s")&&LooksDoubleEncoded("1\u00E2\u20AC\u201C80")&&LooksDoubleEncoded("\u00E2\u0153\u2022")&&LooksDoubleEncoded("a \u00C2\u00B7 b")&&LooksDoubleEncoded("angle \uFFFD;"),"detector flags the typical double-encoded degree, cross, dash, close and dot, and a lost character");
        check(!LooksDoubleEncoded("0\u00B0 30\u00B0 60\u00B0 \u00D7 \u00B7 \u2013 \u2715 \u03B8 \u2265 \u2248 \u00E9 \u00C5ngstr\u00F6m"),"detector leaves proper degree, times, dot, dash, close, theta and accented letters alone");
        // Every C# source file of the mod (including the tests).
        var root=SourceRoot();
        check(root!=null,"source root found next to the test run");
        if(root==null)return;
        int files=0;var bad=new List<string>();
        foreach(var file in Directory.EnumerateFiles(root,"*.cs",SearchOption.AllDirectories))
        {
            var relative=Path.GetRelativePath(root,file).Replace('\\','/');
            if(relative.StartsWith("obj/",StringComparison.Ordinal)||relative.StartsWith("bin/",StringComparison.Ordinal)||relative.Contains("/obj/",StringComparison.Ordinal)||relative.Contains("/bin/",StringComparison.Ordinal))continue;
            files++;
            var lines=File.ReadAllLines(file);
            for(int i=0;i<lines.Length;i++)if(LooksDoubleEncoded(lines[i]))bad.Add($"{relative}:{i+1}");
        }
        check(files>100,"scanned the C# source tree ("+files.ToString(CultureInfo.InvariantCulture)+" files)");
        check(bad.Count==0,"no mojibake in any .cs file: "+string.Join(", ",bad.Take(8)));
        var panel=File.ReadAllText(Path.Combine(root,"CassetteComposerPanel.cs"));
        check(panel.Contains("\"0\u00B0\",\"30\u00B0\",\"60\u00B0\"",StringComparison.Ordinal),"composer angle buttons read 0\u00B0 30\u00B0 60\u00B0");
        check(panel.Contains("1,\"\u00B0\")",StringComparison.Ordinal),"composer tilt field unit is a plain degree sign");
    }

    // ---------------- point 3: messages ----------------
    private static CassetteComposerModel NewModel(CassetteComposer backend,CassetteCatalogue presets,IReadOnlyDictionary<string,CassetteMaterial> materials)
        =>new(backend,materials.Keys.Select(id=>new ComposerMaterial(id,id,"Other")).ToArray(),Array.Empty<ComposerMaterial>(),()=>presets.Presets,_=>false,_=>true,false);
    private static void Messages(Action<bool,string> check)
    {
        var root=AppContext.BaseDirectory;
        var materials=CassetteTechnology.ReadDirectory(Path.Combine(root,"NativeData"));
        var reactions=MaterialReactionCatalogue.Parse(File.ReadAllText(Path.Combine(root,MaterialReactionCatalogue.FileName)));
        var presets=CassetteCatalogue.Parse(File.ReadAllText(Path.Combine(root,CassetteCatalogue.FileName)));
        var backend=new CassetteComposer(materials,reactions,"unused","unused",_=>false);
        CassettePreset Nera(double tilt)=>Preset(new("rha",3,tilt,"up"),new("elastomerNERA",15,tilt,"up"),new("rha",3,tilt,"up"),new("air",20,0,"up"));
        bool Generic(string text)=>text.Contains("tapers",StringComparison.OrdinalIgnoreCase)||text.Contains("maximumAngleDegrees",StringComparison.Ordinal)||text.Contains("in the recipe",StringComparison.Ordinal);

        // Backend: the taper note is gone; the angle notes are concrete.
        check(backend.AngleNotes(Nera(0),0,true,false).Length==0&&backend.AngleNotes(Nera(0),30,true,false).Length==0&&backend.AngleNotes(Nera(0),60,true,false).Length==0,"untilted NERA: no angle message at 0, 30 or 60 degrees (effective angle never passes 75)");
        var at60=backend.AngleNotes(Nera(30),60,true,true);
        check(at60.Length==1&&at60[0].Layer==1&&at60[0].Text.Contains("NERA at 60\u00B0 with tilt",StringComparison.Ordinal)&&at60[0].Text.Contains("effective angle 90\u00B0 (counted as 80\u00B0)",StringComparison.Ordinal)
            &&at60[0].Text.Contains("recipe maximum 75\u00B0",StringComparison.Ordinal)&&at60[0].Text.Contains("gain reduced to 75%",StringComparison.Ordinal),"NERA tilt 30 at 60 degrees with tilt: concrete message with the angle, the maximum and the gain: "+(at60.Length>0?at60[0].Text:"none"));
        check(backend.AngleNotes(Nera(30),60,false,true).Length==0,"the same cassette against the tilt: effective angle 30, no message");
        check(backend.AngleNotes(Nera(30),30,true,true).Length==0&&backend.AngleNotes(Nera(30),0,true,true).Length==0,"tilt 30 at 30 and 0 degrees: effective 60 and 30, no message");
        var steep=backend.AngleNotes(Nera(57),30,true,true);
        check(steep.Length==1&&steep[0].Text.Contains("effective angle 87",StringComparison.Ordinal)&&steep[0].Text.Contains("gain reduced to 75%",StringComparison.Ordinal),"tilt 57 at 30 degrees with tilt: 87 degrees counted as 80: "+(steep.Length>0?steep[0].Text:"none"));
        check(backend.AngleNotes(Preset(new CassetteLayer("rha",20,0,"up")),60,true,false).Length==0,"no cooperative recipe: no message");
        // Between the maximum and the cap the gain is partly reduced, and the percentage is the real taper.
        check(backend.AngleNotes(Nera(15),60,true,true).Length==0,"effective angle exactly at the maximum (75) is not above it: no message");
        var partial=backend.AngleNotes(Nera(18),60,true,true);
        check(partial.Length==1&&partial[0].Text.Contains("effective angle 78°",StringComparison.Ordinal)&&partial[0].Text.Contains("gain reduced to 90%",StringComparison.Ordinal)&&!partial[0].Text.Contains("counted as",StringComparison.Ordinal),"between the maximum and the cap the real taper is named (78 degrees: 90%): "+(partial.Length>0?partial[0].Text:"none"));

        // View model: the message list follows the selected angle and side, and names the layer.
        var model=NewModel(backend,presets,materials);
        model.LoadDraft(Nera(30),ComposerSource.Import,"test");
        model.AngleIndex=0;
        check(model.Issues.Count==0&&model.LayerRows.All(r=>!r.Warning&&r.Note==null),"0 degrees: no message and no indicator on any layer row");
        var quiet=model.Issues;check(ReferenceEquals(quiet,model.Issues),"messages cached between frames");
        model.AngleIndex=2;model.WithTilt=true;
        var shown=model.Issues;
        check(shown.Count==1&&shown[0].Layer==1&&!shown[0].Error&&shown[0].Text.StartsWith("Layer 2 \u00B7 ",StringComparison.Ordinal)&&shown[0].Text.Contains("NERA at 60\u00B0 with tilt",StringComparison.Ordinal),"60 degrees with tilt: one message that names layer 2: "+(shown.Count>0?shown[0].Text:"none"));
        check(!shown.Any(i=>Generic(i.Text)),"no generic patch-note text in the messages");
        check(ReferenceEquals(shown,model.Issues)&&model.IssueCountText=="1","cached while nothing changes, count 1");
        var rows=model.LayerRows;
        check(rows[1].Warning&&!rows[1].Error&&rows[1].Note!=null&&rows[1].Note!.Contains("gain reduced to 75%",StringComparison.Ordinal)&&!rows[1].Note!.StartsWith("Layer",StringComparison.Ordinal),"layer 2 row: indicator plus its own message as the note (without the layer prefix)");
        check(!rows[0].Warning&&rows[0].Note==null&&!rows[2].Warning&&!rows[3].Warning,"the other rows have neither indicator nor note");
        check(rows[1].Title.Contains("t 15 mm",StringComparison.Ordinal)&&rows[1].Title.Contains("tilt 30\u00B0 up",StringComparison.Ordinal),"a row with a note keeps thickness and tilt in its title");
        model.WithTilt=false;
        check(model.Issues.Count==0&&!model.LayerRows[1].Warning&&model.LayerRows[1].Note==null,"against the tilt: message and indicator disappear");
        model.WithTilt=true;model.AngleIndex=1;
        check(model.Issues.Count==0,"30 degrees with tilt 30: effective 60, no message");
        // The table explanation only shows for a tilted cassette.
        check(model.TableNote.Length>0,"tilted cassette: the with/against explanation is shown");
        var plain=NewModel(backend,presets,materials);plain.LoadDraft(Nera(0),ComposerSource.Import,"test");plain.AngleIndex=2;
        check(plain.TableNote.Length==0&&plain.Issues.Count==0&&plain.ShownIssues.Count==0,"untilted cassette: no table explanation, no message");
        // Errors keep showing, and the list is capped.
        var many=NewModel(backend,presets,materials);
        many.LoadDraft(Preset(Enumerable.Range(0,6).Select(_=>new CassetteLayer("elastomerNERA",15,0,"up")).ToArray()),ComposerSource.Import,"test");
        var all=many.Issues;var capped=many.ShownIssues;
        check(all.Count<=many.ShownIssues.Count||capped.Count==CassetteComposerModel.MaxShownMessages,"the shown list never exceeds the cap");
        if(all.Count>CassetteComposerModel.MaxShownMessages)
            check(capped.Count==CassetteComposerModel.MaxShownMessages&&capped[^1].Layer==-1&&capped[^1].Text.StartsWith("+",StringComparison.Ordinal)&&capped[^1].Text.EndsWith("more messages",StringComparison.Ordinal)&&ReferenceEquals(capped,many.ShownIssues),"more than four messages: three plus a '+N more messages' line (cached)");
        else check(ReferenceEquals(all,capped),"four or fewer messages are shown as they are");
        var bad=NewModel(backend,presets,materials);
        bad.LoadDraft(Preset(new("rha",3,80,"up"),new("elastomerNERA",15,0,"up")),ComposerSource.Import,"test");
        check(bad.HasErrors&&bad.ShownIssues.Any(i=>i.Error)&&bad.LayerRows[0].Error&&bad.LayerRows[0].Note!=null,"an error on a layer stays visible, with its note on the row");
        // Source guard: the generic note must not come back into the composer.
        var composerSource=File.ReadAllText(Path.Combine(SourceRoot()??".","CassetteComposer.cs"));
        check(!composerSource.Contains("gain tapers smoothly",StringComparison.Ordinal)&&!composerSource.Contains("TaperNotes",StringComparison.Ordinal),"the always-on taper rule text is gone from the composer backend");
    }

    // ---------------- point 2: protection table and fit ----------------
    private static void Table(Action<bool,string> check)
    {
        // 0.22.23: the fixed-width layout (ProtectionTableLayout) is replaced by the Kit Table; the content stays under test.
        check(ProtectionTable.Headers(true).SequenceEqual(new[]{"With tilt","Against tilt"})&&ProtectionTable.Headers(false).SequenceEqual(new[]{"Protection"}),"column headers: With tilt / Against tilt, or the single Protection column without a tilted layer");
        check(ProtectionTable.Metrics.Length==3,"three metric rows under the header");
    }
    private static void Fit(Action<bool,string> check)
    {
        check(ComposerLayout.ShrinkTo(2500,1100,2400,1000) is {W:2400,H:1000},"a panel larger than the screen shrinks to the screen size");
        check(ComposerLayout.ShrinkTo(2400,1100,2400,1000) is {W:2400,H:1000},"only the height too large: the width stays at the screen width");
        check(ComposerLayout.ShrinkTo(1800,1100,2400,1000) is {W:1800,H:1000},"only the height too large: the width is kept");
        check(ComposerLayout.ShrinkTo(2400,1000,2400,1000)==null&&ComposerLayout.ShrinkTo(2404,1004,2400,1000)==null,"fits (or within 4 units): untouched");
        check(ComposerLayout.ShrinkTo(1200,700,2400,1000)==null,"a panel the user made smaller is never enlarged");
        check(ComposerLayout.ShrinkTo(2500,1100,0,0)==null&&ComposerLayout.ShrinkTo(2500,1100,float.NaN,1000)==null,"an unknown screen size changes nothing");
        // The size for a 3440x1440 screen at the kit scale really fits the view.
        var size=ComposerLayout.Size(3440,1440,1.3333f,1f);
        var view=ComposerLayout.View(3440,1440,ComposerLayout.EffectiveScale(1.3333f,1440,1f));
        check(size.W<=view.W&&size.H<=view.H&&ComposerLayout.ShrinkTo(size.W,size.H,size.W,size.H)==null,"3440x1440 at the kit scale: the panel fits the view");
    }

    // ---------------- presets and ceramic materials (data package) ----------------
    private static readonly string[] NewPresets={"t64a","t64b","t64a_hull","t90","t90_hull","t90a","t90m","oplot","oplot_hull","oplotm","leopard2a5_wedge","leopard2a7_wedge","leclerc","merkava4","challenger2","gen_corundum_light","gen_ti_sic_block","gen_heavy_wedge","gen_sic_block","gen_du_array"};
    private static void Presets(Action<bool,string> check)
    {
        var root=AppContext.BaseDirectory;
        var tech=Path.Combine(root,"NativeData");
        var materials=CassetteTechnology.ReadDirectory(tech);var dates=CassetteTechnology.ReadDates(tech);
        var reactions=MaterialReactionCatalogue.Parse(File.ReadAllText(Path.Combine(root,MaterialReactionCatalogue.FileName)));
        var presets=CassetteCatalogue.Parse(File.ReadAllText(Path.Combine(root,CassetteCatalogue.FileName))).Presets;
        check(presets.Length==35&&presets.Select(p=>p.Id).Distinct().Count()==35,"35 distinct presets in the shipped sidecar");
        check(presets.Count(p=>p.Location=="turret")==25&&presets.Count(p=>p.Location=="hull")==10,"25 turret and 10 hull presets");
        check(NewPresets.All(id=>presets.Any(p=>p.Id==id)),"all 20 new preset ids are present");
        int loaded=0,signatures=0,dated=0;var problems=new List<string>();
        foreach(var preset in presets)
        {
            try
            {
                var model=new CassetteModel(preset,materials,reactions);loaded++;
                if(materials.TryGetValue(preset.MaterialId,out var native)&&native==model.Passive)signatures++;else problems.Add("signature "+preset.Id);
                if(model.DepthMm<preset.MinimumDepthMm||model.DepthMm>preset.MaximumDepthMm)problems.Add("window "+preset.Id);
                var newest=CassetteAvailability.Newest(preset.Layers,id=>dates.TryGetValue(id,out var d)?d:null);
                if(!DateTime.TryParseExact(preset.Date,"yyyy.MM.dd",CultureInfo.InvariantCulture,DateTimeStyles.None,out var presetDate))problems.Add("date format "+preset.Id);
                else if(newest is {} n&&presetDate<n.Date)problems.Add($"dated before {n.Material}: {preset.Id}");else dated++;
            }
            catch(Exception ex){problems.Add(preset.Id+": "+ex.Message);}
        }
        check(loaded==35,"all 35 presets load through CassetteModel (the loader's own constructor): "+string.Join("; ",problems.Take(5)));
        check(signatures==35,"each Technology file equals the preset's passive signature (35 of 35)");
        check(dated==35,"no preset is dated before its newest material");
        check(problems.Count==0,"no other preset problem: "+string.Join("; ",problems.Take(5)));
        check(NewPresets.All(id=>materials.ContainsKey("ebCassette_"+id))&&materials.ContainsKey("aluminaCeramic")&&materials.ContainsKey("siliconCarbide"),"22 new Technology files are read");
        // The new ceramic materials: bound to their own recipe, with the same numbers as the Technology file.
        foreach(var id in new[]{"aluminaCeramic","siliconCarbide"})
        {
            check(reactions.Recipes.ContainsKey(id),id+": recipe present");
            check(CassettePalette.Group(id)=="Ceramic"&&CassettePalette.Colour(id)!=CassettePalette.Colour("rha")&&CassetteSectionLabels.ShortName(id,"x")!="x",id+": palette group, colour and short label");
            check(SprocketExpandedBallistics.Materials.MaterialAvailability.Label(id,"?")!="?",id+": menu label");
        }
        foreach(var id in NewPresets)check(SprocketExpandedBallistics.Materials.MaterialAvailability.Label("ebCassette_"+id,"?")!="?","menu label for ebCassette_"+id);
        check(dates["aluminaCeramic"]<dates["siliconCarbide"]&&dates["aluminaCeramic"].Year==1965&&dates["siliconCarbide"].Year==1985,"material dates 1965 (alumina) and 1985 (SiC)");
        // The merge adds only ids that are missing: no id of the new rows collides with an old row, and ids are unique per list.
        var oldIds=presets.Where(p=>!NewPresets.Contains(p.Id)).Select(p=>p.Id).ToHashSet();
        check(oldIds.Count==15&&!NewPresets.Any(oldIds.Contains),"15 old preset ids, none reused by the 20 new ones (the installer merge adds by id and never replaces)");
    }
}
