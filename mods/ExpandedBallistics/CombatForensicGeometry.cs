namespace SprocketExpandedBallistics;

/// <summary>0.22.41: one constituent of a composer preset inside its plate band, in preset order (front to back).</summary>
internal sealed record ForensicSubBand(CompositeSublayer Layer,float EntryX,float ExitX,float[] Polygon);
/// <summary>Display geometry only. A single plate's band is its normal thickness at its obliquity. 0.22.42: a layer of a composite array has
/// no band of its own (its plates are drawn once in <see cref="ForensicSection.Arrays"/>); it keeps its place on the shot line for its number.</summary>
internal sealed record ForensicBand(ForensicLayer Layer,float EntryX,float ExitX,double? Angle,
    double? NormalMm,float[] Polygon,IReadOnlyList<ForensicSubBand> Subs,float[]? Module=null,float? Top=null,float? Bottom=null);
/// <summary>0.22.42: one plate of a composite array, drawn once with its own normal thickness and obliquity, and its preset constituents.</summary>
internal sealed record ForensicPlateBand(ForensicArrayPlate Plate,float EntryX,float ExitX,double Angle,double NormalMm,float[] Polygon,IReadOnlyList<ForensicSubBand> Subs,float? Top=null,float? Bottom=null);
/// <summary>0.22.42: the plates of a composite array front to back, one continuous band each.</summary>
internal sealed record ForensicArrayBand(ForensicArray Array,float EntryX,float ExitX,IReadOnlyList<ForensicPlateBand> Plates);
/// <summary>0.22.72: an internal module on the shot line, drawn as a dotted frame on the shot line with an icon, "number name" and its result. <paramref name="Mm"/> is the measured
/// path through the module (the frame is then on the millimetre scale); null when the report has none (a frame of fixed width, no number).</summary>
internal sealed record ForensicModuleMark(ForensicLayer Layer,float X0,float X1,double? Mm,IReadOnlyList<string> Kinds,string Name,string Outcome,string State)
{
    internal string Label=>$"{Layer.Number} {Name}";
}
internal sealed record ForensicSection(float Scale,float ShotY,float Top,float Bottom,double ExtentMm,
    int ScaleBarMm,IReadOnlyList<ForensicBand> Bands,IReadOnlyList<ForensicArrayBand> Arrays,IReadOnlyList<ForensicModuleMark>? Marks=null)
{
    internal IReadOnlyList<ForensicModuleMark> Modules=>Marks??System.Array.Empty<ForensicModuleMark>();
    internal const float Front=110;
    internal float X(double mm)=>(float)(Front+mm*Scale);
    /// <summary>The obliquity of the array plate drawn at <paramref name="x"/> on the shot line, if any.</summary>
    internal double? AngleAt(float x)=>Arrays.SelectMany(a=>a.Plates).FirstOrDefault(p=>x>=p.EntryX&&x<=p.ExitX)?.Angle;
    /// <summary>True when the layer belongs to a composite array whose plates are drawn.</summary>
    internal bool Drawn(ForensicLayer layer)=>layer.ArrayFirst>0&&Arrays.Any(a=>a.Array.First==layer.ArrayFirst&&a.Plates.Count>0);
}
internal static class CombatForensicGeometry
{
    internal const float Width=1060,Height=420;
    /// <summary>0.22.43: the display width of an internal module without a measured thickness (an upright grey band, not on the millimetre scale).</summary>
    internal const float ModulePx=14;
    /// <summary>
    /// 0.22.71: a band is drawn from the shot line up and down only as far as it reaches another band; no band runs through the area of another one. Which band stops: the thinner
    /// one (along the shot line); of two equal ones the one that comes later. On the shot line itself every band keeps its place, thickness and angle. Drawing only.
    /// <paramref name="clip"/> false gives the drawing of 0.22.69 (full height; for comparison in the tests).
    /// </summary>
    internal static ForensicSection Build(ForensicModel m,bool clip=true)
    {
        double extent=m.Layers.Select(l=>l.StartMm+(l.PathMm??0)).DefaultIfEmpty(0).Max();
        float scale=(float)((Width-270)/Math.Max(200,extent));
        float y=Height/2,top=Height*.1f,bottom=Height*.9f;
        float[] Polygon(float entry,float exit,double angle,float? y0=null,float? y1=null)
        {
            float yt=y0??top,yb=y1??bottom,tan=(float)Math.Tan(angle*Math.PI/180);
            float shiftTop=(y-yt)*tan,shiftBottom=(y-yb)*tan;
            return new[]{entry+shiftTop,yt,exit+shiftTop,yt,exit+shiftBottom,yb,entry+shiftBottom,yb};
        }
        // The constituents of a preset plate share its band in proportion to their depth along the plate normal.
        List<ForensicSubBand> Split(IReadOnlyList<CompositeSublayer>? list,float entry,float exit,double angle,float? y0=null,float? y1=null)
        {
            var subs=new List<ForensicSubBand>();if(list is not {Count:>0})return subs;
            double at=0;
            foreach(var s in list)
            {
                float a=entry+(float)((exit-entry)*at),b=entry+(float)((exit-entry)*(at+s.Fraction));at+=s.Fraction;
                subs.Add(new(s,a,b,Polygon(a,b,angle,y0,y1)));
            }
            return subs;
        }
        // 0.22.42: each plate of a composite array once, front to back, with its own normal thickness and obliquity; a plate without both
        // measured is listed only (no assumed width).
        var arrays=new List<ForensicArrayBand>();var plateLists=new List<List<ForensicPlateBand>>();
        foreach(var a in m.ArrayRuns)
        {
            float entry=(float)(ForensicSection.Front+a.StartMm*scale),at=entry;var plates=new List<ForensicPlateBand>();
            foreach(var p in a.Plates)
            {
                if(CombatForensics.Path(p.Part) is not {} los||p.Part.AngleDegrees is not {} angle||p.Part.ThicknessMm is not {} normal)continue;
                float from=at,to=at+(float)(los*scale);at=to;
                plates.Add(new(p,from,to,angle,normal,Polygon(from,to,angle),Split(p.Sublayers,from,to,angle)));
            }
            arrays.Add(new(a,entry,at,plates));plateLists.Add(plates);
        }
        var bands=new List<ForensicBand>();var marks=new List<ForensicModuleMark>();float markEnd=float.NegativeInfinity;
        // 0.22.72: an internal module on the shot line is a frame of fixed width at its place (several modules at one place follow each other along the shot line), or, when the
        // report measured the path through it, a frame of that length on the millimetre scale. Only layers on the shot line exist here: nothing is drawn for other parts.
        ForensicModuleMark Mark(ForensicLayer layer,float entry,double? mm)
        {
            float w=mm is >0?Math.Max(8,(float)(mm.Value*scale)):FrameWidth,x0=Math.Max(entry,markEnd+10);
            x0=Math.Min(x0,Math.Max(entry,Width-w-30));markEnd=x0+w;
            string outcome=layer.ModuleOutcome.Length>0?layer.ModuleOutcome:NoDamage;
            var mark=new ForensicModuleMark(layer,x0,x0+w,mm is >0?mm:null,ModuleKinds(layer),layer.Grouped?CombatForensics.Capital(layer.Module):LayerName(layer),outcome,ModuleState(outcome));
            marks.Add(mark);return mark;
        }
        var solid=m.Layers.Where(l=>l.GapMm==null).ToArray();
        for(int i=0;i<solid.Length;i++)
        {
            var layer=solid[i];
            float entry=(float)(ForensicSection.Front+layer.StartMm*scale);
            float exit=(float)(entry+(layer.PathMm??0)*scale);
            if(layer.ArrayFirst>0)
            {
                // Its place on the shot line only; the obliquity is that of the array plate at its entry.
                var plate=arrays.Where(x=>x.Array.First==layer.ArrayFirst).SelectMany(x=>x.Plates).FirstOrDefault(p=>entry>=p.EntryX-.01f&&entry<=p.ExitX);
                bands.Add(new(layer,entry,exit,plate?.Angle,plate?.NormalMm,Array.Empty<float>(),Array.Empty<ForensicSubBand>()));
                continue;
            }
            var member=layer.Parts.Where(p=>p.Armour&&CombatForensics.Path(p)!=null)
                .OrderByDescending(p=>CombatForensics.Path(p)).FirstOrDefault();
            if(layer.Internal&&!layer.Grouped)
            {
                // 0.22.72: an internal module on its own is a framed icon (see Mark); the band keeps its place on the shot line and has no polygon. Nothing on the millimetre scale moves.
                var mk=Mark(layer,entry,layer.PathMm);
                bands.Add(new(layer,mk.X0,mk.X1,null,null,Array.Empty<float>(),Array.Empty<ForensicSubBand>()));
                continue;
            }
            double? angle=member?.AngleDegrees,normal=member?.ThicknessMm;
            float[] polygon=Array.Empty<float>();var subs=new List<ForensicSubBand>();
            if(layer.PathMm!=null&&angle!=null){polygon=Polygon(entry,exit,angle.Value);subs=Split(layer.Sublayers,entry,exit,angle.Value);}
            // 0.22.43: a module grouped with a plate keeps the plate's band (and constituents). 0.22.72: the module is a framed icon of fixed width (the path measured
            // in this layer is the plate's, not the module's).
            if(layer.Grouped)Mark(layer,entry,null);
            bands.Add(new(layer,entry,exit,angle,normal,polygon,subs));
        }
        if(clip)
        {
            // Every band with a slanted area is a slab: its place on the shot line and its slope. The wider slab (along the shot line) is never cut by a narrower one: the narrower one ends along the edge
            // of the wider one it meets, above and below the shot line, so no band runs through the area of another one.
            var slabs=new List<(float Entry,float Exit,double Tan,Action<Func<float[],float[]>> Apply)>();
            for(int i=0;i<bands.Count;i++)
            {
                var b=bands[i];if(b.Polygon.Length==0||b.Angle is not {} a||b.Layer.Internal&&!b.Layer.Grouped)continue;
                int k=i;
                slabs.Add((b.EntryX,b.ExitX,Math.Tan(a*Math.PI/180),cut=>{var nb=bands[k];var poly=cut(nb.Polygon);var subs=Split(nb.Layer.Sublayers,nb.EntryX,nb.ExitX,a).Select(s=>s with{Polygon=cut(s.Polygon)}).Where(s=>s.Polygon.Length>=6).ToList();bands[k]=nb with{Polygon=poly,Subs=subs,Top=Ys(poly).Min,Bottom=Ys(poly).Max};}));
            }
            foreach(var list in plateLists)
                for(int i=0;i<list.Count;i++)
                {
                    var p=list[i];var l=list;int k=i;
                    slabs.Add((p.EntryX,p.ExitX,Math.Tan(p.Angle*Math.PI/180),cut=>{var np=l[k];var poly=cut(np.Polygon);var subs=Split(np.Plate.Sublayers,np.EntryX,np.ExitX,np.Angle).Select(s=>s with{Polygon=cut(s.Polygon)}).Where(s=>s.Polygon.Length>=6).ToList();l[k]=np with{Polygon=poly,Subs=subs,Top=Ys(poly).Min,Bottom=Ys(poly).Max};}));
                }
            for(int i=0;i<slabs.Count;i++)
            {
                var si=slabs[i];var planes=new List<(bool Right,float Edge,double Tan)>();
                for(int j=0;j<slabs.Count;j++)
                {
                    if(j==i)continue;var sj=slabs[j];double wi=si.Exit-si.Entry,wj=sj.Exit-sj.Entry;
                    bool stronger=wj>wi+1e-4||Math.Abs(wj-wi)<=1e-4&&(sj.Entry<si.Entry||sj.Entry==si.Entry&&j<i);
                    if(!stronger||Math.Abs(si.Tan-sj.Tan)<1e-9)continue;
                    double c1=sj.Exit-si.Entry,c2=si.Exit-sj.Entry;
                    if(c1>1e-3&&c2>1e-3)continue; // they overlap on the shot line itself: nothing to cut from
                    // j lies to the right of i: i keeps what is left of the left edge of j; to the left: what is right of the right edge of j
                    if(c2<=1e-3)planes.Add((true,sj.Entry,sj.Tan));else planes.Add((false,sj.Exit,sj.Tan));
                }
                if(planes.Count==0)continue;
                si.Apply(poly=>
                {
                    foreach(var (right,edge,tan) in planes)
                    {
                        float F(float x,float yy)=>right?x-(edge+(y-yy)*(float)tan):(edge+(y-yy)*(float)tan)-x; // <= 0 is kept
                        poly=ClipHalf(poly,F);if(poly.Length<6)break;
                    }
                    return poly;
                });
            }
        }
        int bar=new[]{50,100,200,500}.OrderBy(mm=>Math.Abs(mm*scale-Width*.15)).First();
        return new(scale,y,top,bottom,extent,bar,bands,arrays,marks);
    }
    /// <summary>0.22.72: the width of the dotted frame of an internal module without a measured path (not on the millimetre scale).</summary>
    internal const float FrameWidth=34;
    internal const string NoDamage="no damage recorded";
    internal const string StateSevere="severe",StateDamaged="damaged",StateNone="none";
    /// <summary>0.22.72: "destroyed" and "incapacitated" are one state, "damaged" and "wounded" another, anything else (no damage recorded) a neutral one.</summary>
    internal static string ModuleState(string outcome)=>outcome is "destroyed" or "incapacitated"?StateSevere:outcome is "damaged" or "wounded"?StateDamaged:StateNone;
    /// <summary>0.22.72: the icon kinds of the internal parts of a layer (distinct, in shot-line order): crew, gun, ammunition, engine, fuel, transmission, else a neutral block.</summary>
    internal static IReadOnlyList<string> ModuleKinds(ForensicLayer layer)
    {
        var kinds=new List<string>();
        foreach(var p in layer.Parts.Where(CombatEnvelope.IsInternal))
        {
            var n=(p.Name??"").Trim().ToLowerInvariant().Replace(" ","");
            string kind=n.StartsWith("crew")?"crew":n is "cannon" or "maingun" or "gun" or "breech"?"gun":n is "ammorack"?"ammunition":n is "engine" or "motor" or "radiator"?"engine":
                n is "fueltank"?"fuel":n is "transmission" or "gearbox"?"transmission":"block";
            if(!kinds.Contains(kind))kinds.Add(kind);
        }
        if(kinds.Count==0)kinds.Add("block");
        return kinds;
    }
    /// <summary>
    /// Where the number of every layer is written (the same rule as 0.22.42: on the upper visible part of its band, thin bands staggering down along the band). 0.22.71: a band that is cut short by
    /// another band keeps its number on the part that is drawn.
    /// </summary>
    internal static IReadOnlyList<(ForensicLayer Layer,float X,float Y)> Badges(ForensicSection g,ForensicModel m)
    {
        float y=g.ShotY;
        var result=new List<(ForensicLayer,float,float)>();var badges=new List<(float X,float Y)>();
        foreach(var l in m.Layers)
        {
            if(l.Internal&&!l.Grouped)continue; // 0.22.72: an internal module on its own carries its number in its label ("11 Gunner").
            var b=g.Bands.FirstOrDefault(b=>ReferenceEquals(b.Layer,l));
            float cx=g.X(l.StartMm+(l.PathMm??0)/2),ly=g.Top+12;
            // 0.22.71: a band that is cut short keeps its number on the part that is drawn.
            var plateBand=g.Drawn(l)?g.Arrays.SelectMany(a=>a.Plates).FirstOrDefault(p=>cx>=p.EntryX&&cx<=p.ExitX):null;
            float? bandTop=b?.Top??plateBand?.Top,bandBottom=b?.Bottom??plateBand?.Bottom;
            if(bandTop is {} bt)ly=Math.Max(ly,bt+12);
            // A layer of a composite array follows the slant of the plate drawn at its place on the shot line.
            double tan=Math.Tan(((g.Drawn(l)?g.AngleAt(cx):null)??b?.Angle??0)*Math.PI/180);
            if(tan>.001)ly=Math.Max(ly,(float)(y-(Width-26-cx)/tan));
            float lx=cx+(float)((y-ly)*tan);
            for(int step=0;step<9&&badges.Any(p=>Math.Abs(p.X-lx)<25&&Math.Abs(p.Y-ly)<23);step++)
            {ly+=24;lx=cx+(float)((y-ly)*tan);}
            if(bandBottom is {} bb&&ly>bb-10){ly=Math.Max(bandTop??g.Top,bb-10);lx=cx+(float)((y-ly)*tan);}
            lx=Math.Clamp(lx,16,Width-16);ly=Math.Clamp(ly,g.Top+10,g.Bottom-10);badges.Add((lx,ly));result.Add((l,lx,ly));
        }
        return result;
    }
    private static (float Min,float Max) Ys(float[] poly){float lo=float.MaxValue,hi=float.MinValue;for(int i=1;i<poly.Length;i+=2){lo=Math.Min(lo,poly[i]);hi=Math.Max(hi,poly[i]);}return poly.Length==0?(0,0):(lo,hi);}
    /// <summary>A convex polygon cut by the line F = 0; what has F &lt;= 0 stays (Sutherland-Hodgman).</summary>
    internal static float[] ClipHalf(float[] poly,Func<float,float,float> f)
    {
        var result=new List<float>();int n=poly.Length/2;
        for(int i=0;i<n;i++)
        {
            float x0=poly[2*i],y0=poly[2*i+1],x1=poly[2*((i+1)%n)],y1=poly[2*((i+1)%n)+1];float d0=f(x0,y0),d1=f(x1,y1);
            if(d0<=0){result.Add(x0);result.Add(y0);}
            if(d0<0&&d1>0||d0>0&&d1<0){float t=d0/(d0-d1);result.Add(x0+(x1-x0)*t);result.Add(y0+(y1-y0)*t);}
        }
        return result.ToArray();
    }
    /// <summary>The short label on the shot line of an ERA layer.</summary>
    internal static string EraLabel(ForensicLayer layer)=>layer.Era switch
    {
        "ERA activated"=>"ERA initiated","ERA already spent"=>"ERA previously initiated",
        "ERA not initiated"=>"ERA not initiated","ERA unavailable"=>"ERA not initiated",_=>layer.Era
    };
    /// <summary>0.22.42: a layer of a composite array reads "Composite array"; its plates follow by name (<see cref="Members"/>).</summary>
    internal static string LayerName(ForensicLayer layer)=>layer.Array?CombatReportLanguage.ArrayName:layer.Internal&&!layer.Grouped?CombatForensics.Capital(layer.Name):layer.Name;
    /// <summary>0.22.43: "crew position" or "internal module".</summary>
    internal static string ModuleKind(ForensicLayer layer)=>layer.Parts.Where(CombatEnvelope.IsInternal).All(p=>p.Name.StartsWith("Crew",StringComparison.OrdinalIgnoreCase))?"crew position":"internal module";
    /// <summary>0.22.43: the indented row of a module grouped with a plate: "internal module in the same layer as Plate #508".</summary>
    internal static string ModuleDetail(ForensicLayer layer)=>ModuleKind(layer)+(layer.Plate.Length>0?" in the same layer as "+layer.Plate:"");
    /// <summary>The plates of a composite array front to back by their material names (as the user named them in the composer).</summary>
    internal static string Members(ForensicLayer layer)=>layer.Plates is {Count:>0} plates?string.Join(" → ",plates.Select(p=>p.Material)):
        string.Join(" → ",layer.Parts.Where(p=>p.Armour).Select(CombatForensics.PlateMaterial).Distinct());
    /// <summary>The small text after a layer name in the list: plate name and geometry; for a composite array its plates front to back.</summary>
    internal static string Detail(ForensicLayer layer)
    {
        var parts=new List<string>();
        if(layer.Array)parts.Add(Members(layer));
        else if(layer.Internal&&!layer.Grouped)parts.Add(ModuleKind(layer)); // 0.22.43: "internal module" or "crew position"
        else
        {
            if(layer.Plate.Length>0&&layer.Plate!=layer.Name)parts.Add(layer.Plate);
            if(layer.Geometry.Length>0)parts.Add(layer.Geometry);
            // 0.22.43: "Plate #508 · 55 mm at 23.8° · with fuel tank".
            if(layer.Grouped)parts.Add("with "+(ModuleKind(layer)=="crew position"?"crew position":layer.Module));
        }
        if(layer.Stage=="Precursor")parts.Add("precursor charge");
        return string.Join(" · ",parts);
    }
    /// <summary>"5 mm at 67.3°" for an array plate; only what was measured in this layer.</summary>
    internal static string PlateGeometry(CombatLayerPart p)=>p.ThicknessMm!=null&&p.AngleDegrees!=null?$"{CombatForensics.N(p.ThicknessMm)} mm at {CombatForensics.N(p.AngleDegrees)}°":
        p.ThicknessMm!=null?$"{CombatForensics.N(p.ThicknessMm)} mm":p.AngleDegrees!=null?$"obliquity {CombatForensics.N(p.AngleDegrees)}°":"";
    /// <summary>"Rolled Homogeneous Armour · 5 mm at 67.3°": the material and geometry after an array plate's name (no share).</summary>
    internal static string PlateDetail(ForensicArrayPlate p)=>string.Join(" · ",new[]{p.Material==p.Name?"":p.Material,PlateGeometry(p.Part)}.Where(s=>s.Length>0));
    /// <summary>"Rolled Homogeneous Armour 4.6 mm, tilted 30°" for one constituent (its extent along the plate normal).</summary>
    internal static string SubDetail(CompositeSublayer s)=>$"{CombatForensics.N(s.NormalMm)} mm"+(s.TiltDegrees>0?$", tilted {CombatForensics.N(s.TiltDegrees)}°":"");
    /// <summary>Every fixed text the forensic screen shows (title, mode toggle, list title, notes, column heads).</summary>
    internal static readonly string[] FixedTexts={ForensicModel.Title,ForensicModel.ModeLabel,ForensicModel.ListTitle,ForensicModel.GeometryNote,ForensicModel.Footnote,
        ForensicModel.FindingsTitle,ForensicModel.AdviceTitle,ForensicModel.ResidualLabel,"Increase between layers","Reduction between layers","Close"};
}
