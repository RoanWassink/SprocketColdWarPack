using System.Numerics;
using System.Text.Json;
using System.Text.RegularExpressions;
namespace SprocketExpandedBallistics;

internal sealed record CassetteIssue(string Code,string Text,bool Error=false,int Layer=-1);
/// <summary>One angle of the protection table: mm RHA along the path for each threat (0.22.28: path × the factor the game uses).</summary>
internal sealed record CassetteProtection(double Angle,bool WithTilt,double HeatMm,double KeMm,double FullBoreMm=double.NaN,double HeshMm=double.NaN);
internal sealed record CassettePreview(double DepthMm,double KgM2,double CostM2,double NativeRha,string SpallSource,double SpallFactor,CassetteProtection[] Protection);
internal sealed record CassetteSaveResult(CassettePreset Preset,bool RestartRequired,string Status);
internal sealed record CassetteSelectionPlate(string Material,Vector3[][] Faces,Vector3[] Solid);

// Pure managed backend. Usage must include the caller's persistent blueprint index,
// not just the current selection. Mutations fail closed when usage is unknown.
internal sealed class CassetteComposer
{
    private readonly IReadOnlyDictionary<string,CassetteMaterial> materials;
    private readonly MaterialReactionCatalogue reactions;
    private readonly IReadOnlySet<string> reactive;
    private readonly string sidecar,technology;
    private readonly Func<string,bool> inUse;
    internal double RhaDensity=>materials.TryGetValue("rha",out var rha)?rha.Density:7850;
    private static readonly object gate=new();
    internal CassetteComposer(IReadOnlyDictionary<string,CassetteMaterial> materials,MaterialReactionCatalogue reactions,
        string sidecar,string technology,Func<string,bool> inUse,IReadOnlySet<string>? reactive=null)
    {this.materials=materials;this.reactions=reactions;this.sidecar=sidecar;this.technology=technology;this.inUse=inUse;this.reactive=reactive??new HashSet<string>();}

    internal CassetteIssue[] Validate(CassettePreset preset)
    {
        var issues=new List<CassetteIssue>();
        if(preset.Layers==null||preset.Layers.Length is <1 or >64)
            return new[]{new CassetteIssue("layer-count","A cassette must contain 1 to 64 layers.",true)};
        for(int i=0;i<preset.Layers.Length;i++)
        {
            var l=preset.Layers[i];
            if(!double.IsFinite(l.TiltDegrees)||l.TiltDegrees<0||l.TiltDegrees>75)issues.Add(new("tilt","Tilt exceeds the supported 0-75 degrees.",true,i));
            if(l.Material!="air"&&(!materials.ContainsKey(l.Material)||CompositePlate.IsCassette(l.Material)||reactive.Contains(l.Material)))issues.Add(new("material","Missing or unsupported constituent: "+l.Material,true,i));
        }
        // Roundtrip through the strict file contract before deriving anything.
        try{_=CassetteCatalogue.Parse(Serialize(new[]{preset}));}
        catch(FormatException ex){issues.Add(new("schema",ex.Message,true));}
        if(issues.Any(x=>x.Error))return issues.ToArray();
        double depth=preset.Layers.Sum(l=>l.Material=="air"?l.ThicknessMm:l.ThicknessMm/Math.Cos(l.TiltDegrees*Math.PI/180));
        if(depth<preset.MinimumDepthMm||depth>preset.MaximumDepthMm)issues.Add(new("depth","Nominal depth D is outside the depth window.",true));
        double rounded=Math.Round(depth,MidpointRounding.AwayFromZero);
        if(rounded<preset.MinimumDepthMm||rounded>preset.MaximumDepthMm)issues.Add(new("native-depth","Depth rounded to native whole millimetres is outside the depth window.",true));
        // Widen only for diagnostics; saves retain and enforce the actual window.
        if(!double.IsFinite(depth)||depth<=0||depth>4000){issues.Add(new("depth","Depth must be within 1-4000 mm.",true));return issues.ToArray();}
        var model=new CassetteModel(preset with {MinimumDepthMm=Math.Min(preset.MinimumDepthMm,depth),MaximumDepthMm=Math.Max(preset.MaximumDepthMm,depth)},materials,reactions,reactive);
        foreach(var note in model.TiltNotes())issues.Add(new("sandwich-tilt",note.Text,false,note.Layer));
        foreach(var l in model.Layers)
        {
            if(l.Recipe?.Mode is "sandwich" or "backed"&&!l.Supported)issues.Add(new("support","Layer has no required steel support; extra effect is lost.",false,l.SourceLayerIndex));
            // 0.22.22: only a gap that costs a real part of the effect is worth a message. A void of 20 mm behind a 25 mm core still gives 86 % (the shipped
            // Abrams-style presets), which the table already prices in; below NeraAirWarnBelow the composer says how much is lost.
            if(l.Recipe?.Id=="elastomerSandwich"&&l.NeraAirFactor<CassetteModel.NeraAirWarnBelow)issues.Add(new("nera-air",string.Create(System.Globalization.CultureInfo.InvariantCulture,$"NERA has insufficient air behind its back plate: {l.NeraAirFactor*100:0}% effect (30% without air, full effect from a void as thick as the core)."),false,l.SourceLayerIndex));
        }
        return issues.ToArray();
    }
    /// <summary>
    /// 0.22.19: the messages that concretely apply at the selected shot angle and side: one per cooperative layer (NERA, sandwich,
    /// backed) whose effective angle passes the recipe maximum, so its gain is reduced. Empty when nothing is reduced.
    /// </summary>
    internal CassetteIssue[] AngleNotes(CassettePreset preset,double angleDegrees,bool withTilt,bool tiltedLayers)
    {
        try
        {
            var model=new CassetteModel(preset with {MinimumDepthMm=1,MaximumDepthMm=4000},materials,reactions,reactive);
            return model.AngleNotes(angleDegrees,withTilt,tiltedLayers).Select(n=>new CassetteIssue("angle-taper",n.Text,false,n.Layer)).ToArray();
        }
        catch(Exception ex) when (ex is FormatException or ArgumentException or InvalidOperationException or KeyNotFoundException){return Array.Empty<CassetteIssue>();}
    }
    private CassetteModel Model(CassettePreset preset)
    {
        var errors=Validate(preset).Where(i=>i.Error).ToArray();
        if(errors.Length>0)throw new FormatException(string.Join(" ",errors.Select(i=>i.Text)));
        return new(preset,materials,reactions,reactive);
    }
    internal CassettePreview Preview(CassettePreset preset)
    {
        var model=Model(preset);
        return new(model.DepthMm,model.MassKgM2,model.CostM2,model.Passive.RhaFactor,preset.Layers.Last(l=>l.Material!="air").Material,model.Passive.SpallFactor,Protection(model,PreviewAngles));
    }
    // Scale path and areal quantities with the selected face depth, retaining the exact nominal route at 100 %.
    internal CassettePreview Preview(CassettePreset preset,double depthMm)
    {
        var p=Preview(preset);if(depthMm==p.DepthMm)return p;
        var table=Table(preset);var scale=depthMm/p.DepthMm;
        return p with{DepthMm=depthMm,KgM2=p.KgM2*scale,CostM2=p.CostM2*scale,Protection=Protection(table,PreviewAngles,depthMm)};
    }
    internal CassetteProtection[] ProtectionCurve(CassettePreset preset,double depthMm)
    {
        var table=Table(preset);return depthMm==table.DepthMm?ProtectionCurve(preset):Protection(table,CurveAngles,depthMm);
    }
    private static CassetteProtection[] Protection(ProtectionFactorTable table,IEnumerable<double> angles,double depthMm)
    {
        var rows=new List<CassetteProtection>();
        foreach(double angle in angles)foreach(bool side in new[]{true,false})
        {
            var path=ProtectionFactorTable.PathMm(depthMm,angle);
            rows.Add(new(angle,side,path*table.Factor(ProtectionThreat.Jet,angle),path*table.Factor(ProtectionThreat.LongRod,angle),
                path*table.Factor(ProtectionThreat.FullBoreKE,angle),path*table.Factor(ProtectionThreat.Hesh,angle)));
        }
        return rows.ToArray();
    }
    /// <summary>The three angles of the protection table (0/30/60 degrees).</summary>
    internal static readonly double[] PreviewAngles={0d,30,60};
    /// <summary>0.22.28: the angles of the protection chart are the angles of the game's table, 0 to 85 degrees in steps of 5 (includes 0/30/60).</summary>
    internal static readonly double[] CurveAngles=ProtectionFactorTable.Angles;
    /// <summary>0.22.23: protection (every threat, with and against the tilt) at every chart angle, from the same evaluation as <see cref="Preview"/>.</summary>
    internal CassetteProtection[] ProtectionCurve(CassettePreset preset)=>Protection(Model(preset),CurveAngles);
    /// <summary>0.22.28: the game's factor table of this draft (computed once and cached on the recipe hash).</summary>
    internal ProtectionFactorTable Table(CassettePreset preset)=>ProtectionFactorTable.Build(Model(preset));
    private static CassetteProtection[] Protection(CassetteModel model,IEnumerable<double> angles)
    {
        // 0.22.28: path × the factor the game writes, from one table (ProtectionFactorTable), never a second formula.
        var table=ProtectionFactorTable.Build(model);var rows=new List<CassetteProtection>();
        foreach(double angle in angles)foreach(bool with in new[]{true,false})
            rows.Add(new(angle,with,table.ProtectionMm(ProtectionThreat.Jet,angle),table.ProtectionMm(ProtectionThreat.LongRod,angle),
                table.ProtectionMm(ProtectionThreat.FullBoreKE,angle),table.ProtectionMm(ProtectionThreat.Hesh,angle)));
        return rows.ToArray();
    }
    internal static string Serialize(IEnumerable<CassettePreset> presets)=>JsonSerializer.Serialize(new{schemaVersion=1,presets=presets.Select(p=>new {
        id=p.Id,label=p.Label,date=p.Date,location=p.Location,window=new{minDepthMm=p.MinimumDepthMm,maxDepthMm=p.MaximumDepthMm},
        layers=p.Layers.Select(l=>new{material=l.Material,thicknessMm=l.ThicknessMm,tiltDegrees=l.TiltDegrees,tiltDirection=l.TiltDirection})})},new JsonSerializerOptions{WriteIndented=true})+"\n";
    internal CassetteSaveResult SaveNewVersion(CassettePreset draft)
    {
        lock(gate)
        {
            string original=File.ReadAllText(sidecar);var catalogue=CassetteCatalogue.Parse(original);
            var slug=Regex.Replace(draft.Id.ToLowerInvariant(),"^user_","");slug=Regex.Replace(slug,"_v[0-9]+$","");
            slug=Regex.Replace(slug,"[^a-z0-9_-]+","_").Trim('_','-');
            if(slug.Length==0)slug="cassette";if(slug.Length>32)slug=slug[..32];
            string prefix="user_"+slug;int version=1;
            while(catalogue.Presets.Any(p=>p.Id==prefix+"_v"+version)||File.Exists(Path.Combine(technology,"ebCassette_"+prefix+"_v"+version+".json")))version++;
            // SaveNewVersion always forks. It can never mutate a used identity.
            var saved=draft with{Id=prefix+"_v"+version,Layers=draft.Layers.ToArray()};var model=Model(saved);
            _=ProtectionFactorTable.Build(model);
            var json=Serialize(catalogue.Presets.Append(saved));_=CassetteCatalogue.Parse(json);
            var tech=Path.Combine(technology,saved.MaterialId+".json");
            Commit(original,json,tech,CassetteTechnology.Generate(model),false);
            return new(saved,true,"Saved; restart required until native new-Technology reload is verified in game.");
        }
    }
    internal void Delete(string id)
    {
        lock(gate)
        {
            if(!id.StartsWith("user_",StringComparison.Ordinal))throw new InvalidOperationException("Historical presets are read-only.");
            var original=File.ReadAllText(sidecar);var catalogue=CassetteCatalogue.Parse(original);
            var preset=catalogue.Presets.SingleOrDefault(p=>p.Id==id)??throw new KeyNotFoundException(id);
            if(inUse(preset.MaterialId))throw new InvalidOperationException("Preset is in use; deletion refused.");
            Commit(original,Serialize(catalogue.Presets.Where(p=>p.Id!=id)),Path.Combine(technology,preset.MaterialId+".json"),null,true);
        }
    }
    // Optimistic file guard plus rollback across the Technology/sidecar pair.
    // Technology publishes first; a process interruption can leave an unused file,
    // never a sidecar pointing at an absent Technology. Orphans reserve their id.
    private void Commit(string original,string next,string tech,string? contents,bool deleting)
    {
        if(File.ReadAllText(sidecar)!=original)throw new IOException("Cassette sidecar changed; reload before saving.");
        Directory.CreateDirectory(technology);byte[]? old=File.Exists(tech)?File.ReadAllBytes(tech):null;
        if(!deleting&&old!=null)throw new IOException("Technology identity already exists.");
        string temp=sidecar+"."+Guid.NewGuid().ToString("N")+".tmp";
        string techTemp=tech+"."+Guid.NewGuid().ToString("N")+".tmp";
        bool created=false;
        try
        {
            File.WriteAllText(temp,next);
            if(!deleting)
            {
                File.WriteAllText(techTemp,contents!);
                File.Move(techTemp,tech); // atomic publication, never overwrites an identity
                created=true;
            }
            if(File.ReadAllText(sidecar)!=original)throw new IOException("Cassette sidecar changed during save.");
            File.Move(temp,sidecar,true);
            if(deleting&&File.Exists(tech))File.Delete(tech);
        }
        catch
        {
            if(File.ReadAllText(sidecar)==next)File.WriteAllText(sidecar,original);
            if(deleting&&old!=null)File.WriteAllBytes(tech,old);else if(created&&File.Exists(tech))File.Delete(tech);
            throw;
        }
        finally{if(File.Exists(temp))File.Delete(temp);if(File.Exists(techTemp))File.Delete(techTemp);}
    }
    internal void Export(CassettePreset preset,string path)
    {RequireExtension(path);_=Model(preset);File.WriteAllText(path,Serialize(new[]{preset}));}
    internal CassettePreset Import(string path)
    {
        RequireExtension(path);var list=CassetteCatalogue.Parse(File.ReadAllText(path));
        if(list.Presets.Length!=1)throw new FormatException("A share file must contain exactly one cassette.");
        _=Model(list.Presets[0]);return list.Presets[0]; // Import is a draft; saving allocates a local id.
    }
    private static void RequireExtension(string path)
    {if(!path.EndsWith(".cassette.json",StringComparison.OrdinalIgnoreCase))throw new ArgumentException("Expected a .cassette.json share file.");}

    internal CassettePreset FromSelection(string id,string label,string date,IReadOnlyList<CassetteSelectionPlate> plates,Vector3 frontNormal,Vector3 vehicleUp,Vector3 vehicleForward)
    {
        if(plates.Count is <1 or >64)throw new ArgumentException("Select 1-64 parallel plates.");
        if(!float.IsFinite(frontNormal.LengthSquared())||frontNormal.LengthSquared()<1e-8f)throw new ArgumentException("Invalid cassette front normal.");
        frontNormal=Vector3.Normalize(frontNormal);var read=new List<(string Material,Vector3 Normal,double Front,double Back,double Thickness)>();
        foreach(var p in plates)
        {
            if(!PlateStackGeometry.TrySurface(p.Faces,out var surface,out var reason))throw new ArgumentException(reason);
            if(p.Solid.Length<4||p.Solid.Any(v=>!float.IsFinite(v.LengthSquared())))throw new ArgumentException("Missing native solid geometry.");
            Vector3 area=default;for(int j=1;j<surface.Length-1;j++)area+=Vector3.Cross(surface[j]-surface[0],surface[j+1]-surface[0]);
            var n=Vector3.Normalize(area);
            if(Vector3.Dot(n,frontNormal)<0)n=-n;
            double dot=Vector3.Dot(n,frontNormal);if(dot<.9999)throw new ArgumentException("Selected plates are not parallel.");
            double front=p.Solid.Max(v=>(double)Vector3.Dot(v,frontNormal)),back=p.Solid.Min(v=>(double)Vector3.Dot(v,frontNormal));
            double thickness=(p.Solid.Max(v=>(double)Vector3.Dot(v,n))-p.Solid.Min(v=>(double)Vector3.Dot(v,n)))*1000;
            read.Add((p.Material,n,front,back,thickness));
        }
        read.Sort((a,b)=>b.Front.CompareTo(a.Front));var layers=new List<CassetteLayer>();
        for(int i=0;i<read.Count;i++)
        {
            if(i>0)
            {
                double gap=(read[i-1].Back-read[i].Front)*1000;
                if(gap<-.05)throw new ArgumentException("Selected solid plates overlap.");
                if(gap>=.01)layers.Add(new("air",gap,0,"up"));
            }
            // Parallel selection defines the cassette front. No invented tilt.
            layers.Add(new(read[i].Material,read[i].Thickness,0,"up"));
        }
        double depth=layers.Sum(l=>l.ThicknessMm);
        var preset=new CassettePreset(id,label,date,Math.Max(1,depth*.75),Math.Min(4000,depth*1.25),layers.ToArray());
        _=Model(preset);return preset;
    }
}
