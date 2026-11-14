using System.Numerics;
using System.Text;
using System.Text.Json;
using SprocketUiKit;
namespace SprocketExpandedBallistics;
internal static partial class ComposerFillBlueprintEvidence
{
    internal static void RunCoverage(Action<bool,string> sink)
    {
        int checks=0;void Check(bool b,string m){checks++;sink(b,"Cheek coverage: "+m);}
        var root=Fix02228Tests.SourceRoot();var output=Fix02228Tests.OutputRoot();
        Directory.CreateDirectory(output);var preview=Path.Combine(output,"preview");Directory.CreateDirectory(preview);
        var file=Directory.GetFiles(Path.Combine(root,"tests","fixtures","blueprint-backup"),"*.blueprint").Single();
        using var doc=JsonDocument.Parse(File.ReadAllText(file));
        var objects=doc.RootElement.GetProperty("objects").EnumerateArray().ToDictionary(x=>x.GetProperty("vuid").GetInt32());
        var meshes=doc.RootElement.GetProperty("meshes").EnumerateArray().ToDictionary(x=>x.GetProperty("vuid").GetInt32());
        var blueprints=doc.RootElement.GetProperty("blueprints").EnumerateArray().ToDictionary(x=>x.GetProperty("id").GetInt32());
        var plateObject=objects[1454];var plateBp=blueprints[plateObject.GetProperty("structureBlueprintVuid").GetInt32()].GetProperty("blueprint");
        Check(plateBp.GetProperty("bodyMeshVuid").GetInt32()==13&&plateBp.GetProperty("armourTechID").GetString()=="ebCassette_t80u","old block #1455 identified by object 1454 / mesh 13 / T-80U material");
        var turret=Parent(objects,plateObject);var hostObject=objects[turret.GetProperty("structureID").GetInt32()];
        var hostBp=blueprints[hostObject.GetProperty("structureBlueprintVuid").GetInt32()].GetProperty("blueprint");
        var hostMesh=meshes[hostBp.GetProperty("bodyMeshVuid").GetInt32()].GetProperty("meshData").GetProperty("mesh");
        var walls=Triangles(hostMesh,Mat3.Identity,Vector3.Zero);var bounds=FillBounds.Of("turret",walls.SelectMany(t=>new[]{t.A,t.B,t.C}));
        var (hostR,hostT)=World(objects,hostObject);var (plateR,plateT)=World(objects,plateObject);
        var old=ComposerFill.Read(MapJson(meshes[13].GetProperty("meshData").GetProperty("mesh").GetRawText(),hostR.Transpose()*plateR,hostR.Transpose().Mul(plateT-hostT)));
        var cannon=objects.Values.First(o=>o.TryGetProperty("cannonBlueprintVuid",out _));var cannonBp=blueprints[cannon.GetProperty("cannonBlueprintVuid").GetInt32()].GetProperty("blueprint");
        float caliber=cannonBp.GetProperty("caliber").GetSingle()*.001f;
        float wall=cannonBp.GetProperty("segments").EnumerateArray().SelectMany(g=>g.GetProperty("segments").EnumerateArray()).Max(g=>Math.Max(g.GetProperty("t0").GetSingle(),g.GetProperty("t1").GetSingle()))*.001f;
        float radius=caliber*.5f+wall;var (gunR,gunT)=World(objects,cannon);var gunHostR=hostR.Transpose()*gunR;var gunHostT=hostR.Transpose().Mul(gunT-hostT);
        var gunPoints=new List<Vector3>();foreach(float x in new[]{-radius,radius})foreach(float y in new[]{-radius,radius})foreach(float z in new[]{-.45f,2.1f})gunPoints.Add(gunHostR.Mul(new(x,y,z))+gunHostT);
        var gun=FillBounds.Of("gun",gunPoints);float halfGap=ComposerFill.GunHalfGap(new[]{gun}),centre=(gun.Min.X+gun.Max.X)*.5f;
        var current=ComposerFill.Cheeks(walls,bounds,new(),new(),halfGap,false,centre);
        var previous=Baseline02254Cheeks.Cheeks(walls,bounds,new(),new(),halfGap,false,centre);
        var stats=current.Inspect(RuntimeDensity,Array.Empty<FillBounds>());double hand=0;double maxDepth=0;float minRear=float.MaxValue;
        foreach(var f in current.After.Faces)
        {
            Check(f.Corners.Max(p=>p.X)-f.Corners.Min(p=>p.X)<=.100001f&&f.Corners.Max(p=>p.Y)-f.Corners.Min(p=>p.Y)<=.100001f,"grid cells stay within approximately 100 mm in both axes");
            double a=Vector3.Distance(f.Corners[0],f.Corners[1]),b=Vector3.Distance(f.Corners[1],f.Corners[2]),c=Vector3.Distance(f.Corners[2],f.Corners[0]),s=(a+b+c)/2;
            hand+=Math.Sqrt(Math.Max(0,s*(s-a)*(s-b)*(s-c)))*f.Thickness.Average(t=>(double)t)*.001;
            bool left=f.Corners[0].X<centre;
            for(int j=0;j<f.VertexCount;j++)
            {
                var p=f.Corners[j];var back=p-f.Normal*f.Thickness[j]*.001f;
                maxDepth=Math.Max(maxDepth,f.Thickness[j]);minRear=Math.Min(minRear,back.Z);
                Check(f.Thickness[j]<=450&&back.Z>=.35f-1e-6,"450 mm cap and rear boundary on every extruded corner");
                Check(left?p.X<=centre-halfGap+1e-6&&back.X<=centre-halfGap+1e-6:p.X>=centre+halfGap-1e-6&&back.X>=centre+halfGap-1e-6,"whole triangle prism stays outside gun corridor");
                var hit=ComposerFill.RaySurface(new(p.X,p.Y,.35f),walls);
                Check(hit.HasValue&&Math.Abs(hit.Value.Normal.Z)>=.5-1e-6&&Math.Abs(p.Z-(.35+hit.Value.Distance-.01))<1e-6,"accepted forward wall within 60 degrees, 10 mm clearance");
                Check(p.Y>=bounds.Min.Y+.03f-1e-6&&p.Y<=bounds.Max.Y-.03f+1e-6,"30 mm floor/roof margins");
            }
        }
        Check(Math.Abs(hand-stats.VolumeM3)<1e-6&&Math.Abs(hand*RuntimeDensity-stats.MassKg)<.01,"independent Heron area x mean thickness agrees with preview volume and mass");
        double oldArea=ProjectedArea(Solids(old),centre-halfGap,centre+halfGap),newArea=ProjectedArea(Solids(current.After),centre-halfGap,centre+halfGap),previousArea=ProjectedArea(Solids(previous.After),centre-halfGap,centre+halfGap);
        Check(newArea/oldArea>=.8,"projected union area outside identical gun corridor reaches 80% of old block: "+newArea/oldArea);
        var gunBox=Feat02253ComposerFillTests.Box(gun.Min,gun.Max).Select(t=>new SolidTriangle(t.A,t.B,t.C,Vector3.UnitZ,Raster.Pack(212,152,52,255))).ToList();
        var viewBounds=(bounds.Min,bounds.Max+new Vector3(0,0,.5f));
        foreach(var (view,direction) in new[]{("front",Vector3.UnitZ),("top",Vector3.UnitY)})
        {
            var camera=new Camera(viewBounds,direction);
            File.WriteAllBytes(Path.Combine(preview,"preset-0.22.54-"+view+".png"),Render(Solids(previous.After).Concat(gunBox).ToList(),camera,walls));
            File.WriteAllBytes(Path.Combine(preview,"preset-0.22.55-"+view+".png"),Render(Solids(current.After).Concat(gunBox).ToList(),camera,walls));
        }
        File.WriteAllText(Path.Combine(output,"cheek-0.22.55.mesh.json"),current.Json);
        File.WriteAllText(Path.Combine(output,"cheek-0.22.54.mesh.json"),previous.Json);
        // A single missing corner of this diagonal wall must leave a boundary triangle.
        var triangular=new List<FillTriangle>{new(new(-1,0,1.4f),new(1,0,1.4f),new(-1,1,1.4f))};
        var clipped=ComposerFill.Cheeks(triangular,new("diagonal",new(-1,0,-1),new(1,1,1.4f)),new());
        int expectedFaces=0,threeHit=0;
        foreach(int side in new[]{-1,1})for(int y=0;y<10;y++)for(int x=0;x<7;x++)
        {
            int hits=0;foreach(var (dx,dy) in new[]{(0,0),(1,0),(1,1),(0,1)})
            {double px=side*(.35+.65*(x+dx)/7),py=.03+.94*(y+dy)/10;if(px+2*py<=1+1e-6)hits++;}
            if(hits==4)expectedFaces+=2;else if(hits==3){expectedFaces++;threeHit++;}
        }
        Check(threeHit>0&&clipped.After.FaceCount==expectedFaces,"every analytic three-hit boundary cell retains exactly one triangle");
        var areaFixture=new List<SolidTriangle>{new(new(0,0,0),new(2,0,0),new(0,2,0),Vector3.UnitZ,0),new(new(0,0,1),new(2,0,1),new(0,2,1),Vector3.UnitZ,0)};
        Check(Math.Abs(ProjectedArea(areaFixture,.5,1)-1.375)<1e-10,"union area oracle clips corridor and counts overlapping triangles only once");
        var bevel=new List<FillTriangle>();Feat02253ComposerFillTests.AddQuad(bevel,new(-1,0,2.8f),new(1,0,.5f),new(1,1,.5f),new(-1,1,2.8f));
        var wider=ComposerFill.Cheeks(bevel,new("bevel",new(-1,0,-1),new(1,1,3)),new(),new(450,.35,150,60));
        bool rejected=false;try{ComposerFill.Cheeks(bevel,new("bevel",new(-1,0,-1),new(1,1,3)),new(),new(450,.35,150,45));}catch(InvalidOperationException){rejected=true;}
        Check(wider.After.FaceCount>0&&rejected,"config accepts a 49 degree cheek at 60 and rejects it at 45");
        foreach(double bad in new[]{-1d,86d,double.NaN,double.PositiveInfinity}){bool refused=false;try{new CheekSettings(WallAngleDegrees:bad).Validate();}catch(InvalidOperationException){refused=true;}Check(refused,"invalid wall angle refused");}
        File.WriteAllText(Path.Combine(output,"CHEEK-02255-EVIDENCE.json"),JsonSerializer.Serialize(new{passed=true,checks,blueprint=file,blueprintSha256=Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(file))),oldBlockObject=1454,oldBlockMesh=13,oldAreaM2=oldArea,previousAreaM2=previousArea,newAreaM2=newArea,coveragePercent=100*newArea/oldArea,maximumDepthMm=maxDepth,minimumRearM=minRear,volumeM3=stats.VolumeM3,massKg=stats.MassKg,handVolumeM3=hand,handMassKg=hand*RuntimeDensity,densityKgM3=RuntimeDensity,faces=current.After.FaceCount,hostMin=new[]{bounds.Min.X,bounds.Min.Y,bounds.Min.Z},hostMax=new[]{bounds.Max.X,bounds.Max.Y,bounds.Max.Z},gunMinX=centre-halfGap,gunMaxX=centre+halfGap,areaMethod="exact sweep union of projected triangle-prism surfaces, clipped outside identical gun corridor",gunSource="saved caliber + barrel wall and transforms; proxy, live Renderer.bounds pending"},new JsonSerializerOptions{WriteIndented=true}));
        Console.WriteLine($"FEAT55 PASS: {checks} checks; old={oldArea:R} m2 new={newArea:R} m2 ({newArea/oldArea:P2}).");
    }
    // Sweep all polygon vertex and segment-intersection X events. Between events the union's
    // vertical interval endpoints are linear, so the midpoint integral is exact (no raster rounding).
    private static double ProjectedArea(List<SolidTriangle> triangles,double gapMin,double gapMax)
    {
        var polygons=new List<List<(double X,double Y)>>();
        foreach(var t in triangles)
        {
            var p=new List<(double X,double Y)>{(t.A.X,t.A.Y),(t.B.X,t.B.Y),(t.C.X,t.C.Y)};
            foreach(bool left in new[]{true,false})
            {
                double boundary=left?gapMin:gapMax;var clipped=new List<(double X,double Y)>();
                for(int i=0;i<p.Count;i++){var a=p[i];var b=p[(i+1)%p.Count];bool inside=left?a.X<=boundary:a.X>=boundary,other=left?b.X<=boundary:b.X>=boundary;if(inside)clipped.Add(a);if(inside!=other){double u=(boundary-a.X)/(b.X-a.X);clipped.Add((boundary,a.Y+u*(b.Y-a.Y)));}}
                if(clipped.Count>=3)polygons.Add(clipped);
            }
        }
        var edges=polygons.SelectMany(p=>p.Select((a,i)=>(A:a,B:p[(i+1)%p.Count]))).ToArray();var xs=edges.SelectMany(e=>new[]{e.A.X,e.B.X}).ToList();
        for(int i=0;i<edges.Length;i++)for(int j=i+1;j<edges.Length;j++)
        {
            var a=edges[i];var b=edges[j];double ux=a.B.X-a.A.X,uy=a.B.Y-a.A.Y,vx=b.B.X-b.A.X,vy=b.B.Y-b.A.Y,det=ux*vy-uy*vx;
            if(Math.Abs(det)<1e-14)continue;double dx=b.A.X-a.A.X,dy=b.A.Y-a.A.Y,u=(dx*vy-dy*vx)/det,v=(dx*uy-dy*ux)/det;
            if(u>0&&u<1&&v>0&&v<1)xs.Add(a.A.X+u*ux);
        }
        var sorted=xs.Distinct().OrderBy(x=>x).ToArray();double area=0;
        for(int k=1;k<sorted.Length;k++)
        {
            double width=sorted[k]-sorted[k-1];if(width<1e-12)continue;double x=(sorted[k]+sorted[k-1])/2;var intervals=new List<(double Lo,double Hi)>();
            foreach(var p in polygons){var ys=new List<double>();for(int i=0;i<p.Count;i++){var a=p[i];var b=p[(i+1)%p.Count];if((a.X<x&&b.X>x)||(b.X<x&&a.X>x))ys.Add(a.Y+(x-a.X)*(b.Y-a.Y)/(b.X-a.X));}if(ys.Count>=2)intervals.Add((ys.Min(),ys.Max()));}
            double length=0,lo=0,hi=0;bool first=true;foreach(var interval in intervals.OrderBy(v=>v.Lo)){if(first){lo=interval.Lo;hi=interval.Hi;first=false;}else if(interval.Lo<=hi)hi=Math.Max(hi,interval.Hi);else{length+=hi-lo;lo=interval.Lo;hi=interval.Hi;}}if(!first)length+=hi-lo;area+=width*length;
        }
        return area;
    }
}
