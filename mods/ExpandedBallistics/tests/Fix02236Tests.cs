using System.Globalization;
using System.Text;
namespace SprocketExpandedBallistics;

internal static class Fix02236Tests
{
    internal static void Run(Action<bool,string> check)
    {
        int count=0;var sink=check;check=(ok,msg)=>{sink(ok,"fix02236: "+msg);count++;};
        var root=Fix02228Tests.SourceRoot();var output=Fix02228Tests.OutputRoot();
        var mats=CassetteTechnology.ReadDirectory(Path.Combine(AppContext.BaseDirectory,"NativeData"));
        var reactions=MaterialReactionCatalogue.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory,MaterialReactionCatalogue.FileName)));
        var presets=CassetteCatalogue.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory,CassetteCatalogue.FileName))).Presets;
        var composer=new CassetteComposer(mats,reactions,"","",_=>false);
        var view=new CassetteComposerModel(composer,mats.Keys.Select(id=>new ComposerMaterial(id,id,"Other")).ToArray(),Array.Empty<ComposerMaterial>(),()=>presets,_=>false,_=>true,false);
        var rows=new StringBuilder("preset\ttilt_sign\ttilted\tfactor_cases\tnative_write_cases\tbit_differences\n");
        var angles=Enumerable.Range(-720,1441).Select(i=>i*.125).Concat(new[]{double.NaN,double.PositiveInfinity,double.NegativeInfinity,180d,-180d}).ToArray();
        foreach(var preset in presets)foreach(int sign in new[]{1,-1})
        {
            var model=new CassetteModel(preset with{Layers=preset.Layers.Select(l=>l with{TiltDegrees=l.TiltDegrees*sign}).ToArray()},mats,reactions);
            var table=ProtectionFactorTable.Build(model);var old=Golden02235ProtectionFactorTable.Build(model);int cases=0,writes=0;
            foreach(var threat in ProtectionFactorTable.Threats)foreach(double angle in angles)
            {
                double w=old.Factor(threat,angle,true),a=old.Factor(threat,angle,false),expected=(w+a)*.5,actual=table.Factor(threat,angle);
                check(BitConverter.DoubleToInt64Bits(actual)==BitConverter.DoubleToInt64Bits(expected),"runtime equals 0.22.35 side mean");cases++;
                if(!table.Tilted)check(BitConverter.DoubleToInt64Bits(actual)==BitConverter.DoubleToInt64Bits(w),"untilted bit-exact 0.22.35");
                if(!double.IsFinite(angle))continue;
                foreach(double exponent in new[]{0d,1,1.33,2})foreach(double overmatch in new[]{0d,.25,1})
                {
                    float got=(float)ProtectionFactorTable.NativeRhaFactor(actual,angle,exponent,overmatch,.75);
                    float want=(float)Golden02235ProtectionFactorTable.NativeRhaFactor(expected,angle,exponent,overmatch,.75);
                    check(BitConverter.SingleToInt32Bits(got)==BitConverter.SingleToInt32Bits(want),"native write from shared mean");writes++;
                }
            }
            rows.AppendLine($"{preset.Id}\t{sign}\t{table.Tilted}\t{cases}\t{writes}\t0");
            if(sign<0)continue;
            check(view.LoadPreset(preset.Id),"composer preset loaded");
            foreach(var c in composer.ProtectionCurve(preset))foreach(var threat in ProtectionFactorTable.Threats)
            {
                double mm=threat switch{ProtectionThreat.LongRod=>c.KeMm,ProtectionThreat.FullBoreKE=>c.FullBoreMm,ProtectionThreat.Jet=>c.HeatMm,_=>c.HeshMm};
                check(mm==ProtectionFactorTable.PathMm(table.DepthMm,c.Angle)*table.Factor(threat,c.Angle),"composer chart is runtime mean at all 18 chart angles");
            }
            foreach(bool side in new[]{true,false})for(int i=0;i<3;i++)foreach(var threat in ProtectionFactorTable.Threats)
            {
                view.WithTilt=side;view.AngleIndex=i;var row=view.ProtectionRows(threat)[0];
                var mm=table.ProtectionMm(threat,CassetteComposerModel.Angles[i]);
                check(row.With==row.Against&&row.With==mm.ToString("0",CultureInfo.InvariantCulture),"composer selected table is runtime mean regardless of section side");
            }
        }
        File.WriteAllText(Path.Combine(output,"MEAN-GOLDEN.tsv"),rows.ToString());
        var cheek=ProtectionFactorTable.Build(new(presets.Single(p=>p.Id=="leopard2a4"),mats,reactions));
        var cheeks=new StringBuilder("angle\tmean_factor\trha_at_932mm\n");
        // Three actual 04-11 cheek shots (#2/#3/#4), plus exact 27 degrees
        // and the cheek member angle in the combined wedge block (#1).
        foreach(double angle in new[]{27d,26.83752219179673,26.915973634435634,26.934194507463946,27.03023875307013})
        {
            double f=cheek.Factor(ProtectionThreat.LongRod,angle);
            check(Math.Abs(f-.5252687582408526)<.001,"cheek near 0.52527 for recorded angles (brief's 0.51 is approximate)");
            cheeks.AppendLine(FormattableString.Invariant($"{angle:R}\t{f:R}\t{932*f:R}"));
        }
        File.WriteAllText(Path.Combine(output,"CHEEK-FACTORS.tsv"),cheeks.ToString());
        var native=File.ReadAllText(Path.Combine(root,"NativeMaterialReactions.cs"));
        check(!native.Contains("TiltBlend")&&!native.Contains("WithTilt")&&!native.Contains("FactorBlended"),"native direction/band selection removed");
        check(System.Text.RegularExpressions.Regex.Matches(native,@"\.Factor\(").Count==5,"direct, blended, overlap and skipped paths retain Factor; module group diagnostic uses the same lookup");
        var tableSource=File.ReadAllText(Path.Combine(root,"ProtectionFactorTable.cs"));
        check(!tableSource.Contains("AmbiguousTiltFraction")&&!tableSource.Contains("FactorBlended")&&!tableSource.Contains("TiltBlend")&&!tableSource.Contains("static bool WithTilt"),"obsolete band and sign code deleted");
        var panel=File.ReadAllText(Path.Combine(root,"CassetteComposerPanel.cs"));
        check(panel.Contains("return row.With;")&&!panel.Contains("Side()")&&panel.Contains("mean (game)"),"panel binds mean table and chart with explicit legend");
        Console.WriteLine($"0.22.36 PASS: {count} checks; frozen 0.22.35 mean oracle, untilted bit parity, cheek and composer.");
    }
}
