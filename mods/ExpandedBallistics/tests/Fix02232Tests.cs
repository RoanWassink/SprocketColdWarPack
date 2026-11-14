using System.Numerics;
using System.Text;
namespace SprocketExpandedBallistics;

internal static class Fix02232Tests
{
    private sealed class Renderer:IOverlayNativeRenderer
    {public long Identity=>17;public bool Current=>true;public bool Hidden{get;set;}}
    private sealed class NoRead:IRawMesh
    {
        public long Pointer=>7;public bool Released=>throw new Exception("mesh accessed during orbit");
        public int DirtyFlags=>throw new Exception("dirty read");public int IndexDirtyFlags=>0;
        public int FaceCount=>throw new Exception("face read");public int FaceListCount=>0;public int LoopCount=>0;
        public IRawFace? Face(int index)=>throw new Exception("face read");
    }
    private static OverlaySurface Surface(Vector3[] v,int[] t,Vector3 n)=>new(v,t,Enumerable.Repeat(n,v.Length).ToArray());
    internal static void Run(Action<bool,string> check)
    {
        int checks=0;var sink=check;check=(ok,message)=>{sink(ok,"fix02232: "+message);checks++;};
        var root=Fix02228Tests.SourceRoot();var output=Fix02228Tests.OutputRoot();
        var evidence=new StringBuilder("case\tvertex\tx\ty\tz\td\texpected\terror\n");
        float now=0;bool mouse=false;var gate=new PlateMeshGate(()=>now,()=>mouse);
        var renderer=new Renderer();var hidden=new OverlayVisibility(_=>{});bool overlay=true;hidden.Hide(7,renderer);
        void Follow(){if(gate.Quiet){overlay=false;hidden.RestoreAll();}}
        foreach(int button in new[]{0,1,2})
        {
            mouse=true;now+=2;Follow();
            check(gate.Busy&&!gate.Quiet&&overlay&&renderer.Hidden,"camera button "+button+" retains cached overlay/native hide");
            check(gate.Get(new NoRead(),"orbit",SnapshotUse.Fresh,out var reason)==null&&reason=="mouse button held","Fresh mesh read blocked while orbiting");
            check(gate.Get(new NoRead(),"orbit apply",SnapshotUse.Fresh,out _)==null,"Apply's Fresh mesh read remains blocked while orbiting");
            mouse=false;check(!gate.Busy,"mouse release opens read/write gate");
        }
        gate.NoteModified(7);Follow();check(!overlay&&!renderer.Hidden&&gate.Quiet,"real modification immediately restores native");
        now+=PlateMeshGate.QuietSeconds-.001f;Follow();check(!overlay&&!renderer.Hidden,"native visible throughout quiet interval");
        now+=.002f;check(!gate.Busy&&!gate.Quiet,"quiet interval expires");overlay=true;hidden.Hide(7,renderer);Follow();
        check(overlay&&renderer.Hidden,"settled rebuild returns overlay and native hide");
        // Signature change follows the production Remove/Enqueue path, not mouse state.
        hidden.Restore(7);overlay=false;check(!renderer.Hidden&&!overlay,"changed content signature invalidates drawable cache");
        var runtime=File.ReadAllText(Path.Combine(root,"RuntimeCassettePlateVisual.cs")).Replace("\r\n","\n");
        check(runtime.Split("if(PlateMeshAccess.Gate.Quiet){BeginEdit();return;}").Length==3,"both frame/follow paths invalidate on Quiet only");
        check(!runtime.Contains("Gate.Busy){BeginEdit()")&&runtime.Contains("FollowVisible();\n        if(PlateMeshAccess.Gate.Busy)return;"),"orbit follows cache before busy read/upload guard");
        check(runtime.Contains("if(PlateMeshAccess.Gate.Busy)return;")&&runtime.Contains("if(!Visible||PlateMeshAccess.Gate.Busy)"),"signature reads and final upload gate unchanged");
        check(File.ReadAllText(Path.Combine(root,"PlateMeshAccess.cs")).Contains("RuntimeCassettePlateVisual.BeginEdit();"),"NoteModified restores native immediately");

        var preset=new CassettePreset("depth_test","Depth test","1950.01.01",1,1000,new[]{new CassetteLayer("rha",20,0,"up"),new CassetteLayer("ceramicArray",30,0,"up"),new CassetteLayer("air",50,0,"up")});
        var bounds=CassetteDepthStrip.Boundaries(preset);var strip=CassetteDepthStrip.Build(preset);
        check(strip[0]==CassetteDepthStrip.Colour("rha")&&strip[^1]==CassetteDepthStrip.Colour("air"),"layer 1 at native front; last layer at native back");
        check(bounds.SequenceEqual(new[]{0d,.2,.5,1}),"authored layer order/depth boundaries preserved");
        foreach(var shear in new[]{0f,.7f,-.35f})
        {
            var f=new[]{Vector3.Zero,new Vector3(2,0,0),new Vector3(2,1,0),Vector3.UnitY};
            var b=f.Select(v=>v+new Vector3(shear,0,-.2f)).ToArray();
            Test("normal/edge="+shear,Surface(f,new[]{0,1,2,0,2,3},Vector3.UnitZ),Surface(b.Reverse().ToArray(),new[]{3,1,2,3,0,1},-Vector3.UnitZ),
                new[]{new OverlayWallDepth(f[0],f[1],b[0],b[1],Vector3.UnitZ)},false);
        }
        {
            // A V end wall is coplanar as a side surface, but belongs to two
            // different native front planes. Weld the crease vertices deliberately.
            var f=new[]{new Vector3(-1,0,0),new Vector3(0,0,.5f),new Vector3(1,0,0),new Vector3(-1,1,0),new Vector3(0,1,.5f),new Vector3(1,1,0)};
            var n0=Vector3.Normalize(new Vector3(-.5f,0,1));var n1=Vector3.Normalize(new Vector3(.5f,0,1));
            var b=new[]{f[0]-n0*.2f,f[1]-new Vector3(0,0,.2f/n0.Z),f[2]-n1*.2f};
            var rear=b.Concat(b.Select(v=>v+Vector3.UnitY)).ToArray();
            Test("V shared crease",Surface(f,new[]{0,1,4,0,4,3,1,2,5,1,5,4},Vector3.UnitZ),Surface(rear,new[]{0,4,1,0,3,4,1,5,2,1,4,5},-Vector3.UnitZ),
                new[]{new OverlayWallDepth(f[0],f[1],b[0],b[1],n0),new OverlayWallDepth(f[1],f[2],b[1],b[2],n1)},true);
            var front=Surface(f,new[]{0,1,4,0,4,3,1,2,5,1,5,4},Vector3.UnitZ);
            var back=Surface(rear,new[]{0,4,1,0,3,4,1,5,2,1,4,5},-Vector3.UnitZ);
            var sides=Surface(new[]{f[0],f[1],f[2],b[0],b[1],b[2],new Vector3(-.04f,0,.36f),new Vector3(.04f,0,.36f),new Vector3(0,0,.44f)},
                new[]{0,1,4,0,4,3,1,2,5,1,5,4,6,7,8},Vector3.UnitY);
            var split=CassetteNativeOverlay.Copy(front,back,sides,preset);int off=f.Length+rear.Length;
            check(split.Vertices.Length>off+sides.Vertices.Length,"triangle crossing native V seam is split into independent walls");
            double Area(Vector3[] v,int[] t,int start)=>Enumerable.Range(start/3,(t.Length-start)/3).Sum(i=>(double)Vector3.Cross(v[t[i*3+1]]-v[t[i*3]],v[t[i*3+2]]-v[t[i*3]]).Length()/2);
            check(Math.Abs(Area(split.Vertices,split.Triangles,front.Triangles.Length+back.Triangles.Length)-Area(sides.Vertices,sides.Triangles,0))<1e-5,"crease splitting preserves native surface area");
            for(int i=off+sides.Vertices.Length;i<split.Vertices.Length;i++)
            {
                var p=split.Vertices[i];var n=p.X<=0?n0:n1;var origin=p.X<=0?f[0]:f[1];
                float exact=Vector3.Dot(p-origin,n)/(-.2f);
                check(Math.Abs(exact-split.Uv[i].Y)<1e-4,"new crease/cut vertex exact plane depth "+i);
            }
        }
        {
            var f=new[]{Vector3.Zero,new Vector3(2,0,0),Vector3.UnitY};var b=new[]{new Vector3(0,0,-.2f),new Vector3(2,0,-.4f),new Vector3(0,1,-.2f)};
            var front=Surface(f,new[]{0,1,2},Vector3.UnitZ);var back=Surface(b,new[]{0,2,1},-Vector3.UnitZ);
            var wall=new OverlayWallDepth(f[0],f[1],b[0],b[1],Vector3.UnitZ);
            var side=Surface(new[]{f[0],f[1],b[1],b[0]},new[]{0,1,2,0,2,3},Vector3.UnitY);
            var copy=CassetteNativeOverlay.Copy(front,back,side,preset);int off=f.Length+b.Length;
            check(copy.Vertices.Length>off+side.Vertices.Length,"tapered wall split at actual layer boundaries");
            for(int i=off;i<copy.Vertices.Length;i++)
            {var p=copy.Vertices[i];float expected=-p.Z/(.2f+.1f*p.X);check(Math.Abs(copy.Uv[i].Y-expected)<1e-4,"taper exact ruled-wall depth "+i);}
            foreach(float d in bounds.Skip(1).SkipLast(1).Select(x=>(float)x))
                check(copy.Uv.Skip(off+4).Count(uv=>Math.Abs(uv.Y-d)<1e-5)>=4,"taper boundary duplicated at "+d);
        }

        void Test(string name,OverlaySurface front,OverlaySurface back,OverlayWallDepth[] walls,bool kink)
        {
            // Irregular subdivisions include both rail points and true interior vertices.
            var v=new List<Vector3>();var expected=new List<float>();var tri=new List<int>();
            int Add(Vector3 point,float d){int i=v.IndexOf(point);if(i>=0)return i;v.Add(point);expected.Add(d);return v.Count-1;}
            foreach(var wall in walls)
            {
                float[] along={0,.31f,.73f,1},depth={0,.13f,.57f,.82f,1};var ids=new int[along.Length,depth.Length];
                for(int x=0;x<along.Length;x++)for(int y=0;y<depth.Length;y++)ids[x,y]=Add(wall.At(along[x],depth[y]),depth[y]);
                for(int x=0;x<along.Length-1;x++)for(int y=0;y<depth.Length-1;y++)tri.AddRange(new[]{ids[x,y],ids[x+1,y],ids[x+1,y+1],ids[x,y],ids[x+1,y+1],ids[x,y+1]});
            }
            var sides=Surface(v.ToArray(),tri.ToArray(),Vector3.UnitY);var before=CassetteNativeOverlay.Fingerprint(front,back,sides);
            var copy=CassetteNativeOverlay.Copy(front,back,sides,preset);int off=front.Vertices.Length+back.Vertices.Length;
            check(copy.Uv.Take(front.Vertices.Length).All(uv=>uv.Y==0)&&copy.Uv.Skip(front.Vertices.Length).Take(back.Vertices.Length).All(uv=>uv.Y==1),name+" exact native endpoints");
            for(int i=off;i<copy.Vertices.Length;i++)
            {
                int source=v.IndexOf(copy.Vertices[i]);check(source>=0,name+" copied point belongs to native geometry");
                float error=Math.Abs(copy.Uv[i].Y-expected[source]);check(error<1e-4,name+" exact d vertex "+i);
                var p=copy.Vertices[i];evidence.AppendLine(FormattableString.Invariant($"{name}\t{i}\t{p.X}\t{p.Y}\t{p.Z}\t{copy.Uv[i].Y}\t{expected[source]}\t{error}"));
            }
            if(kink)check(copy.Vertices.Length>off+v.Count,"V crease has independent per-wall vertices");
            for(int t=front.Triangles.Length+back.Triangles.Length;t<copy.Triangles.Length;t+=3)
            {
                var ids=copy.Triangles.Skip(t).Take(3).ToArray();var wall=walls.First(w=>ids.All(i=>w.Contains(copy.Vertices[i])));
                foreach(float d in new[]{.2f,.5f,.8f})
                {
                    var crossings=new List<Vector3>();
                    for(int e=0;e<3;e++)
                    {
                        int a=ids[e],b=ids[(e+1)%3];float da=copy.Uv[a].Y,db=copy.Uv[b].Y;
                        if((da<d&&db>d)||(db<d&&da>d))crossings.Add(Vector3.Lerp(copy.Vertices[a],copy.Vertices[b],(d-da)/(db-da)));
                    }
                    if(crossings.Count==2)
                    {
                        var line=crossings[1]-crossings[0];
                        check(Math.Abs(Vector3.Dot(line,wall.FrontNormal))<1e-5,name+" layer plane parallel to native front plane");
                        check(Vector3.Cross(line,wall.F1-wall.F0).Length()<1e-5,name+" stripe parallel to front edge");
                    }
                }
            }
            check(CassetteNativeOverlay.Fingerprint(front,back,sides)==before,name+" native arrays remain byte-value equal");
        }
        // Protection regression is covered against the 0.22.31 golden in Fix02234Tests.
        File.WriteAllText(Path.Combine(output,"DEPTH-02232.tsv"),evidence.ToString());
        Console.WriteLine($"0.22.32 PASS: {checks} checks; geometry/protection evidence in test-output/.");
    }
}
