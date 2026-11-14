using System.Diagnostics;
using System.Globalization;
using System.Runtime.ExceptionServices;
using SprocketExpandedBallistics.Materials;
using SprocketUiKit;
namespace SprocketExpandedBallistics;

/// <summary>
/// 0.22.6B item 1 and 2. The game froze when a preset was clicked in the composer (LogOutput.log ended on that click).
/// Cause: the kit's CPU rasteriser (Core/Raster.cs Polyline with dash &gt; 0) never returns for most real UI scales, and the
/// cross-section drew a dashed line as soon as a preset was loaded (KIT-BUG.md). These tests load every historical preset and
/// every legacy option through the pure model, draw the cross-section through the pure kit rasteriser with a time limit,
/// so a hang is a test failure, and check the window layout for screens up to 3440 x 1440.
/// </summary>
internal static class Fix0226BComposerTests
{
    /// <summary>Runs <paramref name="work"/> on a background thread; a hang past <paramref name="ms"/> is a failure, not a stuck test run.</summary>
    internal static void Limited(string what,int ms,Action work)
    {
        ExceptionDispatchInfo? error=null;
        var thread=new Thread(()=>{try{work();}catch(Exception ex){error=ExceptionDispatchInfo.Capture(ex);}}){IsBackground=true,Name="limited: "+what};
        thread.Start();
        if(!thread.Join(ms))throw new TimeoutException($"HANG: '{what}' did not finish within {ms} ms");
        error?.Throw();
    }

    private sealed record Fixture(CassetteComposerModel Model,IReadOnlyList<CassettePreset> Presets,IReadOnlyList<ComposerMaterial> Legacy,string Directory);
    private static Fixture Create()
    {
        var root=AppContext.BaseDirectory;
        var materials=CassetteTechnology.ReadDirectory(Path.Combine(root,"NativeData"));
        var reactions=MaterialReactionCatalogue.Parse(File.ReadAllText(Path.Combine(root,MaterialReactionCatalogue.FileName)));
        var presets=CassetteCatalogue.Parse(File.ReadAllText(Path.Combine(root,CassetteCatalogue.FileName)));
        var dir=Path.Combine(SprocketExpandedBallistics.Fix02228Tests.FixtureRoot(),"eb-composer-b-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(dir);
        var sidecar=Path.Combine(dir,CassetteCatalogue.FileName);File.WriteAllText(sidecar,CassetteComposer.Serialize(presets.Presets));
        var composer=new CassetteComposer(materials,reactions,sidecar,Path.Combine(dir,"Technology"),_=>false);
        var list=materials.Keys.Where(id=>!CompositePlate.IsCassette(id)&&!MaterialMenuVisibility.RetiredPresets.Contains(id))
            .Select(id=>new ComposerMaterial(id,id=="rha"?"Steel (RHA)":id,CassettePalette.Group(id))).ToArray();
        // Every legacy option of the game: each retired preset that exists, plus one extra so the list is never empty.
        var legacy=MaterialMenuVisibility.RetiredPresets.Where(materials.ContainsKey).Select(id=>new ComposerMaterial(id,"Legacy "+id,"Other")).ToList();
        foreach(var id in materials.Keys.Where(id=>!CompositePlate.IsCassette(id)&&!legacy.Any(l=>l.Id==id)).Take(Math.Max(0,3-legacy.Count)))legacy.Add(new ComposerMaterial(id,"Legacy "+id,CassettePalette.Group(id)));
        double now=0;
        var model=new CassetteComposerModel(composer,list,legacy,()=>CassetteCatalogue.Parse(File.ReadAllText(sidecar)).Presets,_=>false,_=>true,true,()=>now);
        return new(model,presets.Presets,legacy,dir);
    }

    /// <summary>Everything the panel polls per frame (getters, lists, texts), so a getter that never settles shows up here.</summary>
    private static void Poll(CassetteComposerModel m)
    {
        _=m.PresetRows;_=m.PresetCountText;_=m.LayerRows;_=m.LayersNote;_=m.MetaText;_=m.StateChip;_=m.Issues;_=m.IssueCountText;_=m.AngleText;_=m.DiagramVersion;
        _=m.TargetChip;_=m.ApplyNote;_=m.ApplyButton;_=m.CanSave;_=m.CanSaveAsNew;_=m.CanApply;_=m.CanExport;_=m.ApplyBlocker;_=m.RestartPending;_=m.TableNote;
        _=m.MaterialChoiceIds;_=m.MaterialChoiceLabels;_=m.MaterialChoice;_=m.LayerTitle;_=m.LayerDepthText;_=m.LayerThickness;_=m.LayerTilt;_=m.LayerDirection;_=m.LayerIsAir;_=m.LayerTiltError;
        _=m.LabelError;_=m.DateError;_=m.AnyTilted;_=m.HasErrors;_=m.Preview;_=m.Dirty;_=m.EvalStamp;_=m.Section;
        for(int i=0;i<CassetteComposerModel.DerivedLabels.Length;i++)_=m.Derived(i);
        for(int r=0;r<3;r++)for(int c=0;c<4;c++)_=m.Cell(r,c);
        for(int i=0;i<m.Layers.Count;i++)_=m.BandTooltip(i);
    }

    // ---- the pure kit rasteriser behind the same drawing interface the panel uses (mirrors kit Painter's maths) ----
    private sealed class RasterSink:ISectionSink
    {
        private readonly Raster r=new();private readonly float px;private Affine tf;
        private readonly Stack<(Affine Tf,(int,int,int,int) Clip)> stack=new();
        internal int Shapes,Texts,Regions;
        internal RasterSink(float width,float height,float pxPerUnit)
        {
            px=pxPerUnit;r.Resize(Math.Clamp((int)MathF.Ceiling(width*px),1,4096),Math.Clamp((int)MathF.Ceiling(height*px),1,4096));r.Clear(0);tf=Affine.Scale(px,px);
        }
        internal int Inked{get{int n=0;for(int i=0;i<r.Width*r.Height;i++)if((r.Pixels[i]>>24)!=0)n++;return n;}}
        private float W(float refPx)=>MathF.Max(.1f,refPx*px);
        private static uint U(string hex)
        {
            var h=hex.TrimStart('#');byte B(int i)=>byte.Parse(h.AsSpan(i,2),NumberStyles.HexNumber,CultureInfo.InvariantCulture);
            return Raster.Pack(B(0),B(2),B(4),h.Length>=8?B(6):(byte)255);
        }
        private float[] Tf(float[] xy){var o=new float[xy.Length];for(int i=0;i+1<xy.Length;i+=2){var (x,y)=tf.Apply(xy[i],xy[i+1]);o[i]=x;o[i+1]=y;}return o;}
        public void ViewBox(float width,float height)
        {
            float sw=r.Width/px,sh=r.Height/px,ww=MathF.Max(1e-6f,width),wh=MathF.Max(1e-6f,height);
            float sx=MathF.Min(sw/ww,sh/wh),ox=(sw-ww*sx)/2,oy=(sh-wh*sx)/2;
            tf=Affine.Scale(px,px).Times(Affine.Translate(ox,oy)).Times(Affine.Scale(sx,sx)).Times(Affine.Translate(0,0));
        }
        public void BeginClip(float width,float height)
        {
            stack.Push((tf,r.Clip));tf=tf.Times(Affine.Translate(0,0)).Times(Affine.Rotate(0)).Times(Affine.Scale(1,1));
            var (ax,ay)=tf.Apply(0,0);var (bx,by)=tf.Apply(width,height);var cur=r.Clip;
            float x0=MathF.Max(cur.X0,MathF.Min(ax,bx)),y0=MathF.Max(cur.Y0,MathF.Min(ay,by)),x1=MathF.Min(cur.X1,MathF.Max(ax,bx)),y1=MathF.Min(cur.Y1,MathF.Max(ay,by));
            r.SetClip(x0,y0,MathF.Max(x0,x1),MathF.Max(y0,y1));
        }
        public void EndClip(){if(stack.Count==0)return;var (t,c)=stack.Pop();tf=t;r.SetClip(c.Item1,c.Item2,c.Item3,c.Item4);}
        public void Poly(float[] xy,string fill,string? stroke,float strokePx)
        {Shapes++;r.FillPolygon(Tf(xy),U(fill));if(stroke!=null)r.Polyline(Tf(xy),W(strokePx),U(stroke),0,true);}
        public void Hatch(float[] xy,string colour,float angleDeg,float spacingPx,float widthPx){Shapes++;r.FillPolygon(Tf(xy),U(colour),angleDeg,spacingPx*px,W(widthPx));}
        public void Outline(float[] xy,string colour,float widthPx){Shapes++;r.Polyline(Tf(xy),W(widthPx),U(colour),0,true);}
        public void Line(float x0,float y0,float x1,float y1,string colour,float widthPx)
        {Shapes++;var (a,b)=tf.Apply(x0,y0);var (c,d)=tf.Apply(x1,y1);r.Segment(a,b,c,d,W(widthPx),U(colour));}
        public void Arrow(float x0,float y0,float x1,float y1,string colour,float widthPx,float headPx)
        {Shapes++;var (a,b)=tf.Apply(x0,y0);var (c,d)=tf.Apply(x1,y1);r.Arrow(a,b,c,d,W(widthPx),headPx*px,U(colour));}
        public void Region(string id,float[] xy){Regions++;}
        public void Text(float x,float y,string text,string colour,SectionAnchor anchor,string? background){Texts++;}
    }

    private static int Render(CassetteComposerModel m,float width,float height,float px)
    {
        var sink=new RasterSink(width,height,px);
        CassetteSectionDrawing.Draw(sink,m,width,height);
        return sink.Inked;
    }

    internal static void Run(Action<bool,string> check)
    {
        Guard.Sink=_=>{};                         // the kit's Guard sink is never assigned inside the pure core
        var fx=Create();
        try
        {
            var m=fx.Model;
            // The scale the log showed (1.333 at 1440p) gives px = ceil(w*scale)/w, the value that hangs the kit's dashed polyline.
            var badPx=814f/610f;
            var pxList=new[]{1f,badPx,4f/3f,1.5f,1.7778f,2f,.7111f,.9f,2.6667f};

            // ---- every historical preset and every legacy option loads and settles ----
            check(fx.Presets.Count>=9&&m.Catalogue.Count==fx.Presets.Count,"fix0226b: the composer lists the historical presets");
            var keys=fx.Presets.Select(p=>p.Id).Concat(fx.Legacy.Select(l=>"legacy:"+l.Id)).ToArray();
            check(fx.Legacy.Count>=3&&keys.Length>=12,"fix0226b: freeze test covers all presets and at least three legacy options");
            var total=Stopwatch.StartNew();
            foreach(var key in keys)
            {
                Limited("load "+key,10000,()=>{if(!m.LoadPreset(key))throw new InvalidOperationException("LoadPreset failed for "+key);Poll(m);});
                check(m.Layers.Count>0&&m.SelectedLayer==0,"fix0226b: loaded "+key+" with layers and a selection");
                // Settled: polling the getters never raises the version (a getter that bumps it would rebuild forever).
                int version=m.Version,diagram=m.DiagramVersion,stamp=m.EvalStamp;
                Limited("poll "+key,10000,()=>{for(int i=0;i<200;i++)Poll(m);});
                check(m.Version==version&&m.DiagramVersion==diagram&&m.EvalStamp==stamp,"fix0226b: polling "+key+" does not change Version, DiagramVersion or EvalStamp");
                check(m.Section.Bands.Length==m.Layers.Count&&m.Section.Bands.All(b=>CassetteSectionDrawing.AllFinite(b.Polygon)),"fix0226b: section of "+key+" is finite");
            }

            // ---- draw the cross-section of each, all angles/sides/selections, at the scale that hung the game ----
            int renders=0;
            foreach(var key in keys)
            {
                m.LoadPreset(key);
                for(int angle=0;angle<3;angle++)for(int side=0;side<2;side++)foreach(var layer in new[]{0,m.Layers.Count-1})
                {
                    m.AngleIndex=angle;m.WithTilt=side==0;m.SelectLayer(layer);int inked=0;
                    Limited($"render {key} angle {angle} side {side} layer {layer}",8000,()=>inked=Render(m,1300,430,badPx));renders++;
                    check(inked>1000,$"fix0226b: {key} cross-section draws pixels (angle {angle})");
                }
                m.AngleIndex=1;m.WithTilt=true;m.SelectLayer(0);
                foreach(var px in pxList)foreach(var (w,h) in new[]{(612f,250f),(1300f,430f),(2000f,700f)})
                {
                    Limited($"render {key} px {px} size {w}x{h}",8000,()=>Render(m,w,h,px));renders++;
                }
            }
            check(renders>400,"fix0226b: "+renders+" cross-section renders finished without a hang");

            // ---- the issue outlines (the other dashed call) on a preset that carries warnings or errors ----
            m.LoadPreset("burlington");m.SelectLayer(3);
            check(m.Issues.Count>=0,"fix0226b: burlington issues evaluated");
            Limited("render with issues",8000,()=>Render(m,1300,430,badPx));

            // ---- bad input never reaches the rasteriser as NaN/infinite or runs unbounded ----
            var nasty=new[]
            {
                new CassetteLayer("rha",double.NaN,0,"up"),new CassetteLayer("rha",double.PositiveInfinity,0,"up"),new CassetteLayer("rha",20,90,"up"),
                new CassetteLayer("rha",20,double.NaN,"up"),new CassetteLayer("rha",20,-30,"up"),new CassetteLayer("air",0,0,"up"),new CassetteLayer("rha",1e12,0,"up"),new CassetteLayer("nothere",5,45,"left")
            };
            m.LoadDraft(new CassettePreset("nasty","Nasty","1975.01.01",1,4000,nasty),ComposerSource.Import,"adversarial");
            foreach(var angle in new[]{0,1,2})
            {
                m.AngleIndex=angle;
                Limited("render adversarial layers",8000,()=>{Poll(m);Render(m,1300,430,badPx);Render(m,612,250,1f);});
            }
            var g=CassetteSection.Build(nasty,double.NaN,float.NaN,0);
            check(g.Bands.All(b=>CassetteSectionDrawing.AllFinite(b.Polygon))&&float.IsFinite(g.FrontX)&&float.IsFinite(g.BackX)&&double.IsFinite(g.Scale),"fix0226b: Build is finite for NaN, infinite and 90 degree input");
            var angleClamped=CassetteSection.Build(new[]{new CassetteLayer("rha",10,0,"up")},500);
            check(angleClamped.AngleDegrees<=80&&angleClamped.Bands.All(b=>CassetteSectionDrawing.AllFinite(b.Polygon)),"fix0226b: shot angle is clamped so cos never reaches 0");
            check(CassetteSection.Build(fx.Presets[0].Layers,0).Scale==CassetteSection.Build(fx.Presets[0].Layers,60).Scale,"fix0226b: default geometry still has one fixed scale");

            // ---- dashed helper: bounded, finite, no kit dash anywhere ----
            var calls=new List<(float,float,float,float)>();var probe=new CountingSink(calls);
            foreach(var (len,expect) in new[]{(0f,0),(.4f,0),(5f,1),(12f,1),(600f,50),(1900f,159),(1e9f,1)})
            {
                calls.Clear();int budget=CassetteSectionDrawing.MaxDashesTotal;
                Limited("dash length "+len,5000,()=>CassetteSectionDrawing.Dashed(probe,0,0,len,0,"#ffffff",1,ref budget));
                check(calls.Count==expect&&calls.Count<=CassetteSectionDrawing.MaxDashesPerLine,$"fix0226b: dashing {len} gives {calls.Count} segments (expected {expect})");
                check(calls.All(c=>float.IsFinite(c.Item1+c.Item2+c.Item3+c.Item4)&&(len>1e6||c.Item3-c.Item1<=CassetteSectionDrawing.DashPx+1e-3)),"fix0226b: dash segments are finite and at most one dash long");
            }
            foreach(var bad in new[]{float.NaN,float.PositiveInfinity,float.NegativeInfinity})
            {
                calls.Clear();int budget=CassetteSectionDrawing.MaxDashesTotal;CassetteSectionDrawing.Dashed(probe,0,0,bad,0,"#fff",1,ref budget);
                check(calls.Count==0,"fix0226b: a non-finite line is skipped");
            }
            { // the total budget caps a polygon with many edges
                calls.Clear();int budget=5;var poly=new float[]{-50,-50,2000,-50,2000,900,-50,900};
                Limited("dashed polygon",5000,()=>CassetteSectionDrawing.DashedPolygon(probe,poly,"#fff",1,1300,430,ref budget));
                check(calls.Count<=8,"fix0226b: the dash budget bounds a polygon outline to "+calls.Count+" lines");
            }
            double ax=-100,ay=50,bx=700,by=50;
            check(CassetteSectionDrawing.ClipToBox(ref ax,ref ay,ref bx,ref by,612,250)&&Math.Abs(ax)<1e-9&&Math.Abs(bx-612)<1e-9,"fix0226b: Liang-Barsky clips to the box");
            ax=-100;ay=-100;bx=-50;by=-60;
            check(!CassetteSectionDrawing.ClipToBox(ref ax,ref ay,ref bx,ref by,612,250),"fix0226b: a segment outside the box is dropped");
            check(total.Elapsed.TotalSeconds<120,"fix0226b: freeze checks finished in "+total.Elapsed.TotalSeconds.ToString("0.0",CultureInfo.InvariantCulture)+" s");

            // ---- window layout: nearly the whole screen, scaling with the screen, up to 3440 x 1440 ----
            foreach(var (sw,sh) in new[]{(1280,720),(1366,768),(1600,900),(1920,1080),(2560,1080),(2560,1440),(3440,1440),(3840,2160),(5120,1440),(1280,1024)})
            {
                var scale=Math.Clamp(sh/1080f,.5f,4f);var view=ComposerLayout.View(sw,sh,scale);var (pw,ph)=ComposerLayout.PanelSize(view.W,view.H);
                check(pw<=view.W&&ph<=view.H&&pw>=Math.Min(1100,view.W)&&ph>=Math.Min(640,view.H),$"fix0226b: {sw}x{sh} window {pw:0}x{ph:0} fits the {view.W:0}x{view.H:0} view");
                if(view.W>=1600&&view.H>=900&&view.W<=3600)check(pw/view.W>=.93&&ph/view.H>=.93,$"fix0226b: {sw}x{sh} window uses {pw/view.W:P0} x {ph/view.H:P0} of the screen");
            }
            var ultra=ComposerLayout.PanelSize(ComposerLayout.View(3440,1440,4f/3f).W,ComposerLayout.View(3440,1440,4f/3f).H);
            check(ultra.W>2400&&ultra.H>1000,"fix0226b: 3440x1440 gives a window of "+ultra.W.ToString("0",CultureInfo.InvariantCulture)+" x "+ultra.H.ToString("0",CultureInfo.InvariantCulture)+" kit units");
            check(ComposerLayout.PanelSize(float.NaN,1080)==(1600f,900f)&&ComposerLayout.PanelSize(0,0)==(1600f,900f)&&ComposerLayout.View(0,0,1)==(1920f,1080f),"fix0226b: layout falls back for unknown screen sizes");
        }
        finally{try{Directory.Delete(fx.Directory,true);}catch(IOException){}}
        Console.WriteLine("0.22.6B composer freeze and layout checks PASS");
    }

    private sealed class CountingSink:ISectionSink
    {
        private readonly List<(float,float,float,float)> lines;
        internal CountingSink(List<(float,float,float,float)> lines){this.lines=lines;}
        public void Line(float x0,float y0,float x1,float y1,string colour,float widthPx)=>lines.Add((x0,y0,x1,y1));
        public void ViewBox(float width,float height){}public void BeginClip(float width,float height){}public void EndClip(){}
        public void Poly(float[] xy,string fill,string? stroke,float strokePx){}public void Hatch(float[] xy,string colour,float angleDeg,float spacingPx,float widthPx){}
        public void Outline(float[] xy,string colour,float widthPx){}public void Arrow(float x0,float y0,float x1,float y1,string colour,float widthPx,float headPx){}
        public void Region(string id,float[] xy){}public void Text(float x,float y,string text,string colour,SectionAnchor anchor,string? background){}
    }
}
