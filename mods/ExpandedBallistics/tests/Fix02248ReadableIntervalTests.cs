using System.Text.Json;

using System.Text;

using System.Text.RegularExpressions;

namespace SprocketExpandedBallistics;

internal static class Fix02248ReadableIntervalTests

{

    internal static void Run(Action<bool,string> check)

    {

        int count=0;var sink=check;check=(ok,msg)=>{count++;sink(ok,"fix02248: "+msg);};

        var root=Fix02228Tests.SourceRoot();var parent=Fix02228Tests.SessionRoot();var output=Fix02228Tests.OutputRoot();

        var h=new PaidMaterialPiece(1,"highHardnessSteel","highHardnessSteel",0,.145,1.16,65);

        check(MaterialIntervalDisplay.Piece(h)=="High-hardness steel: 145 mm × 1.16 = 168 mm RHA-eq","canonical steel formula");

        check(MaterialIntervalDisplay.Piece(h with{Material="rha",Factor=1,EndM=.168})=="Rolled homogeneous armour: 168 mm RHA","unity factor omitted");

        check(MaterialIntervalDisplay.Piece(h with{EndM=.00619})=="High-hardness steel: 6.2 mm × 1.16 = 7.2 mm RHA-eq","sub-10 mm precision");

        var layer=new CombatLayerInfo("test",new[]{new CombatLayerPart(1,"1","highHardnessSteel","High hardness steel",50,65,true),new(2,"2","rha","rha",5,0,true)},true);

        var p=new PaidMaterialPiece(2,"rha","rha",.145,.15,1,0);

        var tiny=new PaidMaterialPiece(3,"native:7","Crew member #3",.15,.16,.01,0);

        var e=new CombatReportEvent("materialReaction","","Confirmed material intervals: debug",600,400,"test"){PaidIntervals=new[]{h,p,tiny}};

        var overlap=new CombatReportEvent("materialIntervalOverlap","","diagnostic",HitId:"test",RelatedEntity:1);

        var text=MaterialIntervalDisplay.Reaction(e,layer,new[]{e,overlap});

        check(text.Split('\n').Length==2&&!text.Contains("Crew"),"tiny contribution hidden, no recalculation");

        check(text.Split('\n')[0].EndsWith(" (overlap counted once)"),"overlap note on affected material");

        check(MaterialIntervalDisplay.Sublayers(e,layer,new[]{e}).SequenceEqual(text.Replace(" (overlap counted once)","").Split('\n')),"shared forensic formatter");

        check(MaterialIntervalDisplay.Sublayers(e with{PaidIntervals=new[]{h}},layer,new[]{e}).Length==0,"one paid member has no breakdown");

        check(MaterialIntervalDisplay.Reaction(e with{PaidIntervals=Array.Empty<PaidMaterialPiece>()},layer,new[]{e})=="","exhausted interval hidden");

        check(MaterialIntervalDisplay.Reaction(e with{PaidIntervals=new[]{p}},layer with{Parts=new[]{layer.Parts[1]}},new[]{e})=="","ordinary RHA has no redundant reaction");

        var many=string.Join("\n",Enumerable.Repeat(MaterialIntervalDisplay.Piece(h),30));

        var wrapped=CombatReportView.ReactionLines(many);

        check(!string.Join(" ",wrapped).Contains('…')&&wrapped.Count(x=>x.Contains("RHA-eq"))==30,"all equivalents survive cell wrapping beyond three lines");

        string[] fixtures={"zero-loss-1107","forensic-fixtures","forensic-fixtures-1108","forensic-fixtures-1108b","forensic-fixtures-1108c","t80u-1108","forensic-fixtures-1108d"};

        var rows=new StringBuilder("fixture\tshot\tentity\tlength_mm\tfactor\trha_eq_mm\tdisplay\n");int shots=0,pieces=0;

        foreach(var fixture in fixtures)

        {

            using var doc=JsonDocument.Parse(File.ReadAllText(Path.Combine(parent,fixture,"last-session.json")));

            var saved=JsonSerializer.Deserialize<CombatReportShot[]>(doc.RootElement.GetProperty("shots").GetRawText(),new JsonSerializerOptions{PropertyNameCaseInsensitive=true})!;

            foreach(var shot in saved)

            {

                shots++;var before=JsonSerializer.Serialize(shot);

                var view=CombatReportView.Build(new[]{shot},1)[0];var f=CombatForensics.Build(shot);

                var texts=view.Layers.Select(l=>l.Reaction).Concat(CombatHitProjection.Build(shot).Select(h=>h.Reaction)).Concat(f.Layers.SelectMany(l=>l.PaidIntervals??Array.Empty<string>()));

                foreach(var t in texts)check(!Regex.IsMatch(t,@"entity|ray \[|confirmed unpaid|\[native:|Confirmed material intervals",RegexOptions.IgnoreCase),"clean visible interval text "+fixture+" #"+shot.Number);

                foreach(var piece in shot.Events.SelectMany(e=>e.PaidIntervals??Array.Empty<PaidMaterialPiece>()))

                {

                    pieces++;var label=MaterialIntervalDisplay.Piece(piece);

                    var values=Regex.Match(label,@": ([\d.]+) mm(?: × ([\d.]+) = ([\d.]+) mm RHA-eq| RHA)$");

                    check(values.Success,"readable numeric formula");

                    check(Math.Abs(double.Parse(values.Groups[1].Value,System.Globalization.CultureInfo.InvariantCulture)-piece.LengthMm)<(piece.LengthMm<10?.050001:.500001),"length from unchanged PaidIntervals");

                    check(piece.Factor==1||Math.Abs(double.Parse(values.Groups[3].Value,System.Globalization.CultureInfo.InvariantCulture)-piece.RhaMm)<(piece.RhaMm<10?.050001:.500001),"equivalent from unchanged PaidIntervals");

                    check(piece.Factor==1||Math.Abs(double.Parse(values.Groups[2].Value,System.Globalization.CultureInfo.InvariantCulture)-piece.Factor)<=.00500001,"factor rounded within two decimals");
                    rows.AppendLine(FormattableString.Invariant($"{fixture}\t{shot.Number}\t{piece.Entity}\t{piece.LengthMm:R}\t{piece.Factor:R}\t{piece.RhaMm:R}\t{label}"));

                }

                check(JsonSerializer.Serialize(shot)==before,"presentation leaves complete JSON bytes intact");

            }

            if(fixture=="forensic-fixtures-1108d")

            {

                var history=new CombatReportHistory();history.Restore(saved,0);

                File.WriteAllText(Path.Combine(output,"preview/latest-session.html"),CombatReportHtml.Render(history));

                File.WriteAllText(Path.Combine(output,"preview/latest-forensic.html"),"<html><meta charset='utf-8'><style>body{background:#232930;color:#e7edf2;font:16px system-ui;max-width:1320px;margin:24px auto}"+CombatForensicHtml.Css+"</style>"+string.Join("",saved.Reverse().Select(x=>CombatForensicHtml.Render(CombatForensics.Build(x),"shot-"+x.Number)))+"</html>");

            }

        }



        File.WriteAllText(Path.Combine(output,"READABLE-INTERVALS.tsv"),rows.ToString());

        File.WriteAllText(Path.Combine(output,"FIX48-RESULT.json"),JsonSerializer.Serialize(new{passed=true,checks=count,shots,pieces},new JsonSerializerOptions{WriteIndented=true}));

        Console.WriteLine($"FIX02248 PASS: {count} checks; {shots} fixture shots; {pieces} paid pieces.");

    }

}

