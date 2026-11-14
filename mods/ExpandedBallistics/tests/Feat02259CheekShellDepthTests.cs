using System.Numerics;
using System.Text;
using SprocketUiKit;
namespace SprocketExpandedBallistics;

/// <summary>
/// 0.22.59 "Cheek fill (shell)": thickness up to the new maximum (1000 mm by default) and a rear limit that belongs to the window. The planner is
/// unchanged; the window passes its own limits (CheekShellFill.ShellLimits). Managed evidence on the T-80BVM backup and the two welded fixtures
/// of RunShell: default equal to 0.22.58, a deeper filling that meets the hard requirements, the new field and setting, and the depth figures.
/// </summary>
internal static partial class ComposerFillBlueprintEvidence
{
    internal static void RunShellDepth(Action<bool,string> sink)
    {
        int checks=0;void Check(bool ok,string message){checks++;sink(ok,"Cheek fill (shell) depth: "+message);}
        if(shellCases==null)RunShell((_,_)=>{});
        var cases=shellCases!;var t80=cases[0];
        string root=Fix02228Tests.SourceRoot(),output=Fix02228Tests.OutputRoot(),preview=Path.Combine(output,"preview");
        Directory.CreateDirectory(preview);
        var configured=new CheekSettings();
        // 1. Default equal to 0.22.58: 450 -> 200 mm with the rear limit at 350 mm gives the same plan as the frozen 0.22.58 planner.
        foreach(var (shellCase,_) in cases)
        foreach(bool fallback in new[]{false,true})
        {
            var oldPlan=LegacyCheekShellFill058.Plan(shellCase.Wall,shellCase.HostWalls,shellCase.Bounds,new(450,200,fallback),new(),new(),shellCase.HalfGap,false,shellCase.Centre);
            foreach(var (name,limits,request) in new[]{("window default, rear 350 mm",CheekShellFill.ShellLimits(configured,1000,350),new CheekShellRequest(450,200,fallback,350)),("no window rear value",CheekShellFill.ShellLimits(configured,1000,null),new CheekShellRequest(450,200,fallback))})
            {
                var now=CheekShellFill.Plan(shellCase.Wall,shellCase.HostWalls,shellCase.Bounds,request,new(),limits,shellCase.HalfGap,false,shellCase.Centre);
                string tag=$"{shellCase.Name} 450->200{(fallback?" fallback":"")} ({name})";
                Check(now.Plan.Json==oldPlan.Plan.Json,$"{tag}: plan JSON is byte-identical to 0.22.58");
                Check(now.Plan.Summary==oldPlan.Plan.Summary,$"{tag}: window text is identical to 0.22.58");
                Check(now.Faces.Length==oldPlan.Faces.Length&&now.Faces.Zip(oldPlan.Faces).All(p=>p.First.SequenceEqual(p.Second))&&now.Front.SequenceEqual(oldPlan.Front)&&now.Rib.SequenceEqual(oldPlan.Rib)&&now.Back.Zip(oldPlan.Back).All(p=>p.First.SequenceEqual(p.Second)),$"{tag}: faces, front points, ribs and back points are identical to 0.22.58");
                var a=now.Plan.Inspect(RuntimeDensity,Array.Empty<FillBounds>());var b=oldPlan.Plan.Inspect(RuntimeDensity,Array.Empty<FillBounds>());
                Check(a.VolumeM3==b.VolumeM3&&a.MassKg==b.MassKg,$"{tag}: volume {a.VolumeM3:R} m3 and mass {a.MassKg:R} kg equal 0.22.58");
            }
        }
        // 2. Deeper: 800 -> 800 mm on the T-80BVM with the rear limit at 350, 0 and -300 mm meets every hard requirement checked for 450 -> 200.
        var seams=new Dictionary<double,double>();var figures=new StringBuilder();var rows=new StringBuilder();var results=new Dictionary<double,(CheekShellResult R,(double VolumeM3,double MassKg) Stats,(double Mm,float X,float Y) Ray)>();
        foreach(double rearMm in new[]{350d,0d,-300d})
        {
            var limits=CheekShellFill.ShellLimits(configured,1000,rearMm);
            Check(limits.RearBoundaryM*1000==rearMm&&limits.MaximumMm==1000&&limits.RowDepthMm==configured.RowDepthMm&&limits.WallAngleDegrees==configured.WallAngleDegrees,$"limits for rear {rearMm:0} mm: rear {limits.RearBoundaryM*1000:0.###} mm, maximum 1000 mm, row depth and wall angle unchanged");
            var r=CheekShellFill.Plan(t80.Case.Wall,t80.Case.HostWalls,t80.Case.Bounds,new(800,800,false,rearMm),new(),limits,t80.Case.HalfGap,false,t80.Case.Centre);
            var old=LegacyCheekShellFill057.Plan(t80.Case.Wall,t80.Case.HostWalls,t80.Case.Bounds,new(800,800,false),new(),limits,t80.Case.HalfGap,false,t80.Case.Centre);
            var evaluated=EvaluateShell($"t80bvm 800->800 rear {rearMm:0}",t80.Case,r,old,800,800,false,Check,rows,(float)(rearMm/1000.0),false);
            seams[rearMm]=(double)evaluated.GetType().GetProperty("maxSeamMm")!.GetValue(evaluated)!;
            var stats=r.Plan.Inspect(RuntimeDensity,Array.Empty<FillBounds>());var ray=MaxAxisThickness(r,t80.Case);
            results[rearMm]=(r,(stats.VolumeM3,stats.MassKg),ray);
            Check(seams[rearMm]<=1,$"rear {rearMm:0} mm: back points shared between blocks lie at most {seams[rearMm]:0.###} mm apart (limit 1 mm)");
            Check(ray.Mm>0&&ray.Mm<=800.0/Math.Sin(10*Math.PI/180)+1,$"rear {rearMm:0} mm: the thickest straight-ahead shot line crosses {ray.Mm:0.#} mm of filling");
        }
        Check(results[0].R.Faces.Length>=results[350].R.Faces.Length&&results[-300].R.Faces.Length>=results[0].R.Faces.Length,$"a lower rear limit never gives fewer blocks ({results[350].R.Faces.Length} / {results[0].R.Faces.Length} / {results[-300].R.Faces.Length})");
        figures.AppendLine("# 0.22.59 depth results: T-80BVM, Cheek fill (shell) 800 -> 800 mm");
        figures.AppendLine();
        figures.AppendLine("Managed plan on the saved T-80BVM backup (turret faces drawn, inner wall rebuilt from the saved thickness, gun as saved caliber envelope). Live tessellation, gun bounds and rib acceptance remain in-game checks.");
        figures.AppendLine("The shot line runs along the longitudinal axis of the turret (towards -Z), swept over x and y in 5 mm steps; the thickness is the length inside the filling blocks.");
        figures.AppendLine();
        figures.AppendLine("| rear limit (mm in front of the centre) | blocks | volume (m3) | mass (kg) | shortened corners | largest shortening (mm) | corners clamped at the rear limit | largest distance between shared back points (mm) | thickest straight-ahead shot line (mm) at x, y (m) |");
        figures.AppendLine("|---|---|---|---|---|---|---|---|---|");
        foreach(var (rearMm,(r,s,ray)) in results)
            figures.AppendLine(FormattableString.Invariant($"| {rearMm:0} | {r.Faces.Length} of {r.EligibleFaces} | {s.VolumeM3:0.####} | {s.MassKg:0.#} | {r.Shortened} | {r.MaxShortenMm:0.#} | {r.ClampedRear} | {seams[rearMm]:0.###} | {ray.Mm:0.#} at {ray.X:0.###}, {ray.Y:0.###} |"));
        var baseline=CheekShellFill.Plan(t80.Case.Wall,t80.Case.HostWalls,t80.Case.Bounds,new(450,200,false,350),new(),CheekShellFill.ShellLimits(configured,1000,350),t80.Case.HalfGap,false,t80.Case.Centre);
        var baseStats=baseline.Plan.Inspect(RuntimeDensity,Array.Empty<FillBounds>());var baseRay=MaxAxisThickness(baseline,t80.Case);
        figures.AppendLine();
        figures.AppendLine(FormattableString.Invariant($"For comparison, the 0.22.58 default (450 -> 200 mm, rear limit 350 mm): {baseline.Faces.Length} of {baseline.EligibleFaces} blocks, volume {baseStats.VolumeM3:0.####} m3, mass {baseStats.MassKg:0.#} kg, {baseline.Shortened} shortened corners (largest {baseline.MaxShortenMm:0.#} mm), thickest straight-ahead shot line {baseRay.Mm:0.#} mm."));
        File.WriteAllText(Path.Combine(output,"DEPTH-RESULTS.md"),figures.ToString());
        File.WriteAllText(Path.Combine(output,"SHELL-DEPTH-CORNERS.tsv"),rows.ToString());
        // 3. The field and the setting.
        foreach(double bad in new[]{-2000.001,5000.001,double.NaN,double.PositiveInfinity,double.NegativeInfinity,-1e9})
        {
            string error="";try{CheekShellFill.ShellLimits(configured,1000,bad);}catch(InvalidOperationException e){error=e.Message;}
            Check(error.StartsWith("Enter a rear limit from -2000 to 5000 mm"),$"rear limit {bad} is refused");
        }
        foreach(double good in new[]{-2000,-300,0,350,5000})Check(CheekShellFill.ShellLimits(configured,1000,good).RearBoundaryM==good/1000.0,$"rear limit {good} mm is accepted");
        Check(configured.RearBoundaryM==.35&&configured.MaximumMm==450&&configured with{}==new CheekSettings(),"the configured limits are not changed by building the limits of this button");
        Check(CheekShellFill.ShellLimits(configured,1000,null).RearBoundaryM==configured.RearBoundaryM,"without a window value the config rear boundary applies");
        Check(CheekShellFill.Invariant(Math.Round(configured.RearBoundaryM*1000,3))=="350","the window starts at the config rear boundary, shown as 350 mm");
        // The thickness fields accept 1000 and refuse 1001 (and 3 accepted, 2.99 refused) at the default setting.
        var shellLimits=CheekShellFill.ShellLimits(configured,1000,350);
        foreach(var (gun,outer,ok) in new[]{(1000d,1000d,true),(1000d,3d,true),(1001d,200d,false),(450d,1001d,false),(2.99,200d,false),(double.NaN,200d,false)})
        {
            string error="";bool accepted=true;
            try{CheekShellFill.Plan(t80.Case.Wall,t80.Case.HostWalls,t80.Case.Bounds,new(gun,outer,false,350),new(),shellLimits,t80.Case.HalfGap,false,t80.Case.Centre);}catch(InvalidOperationException e){accepted=false;error=e.Message;}
            Check(accepted==ok&&(ok||error.Contains("from 3 to 1000 mm")),$"thickness {gun} / {outer} mm is {(ok?"accepted":"refused")} at the default maximum of 1000 mm{(ok?"":" ("+error+")")}");
        }
        // The old maximum of the config (450) still governs the preset and Conform: no window value reaches them (source evidence).
        string Source(string name)=>File.ReadAllText(Path.Combine(root,name)).Replace("\r\n","\n");
        var fill=Source("RuntimeComposerFill.cs");var plateConfig=Source("RuntimeCompositePlate.cs");var panel=Source("CassetteComposerPanel.cs");
        Check(fill.Split("ShellLimits(").Length==2&&fill.Split("RearLimitMm").Length==2,"RuntimeComposerFill builds the shell limits once, from the window request only");
        Check(fill.Contains("ConformCheekFill.Plan(innerWalls,offsetMm??RuntimeCompositePlate.CheekLimits.MaximumMm,settings,RuntimeCompositePlate.CheekLimits,")&&fill.Contains(":ComposerFill.Cheeks(hostWalls,bounds,settings,RuntimeCompositePlate.CheekLimits,"),"the preset and Conform still use the config limits (Cheek maximum thickness, Cheek rear boundary)");
        var conform=Source("ConformCheekFill.cs");var composer=Source("ComposerFill.cs");
        Check(!conform.Contains("ShellLimits")&&!conform.Contains("RearLimitMm")&&!composer.Contains("ShellLimits")&&!composer.Contains("RearLimitMm"),"the Conform and preset planners know nothing of the window values");
        Check(plateConfig.Contains("\"Cheek shell maximum thickness (mm)\",1000.0,new ConfigDescription(\"Maximum thickness you can enter for Cheek fill (shell).\",new AcceptableValueRange<double>(3,2000))")&&plateConfig.Contains("\"Cheek maximum thickness (mm)\",450.0,")&&plateConfig.Contains("\"Cheek rear boundary (m)\",.35,new ConfigDescription(\"Rear limit of cheek fillers, measured forward from the turret origin; leaves room for crew and gun.\",new AcceptableValueRange<double>(-2,5))"),"new setting 1000 (3 to 2000) with the requested description; Cheek maximum thickness stays 450 and Cheek rear boundary stays 0.35");
        Check(plateConfig.Contains("config.Bind(\"Composite fill\",\"Cheek shell maximum thickness (mm)\""),"the new setting is in the section Composite fill");
        Check(!System.Text.RegularExpressions.Regex.IsMatch(fill+plateConfig+panel,@"cheek(Rear|Maximum|ShellMaximum|RowDepth|WallAngle)\.Value\s*="),"nothing writes a cheek config value (Apply leaves the config unchanged)");
        var dialog=panel[panel.IndexOf("private static void CheekShellDialog()",StringComparison.Ordinal)..];dialog=dialog[..dialog.IndexOf("private const float DerivedLabelWidth",StringComparison.Ordinal)];
        Check(dialog.Contains("Rear limit (mm in front of the turret centre)")&&dialog.Contains("How far in front of the turret centre the filling must stop. Lower it for a deeper filling; keep room for the crew and the gun.")&&dialog.Contains("Math.Round(limits.RearBoundaryM*1000,3)")&&dialog.Contains("new(g,o,fallback,r)"),"the window has the new field with its tooltip, starts from the config value and passes it with the thicknesses");
        Check(dialog.Contains("{shellMaximum:0.#}")&&!dialog.Contains("limits.MaximumMm"),"the two thickness fields show and use the new maximum");
        Check(panel.Split("CheekShellRequest").Length==1&&panel.Split("new(g,o,fallback,r)").Length==2,"only the shell window builds a request with a rear limit");
        // 4. Pictures for the contact sheet: T-80BVM, 450 -> 200 with the rear limit at 350 next to 800 -> 800 with it at 0, front and top, gun visible.
        var gunBox=Feat02253ComposerFillTests.Box(t80.Gun.Min,t80.Gun.Max).Select(t=>new SolidTriangle(t.A,t.B,t.C,Vector3.UnitZ,Raster.Pack(212,152,52,255))).ToList();
        var viewBounds=(t80.Case.Bounds.Min,t80.Case.Bounds.Max+new Vector3(0,0,.5f));
        foreach(var (label,result) in new[]{("450-200-rear350",baseline),("800-800-rear0",results[0].R)})
        foreach(var (view,direction) in new[]{("front",Vector3.UnitZ),("top",Vector3.UnitY)})
            File.WriteAllBytes(Path.Combine(preview,$"t80bvm-shell-{label}-{view}.png"),Render(BlockSolids(result.Faces,result.Front,result.Back,result.Normals).Concat(gunBox).ToList(),new Camera(viewBounds,direction),t80.Case.Outer));
        Console.WriteLine($"FEAT59 PASS: {checks} checks; T-80BVM 800->800 rear 350/0/-300: volume {results[350].Stats.VolumeM3:0.###}/{results[0].Stats.VolumeM3:0.###}/{results[-300].Stats.VolumeM3:0.###} m3.");
    }

    /// <summary>Thickest path (mm) of the filling along the turret's longitudinal axis: rays towards -Z, swept over x and y in 5 mm steps (offset so no ray runs along an edge).</summary>
    private static (double Mm,float X,float Y) MaxAxisThickness(CheekShellResult r,ShellCase shellCase)
    {
        var blocks=new List<(List<SolidTriangle> T,Vector3 Min,Vector3 Max)>();
        for(int f=0;f<r.Faces.Length;f++)
        {
            var solids=BlockSolids(new[]{r.Faces[f]},r.Front,new[]{r.Back[f]},new[]{r.Normals[f]});var b=Bounds(solids);blocks.Add((solids,b.Min,b.Max));
        }
        double best=0;float bx=0,by=0;var dir=-Vector3.UnitZ;
        for(float x=shellCase.Bounds.Min.X+.00137f;x<shellCase.Bounds.Max.X;x+=.005f)
        for(float y=shellCase.Bounds.Min.Y+.00291f;y<shellCase.Bounds.Max.Y;y+=.005f)
        {
            double total=0;
            foreach(var (tris,min,max) in blocks)
            {
                if(x<min.X||x>max.X||y<min.Y||y>max.Y)continue;
                var hits=new List<(float T,int Sign)>();var origin=new Vector3(x,y,shellCase.Bounds.Max.Z+2);
                foreach(var t in tris)
                {
                    var e1=t.B-t.A;var e2=t.C-t.A;var p=Vector3.Cross(dir,e2);float det=Vector3.Dot(e1,p);if(MathF.Abs(det)<1e-12f)continue;
                    float inv=1/det;var s=origin-t.A;float u=Vector3.Dot(s,p)*inv;if(u<0||u>1)continue;
                    var q=Vector3.Cross(s,e1);float v=Vector3.Dot(dir,q)*inv;if(v<0||u+v>1)continue;
                    float at=Vector3.Dot(e2,q)*inv;if(at<0)continue;
                    hits.Add((at,Vector3.Dot(Vector3.Cross(e1,e2),dir)<0?1:-1));
                }
                hits.Sort((a,b)=>a.T.CompareTo(b.T));int depth=0;float last=0;
                foreach(var (at,sign) in hits){if(depth>0)total+=at-last;depth+=sign;last=at;}
            }
            if(total>best){best=total;bx=x;by=y;}
        }
        return (best*1000,bx,by);
    }
}
