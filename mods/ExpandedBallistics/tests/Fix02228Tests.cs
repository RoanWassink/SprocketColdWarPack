using System.Globalization;
using System.Numerics;
using System.Text;
using System.Text.Json.Nodes;
namespace SprocketExpandedBallistics;
internal static class Fix02228Tests
{
    internal static string SourceRoot()
    {
        for(var d=new DirectoryInfo(AppContext.BaseDirectory);d!=null;d=d.Parent)
            if(File.Exists(Path.Combine(d.FullName,"SprocketExpandedBallistics.csproj")))return d.FullName;
        throw new Exception("Source directory unavailable");
    }
    /// <summary>Scratch and evidence folder of the test run: EB_TEST_OUT, else bin	est-output next to the project (never inside the sources).</summary>
    internal static string OutputRoot()
    {
        var env=Environment.GetEnvironmentVariable("EB_TEST_OUT");
        var path=Path.GetFullPath(string.IsNullOrEmpty(env)?Path.Combine(SourceRoot(),"bin","test-output"):env);
        Directory.CreateDirectory(path);return path;
    }
    /// <summary>Recorded combat-report sessions (last-session.json) the forensic tests replay.</summary>
    internal static string SessionRoot()=>Path.Combine(SourceRoot(),"tests","fixtures","sessions");
    internal static string FixtureRoot()
    {
        var path=Path.Combine(OutputRoot(),"managed-fixtures");
        Directory.CreateDirectory(path);return path;
    }
    private struct Material {internal float Rha,Density,Spall;}
    private readonly struct ValueArray:IRhaArray
    {
        internal readonly Material[] Values;
        internal ValueArray(Material[] values)=>Values=values;
        public float Read(int i)=>Values[i].Rha;
        public void Write(int i,float value){var copy=Values[i];copy.Rha=value;Values[i]=copy;}
    }
    internal static void Run(Action<bool,string> check)
    {
        int count=0;var original=check;check=(ok,text)=>{original(ok,"fix02228: "+text);count++;};
        var root=SourceRoot();var output=Fix02228Tests.OutputRoot();
        var materials=CassetteTechnology.ReadDirectory(Path.Combine(AppContext.BaseDirectory,"NativeData"));
        var reactions=MaterialReactionCatalogue.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory,MaterialReactionCatalogue.FileName)));
        var presets=CassetteCatalogue.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory,CassetteCatalogue.FileName))).Presets;
        var composer=new CassetteComposer(materials,reactions,"","",_=>false);
        var all=new Dictionary<string,CassetteModel>();
        var rows=new StringBuilder("preset\tthreat\tangle\tside\tdepth_mm\tpath_mm\tfactor\tL_mm_RHA\n");
        string F(double v)=>v.ToString("R",CultureInfo.InvariantCulture);
        foreach(var preset in presets)
        {
            var model=new CassetteModel(preset,materials,reactions);all.Add(preset.MaterialId,model);
            var table=ProtectionFactorTable.Build(model);var chart=composer.ProtectionCurve(preset);var preview=composer.Preview(preset);
            check(chart.Length==36,"36 signed chart points "+preset.Id);
            foreach(var c in chart)foreach(var threat in ProtectionFactorTable.Threats)
            {
                var mm=threat switch{ProtectionThreat.LongRod=>c.KeMm,ProtectionThreat.Jet=>c.HeatMm,ProtectionThreat.FullBoreKE=>c.FullBoreMm,_=>c.HeshMm};
                check(mm==table.ProtectionMm(threat,c.Angle),"graph/table identity "+preset.Id+" "+threat+" "+c.Angle);
                check(double.IsFinite(mm)&&mm>0,"finite positive protection");
                rows.AppendLine($"{preset.Id}\t{threat}\t{F(c.Angle)}\t{(c.WithTilt?"with":"against")}\t{F(model.DepthMm)}\t{F(ProtectionFactorTable.PathMm(model.DepthMm,c.Angle))}\t{F(table.Factor(threat,c.Angle))}\t{F(mm)}");
            }
            foreach(var c in preview.Protection)check(chart.Any(x=>x==c),"selected angle preview equals chart");
            foreach(var threat in ProtectionFactorTable.Threats)foreach(var side in new[]{true,false})for(int i=0;i<17;i++)
            {
                var a=i*5d;var f=table.Factor(threat,a+2.5);
                check(Math.Abs(f-(table.Factor(threat,a)+table.Factor(threat,a+5))*.5)<1e-12,"linear lookup midpoint");
            }
            check(ReferenceEquals(table,ProtectionFactorTable.Build(model)),"recipe hash cache reuse");
            check(table.Factor(ProtectionThreat.LongRod,90)==table.Factor(ProtectionThreat.LongRod,85),"lookup clamps beyond 85");
            var renamed=new CassetteModel(preset with{Label=preset.Label+" renamed"},materials,reactions);
            check(ProtectionFactorTable.Build(renamed).Label.EndsWith(" renamed",StringComparison.Ordinal),"cache preserves display identity");
        }
        File.WriteAllText(Path.Combine(output,"PROTECTION-TABLES.tsv"),rows.ToString());
        var view=new CassetteComposerModel(composer,materials.Keys.Select(id=>new ComposerMaterial(id,id,"Other")).ToArray(),Array.Empty<ComposerMaterial>(),()=>presets,_=>false,_=>true,false);
        check(view.LoadPreset(presets[0].Id),"composer view loads");
        var viewTable=composer.Table(presets[0]);
        for(int angle=0;angle<3;angle++)foreach(var threat in ProtectionFactorTable.Threats)
        {
            view.AngleIndex=angle;var display=view.ProtectionRows(threat)[0];
            var mm=viewTable.ProtectionMm(threat,CassetteComposerModel.Angles[angle]);
            check(Math.Abs(double.Parse(display.With,CultureInfo.InvariantCulture)-mm)<=.5,"four-threat UI table rounds the game value only");
        }

        var cases=new[]{("leopard2a7_wedge",68.8,795.7),("leopard2a7_wedge",65.8,555.9),("oplot",22.3,850/Math.Cos(22.3*Math.PI/180)),("leopard2a4_hull",50.8,335.4),("t80u_hull",62d,250/Math.Cos(62*Math.PI/180))};
        var expected=new StringBuilder("preset\tangle\tfixture_path_mm\tthreat\tside\tfactor\tL_mm_RHA\n");
        foreach(var (id,angle,path) in cases)foreach(var threat in ProtectionFactorTable.Threats)foreach(var side in new[]{true,false})
        {
            var table=ProtectionFactorTable.Build(all["ebCassette_"+id]);var factor=table.Factor(threat,angle);
            foreach(var e in new[]{0d,1,1.15,1.33,1.6})foreach(var om in new[]{0d,.07,.57,1})
            {
                var written=ProtectionFactorTable.NativeRhaFactor(factor,angle,e,om);
                var native=path*written/ProtectionFactorTable.NativeAngleMultiplier(angle,e,om);
                check(Math.Abs(native-path*factor)<1e-8,"native L=path*factor "+id+" e="+e+" om="+om);
                if(e==1)check(written==factor,"LOS writes the factor itself");
            }
            expected.AppendLine($"{id}\t{F(angle)}\t{F(path)}\t{threat}\t{(side?"with":"against")}\t{F(factor)}\t{F(path*factor)}");
        }
        File.WriteAllText(Path.Combine(output,"EXPECTED-CASES.tsv"),expected.ToString());
        foreach(var angle in new[]{0d,52,67,82})check(Math.Abs(ProtectionFactorTable.NativeRhaFactor(1,angle,1)-1)<1e-12,"RHA factor unchanged");
        var settings=new SpallSettings();
        File.WriteAllText(Path.Combine(output,"spall.default.json"),SpallBalance.ToJson(settings));
        check(settings.ApfsdsObliquityExponent==1&&settings.HeatObliquityExponent==1&&settings.HeshObliquityExponent==0,"new defaults, native HESH");
        check(SpallBalance.ObliquityExponent("ap",null,settings)==1.33&&settings.ApdsObliquityExponent==1&&settings.ApcbcObliquityExponent==1.15&&settings.ApcrObliquityExponent==1.6,"AP family unchanged");
        foreach(var b in new[]{"heat","atgm","atgm_gun"})check(SpallBalance.ObliquityExponent(b,null,settings)==1,"all HEAT carriers LOS");
        var json=JsonNode.Parse(SpallBalance.ToJson(settings))!;json["obliquityCorrection"]=new JsonObject{["unknownOldOption"]="ignored"};
        check(SpallBalance.Parse(json.ToJsonString())==SpallBalance.Parse(SpallBalance.ToJson(settings)),"obsolete correction config ignored");
        check(ProtectionFactorTable.ThreatOf(new("apfsds","unknown"))==ProtectionThreat.LongRod,"APFSDS bucket");
        foreach(var b in new[]{"heat","atgm","atgm_gun"})check(ProtectionFactorTable.ThreatOf(new(b,"unknown"))==ProtectionThreat.Jet,"jet bucket");
        foreach(var kind in new[]{"ap","apc","apcbc","apds","apcr"})check(ProtectionFactorTable.ThreatOf(new("ap","unknown",null,null,true,kind))==ProtectionThreat.FullBoreKE,"AP family bucket");
        check(ProtectionFactorTable.ThreatOf(new("hesh","unknown"))==ProtectionThreat.Hesh&&ProtectionFactorTable.ThreatOf(new("he","unknown"))==null&&ProtectionFactorTable.ThreatOf(new("heat","unknown",Original:false))==null,"HESH/HE/fragment dispatch");
        var tables=ProtectionFactorTable.ForMaterials(all,reactions,materials,id=>id);
        foreach(var (id,binding) in reactions.Bindings)
        {
            if(!materials.ContainsKey(id)||reactions.Recipes[binding.RecipeId].Mode=="fragmentAbsorber")continue;
            var loose=new CassetteModel(new("loose_"+id,id,"1900.01.01",1,4000,new[]{new CassetteLayer(id,100,0,"up")}),materials,reactions);
            check(tables[id].Key==ProtectionFactorTable.Build(loose).Key,"loose material equals single layer "+id);
        }
        check(!tables.ContainsKey("rha"),"ordinary RHA has no table override");
        var changePreset=presets.First(p=>new CassetteModel(p,materials,reactions).Layers.Any(l=>l.Recipe!=null));
        var changeModel=new CassetteModel(changePreset,materials,reactions);var recipeId=changeModel.Layers.First(l=>l.Recipe!=null).Recipe!.Id;
        var edited=reactions with{Recipes=reactions.Recipes.ToDictionary(x=>x.Key,x=>x.Key==recipeId?x.Value with{ThreatTags=new[]{"futureThreat"}}:x.Value)};
        check(ProtectionFactorTable.KeyOf(changeModel)!=ProtectionFactorTable.KeyOf(new(changePreset,materials,edited)),"array contents invalidate recipe hash");
        var culture=CultureInfo.CurrentCulture;
        try{CultureInfo.CurrentCulture=CultureInfo.GetCultureInfo("en-US");var key=ProtectionFactorTable.KeyOf(changeModel);CultureInfo.CurrentCulture=CultureInfo.GetCultureInfo("nl-NL");check(key==ProtectionFactorTable.KeyOf(changeModel),"recipe hash culture independent");}
        finally{CultureInfo.CurrentCulture=culture;}
        var values=new[]{new Material{Rha=.504f,Density=4500,Spall=.3f},new Material{Rha=1,Density=7850}};var adapter=new ValueArray(values);
        var copy=values[0];copy.Rha=9;check(values[0].Rha==.504f,"MaterialInfo stand-in read is a copy");
        var old=MaterialFactorWrite.Set(ref adapter,0,.73f,out var readBack);
        check(old==.504f&&readBack==.73f&&values[0].Density==4500&&values[0].Spall==.3f,"write/read-back preserves other MaterialInfo fields");
        try{throw new InvalidOperationException("native block stand-in failure");}catch(InvalidOperationException){}
        finally{MaterialFactorWrite.Set(ref adapter,0,old,out readBack);}
        check(readBack==.504f&&values[0].Rha==.504f&&values[1].Rha==1,"finalizer restore shared element before next block");
        var alloc=GC.GetAllocatedBytesForCurrentThread();for(int i=0;i<10000;i++)MaterialFactorWrite.Set(ref adapter,0,old,out _);
        check(GC.GetAllocatedBytesForCurrentThread()-alloc==0,"value-array adapter allocates no closures");
        var runtime=File.ReadAllText(Path.Combine(root,"NativeMaterialReactions.cs"));var hooks=File.ReadAllText(Path.Combine(root,"RuntimeArmourResponses.cs"));
        check(runtime.Contains("values[index]=element;")&&runtime.Contains("MaterialFactorWrite.Set(ref array")&&runtime.Contains("=>values[index].rhaFactor"),"native adapter explicit value-array write and read");
        check(hooks.Contains("finally")&&hooks.Contains("NativeMaterialReactions.Close(current.Sim,current.Index,current.CountBefore)"),"native finalizer restores on every exit");
        check(hooks.Contains("if(context==null")&&hooks.Contains("OriginalPenetrator"),"vanilla Impact null bypasses table scope");
        foreach(var file in new[]{"RuntimeObliquityCorrection.cs","RuntimeResidualPenetrator.cs","ResidualPenetratorModel.cs","RodOrientationModel.cs","RodCoherentFragments.cs","EraRodInteractionPolicy.cs","CassetteChain.cs","NativeSecondaryEnergy.cs","ObliquityCorrection.cs"})check(!File.Exists(Path.Combine(root,file)),"obsolete layer absent "+file);
        Console.WriteLine($"0.22.28 checks PASS: {count} checks; inherited migration checks; DLL-only installer: test-output/Test-Installer.ps1.");
    }
}
