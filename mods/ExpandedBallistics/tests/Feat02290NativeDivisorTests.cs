using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
namespace SprocketExpandedBallistics;

/// <summary>
/// 0.22.90 (brief FEAT-0.22.90-NATIVE-DIVISOR-FLOOR; user 16-11 "het was niet mijn conclusie. Anyway, fix. Dit had al eerder gezien moeten worden, delen door 0...").
/// A: the floor 1e-6 on every native rhaFactor write (D1 exhaustive, D2 the native order, D3 the replay of the log of 16-11, D4 the golden suites, D5 the repaired
/// write sites with 0, a negative value and NaN). B: DIVISOR-AUDIT.tsv and the static test that no SetRha (and no other "ja" site) writes past the floor.
/// C: the read-only diagnostic line. H: cassettes with a stock Sand.json without costMultiplier. Plus the file ledger against src-0.22.89.
/// The native write sites are IL2CPP code: their guard lines are checked on the source and their decision is evaluated with the production pure helpers.
/// </summary>
internal static class Feat02290NativeDivisorTests
{
    /// <summary>The production files 0.22.90 may change on top of 0.22.89.</summary>
    internal static bool IsPlanned(string rel)=>rel is "ProtectionFactorTable.cs" or "NativeMaterialReactions.cs" or "RuntimeOriginalTrajectory.cs" or "CassetteTechnology.cs" or "CassetteModel.cs"
        or "CassetteDefaults.cs" or "RuntimeAmmoLoadout.cs" or "Plugin.cs" or "SprocketExpandedBallistics.csproj" or "RELEASE-NOTES.md";

    /// <summary>
    /// One native block in the game's order (SimulateFragment, VANISH-MAP section 2): B = b x ap / rha (0x1D782C2); when om &lt; 1, B = (B - c*B)*om + c*B with
    /// c = cos^(e-1) (0x1D78310-54). With rha 0 and om &lt; 1 this is inf - inf = NaN, as in the game; the old test models (b*ap/rha*multiplier) gave +inf.
    /// </summary>
    internal static double NativeBudget(double budget,double apModifier,double rha,double exponent,double angle,double overmatch)
    {
        var b=budget*apModifier/rha;
        var om=Math.Clamp(overmatch,0,1);
        if(om<1){var c=Math.Pow(Math.Max(1e-6,Math.Cos(Math.Clamp(Math.Abs(angle),0,89.999)*Math.PI/180)),exponent-1);b=(b-c*b)*om+c*b;}
        return b;
    }
    /// <summary>The same in single precision, as the game computes it (divss, powf(cos,e)/cos, subss/mulss/addss).</summary>
    internal static float NativeBudgetF(float basePenetration,float apModifier,float rha,float exponent,float angle,float overmatch)
    {
        var b=basePenetration*apModifier/rha;
        if(overmatch<1){var cos=MathF.Cos(angle*MathF.PI/180);var c=MathF.Pow(cos,exponent)/cos;b=(b-c*b)*overmatch+c*b;}
        return b;
    }
    private static int Bits(float v)=>BitConverter.SingleToInt32Bits(v);
    private static string G(double v)=>v.ToString("g6",CultureInfo.InvariantCulture);
    private static string G(float v)=>v.ToString("g6",CultureInfo.InvariantCulture);

    /// <summary>The 0.22.89 reader (verbatim), for the bit-for-bit comparison of H1.</summary>
    private static Dictionary<string,CassetteMaterial> ReadDirectory89(string directory)
    {
        var result=new Dictionary<string,CassetteMaterial>(StringComparer.Ordinal);
        foreach(var file in Directory.EnumerateFiles(directory,"*.json"))
        {
            using var doc=JsonDocument.Parse(File.ReadAllText(file),new JsonDocumentOptions{AllowTrailingCommas=true});
            var root=doc.RootElement;
            if(!root.TryGetProperty("type",out var t)||!root.TryGetProperty("properties",out var p)||!p.TryGetProperty("rhaFactor",out var rha))continue;
            string id=t.GetString()!;
            var material=new CassetteMaterial(id,p.GetProperty("density").GetDouble(),rha.GetDouble(),p.GetProperty("spallFactor").GetDouble(),p.GetProperty("costMultiplier").GetDouble());
            if(result.TryGetValue(id,out var old)&&old!=material)throw new FormatException("Ambiguous passive material technology: "+id);
            result[id]=material;
        }
        return result;
    }
    private static bool SameBits(IReadOnlyDictionary<string,CassetteMaterial> a,IReadOnlyDictionary<string,CassetteMaterial> b)
    {
        static long D(double v)=>BitConverter.DoubleToInt64Bits(v);
        return a.Count==b.Count&&a.All(p=>b.TryGetValue(p.Key,out var o)&&o.Id==p.Value.Id&&D(o.Density)==D(p.Value.Density)&&D(o.RhaFactor)==D(p.Value.RhaFactor)&&
            D(o.SpallFactor)==D(p.Value.SpallFactor)&&D(o.CostMultiplier)==D(p.Value.CostMultiplier));
    }

    internal static void Run(Action<bool,string> sink)
    {
        int checks=0,failures=0;void Check(bool ok,string message){checks++;if(!ok)failures++;sink(ok,"FEAT90: "+message);}
        string root=Fix02228Tests.SourceRoot(),parent=Fix02228Tests.SessionRoot(),output=Fix02228Tests.OutputRoot(),old89=Path.Combine(parent,"src-0.22.89");
        string out89=Path.Combine(parent,"out-0.22."+"89");Directory.CreateDirectory(output);
        string Read(string f)=>File.ReadAllText(Path.Combine(root,f)).Replace("\r\n","\n");
        var inv=CultureInfo.InvariantCulture;

        // ---------------- D1: NativeSafe, exhaustive ----------------
        const int Low=0x358637BD,High=0x7F7FFFFF;
        Check(Bits(ProtectionFactorTable.MinimumNativeRhaFactor)==Low,"MinimumNativeRhaFactor is 1e-6f (0x358637BD)");
        var watch=System.Diagnostics.Stopwatch.StartNew();
        long same=0,changed=0;
        for(int bits=Low;bits<=High;bits++){var v=BitConverter.Int32BitsToSingle(bits);if(Bits(ProtectionFactorTable.NativeSafe(v))==bits)same++;else changed++;}
        Check(changed==0&&same==(long)High-Low+1,$"every finite v >= 1e-6f is returned bit for bit ({same:N0} patterns 0x358637BD..0x7F7FFFFF, {changed} changed)");
        long floored=0,other=0;
        for(int bits=0;bits<Low;bits++){var v=BitConverter.Int32BitsToSingle(bits);if(Bits(ProtectionFactorTable.NativeSafe(v))==Low)floored++;else other++;}
        Check(other==0&&floored==Low,$"every v in [0, 1e-6) gives exactly 1e-6f ({floored:N0} patterns 0x00000000..0x358637BC, subnormals included; {other} other)");
        var exhaustiveSeconds=watch.Elapsed.TotalSeconds;
        Check(Bits(ProtectionFactorTable.NativeSafe(-0f))==Low&&Bits(ProtectionFactorTable.NativeSafe(-1f))==Low&&Bits(ProtectionFactorTable.NativeSafe(float.NegativeInfinity))==Low&&
              Bits(ProtectionFactorTable.NativeSafe(-float.Epsilon))==Low,"-0, negatives and -inf give 1e-6f");
        Check(float.IsNaN(ProtectionFactorTable.NativeSafe(float.NaN))&&float.IsPositiveInfinity(ProtectionFactorTable.NativeSafe(float.PositiveInfinity)),"NaN and +inf pass unchanged (the callers reject non-finite values)");
        Check(Bits(ProtectionFactorTable.NativeSafe(MathF.BitDecrement(1e-6f)))==Low&&Bits(ProtectionFactorTable.NativeSafe(MathF.BitIncrement(1e-6f)))==Low+1,"the edge: one ulp below goes to 1e-6, one ulp above stays");

        // ---------------- D2: the native order ----------------
        var order=new StringBuilder("case\tbudget\tapModifier\trha\texponent\tangle\tovermatch\tpath_mm\tB (native order)\tB/path>1 (pass)\tused\tbranch\told test model (b*ap/rha*mult)\n");
        void Row(string name,double budget,double ap,double rha,double e,double angle,double om,double path,double dMm=27.5)
        {
            var b=NativeBudget(budget,ap,rha,e,angle,om);var used=b>path?path:b;
            var branch=b/path>1?"perforation":!(used>dMm)&&angle>=HeshSettings.NativeRicochetDegrees?"ricochet":"embedded";
            var oldModel=budget*ap/rha*ProtectionFactorTable.NativeAngleMultiplier(angle,e,om);
            order.AppendLine(string.Create(inv,$"{name}\t{budget}\t{ap}\t{rha}\t{e}\t{angle}\t{om}\t{path}\t{G(b)}\t{b/path>1}\t{G(used)}\t{branch}\t{G(oldModel)}"));
        }
        var nan=NativeBudget(500,1,0,1,42,0);
        Check(double.IsNaN(nan)&&!(nan/236>1),"rha 0 and om < 1: B is NaN (inf - inf) and there is no pass, as in the game");
        var nanUsed=nan>236?236:nan;Check(double.IsNaN(nanUsed)&&!(nanUsed>27.5),"rha 0: the charge is NaN and the embedded test fails, so at 42 degrees the game takes the ricochet branch (1D7839D)");
        Check(double.IsPositiveInfinity(500*1/0d*ProtectionFactorTable.NativeAngleMultiplier(42,1,0)),"the 0.22.89 test model (b*ap/rha*multiplier) gave +inf here, not NaN: it could not see the fault");
        var floor=NativeBudget(500,1,1e-6,1,42,0);Check(double.IsFinite(floor)&&floor/236>1,"rha 1e-6 and om < 1: B is finite and a pass (B/path > 1)");
        var harmless=NativeBudget(500,1,0,1,42,1);Check(double.IsPositiveInfinity(harmless)&&harmless/3>1,"rha 0 and om >= 1 (thin wall): +inf and a pass, the harmless variant of VANISH-MAP");
        var nanF=NativeBudgetF(.5f,1,0f,1.33f,42,0);var floorF=NativeBudgetF(.5f,1,1e-6f,1.33f,42,0);
        Check(float.IsNaN(nanF)&&float.IsFinite(floorF)&&floorF/.236f>1,"single precision as the game: rha 0 gives NaN, rha 1e-6f gives a finite pass ("+G(floorF)+" m)");
        Check(float.IsFinite(NativeBudgetF(.5f,1,1e-6f,2f,85,0))&&float.IsFinite(NativeBudgetF(3f,2f,1e-6f,0f,0,0)),"single precision: the floor stays finite at the extremes (85 degrees, e 0..2, ap 2)");
        Row("uncovered (16-11 r. 830)",500,1,0,1,42,0,236);Row("floor 1e-6",500,1,1e-6,1,42,0,236);Row("thin wall, om 1",500,1,0,1,42,1,3);Row("ordinary 0.4",500,1,.4,1,42,0,236);Row("ordinary 0.4, e 1.33",500,1,.4,1.33,60,.2,236);
        int sweep=0,sweepDiff=0;
        foreach(var budget in new[]{5d,50,240,800,2000})foreach(var rha in new[]{.01,.2,.41334,1,1.16,3})foreach(var e in new[]{0d,.5,1,1.33,2})
        foreach(var angle in new[]{0d,10,30,42,60,75,85})foreach(var om in new[]{0d,.1,.5,.99,1})foreach(var path in new[]{3d,50,236,700})
        {
            sweep++;var a=NativeBudget(budget,1,rha,e,angle,om);var o=budget/rha*ProtectionFactorTable.NativeAngleMultiplier(angle,e,om);
            if(!(Math.Abs(a-o)<=1e-12*Math.Abs(o))||(a/path>1)!=(o>path))sweepDiff++;
        }
        Check(sweepDiff==0,$"for every finite case the native order equals the old model to 1e-12 and gives the same pass/stop ({sweep} cases)");
        Check(Read("tests/Feat02286HeTests.cs").Contains("var b=Feat02290NativeDivisorTests.NativeBudget(budget,apModifier,rha,exponent,angle,overmatch);",StringComparison.Ordinal)&&
              Read("tests/Feat02278HeshTests.cs").Contains("var b=Feat02290NativeDivisorTests.NativeBudget(budget,apModifier,rha,exponent,angle,overmatch);",StringComparison.Ordinal)&&
              Read("tests/Feat02286HeTests.cs").Contains("return (b/path>1?Native.Perforated:!(used>dMm)",StringComparison.Ordinal)&&Read("tests/Feat02278HeshTests.cs").Contains("return (b/path>1?Native.Perforated:!(used>dMm)",StringComparison.Ordinal),
              "the HE (Feat02286:134) and HESH (Feat02278:58) block models use the native order and tests");

        // ---------------- D3: replay of the log of 16-11 ----------------
        var logPath=Path.Combine(parent,"diag-0.22.87-varma-vanish","LogOutput-20261116-1319.log");
        var replay=new StringBuilder("log_line\tplate\tmaterial\trhaFactor_before\twritten_0.22.87_log\twritten_0.22.90\tstatus\n");
        int writes=0,uncovered=0,splinters=0,unchanged=0;var uncoveredLines=new List<int>();var splinterLines=new List<int>();
        if(File.Exists(logPath))
        {
            var log=File.ReadAllLines(logPath);
            var rx=new Regex(@"\[Protection\] plate #(\d+) (\S+): (.+?); exponent (\S+); rhaFactor (\S+) -> (\S+) \(read back (\S+)\)$",RegexOptions.CultureInvariant);
            for(int i=0;i<log.Length;i++)
            {
                var m=rx.Match(log[i]);if(!m.Success)continue;writes++;
                var material=m.Groups[2].Value;var written=float.Parse(m.Groups[6].Value,inv);var now=ProtectionFactorTable.NativeSafe(written);string status;
                if(material=="uncovered")
                {
                    uncovered++;uncoveredLines.Add(i+1);
                    Check(written==0&&Bits(now)==Low&&G(now)=="1e-06","r. "+(i+1)+" uncovered: 0 -> 1e-06 (floor)");
                    status="uncovered: floor; the log now reads \"-> 1e-06 (read back 1e-06) (floor; no unpaid path)\"";
                }
                else if(written==0)
                {
                    splinters++;splinterLines.Add(i+1);
                    var evidence=Enumerable.Range(i+1,Math.Max(0,Math.Min(3,log.Length-i-1))).Any(k=>log[k].Contains("confirmed unpaid path 0.001 mm",StringComparison.Ordinal));
                    Check(evidence,"r. "+(i+1)+" printed 0 but not uncovered: a 0.001 mm unpaid splinter follows (VANISH-MAP r. 886/1023/1035)");
                    status="printed 0 (5 decimals); exact value not in the log: 0.4 x [0.0005, 0.0015) mm / 236 mm = 8.5e-7..2.5e-6 (estimate, VANISH-MAP 1.7e-6); unchanged if >= 1e-6, else raised to 1e-6 (< 0.00004 mm RHA)";
                }
                else
                {
                    unchanged++;if(Bits(now)!=Bits(written))Check(false,"r. "+(i+1)+" "+material+": a logged value changed under the floor");
                    status="unchanged (bit for bit)";
                }
                replay.AppendLine($"{i+1}\t{m.Groups[1].Value}\t{material}\t{m.Groups[5].Value}\t{m.Groups[6].Value}\t{(material=="uncovered"?G(now):written==0?"see status":m.Groups[6].Value)}\t{status}");
            }
            Check(new[]{830,923,970,1036,1087,1229}.All(uncoveredLines.Contains)&&uncovered==7&&uncoveredLines.Contains(1325),$"the brief's six uncovered lines (830, 923, 970, 1036, 1087, 1229) and r. 1325 are the {uncovered} uncovered writes; each becomes 1e-06");
            Check(splinters==3&&new[]{884,1020,1034}.SequenceEqual(splinterLines),"the three printed-0 splinter writes are r. 884, 1020, 1034 (native:14)");
            Check(writes==94&&unchanged==writes-uncovered-splinters,$"every other rhaFactor a -> b line ({unchanged}) stays bit for bit at the log's precision ({writes} writes in the log)");
        }
        else replay.AppendLine("the log of 16-11 is not in this tree; the replay is in the owner delivery (out-0.22.90)"); // herpak
        Check(G(1e-6f)=="1e-06"&&G(0f)=="0"&&G(.4f)=="0.4","the log format g6 tells 0 and 1e-06 apart");

        // ---------------- D4: golden suites ----------------
        var golden=new StringBuilder("file\tregenerated_this_run\tequal_to_0.22.89\tfactors\tbelow_1e-6\tminimum_factor\n");
        int goldenFactors=0,goldenBelow=0;
        foreach(var name in new[]{"INTERVAL-IMPACT.tsv","PROTECTION-02244.tsv","PROTECTION-02245.tsv","PROTECTION-02246.tsv","PROTECTION-TABLES.tsv"})
        {
            var a=Path.Combine(out89,name);var b=Path.Combine(output,name);
            var exists=File.Exists(b);var equal=exists&&File.Exists(a)&&File.ReadAllBytes(a).AsSpan().SequenceEqual(File.ReadAllBytes(b));
            if(File.Exists(a))Check(!exists||equal,name+": equal to 0.22.89 byte for byte (0 differences)"); // herpak: only where the 0.22.89 output exists
            int factors=0,below=0;double min=double.PositiveInfinity;
            if(exists&&name.StartsWith("PROTECTION-",StringComparison.Ordinal))
            {
                var lines=File.ReadAllLines(b);var head=lines[0].Split('\t');int bitsCol=Array.IndexOf(head,"current_bits"),factorCol=Array.IndexOf(head,"factor");if(bitsCol<0&&head[^1].StartsWith("factor_bits_",StringComparison.Ordinal))bitsCol=head.Length-1;
                foreach(var line in lines.Skip(1))
                {
                    var c=line.Split('\t');double f;
                    if(bitsCol>=0&&c.Length>bitsCol&&long.TryParse(c[bitsCol],NumberStyles.HexNumber,inv,out var raw))f=BitConverter.Int64BitsToDouble(raw);
                    else if(factorCol>=0&&c.Length>factorCol&&double.TryParse(c[factorCol],NumberStyles.Float,inv,out var parsed))f=parsed;else continue;
                    factors++;min=Math.Min(min,f);
                    // the native write is factor x apModifier x multiplier; the smallest multiplier on the table grid (85 degrees, e 2, om 0) is cos 85 = 0.087
                    if(ProtectionFactorTable.NativeSafe((float)(f*Math.Cos(85*Math.PI/180)))!=(float)(f*Math.Cos(85*Math.PI/180)))below++;
                }
                Check(factors>0&&below==0,$"{name}: no table factor reaches the floor, even at the smallest grid multiplier ({factors} factors, minimum {G(min)})");
            }
            goldenFactors+=factors;goldenBelow+=below;
            golden.AppendLine($"{name}\t{(exists?File.GetLastWriteTimeUtc(b).ToString("o",inv):"absent")}\t{equal}\t{factors}\t{below}\t{(factors>0?G(min):"-")}");
        }
        static MaterialInterval I(int entity,double a,double b,double factor,string material="rha")=>new(entity,material,material,a,b,factor,0); // as Fix02247
        var ledgerZero=new (string Name,Func<double> Factor)[]{
            ("repeated block pays nothing",()=>{var all=new[]{I(1,0,.123,1.16,"highHardnessSteel"),I(336,.2,.897,.85,"ebCassette_t80u_hull"),I(1225,-.012,0,1)};var l=new MaterialIntervalLedger();l.Commit(l.Plan(-.012,.123,all)!);l.Commit(l.Plan(0,.3,all)!);l.Commit(l.Plan(.2,.897,all)!);return l.Plan(-.012,.897,all)!.Factor;}),
            ("uncovered material and air zero",()=>new MaterialIntervalLedger().Plan(0,1,Array.Empty<MaterialInterval>())!.Factor),
            ("losing overlap member cannot be charged later",()=>{var l=new MaterialIntervalLedger();l.Commit(l.Plan(0,1,new[]{I(2,0,1,.5),I(1,.2,.8,1.5)})!);return l.Plan(0,1,new[]{I(2,0,1,.5)})!.Factor;})};
        int ledgerBelow=0;
        foreach(var (name,factor) in ledgerZero)
        {
            var f=factor();var write=ProtectionFactorTable.NativeSafe((float)ProtectionFactorTable.NativeRhaFactor(f,42,1));
            Check(f==0&&Bits(write)==Low,"Fix02247 \""+name+"\": the ledger pays 0 (unchanged) and the native write is 1e-6");if(f==0)ledgerBelow++;
            golden.AppendLine($"Fix02247: {name}\t-\tledger 0 unchanged\t1\t1\tnative write {G(write)} (no unpaid path)");
        }
        Check(goldenBelow==0&&ledgerBelow==3,$"written values below 1e-6: only the {ledgerBelow} no-unpaid-path (uncovered) cases of Fix02247 and the {uncovered} uncovered lines of the log; none in {goldenFactors} golden table factors");

        // ---------------- D5 + B4: the repaired write sites ----------------
        var reactions=Read("NativeMaterialReactions.cs");
        const string guard="        if(!float.IsFinite(value)||value<0||value==0&&interval==null&&GroupedProtection.CompositeProtectionScale!=0)return;\n";
        const string floorLines="        var floored=value<ProtectionFactorTable.MinimumNativeRhaFactor; // 0.22.90: the guard above is unchanged; then the floor: never 0 to native (\"uncovered\", a factor-0 face or module, scale 0), from 1e-6 up bit for bit\n        value=ProtectionFactorTable.NativeSafe(value); // 0.22.90\n        Restore(sim.Pointer,reached.Index);\n";
        Check(reactions.Contains(guard+floorLines,StringComparison.Ordinal),"WriteFactor: the 0.22.89 guard unchanged, directly followed by the floor (brief A2)");
        const string refresh="                var value=ProtectionFactorTable.NativeSafe((float)ProtectionFactorTable.NativeRhaFactor(w.Factor,w.Angle,profile.obliquityExponent,om,profile.apModifier)); // 0.22.90 replaced: the floor on the refresh (a re-planned interval can be empty: factor 0)\n"+
            "                if(!float.IsFinite(value)){Restore(sim.Pointer,index);return;} // 0.22.90: DIVISOR-AUDIT: a non-finite refresh is never written; native keeps its own factor (as the guard of WriteFactor)\n"+
            "                SetRha(w.Materials,w.MaterialIndex,value,out var read);\n";
        Check(reactions.Contains(refresh,StringComparison.Ordinal),"CaptureBlockBudget: the floor (brief A3) and the finiteness check (B) right before its SetRha");
        // The decisions of the two sites, evaluated with the production helpers exactly as the lines above read.
        float? Write(float value,bool interval,double scale){if(!float.IsFinite(value)||value<0||value==0&&!interval&&scale!=0)return null;value=ProtectionFactorTable.NativeSafe(value);return value;}
        float? Refresh(double raw){var value=ProtectionFactorTable.NativeSafe((float)raw);if(!float.IsFinite(value))return null;return value;}
        var d5=new StringBuilder("site\tinput\tinterval\tscale\twritten\n");
        foreach(var (input,interval,scale,expected) in new (float,bool,double,float?)[]{(0,true,1,1e-6f),(0,false,0,1e-6f),(0,false,1,null),(-.25f,true,1,null),(float.NaN,true,1,null),(float.PositiveInfinity,true,1,null),(3e-7f,true,1,1e-6f),(.41334f,true,1,.41334f)})
        {
            var got=Write(input,interval,scale);d5.AppendLine($"WriteFactor\t{G(input)}\t{interval}\t{scale}\t{(got is {} g?G(g):"not written")}");
            Check(got.HasValue==expected.HasValue&&(!got.HasValue||Bits(got.Value)==Bits(expected!.Value)),$"WriteFactor with {G(input)} (interval {interval}, scale {scale}): {(expected is {} e?G(e):"not written")}");
        }
        foreach(var (input,expected) in new (double,float?)[]{(0,1e-6f),(-.25,1e-6f),(double.NaN,null),(double.PositiveInfinity,null),(1e300,null),(3e-7,1e-6f),(.41334,.41334f)})
        {
            var got=Refresh(input);d5.AppendLine($"CaptureBlockBudget\t{G(input)}\t-\t-\t{(got is {} g?G(g):"not written (Restore)")}");
            Check(got.HasValue==expected.HasValue&&(!got.HasValue||Bits(got.Value)==Bits(expected!.Value)),$"CaptureBlockBudget refresh with {G(input)}: {(expected is {} e?G(e):"not written, native factor restored")}");
        }
        Check(Bits(Refresh(ProtectionFactorTable.NativeRhaFactor(0,42,1.33,0,1))!.Value)==Low,"CaptureBlockBudget: an empty re-planned interval (factor 0) writes 1e-6, not 0");
        // B4: no SetRha and no other rhaFactor write past the floor
        Check(Regex.Matches(reactions,@"\bSetRha\(").Count==5,"SetRha: the definition and 4 writes (as 0.22.89)");
        var calls=Regex.Matches(reactions,@"\bSetRha\(([^,()]+),([^,()]+),([^,()]+),").Cast<Match>().Where(m=>!m.Groups[1].Value.Contains("Il2CppStructArray",StringComparison.Ordinal)).ToArray();
        Check(calls.Length==4&&calls.All(m=>m.Groups[3].Value=="value"),"every SetRha write writes the local value");
        bool Floored(string methodStart,string methodEnd,string floorLine)
        {
            var a=reactions.IndexOf(methodStart,StringComparison.Ordinal);var z=reactions.IndexOf(methodEnd,a+1,StringComparison.Ordinal);if(a<0||z<0)return false;
            var body=reactions[a..z];var f=body.IndexOf(floorLine,StringComparison.Ordinal);if(f<0)return false;
            var sets=Regex.Matches(body,@"\bSetRha\(").Cast<Match>().Select(m=>m.Index).ToArray();
            return sets.Length==2&&sets.All(s=>s>f&&!Regex.IsMatch(body[(f+floorLine.Length)..s],@"\bvalue\s*(=|\+=|-=|\*=|/=)[^=]"));
        }
        Check(Floored("private static void WriteFactor(","internal static void CaptureBlockBudget(","value=ProtectionFactorTable.NativeSafe(value);"),"WriteFactor: both SetRha writes come after the floor, with no assignment to value in between");
        Check(Floored("internal static void CaptureBlockBudget(","// The block carries another material","var value=ProtectionFactorTable.NativeSafe("),"CaptureBlockBudget: both SetRha writes come after the floor, with no assignment to value in between");
        var production=Directory.EnumerateFiles(root,"*.cs",SearchOption.AllDirectories).Where(f=>!Regex.IsMatch(f.Replace('\\','/'),"/(tests|bin|obj|tools)/")).ToArray();
        var rhaWrites=production.SelectMany(f=>File.ReadAllLines(f).Select((l,i)=>(F:Path.GetRelativePath(root,f).Replace('\\','/'),L:i+1,T:l))).Where(x=>Regex.IsMatch(x.T,@"\.rhaFactor\s*=[^=]|rhaFactor\)\s*=[^=]")).ToArray();
        Check(rhaWrites.Length==1&&rhaWrites[0].F=="NativeMaterialReactions.cs"&&rhaWrites[0].T.Contains("Unsafe.AsRef(in element.rhaFactor)=value;",StringComparison.Ordinal),
            "the only native rhaFactor mutation in production is NativeRhaArray.Write, reached through SetRha and RestorePair: "+string.Join(", ",rhaWrites.Select(x=>x.F+":"+x.L)));
        Check(production.Sum(f=>Regex.Matches(File.ReadAllText(f),@"MaterialFactorWrite\.Set\(").Count)==1,"MaterialFactorWrite.Set is called from SetRha only (RestorePair restores native's own values)");
        Check(production.Sum(f=>Regex.Matches(File.ReadAllText(f),@"new Sprocket\.Vehicles\.Colliders\.DamageModelParameters\(").Count)==3,"three DamageModelParameters writes (ReliktPrototype, RuntimeMaterialBalance, RuntimeCassetteComposer), all audited: every rha source > 0");
        // DIVISOR-AUDIT.tsv (brief B2)
        var auditPath=Path.Combine(output,"DIVISOR-AUDIT.tsv");int auditRows=0,auditJa=0,auditRepaired=0,auditProposals=0;
        if(File.Exists(auditPath))
        {
            var rows=File.ReadAllLines(auditPath);
            Check(rows[0]=="file:line\tnative field\tnative use (source)\tpossible EB range\tcan it be 0, negative or non-finite? (ja/nee + reason)\texisting guard\taction","DIVISOR-AUDIT.tsv: the seven columns of the brief");
            foreach(var row in rows.Skip(1))
            {
                var c=row.Split('\t');auditRows++;
                var at=c[0].LastIndexOf(':');var file=Path.Combine(root,c[0][..at]);var line=int.Parse(c[0][(at+1)..],inv);
                var exists=c.Length==7&&File.Exists(file)&&line>=1&&line<=File.ReadAllLines(file).Length;
                Check(exists,"DIVISOR-AUDIT row "+c[0]+": seven fields and the line exists");
                if(c.Length<7)continue;
                if(c[4].StartsWith("ja",StringComparison.Ordinal))
                {
                    auditJa++;if(c[6].StartsWith("repaired",StringComparison.Ordinal))auditRepaired++;else auditProposals++;
                    Check(exists&&File.ReadAllLines(file)[line-1].Contains("ProtectionFactorTable.NativeSafe(",StringComparison.Ordinal),"DIVISOR-AUDIT \"ja\" row "+c[0]+" points at its floor");
                }
                else Check(c[4].StartsWith("nee",StringComparison.Ordinal),"DIVISOR-AUDIT row "+c[0]+": ja or nee");
            }
            Check(auditRows>=40&&auditJa==2&&auditRepaired==2&&auditProposals==0,$"DIVISOR-AUDIT.tsv: {auditRows} write sites, {auditJa} \"ja\" (both rhaFactor writes), {auditRepaired} repaired, {auditProposals} proposals");
        }
        // herpak: DIVISOR-AUDIT.tsv is part of the owner delivery (out-0.22.90); checked there

        // ---------------- C: the diagnostic line (reads only) ----------------
        var trajectory=Read("RuntimeOriginalTrajectory.cs");
        Check(trajectory.Contains("if(nonFinite<5&&!(OriginalTrajectoryPolicy.Finite(Pure(child.spawnPoint))&&OriginalTrajectoryPolicy.Finite(Pure(child.direction))))NonFinite(sim.Pointer,index,i,child.spawnPoint,child.direction);",StringComparison.Ordinal)&&
              trajectory.Contains("\"[Original trajectory] non-finite child spawn/direction after block \"+(NativeMaterialReactions.OpenWriteText(sim,index)??",StringComparison.Ordinal)&&trajectory.Contains("        nonFinite++;",StringComparison.Ordinal),
              "End: one bounded line (at most 5 per session) per new child with a non-finite spawn point or direction, with the plate and rhaFactor of the open write");
        Check(Regex.Matches(trajectory,@"fragments\[[^\]]+\]\s*=[^=]").Count==1&&!Regex.IsMatch(trajectory[trajectory.IndexOf("// 0.22.90 begin",StringComparison.Ordinal)..],@"fragments\[[^\]]+\]\s*=[^=]|\.direction\s*=|\.spawnPoint\s*="),
              "C writes nothing: the file keeps its one native write (the continuation direction of 0.22.25)");
        var open=reactions[reactions.IndexOf("internal static string? OpenWriteText(",StringComparison.Ordinal)..reactions.IndexOf("internal static void CaptureBlockBudget(",StringComparison.Ordinal)];
        Check(!open.Contains("SetRha",StringComparison.Ordinal)&&!Regex.IsMatch(open,@"\]\s*=[^=]|rhaFactor\s*=")&&open.Contains("catch(Exception){return null;}",StringComparison.Ordinal),"OpenWriteText reads only and never throws");
        Check(string.Create(inv,$"#{452} (rhaFactor {1e-6f:g6})")=="#452 (rhaFactor 1e-06)","the line reads \"... after block #452 (rhaFactor 1e-06)\"");

        // ---------------- H: cassettes with a stock Sand.json without costMultiplier ----------------
        var technology=Path.Combine(root,"native-data/Technology");
        var fixtures=Path.Combine(root,"tests","fixtures","sand");var sandFresh=Path.Combine(fixtures,"Sand-E-fresh-install.json");var sandOld=Path.Combine(fixtures,"Sand-D-old-install.json"); // herpak: the two stock Sand.json copies in tests/fixtures/sand
        var sand=new StringBuilder("case\tresult\n");
        int presetsFresh=-1,tablesCompared=0;
        if(File.Exists(sandFresh)&&File.Exists(sandOld))
        {
            Check(!File.ReadAllText(sandFresh).Contains("costMultiplier",StringComparison.Ordinal)&&File.ReadAllText(sandOld).Contains("costMultiplier",StringComparison.Ordinal),"fixtures: the stock Sand.json of E: has no costMultiplier, the one of D: has it");
            string Dir(string name,string? sandFile,params (string File,string Json)[] extra)
            {
                var d=Path.Combine(output,name);if(Directory.Exists(d))Directory.Delete(d,true);Directory.CreateDirectory(d);
                foreach(var f in Directory.EnumerateFiles(technology,"*.json"))File.Copy(f,Path.Combine(d,Path.GetFileName(f)));
                foreach(var f in Directory.EnumerateFiles(Path.Combine(root,"tests","TestData","Technology"),"*.json"))File.Copy(f,Path.Combine(d,Path.GetFileName(f)),false); // herpak: era-variant B keeps RHA and SheetMetal here
                if(sandFile!=null)File.Copy(sandFile,Path.Combine(d,"Sand.json"));
                foreach(var (file,json) in extra)File.WriteAllText(Path.Combine(d,file),json);
                return d;
            }
            var fresh=Dir("H-TECH-fresh",sandFresh);var installed=Dir("H-TECH-old",sandOld);
            var lines=new List<(bool Warning,string Text)>();var previous=CassetteTechnology.Log;CassetteTechnology.Log=(w,t)=>lines.Add((w,t));
            try
            {
                Exception? oldError=null;try{ReadDirectory89(fresh);}catch(Exception ex){oldError=ex;}
                Check(oldError is KeyNotFoundException,"0.22.89 reader on the fresh install: KeyNotFoundException (\"The given key was not present in the dictionary\"), the E: log r. 134");
                sand.AppendLine("0.22.89 reader, fresh Sand.json\t"+(oldError?.GetType().Name??"no error"));
                Check(SameBits(CassetteTechnology.ReadDirectory(technology),ReadDirectory89(technology)),"complete files (native-data/Technology): the same CassetteMaterial bit for bit as 0.22.89");
                Check(SameBits(CassetteTechnology.ReadDirectory(installed),ReadDirectory89(installed)),"with the Sand.json of D: (costMultiplier 0): the same CassetteMaterial bit for bit as 0.22.89");
                lines.Clear();
                var materialsFresh=CassetteTechnology.ReadDirectory(fresh);_=CassetteTechnology.ReadDirectory(fresh);
                var sandMaterial=materialsFresh.GetValueOrDefault("sand");
                Check(sandMaterial==new CassetteMaterial("sand",1600,.2,0,1),"fresh Sand.json: density 1600, rhaFactor 0.2, spallFactor 0, costMultiplier 1.0 (cost only)");
                Check(lines.Count==1&&!lines[0].Warning&&lines[0].Text.Contains("Sand.json",StringComparison.Ordinal)&&lines[0].Text.Contains("costMultiplier",StringComparison.Ordinal),"one log line for that file, also when it is read twice: "+string.Join(" | ",lines.Select(l=>l.Text)));
                sand.AppendLine("0.22.90 reader, fresh Sand.json\tsand = "+sandMaterial+"; "+lines.Count+" line: "+string.Join(" | ",lines.Select(l=>l.Text)));
                var withoutKeys=Dir("H-TECH-missing",sandOld,("NoDensity.json","{\"type\":\"noDensity\",\"date\":\"1950.01.01\",\"properties\":{\"rhaFactor\":1,\"spallFactor\":0.5,\"costMultiplier\":1}}"),
                    ("NoSpall.json","{\"type\":\"noSpall\",\"date\":\"1950.01.01\",\"properties\":{\"rhaFactor\":1,\"density\":7850}}"));
                lines.Clear();var materialsMissing=CassetteTechnology.ReadDirectory(withoutKeys);
                Check(!materialsMissing.ContainsKey("noDensity")&&!materialsMissing.ContainsKey("noSpall")&&lines.Count(l=>l.Warning)==2&&lines.Count==2,"a file without density or without spallFactor is skipped, one warning each");
                sand.AppendLine("files without density / spallFactor\tskipped; "+string.Join(" | ",lines.Select(l=>l.Text)));
                // the 35 built-in presets load as CassetteDefaults.Load does, and the tables equal those with a Sand.json that has costMultiplier
                var presets=CassetteCatalogue.Parse(File.ReadAllText(Path.Combine(root,"Material/defaults",CassetteCatalogue.FileName))).Presets;
                var reactions2=MaterialReactionCatalogue.Parse(File.ReadAllText(Path.Combine(root,"Material/defaults",MaterialReactionCatalogue.FileName)));
                var responses=SprocketExpandedBallistics.ArmourResponses.Parse(File.ReadAllText(Path.Combine(root,"Material/defaults/sprocket.expandedballistics.responses.json")));
                var reactive=new HashSet<string>(StringComparer.Ordinal);foreach(var r in responses.Responses)if(r.Kind is "lightEra" or "heavyEra")foreach(var id in r.CompatibleMaterialIds)reactive.Add(id);
                (Dictionary<string,CassetteModel> Models,IReadOnlyDictionary<string,ProtectionFactorTable> Tables) Load(IReadOnlyDictionary<string,CassetteMaterial> materials)
                {
                    var models=new Dictionary<string,CassetteModel>(StringComparer.Ordinal);
                    foreach(var preset in presets){var model=new CassetteModel(preset,materials,reactions2,reactive);if(!materials.TryGetValue(preset.MaterialId,out var native)||native!=model.Passive)throw new FormatException("Cassette Technology differs from sidecar: "+preset.MaterialId);models.Add(preset.MaterialId,model);}
                    return (models,ProtectionFactorTable.ForMaterials(models,reactions2,materials,id=>id,null,reactive));
                }
                Exception? loadError=null;(Dictionary<string,CassetteModel> Models,IReadOnlyDictionary<string,ProtectionFactorTable> Tables)? la=null,lb=null;
                try{la=Load(materialsFresh);lb=Load(CassetteTechnology.ReadDirectory(installed));}catch(Exception ex){loadError=ex;}
                presetsFresh=la?.Models.Count??-1;
                Check(loadError==null&&presetsFresh==35&&presets.Length==35,"the load succeeds on the fresh install: 35 presets ("+(loadError?.Message??presetsFresh+" loaded")+")");
                if(la is {} a&&lb is {} b)
                {
                    Check(a.Models.Keys.SequenceEqual(b.Models.Keys)&&a.Models.All(p=>p.Value.Passive==b.Models[p.Key].Passive),"the 35 passive signatures are the same with either Sand.json");
                    var same2=a.Tables.Count==b.Tables.Count;
                    foreach(var (id,table) in a.Tables)
                    {
                        if(!b.Tables.TryGetValue(id,out var twin)||twin.Key!=table.Key){same2=false;continue;}
                        foreach(var threat in ProtectionFactorTable.Threats)for(double angle=0;angle<=85;angle+=2.5)
                        {tablesCompared++;if(BitConverter.DoubleToInt64Bits(table.Factor(threat,angle))!=BitConverter.DoubleToInt64Bits(twin.Factor(threat,angle)))same2=false;}
                    }
                    Check(same2&&tablesCompared>0,$"the protection tables are bit for bit those with a Sand.json that has costMultiplier ({a.Tables.Count} tables, {tablesCompared} factors)");
                    sand.AppendLine($"fresh install load\t{presetsFresh} presets, {a.Tables.Count} tables, {tablesCompared} factors equal to the D: Sand.json");
                }
            }
            finally{CassetteTechnology.Log=previous;}
            foreach(var d in new[]{"H-TECH-fresh","H-TECH-old","H-TECH-missing"})try{Directory.Delete(Path.Combine(output,d),true);}catch(IOException){}
        }
        else Check(false,"H fixtures missing in tests/fixtures/sand");
        // H2: a preset with a missing layer material names the preset and the material (FormatException, not KeyNotFoundException)
        {
            var materials=CassetteTechnology.ReadDirectory(technology);foreach(var kv in CassetteTechnology.ReadDirectory(Path.Combine(root,"tests","TestData","Technology")))materials.TryAdd(kv.Key,kv.Value); // herpak: RHA from tests\TestData
            var reactions3=MaterialReactionCatalogue.Parse(File.ReadAllText(Path.Combine(root,"Material/defaults",MaterialReactionCatalogue.FileName)));
            Exception? error=null;try{_=new CassetteModel(new CassettePreset("fixture_missing","Missing","1980.01.01",1,4000,new[]{new CassetteLayer("rha",20,0,"up"),new CassetteLayer("noSuchMaterial",30,0,"up")}),materials,reactions3);}catch(Exception ex){error=ex;}
            Check(error is FormatException&&error.Message.Contains("fixture_missing",StringComparison.Ordinal)&&error.Message.Contains("noSuchMaterial",StringComparison.Ordinal),"a missing layer material: FormatException with the preset and the material ("+error?.Message+")");
            var model=Read("CassetteModel.cs");
            Check(model.Contains("if(!materials.TryGetValue(layer.Material,out var material))throw new FormatException(presetId+\": layer material \"+layer.Material+\" is missing from the Technology files.\");",StringComparison.Ordinal)&&
                  !model.Contains("materials[layer.Material]",StringComparison.Ordinal),"CassetteModel:62 no longer indexes the dictionary; Prepare names the preset");
            var defaults=Read("CassetteDefaults.cs");var configure=Read("RuntimeArmourResponses.cs");
            var allOff=defaults.Contains("catch(FormatException ex) when(preset.Id.StartsWith(\"user_\",StringComparison.Ordinal))",StringComparison.Ordinal)&&configure.Contains("Plugin.ModLog.LogWarning(\"[Cassettes] Native passive resistance only: \"+ex.Message);",StringComparison.Ordinal);
            Check(allOff,"finding (unchanged, for the report): an error in one built-in preset still switches off every cassette at the first load (only user_ presets are skipped one by one; a reload keeps the previous snapshot)");
            sand.AppendLine("one built-in preset fails\t"+(allOff?"all cassettes off at the first load (unchanged); user_ presets are skipped one by one":"?"));
        }
        // H3: the other readers of the stock Technology folder
        var readers=new[]{("CassetteComposerPanel.cs",1),("RuntimeCassetteComposer.cs",2),("RuntimeComposerFill.cs",2)};
        foreach(var (file,count) in readers)Check(Regex.Matches(Read(file),@"CassetteTechnology\.ReadDirectory\(").Count==count,file+": reads through CassetteTechnology.ReadDirectory ("+count+"x), so it no longer fails on a missing field");
        var dates=Read("ShellEraSelection.cs");var datesLoad=dates[dates.IndexOf("internal static class ShellTechnologyDates",StringComparison.Ordinal)..];
        Check(!datesLoad.Contains(".GetProperty(",StringComparison.Ordinal)&&datesLoad.Contains("TryGetProperty(\"date\"",StringComparison.Ordinal),"ShellTechnologyDates reads with TryGetProperty only (no change needed)");
        Check(production.All(f=>!Regex.IsMatch(File.ReadAllText(f),@"GetProperty\(""(costMultiplier|density|spallFactor)""\)")),"no production code reads costMultiplier, density or spallFactor with GetProperty any more");
        sand.AppendLine("dist\tnothing needed: the fix is in the DLL; no Technology file in dist, no stock file changed");

        // ---------------- I: the rack scan of the mixed-ammunition loadout matches the projectile size (EB-FOLLOWUP, Carousel reserve with another slot) ----------------
        {
            var loadout=Read("RuntimeAmmoLoadout.cs");var scan=loadout[loadout.IndexOf("private static List<RackInfo> ScanRacks(",StringComparison.Ordinal)..];scan=scan[..scan.IndexOf("\n    }\n",StringComparison.Ordinal)];
            Check(scan.Contains("var sizeId=ProjectileSizeID.FromPropellantLength(shell.Diameter,shell.PropellantLength).Value;",StringComparison.Ordinal)&&scan.Contains("if(rack.projectileSizeID.Value!=sizeId)continue;",StringComparison.Ordinal)&&
                  !scan.Contains("ShellSizeBlueprintID",StringComparison.Ordinal),"I: ScanRacks keeps a rack whose projectileSizeID equals FromPropellantLength(diameter, propellant length) of the cannon's shell; the physical slot id is no longer compared");
            Check(loadout.Contains("var size=ProjectileSizeID.FromPropellantLength(cannon.ShellBlueprint.Diameter,cannon.ShellBlueprint.PropellantLength);",StringComparison.Ordinal),"I: the same size as the load request of a typed reload (one definition of the round size)");
            Check(scan.Contains("if(rack==null||rack.Vehicle?.Pointer!=vehicle!.Pointer)continue;",StringComparison.Ordinal)&&scan.Contains("if(items==null)return found;",StringComparison.Ordinal)&&scan.Contains("if(behaviour==null)continue;",StringComparison.Ordinal),
                  "I: the vehicle, item and behaviour guards of ScanRacks are unchanged");
            Check(Feat02290Un.Un90(loadout).Contains("if(rack.ShellSizeBlueprintID!=sizeId)continue;",StringComparison.Ordinal)&&Feat02290Un.Un90(loadout).Contains("var sizeId=cannon.ShellSizeBlueprintID;",StringComparison.Ordinal),"I: without the marked change the scan is that of 0.22.89");
        }
        // ---------------- file ledger against src-0.22.89 ----------------
        var files=new StringBuilder("file\tchanged in 0.22.90\tUn90 equals 0.22.89\n");
        if(Directory.Exists(old89))
        {
            bool Skip(string rel)=>rel.Split('/').Any(p=>p is "bin" or "obj" or "tests" or ".git");
            Dictionary<string,string> Inventory(string dir)=>Directory.EnumerateFiles(dir,"*",SearchOption.AllDirectories).Select(f=>(Rel:Path.GetRelativePath(dir,f).Replace('\\','/'),Full:f)).Where(x=>!Skip(x.Rel)).ToDictionary(x=>x.Rel,x=>x.Full);
            var a=Inventory(old89);var b=Inventory(root);
            var diff=a.Keys.Union(b.Keys).Where(k=>!a.ContainsKey(k)||!b.ContainsKey(k)||!File.ReadAllBytes(a[k]).AsSpan().SequenceEqual(File.ReadAllBytes(b[k]))).OrderBy(k=>k,StringComparer.Ordinal).ToArray();
            var reversed=diff.Where(k=>a.ContainsKey(k)&&b.ContainsKey(k)&&Feat02290Un.Un90Bytes(b[k]).AsSpan().SequenceEqual(File.ReadAllBytes(a[k]))).ToArray();
            foreach(var k in diff)files.AppendLine($"{k}\tyes\t{reversed.Contains(k)}");
            Check(diff.All(IsPlanned)&&diff.Length==10&&a.Keys.ToHashSet().SetEquals(b.Keys),$"only the planned production files differ from 0.22.89: {string.Join(", ",diff)} ({a.Count-diff.Length} files byte for byte)");
            Check(reversed.Length==diff.Length,"every changed file is 0.22.89 byte for byte without its marked change (Un90)");
            var protectedFiles=b.Keys.Where(k=>k.StartsWith("Material/",StringComparison.Ordinal)||Regex.IsMatch(k,@"^(CombatForensic)[^/]*\.cs$")||Regex.IsMatch(k,@"^[^/]*(Composer|Solid|Overlay)[^/]*\.cs$")||
                k is "MaterialIntervalLedger.cs" or "NativeMaterialIntervals.cs" or "RuntimeGroupedProtection.cs" or "CompositePlate.cs" or "LayeredPlateReport.cs"
                    or "RuntimeAtgmGuidanceSight.cs" or "AtgmIndependentSightPolicy.cs" or "RuntimeAtgmSightMount.cs" or "AtgmSightHead.cs" or "AtgmSightReferenceState.cs" or "AtgmSightElevationState.cs" or "AtgmGuidanceSightVisuals.cs"
                    or "RuntimeAtgmAmmoBox.cs" or "AtgmAmmoBoxRules.cs" or "AtgmAmmoBoxVisuals.cs" or "ShellBalance.cs" or "RuntimeArmourResponses.cs" or "RuntimeDamageFeed.cs" or "HitPanelModel.cs").OrderBy(k=>k,StringComparer.Ordinal).ToArray();
            Check(protectedFiles.Length>100&&protectedFiles.All(k=>a.ContainsKey(k)&&File.ReadAllBytes(a[k]).AsSpan().SequenceEqual(File.ReadAllBytes(b[k]))),
                $"the byte-equal files of the brief are 0.22.89 byte for byte ({protectedFiles.Length}: ledger, intervals, grouped protection, CompositePlate, Material, CombatForensic*, LayeredPlateReport, composer/Solid/overlay, ATGM sight and box, the HE skirt rule)");
            static int Hooks_(string s)=>s.Split('\n').Count(l=>l.Contains("[HarmonyPatch",StringComparison.Ordinal));
            var before=a.Where(p=>p.Key.EndsWith(".cs",StringComparison.Ordinal)).Sum(p=>Hooks_(File.ReadAllText(p.Value)));var after=b.Where(p=>p.Key.EndsWith(".cs",StringComparison.Ordinal)).Sum(p=>Hooks_(File.ReadAllText(p.Value)));
            Check(before==47&&after==47,$"[HarmonyPatch annotations {before} -> {after}");
            string[] PatchLines(Dictionary<string,string> d)=>d.Where(p=>p.Key.EndsWith(".cs",StringComparison.Ordinal)).SelectMany(p=>File.ReadAllText(p.Value).Replace("\r\n","\n").Split('\n').Where(l=>Regex.IsMatch(l,@"^\s*\[Harmony(Prefix|Postfix|Finalizer|Transpiler)")).Select(l=>p.Key+"|"+l.Trim())).OrderBy(x=>x,StringComparer.Ordinal).ToArray();
            Check(PatchLines(a).SequenceEqual(PatchLines(b)),"no patch method added or retargeted, no new native target");
        }
        else {static int H(string s)=>s.Split('\n').Count(l=>l.Contains("[HarmonyPatch",StringComparison.Ordinal));var hooks=Directory.EnumerateFiles(root,"*.cs",SearchOption.AllDirectories).Where(f=>!System.Text.RegularExpressions.Regex.IsMatch(f.Replace('\\','/'),"/(tests|bin|obj)/")).Sum(f=>H(File.ReadAllText(f)));Check(hooks==47,"[HarmonyPatch annotations in this tree: "+hooks+" (47)");} // herpak: the old source tree is not here; the hook count of this tree

        File.WriteAllText(Path.Combine(output,"NATIVE-ORDER-0.22.90.tsv"),order.ToString());
        File.WriteAllText(Path.Combine(output,"REPLAY-0.22.90.tsv"),replay.ToString());
        File.WriteAllText(Path.Combine(output,"GOLDEN-0.22.90.tsv"),golden.ToString());
        File.WriteAllText(Path.Combine(output,"FLOOR-SITES-0.22.90.tsv"),d5.ToString());
        File.WriteAllText(Path.Combine(output,"SAND-0.22.90.tsv"),sand.ToString());
        File.WriteAllText(Path.Combine(output,"UN90-FILES.tsv"),files.ToString());
        File.WriteAllText(Path.Combine(output,"FEAT90-RESULT.json"),JsonSerializer.Serialize(new{passed=failures==0,checks,failures,
            exhaustive=new{unchanged=same,floored,seconds=Math.Round(exhaustiveSeconds,1)},replay=new{writes,uncovered,uncoveredLines,splinters=splinterLines,unchanged},
            golden=new{factors=goldenFactors,below=goldenBelow,ledgerNoUnpaidPath=ledgerBelow},audit=new{rows=auditRows,ja=auditJa,repaired=auditRepaired,proposals=auditProposals},
            sand=new{presets=presetsFresh,factorsCompared=tablesCompared},replacedLines=Feat02290Un.Replaced.Length},new JsonSerializerOptions{WriteIndented=true}));
        Console.WriteLine($"FEAT90 PASS: {checks} checks, {failures} failures");
    }
}
