using System.Globalization;
using System.Text;
using System.Text.Json;
namespace SprocketExpandedBallistics;

/// <summary>
/// 0.22.69: a plate in layers is paid by one factor per solid millimetre (the uniform hit model), its report is one line, and Forensic shows its layers and the place where a shot stopped.
/// Tests: (1) equal to the plate as one block for every recipe of the catalogue; (2) the same shot line gives the same result whatever the faces are recognised as; (3) proportional to the solid material;
/// (4) the four shots of the user's report replayed; (5) one line in the combat report and one post in Forensic; (6) the arrest mark lies in the layer where the solid material ran out; (7) ordinary plates unchanged.
/// </summary>
internal static partial class ComposerFillBlueprintEvidence
{
    private sealed class RecordSink:ISectionSink
    {
        internal readonly List<object> Ops=new();
        public void ViewBox(float width,float height)=>Ops.Add(new{op="view",width,height});
        public void BeginClip(float width,float height)=>Ops.Add(new{op="clip",width,height});
        public void EndClip()=>Ops.Add(new{op="endclip"});
        public void Poly(float[] xy,string fill,string? stroke,float strokePx)=>Ops.Add(new{op="poly",xy,fill,stroke,strokePx});
        public void Hatch(float[] xy,string colour,float angleDeg,float spacingPx,float widthPx)=>Ops.Add(new{op="hatch",xy,colour,angleDeg,spacingPx,widthPx});
        public void Outline(float[] xy,string colour,float widthPx)=>Ops.Add(new{op="outline",xy,colour,widthPx});
        public void Line(float x0,float y0,float x1,float y1,string colour,float widthPx)=>Ops.Add(new{op="line",x0,y0,x1,y1,colour,widthPx});
        public void Arrow(float x0,float y0,float x1,float y1,string colour,float widthPx,float headPx)=>Ops.Add(new{op="arrow",x0,y0,x1,y1,colour,widthPx,headPx});
        public void Region(string id,float[] xy){}
        public void Text(float x,float y,string text,string colour,SectionAnchor anchor,string? background)=>Ops.Add(new{op="text",x,y,text,colour,anchor=anchor.ToString(),background});
    }

    /// <summary>The marker the game writes when it first reaches a plate in layers: the plate, the layers of its recipe, the solid material and the extent of the plate on the whole line.</summary>
    private static string MarkerText(string label,int groups,IReadOnlyList<PaidMaterialPiece> all)
    {
        var ordered=all.Where(p=>p.EndM>p.StartM).OrderBy(p=>p.StartM).ToArray();double solid=0,reach=double.NegativeInfinity;
        foreach(var p in ordered){var from=Math.Max(p.StartM,reach);if(p.EndM>from)solid+=(p.EndM-from)*1000;reach=Math.Max(reach,p.EndM);}
        double extent=ordered.Length>0?(ordered.Max(p=>p.EndM)-ordered[0].StartM)*1000:0;
        return string.Create(CultureInfo.InvariantCulture,$"{label}: plate in layers, {groups} layers; {solid:0.###} mm solid in {extent:0.###} mm.");
    }

    /// <summary>Plays a recorded shot again on the hit of 0.22.69: the blocks of the plate (entity <paramref name="plate"/>) cost what the uniform model says, every other block keeps the loss it had, the budget starts at <paramref name="initial"/>; the shot stops where the budget runs out. The marker of the plate in layers is added as the game records it.</summary>
    private static CombatReportShot ReplayUniform(CombatReportShot shot,int plate,double initial,Func<double,double> factorAt,string label,int groups,out double lossInPlate)
    {
        var events=new List<CombatReportEvent>();double budget=initial;bool stopped=false,marked=false;lossInPlate=0;
        var allPieces=shot.Events.Where(e=>e.PaidIntervals!=null).SelectMany(e=>e.PaidIntervals!).Where(p=>p.Entity==plate).ToArray();
        var ids=shot.Events.Where(e=>e.Kind=="layerStart"&&e.HitId!=null).Select(e=>e.HitId!).Distinct().ToList();
        foreach(var id in ids)
        {
            if(stopped)break;
            var own=shot.Events.Where(e=>e.HitId==id).ToArray();var start=own.First(e=>e.Kind=="layerStart");
            bool solidPlate=start.Layer!.AirGapMm==null&&start.Layer.Parts.Count(p=>p.Armour)==1&&start.Layer.Parts.Any(p=>p.Armour&&p.Entity==plate);
            var pieces=own.Where(e=>e.PaidIntervals!=null).SelectMany(e=>e.PaidIntervals!).Where(p=>p.Entity==plate).Select(p=>p with{Factor=factorAt(p.Angle),Label=label}).ToArray();
            double loss;
            if(solidPlate&&pieces.Length>0)loss=pieces.Sum(p=>p.RhaMm);
            else{var after=own.Where(e=>e.AfterMm!=null).Select(e=>e.AfterMm!.Value).DefaultIfEmpty(start.BeforeMm??0).Last();loss=Math.Max(0,(start.BeforeMm??0)-after);}
            if(solidPlate)lossInPlate+=loss;
            bool stops=budget-loss<=1e-9;double exit=stops?0:budget-loss;
            foreach(var e in own)
            {
                if(e.Kind=="materialReaction"&&e.PaidIntervals==null&&e.Text.Contains("found no layer face",StringComparison.Ordinal))continue;
                if(e.Kind=="layerStart"){events.Add(e with{BeforeMm=budget});if(solidPlate&&!marked){marked=true;events.Add(new CombatReportEvent("layeredPlate",e.Stage,MarkerText(label,groups,allPieces),RelatedEntity:plate));}continue;}
                if(e.Kind=="materialReaction"&&e.PaidIntervals!=null&&solidPlate&&pieces.Length>0)
                {events.Add(new CombatReportEvent("materialReaction",e.Stage,"Confirmed material intervals: "+string.Join("; ",pieces.Select(p=>p.Text)),budget,exit,id){PaidIntervals=pieces});continue;}
                if(e.Kind is "plateResidual" or "componentResidual"){if(!stops)events.Add(e with{BeforeMm=budget,AfterMm=exit});continue;}
                if(e.Kind is "materialReaction" or "materialNote" or "blockMembers" or "materialPending"){if(e.Kind=="materialReaction"&&e.PaidIntervals==null)continue;events.Add(e);continue;}
                events.Add(e);
            }
            if(stops){stopped=true;events.Add(new CombatReportEvent("nativeStop",start.Stage,"Native stop in "+label,budget,0,id));}
            budget=exit;
        }
        return shot with{Events=events,InitialPenetrationMm=initial};
    }

    internal static void RunUniform(Action<bool,string> sink)
    {
        int checks=0,failures=0;void Check(bool ok,string message){checks++;if(!ok)failures++;sink(ok,"Uniform: "+message);}
        string root=Fix02228Tests.SourceRoot(),output=Fix02228Tests.OutputRoot(),preview=Path.Combine(output,"preview");Directory.CreateDirectory(preview);
        var materials=CassetteTechnology.ReadDirectory(Path.Combine(AppContext.BaseDirectory,"NativeData"));
        var reactions=MaterialReactionCatalogue.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory,MaterialReactionCatalogue.FileName)));
        var presets=CassetteCatalogue.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory,CassetteCatalogue.FileName))).Presets;
        double Rha(string id)=>materials[id].RhaFactor;
        LayeredRecipe Recipe(CassetteModel m,LayerLayoutKind kind)=>LayeredRecipe.Build(m,LayerLayout.Build(m.Preset,kind,null),Rha);
        var originalModel=LayerHit.Model;
        try
        {
            Check(LayerHit.Model==LayerHit.HitModel.Uniform,"the hit model of a plate in layers is uniform by default");
            // ---------- 1. Equal to the plate as one block ----------
            var hitRows=new StringBuilder("recipe\tlayout\tthreat\tangle\tblock mm RHA-eq\tuniform mm\tdifference %\tper layer mm (0.22.60 to 0.22.68)\tper layer difference %\n");
            int recipes=0;double worst=0;
            foreach(var preset in presets)
            {
                CassetteModel model;ProtectionFactorTable table;
                try{model=new CassetteModel(preset,materials,reactions);table=ProtectionFactorTable.Build(model);}catch(Exception){continue;}
                bool any=false;
                foreach(var kind in new[]{LayerLayoutKind.PerLayer,LayerLayoutKind.PerBlock})
                {
                    LayeredRecipe recipe;try{recipe=Recipe(model,kind);}catch(Exception){continue;}
                    foreach(var threat in ProtectionFactorTable.Threats)
                    foreach(var angle in new[]{0d,30,60})
                    {
                        double block=table.ProtectionMm(threat,angle),uniform=LayerHit.Estimate(recipe,table,threat,angle,LayerHit.HitModel.Uniform),old=LayerHit.Estimate(recipe,table,threat,angle,LayerHit.HitModel.PerLayer);
                        double d=block>0?(uniform-block)/block*100:0,o=block>0?(old-block)/block*100:0;worst=Math.Max(worst,Math.Abs(d));any=true;
                        hitRows.AppendLine(string.Join("\t",preset.Id,kind,ProtectionFactorTable.Text(threat),angle.ToString("0",CultureInfo.InvariantCulture),block.ToString("0.###",CultureInfo.InvariantCulture),uniform.ToString("0.###",CultureInfo.InvariantCulture),d.ToString("0.####",CultureInfo.InvariantCulture),old.ToString("0.###",CultureInfo.InvariantCulture),o.ToString("0.#",CultureInfo.InvariantCulture)));
                        if(Math.Abs(d)>1)Check(false,$"{preset.Id} {kind} {ProtectionFactorTable.Text(threat)} {angle:0} deg: uniform {uniform:0.##} vs block {block:0.##} ({d:0.###} %)");
                    }
                }
                if(any)recipes++;
            }
            Check(recipes>=5,$"every recipe of the catalogue ({recipes}) at its nominal depth, straight on and at 30 and 60 degrees, long rod, full bore, jet and HESH: the plate in layers differs at most {worst:0.####} % from the same plate as one block (limit 1 %)");
            File.WriteAllText(Path.Combine(output,"UNIFORM-HIT.tsv"),hitRows.ToString());

            // ---------- the recipe t90a (the user's) ----------
            var t90a=new CassetteModel(presets.First(p=>p.Id=="t90a"),materials,reactions);var table90=ProtectionFactorTable.Build(t90a);var rec90=Recipe(t90a,LayerLayoutKind.PerBlock);
            double solidDepth=rec90.Layout.Groups.Sum(g=>g.DepthMm),scale=LayerHit.SolidScale(rec90);
            Check(rec90.Layout.Groups.Length==13&&Math.Abs(scale-rec90.Layout.DepthMm/solidDepth)<1e-12&&scale>1,$"t90a: 13 layers, recipe depth {rec90.Layout.DepthMm:0.##} mm, solid depth {solidDepth:0.##} mm, scale {scale:0.####}");
            var factorRows=new StringBuilder("angle\ttable factor (long rod)\tscale\tfactor per solid mm\n");
            foreach(var a in new[]{0d,17,36})factorRows.AppendLine(string.Join("\t",a.ToString("0",CultureInfo.InvariantCulture),table90.Factor(ProtectionThreat.LongRod,a).ToString("0.####",CultureInfo.InvariantCulture),scale.ToString("0.####",CultureInfo.InvariantCulture),(table90.Factor(ProtectionThreat.LongRod,a)*scale).ToString("0.####",CultureInfo.InvariantCulture)));
            File.WriteAllText(Path.Combine(output,"UNIFORM-FACTORS-T90A.tsv"),$"recipe depth (air included) mm\t{rec90.Layout.DepthMm:0.###}\nsolid depth mm\t{solidDepth:0.###}\n"+factorRows);

            // ---------- 2. Consistent: the same shot line, whatever the faces are taken for ----------
            {
                var line=LayerHit.StraightStack(rec90,17,rec90.Layout.DepthMm);
                // a steep side-wall piece as well, as the real shots have
                var chords=line.Concat(new[]{new LayerChord(line[^1].EndM+.001,line[^1].EndM+.0036,70.7,LayerHit.SideWallRank,false)}).ToArray();
                double Total(IReadOnlyList<LayerChord> list){var f=new double[list.Count];LayerHit.UniformFactors(rec90,table90,ProtectionThreat.LongRod,list,f);return list.Select((c,i)=>(c.EndM-c.StartM)*1000*f[i]).Sum();}
                double baseline=Total(chords);
                var errors=new (string,LayerChord[])[]
                {
                    ("every face taken for a side wall",chords.Select(c=>c with{Rank=LayerHit.SideWallRank}).ToArray()),
                    ("no face found for any piece",chords.Select(c=>c with{Rank=-1}).ToArray()),
                    ("the layers swapped (rank 0 for 12, 1 for 11, ...)",chords.Select(c=>c.Rank>=0?c with{Rank=12-c.Rank}:c).ToArray()),
                    ("every piece entered from the back",chords.Select(c=>c with{Back=!c.Back}).ToArray()),
                    ("the side wall taken for the last layer",chords.Select(c=>c.Rank==LayerHit.SideWallRank?c with{Rank=12}:c).ToArray()),
                    ("every piece cut into two (the triangles cut another way)",chords.SelectMany(c=>{var mid=(c.StartM+c.EndM)/2;return new[]{c with{EndM=mid},c with{StartM=mid}};}).ToArray()),
                    ("the pieces in another order",chords.Reverse().ToArray()),
                };
                foreach(var (name,list) in errors)Check(Math.Abs(Total(list)-baseline)<=1e-9*Math.Max(1,baseline),$"consistent: {name}: the same steel equivalent ({Total(list):0.####} vs {baseline:0.####} mm RHA-eq, difference {Total(list)-baseline:0.#e+0})");
                // the old calculation did depend on it (information): the same errors under the per-layer model
                double Old(LayerChord[] list){var f=new double[list.Length];var t=new double[list.Length];LayerHit.Factors(rec90,table90,ProtectionThreat.LongRod,list,f,t);return list.Select((c,i)=>(c.EndM-c.StartM)*1000*f[i]).Sum();}
                double spread=errors.Select(e=>Old(e.Item2)).Append(Old(chords)).Max()-errors.Select(e=>Old(e.Item2)).Append(Old(chords)).Min();
                Check(spread>.01*baseline,$"for information: the per-layer model of 0.22.60 to 0.22.68 gives {spread:0.#} mm RHA-eq of spread over the same errors (that is what the uniform model removes)");
                var gp=File.ReadAllText(Path.Combine(root,"RuntimeGroupedProtection.cs"));
                int from=gp.IndexOf("if(uniform)\r\n",StringComparison.Ordinal);if(from<0)from=gp.IndexOf("if(uniform)\n",StringComparison.Ordinal);
                int to=from<0?-1:gp.IndexOf("continue;",from,StringComparison.Ordinal);var branch=from<0||to<0?"":gp.Substring(from,to-from);
                Check(branch.Length>0&&!new[]{"TryFace","RankOf","SideWallRank","ClipSideWalls","Rank","MidpointFace","Back"}.Any(t=>branch.Contains(t,StringComparison.Ordinal))&&gp.Contains("LayeredChords(reached,ray,fragment.spawnPoint,direction,id,entity,layered.Value.Index,layered.Value.Plate,false)",StringComparison.Ordinal),"the uniform branch of the hit reads no face, rank, side wall or direction, and its pieces are collected without looking up a face");
            }

            // ---------- 3. Proportional to the solid material ----------
            foreach(var angle in new[]{0d,30})
            {
                var full=LayerHit.StraightStack(rec90,angle,rec90.Layout.DepthMm);var half=LayerHit.StraightStack(rec90,angle,rec90.Layout.DepthMm*.5);
                double Sum(LayerChord[] list){var f=new double[list.Length];LayerHit.UniformFactors(rec90,table90,ProtectionThreat.LongRod,list,f);return list.Select((c,i)=>(c.EndM-c.StartM)*1000*f[i]).Sum();}
                double ratio=Sum(half)/Sum(full);
                Check(Math.Abs(ratio-.5)<=.02,$"proportional: a box at 50 % of the recipe depth, {angle:0} deg: {ratio*100:0.###} % of the nominal result (limit 50 +- 2 %)");
                var odd=full.Where((c,i)=>i%2==0).ToArray();double expected=odd.Sum(c=>(c.EndM-c.StartM)*1000)*table90.Factor(ProtectionThreat.LongRod,angle)*scale;
                Check(Math.Abs(Sum(odd)-expected)<=1e-9*Math.Max(1,expected),$"proportional: half of the layers taken away ({odd.Length} of {full.Length}), {angle:0} deg: {Sum(odd):0.###} mm = solid {odd.Sum(c=>(c.EndM-c.StartM)*1000):0.###} mm x factor {table90.Factor(ProtectionThreat.LongRod,angle)*scale:0.####}");
            }

            // ---------- 4. The report of the user: shots 5, 7, 10 and 11 ----------
            var reportPath=Path.Combine(root,"tests","fixtures","last-session-20261111-2239.json");
            using var doc=JsonDocument.Parse(File.ReadAllText(reportPath));
            var shots=JsonSerializer.Deserialize<CombatReportShot[]>(doc.RootElement.GetProperty("shots").GetRawText(),new JsonSerializerOptions{PropertyNameCaseInsensitive=true})!;
            const int plate=366;string label=table90.Label;
            double FactorAt(double angle)=>table90.Factor(ProtectionThreat.LongRod,angle)*scale;
            var shotRows=new StringBuilder("shot\tangle range\tpieces\tsolid mm\told steel equivalent mm (0.22.67)\told mm per solid mm\tnew steel equivalent mm (uniform)\tnew mm per solid mm\n");var perMm=new Dictionary<int,double>();
            foreach(int number in new[]{5,7,10,11})
            {
                var shot=shots.Single(s=>s.Number==number);
                var pieces=shot.Events.Where(e=>e.PaidIntervals!=null).SelectMany(e=>e.PaidIntervals!).Where(p=>p.Entity==plate).ToArray();
                double solid=pieces.Sum(p=>p.LengthMm),old=pieces.Sum(p=>p.RhaMm),now=pieces.Sum(p=>p.LengthMm*FactorAt(p.Angle));perMm[number]=now/solid;
                shotRows.AppendLine(string.Join("\t",number,$"{pieces.Min(p=>p.Angle):0.#}-{pieces.Max(p=>p.Angle):0.#}",pieces.Length,solid.ToString("0.0",CultureInfo.InvariantCulture),old.ToString("0.0",CultureInfo.InvariantCulture),(old/solid).ToString("0.000",CultureInfo.InvariantCulture),now.ToString("0.0",CultureInfo.InvariantCulture),(now/solid).ToString("0.000",CultureInfo.InvariantCulture)));
                Check(pieces.Length>=10&&solid>300&&now>0,$"shot {number}: {pieces.Length} pieces of the plate, {solid:0.#} mm solid, old {old:0.#} mm RHA-eq ({old/solid:0.###} per mm), new {now:0.#} mm RHA-eq ({now/solid:0.###} per mm)");
            }
            File.WriteAllText(Path.Combine(output,"UNIFORM-SHOTS.tsv"),shotRows.ToString());
            Check(Math.Abs(perMm[5]/perMm[11]-1)<=.02&&Math.Abs(perMm[7]/perMm[11]-1)<=.02&&Math.Abs(perMm[5]/perMm[7]-1)<=.02,$"shots 5, 7 and 11 (the same angle, +-17 degrees) give the same steel equivalent per solid millimetre: {perMm[5]:0.####}, {perMm[7]:0.####} and {perMm[11]:0.####} (limit 2 %; the old values were 0.712, 0.713 and 0.861)");

            // ---------- 5. One line in the report, one post in Forensic ----------
            var presetLookup=presets.ToDictionary(p=>p.MaterialId,p=>p);
            CombatCompositeLayers.Configure(id=>id!=null&&presetLookup.TryGetValue(id,out var found)?found:null);
            var lines=new StringBuilder();
            try
            {
                var raw11=ReplayUniform(shots.Single(s=>s.Number==11),plate,588.2,FactorAt,label,13,out var loss11);
                var shot11=CombatReportTruth.Normalize(raw11);var merged11=LayeredPlateReport.Apply(shot11);
                var views=CombatHitProjection.Build(shot11);
                var rows=views.Where(v=>v.Layer?.Parts.Any(p=>p.Entity==plate&&p.Armour)==true&&v.Layer.AirGapMm==null).ToArray();
                Check(rows.Length==1,$"one row for the plate in layers in the combat report ({rows.Length}); the unmerged shot had {CombatHitProjection.Build(CombatReportTruth.Normalize(shots.Single(s=>s.Number==11))).Count(v=>v.Layer?.Parts.Any(p=>p.Entity==plate&&p.Armour)==true&&v.Layer.AirGapMm==null)} rows");
                var row=rows[0];
                var airInside=views.Where(v=>v.Layer?.AirGapMm!=null&&v.Layer.Parts.All(p=>p.Entity==plate)).ToArray();
                Check(airInside.Length==0,"no separate row for an air gap inside the plate");
                Check(views.Where(v=>v.Layer?.Parts.Any(p=>p.Entity==plate)==true).All(v=>!v.Name.Contains("crossing",StringComparison.Ordinal)&&!v.Reaction.Contains("crossing",StringComparison.Ordinal)),"no 'second crossing' row for the plate in layers (another plate that the shot crosses twice keeps its own)");
                var text=string.Join("\n",merged11.Events.Select(e=>e.Text))+string.Join("\n",views.Select(v=>v.Summary(1)));
                Check(!text.Contains("found no layer face",StringComparison.Ordinal)&&!text.Contains("layer face not found",StringComparison.Ordinal)&&!text.Contains("side wall",StringComparison.Ordinal),"no 'found no layer face' and no 'side wall' anywhere in the report");
                var pieces11=merged11.Events.Where(e=>e.PaidIntervals!=null).SelectMany(e=>e.PaidIntervals!).Where(p=>p.Entity==plate).ToArray();
                Check(pieces11.Length==1&&Math.Abs(pieces11[0].RhaMm-loss11)<1e-6,$"the plate has one paid line (solid millimetres, one factor): {pieces11.Length} line(s), {pieces11.FirstOrDefault()?.RhaMm:0.###} mm RHA-eq of {loss11:0.###}");
                var summary=row.Summary(1);
                Check(row.Reaction.Contains("13 layers",StringComparison.Ordinal)&&row.Reaction.Contains(" × ",StringComparison.Ordinal)&&row.Reaction.Contains("mm RHA-eq",StringComparison.Ordinal)&&row.Reaction.Split('\n').Length==1,"the reaction of the plate is one line: "+row.Reaction.Replace("\n"," / "));
                Check(row.Before is {} b0&&Math.Abs(b0-(merged11.Events.First(e=>e.Kind=="layerStart"&&e.Layer!.Parts.Any(p=>p.Entity==plate)).BeforeMm??0))<1e-6&&row.After is {} a0&&a0>0&&a0<row.Before,$"the row holds the penetration in ({row.Before:0.#}) and out ({row.After:0.#})");
                var model=CombatForensics.Build(raw11);
                var plateLayers=model.Layers.Where(l=>l.GapMm==null&&l.Parts.Any(p=>p.Entity==plate)).ToArray();
                Check(plateLayers.Length==1&&plateLayers[0].Sublayers is {Count:>0}&&plateLayers[0].Sublayers!.Count==t90a.Preset.Layers.Length,$"Forensic lists the plate as one layer ({plateLayers.Length}) with the {plateLayers.FirstOrDefault()?.Sublayers?.Count} layers of the recipe under it");
                Check(model.Layers.Count(l=>l.GapMm!=null&&l.Parts.All(p=>p.Entity==plate))==0&&!model.Context.Contains("second",StringComparison.Ordinal),"the layers line of Forensic counts the plate as one part: "+model.Context);
                var html=CombatForensicHtml.Render(model,"u");
                Check(html.Contains("Penetration absorbed per layer",StringComparison.Ordinal)&&plateLayers[0].Sublayers!.All(s=>html.Contains(s.Label,StringComparison.Ordinal)),"the Forensic list holds the plate once with the layers of its recipe");
                lines.AppendLine("== Combat report, shot 11 of the report of the user, replayed on 0.22.69 (T-90A block, the shot goes through) ==");
                for(int i=0;i<views.Length;i++){lines.AppendLine(views[i].Summary(i+1));lines.AppendLine();}
                var ops=new RecordSink();CombatForensicDrawing.Draw(ops,model,false);
                File.WriteAllText(Path.Combine(preview,"uniform-forensic-through.ops.json"),JsonSerializer.Serialize(new{title="Forensic, shot through the T-90A block (shot 11 replayed)",ops=ops.Ops}));
                Check(ops.Ops.Any()&&model.Outcome!="STOPPED","Forensic draws the shot that goes through: "+model.Outcome+", residual "+(model.Residual?.ToString("0.#",CultureInfo.InvariantCulture)??"-")+" mm");
            }
            finally{}

            // ---------- 6. The arrest mark ----------
            var stopRows=new StringBuilder("stop in layer (1-based)\tbudget on entry\tgroup read back\tgroups\tfraction of the recipe depth\tgroup start\tgroup end\tinside the layers of the group\n");
            {
                int G=rec90.Layout.Groups.Length;var f0=FactorAt(0);var stack=LayerHit.StraightStack(rec90,0,rec90.Layout.DepthMm);
                foreach(int s in new[]{0,6,12})
                {
                    // blocks along the shot line: entry gap, the 13 solid blocks with the real air gaps between, an exit gap; a plate before it (another entity) and the cannon behind
                    var evs=new List<CombatReportEvent>();double budget=0;for(int g=0;g<=s;g++)budget+=(stack[g].EndM-stack[g].StartM)*1000*f0;
                    double e0=(stack[s].EndM-stack[s].StartM)*1000*f0;double entry=budget-e0*.5;budget=entry;
                    var part=new CombatLayerPart(plate,"Composite armour plate","ebCassette_t90a",label,rec90.Layout.DepthMm,0,true);
                    evs.Add(new("layerStart","","Reached layer",entry,null,"gap-in",new CombatLayerInfo("gap-in",new[]{part with{Armour=false},new CombatLayerPart(35,"T-90A welded turret","rha","Rolled Homogeneous Armour",null,null,true)},true,50)));
                    evs.Add(new("layeredPlate","",MarkerText(label,G,stack.Select(c=>new PaidMaterialPiece(plate,"ebCassette_t90a",label,c.StartM,c.EndM,f0,0)).ToArray()),RelatedEntity:plate));
                    double running=entry;bool done=false;
                    for(int g=0;g<G&&!done;g++)
                    {
                        string id="b"+g;double len=(stack[g].EndM-stack[g].StartM)*1000,cost=len*f0;double after=running-cost;bool stop=g==s;if(stop)after=0;
                        var lp=part with{ThicknessMm=len,AngleDegrees=0};
                        evs.Add(new("layerStart","","Reached layer",running,null,id,new CombatLayerInfo(id,new[]{lp},false)));
                        evs.Add(new("materialReaction","","Confirmed material intervals: x",running,after,id){PaidIntervals=new[]{new PaidMaterialPiece(plate,"ebCassette_t90a",label,stack[g].StartM,stack[g].EndM,f0,0)}});
                        if(stop){evs.Add(new("nativeStop","","Native stop in plate",running,0,id));done=true;}
                        else{evs.Add(new("plateResidual","","Traversed plate",running,after,id));running=after;
                            if(g+1<G){double gap=(stack[g+1].StartM-stack[g].EndM)*1000;if(gap>0.0005){string gid="a"+g;evs.Add(new("layerStart","","Reached layer",running,null,gid,new CombatLayerInfo(gid,new[]{part with{Armour=false}},false,gap)));}}}
                    }
                    var synth=new CombatReportShot(900+s,"Outgoing","APFSDS gen 3",1500,evs,0,entry,"T-90A",3,3,"T-90A");
                    var norm=LayeredPlateReport.Apply(CombatReportTruth.Normalize(synth));var mark=norm.Events.FirstOrDefault(e=>e.Kind==LayeredPlateReport.Merged);var stopInfo=mark==null?null:LayeredPlateReport.ReadStop(mark);
                    var layout=rec90.Layout;var grp=layout.Groups[s];
                    bool inside=stopInfo!=null;int subLayersOk=0;
                    var model=CombatForensics.Build(synth);var layer=model.Layers.FirstOrDefault(l=>l.GapMm==null&&l.Parts.Any(p=>p.Entity==plate));
                    if(stopInfo!=null&&layer!=null)
                    {
                        var geometry=CombatForensicGeometry.Build(model);var band=geometry.Bands.First(b=>ReferenceEquals(b.Layer,layer));
                        float x=band.EntryX+(float)layer.PlateStop!.Fraction*(band.ExitX-band.EntryX);
                        var hitSub=band.Subs.FirstOrDefault(sb=>x>=Math.Min(sb.EntryX,sb.ExitX)-1e-3&&x<=Math.Max(sb.EntryX,sb.ExitX)+1e-3&&!sb.Layer.Air);
                        inside=hitSub!=null&&grp.Layers.Contains(hitSub.Layer.Index-1);subLayersOk=hitSub==null?0:1;
                    }
                    stopRows.AppendLine(string.Join("\t",s+1,entry.ToString("0.0",CultureInfo.InvariantCulture),stopInfo?.Group.ToString()??"-",stopInfo?.Groups.ToString()??"-",stopInfo?.Fraction.ToString("0.0000",CultureInfo.InvariantCulture)??"-",grp.StartFraction.ToString("0.0000",CultureInfo.InvariantCulture),grp.EndFraction.ToString("0.0000",CultureInfo.InvariantCulture),inside));
                    Check(stopInfo!=null&&stopInfo.Group==s+1&&stopInfo.Groups==G,$"the shot that stops in layer {s+1} of {G}: the report reads layer {stopInfo?.Group} of {stopInfo?.Groups}");
                    Check(stopInfo!=null&&stopInfo.Fraction>=grp.StartFraction-1e-6&&stopInfo.Fraction<=grp.EndFraction+1e-6,$"stop in layer {s+1}: the mark lies at {stopInfo?.Fraction*100:0.##} % of the plate depth, the layer spans {grp.StartFraction*100:0.##} to {grp.EndFraction*100:0.##} %");
                    Check(layer?.PlateStop?.Group==s+1&&model.Arrested&&model.Explanation.Contains($"layer {s+1} of {G} of the plate",StringComparison.Ordinal),$"stop in layer {s+1}: Forensic names it: {model.Explanation}");
                    Check(subLayersOk==1&&inside,$"stop in layer {s+1}: the mark of the drawing lies on one of the constituents of that layer (not in air, not in another layer)");
                    // the drawing: the text and the place of the mark
                    var rec=new RecordSink();CombatForensicDrawing.Draw(rec,model,false);
                    var json=JsonSerializer.Serialize(rec.Ops);
                    Check(json.Contains($"Arrested in layer {s+1} of {G}",StringComparison.Ordinal),$"stop in layer {s+1}: the drawing says 'Arrested in layer {s+1} of {G}'");
                    if(s==6){File.WriteAllText(Path.Combine(preview,"uniform-stop-synthetic.ops.json"),JsonSerializer.Serialize(new{title="Forensic, synthetic shot that stops in layer 7 of 13",ops=rec.Ops}));}
                }
                // a shot replayed from the report, with a budget that runs out inside the plate
                var raw=ReplayUniform(shots.Single(s=>s.Number==11),plate,250,FactorAt,label,13,out var lossStop);
                var model250=CombatForensics.Build(raw);var layer250=model250.Layers.FirstOrDefault(l=>l.GapMm==null&&l.Parts.Any(p=>p.Entity==plate));
                Check(model250.Arrested&&layer250?.PlateStop is {} place&&place.Group>=1&&place.Group<=13,$"shot 11 with a budget of 250 mm stops in the plate: {model250.Explanation}");
                var ops2=new RecordSink();CombatForensicDrawing.Draw(ops2,model250,false);
                File.WriteAllText(Path.Combine(preview,"uniform-forensic-stop.ops.json"),JsonSerializer.Serialize(new{title="Forensic, a shot that stops in the T-90A block (shot 11 replayed with 250 mm of penetration)",ops=ops2.Ops}));
                var viewsStop=CombatHitProjection.Build(CombatReportTruth.Normalize(raw));
                lines.AppendLine("== Combat report, the same shot with 250 mm of penetration (it stops in the plate) ==");
                for(int i=0;i<viewsStop.Length;i++){lines.AppendLine(viewsStop[i].Summary(i+1));lines.AppendLine();}
            }
            File.WriteAllText(Path.Combine(output,"UNIFORM-STOP.tsv"),stopRows.ToString());
            File.WriteAllText(Path.Combine(output,"UNIFORM-REPORT-LINES.txt"),lines.ToString());

            // ---------- 7. Ordinary plates and other reports are unchanged ----------
            {
                var plain=shots.Where(s=>s.Number is 8 or 9 or 1 or 2).ToArray();
                bool same=true;foreach(var s in plain){var collapsed=LayeredPlateReport.Collapse(s.Events);same&=ReferenceEquals(collapsed,s.Events)||collapsed.SequenceEqual(s.Events);}
                Check(same&&plain.Length==4,"shots without a plate in layers (1, 2, 8, 9 of the report of the user, among them the ordinary composite plate #283): the report is not touched");
                var s8=shots.Single(s=>s.Number==8);var n8=CombatReportTruth.Normalize(s8);
                Check(n8.Events.Count==s8.Events.Count&&n8.Events.All(e=>e.Kind!=LayeredPlateReport.Merged),"shot 8 (ordinary composite plate #283): the same events after Normalize, no merged plate");
                Check(CombatHitProjection.Build(n8).Length>0&&CombatForensics.Build(s8).Layers.Count>0,"shot 8: the combat report and Forensic are built as before");
                // the ordinary path of the hit: no marker, no change in the table factor
                var o=ProtectionFactorTable.Build(new CassetteModel(presets.First(p=>p.Id=="t80u"),materials,reactions));
                Check(Math.Abs(o.Factor(ProtectionThreat.LongRod,17)-o.Factor(ProtectionThreat.LongRod,17))<1e-15&&o.ProtectionMm(ProtectionThreat.LongRod,0)>0,"the table factor of an ordinary composite plate is read as before");
                var gp=File.ReadAllText(Path.Combine(root,"NativeMaterialIntervals.cs"));
                Check(gp.Contains("factor=table.Factor(threat,interval.Angle)",StringComparison.Ordinal),"the ordinary route (NativeMaterialIntervals.cs) still pays an ordinary plate with the table factor of the angle");
            }

            // ---------- 8. The switch and the old calculation ----------
            {
                var cfg=File.ReadAllText(Path.Combine(root,"RuntimeCompositePlate.cs"));
                Check(cfg.Contains("\"Layer hit model\",\"uniform\"",StringComparison.Ordinal)&&cfg.Contains("AcceptableValueList<string>(\"uniform\",\"per layer\")",StringComparison.Ordinal),"the setting Composite fill / Layer hit model has the values uniform (default) and per layer");
                LayerHit.Model=LayerHit.HitModel.PerLayer;
                var line=LayerHit.StraightStack(rec90,0,rec90.Layout.DepthMm);
                double perLayer=LayerHit.Estimate(rec90,table90,ProtectionThreat.LongRod,0);LayerHit.Model=LayerHit.HitModel.Uniform;double uniform=LayerHit.Estimate(rec90,table90,ProtectionThreat.LongRod,0);
                Check(perLayer>0&&Math.Abs(uniform-table90.ProtectionMm(ProtectionThreat.LongRod,0))<=.01*uniform&&line.Length==13,$"per layer still calculates (t90a straight on: {perLayer:0.#} mm), uniform gives the block ({uniform:0.#} mm, block {table90.ProtectionMm(ProtectionThreat.LongRod,0):0.#} mm)");
            }
        }
        finally{LayerHit.Model=originalModel;CombatCompositeLayers.Configure(null,null);}
        Console.WriteLine($"FEAT69 PASS: {checks} checks, {failures} failures");
    }
}
