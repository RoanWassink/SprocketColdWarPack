using System.Globalization;
using System.Numerics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
namespace SprocketExpandedBallistics;

/// <summary>The four threats of the protection table (0.22.28).</summary>
internal enum ProtectionThreat{LongRod,FullBoreKE,Jet,Hesh}

/// <summary>
/// 0.22.28 (user decision 03-11): the protection of a cassette or a loose mod material, computed once in advance from the composer model and
/// never per shot. Factor = L / path (×RHA per mm of path) for each threat, impact angle 0-85° in steps of 5°, with and against the tilt.
/// L is the composer's own protection: the native line of sight (path × passive rha) plus the recipe work of <see cref="CassetteModel.Evaluate"/>
/// (rod yaw and erosion, ceramic backing, NERA air, the taper above the recipe maximum angle and the 80° cap), and for HESH the normal thickness
/// over the spacing retention. The composer chart, the composer table and the game read the same table with the same <see cref="Factor"/>.
/// In the game native charges a block L = path × rhaFactor × cos^(1-e) (e: the profile's obliquity exponent, LOS-MAP), so the plugin writes
/// rhaFactor = <see cref="NativeRhaFactor"/> and native then charges exactly path × factor. Pure: no Unity or interop types.
/// </summary>
internal sealed class ProtectionFactorTable
{
    internal const double StepDegrees=5,MaximumDegrees=85;
    internal const int AngleCount=18;
    internal static readonly double[] Angles=Enumerable.Range(0,AngleCount).Select(i=>i*StepDegrees).ToArray();
    internal static readonly ProtectionThreat[] Threats={ProtectionThreat.LongRod,ProtectionThreat.FullBoreKE,ProtectionThreat.Jet,ProtectionThreat.Hesh};
    // The reference shots of the composer (Wave1 PRESETS.md): a 600 mm long rod of 20 mm at 1600 m/s, a 300 mm full-bore round, a 120 mm HESH.
    internal const double RodReferenceMm=600,FullBoreReferenceMm=300,HeshReferenceCalibreMm=120,RodDiameterM=.02;
    internal static readonly double ReferenceEnergyJ=.5*17500*.6*1600d*1600*Math.PI*.25*RodDiameterM*RodDiameterM;
    internal string Label{get;}
    internal string Key{get;}
    internal double DepthMm{get;}
    internal double PassiveRha{get;}
    internal bool Tilted{get;}
    private readonly double[] with,against;
    internal bool Composer {get;}
    private ProtectionFactorTable(string label,string key,double depth,double rha,bool tilted,double[] with,double[] against,bool composer=false)
    {Composer=composer;Label=label;Key=key;DepthMm=depth;PassiveRha=rha;Tilted=tilted;this.with=with;this.against=against;}
    private static int Index(ProtectionThreat threat,int angle)=>(int)threat*AngleCount+angle;

    // Passive module calibration is an explicit layer preset, expressed per kg RHA.
    // The bounding collider includes the flange; divide the layer work by that exact
    // envelope depth so decorative thickness cannot grant extra protection.
    internal static ProtectionFactorTable BuildModule(CompositeModulePreset preset)
    {
        var values=new double[Threats.Length*AngleCount];
        foreach(var threat in Threats)for(int i=0;i<AngleCount;i++)
            values[Index(threat,i)]=threat==ProtectionThreat.Hesh
                ?preset.Factor(false)*Math.Cos(Angles[i]*Math.PI/180)
                :preset.Factor(threat==ProtectionThreat.Jet);
        var key=Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(preset))));
        return new(CompositeModule.DisplayName,key,CompositeModule.DepthMm,preset.Factor(false),false,values,values,true);
    }

    /// <summary>0.22.36: the game always uses the arithmetic mean of the two authored sides, at every angle.</summary>
    internal double Factor(ProtectionThreat threat,double angleDegrees)
    {
        var w=Lookup(with,threat,angleDegrees);
        var mean=Tilted?(w+Lookup(against,threat,angleDegrees))*.5:w;
        return GroupedProtection.Scale(mean,Composer);
    }
    private static double Lookup(double[] values,ProtectionThreat threat,double angleDegrees)
    {
        var a=Math.Clamp(double.IsFinite(angleDegrees)?Math.Abs(angleDegrees):0,0,MaximumDegrees)/StepDegrees;
        var i=Math.Min((int)Math.Floor(a),AngleCount-1);
        if(i==AngleCount-1)return values[Index(threat,i)];
        var lo=values[Index(threat,i)];
        return lo+(values[Index(threat,i+1)]-lo)*(a-i);
    }
    internal static double PathMm(double depthMm,double angleDegrees)=>depthMm/Math.Cos(Math.Clamp(angleDegrees,0,89.9)*Math.PI/180);
    /// <summary>Protection in mm RHA along the shot path for the cassette's own depth: path × factor (the composer chart and table).</summary>
    internal double ProtectionMm(ProtectionThreat threat,double angleDegrees)=>PathMm(DepthMm,angleDegrees)*Factor(threat,angleDegrees);
    // Native budget multiplier, including the overmatch blend (LOS-MAP section 1). At e=1 this is exactly 1 for every overmatch.
    internal static double NativeAngleMultiplier(double angleDegrees,double exponent,double overmatch=0)
    {
        var c=Math.Max(1e-6,Math.Cos(Math.Clamp(Math.Abs(angleDegrees),0,89.999)*Math.PI/180));
        var om=Math.Clamp(overmatch,0,1);
        return Math.Pow(c,exponent-1)*(1-om)+om;
    }
    internal static double NativeRhaFactor(double factor,double angleDegrees,double exponent,double overmatch=0,double apModifier=1)=>
        factor*apModifier*NativeAngleMultiplier(angleDegrees,exponent,overmatch);
    // 0.22.90 begin
    // User 16-11, "Anyway, fix. Dit had al eerder gezien moeten worden, delen door 0...": native divides by the rhaFactor EB writes
    // (CompoundStructure.SimulateFragment 0x1D782C2: B = GetBasePenetration x apModifier / rhaFactor). At 0, B is +inf and the overmatch blend
    // (B - c*B)*om + c*B (0x1D78333) is NaN: the continuation becomes a ricochet child at a NaN position and the vehicle vanishes (VANISH-MAP section 2).
    // Every native rhaFactor write passes this floor. Every v from 1e-6f up (finite or +inf) comes back bit for bit; [0, 1e-6) and negatives give
    // exactly 1e-6f. NaN passes unchanged: the callers reject non-finite values themselves. Cost of the floor: 1e-6 x path, about 0.0002 mm RHA on 236 mm.
    internal const float MinimumNativeRhaFactor=1e-6f;
    internal static float NativeSafe(float v)=>v<MinimumNativeRhaFactor?MinimumNativeRhaFactor:v;
    // 0.22.90 end
    internal static double Overmatch(double mass,double speed,double diameter,double pathM,double angleDegrees)
    {
        var c=Math.Max(1e-6,Math.Cos(angleDegrees*Math.PI/180));var t=pathM*c;
        var ratio=(.5*mass*speed*speed*c)/(t*t*t*(Math.PI/4)*diameter*diameter*1e13/c+1e4);
        return Math.Clamp(ratio*ratio,0,1);
    }
    /// <summary>The table bucket of a shot: HESH, jet (HEAT, HEAT-FS, tandem, ATGM), long rod (APFSDS), full-bore KE (AP family, APDS, APCR). HE and fragments: none.</summary>
    internal static ProtectionThreat? ThreatOf(MaterialThreatTags tags)
    {
        if(!tags.Original)return null;
        if(tags.UsesHesh)return ProtectionThreat.Hesh;
        if(tags.UsesJet)return ProtectionThreat.Jet;
        if(tags.Behavior=="apfsds"||tags.Contains("longRod"))return ProtectionThreat.LongRod;
        if(tags.UsesRod||tags.UsesClassic)return ProtectionThreat.FullBoreKE;
        return null;
    }
    internal static string Text(ProtectionThreat threat)=>threat switch
    {ProtectionThreat.LongRod=>"long rod",ProtectionThreat.FullBoreKE=>"full-bore KE",ProtectionThreat.Jet=>"jet",_=>"HESH"};

    /// <summary>The composer protection L (mm RHA) of one shot; the factor is L / path.</summary>
    private static double Protection(CassetteModel model,ProtectionThreat threat,double angleDegrees,bool withTilt,double heshRetention)
    {
        var r=angleDegrees*Math.PI/180;var depth=model.DepthMm;
        var native=depth*model.Passive.RhaFactor/Math.Cos(r);
        if(threat==ProtectionThreat.Hesh)return depth*model.Passive.RhaFactor/heshRetention;
        var ray=new Vector3(0,(float)(Math.Sin(r)*(withTilt?1:-1)),(float)-Math.Cos(r));
        var tags=threat switch
        {
            ProtectionThreat.Jet=>new MaterialThreatTags("","chemicalJet"),
            ProtectionThreat.LongRod=>new MaterialThreatTags("","longRod"),
            _=>new MaterialThreatTags("ap","kineticFullBore")
        };
        var e=model.Evaluate(depth,ray,Vector3.UnitZ,Vector3.UnitY,Vector3.UnitX,tags,RodDiameterM,ReferenceEnergyJ,true);
        return threat switch
        {
            ProtectionThreat.Jet=>native+e.JetWorkMm,
            ProtectionThreat.LongRod=>Kinetic(native,RodReferenceMm,e.TotalRodWorkJ/ReferenceEnergyJ,true),
            _=>Kinetic(native,FullBoreReferenceMm,e.TotalRodWorkJ/ReferenceEnergyJ,false)
        };
    }
    /// <summary>
    /// The residual law of the reference penetrator P after the native line of sight and the work fraction w, as the RHA length with the same
    /// residual: P·(1 - ((1 - native/P)^(2/1.43) - w)^(1.43/2)). Below P/2 this is the composer formula of 0.22.23 unchanged; for a longer
    /// line of sight the reference grows to 2·native (a long rod's fixed work in joules is then a smaller share of its larger energy), so the
    /// factor never collapses because the reference itself would be stopped.
    /// </summary>
    internal static double Kinetic(double nativeMm,double referenceMm,double workFraction,bool fixedWork)
    {
        var p=Math.Max(referenceMm,2*nativeMm);
        var w=Math.Max(0,double.IsFinite(workFraction)?workFraction:0)*(fixedWork?referenceMm/p:1);
        var rest=Math.Max(0,Math.Pow(Math.Max(0,1-nativeMm/p),2/1.43)-w);
        return p*(1-Math.Pow(rest,1.43/2));
    }
    internal static ProtectionFactorTable Build(CassetteModel model,string? label=null,bool composer=true)
    {
        var key=KeyOf(model);
        lock(cache){if(cache.TryGetValue(key+(composer?"/composer":"/simple"),out var hit))return hit;}
        var tilted=false;foreach(var g in model.Groups)tilted|=Math.Abs(g.Sin)>1e-9;
        var hesh=Math.Clamp(model.HeshRetention(HeshReferenceCalibreMm).Retention,1e-3,1);
        double[] Side(bool side)
        {
            var values=new double[Threats.Length*AngleCount];
            foreach(var threat in Threats)for(int i=0;i<AngleCount;i++)
            {
                var path=PathMm(model.DepthMm,Angles[i]);
                var l=Protection(model,threat,Angles[i],side,hesh);
                values[Index(threat,i)]=double.IsFinite(l)&&l>0&&path>0?l/path:model.Passive.RhaFactor;
            }
            return values;
        }
        var w=Side(true);var table=new ProtectionFactorTable(label??model.Preset.Label,key,model.DepthMm,model.Passive.RhaFactor,tilted,w,tilted?Side(false):w,composer);
        lock(cache){if(cache.Count>=512)cache.Clear();cache[key+(composer?"/composer":"/simple")]=table;}
        return table;
    }
    /// <summary>
    /// Every loaded cassette (by its material id) plus every loose mod material: a cassette with one 100 mm layer, exactly
    /// like the composer's legacy draft. A fragment absorber has no effect on the original penetrator and gets no table (native only).
    /// </summary>
    internal static IReadOnlyDictionary<string,ProtectionFactorTable> ForMaterials(IReadOnlyDictionary<string,CassetteModel> cassettes,MaterialReactionCatalogue reactions,
        IReadOnlyDictionary<string,CassetteMaterial> materials,Func<string,string> label,Action<string,string>? unavailable=null,IReadOnlySet<string>? reactiveMaterials=null)
    {
        var tables=new Dictionary<string,ProtectionFactorTable>(StringComparer.Ordinal);
        foreach(var (id,model) in cassettes)tables[id]=Build(model);
        foreach(var id in materials.Keys)
        {
            if(id is "rha" or "sheetMetal" or "sand"||CompositePlate.IsCassette(id)||tables.ContainsKey(id)||reactiveMaterials?.Contains(id)==true)continue;
            if(reactions.Bindings.TryGetValue(id,out var binding)&&reactions.Recipes[binding.RecipeId].Mode=="fragmentAbsorber")continue;
            var preset=new CassettePreset("loose_"+id,label(id),"1900.01.01",1,4000,new[]{new CassetteLayer(id,LooseLayerMm,0,"up")});
            try{tables[id]=Build(new CassetteModel(preset,materials,reactions),composer:false);}
            catch(FormatException ex){unavailable?.Invoke(id,ex.Message);}
        }
        return tables;
    }
    /// <summary>The single layer of a loose material: the composer's legacy draft (CassetteComposerModel.DefaultLegacyMm).</summary>
    internal const double LooseLayerMm=100;
    private static readonly Dictionary<string,ProtectionFactorTable> cache=new(StringComparer.Ordinal);
    internal static int CacheCount{get{lock(cache)return cache.Count;}}
    /// <summary>The recipe hash: the layers, the passive values and every recipe number the layers use. Equal recipe and display identities share one table.</summary>
    internal static string KeyOf(CassetteModel model)
    {
        // JSON includes array contents and every recipe/calibration number, independent of the current culture.
        // Include display identity so equal physical recipes never reuse another material's report label.
        var recipe=JsonSerializer.Serialize(new {model.Preset,model.Passive,model.Work,model.Layers,Steel=model.SteelIds});
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(recipe)));
    }
}

/// <summary>0.22.28: the one report line per block, "Leopard 2A7 wedge: 552 mm × 0.51 (long rod @66°) = 281 mm RHA".</summary>
internal static class ProtectionLine
{
    internal const string Marker=" mm RHA";
    internal static string Text(string label,double pathMm,double factor,ProtectionThreat threat,double angleDegrees)
    {
        var c=CultureInfo.InvariantCulture;
        return string.Create(c,$"{label}: {pathMm:0} mm × {factor:0.00} ({ProtectionFactorTable.Text(threat)} @{angleDegrees:0}°) = {pathMm*factor:0}{Marker}");
    }
    internal static bool Is(string? text)=>text!=null&&text.EndsWith(Marker,StringComparison.Ordinal)&&text.Contains(" mm × ",StringComparison.Ordinal)&&text.Contains(" @",StringComparison.Ordinal);
}

// Constrained struct adapter: explicit value-array write/read without allocating closures per block.
internal interface IRhaArray
{
    float Read(int index);
    void Write(int index,float value);
}
internal static class MaterialFactorWrite
{
    // Always attempt both native arrays: failure of the simulation-array accessor
    // must not strand the separately supplied argument array at EB's temporary value.
    internal static bool RestorePair<T>(ref T primary,ref T extra,bool hasExtra,int index,float original,float extraOriginal,Action<Exception>? failed=null) where T:struct,IRhaArray
    {
        Exception? first=null,second=null;
        try{Set(ref primary,index,original,out var read);if(read!=original)throw new InvalidOperationException("MaterialInfo restore read-back mismatch.");}
        catch(Exception ex){first=ex;}
        try{if(hasExtra){Set(ref extra,index,extraOriginal,out var read);if(read!=extraOriginal)throw new InvalidOperationException("MaterialInfo argument restore read-back mismatch.");}}
        catch(Exception ex){second=ex;}
        // Logging cannot interrupt restoration of the other array.
        if(first!=null)failed?.Invoke(first);if(second!=null)failed?.Invoke(second);
        return first==null&&second==null;
    }
    internal static float Set<T>(ref T array,int index,float value,out float readBack) where T:struct,IRhaArray
    {
        var original=array.Read(index);
        array.Write(index,value);
        readBack=array.Read(index);
        return original;
    }
}
