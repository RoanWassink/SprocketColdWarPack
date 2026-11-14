using System.Globalization;
namespace SprocketExpandedBallistics;

internal enum SectionAnchor{MiddleCenter,MiddleLeft,MiddleRight}

/// <summary>
/// The few drawing operations the cross-section needs, as plain floats and hex strings. The panel maps them onto the
/// kit <c>Painter</c>; the tests map them onto the pure kit <c>Raster</c> with a time limit. There is deliberately no
/// dash parameter: the kit's dashed polyline loops forever for most real scales (0.22.6B, KIT-BUG.md), so the dashes
/// are cut into short solid lines here, with a hard cap on their number.
/// </summary>
internal interface ISectionSink
{
    /// <summary>Map the design box (0,0)-(width,height) onto the canvas, 1:1.</summary>
    void ViewBox(float width,float height);
    void BeginClip(float width,float height);
    void EndClip();
    void Poly(float[] xy,string fill,string? stroke,float strokePx);
    void Hatch(float[] xy,string colour,float angleDeg,float spacingPx,float widthPx);
    void Outline(float[] xy,string colour,float widthPx);
    void Line(float x0,float y0,float x1,float y1,string colour,float widthPx);
    void Arrow(float x0,float y0,float x1,float y1,string colour,float widthPx,float headPx);
    void Region(string id,float[] xy);
    void Text(float x,float y,string text,string colour,SectionAnchor anchor,string? background);
}

/// <summary>The to-scale cross-section of the composer, drawn into any <see cref="ISectionSink"/>.</summary>
internal static class CassetteSectionDrawing
{
    internal const float DashPx=6,GapPx=6;
    internal const int MaxDashesPerLine=160,MaxDashesTotal=2400;
    private static string N(double v,string format)=>v.ToString(format,CultureInfo.InvariantCulture);

    internal static bool AllFinite(float[] xy){foreach(var v in xy)if(!float.IsFinite(v))return false;return xy.Length>=6;}

    internal static void Draw(ISectionSink d,CassetteComposerModel m,float width,float height)
    {
        if(!(width>=8)||!(height>=8)||!float.IsFinite(width)||!float.IsFinite(height))return;
        height=CassetteSectionLabels.Height(m.Layers.Count,height);
        d.ViewBox(width,height);
        if(m.Layers.Count==0){d.Text(width/2,height/2,"No layers","#8f8670",SectionAnchor.MiddleCenter,null);return;}
        var diagramWidth=CassetteSectionLabels.DiagramWidth(width);
        var g=CassetteSection.Build(m.Layers,CassetteComposerModel.Angles[m.AngleIndex],diagramWidth,height);
        var issues=m.Issues;
        d.BeginClip(diagramWidth,height);
        foreach(var b in g.Bands)
        {
            if(!AllFinite(b.Polygon))continue;
            d.Poly(b.Polygon,b.Colour,CassettePalette.SeparatorHex,1);
            if(b.Air)d.Hatch(b.Polygon,CassettePalette.AirHatchHex,45,7,1.4f);
            else if(b.TiltDegrees>0)d.Hatch(b.Polygon,CassettePalette.TiltHex,(float)(90-g.AngleDegrees+(m.WithTilt?-1:1)*b.TiltDegrees),8,1);
            d.Region("L"+b.Layer.ToString(CultureInfo.InvariantCulture),b.Polygon);
        }
        foreach(var issue in issues)
            if(issue.Layer>=0&&issue.Layer<g.Bands.Length&&AllFinite(g.Bands[issue.Layer].Polygon))
                d.Outline(g.Bands[issue.Layer].Polygon,issue.Error?CassettePalette.ErrorHex:CassettePalette.AccentHex,1.5f);
        foreach(var chosen in m.SelectedLayers)if(chosen>=0&&chosen<g.Bands.Length&&AllFinite(g.Bands[chosen].Polygon))d.Outline(g.Bands[chosen].Polygon,CassettePalette.AccentHex,2);
        d.EndClip();
        foreach(var label in CassetteSectionLabels.Build(m.Layers,g,width,height,m.MaterialLabel))
        {
            d.Line(label.SourceX,label.SourceY,label.X-4,label.Y,CassettePalette.AirHatchHex,.7f);
            if(label.Numbered)
            {
                d.Text(label.X,label.Y,label.Number,CassettePalette.AccentHex,SectionAnchor.MiddleLeft,"#14150fcc");
                d.Text(label.X+28,label.Y,label.Text,CassettePalette.TextHex,SectionAnchor.MiddleLeft,null);
            }
            else d.Text(label.X,label.Y,label.Text,CassettePalette.TextHex,SectionAnchor.MiddleLeft,"#14150fcc");
        }
        var frontX=float.IsFinite(g.FrontX)?g.FrontX:0;var backX=float.IsFinite(g.BackX)?g.BackX:width;
        d.Arrow(8,g.ShotY,Math.Max(20,frontX-3),g.ShotY,CassettePalette.AccentHex,2,9);
        d.Line(frontX,g.ShotY,backX,g.ShotY,"#dc9e33aa",1.5f);
        d.Text(10,g.ShotY-14,$"{N(g.AngleDegrees,"0")}°",CassettePalette.AccentHex,SectionAnchor.MiddleLeft,null);
        d.Text(diagramWidth/2,height-12,$"path {N(g.PathMm,"0")} mm (D {N(g.DepthMm,"0.#")} / cos {N(g.AngleDegrees,"0")}°)",CassettePalette.TextHex,SectionAnchor.MiddleCenter,"#14150fcc");
        d.Text(8,height-32,"OUTSIDE","#8f8670",SectionAnchor.MiddleLeft,null);
        d.Text(diagramWidth-8,height-32,"INSIDE","#8f8670",SectionAnchor.MiddleRight,null);
        var bar=CassetteSection.ScaleBarMm(g.Scale);var px=(float)(bar*g.Scale);
        if(float.IsFinite(px)&&px>=1&&px<=diagramWidth-24)
        {
            d.Line(diagramWidth-12-px,12,diagramWidth-12,12,CassettePalette.TextHex,2);
            d.Text(diagramWidth-12-px/2,24,$"{N(bar,"0")} mm",CassettePalette.TextHex,SectionAnchor.MiddleCenter,"#14150fcc");
        }
    }

    /// <summary>
    /// Dashed line as short solid segments. Each dash starts at k*(dash+gap) (an integer multiple, no accumulated float
    /// drift), the number of segments is capped per line and per drawing; above the cap the line is drawn solid.
    /// </summary>
    internal static void Dashed(ISectionSink d,float x0,float y0,float x1,float y1,string colour,float widthPx,ref int budget)
    {
        if(!float.IsFinite(x0)||!float.IsFinite(y0)||!float.IsFinite(x1)||!float.IsFinite(y1))return;
        double dx=x1-x0,dy=y1-y0,len=Math.Sqrt(dx*dx+dy*dy);
        if(!(len>=.5))return;
        double period=DashPx+GapPx;var count=Math.Ceiling(len/period);
        if(!(count<=MaxDashesPerLine)||count>budget){d.Line(x0,y0,x1,y1,colour,widthPx);budget=Math.Max(0,budget-1);return;}
        int n=(int)count;
        for(int k=0;k<n;k++)
        {
            double s=k*period,e=Math.Min(len,s+DashPx);if(e<=s)break;
            d.Line((float)(x0+dx*s/len),(float)(y0+dy*s/len),(float)(x0+dx*e/len),(float)(y0+dy*e/len),colour,widthPx);
        }
        budget-=n;
    }
    /// <summary>The visible part of every polygon edge, dashed.</summary>
    internal static void DashedPolygon(ISectionSink d,float[] xy,string colour,float widthPx,float width,float height,ref int budget)
    {
        int n=xy.Length/2;
        for(int i=0;i<n&&budget>0;i++)
        {
            int j=(i+1)%n;double ax=xy[2*i],ay=xy[2*i+1],bx=xy[2*j],by=xy[2*j+1];
            if(!ClipToBox(ref ax,ref ay,ref bx,ref by,width,height))continue;
            Dashed(d,(float)ax,(float)ay,(float)bx,(float)by,colour,widthPx,ref budget);
        }
    }
    /// <summary>Liang-Barsky: clip a segment to the box (0,0)-(w,h); false when nothing of it is inside.</summary>
    internal static bool ClipToBox(ref double x0,ref double y0,ref double x1,ref double y1,double w,double h)
    {
        double dx=x1-x0,dy=y1-y0,t0=0,t1=1;
        bool Edge(double p,double q){if(p==0)return q>=0;var r=q/p;if(p<0){if(r>t1)return false;if(r>t0)t0=r;}else{if(r<t0)return false;if(r<t1)t1=r;}return true;}
        if(!Edge(-dx,x0)||!Edge(dx,w-x0)||!Edge(-dy,y0)||!Edge(dy,h-y0))return false;
        double nx0=x0+t0*dx,ny0=y0+t0*dy,nx1=x0+t1*dx,ny1=y0+t1*dy;
        x0=nx0;y0=ny0;x1=nx1;y1=ny1;return true;
    }
}

/// <summary>Size of the composer window: almost the whole screen, in kit units (screen px / UI scale).</summary>
internal static class ComposerLayout
{
    internal const float Margin=24,MinWidth=1100,MinHeight=640,MaxWidth=3600,MaxHeight=1700;
    /// <summary>Re-fit only on a real difference (kit units), so the open panel never flickers or rebuilds (0.22.15).</summary>
    internal const float ResizeTolerance=4;
    /// <summary>Visible area in kit units for a screen and UI scale (screen height / 1080 x factor).</summary>
    internal static (float W,float H) View(int screenWidth,int screenHeight,float scale)
    {
        if(screenWidth<=0||screenHeight<=0)return (1920,1080);
        // 0.22.15: an invalid scale falls back to the kit's own default (screen height / 1080), which always fits.
        if(!(scale>0)||!float.IsFinite(scale))scale=KitScale(screenHeight,null);
        return (screenWidth/scale,screenHeight/scale);
    }
    /// <summary>The kit formula: screen height / 1080 x Display.Scale (factor 0.5-2, result 0.5-4); factor unknown or invalid: 1.</summary>
    internal static float KitScale(int screenHeight,float? factor)
    {
        if(screenHeight<=0)return 1;
        var f=factor is float v&&float.IsFinite(v)&&v>0?Math.Clamp(v,.5f,2f):1f;
        return Math.Clamp(screenHeight/1080f*f,.5f,4f);
    }
    /// <summary>
    /// The scale the composer is sized for (0.22.15). <c>UiKit.Scale</c> reads 1 until the kit canvas exists, so a first
    /// panel of the session was sized for scale 1 and came out 4/3 too large at 1440p. The value 1 is therefore not
    /// trusted; any other valid kit value counts, and the larger of it and the estimate keeps the panel on screen.
    /// </summary>
    internal static float EffectiveScale(float kitScale,int screenHeight,float? factor)
    {
        var estimate=KitScale(screenHeight,factor);
        return kitScale>0&&float.IsFinite(kitScale)&&Math.Abs(kitScale-1)>.0001f?Math.Max(kitScale,estimate):estimate;
    }
    /// <summary>Composer size in kit units for the current screen, kit scale and kit factor.</summary>
    internal static (float W,float H) Size(int screenWidth,int screenHeight,float kitScale,float? factor)
    {
        var view=View(screenWidth,screenHeight,EffectiveScale(kitScale,screenHeight,factor));
        return PanelSize(view.W,view.H);
    }
    /// <summary>
    /// The size to shrink to when the current panel is larger than the screen size by more than the tolerance; null when it fits
    /// (smaller, equal or within tolerance), so a panel the user made smaller is never enlarged and one that fits never flickers.
    /// </summary>
    internal static (float W,float H)? ShrinkTo(float currentW,float currentH,float screenW,float screenH)
    {
        if(!(screenW>0)||!(screenH>0))return null;
        bool tooWide=currentW>screenW+ResizeTolerance,tooTall=currentH>screenH+ResizeTolerance;
        if(!tooWide&&!tooTall)return null;
        return (tooWide?screenW:currentW,tooTall?screenH:currentH);
    }
    internal static bool NeedsResize(float w,float h,float currentW,float currentH)=>Math.Abs(w-currentW)>ResizeTolerance||Math.Abs(h-currentH)>ResizeTolerance;
    /// <summary>Panel size for a visible area: a small margin on every side, never wider or taller than the view.</summary>
    internal static (float W,float H) PanelSize(float viewW,float viewH)
    {
        if(!(viewW>0)||!(viewH>0)||!float.IsFinite(viewW)||!float.IsFinite(viewH))return (1600,900);
        var w=Math.Clamp(viewW-2*Margin,Math.Min(MinWidth,viewW),MaxWidth);
        var h=Math.Clamp(viewH-2*Margin,Math.Min(MinHeight,viewH),MaxHeight);
        return (Math.Min(w,viewW),Math.Min(h,viewH));
    }
}
