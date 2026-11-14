using System.Numerics;
using System.Text.Json;
using System.Text;
namespace SprocketExpandedBallistics;
internal static class Fix02233Tests
{
    private static OverlaySurface Surface(Vector3[] v,int[] t,Vector3 n)=>new(v,t,Enumerable.Repeat(n,v.Length).ToArray());
    internal static void Run(Action<bool,string> check)
    {
        int count=0;var sink=check;check=(ok,text)=>{sink(ok,"fix02233: "+text);count++;};
        var root=Fix02228Tests.SourceRoot();var output=Fix02228Tests.OutputRoot();
        var evidence=new StringBuilder("case\tsideTriangles\tsideArea\tfallbackTriangles\tfallbackArea\toverlayTriangles\n");
        var f=Surface(new[]{new Vector3(1,0,0),new Vector3(3,0,0),new Vector3(3,1,0),new Vector3(1,1,0)},new[]{0,1,2,0,2,3},Vector3.UnitZ);
        var b=Surface(f.Vertices.Select(v=>v+new Vector3(.17f,0,-.2f)).ToArray(),new[]{0,2,1,0,3,2},-Vector3.UnitZ);
        // Subdivided, non-coplanar and disconnected native side triangles: the old quad containment gives zero area.
        var sv=new[]{f.Vertices[0],f.Vertices[1],b.Vertices[1],b.Vertices[0],new Vector3(1.7f,-.0002f,-.087f),new Vector3(2,-.0001f,-.03f),new Vector3(2.2f,-.0002f,-.08f),new Vector3(2.1f,-.0003f,-.06f)};
        var side=Surface(sv,new[]{0,1,4,1,2,4,2,3,4,3,0,4,5,6,7},Vector3.UnitY);
        var before=CassetteNativeOverlay.Fingerprint(f,b,side);var copy=CassetteNativeOverlay.Copy(f,b,side);
        Test("subdivided/kinked/detached",f,b,side,copy);
        for(int i=f.Vertices.Length+b.Vertices.Length;i<copy.Vertices.Length;i++)check(Math.Abs(copy.Uv[i].Y-Math.Clamp(-copy.Vertices[i].Z/.2f,0,1))<1e-4,"normal-projected depth on irregular side "+i);
        check(before==CassetteNativeOverlay.Fingerprint(f,b,side),"input copies unchanged");
        var assignment=new PlateFrontAssignment(f,b);var owned=assignment.Assign(sv[0],sv[1],sv[4]);
        check(owned!=null&&Vector3.Dot(owned.Normal,Vector3.UnitZ)>.99999f,"shared edge wins");
        check(assignment.Assign(sv[5],sv[6],sv[7]) is {} detached&&Vector3.Dot(detached.Normal,Vector3.UnitZ)>.99999f,"detached wall gets nearest front");
        var mirror=Matrix4x4.CreateReflection(new Plane(Vector3.UnitX,-.25f));var both=PlateOverlayGeometry.Mirror(copy,mirror);int off=copy.Vertices.Length;
        check(both.Triangles.Length==copy.Triangles.Length*2,"both halves drawn");
        for(int i=0;i<off;i++)
        {
            check(Vector3.Distance(both.Vertices[off+i],Vector3.Transform(copy.Vertices[i],mirror))<1e-6,"native mirror plane position");
            check(Vector3.Distance(both.Normals[off+i],Vector3.TransformNormal(copy.Normals[i],mirror))<1e-6,"mirrored normals");
            check(both.Uv[off+i]==copy.Uv[i],"mirror keeps d");
        }
        for(int t=0;t<copy.Triangles.Length;t+=3)check(both.Triangles[copy.Triangles.Length+t]==copy.Triangles[t]+off&&both.Triangles[copy.Triangles.Length+t+1]==copy.Triangles[t+2]+off&&both.Triangles[copy.Triangles.Length+t+2]==copy.Triangles[t+1]+off,"mirror winding reversed");
        check(PlateOverlayGeometry.Covers(both.Vertices,both.Triangles.Length/3,both.Vertices,both.Triangles.Length/3),"full mirror coverage allowed");
        check(!PlateOverlayGeometry.Covers(copy.Vertices,copy.Triangles.Length/3,both.Vertices,both.Triangles.Length/3),"primary-only cannot hide mirror");
        check(!PlateOverlayGeometry.Covers(both.Vertices,both.Triangles.Length/3,both.Vertices,both.Triangles.Length/3+1),"additional native geometry retained");
        check(!PlateOverlayGeometry.Covers(both.Vertices,999,new[]{new Vector3(100)},1),"uncovered bounds retained even with sufficient triangles");
        check(!PlateOverlayGeometry.Covers(both.Vertices,999,Array.Empty<Vector3>(),0),"unknown native mesh retained");
        var materials=CassetteTechnology.ReadDirectory(Path.Combine(AppContext.BaseDirectory,"NativeData"));
        var reactions=MaterialReactionCatalogue.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory,MaterialReactionCatalogue.FileName)));
        var preset=CassetteCatalogue.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory,CassetteCatalogue.FileName))).Presets.First(p=>p.Layers.Any(l=>l.TiltDegrees!=0));
        var fixture=JsonDocument.Parse(File.ReadAllText(Path.Combine(root,"tests/fixtures/strv122a-plate-shapes.json")));
        check(fixture.RootElement.GetArrayLength()==5,"all five STRV-122A shapes found");
        foreach(var shape in fixture.RootElement.EnumerateArray())
        {
            var raw=shape.GetProperty("vertices").EnumerateArray().Select(v=>v.GetSingle()).ToArray();
            var v=Enumerable.Range(0,raw.Length/3).Select(i=>new Vector3(raw[3*i],raw[3*i+1],raw[3*i+2])).ToArray();
            // Per-face stand-ins preserve saved polygon coordinates and configured corner thickness;
            // native joins/tool bevels are deliberately perturbed/subdivided, not claimed as a native export.
            int index=0;
            var combinedFront=new List<Vector3>();var combinedBack=new List<Vector3>();var combinedSide=new List<Vector3>();
            var combinedFN=new List<Vector3>();var combinedBN=new List<Vector3>();var combinedSN=new List<Vector3>();
            var combinedFT=new List<int>();var combinedBT=new List<int>();var combinedST=new List<int>();
            foreach(var face in shape.GetProperty("faces").EnumerateArray())
            {
                var ids=face.GetProperty("v").EnumerateArray().Select(x=>x.GetInt32()).ToArray();if(ids.Length<3)continue;
                var front=ids.Select(i=>v[i]).ToArray();var normal=Vector3.Cross(front[1]-front[0],front[2]-front[0]);if(normal.LengthSquared()<1e-12)continue;normal=Vector3.Normalize(normal);
                float thickness=face.GetProperty("t").EnumerateArray().Average(x=>x.GetSingle())*.001f;
                var back=front.Select(p=>p-normal*thickness).ToArray();var triangles=Enumerable.Range(1,front.Length-2).SelectMany(i=>new[]{0,i,i+1}).ToArray();
                var fs=Surface(front,triangles,normal);var bs=Surface(back,triangles.Reverse().ToArray(),-normal);
                var points=new List<Vector3>();var st=new List<int>();
                for(int i=0;i<front.Length;i++)
                {
                    int j=(i+1)%front.Length,k=points.Count;
                    var a=front[i];var z=front[j];var c=back[j];var d=back[i];var middle=(a+z+c+d)*.25f+Vector3.Normalize(Vector3.Cross(z-a,normal))*.0002f;
                    points.AddRange(new[]{a,z,c,d,middle,Vector3.Lerp(a,c,.32f),Vector3.Lerp(a,c,.61f),Vector3.Lerp(z,d,.53f)});
                    st.AddRange(new[]{k,k+1,k+4,k+1,k+2,k+4,k+2,k+3,k+4,k+3,k,k+4,k+5,k+6,k+7});
                }
                var ss=Surface(points.ToArray(),st.ToArray(),normal);var overlay=CassetteNativeOverlay.Copy(fs,bs,ss);
                Test("STRV mesh "+shape.GetProperty("meshVuid").GetInt32()+" face "+index++,fs,bs,ss,overlay);
                check(PlateOverlayGeometry.Mirror(overlay,mirror).Triangles.Length==2*overlay.Triangles.Length,"STRV both halves");
                void Append(OverlaySurface part,List<Vector3> positions,List<Vector3> normals,List<int> indices)
                {int start=positions.Count;positions.AddRange(part.Vertices);normals.AddRange(part.Normals);indices.AddRange(part.Triangles.Select(t=>t+start));}
                Append(fs,combinedFront,combinedFN,combinedFT);Append(bs,combinedBack,combinedBN,combinedBT);Append(ss,combinedSide,combinedSN,combinedST);
            }
            var allFront=new OverlaySurface(combinedFront.ToArray(),combinedFT.ToArray(),combinedFN.ToArray());
            var allBack=new OverlaySurface(combinedBack.ToArray(),combinedBT.ToArray(),combinedBN.ToArray());
            var allSide=new OverlaySurface(combinedSide.ToArray(),combinedST.ToArray(),combinedSN.ToArray());
            Test("STRV whole mesh "+shape.GetProperty("meshVuid").GetInt32(),allFront,allBack,allSide,CassetteNativeOverlay.Copy(allFront,allBack,allSide));
        }
        var dirPath=Path.Combine(output,"diagnostic-fixtures");Directory.CreateDirectory(dirPath);
        try
        {
            var dumps=new PlateDiagnosticDumps();var bad=copy with{FallbackTriangles=1,FallbackArea=copy.SideArea*.051};
            check(!dumps.Write(dirPath,"low",f,b,side,copy with{FallbackArea=copy.SideArea*.05}),"exactly five percent does not dump");
            for(int i=0;i<12;i++)check(dumps.Write(dirPath,i.ToString(),f,b,side,bad)==(i<10),"diagnostic cap "+i);
            check(!dumps.Write(dirPath,"0",f,b,side,bad),"once per plate");
            check(!new PlateDiagnosticDumps().Write(dirPath,"new-session",f,b,side,bad),"cap counts existing files across sessions");
            using var dump=JsonDocument.Parse(File.ReadAllText(Path.Combine(dirPath,"plate-0.json")));
            check(dump.RootElement.GetProperty("front").GetProperty("Vertices")[0].GetProperty("X").GetSingle()==f.Vertices[0].X,"dump serializes real vector fields");
            check(Directory.GetFiles(dirPath).Length==10,"maximum ten diagnostic files");
        }
        finally{Directory.Delete(dirPath,true);}
        var noBack=CassetteNativeOverlay.Copy(f,Surface(Array.Empty<Vector3>(),Array.Empty<int>(),Vector3.Zero),side,preset);
        check(noBack.FallbackTriangles==side.Triangles.Length/3&&noBack.FallbackArea==noBack.SideArea,"unresolvable thickness drawn in nearest layer, counted by area");
        check(noBack.Uv.All(x=>float.IsFinite(x.Y)&&x.Y>=0&&x.Y<=1),"nearest-layer fallback always finite");
        File.WriteAllText(Path.Combine(output,"GEOMETRY-02233.tsv"),evidence.ToString());
        Console.WriteLine($"0.22.33 PASS: {count} checks.");
        void Test(string name,OverlaySurface fs,OverlaySurface bs,OverlaySurface ss,OverlayCopy overlay)
        {
            check(overlay.Triangles.Length>=fs.Triangles.Length+bs.Triangles.Length+ss.Triangles.Length,"nonempty complete overlay "+name);
            check(overlay.SideArea>0&&overlay.FallbackArea<=overlay.SideArea*.05,"fallback area <=5% "+name);
            check(overlay.Uv.All(p=>float.IsFinite(p.Y)&&p.Y>=0&&p.Y<=1),"clamped depth "+name);
            check(Math.Abs(PlateFrontAssignment.Area(new(overlay.Vertices,overlay.Triangles.Skip(fs.Triangles.Length+bs.Triangles.Length).ToArray(),overlay.Normals))-PlateFrontAssignment.Area(ss))<Math.Max(1e-5,overlay.SideArea*1e-5),"side area retained "+name);
            evidence.AppendLine(FormattableString.Invariant($"{name}\t{ss.Triangles.Length/3}\t{overlay.SideArea}\t{overlay.FallbackTriangles}\t{overlay.FallbackArea}\t{overlay.Triangles.Length/3}"));
        }
    }
}

