using System.Globalization;
using System.Text;
using System.Text.Json;
namespace SprocketExpandedBallistics;

/// <summary>
/// 0.22.20: calibration of the protection numbers of the non-steel materials (ceramic, composite, textolite, NERA, heavy metal).
/// The same preview as the composer table (CassetteComposer.Preview) is the measuring instrument: per preset the KE and HEAT
/// protection in mm RHA at 0/30/60 degrees, divided by the LOS thickness (x LOS) and by the same-mass RHA (x mass).
/// </summary>
internal static class Fix02220Tests
{
    internal static readonly double[] Angles={0,30,60};
    internal sealed record Row(CassettePreset Preset,string Category,double DepthMm,double KgM2,double[] KeWith,double[] KeAgainst,double[] HeatWith,double[] HeatAgainst,double ReferenceMm,double[] RefWith,double[] RefAgainst,bool Hybrid);

    private static readonly string[] Ceramics={"ceramicArray","aluminaCeramic","siliconCarbide","boronCarbide"};
    private static readonly string[] HeavyMetals={"duMesh","heavyAlloyInsert"};
    private static readonly string[] Glass={"cwepGlassTextolite","quartzFiller"};
    internal static string Category(CassettePreset preset)
    {
        var ids=preset.Layers.Select(l=>l.Material).ToHashSet(StringComparer.Ordinal);
        if(ids.Overlaps(Ceramics))return "ceramic";
        if(ids.Overlaps(HeavyMetals))return "heavy metal";
        if(ids.Overlaps(Glass))return "textolite/glass";
        if(ids.Contains("elastomerNERA"))return "NERA";
        return "steel/other";
    }
    internal static readonly string[] Categories={"ceramic","heavy metal","textolite/glass","NERA","steel/other"};

    /// <summary>Preview of every preset against the given Technology directory and reaction catalogue.</summary>
    internal static List<Row> Measure(string technologyDir,string reactionsJson,IEnumerable<CassettePreset> presets)
    {
        var materials=CassetteTechnology.ReadDirectory(technologyDir);var reactions=MaterialReactionCatalogue.Parse(reactionsJson);
        var composer=new CassetteComposer(materials,reactions,"","",_=>false);var rows=new List<Row>();
        foreach(var preset in presets)
        {
            var p=composer.Preview(preset);
            double[] Pick(Func<CassetteProtection,double> value,bool with)=>Angles.Select(a=>value(p.Protection.Single(x=>x.Angle==a&&x.WithTilt==with))).ToArray();
            var model=new CassetteModel(preset with{MinimumDepthMm=1,MaximumDepthMm=4000},materials,reactions);
            double reference=ReferenceLengthMm(p.DepthMm);
            double[] Ref(bool with)=>Angles.Select(a=>KeProtection(model,a,with,reference)).ToArray();
            var hybrid=preset.Layers.Any(l=>l.Material=="elastomerNERA")&&Category(preset)!="NERA";
            rows.Add(new(preset,Category(preset),p.DepthMm,p.KgM2,Pick(x=>x.KeMm,true),Pick(x=>x.KeMm,false),Pick(x=>x.HeatMm,true),Pick(x=>x.HeatMm,false),reference,Ref(true),Ref(false),hybrid));
        }
        return rows;
    }
    /// <summary>
    /// The composer table's reference rod is 600 mm long, so its KE saturates at 600 mm and hides thick presets. The calibration
    /// therefore also reads KE against a reference rod of L = max(600 mm, 4 x D) (same 20 mm diameter, 17500 kg/m3, 1600 m/s,
    /// the same conversion): at 60 degrees the LOS is 2 D, so even a x LOS of 1.5 stays below 0.75 L.
    /// </summary>
    internal static double ReferenceLengthMm(double depthMm)=>Math.Max(600,4*depthMm);
    internal static double KeProtection(CassetteModel model,double angle,bool with,double referenceMm)
    {
        const double diameter=.02,speed=1600,density=17500;
        double energy=.5*density*(referenceMm/1000)*speed*speed*Math.PI*.25*diameter*diameter;
        var r=angle*Math.PI/180;var ray=new System.Numerics.Vector3(0,(float)(Math.Sin(r)*(with?1:-1)),(float)-Math.Cos(r));
        var rod=model.Evaluate(model.DepthMm,ray,System.Numerics.Vector3.UnitZ,System.Numerics.Vector3.UnitY,System.Numerics.Vector3.UnitX,"longRod",diameter,energy,true);
        double native=model.DepthMm*model.Passive.RhaFactor/Math.Cos(r);
        return referenceMm*(1-Math.Pow(Math.Max(0,Math.Pow(Math.Max(0,1-native/referenceMm),2/1.43)-rod.TotalRodWorkJ/energy),1.43/2));
    }
    /// <summary>
    /// Element-level reference panels (steel face, 50 mm of the material, steel back plate; a NERA module with its air gap): what one
    /// material delivers on its own, for comparison with public thickness/mass efficiencies.
    /// </summary>
    internal static List<CassettePreset> ReferencePanels()
    {
        CassettePreset Panel(string material,double mm)=>new("panel_"+material,"Panel "+material,"2000.01.01",1,4000,new[]{new CassetteLayer("rha",10,0,"up"),new(material,mm,0,"up"),new("rha",15,0,"up")});
        var list=new List<CassettePreset>();
        foreach(var m in new[]{"aluminaCeramic","ceramicArray","siliconCarbide","boronCarbide","cwepGlassTextolite","quartzFiller","duMesh","heavyAlloyInsert"})list.Add(Panel(m,50));
        list.Add(new("panel_nera_module","Panel NERA module","2000.01.01",1,4000,new[]{new CassetteLayer("rha",3,30,"up"),new("elastomerNERA",15,30,"up"),new("rha",3,30,"up"),new("air",20,0,"up")}));
        return list;
    }
    internal static double PerLos(double protection,double depth,double angle)=>protection/(depth/Math.Cos(angle*Math.PI/180));
    internal static double PerMass(double protection,double kg,double angle)=>protection/(kg*1000/7850/Math.Cos(angle*Math.PI/180));

    private static string F(double x)=>x.ToString("0.00",CultureInfo.InvariantCulture);
    private static string F0(double x)=>x.ToString("0",CultureInfo.InvariantCulture);
    internal static double Median(IEnumerable<double> values){var v=values.OrderBy(x=>x).ToArray();return v.Length==0?double.NaN:v.Length%2==1?v[v.Length/2]:(v[v.Length/2-1]+v[v.Length/2])/2;}

    /// <summary>Markdown tables: one block per category, KE and HEAT at 0/30/60 (with tilt), mm, x LOS and x mass.</summary>
    internal static string Report(string title,List<Row> rows,string note)
    {
        var sb=new StringBuilder();
        sb.AppendLine("# "+title).AppendLine();
        sb.AppendLine(note).AppendLine();
        sb.AppendLine("Measuring instrument: `CassetteModel.Evaluate`, the same evaluation as the composer's protection table (`CassetteComposer.Preview`): native resistance (nominal rhaFactor x depth / cos) plus the shared recipe work, 20 mm rod at 1600 m/s (17500 kg/m3) and the 800 mm jet convention. Angles are from the plate normal, 'with tilt' side. x LOS = protection / (depth / cos). x mass = protection / (kg per m2 / 7850 kg/m3 / cos).").AppendLine();
        sb.AppendLine("KE is read against a reference rod of L = max(600 mm, 4 x D), so the 600 mm saturation of the composer table cannot hide thick presets (the composer's own 600 mm value is in the last column). A '*' after the id marks a hybrid: a composite core followed by NERA/air modules, whose air gaps and 60 degree module tilt dilute x LOS.").AppendLine();
        foreach(var category in Categories)
        {
            var list=rows.Where(r=>r.Category==category).ToList();if(list.Count==0)continue;
            sb.AppendLine("## "+category+" ("+list.Count+")").AppendLine();
            sb.AppendLine("| Preset | D mm | kg/m2 | KE mm 0/30/60 | KE xLOS 0/30/60 | KE xmass 0/30/60 | HEAT mm 0/30/60 | HEAT xLOS 0/30/60 | HEAT xmass 0/30/60 | KE xLOS against tilt 0/60 | composer KE (600 mm) 0/30/60 |");
            sb.AppendLine("|---|---:|---:|---|---|---|---|---|---|---|---|");
            foreach(var r in list)
            {
                string Mm(double[] v)=>string.Join("/",v.Select(F0));
                string Los(double[] v)=>string.Join("/",v.Select((x,i)=>F(PerLos(x,r.DepthMm,Angles[i]))));
                string Mass(double[] v)=>string.Join("/",v.Select((x,i)=>F(PerMass(x,r.KgM2,Angles[i]))));
                sb.AppendLine($"| {r.Preset.Id}{(r.Hybrid?"*":"")} | {F0(r.DepthMm)} | {F0(r.KgM2)} | {Mm(r.RefWith)} | {Los(r.RefWith)} | {Mass(r.RefWith)} | {Mm(r.HeatWith)} | {Los(r.HeatWith)} | {Mass(r.HeatWith)} | {F(PerLos(r.RefAgainst[0],r.DepthMm,0))}/{F(PerLos(r.RefAgainst[2],r.DepthMm,60))} | {Mm(r.KeWith)} |");
            }
            sb.AppendLine();
        }
        sb.AppendLine("## min / median / max per category (min / median / max)").AppendLine();
        sb.AppendLine("| Category | n | KE xLOS 0 | KE xLOS 30 | KE xLOS 60 | KE xmass 0 | KE xmass 60 | HEAT xLOS 0 | HEAT xLOS 60 | HEAT xmass 0 | HEAT xmass 60 |");
        sb.AppendLine("|---|---:|---|---|---|---|---|---|---|---|---|");
        void Summary(string label,List<Row> list)
        {
            if(list.Count==0)return;
            string S(Func<Row,double> f){var v=list.Select(f).ToArray();return F(v.Min())+" / "+F(Median(v))+" / "+F(v.Max());}
            sb.AppendLine($"| {label} | {list.Count} | {S(r=>PerLos(r.RefWith[0],r.DepthMm,0))} | {S(r=>PerLos(r.RefWith[1],r.DepthMm,30))} | {S(r=>PerLos(r.RefWith[2],r.DepthMm,60))} | {S(r=>PerMass(r.RefWith[0],r.KgM2,0))} | {S(r=>PerMass(r.RefWith[2],r.KgM2,60))} | {S(r=>PerLos(r.HeatWith[0],r.DepthMm,0))} | {S(r=>PerLos(r.HeatWith[2],r.DepthMm,60))} | {S(r=>PerMass(r.HeatWith[0],r.KgM2,0))} | {S(r=>PerMass(r.HeatWith[2],r.KgM2,60))} |");
        }
        foreach(var category in Categories)Summary(category,rows.Where(r=>r.Category==category).ToList());
        Summary("compact composites (ceramic, heavy metal, textolite/glass without NERA modules)",rows.Where(r=>IsCompact(r)).ToList());
        Summary("hybrids (composite core plus NERA/air modules)",rows.Where(r=>r.Hybrid).ToList());
        sb.AppendLine();
        return sb.ToString();
    }
    /// <summary>A composite or ceramic preset without NERA modules: the set that must reach x LOS 0.95 against KE.</summary>
    internal static bool IsCompact(Row r)=>r.Category is "ceramic" or "heavy metal" or "textolite/glass"&&!r.Hybrid;
    /// <summary>Machine-readable copy (id;category;hybrid;D;kg;reference;KE0/30/60;KE against 0/30/60;HEAT with;HEAT against), invariant culture.</summary>
    internal static string Csv(List<Row> rows)
    {
        var sb=new StringBuilder("id;category;hybrid;depth;kg;ref;ke0;ke30;ke60;kea0;kea30;kea60;heat0;heat30;heat60;heata0;heata30;heata60"+"\n");
        foreach(var r in rows)
            sb.AppendLine(string.Join(";",new object[]{r.Preset.Id,r.Category,r.Hybrid?1:0}.Concat(new[]{r.DepthMm,r.KgM2,r.ReferenceMm}.Concat(r.RefWith).Concat(r.RefAgainst).Concat(r.HeatWith).Concat(r.HeatAgainst).Select(x=>(object)x.ToString("R",CultureInfo.InvariantCulture)))));
        return sb.ToString();
    }

    private static string? SourceRoot()
    {
        for(var dir=new DirectoryInfo(AppContext.BaseDirectory);dir!=null;dir=dir.Parent)
            if(File.Exists(Path.Combine(dir.FullName,"SprocketExpandedBallistics.csproj")))return dir.FullName;
        return null;
    }
    internal static void Run(Action<bool,string> check)
    {
        int count=0;var assert=check;check=(ok,text)=>{assert(ok,"fix02220: "+text);count++;};
        var root=AppContext.BaseDirectory;var tech=Path.Combine(root,"NativeData");
        var reactionsJson=File.ReadAllText(Path.Combine(root,MaterialReactionCatalogue.FileName));
        var reactions=MaterialReactionCatalogue.Parse(reactionsJson);var materials=CassetteTechnology.ReadDirectory(tech);
        var presets=CassetteCatalogue.Parse(File.ReadAllText(Path.Combine(root,CassetteCatalogue.FileName))).Presets;
        var rows=Measure(tech,reactionsJson,presets);
        check(rows.Count==35,"all 35 shipped presets are measured");
        // 1. x LOS >= 0.95 against KE for every compact ceramic/composite preset, at 0/30/60 degrees, both tilt sides.
        var compact=rows.Where(IsCompact).ToList();
        check(compact.Count>=15,"the compact ceramic/composite set has at least 15 presets ("+compact.Count+")");
        foreach(var r in compact)
            for(int i=0;i<Angles.Length;i++)
            {
                // Textolite/quartz arrays keep a realistic band (public glass/laminate Et about 0.5-0.75 for the material alone, mass efficiency 2.0-2.8):
                // 0.80-1.0 x LOS instead of 0.95 (0.22.20 round 2 decision).
                bool glass=r.Preset.Layers.Any(l=>l.Material is "cwepGlassTextolite" or "quartzFiller");double floor=glass?.80:.95;
                check(PerLos(r.RefWith[i],r.DepthMm,Angles[i])>=floor,"KE x LOS >= "+F(floor)+": "+r.Preset.Id+" at "+Angles[i]+" deg (with tilt) = "+F(PerLos(r.RefWith[i],r.DepthMm,Angles[i])));
                check(PerLos(r.RefAgainst[i],r.DepthMm,Angles[i])>=floor,"KE x LOS >= "+F(floor)+": "+r.Preset.Id+" at "+Angles[i]+" deg (against tilt)");
                if(glass)check(PerLos(r.RefWith[i],r.DepthMm,Angles[i])<=1.0,"a preset with textolite/quartz stays at or under 1.0 x LOS: "+r.Preset.Id);
            }
        // HEAT is higher than KE per LOS thickness for every compact preset.
        foreach(var r in compact.Where(x=>x.Category!="heavy metal"))check(PerLos(r.HeatWith[0],r.DepthMm,0)>PerLos(r.RefWith[0],r.DepthMm,0),"HEAT x LOS above KE x LOS: "+r.Preset.Id);
        // 2. Mass-efficiency ceilings (public figures: alumina ~1.5-2.0, SiC/B4C ~2.0-2.5 against KE; ceramic ~2-3, textolite/glass ~1.5-2.5 against HEAT; NERA higher).
        foreach(var r in rows)
            for(int i=0;i<Angles.Length;i+=2)
            {
                double ke=PerMass(r.RefWith[i],r.KgM2,Angles[i]),heat=PerMass(r.HeatWith[i],r.KgM2,Angles[i]);
                bool carbide=r.Preset.Layers.Any(l=>l.Material is "siliconCarbide" or "boronCarbide");
                double keMax=r.Category switch{"ceramic"=>carbide?2.5:2.1,"NERA"=>3.0,_=>2.0};
                double heatMax=r.Category switch{"ceramic"=>3.0,"NERA"=>6.0,_=>2.5};
                check(ke<=keMax,"KE x mass ceiling "+F(keMax)+": "+r.Preset.Id+" at "+Angles[i]+" = "+F(ke));
                check(heat<=heatMax,"HEAT x mass ceiling "+F(heatMax)+": "+r.Preset.Id+" at "+Angles[i]+" = "+F(heat));
            }
        // 3. Steel, RHA, DU/heavy alloy, NERA and the other recipes are untouched.
        check(materials["rha"].RhaFactor==1&&materials["highHardnessSteel"].RhaFactor==1.16&&materials["advancedArmourSteel"].RhaFactor==1.3&&materials["castArmour"].Density==7850,"RHA and the steels keep their rhaFactor");
        check(materials["duMesh"].RhaFactor==1.6&&materials["heavyAlloyInsert"].RhaFactor==1.5&&materials["elastomerNERA"].RhaFactor==.07&&materials["perforatedHhs"].RhaFactor==.6,"heavy metals, NERA and perforated HHS keep their rhaFactor");
        double Loss(string id)=>reactions.Recipes[id].LossPerReference;
        check(Loss("hardSteel")==.03&&Loss("advancedSteel")==.03&&Loss("elastomerSandwich")==.07&&Loss("duMesh")==.14&&Loss("heavyAlloy")==.12&&Loss("perforatedHhs")==.1&&Loss("ceramicStrikeFace")==.04&&Loss("siliconCarbide")==.05,"steel, NERA, heavy-metal and unchanged ceramic recipes keep their values");
        check(reactions.Work.RodLengthM==.12&&reactions.MaximumInteractionLoss==.5,"work calibration (rodLengthM 0.12) is not touched");
        // The calibrated values.
        (string Id,double Rha)[] calibrated={("ceramicArray",.9),("aluminaCeramic",.95),("siliconCarbide",1.0),("boronCarbide",.8),("cwepGlassTextolite",.65),("quartzFiller",.78)};
        foreach(var (id,rha) in calibrated)
        {
            check(materials[id].RhaFactor==rha,"Technology rhaFactor of "+id+" is "+rha);
            check(reactions.Bindings[id].RhaFactor==rha&&reactions.Bindings[id].Density==materials[id].Density,"binding of "+id+" equals the Technology file (a mismatch would drop the reaction)");
        }
        double Em(string id)=>materials[id].RhaFactor*7850/materials[id].Density;
        check(Em("aluminaCeramic")<=2.15&&Em("ceramicArray")<=2.0&&Em("siliconCarbide")<=2.5&&Em("boronCarbide")<=2.5,"material-level mass efficiency of the ceramics: alumina <= 2.15, ceramicArray <= 2.0, SiC and B4C <= 2.5");
        check(Em("cwepGlassTextolite")<=2.8&&Em("quartzFiller")<=2.8,"textolite and quartz: material-level KE mass efficiency <= 2.8 ("+F(Em("cwepGlassTextolite"))+", "+F(Em("quartzFiller"))+")");
        // Material-level ceiling for EVERY non-steel material of the Technology folder (steels excluded, cassettes are derived).
        int nonSteel=0;
        foreach(var m in materials.Values)
        {
            if(m.Id.StartsWith("ebCassette_",StringComparison.Ordinal)||reactions.SteelMaterialIds.Contains(m.Id)||m.Id is "rha")continue;
            nonSteel++;
            double ceiling=m.Id switch{"aluminaCeramic"=>2.15,"ceramicArray"=>2.0,"siliconCarbide" or "boronCarbide"=>2.5,_=>2.8};
            check(Em(m.Id)<=ceiling+1e-9,"material-level mass efficiency ceiling "+F(ceiling)+": "+m.Id+" = "+F(Em(m.Id)));
        }
        check(nonSteel>=15,"the material ceiling covers every non-steel material ("+nonSteel+")");
        // The armour responses' passiveMaterial rows carry the same numbers.
        using(var doc=JsonDocument.Parse(File.ReadAllText(Path.Combine(root,"sprocket.expandedballistics.responses.json"))))
            foreach(var row in doc.RootElement.GetProperty("responses").EnumerateArray())
            {
                var id=row.GetProperty("responseId").GetString()!;
                if(!id.StartsWith("editor_",StringComparison.Ordinal)||!materials.TryGetValue(id["editor_".Length..],out var m)||!calibrated.Any(c=>c.Id==m.Id))continue;
                check(row.GetProperty("passiveMaterial").GetProperty("rhaFactor").GetDouble()==m.RhaFactor,"responses row "+id+" passiveMaterial.rhaFactor equals the Technology file");
            }
        // 4. Technology signatures: every shipped cassette file equals the generated signature (so every calibrated cassette is regenerated).
        foreach(var p in presets)
        {
            var model=new CassetteModel(p,materials,reactions);
            check(materials.TryGetValue(p.MaterialId,out var native)&&native==model.Passive,"Technology signature of "+p.Id+" equals the sidecar layers");
        }
        // 5. A user cassette stays valid: the live sidecar's user presets (fixture) build against the new materials and their regenerated
        //    Technology file is exactly the signature the loader compares with.
        var src=SourceRoot();check(src!=null,"source root found");
        if(src!=null)
        {
            var user=CassetteCatalogue.Parse(File.ReadAllText(Path.Combine(src,"tests","fixtures-0.22.20","user-cassettes.json"))).Presets;
            check(user.Length==5&&user.All(u=>u.Id.StartsWith("user_",StringComparison.Ordinal)),"five user cassettes from the live config");
            var reactive=new HashSet<string>(StringComparer.Ordinal);var scratch=Path.Combine(SprocketExpandedBallistics.Fix02228Tests.FixtureRoot(),"eb-0.22.20-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(scratch);
            try
            {
                foreach(var u in user)
                {
                    var model=new CassetteModel(u,materials,reactions,reactive);
                    File.WriteAllText(Path.Combine(scratch,u.MaterialId+".json"),CassetteTechnology.Generate(model));
                    check(model.Passive.RhaFactor>0,"user cassette builds with the new values: "+u.Id);
                }
                var regenerated=CassetteTechnology.ReadDirectory(scratch);
                foreach(var u in user)check(regenerated[u.MaterialId]==new CassetteModel(u,materials,reactions,reactive).Passive,"regenerated user Technology equals the loader signature: "+u.Id);
                var changed=user.Where(u=>u.Layers.Any(l=>calibrated.Any(c=>c.Id==l.Material))).Select(u=>u.Id).OrderBy(x=>x).ToArray();
                check(changed.SequenceEqual(new[]{"user_new_cassette_v1","user_t84_surblinde_glacic_v1"}),"only the ceramic and the textolite user cassette need a regenerated file: "+string.Join(",",changed));
            }
            finally{Directory.Delete(scratch,true);}
        }
        Console.WriteLine($"0.22.20 checks PASS: {count} checks.");
    }

    private static string? BaseDir()=>AppContext.BaseDirectory;

    /// <summary>
    /// Writes the calibration table when EB_CALIBRATION_OUT names a file (never during the normal run). EB_CALIBRATION_LIVE
    /// may name a folder with copies of the live sidecar (config) and Technology; its user cassettes are measured too.
    /// </summary>
    internal static bool ReportIfRequested()
    {
        var output=Environment.GetEnvironmentVariable("EB_CALIBRATION_OUT");if(string.IsNullOrEmpty(output))return false;
        var root=BaseDir()!;var tech=Path.Combine(root,"NativeData");
        var reactions=File.ReadAllText(Path.Combine(root,MaterialReactionCatalogue.FileName));
        var presets=CassetteCatalogue.Parse(File.ReadAllText(Path.Combine(root,CassetteCatalogue.FileName))).Presets.ToList();
        if(Environment.GetEnvironmentVariable("EB_CALIBRATION_REGEN") is {Length:>0} regen)
        {
            // Regenerates the Technology file of every shipped preset from the Technology/reactions of this tree.
            Directory.CreateDirectory(regen);var mats=CassetteTechnology.ReadDirectory(tech);var react=MaterialReactionCatalogue.Parse(reactions);
            foreach(var preset in presets)File.WriteAllText(Path.Combine(regen,preset.MaterialId+".json"),CassetteTechnology.Generate(new CassetteModel(preset,mats,react)));
            File.WriteAllText(Path.Combine(regen,"spall.default.json"),SpallBalance.ToJson(new SpallSettings{SpacedKinetic=SpacedKineticSettings.Default,FragmentRicochet=FragmentRicochetSettings.Default}));
            Console.WriteLine("Regenerated "+presets.Count+" Technology files in "+regen);
        }
        var title=Environment.GetEnvironmentVariable("EB_CALIBRATION_TITLE")??"Calibration";
        var sb=new StringBuilder();
        sb.Append(Report(title+": the 35 shipped presets",Measure(tech,reactions,presets),"Technology and reaction defaults of this source tree."));
        sb.AppendLine().Append(Report(title+": element-level reference panels",Measure(tech,reactions,ReferencePanels()),"Panels: rha 10 mm, 50 mm of the material, rha 15 mm (normal incidence; at 30/60 degrees the same stack tilted by the shot only). Compare the KE/HEAT x LOS columns with the public element-level numbers in CALIBRATION.md."));
        var live=Environment.GetEnvironmentVariable("EB_CALIBRATION_LIVE");
        if(!string.IsNullOrEmpty(live))
        {
            var liveSidecar=CassetteCatalogue.Parse(File.ReadAllText(Path.Combine(live,"config",CassetteCatalogue.FileName))).Presets;
            var user=liveSidecar.Where(p=>p.Id.StartsWith("user_",StringComparison.Ordinal)).ToList();
            var userRows=new List<Row>();
            // The user cassettes measured against the Technology/reactions of this tree (the live Technology is the previous version's).
            foreach(var p in user)userRows.AddRange(Measure(tech,reactions,new[]{p}));
            sb.AppendLine().Append(Report(title+": the user cassettes in the live config (read-only copy)",userRows,"Layers from the live sidecar; Technology and reaction values of this source tree (the user cassettes' own Technology files are regenerated by the installer)."));
        }
        File.WriteAllText(output,sb.ToString());
        if(Environment.GetEnvironmentVariable("EB_CALIBRATION_CSV") is {Length:>0} csv)File.WriteAllText(csv,Csv(Measure(tech,reactions,presets.Concat(ReferencePanels()))));
        Console.WriteLine("Calibration report written: "+output);
        return true;
    }
}
