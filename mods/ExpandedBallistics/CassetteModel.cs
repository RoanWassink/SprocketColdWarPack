using System.Globalization;
using System.Numerics;
namespace SprocketExpandedBallistics;

internal sealed record CassetteMaterial(string Id,double Density,double RhaFactor,double SpallFactor,double CostMultiplier);
internal readonly record struct CassetteTilt(double Sin,double Cos,string Direction);
internal sealed record PreparedCassetteLayer(CassetteLayer Layer,MaterialReactionRecipe? Recipe,int Group,
    bool Supported,double Coupling,double NeraAirFactor,double RepeatFactor,double GapBehindMm,
    double FrontGapMm,double RearGapMm,int SourceLayerIndex);
internal readonly record struct CassetteEvaluation(bool InWindow,double WorkJ,double JetWorkMm,double ErosionShare,
    double ReservedWorkJ,double YawDegrees,Vector3 DisturbanceNormal,ulong ActiveLayers,bool Reverse=false,string? ScaleNote=null,
    double EffectiveAngleDegrees=0,double EffectiveThicknessMm=0,double RawAngleDegrees=0,string? TiltGroupText=null,double AngleGain=1,double AngleMaxDegrees=0)
{ internal double TotalRodWorkJ=>WorkJ+ReservedWorkJ; }
internal readonly record struct CassetteHeshRetention(double Retention,double GapMm);

internal sealed class CassetteModel
{
    internal CassettePreset Preset {get;}
    internal PreparedCassetteLayer[] Layers {get;}
    internal PreparedCassetteLayer[] ReverseLayers {get;}
    internal PreparedCassetteLayer[] Traversal(bool reverse)=>reverse?ReverseLayers:Layers;
    internal CassetteTilt[] Groups {get;}
    internal double DepthMm {get;}
    internal CassetteMaterial Passive {get;}
    internal double MassKgM2=>Passive.Density*DepthMm*.001;
    internal double CostM2=>MassKgM2*Passive.CostMultiplier;
    internal bool HasBacked {get;}
    internal bool HasNera {get;}
    private readonly MaterialWorkCalibration work;
    private readonly string[] steel;
    internal MaterialWorkCalibration Work=>work;
    internal IReadOnlyList<string> SteelIds=>steel;
    internal CassetteModel(CassettePreset preset,IReadOnlyDictionary<string,CassetteMaterial> materials,MaterialReactionCatalogue reactions,IReadOnlySet<string>? reactiveMaterials=null)
    {
        Preset=preset;work=reactions.Work;steel=reactions.SteelMaterialIds;
        var groups=new List<CassetteTilt>();double mass=0,resistance=0,cost=0,spall=0;
        foreach(var layer in preset.Layers)
        {
            var depth=layer.Material=="air"?layer.ThicknessMm:layer.ThicknessMm/Math.Cos(layer.TiltDegrees*Math.PI/180);DepthMm+=depth;
            if(layer.Material=="air")continue;
            if(layer.Material.StartsWith("ebCassette_",StringComparison.Ordinal)||reactiveMaterials?.Contains(layer.Material)==true)throw new FormatException(preset.Id+": nested cassettes and ERA layers are not supported.");
            if(!materials.TryGetValue(layer.Material,out var material))throw new FormatException(preset.Id+": unknown layer material "+layer.Material);
            if(!double.IsFinite(material.Density)||material.Density<=0||!double.IsFinite(material.RhaFactor)||material.RhaFactor<=0||!double.IsFinite(material.CostMultiplier)||material.CostMultiplier<0)throw new FormatException("Invalid cassette passive material: "+material.Id);
            mass+=material.Density*depth;resistance+=material.RhaFactor*depth;cost+=material.Density*depth*material.CostMultiplier;spall=material.SpallFactor;
        }
        if(DepthMm<preset.MinimumDepthMm||DepthMm>preset.MaximumDepthMm)throw new FormatException(preset.Id+": nominal depth is outside window.");
        Passive=new(preset.MaterialId,mass/DepthMm,resistance/DepthMm,spall,1.2*cost/mass);
        Layers=Prepare(preset.Id,preset.Layers,false,materials,reactions,groups); // 0.22.90 replaced
        ReverseLayers=Prepare(preset.Id,preset.Layers.Reverse().ToArray(),true,materials,reactions,groups); // 0.22.90 replaced
        Groups=groups.ToArray();HasBacked=Layers.Any(l=>l.Recipe?.Mode=="backed");
        for(int i=0;i<Layers.Length;i++)
            if(Layers[i].Recipe?.Id=="elastomerSandwich"||Layers[i].Layer.Material.Contains("nera",StringComparison.OrdinalIgnoreCase))
            {HasNera=true;break;}
    }
    private static PreparedCassetteLayer[] Prepare(string presetId,CassetteLayer[] layers,bool reverse, // 0.22.90 replaced
        IReadOnlyDictionary<string,CassetteMaterial> materials,MaterialReactionCatalogue reactions,List<CassetteTilt> groups)
    {
        var prepared=new List<PreparedCassetteLayer>();var repeats=new Dictionary<string,int>(StringComparer.Ordinal);
        for(int i=0;i<layers.Length;i++)
        {
            var layer=layers[i];if(layer.Material=="air")continue;
            if(!materials.TryGetValue(layer.Material,out var material))throw new FormatException(presetId+": layer material "+layer.Material+" is missing from the Technology files.");MaterialReactionRecipe? recipe=null; // 0.22.90 replaced: a FormatException with the preset and the material, not a KeyNotFoundException
            if(reactions.Bindings.TryGetValue(layer.Material,out var binding))
            {
                if(binding.Density!=material.Density||binding.RhaFactor!=material.RhaFactor||binding.SpallFactor!=material.SpallFactor)throw new FormatException("Cassette passive binding differs for "+material.Id);
                recipe=reactions.Recipes[binding.RecipeId];
                if(recipe.Mode=="fragmentAbsorber")recipe=null;
                else if(recipe.Mode is not ("backed" or "sandwich"))recipe=recipe with {MaximumLoss=Math.Min(recipe.MaximumLoss,reactions.MaximumInteractionLoss)};
            }
            bool Steel(int n)=>n>=0&&n<layers.Length&&Array.IndexOf(reactions.SteelMaterialIds,layers[n].Material)>=0&&layers[n].ThicknessMm>=(recipe?.MinimumSteelThicknessMm??3);
            int rear=i+1,front=i-1;double rearGap=0,frontGap=0;
            while(rear<layers.Length&&layers[rear].Material=="air")rearGap+=layers[rear++].ThicknessMm;
            while(front>=0&&layers[front].Material=="air")frontGap+=layers[front--].ThicknessMm;
            bool supported=true;double coupling=1;
            if(recipe?.Mode is "backed" or "sandwich")
            {
                supported=Steel(rear)&&(recipe.Mode!="sandwich"||Steel(front));
                coupling=MaterialReactionModel.Coupling(rearGap,recipe.MaximumGapMm)*(recipe.Mode=="sandwich"?MaterialReactionModel.Coupling(frontGap,recipe.MaximumGapMm):1);
            }
            double gap=rearGap;
            if(recipe?.Id=="elastomerSandwich"&&Steel(rear)&&rear+1<layers.Length&&layers[rear+1].Material=="air")gap+=layers[rear+1].ThicknessMm;
            var nera=recipe?.Id=="elastomerSandwich"?NeraAirFactor(gap,layer.ThicknessMm):1;
            var repeat=1d;
            if(recipe!=null){repeats.TryGetValue(recipe.Id,out var count);repeat=Math.Pow(.85,count);repeats[recipe.Id]=count+1;}
            var radians=layer.TiltDegrees*Math.PI/180;var group=new CassetteTilt(Math.Sin(radians),Math.Cos(radians),layer.TiltDirection);int g=groups.IndexOf(group);
            if(g<0){g=groups.Count;groups.Add(group);}
            prepared.Add(new(layer,recipe,g,supported,coupling,nera,repeat,gap,frontGap,rearGap,reverse?layers.Length-1-i:i));
        }
        return prepared.ToArray();
    }
    internal const double NeraAirWarnBelow=.8;
    internal static double NeraAirFactor(double gapMm,double coreMm)=>.3+.7*Math.Min(1,Math.Max(0,gapMm)/coreMm);
    // A short report tag for the recipe that supplied a scaled cassette response.
    internal static string ShortName(MaterialReactionRecipe recipe)=>recipe.Id switch
    {
        "elastomerSandwich"=>"NERA","textoliteSandwich"=>"textolite","ceramicStrikeFace"=>"ceramic",
        "perforatedHhs"=>"perforated HHS","hardSteel"=>"HHS","advancedSteel"=>"advanced steel",
        "heavyAlloy"=>"heavy alloy","duMesh"=>"DU mesh","aluminaCeramic"=>"alumina","siliconCarbide"=>"SiC",_=>recipe.Label
    };
    // null: this recipe deliberately has no reaction for the threat bucket.
    // 1: legacy recipe without a threatScale object.
    internal static double? ScaleFor(MaterialReactionRecipe recipe,string category)
    {
        if(recipe.ThreatScale is not {} values)return 1;
        return values.TryGetValue(category,out var value)?value:null;
    }
    private double LayerDepth(CassetteLayer layer)=>layer.Material=="air"?layer.ThicknessMm:layer.ThicknessMm/Math.Cos(layer.TiltDegrees*Math.PI/180);
    // HESH: a shockwave decouples over air and thick non-steel layers before the
    // last steel layer. Same exponential curve as SpacedRetention for HESH.
    internal CassetteHeshRetention HeshRetention(double calibreMm,bool reverse=false)
    {
        if(!double.IsFinite(calibreMm)||calibreMm<=0)return new(1,0);
        // Air is not a prepared solid layer; walk the authored stack so the real
        // air thickness before the last steel layer is counted.
        var source=reverse?Preset.Layers.Reverse().ToArray():Preset.Layers;
        int lastSteel=-1;
        for(int i=0;i<source.Length;i++)
            if(Array.IndexOf(steel,source[i].Material)>=0)lastSteel=i;
        if(lastSteel<0)return new(1,0);
        double gap=0;
        for(int i=0;i<lastSteel;i++)
        {
            var layer=source[i];
            if(layer.Material=="air"){gap+=layer.ThicknessMm;continue;}
            if(Array.IndexOf(steel,layer.Material)>=0)continue;
            var depth=LayerDepth(layer);
            if(depth>=10)gap+=depth;
        }
        if(gap<=0)return new(1,0);
        return new(Math.Max(.1,Math.Exp(-12*gap/calibreMm)),gap);
    }
    // Directions belong to the VEHICLE, independently of component attachment or
    // rotation. Project vehicle up/down/left/right onto this face's front plane;
    // vehicle-forward is the fallback when that projection vanishes (roof, etc.).
    // The same front normal is used from either hit side; only traversal reverses.
    internal static Vector3 TiltNormal(Vector3 normal,Vector3 up,Vector3 forward,CassetteTilt tilt)
    {
        bool vertical=tilt.Direction is "up" or "down";
        var right=Vector3.Normalize(Vector3.Cross(up,forward));
        var target=vertical?up:right;
        float sign=tilt.Direction is "down" or "left"?-1:1;
        var tangent=target-normal*Vector3.Dot(target,normal);
        if(tangent.LengthSquared()<1e-8f)tangent=forward-normal*Vector3.Dot(forward,normal);
        // With valid vehicle axes, forward can only be parallel for a side-facing
        // target; use vehicle-up as a final numerical degeneracy guard.
        if(tangent.LengthSquared()<1e-8f)tangent=up-normal*Vector3.Dot(up,normal);
        tangent=Vector3.Normalize(tangent)*sign;
        return normal*(float)tilt.Cos+tangent*(float)tilt.Sin;
    }
    // 0.22.17 point 4: the physics keeps the 80 degree cap; the report shows the uncapped angle.
    internal const double MaximumEffectiveAngle=80;
    internal static double RawAngle(Vector3 direction,Vector3 normal)=>Math.Acos(Math.Clamp(Math.Abs(Vector3.Dot(direction,normal)),0,1))*180/Math.PI;
    internal static double EffectiveAngle(Vector3 direction,Vector3 normal)=>Math.Min(MaximumEffectiveAngle,RawAngle(direction,normal));
    internal static double GroupDegrees(CassetteTilt tilt)=>Math.Atan2(tilt.Sin,tilt.Cos)*180/Math.PI;
    // At or above the cap the true angle is hidden by the physics clamp, so the report says so.
    internal static string AngleText(double rawDegrees)=>rawDegrees>=MaximumEffectiveAngle-1e-9
        ?"≥ 80°":rawDegrees.ToString("0.#",CultureInfo.InvariantCulture)+"°";
    // "θ_eff ≈ 18°" below the cap, "θ_eff ≥ 80°" at it, plus one entry per tilt group when several paid.
    internal static string AngleReport(CassetteEvaluation e)
    {
        var raw=e.RawAngleDegrees>0?e.RawAngleDegrees:e.EffectiveAngleDegrees;
        var capped=raw>=MaximumEffectiveAngle-1e-9;
        var text="θ_eff "+(capped?"≥ 80°":"≈ "+raw.ToString("0.#",CultureInfo.InvariantCulture)+"°");
        if(e.AngleGain<1-1e-9)
            text+=string.Create(CultureInfo.InvariantCulture,$" (above max {e.AngleMaxDegrees:0.#}\u00B0: gain reduced to {e.AngleGain*100:0.#}%)");
        return e.TiltGroupText is {Length:>0} groups?text+"; tilt groups ["+groups+"]":text;
    }
    // A sandwich pays at the tilt of its core. Steel plates that lean differently only change the depth.
    internal IReadOnlyList<(int Layer,string Text)> TiltNotes()
    {
        var notes=new List<(int,string)>();var source=Preset.Layers;
        foreach(var core in Layers)
        {
            if(core.Recipe?.Mode!="sandwich"||!core.Supported)continue;
            int i=core.SourceLayerIndex;
            int front=i-1,rear=i+1;
            while(front>=0&&source[front].Material=="air")front--;
            while(rear<source.Length&&source[rear].Material=="air")rear++;
            var neighbours=new[]{front,rear}.Where(n=>n>=0&&n<source.Length&&Array.IndexOf(steel,source[n].Material)>=0).ToArray();
            var differing=neighbours.Where(n=>TiltDiffers(source[n],source[i])).ToArray();
            if(differing.Length==0)continue;
            var c=source[i];
            string Tilt(CassetteLayer l)=>l.TiltDegrees<=0?"0°":string.Create(CultureInfo.InvariantCulture,$"{l.TiltDegrees:0.#}° {l.TiltDirection}");
            notes.Add((i,$"{ShortName(core.Recipe)} sandwich: steel plates tilt {string.Join(" / ",differing.Select(n=>Tilt(source[n])))} but the core tilts {Tilt(c)}. The hit angle is taken from the core ({Tilt(c)}); the steel plates' tilt only changes the depth."));
        }
        return notes;
    }
    /// <summary>
    /// 0.22.19: cooperative layers (sandwich, backed) whose effective angle at this shot is above the recipe maximum, so the gain
    /// is reduced (the same taper the hit uses, on the same capped angle). The shot is the composer's preview ray: angle to the
    /// plate normal, turned toward the tilt (<paramref name="withTilt"/>) or away from it. Nothing else is reported.
    /// </summary>
    internal IReadOnlyList<(int Layer,string Text)> AngleNotes(double angleDegrees,bool withTilt,bool tiltedLayers)
    {
        var notes=new List<(int,string)>();var r=angleDegrees*Math.PI/180;
        var ray=new Vector3(0,(float)(Math.Sin(r)*(withTilt?1:-1)),(float)-Math.Cos(r));
        foreach(var layer in Layers)
        {
            var recipe=layer.Recipe;
            if(recipe is not {Mode:"backed" or "sandwich"}||!layer.Supported||!(recipe.MaximumAngleDegrees<90))continue;
            var raw=RawAngle(ray,TiltNormal(Vector3.UnitZ,Vector3.UnitY,Vector3.UnitX,Groups[layer.Group]));
            var counted=Math.Min(MaximumEffectiveAngle,raw);
            var gain=MaterialReactionModel.TaperFactor(recipe,counted);
            if(!(gain<1))continue;
            var shot=string.Create(CultureInfo.InvariantCulture,$"{angleDegrees:0}°")+(tiltedLayers?withTilt?" with tilt":" against tilt":"");
            var effective=raw>counted+1e-9?string.Create(CultureInfo.InvariantCulture,$"{raw:0.#}° (counted as {counted:0.#}°)"):string.Create(CultureInfo.InvariantCulture,$"{raw:0.#}°");
            notes.Add((layer.SourceLayerIndex,string.Create(CultureInfo.InvariantCulture,
                $"{ShortName(recipe)} at {shot}: effective angle {effective} is above the recipe maximum {recipe.MaximumAngleDegrees:0.#}°; gain reduced to {gain*100:0}%.")));
        }
        return notes;
    }
    private static bool TiltDiffers(CassetteLayer a,CassetteLayer b)=>
        Math.Abs(a.TiltDegrees-b.TiltDegrees)>1e-6||a.TiltDegrees>0&&a.TiltDirection!=b.TiltDirection;
    internal CassetteEvaluation Evaluate(double plateDepthMm,Vector3 direction,Vector3 normal,Vector3 up,Vector3 forward,
        string threat,double diameterM,double incomingEnergyJ,bool intact,bool reverse=false)
        =>Evaluate(plateDepthMm,direction,normal,up,forward,new MaterialThreatTags("",threat),diameterM,incomingEnergyJ,intact,reverse);
    internal CassetteEvaluation Evaluate(double plateDepthMm,Vector3 direction,Vector3 normal,Vector3 up,Vector3 forward,
        MaterialThreatTags threats,double diameterM,double incomingEnergyJ,bool intact,bool reverse=false,ThicknessMode thicknessMode=ThicknessMode.Uniform)
    {
        var minimum=thicknessMode==ThicknessMode.ToBackPlane?BackplaneThickness.MinimumMm:Preset.MinimumDepthMm;
        var maximum=thicknessMode==ThicknessMode.ToBackPlane?2*DepthMm:Preset.MaximumDepthMm;
        if(!double.IsFinite(plateDepthMm)||plateDepthMm<minimum||plateDepthMm>maximum)return default;
        var scale=plateDepthMm/DepthMm;bool rod=threats.UsesRod,classic=threats.UsesClassic;
        if(!rod&&!classic&&!threats.UsesJet)return new(true,0,0,0,0,0,normal,0,reverse);
        var layers=Traversal(reverse);var category=threats.Category;
        Span<double> angles=stackalloc double[64];Span<double> raws=stackalloc double[64];Span<Vector3> normals=stackalloc Vector3[64];
        for(int g=0;g<Groups.Length;g++){normals[g]=TiltNormal(normal,up,forward,Groups[g]);raws[g]=RawAngle(direction,normals[g]);angles[g]=Math.Min(MaximumEffectiveAngle,raws[g]);}
        double joules=0,jet=0,weightedShare=0,reserved=0,yaw=0,classicDose=0,classicRetention=1,strongest=0;
        double strongestWork=-1,effectiveAngle=0,effectiveRaw=0,effectiveThickness=0;ulong groupMask=0;MaterialReactionRecipe? strongestRecipe=null;
        string? note=null;Vector3 disturbance=Vector3.Zero;ulong mask=0;
        for(int i=0;i<layers.Length;i++)
        {
            var layer=layers[i];var recipe=layer.Recipe;
            if(recipe==null||!layer.Supported||!threats.Matches(recipe.ThreatTags))continue;
            var threatScale=ScaleFor(recipe,category);if(threatScale is null)continue;
            var coupling=recipe.Mode is "backed" or "sandwich"?MaterialReactionModel.Coupling(layer.RearGapMm*scale,recipe.MaximumGapMm)*(recipe.Mode=="sandwich"?MaterialReactionModel.Coupling(layer.FrontGapMm*scale,recipe.MaximumGapMm):1):1;
            if(classic)
            {
                var t=layer.Layer.ThicknessMm*scale;if(t<recipe.MinimumThicknessMm||t>recipe.MaximumThicknessMm)continue;
                var factorClassic=layer.NeraAirFactor*layer.RepeatFactor*threatScale.Value;double retention;
                if(recipe.Mode is "backed" or "sandwich")
                {
                    var dose=MaterialWorkMath.ClassicDose(recipe,t,angles[layer.Group],coupling,intact)*factorClassic;
                    retention=(1+classicDose)/(1+classicDose+dose);classicDose+=dose;
                }
                else retention=1-MaterialWorkMath.ClassicDirectLoss(recipe,t,angles[layer.Group],recipe.MaximumLoss)*factorClassic;
                classicRetention*=retention;if(retention<1){mask|=1UL<<i;groupMask|=1UL<<layer.Group;}
                if(1-retention>strongestWork){strongestWork=1-retention;effectiveAngle=angles[layer.Group];effectiveRaw=raws[layer.Group];effectiveThickness=t;strongestRecipe=recipe;}
                if(threatScale.Value!=1&&(1-retention)>strongest){strongest=1-retention;note=ShortName(recipe)+" \u00D7"+threatScale.Value.ToString("0.##",CultureInfo.InvariantCulture);}
                continue;
            }
            var w=MaterialWorkMath.Calculate(recipe,work,rod,layer.Layer.ThicknessMm*scale,angles[layer.Group],coupling,intact,diameterM);
            var factor=layer.NeraAirFactor*layer.RepeatFactor*threatScale.Value;double j=w.Joules*factor,mm=w.JetMm*factor;
            if(j<=0&&mm<=0)continue;mask|=1UL<<i;groupMask|=1UL<<layer.Group;
            var layerWork=j+mm;
            if(layerWork>strongestWork){strongestWork=layerWork;effectiveAngle=angles[layer.Group];effectiveRaw=raws[layer.Group];effectiveThickness=layer.Layer.ThicknessMm*scale;strongestRecipe=recipe;}
            if(threatScale.Value!=1&&j>strongest){strongest=j;note=ShortName(recipe)+" \u00D7"+threatScale.Value.ToString("0.##",CultureInfo.InvariantCulture);}
            joules+=j;jet+=mm;weightedShare+=j*w.ErosionShare;
            var r=j*w.DisturbanceShare;
            reserved+=r;yaw+=r*w.YawDegrees*(1+Math.Min(1,layer.GapBehindMm*scale/50));disturbance+=normals[layer.Group]*(float)r;
        }
        jet=Math.Min(jet,.75*work.JetBudgetMm);
        if(classic)joules=incomingEnergyJ*(1-Math.Pow(classicRetention,2/1.43));
        double cap=.75*Math.Max(0,incomingEnergyJ),multiplier=joules>cap?cap/joules:1;
        var erosion=joules>0?weightedShare/joules:0;
        var yawDegrees=reserved>0?yaw/reserved:0;reserved*=multiplier;joules*=multiplier;
        if(disturbance.LengthSquared()>1e-16f)disturbance=Vector3.Normalize(disturbance);else disturbance=normal;
        string? groupText=null;
        if(strongestWork>=0&&System.Numerics.BitOperations.PopCount(groupMask)>1)
        {
            var parts=new List<string>();
            for(int g=0;g<Groups.Length;g++)
                if((groupMask&(1UL<<g))!=0)
                    parts.Add(string.Create(CultureInfo.InvariantCulture,$"{GroupDegrees(Groups[g]):0.#}° {Groups[g].Direction}: {AngleText(raws[g])}"));
            groupText=string.Join(", ",parts);
        }
        return new(true,joules-reserved,jet,erosion,reserved,yawDegrees,disturbance,mask,reverse,note,
            strongestWork<0?0:effectiveAngle,effectiveThickness,strongestWork<0?0:effectiveRaw,groupText,
            strongestRecipe is {Mode:"backed" or "sandwich"} cooperative?MaterialReactionModel.TaperFactor(cooperative,effectiveAngle):1,strongestRecipe?.MaximumAngleDegrees??0);
    }
}
