using System.Globalization;
using System.Net;
using System.Text;
namespace SprocketExpandedBallistics;

/// <summary>The same immutable drawing feeds the Kit Painter, HTML SVG and headless raster tests.</summary>
internal static class CombatForensicDrawing
{
    internal const float Width=1060,BlastHeight=340;
    internal const string TopLabel="STRIKE FACE  →  SHOT LINE  →  BEHIND ARMOUR",BlastTopLabel="EXTERIOR  →  DETONATION  →  INTERIOR",NoLayers="No armour layer data";
    internal static float Height(ForensicModel m)=>m.IsPureBlast?BlastHeight:CombatForensicGeometry.Height;
    /// <summary>Kit hatch angle (degrees, screen y down) of stripes parallel to a plate face at obliquity <paramref name="obliquity"/>, turned by a layer tilt.</summary>
    internal static float FaceHatch(double obliquity,double tilt)=>(float)(90+obliquity+tilt);
    private static string StopLabel(ForensicLayer l)
    {
        if(l.PlateStop is not {} at)return $"Arrested in layer {l.Number}";
        var sub=CombatForensics.StopSublayer(l);
        return sub!=null?$"Arrested in layer {at.Group} of {at.Groups} ({sub.Label})":$"Arrested in layer {at.Group} of {at.Groups}";
    }
    internal static void Draw(ISectionSink d,ForensicModel m,bool heat,bool clip=true)
    {
        if(m.IsPureBlast&&m.Blast!=null){DrawBlast(d,m,m.Blast);return;}
        var g=CombatForensicGeometry.Build(m,clip);float h=Height(m),y=g.ShotY;
        d.ViewBox(Width,h);d.Poly(new float[]{0,0,Width,0,Width,h,0,h},"#13191f",null,0);
        d.BeginClip(Width,h);
        foreach(var band in g.Bands)
        {
            var l=band.Layer;
            // A layer without measured geometry (crew position, internal module) is a thin mark at its place on the shot line;
            // 0.22.42: a layer of a composite array is drawn through its plates below.
            // 0.22.72: an internal module is a framed icon (drawn below), not a band. A layer of another kind without a polygon keeps its thin mark.
            if(band.Polygon.Length==0){if(!g.Drawn(l)&&!l.Internal)d.Line(band.EntryX,y-46,band.EntryX,y+46,"#9aa6b2",1.5f);continue;}
            Fill(d,band.Polygon,band.Subs,heat?CombatForensics.Heat(l.Percent):l.Colour,heat,band.Angle??0);
            d.Region("layer-"+l.Number,band.Polygon);
        }
        // 0.22.42: a composite array draws each plate once, front to back, with its own normal thickness and obliquity, and inside a preset
        // plate the preset's constituents. The heat-map colours the array by the share of all its layers together.
        foreach(var a in g.Arrays)foreach(var p in a.Plates)
        {
            Fill(d,p.Polygon,p.Subs,heat?CombatForensics.Heat(a.Array.Percent):p.Plate.Colour,heat,p.Angle);
            d.Region($"layer-{a.Array.First}-plate-{p.Plate.Order}",p.Polygon);
        }
        // 0.22.43: the arrest marker follows the arrest itself; after a perforated envelope the outcome reads PENETRATED but the arrest is still drawn.
        var stopped=m.Arrested||m.Outcome.StartsWith("STOPPED",StringComparison.Ordinal);
        var stop=stopped?g.Bands.FirstOrDefault(b=>b.Layer.Stage!="Precursor"&&b.Layer.After==0):null;
        // 0.22.69: in a plate in layers the arrest is drawn where the solid material ran out (computed from the hit), not at the middle of the band.
        float end=stop!=null?stop.Layer.PlateStop is {} place?stop.EntryX+(float)place.Fraction*(stop.ExitX-stop.EntryX):(stop.EntryX+stop.ExitX)/2:g.X(g.ExtentMm);
        // 0.22.91: the residual label sits behind the last module frame or mark when they lie beyond the end of the bands (else where it was).
        float behind=g.Modules.Select(k=>k.X1).Concat(g.Bands.Where(b=>b.Polygon.Length==0&&!b.Layer.Internal).Select(b=>b.ExitX)).Where(x=>x>end).DefaultIfEmpty(end).Max();
        string residualText="Residual "+CombatForensics.N(m.Residual)+" mm RHA-eq";float residualHalf=residualText.Length*3.15f+5;
        float residualX=behind>end?Math.Min((behind+6+Width-24)/2,Width-residualHalf-8):(end+Width-24)/2;
        d.Line(18,y,end,y,"#151a20",7);
        d.Arrow(18,y,Math.Max(26,end),y,"#f1c471",2.5f,9);
        string stopText=stop!=null?StopLabel(stop.Layer):"";
        float stopHalf=stopText.Length*3.15f+5;
        float stopX=Math.Clamp(end,stopHalf+8,Width-stopHalf-8);
        if(stop!=null)
        {
            d.Line(end-6,y-8,end+6,y+8,"#ff8d72",3);d.Line(end-6,y+8,end+6,y-8,"#ff8d72",3);
            d.Line(end,y+10,stopX,y+29,"#ff8d72",1);
            d.Text(stopX,y+43,stopText,"#ffac91",SectionAnchor.MiddleCenter,"#13191fe8");
        }
        else if(m.Outcome=="PENETRATED"&&m.Residual is >0)
        {
            d.Arrow(end,y,Width-24,y,"#8fd99c",3,10);
            d.Text(residualX,y-25,residualText,"#a7e5b1",SectionAnchor.MiddleCenter,"#13191fe8");
        }
        else if(m.Outcome=="RICOCHET")d.Text(Math.Min(Width-100,end+65),y+30,"Ricochet","#b8ad94",SectionAnchor.MiddleCenter,"#13191fe8");
        var first=g.Bands.FirstOrDefault(b=>b.Angle!=null);
        if(first?.Angle is {} theta)
        {
            float x=first.EntryX;double angle=theta*Math.PI/180;
            d.Line(x,y,x+57,y,"#d6e4ec",1);
            d.Line(x,y,x+(float)(57*Math.Cos(angle)),y+(float)(57*Math.Sin(angle)),"#79d8e8",1);
            for(int i=0;i<16;i++)
            {
                double a=angle*i/16,b=angle*(i+1)/16;
                d.Line(x+(float)(36*Math.Cos(a)),y+(float)(36*Math.Sin(a)),x+(float)(36*Math.Cos(b)),y+(float)(36*Math.Sin(b)),"#d6e4ec",1.3f);
            }
            d.Text(x+66,y+75,$"θ = {CombatForensics.N(theta)}°","#b9e9f2",SectionAnchor.MiddleCenter,"#13191fe8");
        }
        // 0.22.91 (owner review 16-11): the sublayers of a layered module are named in the band ("housing", "core") where they are wide enough; when one is too thin,
        // a legend on the top line names them all, front to back.
        var moduleLabels=new List<(float X0,float Y0,float X1,float Y1)>();var moduleLegend=new List<(string Text,string Colour)>();
        foreach(var band in g.Bands.Where(b=>b.Layer.ModuleLayers is {Count:>0}&&b.Subs.Count>0))
        {
            var parts=band.Layer.ModuleLayers!;var solidSubs=band.Subs.Where(s=>!s.Layer.Air).ToList();bool thin=false;
            double tan=Math.Tan((band.Angle??0)*Math.PI/180);float ly=y+20;
            for(int i=0;i<solidSubs.Count&&i<parts.Count;i++)
            {
                var s=solidSubs[i];string text=parts[i].Short;float w=text.Length*6.3f+8;
                if(s.ExitX-s.EntryX<w+4){thin=true;continue;}
                float cx=(s.EntryX+s.ExitX)/2-(float)((ly-y)*tan);
                d.Text(cx,ly,text,"#ffffff",SectionAnchor.MiddleCenter,"#13191fcc");moduleLabels.Add((cx-w/2,ly-10,cx+w/2,ly+10));
            }
            if(thin)
            {
                moduleLegend.Add(($"Layer {band.Layer.Number}:",""));
                for(int i=0;i<parts.Count;i++)moduleLegend.Add((parts[i].Short==parts[i].Material||parts[i].Material.Length==0?parts[i].Short:$"{parts[i].Short} ({parts[i].Material})",parts[i].Colour));
            }
        }
        // Numbers stay on the upper visible part of their band; nearby thin bands stagger down along the band.
        var badges=new List<(float X,float Y,bool Marked)>();
        foreach(var (l,lx,ly) in CombatForensicGeometry.Badges(g,m))
        {d.Text(lx,ly,l.Number.ToString(CultureInfo.InvariantCulture),l.GapMm!=null?"#9baab8":"#ffffff",SectionAnchor.MiddleCenter,"#13191fed");badges.Add((lx,ly,g.Bands.FirstOrDefault(b=>ReferenceEquals(b.Layer,l)) is {Polygon.Length:0}&&!g.Drawn(l)));}
        // Only local reaction callouts. Full material names remain in the absorption list.
        var labels=new List<(float X,float Y,float Half)>();
        // 0.22.43: where the projectile perforated the protective envelope (entry into the first internal module), with the residual penetration there.
        if(EnvelopeLabel(m).Length>0&&g.Bands.FirstOrDefault(b=>b.Layer.Number==m.Protection.FirstLayer) is {} entryBand)
        {
            string text=EnvelopeLabel(m);float half=text.Length*3.15f+5;
            float source=entryBand.EntryX,x=Math.Clamp(source,half+8,Width-half-8),ly=y-62;
            labels.Add((x,ly,half));
            d.Line(source,g.Top+4,source,g.Bottom-4,"#8fd99c",1);
            d.Line(source,y-8,x,ly+10,"#8fd99c",1);
            d.Text(x,ly,text,"#a7e5b1",SectionAnchor.MiddleCenter,"#13191fed");
        }
        foreach(var l in m.Layers.Where(l=>l.Era.Length>0))
        {
            string text=$"{l.Number} "+CombatForensicGeometry.EraLabel(l);float half=text.Length*3.15f+5;
            float source=g.X(l.StartMm+(l.PathMm??0)/2),x=Math.Clamp(source,half+8,Width-half-8),ly=y-34;
            foreach(float offset in new[]{-34f,34f,-58f,58f,-82f})
            {ly=y+offset;if(!labels.Any(p=>Math.Abs(p.X-x)<p.Half+half+8&&Math.Abs(p.Y-ly)<20))break;}
            labels.Add((x,ly,half));d.Line(source,y+(ly<y?-8:8),x,ly+(ly<y?10:-10),"#d9af71",1);
            d.Text(x,ly,text,"#f5d6a0",SectionAnchor.MiddleCenter,"#13191fed");
        }
        // 0.22.72: the internal modules on the shot line, as a dotted frame with an icon above it, "number name" above the icon and the recorded result below.
        var occupied=labels.Select(p=>(X0:p.X-p.Half,Y0:p.Y-10,X1:p.X+p.Half,Y1:p.Y+10)).ToList();
        if(stop!=null)occupied.Add((stopX-stopHalf,y+8,stopX+stopHalf,y+53));
        else if(m.Outcome=="PENETRATED"&&m.Residual is >0)occupied.Add((residualX-90,y-36,residualX+90,y-14));
        else if(m.Outcome=="RICOCHET")occupied.Add((Math.Min(Width-100,end+65)-30,y+20,Math.Min(Width-100,end+65)+30,y+40));
        if(first?.Angle!=null)occupied.Add((first.EntryX+30,y+64,first.EntryX+102,y+86));
        occupied.AddRange(moduleLabels);
        foreach(var mk in g.Modules)DrawModule(d,mk,y,occupied);
        d.EndClip();
        if(g.Modules.Count>0)Legend(d,h-19);
        d.Text(16,17,TopLabel,"#c1c6ca",SectionAnchor.MiddleLeft,"#13191fed");
        // 0.22.91: the legend of the sublayers of a layered module, on the top line after the direction label: a swatch and the name of each, front to back.
        if(moduleLegend.Count>0)
        {
            float lx=16+TopLabel.Length*6.3f+8+24;
            foreach(var (text,colour) in moduleLegend)
            {
                if(colour.Length>0){d.Poly(new[]{lx,12f,lx+10,12f,lx+10,22f,lx,22f},colour,"#5a6570",1);lx+=14;}
                d.Text(lx,17,text,"#c1c6ca",SectionAnchor.MiddleLeft,null);lx+=text.Length*6.3f+12;
            }
        }
        float bar=g.ScaleBarMm*g.Scale,bx=Width-bar-24,by=h-19;
        d.Line(bx,by,bx+bar,by,"#e7edf2",2);d.Line(bx,by-5,bx,by+5,"#e7edf2",1);d.Line(bx+bar,by-5,bx+bar,by+5,"#e7edf2",1);
        d.Text(bx+bar/2,by-13,g.ScaleBarMm+" mm","#e7edf2",SectionAnchor.MiddleCenter,"#13191fed");
        if(m.Layers.Count==0)d.Text(70,y,NoLayers,"#b8ad94",SectionAnchor.MiddleLeft,null);
    }
    /// <summary>0.22.112: dedicated HE/HESH explosive detonation cross-section (shared by UI Kit Painter and HTML SVG).</summary>
    private static void DrawBlast(ISectionSink d,ForensicModel m,BlastReportData blast)
    {
        float h=BlastHeight,y=172,top=44,bottom=292;
        d.ViewBox(Width,h);
        d.Poly(Box(0,0,Width,h),"#13191f",null,0);
        d.BeginClip(Width,h);

        double deg=Math.Clamp(blast.PrimaryObliquityDeg??0,0,50);
        float tan=(float)Math.Tan(deg*Math.PI/180.0);
        double thickMm=blast.PrimaryThicknessMm??20.0;
        float bandPx=Math.Clamp(22f+(float)(thickMm*0.42),24f,84f);
        float frontX=390f,backX=frontX+bandPx;

        // Subtle interior shading behind the plate so exterior vs interior is immediately clear.
        float interiorTopX=backX+(y-top)*tan,interiorBotX=backX+(y-bottom)*tan;
        d.Poly(new[]{interiorTopX,top,Width,top,Width,bottom,interiorBotX,bottom},"#171f27",null,0);

        float[] Slab(float x0,float x1,float yt,float yb)
        {
            float st=(y-yt)*tan,sb=(y-yb)*tan;
            return new[]{x0+st,yt,x1+st,yt,x1+sb,yb,x0+sb,yb};
        }
        void Arc(float cx,float cy,float r,double startDeg,double endDeg,string colour,float widthPx,int steps=20)
        {
            for(int i=0;i<steps;i++)
            {
                double a0=(startDeg+(endDeg-startDeg)*i/steps)*Math.PI/180.0;
                double a1=(startDeg+(endDeg-startDeg)*(i+1)/steps)*Math.PI/180.0;
                d.Line(cx+(float)(r*Math.Cos(a0)),cy+(float)(r*Math.Sin(a0)),cx+(float)(r*Math.Cos(a1)),cy+(float)(r*Math.Sin(a1)),colour,widthPx);
            }
        }
        float[] Star(float cx,float cy,float outerR,float innerR,int rays)
        {
            var pts=new float[rays*4];
            for(int i=0;i<rays*2;i++)
            {
                double a=i*Math.PI/rays-Math.PI/2;
                float r=i%2==0?outerR:innerR;
                pts[2*i]=cx+(float)(r*Math.Cos(a));
                pts[2*i+1]=cy+(float)(r*Math.Sin(a));
            }
            return pts;
        }

        string plateColour=blast.PrimaryMaterialColour.Length>0?blast.PrimaryMaterialColour:"#8095a6";
        float gapHalf=blast.Breached?Math.Clamp((float)((blast.HoleDiameterM??0.35)*90.0),26f,54f):0f;

        // 1. Armour slab (split around breach hole when breached, continuous with surface crater when resisted).
        if(blast.Breached)
        {
            var upper=Slab(frontX,backX,top,y-gapHalf);
            var lower=Slab(frontX,backX,y+gapHalf,bottom);
            d.Poly(upper,plateColour+"cc","#d0d8df",1);
            d.Poly(lower,plateColour+"cc","#d0d8df",1);
            // Highlight ruptured breach edges.
            float sUp=gapHalf*tan,sDn=-gapHalf*tan;
            d.Line(frontX+sUp,y-gapHalf,backX+sUp,y-gapHalf,"#ff8d72",2.5f);
            d.Line(frontX+sDn,y+gapHalf,backX+sDn,y+gapHalf,"#ff8d72",2.5f);
            // Overpressure blast cone projecting into the interior compartment.
            float coneX=710f,coneSpread=Math.Min(108f,gapHalf+56f);
            d.Poly(new[]{backX+sUp,y-gapHalf,coneX,y-coneSpread,coneX,y+coneSpread,backX+sDn,y+gapHalf},"#ff7a6620",null,0);
            d.Line(backX+sUp,y-gapHalf,coneX,y-coneSpread,"#ff8d7277",1.2f);
            d.Line(backX+sDn,y+gapHalf,coneX,y+coneSpread,"#ff8d7277",1.2f);
            Arc(backX+4,y,48,-46,46,"#ff8d72",2.2f);
            Arc(backX+4,y,92,-42,42,"#ff8d72aa",1.6f);
            Arc(backX+4,y,140,-38,38,"#ff8d7255",1.2f);
            // Vertical breach diameter dimension.
            float dimX=(frontX+backX)/2f;
            d.Line(dimX,y-gapHalf+3,dimX,y+gapHalf-3,"#ffac91",1.4f);
            d.Line(dimX-5,y-gapHalf+3,dimX+5,y-gapHalf+3,"#ffac91",1.2f);
            d.Line(dimX-5,y+gapHalf-3,dimX+5,y+gapHalf-3,"#ffac91",1.2f);
            string holeLabel=blast.HoleDiameterM is >0?string.Create(CultureInfo.InvariantCulture,$"Ø {blast.HoleDiameterM:0.00} m breach"):"Breached";
            d.Text(dimX,y,holeLabel,"#ffac91",SectionAnchor.MiddleCenter,"#13191ff2");
            d.Text(backX+118,y-76,"Compartment overpressure","#ffac91",SectionAnchor.MiddleCenter,"#13191fed");
        }
        else
        {
            d.Poly(Slab(frontX,backX,top,bottom),plateColour+"cc","#d0d8df",1);
            // Surface dent / crater on strike face.
            float dentDepth=Math.Min(bandPx*0.32f,10f);
            d.Poly(new[]{frontX+16*tan,y-16,frontX+dentDepth,y,frontX-16*tan,y+16,frontX-4,y+16,frontX-4,y-16},"#13191f",null,0);
            d.Line(frontX+16*tan,y-16,frontX+dentDepth,y,"#f39c52",2f);
            d.Line(frontX+dentDepth,y,frontX-16*tan,y+16,"#f39c52",2f);
            if(blast.ShockPercent is >0)
            {
                Arc(backX+4,y,42,-44,44,"#f3cf8699",1.8f);
                Arc(backX+4,y,82,-40,40,"#f3cf8655",1.3f);
                Arc(backX+4,y,122,-36,36,"#f3cf8633",1.0f);
                d.Text(backX+116,y-58,string.Create(CultureInfo.InvariantCulture,$"Attenuated shock · {blast.ShockPercent:0.#} %"),"#f3cf86",SectionAnchor.MiddleCenter,"#13191fed");
            }
            else
            {
                d.Text(backX+116,y,"Compartment sealed","#a7e5b1",SectionAnchor.MiddleCenter,"#13191fed");
            }
            d.Text(backX+116,y+58,"Resisted · surface dent only","#8fd99c",SectionAnchor.MiddleCenter,"#13191fed");
        }

        // 2. Incoming dashed shell path & exterior detonation burst.
        float detX=frontX-4;
        for(float sx=34;sx<detX-24;sx+=14)d.Line(sx,y,Math.Min(detX-24,sx+8),y,"#c9a86a88",1.6f);
        d.Arrow(detX-34,y,detX-10,y,"#f1c471",2.2f,8);
        Arc(detX,y,38,118,242,"#f39c52",2.2f);
        Arc(detX,y,74,124,236,"#f39c52aa",1.6f);
        Arc(detX,y,112,130,230,"#f39c5255",1.2f);
        d.Poly(Star(detX,y,20,9,10),"#f39c52","#ff7a66",1);
        d.Poly(Star(detX,y,11,5,10),"#fff1c4",null,0);

        string chargeLabel=blast.CalibreMm is >0
            ?string.Create(CultureInfo.InvariantCulture,$"{blast.CalibreMm:0.#} mm {blast.Kind}")+(blast.BreachCapacityMm is >0?string.Create(CultureInfo.InvariantCulture,$" · {blast.BreachCapacityMm:0.#} mm cap."):"")
            :blast.Kind+(blast.BreachCapacityMm is >0?string.Create(CultureInfo.InvariantCulture,$" · {blast.BreachCapacityMm:0.#} mm cap."):"");
        d.Text(156,y-48,chargeLabel,"#f3cf86",SectionAnchor.MiddleCenter,"#13191fed");

        // 3. Stripped ERA tiles visual indicator along upper exterior face when ERA was stripped.
        if(blast.TotalEraStripped>0)
        {
            // Slanted ghost tile hugging the upper strike face + detached brick blown outward.
            d.Poly(Slab(frontX-16,frontX-3,top+14,top+50),"#f39c5222","#f39c52",1.3f);
            float detachedTopX=frontX-54+(y-(top+58))*tan;
            d.Poly(new[]{detachedTopX,top+58,detachedTopX+15,top+52,detachedTopX+22,top+82,detachedTopX+7,top+88},"#e07a4f44","#f39c52",1.2f);
            string eraLabel=blast.EraStripRadiusM is >0
                ?string.Create(CultureInfo.InvariantCulture,$"{blast.TotalEraStripped} ERA stripped · {blast.EraStripRadiusM:0.00} m")
                :$"{blast.TotalEraStripped} ERA stripped";
            d.Text(178,top+34,eraLabel,"#f5d6a0",SectionAnchor.MiddleCenter,"#13191fed");
        }

        // 4. Plate caption & obliquity label.
        float plateTopMidX=(frontX+backX)/2f+(y-top)*tan;
        string plateTitle=blast.PrimaryThicknessMm is >0
            ?$"{blast.PrimaryPlateName} · {CombatForensics.N(blast.PrimaryThicknessMm)} mm"
            :blast.PrimaryPlateName;
        float titleHalf=plateTitle.Length*3.15f+6;
        d.Text(Math.Clamp(plateTopMidX,titleHalf+12,Width-titleHalf-12),top-14,plateTitle,"#e7edf2",SectionAnchor.MiddleCenter,"#13191fed");
        if(blast.PrimaryObliquityDeg is >=5)
        {
            float botX=frontX+(y-bottom)*tan;
            d.Text(Math.Clamp(botX-28,60,Width-60),bottom+14,$"θ = {CombatForensics.N(blast.PrimaryObliquityDeg)}°","#b9e9f2",SectionAnchor.MiddleCenter,"#13191fed");
        }

        // 5. Right-side affected interior crew & module icons (reusing existing crisp flat icons).
        var icons=BlastDamageIcons(m.DamageSummary);
        if(icons.Count>0)
        {
            float panelLeft=732f,panelRight=1036f,panelMid=(panelLeft+panelRight)/2f;
            d.Text(panelMid,top+16,"AFFECTED CREW & MODULES","#b8ad94",SectionAnchor.MiddleCenter,null);
            int cols=icons.Count<=3?icons.Count:3;
            int rows=(icons.Count+cols-1)/cols;
            float colStep=96f,rowStep=74f;
            float startY=y-(rows-1)*rowStep/2f+8f;
            for(int i=0;i<icons.Count;i++)
            {
                int row=i/cols,col=i%cols;
                int inRow=Math.Min(cols,icons.Count-row*cols);
                float cx=panelMid+(col-(inRow-1)/2f)*colStep;
                float cy=startY+row*rowStep;
                var item=icons[i];
                Icon(d,item.Kind,cx,cy-6,item.Colour);
                d.Text(cx,cy+18,item.Label,item.Colour,SectionAnchor.MiddleCenter,"#13191fed");
            }
        }

        d.EndClip();
        if(icons.Count>0)BlastLegend(d,h-17);
        d.Text(16,17,BlastTopLabel,"#c1c6ca",SectionAnchor.MiddleLeft,"#13191fed");
    }
    private static List<(string Kind,string Label,string Colour)> BlastDamageIcons(CombatDamageSummary? d)
    {
        var list=new List<(string Kind,string Label,string Colour)>();
        if(d==null||!d.Any)return list;
        if(d.CrewKilled>0)list.Add(("crew",$"Crew {d.CrewKilled} killed",ColourSevere));
        else if(d.CrewWounded>0)list.Add(("crew",$"Crew {d.CrewWounded} wounded",ColourDamaged));
        static string KindOf(string part)
        {
            var s=part.ToLowerInvariant();
            if(s.Contains("ammo"))return "ammunition";
            if(s.Contains("fuel"))return "fuel";
            if(s.Contains("engine")||s.Contains("motor"))return "engine";
            if(s.Contains("trans")||s.Contains("gear"))return "transmission";
            if(s.Contains("gun")||s.Contains("cannon")||s.Contains("breech"))return "gun";
            return "other";
        }
        static string ShortLabel(string part)=>part.Length>15?part[..14]+"…":CombatForensics.Capital(part);
        foreach(var p in d.PartsDestroyed){if(list.Count>=6)break;list.Add((KindOf(p),ShortLabel(p),ColourSevere));}
        foreach(var p in d.PartsDamaged){if(list.Count>=6)break;list.Add((KindOf(p),ShortLabel(p),ColourDamaged));}
        return list;
    }
    private static void BlastLegend(ISectionSink d,float y)
    {
        float x=16;
        foreach(var (text,colour) in new[]{(LegendSevere,ColourSevere),(LegendDamaged,ColourDamaged)})
        {
            d.Poly(Box(x,y-5,x+10,y+5),colour,null,0);
            d.Text(x+14,y,text,"#c1c6ca",SectionAnchor.MiddleLeft,null);
            x+=14+text.Length*6.3f+22;
        }
    }
    internal const float IconBox=26,IconPitch=30;
    internal const string ColourSevere="#ff7a66",ColourDamaged="#f2b84b",ColourNone="#a6b0ba";
    internal const string LegendSevere="destroyed or incapacitated",LegendDamaged="damaged or wounded",LegendNone=CombatForensicGeometry.NoDamage;
    internal static string StateColour(string state)=>state==CombatForensicGeometry.StateSevere?ColourSevere:state==CombatForensicGeometry.StateDamaged?ColourDamaged:ColourNone;
    private static float[] Box(float x0,float y0,float x1,float y1)=>new[]{x0,y0,x1,y0,x1,y1,x0,y1};
    private static float[] Disc(float cx,float cy,float r){var p=new float[28];for(int i=0;i<14;i++){double a=Math.PI*2*i/14;p[2*i]=cx+(float)(r*Math.Cos(a));p[2*i+1]=cy+(float)(r*Math.Sin(a));}return p;}
    /// <summary>0.22.72: a dotted rectangle (short dashes along each side).</summary>
    private static void Dotted(ISectionSink d,float x0,float y0,float x1,float y1,string colour,float widthPx)
    {
        void Side(float ax,float ay,float bx,float by)
        {
            float len=MathF.Sqrt((bx-ax)*(bx-ax)+(by-ay)*(by-ay));if(len<1)return;float ux=(bx-ax)/len,uy=(by-ay)/len;
            for(float t=0;t<len;t+=7){float e=Math.Min(len,t+4);d.Line(ax+ux*t,ay+uy*t,ax+ux*e,ay+uy*e,colour,widthPx);}
        }
        Side(x0,y0,x1,y0);Side(x1,y0,x1,y1);Side(x1,y1,x0,y1);Side(x0,y1,x0,y0);
    }
    /// <summary>
    /// 0.22.72: a small icon of simple flat shapes for one kind of internal module (crew, gun, ammunition, engine, fuel, transmission, else a neutral block), on a dark chip,
    /// coloured by state. The same shapes feed the panel and the HTML (SVG).
    /// </summary>
    internal static void Icon(ISectionSink d,string kind,float cx,float cy,string colour)
    {
        const string ink="#13191f";
        d.Poly(Box(cx-IconBox/2,cy-IconBox/2,cx+IconBox/2,cy+IconBox/2),"#13191fee",colour,1);
        switch(kind)
        {
            case "crew":
                d.Poly(Disc(cx,cy-5,3.8f),colour,null,0);
                d.Poly(new[]{cx-7,cy+9,cx-6,cy+1,cx-3,cy-1,cx+3,cy-1,cx+6,cy+1,cx+7,cy+9},colour,null,0);break;
            case "gun":
                d.Poly(Box(cx-10,cy-2,cx+1,cy+2),colour,null,0);
                d.Poly(Box(cx+1,cy-5,cx+9,cy+5),colour,null,0);
                d.Poly(Box(cx+3,cy+5,cx+8,cy+8),colour,null,0);
                d.Poly(Box(cx+3,cy-2,cx+7,cy+2),ink,null,0);break;
            case "ammunition":
                foreach(float i in new[]{-6f,0f,6f})
                {
                    d.Poly(Box(cx+i-2,cy-2,cx+i+2,cy+9),colour,null,0);
                    d.Poly(new[]{cx+i-2,cy-2,cx+i,cy-8,cx+i+2,cy-2},colour,null,0);
                    d.Poly(Box(cx+i-2,cy+5,cx+i+2,cy+6),ink,null,0);
                }
                break;
            case "engine":
                d.Poly(Box(cx-8,cy-3,cx+6,cy+7),colour,null,0);
                d.Poly(Box(cx-6,cy-8,cx-2,cy-3),colour,null,0);
                d.Poly(Box(cx,cy-8,cx+4,cy-3),colour,null,0);
                d.Poly(Box(cx+6,cy,cx+10,cy+3),colour,null,0);
                d.Poly(Box(cx-5,cy,cx+3,cy+1.5f),ink,null,0);break;
            case "fuel":
                d.Poly(Box(cx-6,cy-6,cx+6,cy+8),colour,null,0);
                d.Poly(Box(cx-2,cy-9,cx+2,cy-6),colour,null,0);
                d.Poly(new[]{cx,cy-3,cx+3,cy+2,cx+2,cy+5,cx-2,cy+5,cx-3,cy+2},ink,null,0);break;
            case "transmission":
                d.Poly(Disc(cx-3,cy+2,6.5f),colour,null,0);d.Poly(Disc(cx-3,cy+2,2.2f),ink,null,0);
                d.Poly(Disc(cx+6,cy-4,4f),colour,null,0);d.Poly(Disc(cx+6,cy-4,1.4f),ink,null,0);break;
            default:
                d.Poly(Box(cx-6,cy-6,cx+6,cy+6),colour,null,0);d.Poly(Box(cx-2.5f,cy-2.5f,cx+2.5f,cy+2.5f),ink,null,0);break;
        }
    }
    private static bool Overlaps(List<(float X0,float Y0,float X1,float Y1)> list,float x0,float y0,float x1,float y1)=>list.Any(r=>x0<r.X1+8&&x1>r.X0-8&&y0<r.Y1+2&&y1>r.Y0-2);
    private static void DrawModule(ISectionSink d,ForensicModuleMark mk,float y,List<(float X0,float Y0,float X1,float Y1)> occupied)
    {
        string colour=StateColour(mk.State);float cx=(mk.X0+mk.X1)/2,top=y-13,bottom=y+13;
        d.Poly(Box(mk.X0,top,mk.X1,bottom),colour+"26",null,0);
        Dotted(d,mk.X0,top,mk.X1,bottom,colour,1.6f);
        d.Region(mk.Layer.Grouped?"layer-"+mk.Layer.Number+"-module":"layer-"+mk.Layer.Number,Box(mk.X0,top,mk.X1,bottom));
        // Above: the icons (one per kind) and the name with its number; the whole stack moves up when it would cover another label.
        int n=mk.Kinds.Count;float iconsHalf=(n*IconPitch-(IconPitch-IconBox))/2,half=mk.Label.Length*3.15f+5;
        float nameX=Math.Clamp(cx,half+8,Width-half-8);
        float iy=0,ny=0;
        for(int k=0;k<4;k++)
        {
            iy=y-34-k*40;ny=iy-24;
            if(!Overlaps(occupied,cx-iconsHalf,iy-IconBox/2,cx+iconsHalf,iy+IconBox/2)&&!Overlaps(occupied,nameX-half,ny-10,nameX+half,ny+10))break;
        }
        occupied.Add((cx-iconsHalf,iy-IconBox/2,cx+iconsHalf,iy+IconBox/2));occupied.Add((nameX-half,ny-10,nameX+half,ny+10));
        d.Line(cx,iy+IconBox/2,cx,top,colour,1);
        for(int i=0;i<n;i++)Icon(d,mk.Kinds[i],cx-iconsHalf+IconBox/2+i*IconPitch,iy,colour);
        d.Text(nameX,ny,mk.Label,"#ffffff",SectionAnchor.MiddleCenter,"#13191fed");
        // Below: the measured length (only when the report has it), then the result.
        float ty=bottom+14;
        if(mk.Mm is {} mm){d.Text(cx,ty,CombatForensics.N(mm)+" mm","#e7edf2",SectionAnchor.MiddleCenter,"#13191fed");ty+=20;}
        float ohalf=mk.Outcome.Length*3.15f+5,ox=Math.Clamp(cx,ohalf+8,Width-ohalf-8),oy=ty;
        for(int j=0;j<5;j++)
        {
            oy=ty+j*22;
            if(!Overlaps(occupied,ox-ohalf,oy-10,ox+ohalf,oy+10))break;
        }
        occupied.Add((ox-ohalf,oy-10,ox+ohalf,oy+10));
        if(oy-9>bottom+4)d.Line(cx,bottom,ox,oy-9,colour,1);
        d.Text(ox,oy,mk.Outcome,colour,SectionAnchor.MiddleCenter,"#13191fed");
    }
    /// <summary>0.22.72: the legend of the three states, shown only when icons are in the picture.</summary>
    private static void Legend(ISectionSink d,float y)
    {
        float x=16;
        foreach(var (text,colour) in new[]{(LegendSevere,ColourSevere),(LegendDamaged,ColourDamaged),(LegendNone,ColourNone)})
        {
            d.Poly(Box(x,y-5,x+10,y+5),colour,null,0);
            d.Text(x+14,y,text,"#c1c6ca",SectionAnchor.MiddleLeft,null);
            x+=14+text.Length*6.3f+22;
        }
    }
    /// <summary>
    /// One band: a plain fill, or (0.22.41) the constituents of a composer preset front to back in the composer colours; tilted constituents
    /// carry stripes at their tilt, air carries the composer's air hatch.
    /// </summary>
    private static void Fill(ISectionSink d,float[] polygon,IReadOnlyList<ForensicSubBand> subs,string colour,bool heat,double angle)
    {
        if(subs.Count==0){d.Poly(polygon,colour+"c0","#d0d8df",1);return;}
        foreach(var s in subs)
        {
            if(heat){d.Poly(s.Polygon,colour+"c0",null,0);continue;}
            if(s.Layer.Air){d.Poly(s.Polygon,CassettePalette.Hex("air")+"e6",null,0);d.Hatch(s.Polygon,CassettePalette.AirHatchHex+"b0",45,7,1);}
            else
            {
                d.Poly(s.Polygon,s.Layer.Colour+"ee",null,0);
                if(s.Layer.TiltDegrees>0)d.Hatch(s.Polygon,CassettePalette.TiltHex,FaceHatch(angle,s.Layer.TiltDegrees),6,1);
            }
        }
        for(int i=1;i<subs.Count;i++){var p=subs[i].Polygon;if(p.Length>=8)d.Line(p[0],p[1],p[p.Length-2],p[p.Length-1],heat?"#13191f80":"#13191fcc",.8f);}
        d.Poly(polygon,"#00000000","#d0d8df",1);
    }
    /// <summary>0.22.43: "Envelope perforated · 203.1 mm RHA-eq" at the entry into the first internal module.</summary>
    internal static string EnvelopeLabel(ForensicModel m)=>m.Protection.Perforated&&m.Protection.EntryMm!=null?"Envelope perforated · "+CombatForensics.N(m.Protection.EntryMm)+" mm RHA-eq":"";
    internal static string Svg(ForensicModel m,bool heat,string? prefix=null)
    {
        var sink=new SvgSink(Height(m),prefix??$"shot-{m.Number}-{(heat?"heat":"material")}");Draw(sink,m,heat);return sink.Finish();
    }
    private sealed class SvgSink:ISectionSink
    {
        private readonly StringBuilder s=new();private int clips,hatches;private readonly string prefix;
        private static string F(float x)=>x.ToString("0.###",CultureInfo.InvariantCulture);
        private static string E(string x)=>WebUtility.HtmlEncode(x);
        internal SvgSink(float height,string prefix){this.prefix=E(prefix);s.Append($"<svg xmlns='http://www.w3.org/2000/svg' class='forensic-section' role='img' aria-label='Forensic cross-section' viewBox='0 0 {F(Width)} {F(height)}'><title>Cross-section along the shot line</title>");}
        internal string Finish()=>s.Append("</svg>").ToString();
        public void ViewBox(float width,float height){}
        public void BeginClip(float width,float height){var id=prefix+"-clip"+(++clips);s.Append($"<defs><clipPath id='{id}'><rect width='{F(width)}' height='{F(height)}'/></clipPath></defs><g clip-path='url(#{id})'>");}
        public void EndClip()=>s.Append("</g>");
        public void Poly(float[] xy,string fill,string? stroke,float strokePx)
        {
            s.Append("<polygon points='");for(int i=0;i<xy.Length;i+=2)s.Append(F(xy[i])+","+F(xy[i+1])+" ");
            s.Append($"' fill='{fill}' stroke='{stroke??"none"}' stroke-width='{F(strokePx)}'/>");
        }
        public void Hatch(float[] xy,string colour,float angleDeg,float spacingPx,float widthPx)
        {
            // 0.22.41: the Kit raster draws stripes along (cos a, sin a) in screen coordinates; a vertical pattern line rotated by a - 90 matches it.
            var id=prefix+"-hatch"+(++hatches);
            s.Append($"<defs><pattern id='{id}' patternUnits='userSpaceOnUse' width='{F(spacingPx)}' height='{F(spacingPx)}' patternTransform='rotate({F(angleDeg-90)})'><line x1='0' y1='0' x2='0' y2='{F(spacingPx)}' stroke='{colour}' stroke-width='{F(widthPx)}'/></pattern></defs>");
            Poly(xy,$"url(#{id})",null,0);
        }
        public void Outline(float[] xy,string colour,float widthPx)=>Poly(xy,"none",colour,widthPx);
        public void Line(float x0,float y0,float x1,float y1,string colour,float widthPx)=>s.Append($"<line x1='{F(x0)}' y1='{F(y0)}' x2='{F(x1)}' y2='{F(y1)}' stroke='{colour}' stroke-width='{F(widthPx)}'/>");
        public void Arrow(float x0,float y0,float x1,float y1,string colour,float widthPx,float headPx){Line(x0,y0,x1,y1,colour,widthPx);Poly(new[]{x1,y1,x1-headPx,y1-headPx/2,x1-headPx,y1+headPx/2},colour,null,0);}
        public void Region(string id,float[] xy){}
        public void Text(float x,float y,string text,string colour,SectionAnchor anchor,string? background)
        {
            float w=text.Length*6.3f+8,l=anchor==SectionAnchor.MiddleCenter?x-w/2:anchor==SectionAnchor.MiddleRight?x-w:x-4;
            if(background!=null)s.Append($"<rect x='{F(l)}' y='{F(y-9)}' width='{F(w)}' height='18' rx='3' fill='{background}'/>");
            s.Append($"<text x='{F(x)}' y='{F(y)}' fill='{colour}' font-size='11' dominant-baseline='middle' text-anchor='{(anchor==SectionAnchor.MiddleCenter?"middle":anchor==SectionAnchor.MiddleRight?"end":"start")}'>{E(text)}</text>");
        }
    }
}
