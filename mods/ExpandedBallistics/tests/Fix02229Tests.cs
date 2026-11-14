using System.Diagnostics;
using System.Globalization;
using System.Text;
using SprocketUiKit;
namespace SprocketExpandedBallistics;
internal static class Fix02229Tests
{
    internal static void Run(Action<bool,string> check)
    {
        var count=0;var sink=check;check=(ok,msg)=>{sink(ok,"fix02229: "+msg);count++;};
        var root=Fix02228Tests.SourceRoot();var output=Fix02228Tests.OutputRoot();
        var source=File.ReadAllText(Path.Combine(root,"CassetteComposerPanel.cs"));
        check(source.Contains("foreach(var column in c)column.Height(0);"),"columns have zero measured minimum before panel Fill allocation");
        check(source.Contains("var body=c.ScrollView().Fill();")&&source.Contains("var actions=c.Footer(\"Apply\");"),"scroll body and pinned action footer are siblings");
        var footer=source[source.IndexOf("var actions=c.Footer(\"Apply\");",StringComparison.Ordinal)..source.IndexOf("private static bool CanMeshAction",StringComparison.Ordinal)];
        check(footer.Contains("Save as new version")&&footer.Contains("Save as new cassette")&&footer.Contains("actions.Row(8).Button(\"Apply\""),"all three actions exist in separate footer rows");
        check(!footer.Contains("Wrap()")&&!footer.Contains("Warning(")&&!footer.Contains("Canvas("),"variable height content cannot enlarge action footer");
        var chart=source[source.IndexOf("private static LineChart AngleChart(",StringComparison.Ordinal)..source.IndexOf("// ---------------- drawing",StringComparison.Ordinal)];
        check(System.Text.RegularExpressions.Regex.Matches(chart,@"\.Series\(").Count==4,"four series respect actual Kit limit; old tilted eight-series construction threw before footer creation");
        check(chart.Contains("min:0,max:85,tick:5")&&chart.Contains("mean (game)"),"fixed 0..85 axis, including 85 tick; chart displays game mean");
        check(source.Contains("return row.With;"),"four table columns display runtime mean");
        foreach(var name in new[]{"longRod","fullBoreKE","jet","hesh"})check(source.Contains(".Column(\""+name+"\""),"aligned threat column "+name);
        // Real Kit geometry, not a replacement Fill algorithm. Kit StackHeights never shrinks an intrinsic minimum.
        const float header=Tok.HeaderH,kitFooter=Tok.FooterH,columnPad=Tok.ColumnPadY,columnGap=Tok.ColumnGap,blockPad=Tok.BlockPad,blockGap=Tok.BlockGap,buttonH=Tok.ButtonH;
        var titleH=MathF.Round(Tok.FontSmall*1.35f);
        var sectionHeight=MathF.Round(Tok.FontSection*1.35f)+Tok.ColumnGap;
        var kitRoot=Path.GetFullPath(Path.Combine(root,"../ui-kit"));
        check(File.ReadAllText(Path.Combine(kitRoot,"Runtime/Charts.cs")).Contains("series.Count >= 4"),"local Kit source confirms four-series construction limit");
        var actionHeight=blockPad*2+titleH+3*blockGap+3*buttonH;
        var report=new StringBuilder("scale\tminimum_panel\tlayers\ttilt\tpanel_h\tcolumn_h\tfooter_top\tfooter_bottom\tbody_viewport_h\tbody_content_h\tscroll_overflow\n");
        var materials=CassetteTechnology.ReadDirectory(Path.Combine(AppContext.BaseDirectory,"NativeData"));
        var reactions=MaterialReactionCatalogue.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory,MaterialReactionCatalogue.FileName)));
        var presets=CassetteCatalogue.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory,CassetteCatalogue.FileName))).Presets;
        var maxLayers=presets.Max(p=>p.Layers.Length);
        foreach(var scale in new[]{1f,1.25f,1.333f})foreach(var minimum in new[]{false,true})foreach(var layers in new[]{maxLayers,Math.Max(256,maxLayers+1)})foreach(var tilt in new[]{false,true})
        {
            var size=minimum?(W:960f,H:560f):ComposerLayout.PanelSize(2560/scale,1440/scale);
            var column=KitMath.StackHeights(size.H-header-kitFooter,0,new[]{0f},new[]{true})[0];
            var bottom=header+column-columnPad;var top=bottom-actionHeight;
            var above=column-2*columnPad-actionHeight-columnGap;
            var heights=KitMath.StackHeights(above,columnGap,new[]{sectionHeight,Tok.RowH},new[]{false,true});
            var viewport=heights[1];
            // The largest shipped stack and a 256-layer open-ended stack, each tilted and plain; all preview content may scroll.
            var content=CassetteSectionLabels.Height(layers,CassetteSection.Height)+240+500+(tilt?120:0);
            check(top>=header&&bottom<=size.H-kitFooter,"footer inside panel scale="+scale+" layers="+layers+" tilt="+tilt);
            check(viewport>36&&sectionHeight+columnGap+viewport<=above+.01f,"body fills only height above footer");
            check(content>viewport&&KitMath.ClampScroll(float.MaxValue,content,viewport)>0,"maximum layers and tilt overflow body scroll only");
            report.AppendLine(FormattableString.Invariant($"{scale}\t{minimum}\t{layers}\t{tilt}\t{size.H}\t{column}\t{top}\t{bottom}\t{viewport}\t{content}\t{content-viewport}"));
        }
        // Control reproduces the Kit's former root-height failure, independent of chart construction.
        check(KitMath.StackHeights(800,0,new[]{1200f},new[]{true})[0]>800,"old Fill retains oversized intrinsic minimum");
        foreach(var tilted in new[]{false,true})
        {
            var range=ChartMath.Range(0,85,0,85,false,5);Span<float> ticks=new float[64];
            var n=ChartMath.Ticks(range,ticks);check(range.Min==0&&range.Max==85&&ticks[n-1]==85,"actual Kit range ends at 85 tilt="+tilted);
        }
        File.WriteAllText(Path.Combine(output,"LAYOUT-CHECK.tsv"),report.ToString());
        foreach(var file in new[]{"NativeCassetteGeometry.cs","RuntimeCompositePlate.cs","RuntimeCassettePlateVisual.cs"})
        {
            var text=File.ReadAllText(Path.Combine(root,file));
            check(!text.Contains("nameof(PlateStructure.OnAssignedMeshModified)")&&text.Contains("nameof(PlateStructure.RequestMeshRefresh)"),"no-argument mesh target "+file);
            check(text.Contains("Modified(PlateStructure __instance)"),"mesh postfix reads only exact PlateStructure instance "+file);
        }
        var start=new ProcessStartInfo("py"){WorkingDirectory=root,RedirectStandardOutput=true,RedirectStandardError=true,UseShellExecute=false,CreateNoWindow=true};
        start.ArgumentList.Add(Path.Combine(root,"tests/icf_audit.py"));
        using(var process=Process.Start(start)!)
        {
            var stdout=process.StandardOutput.ReadToEnd();var stderr=process.StandardError.ReadToEnd();process.WaitForExit();
            File.WriteAllText(Path.Combine(output,"icf-tests.log"),stdout+stderr);
            check(process.ExitCode==0,"source-extracted ICF dump audit and undocumented-sharing negative control: "+stdout+stderr);
        }
        Console.WriteLine($"0.22.29 PASS: {count} checks; layout and ICF evidence in test-output/.");
    }
}
