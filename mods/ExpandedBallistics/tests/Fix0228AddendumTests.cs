using System.Globalization;
using System.Text;
namespace SprocketExpandedBallistics;

internal static class Fix0228AddendumTests
{
    internal static void Run(Action<bool,string> check)
    {
        Reloads(check);Designer(check);Labels(check);Era(check);
        Console.WriteLine("0.22.8 ADDENDUM loader, scene state, 1-30 layer/1-200 mm label layout and ERA attribution PASS");
    }
    private static void Reloads(Action<bool,string> check)
    {
        var chamber=new ChamberLedger<int>();chamber.Initial(1,"hesh");
        long fired=chamber.Token(1);int stock=5,reloads=0;
        // Native FireInternal starts another load inside its body, before its postfix.
        stock--;reloads++;chamber.Debit(1,10,"heat");long next=chamber.Token(1);
        chamber.Spend(1,fired);
        check(chamber.Has(1)&&chamber.Token(1)==next,"addendum: FireInternal postfix preserves reentrant next-load credit");
        for(int frame=0;frame<1000;frame++)
        {
            bool loading=frame<900;
            if(AmmoReloadPolicy.CanSteer(loading,true))reloads++;
            check(!AmmoReloadPolicy.RemovePhantom(!loading,loading,chamber.Has(1),true,false,true,true),"addendum: normal carousel load is never removed");
        }
        check(reloads==1&&stock==4&&chamber.Check(1,20,8,out _,out _)==FireVerdict.Allowed,"addendum: one carousel reload and one debit, no second reload");
        chamber.Spend(1,next);chamber.Spend(1,next);
        check(!chamber.Has(1),"addendum: fired generation is still spent once");
        check(AmmoReloadPolicy.RecoverDebit(5,4,true,true,false),"addendum: exact owned typed rack count proves missed native debit");
        check(AmmoReloadPolicy.RecoverCycleDebit(10,9,5,4,true,false),"addendum: debit before UpdateRequest is recovered from exact source plus pre-shot stock");
        check(!AmmoReloadPolicy.RecoverCycleDebit(10,10,5,4,true,false)&&!AmmoReloadPolicy.RecoverCycleDebit(10,8,5,4,true,false),"addendum: transfers and ambiguous multi-round cycle losses cannot create chamber credit");
        check(!AmmoReloadPolicy.RecoverDebit(5,5,true,true,false)&&!AmmoReloadPolicy.RecoverDebit(5,3,true,true,false)&&
            !AmmoReloadPolicy.RecoverDebit(5,4,false,true,false)&&!AmmoReloadPolicy.RecoverDebit(5,4,true,false,false)&&
            !AmmoReloadPolicy.RecoverDebit(5,4,true,true,true),"addendum: unchanged, multi-round, other-source and synthetic waiting changes cannot mint credit");
        check(AmmoReloadPolicy.RemovePhantom(true,false,false,true,true,true,false),"addendum: proven phantom with no debit or count fall is removed");
        foreach(bool? external in new bool?[]{true,null})
            check(!AmmoReloadPolicy.RemovePhantom(true,false,false,true,true,true,external),"addendum: external/unknown loader never loses a legitimate round to phantom cleanup");
        check(!AmmoReloadPolicy.RemovePhantom(true,false,false,true,false,true,false)&&
            !AmmoReloadPolicy.RemovePhantom(true,false,false,true,true,false,false)&&
            !AmmoReloadPolicy.RemovePhantom(true,true,false,true,true,true,false),"addendum: doubt, count fall or running reload prevents removal");
        var entries=new[]{new AmmoLoadoutEntry("hesh",1),new AmmoLoadoutEntry("heat",2)};
        int requests=0;bool loadingNow=false,loaded=false;string? selected="hesh";int heat=2;
        for(int frame=0;frame<100;frame++)
        {
            if(AmmoReloadPolicy.CanSteer(loadingNow,loaded))
            {
                var choice=AmmoSelection.Choose(entries,selected,id=>id=="heat"?heat:0);
                selected=choice.Type;if(selected!=null){heat--;requests++;loadingNow=true;}
            }
            if(frame==80){loadingNow=false;loaded=true;}
        }
        check(requests==1&&selected=="heat"&&heat==1,"addendum: empty HESH selects exactly one normal HEAT reload");
        check(!AmmoReloadPolicy.CanSteer(true,false),"addendum: missing credit cannot restart or retag a running native reload");
        check(AmmoReloadPolicy.CanSteer(false,false),"addendum: synthetic crew-wait with no occupied round can choose the next type");
        var refund=new RefundLatch();int returned=0;
        if(refund.TryTake())returned++;if(refund.TryTake())returned++;
        check(returned==1,"addendum: nested native refund remains once-only");
    }
    private static void Designer(Action<bool,string> check)
    {
        var state=new CassetteDesignerState();
        for(int cycle=0;cycle<5;cycle++)
        {
            check(state.Observe(true,11)==DesignerTransition.Entered,"addendum: Designer enters again");
            state.Toggle(false);check(state.Visible(false,false),"addendum: toggle works after Edit-Play-Edit");
            state.PlayerState(false);
            check(state.Observe(false,11)==DesignerTransition.Left&&!state.Active&&state.Manual==null,"addendum: Play clears overlay/toggle state");
            // No PlayerState(true) callback: fresh surface activation must recover by itself.
        }
        state.Observe(true,11);state.Toggle(false);
        check(state.Observe(true,12)==DesignerTransition.Restarted&&state.Manual==null,"addendum: reloaded scene rebuilds even without a sampled unloaded frame");
        state.PlayerState(false);
        check(state.Observe(true,12)==DesignerTransition.Left,"addendum: state notification leaves a still-loaded scene");
        state.PlayerState(true);
        check(state.Observe(true,12)==DesignerTransition.Entered,"addendum: explicit Designer state can reactivate same surface");
        state.Toggle(false);state.ClearManual();
        check(state.Visible(false,true)&&!state.Visible(true,true),"addendum: auto mode and disabled mode retain their meaning");
    }
    private static void Labels(Action<bool,string> check)
    {
        for(int count=1;count<=30;count++)for(int thickness=1;thickness<=200;thickness++)foreach(int angle in new[]{0,30,60})
        {
            var layers=Enumerable.Range(0,count).Select(i=>new CassetteLayer(i%2==0?"cwepGlassTextolite":"highHardnessSteel",i%2==0?thickness:201-thickness,0,"up")).ToArray();
            const float width=612;float height=CassetteSectionLabels.Height(count,250);
            var geometry=CassetteSection.Build(layers,angle,CassetteSectionLabels.DiagramWidth(width),height);
            var labels=CassetteSectionLabels.Build(layers,geometry,width,height,_=>"Very long full material name - retained in the layer list");
            bool good=labels.Length==count;
            for(int i=0;i<labels.Length;i++)
            {
                var l=labels[i];
                good&=float.IsFinite(l.SourceX+l.SourceY+l.X+l.Y)&&l.Y>=CassetteSectionLabels.Padding-.001f&&l.Y<=height-CassetteSectionLabels.Padding+.001f;
                good&=l.X>=CassetteSectionLabels.DiagramWidth(width)&&l.Numbered==(count>12);
                good&=l.X+(l.Numbered?28:0)+l.Text.Length*8<=width-8;
                if(i>0)good&=l.Y-labels[i-1].Y>=CassetteSectionLabels.RowSpacing-.001f;
            }
            check(good,$"addendum: no overlapping labels, {count} layers / {thickness} mm / {angle} degrees");
        }
        check(CassetteSectionLabels.ShortName("cwepGlassTextolite","TEXTOLITE - GLASS LAMINATE")=="Textolite","addendum: textolite label is short; full layer-list name is unaffected");
        var output=Environment.GetEnvironmentVariable("EB0228_EVIDENCE");if(string.IsNullOrWhiteSpace(output))return;
        foreach(int count in new[]{3,30})
        {
            var materials=new[]{new ComposerMaterial("highHardnessSteel","High hardness steel","Steel"),new ComposerMaterial("cwepGlassTextolite","TEXTOLITE - GLASS LAMINATE","Composite and filler")};
            var model=new CassetteComposerModel(null,materials,Array.Empty<ComposerMaterial>(),()=>Array.Empty<CassettePreset>(),_=>false,_=>false,false);
            var layers=Enumerable.Range(0,count).Select(i=>new CassetteLayer(i%2==0?"highHardnessSteel":"cwepGlassTextolite",i==0?50:10,0,"up")).ToArray();
            model.LoadDraft(new("labels","Labels","1980.01.01",1,4000,layers),ComposerSource.Import,"fixture");
            var sink=new SvgSink();CassetteSectionDrawing.Draw(sink,model,820,735);
            File.WriteAllText(Path.Combine(output,$"composer-labels-{count}.svg"),sink.Result);
        }
    }
    private static void Era(Action<bool,string> check)
    {
        check(Math.Abs(CombatReportMeasurement.ActiveEraThickness(ModularCellSpec.DupletHull,1,1)!.Value-12)<1e-8,"addendum: Duplet active stage uses 12 mm, not panel-envelope depth");
        check(CombatReportMeasurement.ActiveEraThickness(ModularCellSpec.DupletHull,0,1)==null&&CombatReportMeasurement.ActiveEraThickness(ModularCellSpec.DupletHull,1,double.NaN)==null,"addendum: housing/invalid scale cannot fabricate active thickness");
        var composite=new CombatLayerPart(281,"Composite armour plate","ebCassette_test","Textolite",90,60,true);
        var era=new CombatLayerPart(290,"dupletPanelHullStage1","dupletHullActive","ERA - Duplet hull",12,60,true);
        CombatReportEvent Start(string stage,string id,CombatLayerPart part,double before)=>new("layerStart",stage,"Reached",before,HitId:id,Layer:new(id,new[]{part},false));
        var shot=new CombatReportShot(5,"Outgoing","Tandem HEAT",800,new[]{
            Start("Main","X",composite,775),new CombatReportEvent("plateResidual","Main","Passed",775,650,"X"),
            new CombatReportEvent("heavyEra","Main","Duplet activated",HitId:"E",RelatedEntity:290),
            Start("Precursor","E",composite,600),new CombatReportEvent("plateResidual","Precursor","Passed",600,500,"E"),
            Start("Main","E",era,650),new CombatReportEvent("plateResidual","Main","Passed",650,135,"E")});
        var hits=CombatHitProjection.Build(shot);
        check(hits.Single(h=>h.Stage=="Main"&&h.Id=="E").Reaction=="ERA activated","addendum: early ERA event waits for its own later layer");
        check(hits.Single(h=>h.Stage=="Precursor"&&h.Id=="E").Reaction==""&&hits[0].Reaction=="","addendum: same HitId in another stage cannot borrow ERA reaction");
        check(hits.Where(h=>h.Stage=="Main").Select(h=>h.Id).SequenceEqual(new[]{"X","E"}),"addendum: real reached order is preserved, not sorted by reaction timing or part name");
        var unknown=shot with{Events=shot.Events.Append(new CombatReportEvent("heavyEra","Main","Unknown ERA",HitId:"not-captured")).ToArray()};
        check(CombatHitProjection.Build(unknown).Any(h=>h.Status=="Diagnostic only"&&h.Details.Any(e=>e.HitId=="not-captured")),"addendum: unbound ERA stays diagnostic instead of moving onto another plate");
        check(CombatReportView.Build(new[]{shot})[0].Layers.Single(l=>l.Name=="dupletPanelHullSt\u2026"||l.Name.StartsWith("dupletPanelHull",StringComparison.Ordinal)).Thickness=="12 mm","addendum: active ERA thickness reaches panel and HTML table");
    }
    private sealed class SvgSink:ISectionSink
    {
        private readonly StringBuilder b=new();private float w,h;
        private static string N(float n)=>n.ToString("0.###",CultureInfo.InvariantCulture);
        private static string E(string s)=>System.Security.SecurityElement.Escape(s)??"";
        internal string Result=>$"<svg xmlns='http://www.w3.org/2000/svg' width='{N(w)}' height='{N(h)}' viewBox='0 0 {N(w)} {N(h)}'><rect width='100%' height='100%' fill='#1b1c15'/>{b}</svg>";
        public void ViewBox(float width,float height){w=width;h=height;}
        public void BeginClip(float width,float height)=>b.Append($"<defs><clipPath id='section'><rect width='{N(width)}' height='{N(height)}'/></clipPath></defs><g clip-path='url(#section)'>");
        public void EndClip()=>b.Append("</g>");
        public void Poly(float[] xy,string fill,string? stroke,float strokePx)=>b.Append($"<polygon points='{string.Join(" ",xy.Select(N))}' fill='{fill}' stroke='{stroke??"none"}' stroke-width='{N(strokePx)}'/>");
        public void Hatch(float[] xy,string colour,float angleDeg,float spacingPx,float widthPx){}
        public void Outline(float[] xy,string colour,float widthPx)=>Poly(xy,"none",colour,widthPx);
        public void Line(float x0,float y0,float x1,float y1,string colour,float widthPx)=>b.Append($"<line x1='{N(x0)}' y1='{N(y0)}' x2='{N(x1)}' y2='{N(y1)}' stroke='{colour}' stroke-width='{N(widthPx)}'/>");
        public void Arrow(float x0,float y0,float x1,float y1,string colour,float widthPx,float headPx)=>Line(x0,y0,x1,y1,colour,widthPx);
        public void Region(string id,float[] xy){}
        public void Text(float x,float y,string text,string colour,SectionAnchor anchor,string? background)=>b.Append($"<text x='{N(x)}' y='{N(y)}' fill='{colour}' font-family='sans-serif' font-size='14' dominant-baseline='middle' text-anchor='{(anchor==SectionAnchor.MiddleLeft?"start":anchor==SectionAnchor.MiddleRight?"end":"middle")}'>{E(text)}</text>");
    }
}
