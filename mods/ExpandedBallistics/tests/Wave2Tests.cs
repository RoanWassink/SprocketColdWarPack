using System.Diagnostics;
using System.Globalization;
using System.Numerics;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Xml.Linq;
using SprocketExpandedBallistics.Materials;
namespace SprocketExpandedBallistics;

internal static class Wave2Tests
{
    internal static void Run(Action<bool,string> check)
    {
        var root=AppContext.BaseDirectory;
        var partPath=Path.Combine(root,"CompositePart/compositeArmourPlatePart.json");
        using var part=JsonDocument.Parse(File.ReadAllText(partPath));var p=part.RootElement;
        check(Guid.Parse(p.GetProperty("guid").GetString()!)==Guid.Parse(CompositePlate.PrefabGuid),"wave2: fixed prefab GUID");
        check(p.GetProperty("tags")[0].GetString()=="plateStructurePrefab","wave2: native prefab tag");
        var mesh=p.GetProperty("components")[0].GetProperty("info").GetProperty("mesh");
        // 0.22.6: the v0.0 prefab wrapper carries the identity name, so the game's own placement needs
        // no mesh swap. FromJson receives the bare MeshBlueprint that ToJson returns (minorVersion at root).
        var cube=JsonDocument.Parse(CompositePlate.QuadMeshJson).RootElement;
        check(mesh.GetProperty("v").GetString()=="0.0"&&mesh.GetProperty("name").GetString()==CompositePlate.Name
            &&mesh.GetProperty("mesh").GetProperty("minorVersion").GetInt32()==0,"wave2: prefab in the cube's v0.0 format with the identity name");
        check(cube.TryGetProperty("majorVersion",out var major)&&major.GetInt32()==0
            &&cube.GetProperty("minorVersion").GetInt32()==3&&!cube.TryGetProperty("mesh",out _)&&!cube.TryGetProperty("name",out _)
            &&cube.EnumerateObject().Select(x=>x.Name).OrderBy(x=>x).SequenceEqual(new[]{"edgeFlags","edges","faces","majorVersion","minorVersion","vertices"}),
            "wave2: FromJson input is the bare ToJson MeshBlueprint (no wrapper or name)");
        var packedFace=cube.GetProperty("faces")[0];
        var authored=mesh.GetProperty("mesh");
        var packed=authored.GetProperty("vertices").EnumerateArray().Select(e=>e.GetSingle()).ToArray();
        check(packedFace.GetProperty("tm").GetInt32()==0&&packedFace.GetProperty("te").GetInt32()==0
            &&cube.GetProperty("vertices").EnumerateArray().Select(e=>e.GetSingle()).SequenceEqual(packed),
            "wave2: packed quad equals the prefab geometry, AlongFaceNormal, no edge override");
        var vertices=Enumerable.Range(0,packed.Length/3).Select(i=>new Vector3(packed[i*3],packed[i*3+1],packed[i*3+2])).ToArray();
        var face=authored.GetProperty("faces")[0];var order=face.GetProperty("v").EnumerateArray().Select(e=>e.GetInt32()).ToArray();
        check(vertices.Length==4&&authored.GetProperty("faces").GetArrayLength()==1&&face.GetProperty("t").EnumerateArray().All(e=>e.GetInt32()==20),"wave2: single 20 mm quad");
        check(face.EnumerateObject().Select(x=>x.Name).OrderBy(x=>x).SequenceEqual(new[]{"t","v"}),"wave2: prefab faces as the cube prefab ({v,t}, native default thickening)");
        check(Vector3.Normalize(Vector3.Cross(vertices[order[1]]-vertices[order[0]],vertices[order[2]]-vertices[order[0]]))==Vector3.UnitY,"wave2: declared front is +Y");
        check(Math.Abs(vertices.Max(v=>v.X)-vertices.Min(v=>v.X)-1)<1e-6&&Math.Abs(vertices.Max(v=>v.Z)-vertices.Min(v=>v.Z)-1)<1e-6&&vertices.All(v=>Math.Abs(v.Y-.02f)<1e-6),"wave2: one metre quad, outer face 20 mm above mounting plane");
        check(XDocument.Load(Path.Combine(root,"CompositePart/compositeArmourPlate.xml")).Root!.Element("name")!.Value==CompositePlate.Name,"wave2: native localization label");
        var cache=new CompositePlate.Cache();int reads=0;string name=CompositePlate.Name;
        bool Read()=>cache.Get((IntPtr)12,()=>{reads++;return name;});
        check(Read()&&Read()&&reads==1,"wave2: name cached by mesh pointer");
        // Arbitrary vertex, face and bend edits do not form part of identity.
        vertices[0]+=new Vector3(3,2,7);cache.Invalidate((IntPtr)12);
        check(Read()&&reads==2,"wave2: vertex edits retain composite identity");
        // Simulated PlateStructureMeshBlueprint (ToMeshBlueprint/LoadBlueprint): name at wrapper level,
        // mesh data unchanged by the separate FromJson payload.
        var roundtrip=new JsonObject
        {
            ["name"]=CompositePlate.Name,["v"]="0.0",["smoothAngle"]=0,["gridSize"]=1,["format"]="freeform",
            ["mesh"]=JsonNode.Parse(CompositePlate.QuadMeshJson)!
        };
        roundtrip["mesh"]!["vertices"]![0]=99;
        check(CompositePlate.IsName(roundtrip["name"]!.GetValue<string>())
            &&roundtrip["mesh"]!["vertices"]![0]!.GetValue<int>()==99,"wave2: name survives serialized mesh editing on the native blueprint wrapper");
        name="Different library shape";cache.Invalidate((IntPtr)12);
        check(!Read()&&!CompositePlate.IsName("composite armour plate")&&!cache.Get(IntPtr.Zero,()=>CompositePlate.Name),"wave2: loading another shape removes identity; exact name only");
        check(!MaterialMenuVisibility.Visible("ebCassette_t64k",false)&&!MaterialMenuVisibility.Visible("ebCassette_t64k",false,true)&&!MaterialMenuVisibility.Visible("ebCassette_user_test_v1",false),"wave2: cassettes only in composite menu");
        check(MaterialMenuVisibility.Visible("rha",false)&&!MaterialMenuVisibility.Visible("cwepNeraCassette",false,true),"wave2: existing player/retired menu policy retained");
        var old=new[]{new ushort[]{1,2,3},new ushort[]{5,7,6,8}};
        var set=CompositeThicknessPlan.Create("ebCassette_test",old,132.75);
        check(set.Thicknesses.All(f=>f.All(t=>t==133))&&old[0][0]==1&&old[1][3]==8,"wave2: one immutable material/all-corners thickness plan rounds mm");
        var restored=CompositeThicknessPlan.Create("rha",old,null);
        check(restored.MaterialId=="rha"&&restored.Thicknesses[1].SequenceEqual(old[1])&&!ReferenceEquals(restored.Thicknesses[0],old[0]),"wave2: non-cassette material leaves all corner thicknesses intact");
        Reject(()=>CompositeThicknessPlan.Create("x",old,double.NaN),"non-finite thickness");
        Reject(()=>CompositeThicknessPlan.Create("x",old,40000),"native thickness range");

        var quad=new[]{new Vector3(0,0,0),new Vector3(1,0,0),new Vector3(1,1,0),new Vector3(0,1,0)};
        var visual=CassetteEdgeMesh.Build(quad,new[]{new CassetteVisualFace(new[]{0,1,2,3},20)});
        check(visual.BoundaryEdges==4&&visual.Edges.Vertices.Length==16&&visual.Edges.Triangles.Length==24&&!visual.TintOnly,"wave2: one open-edge quad per boundary edge");
        check(visual.Edges.Vertices.All(v=>v.Z<=0)&&Math.Abs(visual.Edges.Vertices[2].Z+.02f)<1e-7&&visual.Front.Vertices.All(v=>Math.Abs(v.Z-.0003f)<1e-7),"wave2: stripes grow inward, front offset grows outward");
        check(visual.Arrows.Triangles.Length>0&&visual.Edges.Uv[2].X==1&&visual.Edges.Uv[1].Y==20,"wave2: geometric entry marks and 50 mm repeating UV");
        var joined=new[]{new CassetteVisualFace(new[]{0,1,2},20),new CassetteVisualFace(new[]{0,2,3},40)};
        check(CassetteEdgeMesh.Build(quad,joined).BoundaryEdges==4,"wave2: internal shared diagonal has no stripe");
        var bent=quad.Concat(new[]{new Vector3(1,0,1),new Vector3(1,1,1)}).ToArray();
        var bendFaces=new[]{new CassetteVisualFace(new[]{0,1,2,3},20),new CassetteVisualFace(new[]{1,4,5,2},40)};
        var curved=CassetteEdgeMesh.Build(bent,bendFaces);
        check(curved.BoundaryEdges==6&&curved.Edges.Vertices.Length==24&&curved.Front.Vertices.Length==8,"wave2: bent strip stripes and front on each face");
        check(Math.Abs((curved.Edges.Vertices[14]-curved.Edges.Vertices[13]).Length()-.04f)<1e-6,"wave2: bent second face has its own thickness and normal");
        var nonmanifold=CassetteEdgeMesh.Build(quad,joined.Append(joined[0]).ToArray());
        check(nonmanifold.TintOnly&&nonmanifold.Edges.Triangles.Length==0&&nonmanifold.Front.Triangles.Length>0&&nonmanifold.Arrows.Triangles.Length==0,"wave2: non-manifold fallback has front only");
        check(CassetteEdgeMesh.Build(quad,new[]{new CassetteVisualFace(new[]{0,1,2,3},0)}).TintOnly,"wave2: zero thickness fallback");
        var manyV=new List<Vector3>();var manyF=new List<CassetteVisualFace>();
        for(int i=0;i<129;i++){int k=manyV.Count;manyV.AddRange(quad.Select(v=>v+Vector3.UnitX*i*2));manyF.Add(new(new[]{k,k+1,k+2,k+3},20));}
        check(CassetteEdgeMesh.Build(manyV,manyF).TintOnly,"wave2: 512 boundary-edge budget");
        check(CassetteEdgeMesh.Build(quad,new[]{new CassetteVisualFace(new[]{0,1,2,3},20)},true).Front.Triangles.Length==24,"wave2: approved opaque frame fallback");
        var concave=new[]{new Vector3(0,0,0),new Vector3(2,0,0),new Vector3(2,1,0),new Vector3(1,1,0),new Vector3(1,2,0),new Vector3(0,2,0)};
        var concaveMesh=CassetteEdgeMesh.Build(concave,new[]{new CassetteVisualFace(new[]{0,1,2,3,4,5},20)});
        double areaSum=0;for(int i=0;i<concaveMesh.Front.Triangles.Length;i+=3)
        {var t=concaveMesh.Front.Triangles;var v=concaveMesh.Front.Vertices;areaSum+=Vector3.Cross(v[t[i+1]]-v[t[i]],v[t[i+2]]-v[t[i]]).Length()*.5;}
        check(!concaveMesh.TintOnly&&concaveMesh.BoundaryEdges==6&&Math.Abs(areaSum-3)<1e-7,"wave2: concave freeform face tint triangulates without crossing its notch");

        var materials=CassetteTechnology.ReadDirectory(Path.Combine(root,"NativeData"));
        var reactions=MaterialReactionCatalogue.Parse(File.ReadAllText(Path.Combine(root,MaterialReactionCatalogue.FileName)));
        var presets=CassetteCatalogue.Parse(File.ReadAllText(Path.Combine(root,CassetteCatalogue.FileName)));
        var output=Path.Combine(Fix02228Tests.OutputRoot(),"legacy-fixtures");Directory.CreateDirectory(output);
        var dir=Path.Combine(output,"composer-fixtures","run-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(dir);
        var sidecar=Path.Combine(dir,CassetteCatalogue.FileName);File.WriteAllText(sidecar,CassetteComposer.Serialize(presets.Presets));
        bool used=true;var composer=new CassetteComposer(materials,reactions,sidecar,Path.Combine(dir,"Technology"),_=>used);
        var draft=presets.Presets.Single(p=>p.Id=="nera") with{Id="user_test",Label="User test <name>"};
        check(composer.Validate(draft).Length==0,"wave2: complete NERA validates");
        var unsupported=draft with{Layers=new[]{new CassetteLayer("elastomerNERA",15,30,"up"),new("rha",3,30,"up")}};
        check(composer.Validate(unsupported).Any(i=>i.Code=="support")&&composer.Validate(unsupported).Any(i=>i.Code=="nera-air"),"wave2: missing sandwich support and NERA air warnings");
        check(composer.Validate(draft with{Layers=new[]{new CassetteLayer("rha",20,76,"up")}}).Any(i=>i.Code=="tilt"&&i.Error),"wave2: tilt above 75 blocked");
        check(composer.Validate(draft with{MinimumDepthMm=150,MaximumDepthMm=200}).Any(i=>i.Code=="depth"&&i.Error),"wave2: nominal D outside window blocked");
        var nominal=new CassetteModel(draft,materials,reactions).DepthMm;
        check(composer.Validate(draft with{MinimumDepthMm=nominal-.01,MaximumDepthMm=nominal+.01}).Any(i=>i.Code=="native-depth"&&i.Error),"wave2: a window excluding the rounded native depth cannot save an unusable cassette");
        check(composer.Validate(draft with{Layers=Enumerable.Repeat(new CassetteLayer("rha",1,0,"up"),65).ToArray()}).Any(i=>i.Code=="layer-count"&&i.Error),"wave2: maximum 64 layers");
        check(composer.Validate(draft with{Layers=new[]{new CassetteLayer("doesNotExist",20,0,"up")}}).Any(i=>i.Code=="material"&&i.Error),"wave2: missing material blocks save");
        var raster=CassetteStripRaster.Build(draft);check(raster.Length==8192&&raster.Contains(0x1b1c15ffu)&&raster.Contains(0x8f8670ffu)&&raster.Contains(0xdc9e33ffu),"wave2: air hatch, family stripes and front chevrons rasterized");
        var report=new StringBuilder("# Composer API preview\n\nSame 600 mm L/D 30 reference rod, 800 mm HEAT reporting convention and shared recipe calculation as wave 1. HEAT/KE values below are mm RHA equivalent; no native game trial is implied. Cost/m2 uses the model's multiplier units (before the game-wide armour price constant).\n\n| Preset | D mm | kg/m2 | cost/m2 | native RHA factor | spall source | 0 | +30 | -30 | +60 | -60 |\n|---|---:|---:|---:|---:|---|---|---|---|---|---|\n");
        foreach(var preset in presets.Presets)
        {
            var preview=composer.Preview(preset);var model=new CassetteModel(preset,materials,reactions);
            check(preview.DepthMm==model.DepthMm&&preview.KgM2==model.MassKgM2&&preview.CostM2==model.CostM2&&preview.NativeRha==model.Passive.RhaFactor,"wave2: preview derived values share runtime model "+preset.Id);
            check(preview.Protection.Length==6&&preview.Protection.All(r=>double.IsFinite(r.HeatMm)&&r.KeMm>=0&&double.IsFinite(r.KeMm)),"wave2: preview six signed-angle results "+preset.Id);
            var row=preview.Protection[0];const double energy=.5*17500*.6*1600*1600*Math.PI*.25*.02*.02;
            var expected=model.Evaluate(model.DepthMm,-Vector3.UnitZ,Vector3.UnitZ,Vector3.UnitY,Vector3.UnitX,"chemicalJet",.02,energy,true);
            check(Math.Abs(row.HeatMm-model.DepthMm*model.Passive.RhaFactor-expected.JetWorkMm)<1e-9,"wave2: PRESETS calculation parity "+preset.Id);
            check(preview.SpallSource==preset.Layers.Last(l=>l.Material!="air").Material,"wave2: rear solid spall source "+preset.Id);
            string F(double x)=>x.ToString("0.##",CultureInfo.InvariantCulture);
            string Row(CassetteProtection r)=>F(r.HeatMm)+" / "+F(r.KeMm);
            report.AppendLine($"| {preset.Label} | {F(preview.DepthMm)} | {F(preview.KgM2)} | {F(preview.CostM2)} | {F(preview.NativeRha)} | {preview.SpallSource} | {Row(preview.Protection[0])} | {Row(preview.Protection[2])} | {Row(preview.Protection[3])} | {Row(preview.Protection[4])} | {Row(preview.Protection[5])} |");
        }
        File.WriteAllText(Path.Combine(output,"COMPOSER-PREVIEW.md"),report.ToString());
        var save=composer.SaveNewVersion(draft);string oldTech=File.ReadAllText(Path.Combine(dir,"Technology",save.Preset.MaterialId+".json"));
        check(save.Preset.Id=="user_test_v1"&&save.RestartRequired,"wave2: versioned user identity and conservative reload status");
        var v2=composer.SaveNewVersion(save.Preset with{Layers=save.Preset.Layers.Select(l=>l with{ThicknessMm=l.ThicknessMm*1.1}).ToArray()});
        check(v2.Preset.Id=="user_test_v2"&&File.ReadAllText(Path.Combine(dir,"Technology",save.Preset.MaterialId+".json"))==oldTech,"wave2: used v1 stays byte-immutable when v2 saved");
        check(CassetteTechnology.ReadDirectory(Path.Combine(dir,"Technology"))[save.Preset.MaterialId]==new CassetteModel(save.Preset,materials,reactions).Passive,"wave2: generated user Technology exactly matches derived native values");
        Reject(()=>composer.Delete(save.Preset.Id),"used preset deletion");
        used=false;composer.Delete(v2.Preset.Id);
        check(!CassetteCatalogue.Parse(File.ReadAllText(sidecar)).Presets.Any(p=>p.Id==v2.Preset.Id)&&!File.Exists(Path.Combine(dir,"Technology",v2.Preset.MaterialId+".json")),"wave2: unused version deletion removes sidecar and Technology");
        var share=Path.Combine(dir,"shared.cassette.json");composer.Export(save.Preset,share);var imported=composer.Import(share);
        composer.Export(save.Preset,Path.Combine(output,"example-user.cassette.json"));
        check(imported==save.Preset with{Layers=imported.Layers}&&imported.Layers.SequenceEqual(save.Preset.Layers),"wave2: share export/import roundtrip");
        Reject(()=>composer.Export(draft,Path.Combine(dir,"wrong.json")),"share extension");
        var before=File.ReadAllText(sidecar);File.WriteAllText(share,"{}");Reject(()=>composer.Import(share),"invalid import");
        check(File.ReadAllText(sidecar)==before,"wave2: invalid import never mutates catalogue");
        // An orphan from an interrupted save reserves its identity forever.
        File.WriteAllText(Path.Combine(dir,"Technology","ebCassette_user_test_v2.json"),"orphan");
        check(composer.SaveNewVersion(draft).Preset.Id=="user_test_v3","wave2: orphan Technology identity is never overwritten");
        var stack=new[]{Slab("rha",0,.02f),Slab("ceramicArray",-.03f,.04f)};
        var selection=composer.FromSelection("selection","Selection","1980.01.01",stack,Vector3.UnitZ,Vector3.UnitY,Vector3.UnitX);
        check(selection.Layers.Length==3&&selection.Layers[0].Material=="rha"&&selection.Layers[1].Material=="air"&&Math.Abs(selection.Layers[1].ThicknessMm-10)<1e-4&&Math.Abs(selection.Layers[2].ThicknessMm-40)<1e-4,"wave2: from selection material, native normal thickness and actual geometry gap");
        Reject(()=>composer.FromSelection("s","S","1980.01.01",new[]{Slab("rha",0,.02f),Slab("rha",-.01f,.02f)},Vector3.UnitZ,Vector3.UnitY,Vector3.UnitX),"overlapping selection");
        var fallback=CassetteMissingPreset.Resolve("ebCassette_user_missing_v1",false,materials["ebCassette_nera"]);
        check(fallback is {Exact:true}&&fallback.Passive.RhaFactor==materials["ebCassette_nera"].RhaFactor,"wave2: known missing Technology keeps exact native RHA equivalent");
        check(CassetteMissingPreset.Resolve("ebCassette_user_missing_v1",false,null) is {Exact:false,Passive.RhaFactor:1},"wave2: unrecoverable shared composition explicitly reports approximate RHA");
        check(CassetteMissingPreset.Resolve("ebCassette_user_bad_v1",false,new("bad",double.NaN,1,0,2)) is {Exact:false,Passive.Density:7850},"wave2: malformed missing Technology cannot inject invalid native passive values");
        check(CassetteMissingPreset.Resolve("ebCassette_user_present_v1",true,null)==null&&CassetteMissingPreset.Resolve("rha",false,null)==null,"wave2: present/native identities untouched by missing-user guard");
        File.WriteAllText(sidecar,CassetteComposer.Serialize(new[]{save.Preset}));composer.Delete(save.Preset.Id);
        check(CassetteCatalogue.Parse(File.ReadAllText(sidecar)).Presets.Length==0,"wave2: deleting the final unused user preset leaves a valid empty catalogue");
        // Rebuild benchmark excludes native interop/material upload and is not a live frame claim.
        var watch=Stopwatch.StartNew();for(int i=0;i<2000;i++)_=CassetteEdgeMesh.Build(bent,bendFaces,true);watch.Stop();
        Console.WriteLine($"WAVE2 geometry benchmark: {watch.Elapsed.TotalMilliseconds/2000:0.0000} ms/bent plate, pure managed build (native upload unverified).");
        Console.WriteLine("WAVE2 composite prefab, identity/menu, thickness planner, edge meshes/raster and composer API PASS.");
        void Reject(Action action,string label)
        {bool rejected=false;try{action();}catch(Exception ex) when(ex is FormatException or ArgumentException or InvalidOperationException or IOException){rejected=true;}check(rejected,"wave2 rejects "+label);}
    }
    private static CassetteSelectionPlate Slab(string material,float z,float thickness)
    {
        var face=new[]{new Vector3(0,0,z),new Vector3(1,0,z),new Vector3(1,1,z),new Vector3(0,1,z)};
        return new(material,new[]{face},face.Concat(face.Select(v=>v-Vector3.UnitZ*thickness)).ToArray());
    }
}
