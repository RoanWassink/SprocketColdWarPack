using System.Globalization;
using System.Text.Json;
namespace SprocketExpandedBallistics;

// Calibrated passive preset; optional ERA is owned by the existing placed-cell adapter.
internal sealed record CompositeModuleLayer(string Name,double ThicknessMm,double Density,double KeMassEfficiency,double HeatMassEfficiency);
internal sealed record CompositeModulePreset(string Date,string Label,CompositeModuleLayer[] Layers,double SpallFactor,double CostMultiplier)
{
    public double MinFootprintScale {get;init;}=.5;
    public double MaxFootprintScale {get;init;}=8;
    public CompositeModuleEra Era {get;init;}=new();
    internal double ArealMass=>Layers.Sum(l=>l.ThicknessMm*.001*l.Density);
    internal double EquivalentMm(bool heat)=>Layers.Sum(l=>l.ThicknessMm*l.Density/7850*(heat?l.HeatMassEfficiency:l.KeMassEfficiency));
    internal double Factor(bool heat)=>EquivalentMm(heat)/CompositeModule.DepthMm;
    internal double Efficiency(bool heat)=>EquivalentMm(heat)/(ArealMass/7.85);
    internal double Mass(double x=1,double y=1,double z=1)=>CompositeModule.AreaM2*ArealMass*CompositeModule.VolumeScale(x,y,z);
    internal bool Allows(DateTime vehicleDate)=>vehicleDate>=DateTime.ParseExact(Date,"yyyy.MM.dd",CultureInfo.InvariantCulture);
    internal static CompositeModulePreset Parse(string json)
    {
        var p=JsonSerializer.Deserialize<CompositeModulePreset>(json,new JsonSerializerOptions{PropertyNameCaseInsensitive=true})??throw new FormatException("Module preset missing");
        if(!DateTime.TryParseExact(p.Date,"yyyy.MM.dd",CultureInfo.InvariantCulture,DateTimeStyles.None,out _)||string.IsNullOrWhiteSpace(p.Label)||p.Layers is not {Length:>0})throw new FormatException("Module date, label or layers invalid");
        static bool Positive(double v)=>double.IsFinite(v)&&v>0;
        if(p.Layers.Any(l=>string.IsNullOrWhiteSpace(l.Name)||!Positive(l.ThicknessMm)||!Positive(l.Density)||!Positive(l.KeMassEfficiency)||!Positive(l.HeatMassEfficiency))||!double.IsFinite(p.SpallFactor)||p.SpallFactor<0||!double.IsFinite(p.CostMultiplier)||p.CostMultiplier<0||!Positive(p.Mass())||!Positive(p.Factor(false))||!Positive(p.Factor(true)))throw new FormatException("Module values must be finite and positive");
        if(!Positive(p.MinFootprintScale)||!Positive(p.MaxFootprintScale)||p.MinFootprintScale>p.MaxFootprintScale||!p.Era.Valid)throw new FormatException("Invalid footprint or ERA settings");
        return p; // No gameplay ceiling: the efficiency band is a default calibration, never a clamp.
    }
}
internal readonly record struct CompositeModuleInterval(int Entity,double StartM,double EndM,double Angle,ProtectionFactorTable Table);
internal sealed record CompositeModuleBlock(double Factor,CompositeModuleInterval[] Layers,bool Grouped,double StartM,double EndM);
internal static class CompositeModule
{
    internal const string ComponentId="compositeSideModule",MaterialId="compositeSideModuleMaterial",PartGuid="a95bc652-39c6-51ad-9f52-ecb022370001",ConfigFile="sprocket.expandedballistics.composite-module.json";
    internal const string DisplayName="Challenger 2 TES side module (ASPRO-HMT ERA)";
    internal const double DepthMm=327,AreaM2=.699235*.98;
    internal static double VolumeScale(double x,double y,double z)=>Math.Abs(x*y*z);
    internal static CompositeModulePreset Current{get;private set;}=ReadDefault();
    private static CompositeModulePreset ReadDefault()
    {
        using var stream=typeof(CompositeModule).Assembly.GetManifestResourceStream("CompositeModule.Defaults")??throw new FileNotFoundException("Composite module defaults missing");
        using var reader=new StreamReader(stream);return CompositeModulePreset.Parse(reader.ReadToEnd());
    }
    internal static void Load(string directory)
    {
        var path=Path.Combine(directory,ConfigFile);
        Current=File.Exists(path)?CompositeModulePreset.Parse(File.ReadAllText(path)):ReadDefault();
    }
    // Own intervals are clipped to the current native block. Gaps and other objects keep
    // the block's original native factor. A hull never inherits the module's factor.
    internal static CompositeModuleBlock? Plan(double start,double end,double nativeFactor,ProtectionThreat threat,IEnumerable<CompositeModuleInterval> source)
    {
        if(!double.IsFinite(start)||!double.IsFinite(end)||end<=start||!double.IsFinite(nativeFactor)||nativeFactor<0)return null;
        var layers=source.Where(i=>double.IsFinite(i.StartM)&&double.IsFinite(i.EndM)&&double.IsFinite(i.Angle))
            .Select(i=>i with{StartM=Math.Max(start,i.StartM),EndM=Math.Min(end,i.EndM)}).Where(i=>i.EndM>i.StartM).OrderBy(i=>i.StartM).ToArray();
        if(layers.Length==0)return null;
        var edge=start;double resistance=0;
        foreach(var layer in layers)
        {
            if(layer.StartM<edge-1e-6)return null; // overlapping intervals are ambiguous; no guessed resistance
            resistance+=(layer.StartM-edge)*nativeFactor+(layer.EndM-layer.StartM)*layer.Table.Factor(threat,layer.Angle);edge=layer.EndM;
        }
        resistance+=(end-edge)*nativeFactor;
        return new(resistance/(end-start),layers,layers.Length>1||layers[0].StartM>start+1e-6||edge<end-1e-6,start,end);
    }
    internal static CombatReportEvent Report(CompositeModuleInterval layer,ProtectionThreat threat,string stage,string hitId,bool grouped)
    {
        var path=(layer.EndM-layer.StartM)*1000;
        var part=new CombatLayerPart(layer.Entity,DisplayName,MaterialId,DisplayName,path*Math.Cos(layer.Angle*Math.PI/180),layer.Angle,true);
        return new("materialReaction",stage,ProtectionLine.Text(layer.Table.Label,path,layer.Table.Factor(threat,layer.Angle),threat,layer.Angle)+(grouped?"; native grouped block: interval-weighted resistance, shared outcome":""),HitId:hitId+":module:"+layer.Entity,Layer:new(hitId+":module:"+layer.Entity,new[]{part},false),RelatedEntity:layer.Entity);
    }
    internal static CombatReportEvent[] ReportEvents(CombatReportEvent reaction)=>new[]{reaction with{Kind="layerStart",Text=DisplayName},reaction with{Layer=null}};
}
