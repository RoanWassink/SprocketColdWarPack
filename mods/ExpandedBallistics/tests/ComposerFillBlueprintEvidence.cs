using System.IO.Compression;
using System.Numerics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using SprocketUiKit;
namespace SprocketExpandedBallistics;

/// <summary>
/// 0.22.53 managed evidence for the Fill to wall button on the real T-80BVM (proporties) turret plate.
/// Reads the read-only blueprint backup, runs the production ComposerFill planner against the raw
/// turret structure triangles, checks an independent ray test, saves the payload round-trip and draws
/// before/after PNGs with the UI Kit CPU rasteriser. No Unity, native call or installer action.
/// </summary>
internal static partial class ComposerFillBlueprintEvidence
{
    internal static void Run(string output,Action<bool,string> check)
    {
        Directory.CreateDirectory(output);
        var srcRoot=Fix02228Tests.SourceRoot();
        var backup=Path.Combine(srcRoot,"tests","fixtures","blueprint-backup");
        var file=Directory.GetFiles(backup,"*.blueprint").OrderBy(x=>x).FirstOrDefault()
            ??throw new InvalidOperationException("No blueprint backup found in "+backup);
        using var doc=JsonDocument.Parse(File.ReadAllText(file,Encoding.UTF8));
        var objects=doc.RootElement.GetProperty("objects").EnumerateArray().ToDictionary(x=>x.GetProperty("vuid").GetInt32());
        var meshes=doc.RootElement.GetProperty("meshes").EnumerateArray().ToDictionary(x=>x.GetProperty("vuid").GetInt32());
        var blueprints=doc.RootElement.GetProperty("blueprints").EnumerateArray().ToDictionary(x=>x.GetProperty("id").GetInt32());
        int MeshOf(int blueprintId)=>blueprints[blueprintId].GetProperty("blueprint").GetProperty("bodyMeshVuid").GetInt32();
        // The turret plate is the structure that carries the T-80U cassette and is named Fill/To back plane.
        var plateStructure=blueprints.Values.Where(b=>b.GetProperty("type").GetString()=="structure")
            .Select(b=>(Id:b.GetProperty("id").GetInt32(),Bp:b.GetProperty("blueprint")))
            .Where(x=>x.Bp.TryGetProperty("armourTechID",out var a)&&a.GetString()=="ebCassette_t80u")
            .Select(x=>(x.Id,Mesh:MeshOf(x.Id)))
            .FirstOrDefault(x=>MeshName(meshes[x.Mesh]).Contains("Composite armour plate",StringComparison.Ordinal));
        check(plateStructure.Mesh!=0,"blueprint has a turret composite plate");
        var plateObject=objects.Values.Where(o=>o.TryGetProperty("structureBlueprintVuid",out var s)&&s.GetInt32()==plateStructure.Id).DefaultIfEmpty().First();
        if(plateObject.ValueKind==JsonValueKind.Undefined)throw new InvalidOperationException("No object uses the turret plate structure.");
        check(MeshName(meshes[plateStructure.Mesh]).Contains("[To back plane]",StringComparison.Ordinal),"selected plate is the T-80BVM To back plane turret plate");
        var turret=Parent(objects,plateObject);
        var structureId=turret.TryGetProperty("structureID",out var sid)?sid.GetInt32():-1;
        if(!objects.TryGetValue(structureId,out var hostObject))throw new InvalidOperationException("Turret has no structureID host.");
        var hostMeshId=MeshOf(hostObject.GetProperty("structureBlueprintVuid").GetInt32());
        // Flip on every real mesh of the blueprint: each corner keeps (vertex, t, tm byte, te slot); two flips are bit-exact.
        int realMeshes=0,realFaces=0,realCorners=0;
        foreach(var m in meshes.Values)
        {
            if(!m.GetProperty("meshData").TryGetProperty("mesh",out var real)||!real.TryGetProperty("faces",out _))continue;
            var rawMesh=real.GetRawText();var snap=ComposerFill.Read(rawMesh);
            var once=ComposerFill.Plan(rawMesh,snap,null,ComposerMeshAction.Flip,Vector3.Zero,Array.Empty<FillTriangle>(),new FillSettings());
            var twice=ComposerFill.Plan(once.Json,once.After,null,ComposerMeshAction.Flip,Vector3.Zero,Array.Empty<FillTriangle>(),new FillSettings());
            check(twice.Json==rawMesh,"real mesh double flip is bit-exact: mesh "+m.GetProperty("vuid").GetInt32());
            using var a=JsonDocument.Parse(rawMesh);using var b=JsonDocument.Parse(once.Json);
            var fa=a.RootElement.GetProperty("faces").EnumerateArray().ToArray();var fb=b.RootElement.GetProperty("faces").EnumerateArray().ToArray();
            bool same=fa.Length==fb.Length;
            for(int i=0;same&&i<fa.Length;i++)
            {
                var ca=Corners(fa[i]);var cb=Corners(fb[i]);
                same=ca.Length==cb.Length&&ca.Reverse().SequenceEqual(cb);realCorners+=ca.Length;
            }
            check(same,"real mesh flip keeps every corner's vertex, thickness, tm and te together: mesh "+m.GetProperty("vuid").GetInt32());
            check(once.After.Faces.Zip(snap.Faces).All(p=>p.Second.Normal==Vector3.Zero?p.First.Normal==Vector3.Zero:Vector3.Dot(p.First.Normal,p.Second.Normal)<-.999f),"real mesh flip reverses every face normal: mesh "+m.GetProperty("vuid").GetInt32());
            realMeshes++;realFaces+=fa.Length;
        }
        check(realMeshes>=20&&realFaces>800,"flip covered the blueprint's real meshes: "+realMeshes+" meshes, "+realFaces+" faces, "+realCorners+" corners");
        RealFlip=(realMeshes,realFaces,realCorners);
        var plateMesh=meshes[plateStructure.Mesh].GetProperty("meshData").GetProperty("mesh");
        var hostMesh=meshes[hostMeshId].GetProperty("meshData").GetProperty("mesh");
        var plateJson=plateMesh.GetRawText();
        var before=ComposerFill.Read(plateJson);
        check(before.FaceCount==16&&before.Faces.All(f=>f.VertexCount>=3),"turret plate payload reads as 16 faces");
        var (plateR,plateT)=World(objects,plateObject);
        var (hostR,hostT)=World(objects,hostObject);
        var relR=plateR.Transpose()*hostR;var relT=plateR.Transpose().Mul(hostT-plateT);
        var walls=Triangles(hostMesh,relR,relT);
        check(walls.Count>50,"turret structure supplies the wall triangles");
        var reference=relT;
        var plan=ComposerFill.Plan(plateJson,before,null,ComposerMeshAction.Fill,reference,walls,new FillSettings());
        check(plan.After.FaceCount==before.FaceCount&&plan.After.Vertices.SequenceEqual(before.Vertices),"Fill to wall keeps the authored surface");
        check(plan.After.Faces.All(f=>f.Thickness.All(t=>t>=3&&t<=500)),"every filled corner is a valid native thickness");
        check(plan.Flipped==0,"the T-80BVM plate already thickens toward the turret interior");
        // Every corner must match an independent point-in-triangle ray, not the production ray helper.
        int compared=0;
        for(int f=0;f<plan.After.FaceCount;f++)
        {
            var face=plan.After.Faces[f];
            for(int j=0;j<face.VertexCount;j++)
            {
                var hit=IndependentRay(face.Corners[j],-face.Normal,walls);
                ushort expected=hit.HasValue?(ushort)Math.Clamp(Math.Round(hit.Value*1000-10,MidpointRounding.AwayFromZero),3,500):(ushort)50;
                check(face.Thickness[j]==expected,"independent ray agrees on face "+(f+1)+" corner "+(j+1)+": hit="+(hit?.ToString("R")??"miss")+" expected="+expected+" stored="+face.Thickness[j]);
                compared++;
            }
        }
        check(compared==plan.After.Faces.Sum(f=>f.VertexCount),"all corners checked against the independent ray");
        check(plan.Summary.Contains("without a wall")&&plan.Summary.Contains("clearance 10 mm")&&plan.Summary.Contains("capped at 500 mm"),"fill message reports clearance, reserve and cap");
        // Save/load payload: the name carries the mode and the native mesh JSON carries every corner value.
        var name=CompositePlate.NameForMode(ThicknessMode.FillToWall);
        var wrapper=JsonSerializer.Serialize(new{name,mesh=JsonDocument.Parse(plan.Json).RootElement});
        using var loaded=JsonDocument.Parse(wrapper);
        var loadedName=loaded.RootElement.GetProperty("name").GetString()!;
        var loadedMesh=loaded.RootElement.GetProperty("mesh").GetRawText();
        check(CompositePlate.ModeFromName(loadedName)==ThicknessMode.FillToWall&&CompositePlate.IsName(loadedName),"Fill to wall name survives save/load");
        var reread=ComposerFill.Read(loadedMesh);
        check(reread.Faces.Select(f=>f.Thickness).SelectMany(x=>x).SequenceEqual(plan.After.Faces.Select(f=>f.Thickness).SelectMany(x=>x)),"corner thickness survives the payload round-trip");
        check(ThickenModeJson.Verify(loadedMesh,plan.After.Faces.Select(f=>f.Along).ToArray())==null,"saved payload stores the inward direction");
        var restored=ComposerFill.Read(plateJson);
        check(restored.Faces.Select(f=>f.Thickness).SelectMany(x=>x).SequenceEqual(before.Faces.Select(f=>f.Thickness).SelectMany(x=>x)),"one revert snapshot restores every corner");
        // Preset: hand calculation and a simulated crew bound.
        var box=Feat02253ComposerFillTests.Box(new(-1.3f,0,-1),new(1.3f,.8f,1.4f));
        var cheeks=ComposerFill.Cheeks(box,new("turret",new(-1.3f,0,-1),new(1.3f,.8f,1.4f)),new FillSettings());
        double handVolume=cheeks.After.Faces.Sum(HandVolume);double density=RuntimeDensity;
        var stats=cheeks.Inspect(density,Array.Empty<FillBounds>());
        check(Math.Abs(stats.VolumeM3-handVolume)<1e-9,"preset volume matches the hand integral");
        check(Math.Abs(stats.MassKg-handVolume*density)<1e-6,"preset mass matches volume x density");
        check(cheeks.After.FaceCount==320&&cheeks.After.Vertices.All(p=>Math.Abs(p.X)>=.35f-1e-6&&p.Y>=.03f-1e-6&&p.Z>1),"preset stays in the front cheeks and out of the gun corridor");
        var crew=FillBounds.Of("simulated crew",new[]{new Vector3(.2f,.2f,1.35f),new Vector3(.8f,.6f,1.45f)});
        check(cheeks.Inspect(density,new[]{crew}).Warnings.Any(w=>w.Contains("simulated crew")),"simulated crew overlap is warned about");
        check(cheeks.Inspect(density,new[]{new FillBounds("far cannon",new(5,5,5),new(6,6,6))}).Warnings.Length==0,"far component does not warn");
        // The same preset on the actual turret structure bounds.
        var hostLocal=Triangles(hostMesh,Mat3.Identity,Vector3.Zero);
        var hostBounds=FillBounds.Of("turret",hostLocal.SelectMany(t=>new[]{t.A,t.B,t.C}));
        var realCheeks=ComposerFill.Cheeks(hostLocal,hostBounds,new FillSettings());
        var realMapped=ComposerFill.Read(MapJson(realCheeks.Json,relR,relT));
        var realStats=(realCheeks with{After=realMapped}).Inspect(density,Array.Empty<FillBounds>());
        check(realCheeks.After.FaceCount is >=12 and <=2000&&realStats.VolumeM3>0&&realStats.MassKg>0,"preset builds bounded cheeks on the real T-80BVM turret: faces="+realCheeks.After.FaceCount+" volume="+realStats.VolumeM3.ToString("R")+" mass="+realStats.MassKg.ToString("R"));
        check(realCheeks.After.Vertices.All(p=>Math.Abs(p.X)>=.35f-1e-6&&p.Y>=hostBounds.Min.Y+.03f-1e-6),"real preset keeps the gun corridor clear");
        check(ComposerFill.IsObstacleType("CrewComponent")&&ComposerFill.IsObstacleType("Cannon")&&ComposerFill.IsObstacleType("AmmoRack")&&ComposerFill.IsObstacleType("Carousel")&&!ComposerFill.IsObstacleType("HullStructure"),"overlap filter covers crew, gun, ammunition and carousel only");
        // Managed before/after render of the actual plate mesh with the same camera.
        var preview=Path.Combine(output,"preview");Directory.CreateDirectory(preview);
        var beforeFile=Path.Combine(preview,"turret-before.png");
        var afterFile=Path.Combine(preview,"turret-fill-after.png");
        var cheekFile=Path.Combine(preview,"turret-cheek-preset.png");
        var solids=Solids(before);
        var afterSolids=Solids(plan.After);
        var cheekSolids=Solids(realMapped);
        // Context: the turret structure's authored surface as a faint wireframe in the same (plate) frame.
        var hostPoints=walls.SelectMany(t=>new[]{t.A,t.B,t.C}).ToArray();
        var bounds=Union(Union(Bounds(solids),Bounds(afterSolids)),(hostPoints.Aggregate(Vector3.Min),hostPoints.Aggregate(Vector3.Max)));
        var camera=new Camera(bounds);
        var beforePixels=Render(solids,camera,walls);
        var afterPixels=Render(afterSolids,camera,walls);
        var cheekPixels=Render(cheekSolids,camera,walls);
        var blueprintStats=plan.Inspect(RuntimeDensity,Array.Empty<FillBounds>());
        double beforeVolume=before.Faces.Sum(ComposerFill.Volume);
        File.WriteAllBytes(beforeFile,beforePixels);
        File.WriteAllBytes(afterFile,afterPixels);
        File.WriteAllBytes(cheekFile,cheekPixels);
        check(File.Exists(beforeFile)&&new FileInfo(beforeFile).Length>20000,"before PNG written");
        check(File.Exists(afterFile)&&new FileInfo(afterFile).Length>20000,"after PNG written");
        check(PixelHash(beforePixels)!=PixelHash(afterPixels),"before and after renders differ");
        // A blueprint has no renderer objects: show an explicitly labelled gun envelope proxy.
        // Live corridor sizing uses actual Renderer.bounds; the proxy is only managed preview context.
        var cannonObject=objects.Values.First(o=>o.TryGetProperty("cannonBlueprintVuid",out _));
        var cannonBlueprint=blueprints[cannonObject.GetProperty("cannonBlueprintVuid").GetInt32()].GetProperty("blueprint");
        float caliber=cannonBlueprint.GetProperty("caliber").GetSingle()*.001f;
        float wallThickness=cannonBlueprint.GetProperty("segments").EnumerateArray().SelectMany(g=>g.GetProperty("segments").EnumerateArray()).Max(g=>Math.Max(g.GetProperty("t0").GetSingle(),g.GetProperty("t1").GetSingle()))*.001f;
        float radius=caliber*.5f+wallThickness;
        var (gunR,gunT)=World(objects,cannonObject);var gunHostR=hostR.Transpose()*gunR;var gunHostT=hostR.Transpose().Mul(gunT-hostT);
        var gunPoints=new List<Vector3>();
        foreach(float x in new[]{-radius,radius})foreach(float y in new[]{-radius,radius})foreach(float z in new[]{-.45f,2.1f})gunPoints.Add(gunHostR.Mul(new(x,y,z))+gunHostT);
        var gunBound=FillBounds.Of("gun",gunPoints);float halfGap=ComposerFill.GunHalfGap(new[]{gunBound});float gunCentre=(gunBound.Min.X+gunBound.Max.X)*.5f;
        var fitted=ComposerFill.Cheeks(hostLocal,hostBounds,new(),new(),halfGap,false,gunCentre);
        check(fitted.After.Vertices.All(v=>v.X<=gunBound.Min.X-.05f+1e-6||v.X>=gunBound.Max.X+.05f-1e-6),"real turret cheek grid respects gun envelope plus 50 mm each side (blueprint proxy, live renderers pending)");
        check(fitted.After.Faces.All(f=>f.Thickness.All(t=>t<=450)),"real cheek maximum is 450 mm");
        foreach(var face in fitted.After.Faces)foreach(var corner in face.Corners)
        {
            var hit=ComposerFill.RaySurface(new(corner.X,corner.Y,.35f),hostLocal);
            check(hit.HasValue&&Math.Abs(hit.Value.Normal.Z)>=.5f-1e-6,"real cheek sample never reaches a side wall");
        }
        // Independent Heron areas rather than the planner's cross-product integral.
        double hand=0;
        foreach(var f in fitted.After.Faces)
        {double aa=Vector3.Distance(f.Corners[0],f.Corners[1]),bb=Vector3.Distance(f.Corners[1],f.Corners[2]),cc=Vector3.Distance(f.Corners[2],f.Corners[0]),semi=(aa+bb+cc)/2;hand+=Math.Sqrt(Math.Max(0,semi*(semi-aa)*(semi-bb)*(semi-cc)))*f.Thickness.Average(t=>(double)t)*.001;}
        var fittedStats=fitted.Inspect(density,Array.Empty<FillBounds>());
        check(Math.Abs(hand-fittedStats.VolumeM3)<1e-6&&Math.Abs(hand*density-fittedStats.MassKg)<.01,"real cheek volume and mass agree with independent Heron hand calculation");
        var gunJson=JsonSerializer.Serialize(new{vertices=gunPoints.SelectMany(v=>new[]{v.X,v.Y,v.Z}).ToArray(),faces=new[]{new{v=new[]{0,1,3,2},t=new[]{3,3,3,3},tm=0,te=0},new{v=new[]{4,6,7,5},t=new[]{3,3,3,3},tm=0,te=0},new{v=new[]{0,4,5,1},t=new[]{3,3,3,3},tm=0,te=0},new{v=new[]{2,3,7,6},t=new[]{3,3,3,3},tm=0,te=0},new{v=new[]{1,5,7,3},t=new[]{3,3,3,3},tm=0,te=0},new{v=new[]{0,2,6,4},t=new[]{3,3,3,3},tm=0,te=0}}});
        var gunSolids=Solids(ComposerFill.Read(gunJson)).Select(t=>t with{Colour=Raster.Pack(212,152,52,255)}).ToList();
        var beforeHost=Solids(ComposerFill.Read(MapJson(plateJson,relR.Transpose(),-relR.Transpose().Mul(relT))));
        var viewBounds=(hostBounds.Min,hostBounds.Max+new Vector3(0,0,.5f));
        foreach(var (view,direction) in new[]{("front",new Vector3(0,0,1)),("top",new Vector3(0,1,0))})
        {
            var cam=new Camera(viewBounds,direction);
            File.WriteAllBytes(Path.Combine(preview,"preset-before-"+view+".png"),Render(beforeHost.Concat(gunSolids).ToList(),cam,hostLocal));
            File.WriteAllBytes(Path.Combine(preview,"preset-after-"+view+".png"),Render(Solids(fitted.After).Concat(gunSolids).ToList(),cam,hostLocal));
        }
        var forwardJson=JsonSerializer.Serialize(new{vertices=new[]{.55f,.16f,.55f,.55f,.26f,.55f,.70f,.26f,.55f,.70f,.16f,.55f},faces=new[]{new{v=new[]{0,1,2,3},t=new[]{10,10,10,10},tm=0,te=0}}});
        var forwardPlate=ComposerFill.Read(forwardJson);var forwardFill=ComposerFill.Plan(forwardJson,forwardPlate,null,ComposerMeshAction.Fill,Vector3.Zero,hostLocal,new());
        check(forwardFill.Flipped==0&&forwardFill.Total-forwardFill.Misses>0,"plate behind cheek grows forwards on actual turret");
        var forwardCam=new Camera(viewBounds,new(1,.65f,1.1f));
        File.WriteAllBytes(Path.Combine(preview,"fill-forward-before.png"),Render(Solids(forwardPlate).Concat(gunSolids).ToList(),forwardCam,hostLocal));
        File.WriteAllBytes(Path.Combine(preview,"fill-forward-after.png"),Render(Solids(forwardFill.After).Concat(gunSolids).ToList(),forwardCam,hostLocal));
        File.WriteAllText(Path.Combine(output,"CHEEK-02254-EVIDENCE.json"),JsonSerializer.Serialize(new{rendererBoundsSource="blueprint barrel envelope proxy; actual runtime renderer bounds require in-game check",gunMinX=gunBound.Min.X,gunMaxX=gunBound.Max.X,openingMinX=gunBound.Min.X-.05f,openingMaxX=gunBound.Max.X+.05f,maximumMm=450,faces=fitted.After.FaceCount,volumeM3=fittedStats.VolumeM3,massKg=fittedStats.MassKg,handVolumeM3=hand,handMassKg=hand*density,forwardHits=forwardFill.Total-forwardFill.Misses,forwardMisses=forwardFill.Misses,forwardCaps=forwardFill.Limited},new JsonSerializerOptions{WriteIndented=true}));
        var report=new
        {
            blueprint=file,
            plateMesh=plateStructure.Mesh,
            hostMesh=hostMeshId,
            plateTransform=new[]{plateT.X,plateT.Y,plateT.Z},
            hostOriginPlateLocal=new[]{reference.X,reference.Y,reference.Z},
            faces=before.FaceCount,
            corners=compared,
            flipped=plan.Flipped,
            misses=plan.Misses,
            limited=plan.Limited,
            tightCorners=plan.Tight,
            realFlipMeshes=realMeshes,realFlipFaces=realFaces,realFlipCorners=realCorners,
            clearanceMm=10,
            maximumMm=500,
            reserveMm=50,
            beforeVolumeM3=beforeVolume,
            afterVolumeM3=blueprintStats.VolumeM3,
            afterMassKg=blueprintStats.MassKg,
            fixturePresetVolumeM3=stats.VolumeM3,
            fixturePresetMassKg=stats.MassKg,
            presetFaces=realCheeks.After.FaceCount,
            presetVolumeM3=realStats.VolumeM3,
            presetMassKg=realStats.MassKg,
            presetMisses=realCheeks.Misses,
            checks=compared+20,
            nativeRay=false,
            nativeSaveLoad=false,
            nativeUndoTested=false,
            pngs=new[]{beforeFile,afterFile,cheekFile},
            pngSha256=new[]{SHA256.HashData(beforePixels),SHA256.HashData(afterPixels),SHA256.HashData(cheekPixels)}.Select(Convert.ToHexString).ToArray()
        };
        File.WriteAllText(Path.Combine(output,"BLUEPRINT-PREVIEW.json"),JsonSerializer.Serialize(report,new JsonSerializerOptions{WriteIndented=true}));
    }
    internal const double RuntimeDensity=3550.8555233640577;
    internal static (int Meshes,int Faces,int Corners) RealFlip;
    private static (int V,int T,ulong Tm,ulong Te)[] Corners(JsonElement face)
    {
        var v=face.GetProperty("v").EnumerateArray().Select(x=>x.GetInt32()).ToArray();var t=face.GetProperty("t").EnumerateArray().Select(x=>x.GetInt32()).ToArray();
        ulong tm=face.TryGetProperty("tm",out var m)?m.GetUInt64():0,te=face.TryGetProperty("te",out var e)?e.GetUInt64():0;
        return v.Select((id,j)=>(id,t[j],(tm>>(8*j))&0xff,(te>>(16*j))&0xffff)).ToArray();
    }
    private static string MapJson(string json,Mat3 r,Vector3 t)
    {
        var node=JsonNode.Parse(json)!;var v=node["vertices"]!.AsArray();
        for(int i=0;i<v.Count;i+=3)
        {
            var p=r.Mul(new(v[i]!.GetValue<float>(),v[i+1]!.GetValue<float>(),v[i+2]!.GetValue<float>()))+t;
            v[i]=p.X;v[i+1]=p.Y;v[i+2]=p.Z;
        }
        return node.ToJsonString();
    }
    private static string MeshName(JsonElement mesh)=>mesh.GetProperty("meshData").TryGetProperty("name",out var n)&&n.ValueKind==JsonValueKind.String?n.GetString()??"":"";
    private static JsonElement Parent(Dictionary<int,JsonElement> objects,JsonElement o)
    {
        var pv=o.GetProperty("pvuid").GetInt32();if(!objects.TryGetValue(pv,out var p))throw new InvalidOperationException("No parent object.");
        return p;
    }
    private readonly struct Mat3
    {
        internal readonly float A,B,C,D,E,F,G,H,I;
        internal Mat3(float a,float b,float c,float d,float e,float f,float g,float h,float i){A=a;B=b;C=c;D=d;E=e;F=f;G=g;H=h;I=i;}
        internal static readonly Mat3 Identity=new(1,0,0,0,1,0,0,0,1);
        internal static Mat3 FromEulerDegrees(float x,float y,float z)
        {
            float rx=x*MathF.PI/180,ry=y*MathF.PI/180,rz=z*MathF.PI/180;
            float cx=MathF.Cos(rx),sx=MathF.Sin(rx),cy=MathF.Cos(ry),sy=MathF.Sin(ry),cz=MathF.Cos(rz),sz=MathF.Sin(rz);
            var mx=new Mat3(1,0,0,0,cx,-sx,0,sx,cx);
            var my=new Mat3(cy,0,sy,0,1,0,-sy,0,cy);
            var mz=new Mat3(cz,-sz,0,sz,cz,0,0,0,1);
            return my*mx*mz;
        }
        internal Vector3 Mul(Vector3 v)=>new(A*v.X+B*v.Y+C*v.Z,D*v.X+E*v.Y+F*v.Z,G*v.X+H*v.Y+I*v.Z);
        internal Mat3 Transpose()=>new(A,D,G,B,E,H,C,F,I);
        public static Mat3 operator*(Mat3 a,Mat3 b)=>new(
            a.A*b.A+a.B*b.D+a.C*b.G,a.A*b.B+a.B*b.E+a.C*b.H,a.A*b.C+a.B*b.F+a.C*b.I,
            a.D*b.A+a.E*b.D+a.F*b.G,a.D*b.B+a.E*b.E+a.F*b.H,a.D*b.C+a.E*b.F+a.F*b.I,
            a.G*b.A+a.H*b.D+a.I*b.G,a.G*b.B+a.H*b.E+a.I*b.H,a.G*b.C+a.H*b.F+a.I*b.I);
    }
    private static (Mat3 R,Vector3 T) World(Dictionary<int,JsonElement> objects,JsonElement o)
    {
        var ts=o.GetProperty("transform");var rot=ts.GetProperty("rot");var pos=ts.GetProperty("pos");var scale=ts.GetProperty("scale");
        var r=Mat3.FromEulerDegrees(rot[0].GetSingle(),rot[1].GetSingle(),rot[2].GetSingle());
        var p=new Vector3(pos[0].GetSingle(),pos[1].GetSingle(),pos[2].GetSingle());
        var parent=o.GetProperty("pvuid").GetInt32();
        if(parent<0||!objects.TryGetValue(parent,out var po))return (r,p);
        var (pr,pt)=World(objects,po);
        return (pr*r,pr.Mul(p)+pt);
    }
    private static List<FillTriangle> Triangles(JsonElement mesh,Mat3 r,Vector3 t)
    {
        var verts=mesh.GetProperty("vertices").EnumerateArray().Select(x=>x.GetSingle()).ToArray();
        Vector3 V(int i)=>r.Mul(new(verts[3*i],verts[3*i+1],verts[3*i+2]))+t;
        var list=new List<FillTriangle>();
        foreach(var f in mesh.GetProperty("faces").EnumerateArray())
        {
            var ids=f.GetProperty("v").EnumerateArray().Select(x=>x.GetInt32()).ToArray();
            for(int k=1;k+1<ids.Length;k++)list.Add(new(V(ids[0]),V(ids[k]),V(ids[k+1])));
        }
        return list;
    }
    private static double? IndependentRay(Vector3 origin,Vector3 direction,IReadOnlyList<FillTriangle> walls)
    {
        var d=Vector3.Normalize(direction);double best=double.PositiveInfinity;
        foreach(var t in walls)
        {
            var e1=t.B-t.A;var e2=t.C-t.A;var p=Vector3.Cross(d,e2);double det=Vector3.Dot(e1,p);
            if(Math.Abs(det)<1e-10)continue;var s=origin-t.A;double u=Vector3.Dot(s,p)/det;
            if(u< -1e-6||u>1+1e-6)continue;var q=Vector3.Cross(s,e1);double v=Vector3.Dot(d,q)/det;
            if(v< -1e-6||u+v>1+1e-6)continue;double distance=Vector3.Dot(e2,q)/det;
            if(distance>1e-5&&distance<=100+1e-6&&distance<best)best=distance;
        }
        return double.IsPositiveInfinity(best)?null:best;
    }
    private static double HandVolume(SnapFace f)
    {
        double v=0;for(int j=1;j+1<f.VertexCount;j++)
            v+=Vector3.Cross(f.Corners[j]-f.Corners[0],f.Corners[j+1]-f.Corners[0]).Length()*.5*(f.Thickness[0]+f.Thickness[j]+f.Thickness[j+1])/3000d;
        return v;
    }
    private readonly record struct SolidTriangle(Vector3 A,Vector3 B,Vector3 C,Vector3 Normal,uint Colour);
    private static List<SolidTriangle> Solids(PlateMeshSnapshot snapshot,Vector3? recentre=null)
    {
        var result=new List<SolidTriangle>();var shift=recentre??Vector3.Zero;
        foreach(var f in snapshot.Faces)
        {
            var normal=Vector3.Normalize(f.Normal);
            var front=new Vector3[f.VertexCount];var back=new Vector3[f.VertexCount];
            for(int j=0;j<f.VertexCount;j++){front[j]=f.Corners[j]-shift;back[j]=front[j]-normal*f.Thickness[j]*.001f;}
            uint frontColour=Shade(normal,0xb8,0xc4,0x6a);uint backColour=Shade(-normal,0x6c,0x72,0x70);
            for(int j=1;j+1<f.VertexCount;j++)
            {
                result.Add(new(front[0],front[j],front[j+1],normal,frontColour));
                result.Add(new(back[0],back[j+1],back[j],-normal,backColour));
            }
            for(int j=0;j<f.VertexCount;j++)
            {
                int k=(j+1)%f.VertexCount;
                var side=Vector3.Cross(front[k]-front[j],back[j]-front[j]);
                if(side.LengthSquared()<1e-16)continue;
                var sn=Vector3.Normalize(side);uint sc=Shade(sn,0x86,0x8c,0x82);
                result.Add(new(front[j],front[k],back[k],sn,sc));
                result.Add(new(front[j],back[k],back[j],sn,sc));
            }
        }
        return result;
    }
    private static uint Shade(Vector3 n,byte r,byte g,byte b)
    {
        float l=.55f+.45f*MathF.Abs(Vector3.Dot(Vector3.Normalize(n),Vector3.Normalize(new(.35f,.75f,.55f))));
        return Raster.Pack((byte)Math.Clamp(r*l,0,255),(byte)Math.Clamp(g*l,0,255),(byte)Math.Clamp(b*l,0,255),245);
    }
    private static (Vector3 Min,Vector3 Max) Bounds(IReadOnlyList<SolidTriangle> triangles)
    {
        var p=triangles.SelectMany(t=>new[]{t.A,t.B,t.C}).ToArray();
        return (p.Aggregate(Vector3.Min),p.Aggregate(Vector3.Max));
    }
    private static (Vector3 Min,Vector3 Max) Union((Vector3 Min,Vector3 Max) a,(Vector3 Min,Vector3 Max) b)=>(Vector3.Min(a.Min,b.Min),Vector3.Max(a.Max,b.Max));
    private sealed class Camera
    {
        internal readonly Vector3 Centre,Right,Up,Forward;internal readonly float Scale;
        internal Camera((Vector3 Min,Vector3 Max) b,Vector3? view=null)
        {
            Centre=(b.Min+b.Max)*.5f;var extent=MathF.Max(.05f,(b.Max-b.Min).Length());
            var eye=Centre+Vector3.Normalize(view??new(1.05f,.72f,1.35f))*extent*3;
            Forward=Vector3.Normalize(Centre-eye);
            Right=Vector3.Normalize(Vector3.Cross(Math.Abs(Forward.Y)>.99f?new(0,0,1):new(0,1,0),Forward));
            Up=Vector3.Normalize(Vector3.Cross(Forward,Right));
            Scale=1000f/MathF.Max(.05f,Math.Max(b.Max.X-b.Min.X,Math.Max(b.Max.Y-b.Min.Y,b.Max.Z-b.Min.Z)));
        }
        internal (float X,float Y,float Depth) Project(Vector3 p)
        {
            var q=p-Centre;return (600+Vector3.Dot(q,Right)*Scale*.72f,450-Vector3.Dot(q,Up)*Scale*.72f,Vector3.Dot(q,Forward));
        }
    }
    private static byte[] Render(List<SolidTriangle> triangles,Camera camera,IReadOnlyList<FillTriangle>? context=null)
    {
        const int w=1200,h=900;var raster=new Raster();raster.Resize(w,h);raster.Clear(Raster.Pack(20,21,15,255));
        for(int y=0;y<h;y+=45)for(int x=0;x<w;x+=45)raster.Blend(y*w+x,Raster.Pack(36,38,32,255),.35f);
        if(context!=null)foreach(var c in context)
        {
            var a=camera.Project(c.A);var b=camera.Project(c.B);var d=camera.Project(c.C);var line=Raster.Pack(110,150,190,120);
            raster.Segment(a.X,a.Y,b.X,b.Y,1,line);raster.Segment(b.X,b.Y,d.X,d.Y,1,line);raster.Segment(d.X,d.Y,a.X,a.Y,1,line);
        }
        var xy=new float[6];
        foreach(var t in triangles.OrderByDescending(t=>MathF.Max(camera.Project(t.A).Depth,MathF.Max(camera.Project(t.B).Depth,camera.Project(t.C).Depth))))
        {
            var a=camera.Project(t.A);var b=camera.Project(t.B);var c=camera.Project(t.C);
            xy[0]=a.X;xy[1]=a.Y;xy[2]=b.X;xy[3]=b.Y;xy[4]=c.X;xy[5]=c.Y;
            raster.FillPolygon(xy,t.Colour);
            raster.Segment(a.X,a.Y,b.X,b.Y,1,Raster.Pack(35,37,30,110));
            raster.Segment(b.X,b.Y,c.X,c.Y,1,Raster.Pack(35,37,30,110));
            raster.Segment(c.X,c.Y,a.X,a.Y,1,Raster.Pack(35,37,30,110));
        }
        // A 0.5 m scale bar, projected on the camera right axis.
        var p0=camera.Project(camera.Centre-camera.Right*.25f-camera.Up*1.05f);
        var p1=camera.Project(camera.Centre+camera.Right*.25f-camera.Up*1.05f);
        raster.Segment(p0.X,p0.Y,p1.X,p1.Y,4,Raster.Pack(220,158,51,255));
        var pixels=new uint[w*h];Array.Copy(raster.Pixels,pixels,pixels.Length);
        return PackPng(w,h,pixels);
    }
    private static byte[] PackPng(int w,int h,uint[] pixels)
    {
        var raw=new byte[h*(1+w*4)];for(int y=0;y<h;y++){int at=y*(1+w*4);raw[at]=0;for(int x=0;x<w;x++){var p=pixels[y*w+x];int d=at+1+x*4;raw[d]=(byte)p;raw[d+1]=(byte)(p>>8);raw[d+2]=(byte)(p>>16);raw[d+3]=(byte)(p>>24);}}
        using var ms=new MemoryStream();using(var z=new ZLibStream(ms,CompressionLevel.Optimal,true))z.Write(raw);
        return Png(w,h,ms.ToArray());
    }
    private static byte[] Png(int w,int h,byte[] idat)
    {
        using var ms=new MemoryStream();ms.Write(new byte[]{137,80,78,71,13,10,26,10});
        var ihdr=new byte[13];WriteBE(ihdr,0,w);WriteBE(ihdr,4,h);ihdr[8]=8;ihdr[9]=6;Chunk(ms,"IHDR",ihdr);Chunk(ms,"IDAT",idat);Chunk(ms,"IEND",Array.Empty<byte>());return ms.ToArray();
    }
    private static void WriteBE(byte[] b,int at,int value){b[at]=(byte)(value>>24);b[at+1]=(byte)(value>>16);b[at+2]=(byte)(value>>8);b[at+3]=(byte)value;}
    private static void Chunk(Stream s,string type,byte[] data)
    {
        var len=new byte[4];WriteBE(len,0,data.Length);s.Write(len);var t=Encoding.ASCII.GetBytes(type);s.Write(t);s.Write(data);
        var crc=new Crc32();crc.Add(t);crc.Add(data);var c=new byte[4];WriteBE(c,0,(int)crc.Value);s.Write(c);
    }
    private struct Crc32
    {
        private uint value;internal uint Value=>value^0xffffffffu;
        public Crc32(){value=0xffffffffu;}
        private static readonly uint[] Table=Build();
        private static uint[] Build(){var t=new uint[256];for(uint i=0;i<256;i++){uint c=i;for(int k=0;k<8;k++)c=(c&1)!=0?0xedb88320u^(c>>1):c>>1;t[i]=c;}return t;}
        internal void Add(byte[] data){foreach(var b in data)value=Table[(value^b)&255]^(value>>8);}
    }
    private static string PixelHash(byte[] png)=>Convert.ToHexString(SHA256.HashData(png));
}
