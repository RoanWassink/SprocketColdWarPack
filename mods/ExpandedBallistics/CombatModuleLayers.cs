using System.Globalization;
using System.Text.RegularExpressions;
namespace SprocketExpandedBallistics;

/// <summary>
/// 0.22.91: one constituent of a layered module (VARMA, a side module, a cassette: parts that are no armour plate of their own) as the report shows it,
/// front to back along the shot line. <paramref name="PathMm"/>, <paramref name="Factor"/> and <paramref name="RhaMm"/> are the confirmed interval of
/// the hit; <paramref name="NormalMm"/> is derived from the path (path × cos(obliquity)); <paramref name="ReductionMm"/> is the measured reduction of the
/// whole module divided in proportion to the RHA equivalent (derived; null when the module has no measured entry and exit). Display data only.
/// </summary>
internal sealed record ForensicModuleLayer(int Order,int Entity,string Name,string Material,string Colour,double PathMm,double NormalMm,double Angle,double Factor,double RhaMm,double? ReductionMm)
{
    /// <summary>"VARMA-style passive side armour, housing (steel)".</summary>
    internal string Label=>Material.Length>0?$"{Name} ({Material})":Name;
    /// <summary>The short name in the drawing: the role ("housing", "core"), else the material, else the part name.</summary>
    internal string Short=>Name.LastIndexOf(", ",StringComparison.Ordinal) is var i and >=0&&Name[(i+2)..] is "housing" or "core"?Name[(i+2)..]:Material.Length>0?Material:Name;
    /// <summary>"2.7 mm along the path, 2 mm normal (derived from the path) × 1 = 2.7 mm RHA-eq; 2 mm of the measured reduction (derived)".</summary>
    internal string Detail=>$"{CombatForensics.N(PathMm)} mm along the path, {CombatForensics.N(NormalMm)} mm normal{LayeredPlateReport.DerivedLabel} × {Factor.ToString("0.##",CultureInfo.InvariantCulture)} = {CombatForensics.N(RhaMm)} mm RHA-eq"+
        (ReductionMm is {} r?$"; {CombatForensics.N(r)} mm of the measured reduction (derived)":"");
}

/// <summary>
/// 0.22.91 (user 16-11: "ik zou bij dat soort platen als Varma ook de laagjes in de forensic willen zien. hoeft allemaal niet echt gesimuleerd te worden"). Presentation
/// only, called by <see cref="LayeredPlateReport.Apply"/>, so the list, Forensic and the HTML all read the same result; the stored report, its JSON, the hit and the
/// calculation are never touched.
/// <list type="number">
/// <item>A module: a solid group (layerStart, Combined) whose parts are no armour plate (Armour false, no thickness) but whose hit confirmed material intervals.
/// The group and the following groups of the same parts that paid nothing new (the "uncovered" stretches, 0 mm RHA-eq) become one layer with one part (Armour
/// true, so it is a row of the layer table): its normal thickness is derived from the path, its penetration in and out is the measured one, its confirmed
/// intervals are its sublayers (see <see cref="Sublayers"/>).</item>
/// <item>A vehicle part without a material (engine, fuel tank, track, crew) is recorded with the fallback material name "ERA"; it reads "Internal module",
/// "Crew position" or its own type name instead. A part of a merged module reads <see cref="ModuleLabel"/>. Real ERA (a bound cassette) has a material and is not touched.</item>
/// </list>
/// Pure: returns the same list when nothing changes, and applying it twice gives the same result.
/// </summary>
internal static class CombatModuleLayers
{
    internal const string ModuleLabel="Layered module",InternalLabel="Internal module",CrewLabel="Crew position",FallbackLabel="ERA";
    /// <summary>The start of the reaction line of a merged module (a "materialNote" of its hit); <see cref="Is"/> reads it.</summary>
    internal const string NotePrefix="Layered module, ";
    private static readonly Regex NativeId=new(@"^[a-z][A-Za-z0-9]*$",RegexOptions.CultureInvariant);
    private static readonly Regex Geometry=new(@"materialIndex=(?<i>-?\d+);.*?RHAfactor=(?<f>[\d.Ee+-]+); density=(?<d>[\d.Ee+-]+) kg/m3",RegexOptions.CultureInvariant);

    internal static IReadOnlyList<CombatReportEvent> Apply(IReadOnlyList<CombatReportEvent> events)
    {
        if(events==null||events.Count==0)return events??Array.Empty<CombatReportEvent>();
        var merged=Merge(events,out var modules);
        return Relabel(merged,modules);
    }

    /// <summary>True for the events of a merged module's hit (its reaction line).</summary>
    internal static bool Is(IEnumerable<CombatReportEvent> hitEvents)=>hitEvents.Any(e=>e!=null&&e.Kind=="materialNote"&&e.Text.StartsWith(NotePrefix,StringComparison.Ordinal));

    private static bool Plain(CombatLayerPart p)=>p!=null&&!p.Armour&&p.ThicknessMm==null;
    private static string Id(CombatReportEvent start)=>start.HitId??start.Layer!.Id;
    private static PaidMaterialPiece[] Paid(IEnumerable<CombatReportEvent> events,string stage,string id)=>
        events.Where(e=>e!=null&&e.Stage==stage&&e.HitId==id&&e.Kind is "materialReaction" or "materialStop"&&e.PaidIntervals!=null).SelectMany(e=>e.PaidIntervals!)
            .Where(p=>p!=null&&double.IsFinite(p.StartM)&&double.IsFinite(p.EndM)&&p.EndM>p.StartM&&double.IsFinite(p.Factor)).ToArray();
    private static bool HasLedger(IEnumerable<CombatReportEvent> events,string stage,string id)=>
        events.Any(e=>e!=null&&e.Stage==stage&&e.HitId==id&&e.Kind is "materialReaction" or "materialStop"&&e.PaidIntervals!=null);

    private static IReadOnlyList<CombatReportEvent> Merge(IReadOnlyList<CombatReportEvent> events,out Dictionary<(string Stage,int Entity),bool> modules)
    {
        modules=new();
        List<CombatReportEvent>? list=null;
        for(int guard=0;guard<64;guard++)
        {
            var current=(IReadOnlyList<CombatReportEvent>?)list??events;
            var next=MergeFirst(current,modules);
            if(next==null)break;
            list=next;
        }
        return (IReadOnlyList<CombatReportEvent>?)list??events;
    }

    /// <summary>Merges the first module run found; null when there is none.</summary>
    private static List<CombatReportEvent>? MergeFirst(IReadOnlyList<CombatReportEvent> events,Dictionary<(string Stage,int Entity),bool> modules)
    {
        for(int s=0;s<events.Count;s++)
        {
            var start=events[s];
            if(start==null||start.Kind!="layerStart"||start.Layer is not {AirGapMm:null,Combined:true} layer||layer.Parts.Count==0||!layer.Parts.All(Plain))continue;
            // an internal module (crew, engine, ammunition stowage, ...) is never part of a module: the protective-envelope verdict (0.22.43) depends on it
            if(layer.Parts.Any(CombatEnvelope.IsInternal))continue;
            // one placed module: its objects (housing, core) carry the module's own name. Different generic models that one native group happens to cover
            // (T-72A shot 4 of 15-11: "model" at 33.4° and "baseModel" at 49.4° in front of the turret) are no module (review 16-11).
            var names=layer.Parts.Select(p=>(p.Name??"").Trim()).Distinct(StringComparer.Ordinal).ToArray();
            if(names.Length!=1||names[0].Length==0||NativeId.IsMatch(names[0]))continue;
            string stage=start.Stage,id=Id(start);
            var entities=layer.Parts.Select(p=>p.Entity).ToHashSet();
            var pieces=Paid(events,stage,id).Where(p=>entities.Contains(p.Entity)).ToList();
            if(pieces.Count==0||pieces.Sum(p=>p.LengthMm)<CombatLayerContinuity.SettledMm)continue;
            // the following groups of the same parts that paid nothing new (the stretches the ledger had already paid) belong to this module
            var run=new List<int>{s};
            for(int k=s+1;k<events.Count;k++)
            {
                var e=events[k];
                if(e==null||e.Kind!="layerStart"||e.Layer==null||e.Stage!=stage)continue;
                var info=e.Layer;var other=Id(e);
                if(info.AirGapMm!=null||info.Parts.Count==0||!info.Parts.All(Plain)||!info.Parts.All(p=>entities.Contains(p.Entity)))break;
                if(!HasLedger(events,stage,other)||Paid(events,stage,other).Sum(p=>p.LengthMm)>=CombatLayerContinuity.SettledMm)break;
                run.Add(k);
            }
            var ids=run.Select(i=>Id(events[i])).ToHashSet(StringComparer.Ordinal);
            var inside=Enumerable.Range(0,events.Count).Where(i=>events[i]!=null&&(run.Contains(i)||events[i].Stage==stage&&events[i].HitId!=null&&ids.Contains(events[i].HitId!))).ToArray();
            var own=inside.Select(i=>events[i]).ToArray();
            var lastId=Id(events[run[^1]]);
            bool stopped=own.Any(e=>e.Kind is "nativeStop" or "materialStop"||e.Kind=="outcome"&&e.Text.StartsWith("Stopped",StringComparison.OrdinalIgnoreCase));
            var lastResidual=own.LastOrDefault(e=>e.HitId==lastId&&e.Kind is "plateResidual" or "componentResidual")??own.LastOrDefault(e=>e.Kind is "plateResidual" or "componentResidual");
            double? before=start.BeforeMm;
            double? after=stopped?0:lastResidual?.AfterMm;
            pieces.Sort((a,b)=>a.StartM.CompareTo(b.StartM));
            double extentMm=(pieces.Max(p=>p.EndM)-pieces.Min(p=>p.StartM))*1000;
            double angle=Median(pieces);
            if(!(extentMm>0)||!double.IsFinite(extentMm)||!double.IsFinite(angle))continue;
            angle=Math.Clamp(angle,0,89.9);
            var first=layer.Parts.FirstOrDefault(p=>p.Entity==pieces[0].Entity)??layer.Parts[0];
            var part=new CombatLayerPart(first.Entity,CombatReportLanguage.Module(first.Name)+LayeredPlateReport.DerivedLabel,null,ModuleLabel,extentMm*Math.Cos(angle*Math.PI/180),angle,true);
            var created=new List<CombatReportEvent>();
            // the native observations of the module's groups (they precede the group start), now under the module's hit
            foreach(var e in own.Where(e=>e.Kind is "chemicalGeometry" or "chemicalGap"))created.Add(e with{HitId=id});
            created.Add(new("layerStart",stage,"Reached layer",before,HitId:id,Layer:new CombatLayerInfo(id,new[]{part},false,null)));
            bool members=false;
            foreach(var e in own)
            {
                if(e.Kind is "layerStart" or "chemicalGeometry" or "chemicalGap" or "plateResidual" or "componentResidual")continue; // the groups and their exits: one module row and one exit below
                if(e.Kind=="blockMembers"){if(!members){members=true;created.Add(e with{HitId=id});}continue;}
                // the confirmed intervals once; the measured entry and exit belong to the module row (the exit line below), not to this line
                if(e.Kind=="materialReaction"&&e.PaidIntervals is {} paid){if(paid.Length>0)created.Add(e with{HitId=id,BeforeMm=null,AfterMm=null});continue;}
                // anything else of the groups (a stop, an ERA line, a note) stays, under the module's hit and its part
                created.Add(e with{HitId=id,RelatedEntity=e.RelatedEntity is {} related&&entities.Contains(related)?part.Entity:e.RelatedEntity});
            }
            created.Add(new("materialNote",stage,Note(pieces,run.Count-1),HitId:id));
            if(!stopped&&lastResidual!=null)created.Add(lastResidual with{HitId=id,BeforeMm=before,AfterMm=after});
            foreach(var p in layer.Parts)modules[(stage,p.Entity)]=true;
            var result=new List<CombatReportEvent>(events.Count);var remove=inside.ToHashSet();int insertAt=inside.Min();
            for(int i=0;i<events.Count;i++)
            {
                if(i==insertAt)result.AddRange(created);
                if(!remove.Contains(i))result.Add(events[i]);
            }
            return result;
        }
        return null;
    }
    /// <summary>The obliquity half of the confirmed path lies below (weighted by length).</summary>
    private static double Median(IReadOnlyList<PaidMaterialPiece> pieces)
    {
        var finite=pieces.Where(p=>double.IsFinite(p.Angle)).OrderBy(p=>p.Angle).ToArray();
        if(finite.Length==0)return double.NaN;
        double half=finite.Sum(p=>p.LengthMm)/2,run=0;
        foreach(var p in finite){run+=p.LengthMm;if(run>=half)return p.Angle;}
        return finite[^1].Angle;
    }
    /// <summary>The reaction line of the module row: its constituents front to back (the hit's own confirmed intervals).</summary>
    private static string Note(IReadOnlyList<PaidMaterialPiece> pieces,int settled)
    {
        var roles=Roles(pieces);
        var text=NotePrefix+pieces.Count+" layers along the shot line: "+string.Join("; ",pieces.Select((p,i)=>(roles[i].Length>0?roles[i]+": ":"")+
            (p.Factor==1?$"{MaterialIntervalDisplay.Mm(p.LengthMm)} mm RHA":$"{MaterialIntervalDisplay.Mm(p.LengthMm)} mm × {p.Factor.ToString("0.##",CultureInfo.InvariantCulture)} = {MaterialIntervalDisplay.Mm(p.RhaMm)} mm RHA-eq")))+".";
        return settled>0?text+$" {settled} later passage{(settled==1?"":"s")} through the same module paid no further material.":text;
    }
    /// <summary>"housing" for an object that both opens and closes the module with another object between, "core" for one enclosed by it; else empty.</summary>
    private static string[] Roles(IReadOnlyList<PaidMaterialPiece> pieces)
    {
        var roles=new string[pieces.Count];for(int i=0;i<roles.Length;i++)roles[i]="";
        if(pieces.Count<3)return roles;
        int outer=pieces[0].Entity;
        if(pieces[^1].Entity!=outer||pieces.Skip(1).Take(pieces.Count-2).Any(p=>p.Entity==outer))return roles;
        var inner=pieces.Skip(1).Take(pieces.Count-2).Select(p=>p.Entity).Distinct().ToArray();
        for(int i=0;i<pieces.Count;i++)roles[i]=pieces[i].Entity==outer?"housing":inner.Length==1?"core":"";
        return roles;
    }

    /// <summary>
    /// The constituents of a merged module for Forensic, front to back: the confirmed intervals of its hit with the part name, the role (housing, core), the
    /// material (steel for a density of 7700-8000 kg/m³, else the density, read from the shot's own native observations), the path, the normal derived from it, the
    /// factor and the RHA equivalent; the measured reduction of the module (<paramref name="before"/> − <paramref name="after"/>) is divided in proportion to the RHA equivalent.
    /// Null for any other layer.
    /// </summary>
    internal static IReadOnlyList<ForensicModuleLayer>? Sublayers(IReadOnlyList<CombatReportEvent> hitEvents,IReadOnlyList<CombatReportEvent> shotEvents,double? before,double? after)
    {
        if(hitEvents==null||!Is(hitEvents))return null;
        var pieces=hitEvents.Where(e=>e.Kind is "materialReaction" or "materialStop"&&e.PaidIntervals!=null).SelectMany(e=>e.PaidIntervals!)
            .Where(p=>p!=null&&double.IsFinite(p.StartM)&&double.IsFinite(p.EndM)&&p.EndM>p.StartM&&double.IsFinite(p.Factor)).Distinct().OrderBy(p=>p.StartM).ToArray();
        if(pieces.Length==0)return null;
        var densities=new Dictionary<string,double>(StringComparer.Ordinal);
        foreach(var e in shotEvents.Where(e=>e!=null&&e.Kind=="chemicalGeometry"))
        {
            var m=Geometry.Match(e.Text??"");
            if(m.Success&&double.TryParse(m.Groups["d"].Value,NumberStyles.Float,CultureInfo.InvariantCulture,out var d)&&double.IsFinite(d))densities.TryAdd("native:"+m.Groups["i"].Value,d);
        }
        var roles=Roles(pieces);double sum=pieces.Sum(p=>p.RhaMm);
        double? drop=before is {} b&&after is {} a&&double.IsFinite(b)&&double.IsFinite(a)?b-a:null;
        var list=new List<ForensicModuleLayer>();
        for(int i=0;i<pieces.Length;i++)
        {
            var p=pieces[i];
            string name=MaterialIntervalDisplay.Name(p);if(roles[i].Length>0)name+=", "+roles[i];
            bool steel=p.Material is "rha" or "highHardnessSteel" or "sheetMetal"||densities.TryGetValue(p.Material,out var density)&&density>=7700&&density<=8000;
            string material=steel?"steel":densities.TryGetValue(p.Material,out var dens)?dens.ToString("0",CultureInfo.InvariantCulture)+" kg/m³":p.Material.StartsWith("native:",StringComparison.Ordinal)?"":MaterialIntervalDisplay.Name(p);
            string colour=steel?CombatForensics.MaterialColour("rha"):"#c8bca0";
            double angle=double.IsFinite(p.Angle)?Math.Clamp(p.Angle,0,89.9):0;
            list.Add(new(i+1,p.Entity,name,material,colour,p.LengthMm,p.LengthMm*Math.Cos(angle*Math.PI/180),p.Angle,p.Factor,p.RhaMm,drop is {} x&&sum>0?x*p.RhaMm/sum:null));
        }
        return list;
    }
    /// <summary>The cross-section of a module: its constituents along the shot line as fractions of the band (a stretch without a confirmed interval is drawn as air).</summary>
    internal static IReadOnlyList<CompositeSublayer> Drawing(IReadOnlyList<ForensicModuleLayer> layers,IReadOnlyList<CombatReportEvent> hitEvents)
    {
        var pieces=hitEvents.Where(e=>e.Kind is "materialReaction" or "materialStop"&&e.PaidIntervals!=null).SelectMany(e=>e.PaidIntervals!)
            .Where(p=>p!=null&&double.IsFinite(p.StartM)&&double.IsFinite(p.EndM)&&p.EndM>p.StartM&&double.IsFinite(p.Factor)).Distinct().OrderBy(p=>p.StartM).ToArray();
        var result=new List<CompositeSublayer>();
        if(pieces.Length!=layers.Count||pieces.Length==0)return result;
        double from=pieces[0].StartM,extent=pieces[^1].EndM-from,at=from;
        if(!(extent>0))return result;
        for(int i=0;i<pieces.Length;i++)
        {
            var p=pieces[i];var l=layers[i];
            if(p.StartM-at>1e-6)result.Add(new(result.Count+1,"air","Air",CassettePalette.Hex("air"),true,0,0,(p.StartM-at)/extent));
            result.Add(new(result.Count+1,p.Material,l.Label,l.Colour,false,0,l.NormalMm,(p.EndM-Math.Max(at,p.StartM))/extent));
            at=Math.Max(at,p.EndM);
        }
        return result;
    }

    private static IReadOnlyList<CombatReportEvent> Relabel(IReadOnlyList<CombatReportEvent> events,Dictionary<(string Stage,int Entity),bool> modules)
    {
        List<CombatReportEvent>? copy=null;
        for(int i=0;i<events.Count;i++)
        {
            var e=events[i];
            if(e==null||e.Kind!="layerStart"||e.Layer is not {} layer||!layer.Parts.Any(p=>Label(p,e.Stage,modules)!=null))continue;
            var parts=layer.Parts.Select(p=>Label(p,e.Stage,modules) is {} label?p with{MaterialLabel=label}:p).ToArray();
            copy??=events.ToList();
            copy[i]=e with{Layer=layer with{Parts=parts}};
        }
        return (IReadOnlyList<CombatReportEvent>?)copy??events;
    }
    /// <summary>The report's material name for a part recorded with the fallback "ERA" and no material; null when the part keeps its name.</summary>
    internal static string? Label(CombatLayerPart p,string stage,IReadOnlyDictionary<(string Stage,int Entity),bool>? modules=null)
    {
        if(p==null||p.Armour||p.MaterialId!=null||p.MaterialLabel!=FallbackLabel)return null;
        if(modules!=null&&modules.ContainsKey((stage,p.Entity)))return ModuleLabel;
        var name=(p.Name??"").Trim();
        if(name.StartsWith("Crew member",StringComparison.OrdinalIgnoreCase)||name.StartsWith("Crew seat",StringComparison.OrdinalIgnoreCase))return CrewLabel;
        if(CombatEnvelope.IsInternal(p))return InternalLabel;
        if(!NativeId.IsMatch(name))return null; // a part of another mod with its own name (an add-on armour model) is left as recorded
        return CombatForensics.Capital(CombatReportLanguage.Module(name));
    }
}
