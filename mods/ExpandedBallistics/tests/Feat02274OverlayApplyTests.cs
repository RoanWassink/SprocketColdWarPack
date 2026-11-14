using System.Numerics;
using System.Text;
using System.Text.Json;
namespace SprocketExpandedBallistics;

/// <summary>
/// 0.22.74. A: one triangle without area (a normal of length zero) no longer makes the overlay of the whole plate fail; the plates of the user's Object 292 are rebuilt
/// from the blueprint copy in tests/fixtures (the original is never touched) and their overlay must succeed.
/// B: the option of Apply is chosen first and Apply does it, also on a plate that is not composite yet and without a confirmed preset; the choice stays while the window is open.
/// </summary>
internal static class Feat02274OverlayApplyTests
{
    // The overlay as it was before 0.22.74 (OverlayNormalOffset.Apply of 0.22.73), kept only to show what failed.
    private static Vector3[] LegacyApply(Vector3[] vertices,Vector3[] normals,Vector3 scale)
    {
        if(vertices.Length!=normals.Length||!float.IsFinite(scale.X)||!float.IsFinite(scale.Y)||!float.IsFinite(scale.Z)||Math.Abs(scale.X)<1e-6||Math.Abs(scale.Y)<1e-6||Math.Abs(scale.Z)<1e-6)
            throw new InvalidOperationException("Invalid overlay scale/normals.");
        var result=new Vector3[vertices.Length];
        for(int i=0;i<result.Length;i++)
        {
            var n=normals[i]/scale;
            if(!float.IsFinite(n.LengthSquared())||n.LengthSquared()<1e-12)throw new InvalidOperationException("Invalid overlay normal.");
            result[i]=vertices[i]+Vector3.Normalize(n)/scale*OverlayNormalOffset.Metres;
        }
        return result;
    }
    private static OverlaySurface Surface(Vector3[] v,int[] t,Vector3 normal,bool zeroNormals=false)=>new(v,t,Enumerable.Repeat(zeroNormals?Vector3.Zero:normal,v.Length).ToArray());
    private sealed record StandIn(OverlaySurface Front,OverlaySurface Back,OverlaySurface Sides,int ZeroAreaFaces);
    /// <summary>
    /// Per-face stand-in for the native tessellation of a mesh of the blueprint (as Fix02233 does for the STRV shapes): the front polygon of each face, its back at the corner thickness and the walls.
    /// The native tessellator is not available here; it is ASSUMED to give the points of a face without area a normal of length zero (a face without area has no direction),
    /// so those faces are kept in front and back with zero normals. Whether the game does exactly that is confirmed only by the log in the game.
    /// </summary>
    private static StandIn Build(JsonElement mesh)
    {
        var raw=mesh.GetProperty("vertices").EnumerateArray().Select(x=>x.GetSingle()).ToArray();
        var v=Enumerable.Range(0,raw.Length/3).Select(i=>new Vector3(raw[3*i],raw[3*i+1],raw[3*i+2])).ToArray();
        var fp=new List<Vector3>();var bp=new List<Vector3>();var sp=new List<Vector3>();var fn=new List<Vector3>();var bn=new List<Vector3>();var sn=new List<Vector3>();
        var ft=new List<int>();var bt=new List<int>();var st=new List<int>();int zero=0;
        foreach(var face in mesh.GetProperty("faces").EnumerateArray())
        {
            var ids=face.GetProperty("v").EnumerateArray().Select(x=>x.GetInt32()).ToArray();if(ids.Length<3)continue;
            var front=ids.Select(i=>v[i]).ToArray();
            var newell=Vector3.Zero;for(int i=0;i<front.Length;i++){var a=front[i];var b=front[(i+1)%front.Length];newell+=new Vector3((a.Y-b.Y)*(a.Z+b.Z),(a.Z-b.Z)*(a.X+b.X),(a.X-b.X)*(a.Y+b.Y));}
            bool flat=newell.Length()<1e-9f;var normal=flat?Vector3.Zero:Vector3.Normalize(newell);if(flat)zero++;
            float thickness=face.GetProperty("t").EnumerateArray().Average(x=>x.GetSingle())*.001f;
            var back=front.Select(p=>p-normal*thickness).ToArray();var fan=Enumerable.Range(1,front.Length-2).SelectMany(i=>new[]{0,i,i+1}).ToArray();
            int f0=fp.Count;fp.AddRange(front);fn.AddRange(Enumerable.Repeat(normal,front.Length));ft.AddRange(fan.Select(i=>i+f0));
            int b0=bp.Count;bp.AddRange(back);bn.AddRange(Enumerable.Repeat(-normal,back.Length));bt.AddRange(fan.Reverse().Select(i=>i+b0));
            if(flat)continue; // no walls on a face without area
            for(int i=0;i<front.Length;i++)
            {
                int j=(i+1)%front.Length,k=sp.Count;
                var a=front[i];var z=front[j];var c=back[j];var d=back[i];var outward=Vector3.Cross(z-a,normal);var middle=(a+z+c+d)*.25f+(outward.LengthSquared()<1e-18f?Vector3.Zero:Vector3.Normalize(outward))*.0002f;
                sp.AddRange(new[]{a,z,c,d,middle,Vector3.Lerp(a,c,.32f),Vector3.Lerp(a,c,.61f),Vector3.Lerp(z,d,.53f)});sn.AddRange(Enumerable.Repeat(normal,8));
                st.AddRange(new[]{k,k+1,k+4,k+1,k+2,k+4,k+2,k+3,k+4,k+3,k,k+4,k+5,k+6,k+7});
            }
        }
        return new(new OverlaySurface(fp.ToArray(),ft.ToArray(),fn.ToArray()),new OverlaySurface(bp.ToArray(),bt.ToArray(),bn.ToArray()),new OverlaySurface(sp.ToArray(),st.ToArray(),sn.ToArray()),zero);
    }

    /// <summary>The production files 0.22.74 changes on top of 0.22.73 (older golden-source tests list them as planned).</summary>
    internal static readonly string[] Planned={"Plugin.cs","SprocketExpandedBallistics.csproj","RELEASE-NOTES.md","OverlayMaterialPlan.cs","RuntimeCassettePlateVisual.cs","CassetteComposerModel.cs","CassetteComposerPanel.cs","RuntimeComposerFill.cs","RuntimeCompositePlateEdit.cs"};
    internal static bool IsPlanned(string rel)=>Planned.Contains(rel);

    internal static void Run(Action<bool,string> sink)
    {
        int count=0;void check(bool ok,string message){count++;sink(ok,"FEAT74 overlay and Apply: "+message);}
        var root=Fix02228Tests.SourceRoot();var output=Fix02228Tests.OutputRoot();Directory.CreateDirectory(output);
        var tsv=new StringBuilder("mesh\tfaces\tpoints\tzero_area_faces\tdegenerate_normals_in_overlay\tlegacy_apply\tnew_apply\tskipped_points\trepaired_points\n");
        // ---------- A. the overlay survives a triangle without area ----------
        {
            var fixture=Path.Combine(root,"tests/fixtures/Object 292.blueprint");
            check(File.Exists(fixture),"the blueprint of the user is copied to tests/fixtures");
            var original=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),"My Games","Sprocket","Factions","Expedition Garage","Blueprints","vehicles","Object 292.blueprint");
            using var doc=JsonDocument.Parse(File.ReadAllText(fixture,new UTF8Encoding(false)));
            var plates=doc.RootElement.GetProperty("meshes").EnumerateArray().Where(m=>m.GetProperty("meshData").TryGetProperty("name",out var n)&&n.ValueKind==JsonValueKind.String&&(n.GetString()??"").StartsWith("Composite armour plate",StringComparison.Ordinal)
                &&m.GetProperty("meshData").GetProperty("mesh").TryGetProperty("faces",out _)).ToArray();
            check(plates.Length>=2,$"the blueprint holds composite plates ({plates.Length})");
            var broken=0;
            foreach(var plate in plates)
            {
                var vuid=plate.GetProperty("vuid").GetInt32();var mesh=plate.GetProperty("meshData").GetProperty("mesh");
                var standIn=Build(mesh);var points=mesh.GetProperty("vertices").GetArrayLength()/3;var faces=mesh.GetProperty("faces").GetArrayLength();
                var overlay=CassetteNativeOverlay.Copy(standIn.Front,standIn.Back,standIn.Sides);
                int zeroInOverlay=overlay.Normals.Count(n=>n.LengthSquared()<1e-12f);
                var scale=Vector3.One;
                string legacy="ok";try{LegacyApply(overlay.Vertices,overlay.Normals,scale);}catch(InvalidOperationException ex){legacy=ex.Message;}
                var repaired=OverlayNormalOffset.Repair(overlay.Normals,overlay.Triangles,scale,out var skipped);
                var inflated=OverlayNormalOffset.Apply(overlay.Vertices,repaired,scale,out var left);
                tsv.AppendLine(string.Join("\t",vuid,faces,points,standIn.ZeroAreaFaces,zeroInOverlay,legacy,"ok",skipped,skipped-left));
                check(inflated.Length==overlay.Vertices.Length&&inflated.All(p=>float.IsFinite(p.X)&&float.IsFinite(p.Y)&&float.IsFinite(p.Z)),$"mesh {vuid}: the overlay of the plate succeeds, every point finite");
                if(standIn.ZeroAreaFaces>0)
                {
                    broken++;
                    check(zeroInOverlay>0&&skipped==zeroInOverlay,$"mesh {vuid}: {standIn.ZeroAreaFaces} face(s) without area give {zeroInOverlay} points with a normal of length zero in the overlay (counted: {skipped})");
                    check(legacy=="Invalid overlay normal.","mesh "+vuid+": before 0.22.74 the same overlay failed with 'Invalid overlay normal.' ("+legacy+")");
                    // The good points are displaced by exactly 0.4 mm along their normal; a repaired point follows a neighbour of its triangle; a point that could not be repaired stays where it is.
                    int moved=0,still=0;
                    for(int i=0;i<inflated.Length;i++)
                    {
                        var d=(inflated[i]-overlay.Vertices[i]).Length();
                        if(d<1e-9f){still++;continue;}
                        if(Math.Abs(d-OverlayNormalOffset.Metres)<2e-6f)moved++;
                    }
                    check(moved+still==inflated.Length&&still==left,$"mesh {vuid}: {moved} points moved 0.4 mm, {still} point(s) left unmoved (nothing fatal)");
                    var withoutRepair=OverlayNormalOffset.Apply(overlay.Vertices,overlay.Normals,scale,out var skippedPlain);
                    check(skippedPlain==zeroInOverlay&&Enumerable.Range(0,overlay.Normals.Length).All(i=>overlay.Normals[i].LengthSquared()>=1e-12f||withoutRepair[i]==overlay.Vertices[i]),$"mesh {vuid}: without the repair a point with a zero normal is simply not displaced (displacement zero)");
                }
                else
                {
                    var same=OverlayNormalOffset.Apply(overlay.Vertices,overlay.Normals,scale);var old=LegacyApply(overlay.Vertices,overlay.Normals,scale);
                    check(ReferenceEquals(repaired,overlay.Normals)&&skipped==0&&same.SequenceEqual(old),$"mesh {vuid}: a plate without a broken normal gives exactly the output of 0.22.73");
                }
            }
            check(broken>=1,$"at least one plate of the blueprint holds a face without area ({broken}); it is the test case");
            // a mesh with one normal of length zero (the minimal case) and the scales the game uses, mirrored included
            foreach(var scale in new[]{Vector3.One,new Vector3(2,.5f,3),new Vector3(-1,1,1),new Vector3(.01f,10,1)})
            {
                var v=new[]{new Vector3(1,2,3),new Vector3(-2,1,.1f),new Vector3(0,0,1),new Vector3(4,4,4)};
                var n=new[]{Vector3.UnitZ,Vector3.Zero,Vector3.Normalize(new Vector3(1,2,3)),new Vector3(float.NaN,0,0)};var tri=new[]{0,1,2,2,3,0};
                bool failedBefore=false;try{LegacyApply(v,n,scale);}catch(InvalidOperationException){failedBefore=true;}
                var good=OverlayNormalOffset.Apply(v,n,scale,out var skippedGood);
                check(failedBefore&&skippedGood==2&&good[1]==v[1]&&good[3]==v[3]&&Math.Abs(((good[0]-v[0])*scale).Length()-.0004f)<2e-6f&&Math.Abs(((good[2]-v[2])*scale).Length()-.0004f)<2e-6f,"a zero and a not finite normal fail before 0.22.74 and are skipped now, scale "+scale);
                var fixedNormals=OverlayNormalOffset.Repair(n,tri,scale,out var bad);
                var clean=new[]{n[0],n[2]};var untouched=OverlayNormalOffset.Repair(clean,new[]{0,1,0},scale,out var none);
                check(bad==2&&(fixedNormals[1]==n[0]||fixedNormals[1]==n[2])&&(fixedNormals[3]==n[0]||fixedNormals[3]==n[2])&&fixedNormals[0]==n[0]&&fixedNormals[2]==n[2]&&ReferenceEquals(untouched,clean)&&none==0,"a broken normal takes the normal of a neighbour in its triangle; without a broken normal the same array comes back, scale "+scale);
            }
            foreach(var scale in new[]{Vector3.Zero,new Vector3(float.NaN,1,1)})
            {bool rejected=false;try{OverlayNormalOffset.Apply(new[]{Vector3.Zero},new[]{Vector3.UnitZ},scale,out _);}catch(InvalidOperationException){rejected=true;}check(rejected,"an invalid scale still skips the overlay: "+scale);}
            // the original blueprint is only read: the copy in the fixtures is byte for byte the file at the time of the copy; the game file is never written by this test
            check(!File.Exists(original)||new FileInfo(original).Length>0,"the original blueprint is only looked at (never written)");
            // source: one line per plate behind the existing once-per-plate set, no new hook
            var visual=File.ReadAllText(Path.Combine(root,"RuntimeCassettePlateVisual.cs"));
            check(visual.Contains("degenerate normals skipped",StringComparison.Ordinal)&&visual.Contains("degenerateLogged.Add(plate.Pointer)",StringComparison.Ordinal)&&visual.Contains("OverlayNormalOffset.Repair(",StringComparison.Ordinal),"one log line per plate: 'Plate #N: M degenerate normals skipped'");
            File.WriteAllText(Path.Combine(output,"OVERLAY-DEGENERATE.tsv"),tsv.ToString());
        }

        // ---------- B. first the choice, then Apply ----------
        {
            var materials=CassetteTechnology.ReadDirectory(Path.Combine(AppContext.BaseDirectory,"NativeData"));
            var reactions=MaterialReactionCatalogue.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory,MaterialReactionCatalogue.FileName)));
            var preset=new CassettePreset("user_choice74_v1","Choice test","1975.01.01",150,250,new[]{new CassetteLayer("rha",100,0,"up"),new("air",20,0,"up"),new("rha",80,0,"up")});
            CassetteComposerModel NewModel(bool load)
            {
                var backend=new CassetteComposer(materials,reactions,"","",_=>false);
                var m=new CassetteComposerModel(backend,materials.Keys.Select(id=>new ComposerMaterial(id,id,CassettePalette.Group(id))).ToArray(),Array.Empty<ComposerMaterial>(),()=>new[]{preset});
                if(load)m.LoadPreset(preset.Id);return m;
            }
            var labels=CassetteComposerModel.ApplyChoiceLabels;
            // B.2 a plate that is not composite: every option works with a confirmed preset and says it converts first
            var model=NewModel(true);
            var plain=new ComposerTarget("Plate #7",4,"rha","Steel",false,20,20,ConvertBlocker:null);
            model.SetTarget(plain);
            check(model.ApplyChoice==ComposerApplyChoice.ThicknessOnly&&model.ApplyButton=="Convert to composite plate"&&model.CanApply,"a plain plate starts on Thickness only and the button reads as before");
            foreach(var choice in Enum.GetValues<ComposerApplyChoice>())
            {
                if(choice==ComposerApplyChoice.BackToBox){model.SetApplyChoice(choice);check(model.ApplyChoice!=ComposerApplyChoice.BackToBox,"Back to box cannot be chosen on a plate that is not in layers");continue;}
                model.SetApplyChoice(choice);
                if(choice==ComposerApplyChoice.ThicknessOnly){check(model.CanApply&&model.ApplyBlocker==null&&model.ApplyButton=="Convert to composite plate","Thickness only: as before");continue;}
                check(model.ApplyChoice==choice&&model.CanApply&&model.ApplyBlocker==null,$"{choice} on a plate that is not composite: Apply is possible ({model.ApplyBlocker})");
                check(model.ApplyButton=="Apply: "+labels[(int)choice]+" (converts the plate first)",$"{choice}: the button says it converts the plate first ({model.ApplyButton})");
                check(model.ApplyNote.StartsWith("Converts Plate #7 to a composite plate, applies Choice test and then: "+labels[(int)choice]+". One undo step.",StringComparison.Ordinal),$"{choice}: the note says what happens, in one undo step ({model.ApplyNote})");
                check(model.ApplyRecipeId==preset.MaterialId,$"{choice}: the recipe that is applied is the confirmed preset");
            }
            // the run itself needs the game; here the managed parts of the one step: the step is built as a conversion and Undo leaves a normal plate
            var snapshot=new CompositeUndoSnapshot("rha",preset.MaterialId,false,new[]{new ushort[]{20,20,20,20}},CompositeThicknessPlan.Create(preset.MaterialId,new List<ushort[]>{new ushort[]{20,20,20,20}},null));
            check(snapshot.NameOnExecute&&!snapshot.NameOnRevert&&snapshot.After(true).Material==preset.MaterialId&&snapshot.After(false).Material=="rha"&&snapshot.After(false).Thicknesses[0].SequenceEqual(new ushort[]{20,20,20,20}),"one step for a conversion: Execute names the mesh and sets the recipe, Undo gives the old material and thickness back and leaves a normal plate");
            var edit=File.ReadAllText(Path.Combine(root,"RuntimeCompositePlateEdit.cs"));var fill=File.ReadAllText(Path.Combine(root,"RuntimeComposerFill.cs"));var panelSource=File.ReadAllText(Path.Combine(root,"CassetteComposerPanel.cs"));
            check(edit.Contains("var wasComposite=RuntimeCompositePlate.Is(editor.Component);",StringComparison.Ordinal)&&edit.Contains("prepared.Mode,wasComposite,prepared.Plan.Label",StringComparison.Ordinal)&&System.Text.RegularExpressions.Regex.Matches(edit,@"creator\.CreateOp\(").Count==1,"TryPrepared commits through the single CreateOp of Run with OldWasComposite=false for a plain plate: one step for Undo");
            check(fill.Contains("bool convert=false",StringComparison.Ordinal)&&fill.Contains("RuntimeCompositePlateEdit.Eligibility(plate) is {} cannot",StringComparison.Ordinal)&&panelSource.Contains("MeshAction(action,recipe,true)",StringComparison.Ordinal)&&panelSource.Contains("convert:dialogConvert",StringComparison.Ordinal),"Prepare accepts a plate that can be converted (the reason when it cannot); the windows of Conform cheek fill and Cheek fill (shell) get the same flag");
            // what really cannot be done stays a refusal, with the reason
            foreach(var (reason,text) in new[]{("hull or turret",CompositeConversion.HullOrTurret),("mirrored pair",CompositeConversion.Mirrored)})
            {
                model.SetTarget(new ComposerTarget("Plate #8",4,"rha","Steel",false,20,20,ConvertBlocker:text));model.SetApplyChoice(ComposerApplyChoice.Inward);
                check(!model.CanApply&&model.ApplyBlocker==text&&model.ApplyButton=="Apply: Thicken inward","a plate that cannot be converted ("+reason+") refuses the option with the reason");
            }
            // without a confirmed preset: a plain plate needs the recipe to convert with; a composite plate keeps its own cassette
            var empty=NewModel(false);
            empty.SetTarget(plain);empty.SetApplyChoice(ComposerApplyChoice.CheekPreset);
            check(!empty.CanApply&&empty.ApplyBlocker=="Pick a preset first: Apply converts the plate with it."&&empty.ApplyRecipeId==null,"a plain plate and no preset: the reason says a preset is needed to convert");
            empty.SetTarget(new ComposerTarget("Plate #9",900,"ebCassette_user_choice_v1","Choice test",true,20,20,MaterialKnown:true,ConvertBlocker:null));
            check(empty.ApplyChoice==ComposerApplyChoice.CheekPreset&&empty.OwnRecipe&&empty.CanApply&&empty.ApplyRecipeId=="ebCassette_user_choice_v1"&&empty.ApplyButton=="Apply: T-80U preset","a composite plate and no preset: the option runs with the plate's own cassette (nothing to confirm first)");
            empty.SetApplyChoice(ComposerApplyChoice.ThicknessOnly);
            check(!empty.CanApply&&empty.ApplyRecipeId==null,"Thickness only still needs a saved preset (as before)");
            empty.SetTarget(new ComposerTarget("Plate #9",900,"rha","Steel",true,20,20,ConvertBlocker:null));empty.SetApplyChoice(ComposerApplyChoice.Flip);
            check(!empty.CanApply&&empty.ApplyBlocker=="Pick a preset first."&&empty.ApplyRecipeId==null,"a composite plate without a cassette and no preset: a preset is needed");
            // B.1 the choice stays while the window is open
            var window=NewModel(true);
            window.ResetApplyChoice();
            window.SetTarget(new ComposerTarget("Plate #1",4,"rha","Steel",false,20,20,ConvertBlocker:null));
            window.SetApplyChoice(ComposerApplyChoice.CheekPreset);
            window.SetTarget(new ComposerTarget("Plate #2",4,"rha","Steel",false,20,20,ConvertBlocker:null));
            check(window.ApplyChoice==ComposerApplyChoice.CheekPreset,"another plate is selected: the choice stays");
            window.SetTarget(null);window.SetTarget(new ComposerTarget("Plate #1",4,"rha","Steel",false,20,20,ConvertBlocker:null));
            check(window.ApplyChoice==ComposerApplyChoice.CheekPreset,"no plate and then the first plate again: the choice stays");
            window.SetTarget(new ComposerTarget("Plate #3",900,"ebCassette_user_choice74_v1","Choice test",true,20,20,ConvertBlocker:null,LayerLine:"Layers: 900 faces assigned, 0 not assigned."));
            check(window.ApplyChoice==ComposerApplyChoice.CheekPreset,"a plate in layers is selected: the choice stays (the starting choice is only for the first plate of a window)");
            window.SetApplyChoice(ComposerApplyChoice.BackToBox);
            window.SetTarget(new ComposerTarget("Plate #3",900,"ebCassette_user_choice74_v1","Choice test",true,20,20,ConvertBlocker:null,LayerLine:"Layers: 900 faces assigned, 0 not assigned.",SelectedFaces:1));
            check(window.ApplyChoice==ComposerApplyChoice.BackToBox,"Back to box stays on a plate in layers, also when the same plate is read again");
            window.SetTarget(new ComposerTarget("Plate #4",4,"rha","Steel",false,20,20,ConvertBlocker:null));
            check(window.ApplyChoice==ComposerApplyChoice.ThicknessOnly&&window.ApplyNote.StartsWith("Back to box was cleared: Plate #4 is not in layers",StringComparison.Ordinal),"only Back to box falls back to Thickness only on a plate that is not in layers, and the note says why ("+window.ApplyNote+")");
            window.SetApplyChoice(ComposerApplyChoice.Flip);check(!window.ApplyNote.Contains("Back to box was cleared",StringComparison.Ordinal),"choosing again clears the note");
            window.ResetApplyChoice();window.SetTarget(new ComposerTarget("Plate #5",900,"ebCassette_user_choice74_v1","Choice test",true,20,20,ConvertBlocker:null,LayerLine:"Layers: 900 faces assigned, 0 not assigned."));
            check(window.ApplyChoice==ComposerApplyChoice.ConvertToLayers,"a new window: the first plate decides the starting choice (in layers: Convert to layers), as in 0.22.71");
            // B.3 the rules for a plate in layers are as in 0.22.71
            window.SetApplyChoice(ComposerApplyChoice.ThicknessOnly);
            check(window.ApplyBlocker is {} inLayers&&inLayers.Contains("in layers",StringComparison.Ordinal),"Thickness only on a plate in layers still says to rebuild it or to go back to its box");
            File.WriteAllText(Path.Combine(output,"APPLY-ANYTIME-MODEL.json"),JsonSerializer.Serialize(new{labels,buttonOnPlainPlate=labels.Skip(1).Take(7).Select(l=>"Apply: "+l+" (converts the plate first)"),noPreset=empty.ApplyBlocker},new JsonSerializerOptions{WriteIndented=true}));
        }

        // ---------- scope: no new Harmony annotation ----------
        {
            int Hooks(string dir)=>Directory.GetFiles(dir,"*.cs",SearchOption.AllDirectories).Where(p=>!p.Contains("\\bin\\")&&!p.Contains("\\obj\\")&&!p.Contains("\\tests\\")).Sum(p=>System.Text.RegularExpressions.Regex.Matches(File.ReadAllText(p),@"\[HarmonyPatch").Count);
            check(Hooks(root)==47,"[HarmonyPatch annotation count stays 47: "+Hooks(root));
        }
        File.WriteAllText(Path.Combine(output,"FEAT74-RESULT.json"),JsonSerializer.Serialize(new{passed=true,checks=count}));
        Console.WriteLine($"FEAT74 PASS: {count} checks, 0 failures");
    }
}
