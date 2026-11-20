using System.Globalization;
using System.Numerics;
using System.Reflection;
using System.Runtime.Loader;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
namespace SprocketExpandedBallistics;

/// <summary>
/// 0.22.83: the layer view (Ctrl+L) shows a plate marked [Solid block] as one dense block in the colour of its recipe (SolidBlockOverlay, called from
/// RuntimeLayerFaces.Overlay), and the overlay signature carries the mark (one line in RuntimeCassettePlateVisual.cs) so that a change of only the mark rebuilds it.
/// The native tessellation is not available here: the native surfaces are the per-face stand-in of FEAT74 (front polygon, back at the corner thickness along the
/// face normal, walls), passed through the production CassetteNativeOverlay.Copy. Plates without the mark: every overlay output is compared with the 0.22.82
/// code itself (its compiled test assembly, loaded by reflection), on the overlay fixtures, Object 292 and Object 293 (in layers and not).
/// </summary>
internal static class Feat02283SolidOverlayTests
{
    /// <summary>The production files 0.22.83 may change on top of 0.22.82 (SolidBlockOverlay.cs is new).</summary>
    internal static bool IsPlanned(string rel)=>rel is "RuntimeCassettePlateVisual.cs" or "RuntimeComposerFill.cs" or "SolidBlockOverlay.cs" or "Plugin.cs" or "SprocketExpandedBallistics.csproj" or "RELEASE-NOTES.md";
    internal const string FillBegin="    // ---- 0.22.83 solid overlay (begin) ----",FillEnd="    // ---- 0.22.83 solid overlay (end) ----",FillLine="// 0.22.83 solid overlay";
    internal const string VisualSuffix="+(CompositePlate.IsSolidBlock(RuntimeLayerFaces.MeshName(plate,true))?SolidBlockOverlay.Mark:\"\"); // 0.22.83: the [Solid block] mark only for a marked plate";
    /// <summary>RuntimeComposerFill.cs without the 0.22.83 additions (the one line in Overlay and the region of the solid overlay), for the ledger checks of older versions.</summary>
    internal static string Without83Fill(string s)
    {
        var lines=s.Split("\r\n").ToList();
        int b=lines.IndexOf(FillBegin),e=lines.IndexOf(FillEnd);
        if(b>=0&&e>b)lines.RemoveRange(b,e-b+1);
        return string.Join("\r\n",lines.Where(l=>!l.EndsWith(FillLine,StringComparison.Ordinal)));
    }
    /// <summary>RuntimeCassettePlateVisual.cs with the one 0.22.83 line back as it was in 0.22.82.</summary>
    internal static string Without83Visual(string s)=>s.Replace(VisualSuffix,";",StringComparison.Ordinal);
    internal static byte[] Without83VisualBytes(byte[] b){var s=Encoding.UTF8.GetString(b);return s.Contains(VisualSuffix,StringComparison.Ordinal)?Encoding.UTF8.GetBytes(Without83Visual(s)):b;}
    internal static string F(double v,string f="0.###")=>v.ToString(f,CultureInfo.InvariantCulture);

    // ---------------- the stand-in of the native surfaces (FEAT74, Build) ----------------
    internal static (OverlaySurface Front,OverlaySurface Back,OverlaySurface Sides) StandIn(string json)
    {
        using var doc=JsonDocument.Parse(json);var mesh=doc.RootElement;
        var raw=mesh.GetProperty("vertices").EnumerateArray().Select(x=>x.GetSingle()).ToArray();
        var v=Enumerable.Range(0,raw.Length/3).Select(i=>new Vector3(raw[3*i],raw[3*i+1],raw[3*i+2])).ToArray();
        var fp=new List<Vector3>();var bp=new List<Vector3>();var sp=new List<Vector3>();var fn=new List<Vector3>();var bn=new List<Vector3>();var sn=new List<Vector3>();
        var ft=new List<int>();var bt=new List<int>();var st=new List<int>();
        foreach(var face in mesh.GetProperty("faces").EnumerateArray())
        {
            var ids=face.GetProperty("v").EnumerateArray().Select(x=>x.GetInt32()).ToArray();if(ids.Length<3)continue;
            var front=ids.Select(i=>v[i]).ToArray();
            var newell=Vector3.Zero;for(int i=0;i<front.Length;i++){var a=front[i];var b=front[(i+1)%front.Length];newell+=new Vector3((a.Y-b.Y)*(a.Z+b.Z),(a.Z-b.Z)*(a.X+b.X),(a.X-b.X)*(a.Y+b.Y));}
            bool flat=newell.Length()<1e-9f;var normal=flat?Vector3.Zero:Vector3.Normalize(newell);
            float thickness=face.GetProperty("t").EnumerateArray().Average(x=>x.GetSingle())*.001f;
            var back=front.Select(p=>p-normal*thickness).ToArray();var fan=Enumerable.Range(1,front.Length-2).SelectMany(i=>new[]{0,i,i+1}).ToArray();
            int f0=fp.Count;fp.AddRange(front);fn.AddRange(Enumerable.Repeat(normal,front.Length));ft.AddRange(fan.Select(i=>i+f0));
            int b0=bp.Count;bp.AddRange(back);bn.AddRange(Enumerable.Repeat(-normal,back.Length));bt.AddRange(fan.Reverse().Select(i=>i+b0));
            if(flat)continue;
            for(int i=0;i<front.Length;i++)
            {
                int j=(i+1)%front.Length,k=sp.Count;
                var a=front[i];var z=front[j];var c=back[j];var d=back[i];var outward=Vector3.Cross(z-a,normal);var middle=(a+z+c+d)*.25f+(outward.LengthSquared()<1e-18f?Vector3.Zero:Vector3.Normalize(outward))*.0002f;
                sp.AddRange(new[]{a,z,c,d,middle,Vector3.Lerp(a,c,.32f),Vector3.Lerp(a,c,.61f),Vector3.Lerp(z,d,.53f)});sn.AddRange(Enumerable.Repeat(normal,8));
                st.AddRange(new[]{k,k+1,k+4,k+1,k+2,k+4,k+2,k+3,k+4,k+3,k,k+4,k+5,k+6,k+7});
            }
        }
        return (new OverlaySurface(fp.ToArray(),ft.ToArray(),fn.ToArray()),new OverlaySurface(bp.ToArray(),bt.ToArray(),bn.ToArray()),new OverlaySurface(sp.ToArray(),st.ToArray(),sn.ToArray()));
    }

    /// <summary>The stand-in surfaces taken as the native copy itself (front d 0, back d 1, wall points at their depth), without CassetteNativeOverlay.Copy: for the large plates in
    /// layers (15 000 triangles), where Copy takes minutes; Copy is byte-identical to 0.22.82 (Verify-Delivery) and the comparison is about the overlay that follows it.</summary>
    internal static OverlayCopy DirectCopy(string json)
    {
        var s=StandIn(json);var v=new List<Vector3>();var n=new List<Vector3>();var uv=new List<Vector2>();var t=new List<int>();
        void Add(OverlaySurface part,Func<int,float> depth){int o=v.Count;v.AddRange(part.Vertices);n.AddRange(part.Normals);for(int i=0;i<part.Vertices.Length;i++)uv.Add(new(.5f,depth(i)));t.AddRange(part.Triangles.Select(i=>i+o));}
        Add(s.Front,_=>0);Add(s.Back,_=>1);
        var wall=new[]{0f,0f,1f,1f,.5f,.32f,.61f,.53f};Add(s.Sides,i=>wall[i%8]);
        return new(v.ToArray(),t.ToArray(),n.ToArray(),uv.ToArray());
    }

    // ---------------- the glue of RuntimeLayerFaces.Overlay, statement for statement (the IL2CPP reads replaced by the saved mesh) ----------------
    /// <summary>0.22.83: the line "if(SolidOverlay(plate,data) is {} solid)return solid;" and then the 0.22.82 body. Null: the native copy stays (as the runtime's "??data").</summary>
    internal static OverlayCopy? Overlay83(string? name,OverlayCopy data,string json,CassettePreset? preset,bool painted,SolidBlockOverlay.Report? report=null)
    {
        if(CompositePlate.IsSolidBlock(name))return SolidBlockOverlay.Apply(data,json,preset,report);
        if(CompositePlate.LayoutFromName(name) is not {} kind)return null;
        if(preset==null)return null;
        var snapshot=ComposerFill.Read(json);
        var mesh=LayerMesh.Read(json) with{FollowWall=name?.Contains(CompositePlate.FrontMarker,StringComparison.Ordinal)==true};if(mesh.Polygons.Length!=snapshot.Faces.Length)return null;
        var ranks=LayerHitIndex.RanksOf(snapshot,json);var layout=LayerLayout.Build(preset,kind,LayerLayout.DefaultLabel);
        return painted?LayerOverlay.RemapPainted(data,mesh,ranks,layout,new LayerOverlay.OverlayReport()):LayerOverlay.Remap(data,mesh,ranks,layout);
    }
    /// <summary>The 0.22.82 code (its own compiled test assembly): CassetteNativeOverlay.Copy and the body of RuntimeLayerFaces.Overlay of 0.22.82.</summary>
    internal sealed class Old:IDisposable
    {
        private readonly AssemblyLoadContext alc;private readonly Assembly a;private const BindingFlags All=BindingFlags.Static|BindingFlags.Instance|BindingFlags.NonPublic|BindingFlags.Public;
        internal Old(string dll){alc=new AssemblyLoadContext("eb-0.22.82-overlay",true);a=alc.LoadFromAssemblyPath(dll);}
        internal Type T(string n)=>a.GetType("SprocketExpandedBallistics."+n,true)!;
        internal object? Call(string type,string method,params object?[] args)=>T(type).GetMethods(All).Single(m=>m.Name==method&&m.IsStatic&&m.GetParameters().Length==args.Length&&m.GetParameters().Zip(args).All(p=>p.Second==null||p.First.ParameterType.IsInstanceOfType(p.Second)||p.First.ParameterType.IsByRef)).Invoke(null,args);
        internal object? Preset(string catalogueText,string id){var cat=Call("CassetteCatalogue","Parse",catalogueText)!;var list=(System.Collections.IEnumerable)cat.GetType().GetProperty("Presets",All)!.GetValue(cat)!;foreach(var p in list)if(Equals(p.GetType().GetProperty("Id")!.GetValue(p),id))return p;throw new InvalidOperationException("preset "+id);}
        private object Surface(OverlaySurface s)=>Activator.CreateInstance(T("OverlaySurface"),s.Vertices,s.Triangles,s.Normals)!;
        internal object Copy((OverlaySurface F,OverlaySurface B,OverlaySurface S) s,object? preset)=>Call("CassetteNativeOverlay","Copy",Surface(s.F),Surface(s.B),Surface(s.S),preset)!;
        internal object CopyOf(OverlayCopy c)=>Activator.CreateInstance(T("OverlayCopy"),c.Vertices,c.Triangles,c.Normals,c.Uv)!;
        internal string Signature(string mesh,string material,string preset)=>(string)Call("OverlayContent","Signature",mesh,material,preset)!;
        internal object? Overlay82(string? name,object data,string json,object? preset,bool painted)
        {
            var kind=Call("CompositePlate","LayoutFromName",name);if(kind==null)return null;
            if(preset==null)return null;
            var snapshot=Call("ComposerFill","Read",json,false)!;
            var mesh=Call("LayerMesh","Read",json)!;var clone=mesh.GetType().GetMethod("<Clone>$",All)!.Invoke(mesh,null)!;clone.GetType().GetProperty("FollowWall",All)!.SetValue(clone,name?.Contains(" [FW75:",StringComparison.Ordinal)==true);mesh=clone;
            int polygons=((Array)mesh.GetType().GetProperty("Polygons")!.GetValue(mesh)!).Length;int faces=((Array)snapshot.GetType().GetProperty("Faces")!.GetValue(snapshot)!).Length;if(polygons!=faces)return null;
            var ranks=Call("LayerHitIndex","RanksOf",snapshot,json)!;
            var label=Delegate.CreateDelegate(typeof(Func<string,string>),T("LayerLayout").GetMethod("DefaultLabel",All)!);
            var layout=Call("LayerLayout","Build",preset,kind,label)!;
            return painted?Call("LayerOverlay","RemapPainted",data,mesh,ranks,layout,Activator.CreateInstance(T("LayerOverlay").GetNestedType("OverlayReport",All)!,true)):Call("LayerOverlay","Remap",data,mesh,ranks,layout,false);
        }
        internal static string Hash(object copy)
        {
            var t=copy.GetType();T0 G<T0>(string p)=>(T0)t.GetProperty(p,All)!.GetValue(copy)!;
            return HashOf(G<Vector3[]>("Vertices"),G<int[]>("Triangles"),G<Vector3[]>("Normals"),G<Vector2[]>("Uv"),G<int>("FallbackTriangles"),G<double>("FallbackArea"),G<double>("SideArea"));
        }
        public void Dispose()=>alc.Unload();
    }
    internal static string Hash(OverlayCopy c)=>HashOf(c.Vertices,c.Triangles,c.Normals,c.Uv,c.FallbackTriangles,c.FallbackArea,c.SideArea);
    internal static string HashOf(Vector3[] v,int[] t,Vector3[] n,Vector2[] uv,int fallback,double area,double side)
    {
        using var ms=new MemoryStream();using var w=new BinaryWriter(ms);
        w.Write(v.Length);foreach(var p in v){w.Write(p.X);w.Write(p.Y);w.Write(p.Z);}
        w.Write(t.Length);foreach(var i in t)w.Write(i);
        w.Write(n.Length);foreach(var p in n){w.Write(p.X);w.Write(p.Y);w.Write(p.Z);}
        w.Write(uv.Length);foreach(var p in uv){w.Write(p.X);w.Write(p.Y);}
        w.Write(fallback);w.Write(area);w.Write(side);w.Flush();
        return Convert.ToHexString(SHA256.HashData(ms.ToArray()))[..16];
    }

    // ---------------- independent geometry for the checks (not the code of SolidBlockOverlay) ----------------
    internal static (Vector3[] P,int[][] F) Read(string json)
    {
        using var d=JsonDocument.Parse(json);var v=d.RootElement.GetProperty("vertices").EnumerateArray().Select(x=>x.GetSingle()).ToArray();
        var p=new Vector3[v.Length/3];for(int i=0;i<p.Length;i++)p[i]=new(v[3*i],v[3*i+1],v[3*i+2]);
        return (p,d.RootElement.GetProperty("faces").EnumerateArray().Select(f=>f.GetProperty("v").EnumerateArray().Select(x=>x.GetInt32()).ToArray()).ToArray());
    }
    /// <summary>Seven pseudo-random directions, majority of the parities, on the fan triangles of the drawn faces.</summary>
    internal static bool InsideIndependent(Vector3 point,Vector3[] p,int[][] faces)
    {
        var rng=new Random(83);int votes=0;
        for(int k=0;k<7;k++)
        {
            var d=Vector3.Normalize(new Vector3((float)(rng.NextDouble()*2-1),(float)(rng.NextDouble()*2-1),(float)(rng.NextDouble()*2-1)));int n=0;
            foreach(var f in faces)for(int j=1;j+1<f.Length;j++)if(Ray(point,d,p[f[0]],p[f[j]],p[f[j+1]]))n++;
            if((n&1)==1)votes++;
        }
        return votes>=4;
    }
    internal static bool Ray(Vector3 o,Vector3 d,Vector3 a,Vector3 b,Vector3 c)
    {
        double e1x=b.X-a.X,e1y=b.Y-a.Y,e1z=b.Z-a.Z,e2x=c.X-a.X,e2y=c.Y-a.Y,e2z=c.Z-a.Z;
        double px=d.Y*e2z-d.Z*e2y,py=d.Z*e2x-d.X*e2z,pz=d.X*e2y-d.Y*e2x;double det=e1x*px+e1y*py+e1z*pz;if(Math.Abs(det)<1e-18)return false;
        double inv=1/det,tx=o.X-a.X,ty=o.Y-a.Y,tz=o.Z-a.Z;double u=(tx*px+ty*py+tz*pz)*inv;if(u<0||u>1)return false;
        double qx=ty*e1z-tz*e1y,qy=tz*e1x-tx*e1z,qz=tx*e1y-ty*e1x;double v=(d.X*qx+d.Y*qy+d.Z*qz)*inv;if(v<0||u+v>1)return false;
        return (e2x*qx+e2y*qy+e2z*qz)*inv>0;
    }
    /// <summary>Distance from a point to the drawn surface: every triangle of three corners of a face (any diagonal of a warped face), by sampling-free projection.</summary>
    internal static double SurfaceDistance(Vector3 q,Vector3[] p,int[][] faces)
    {
        double best=double.PositiveInfinity;
        foreach(var f in faces)for(int i=0;i<f.Length;i++)for(int j=i+1;j<f.Length;j++)for(int k=j+1;k<f.Length;k++)best=Math.Min(best,TriDistance(q,p[f[i]],p[f[j]],p[f[k]]));
        return best;
    }
    internal static double TriDistance(Vector3 q,Vector3 a,Vector3 b,Vector3 c)
    {
        var n=Vector3.Cross(b-a,c-a);double len=n.Length();
        double Seg(Vector3 x,Vector3 y){var d=y-x;double l=d.LengthSquared();double t=l<1e-24?0:Math.Clamp(Vector3.Dot(q-x,d)/l,0,1);return (q-(x+d*(float)t)).Length();}
        double edges=Math.Min(Seg(a,b),Math.Min(Seg(b,c),Seg(c,a)));if(len<1e-18)return edges;
        n/=(float)len;double h=Vector3.Dot(q-a,n);var proj=q-n*(float)h;
        bool inside=Vector3.Dot(Vector3.Cross(b-a,proj-a),n)>=0&&Vector3.Dot(Vector3.Cross(c-b,proj-b),n)>=0&&Vector3.Dot(Vector3.Cross(a-c,proj-c),n)>=0;
        return inside?Math.Abs(h):edges;
    }
    internal static double Area(Vector3 a,Vector3 b,Vector3 c)=>Vector3.Cross(b-a,c-a).Length()*.5;

    // ---------------- blueprint frames (as FEAT82) ----------------
    internal static float[] Euler(float x,float y,float z)
    {
        float rx=x*MathF.PI/180,ry=y*MathF.PI/180,rz=z*MathF.PI/180;float cx=MathF.Cos(rx),sx=MathF.Sin(rx),cy=MathF.Cos(ry),sy=MathF.Sin(ry),cz=MathF.Cos(rz),sz=MathF.Sin(rz);
        return MM(MM(new[]{cy,0,sy,0,1,0,-sy,0,cy},new[]{1,0,0,0,cx,-sx,0,sx,cx}),new[]{cz,-sz,0,sz,cz,0,0,0,1});
    }
    internal static float[] MM(float[] a,float[] b){var r=new float[9];for(int i=0;i<3;i++)for(int j=0;j<3;j++){float s=0;for(int k=0;k<3;k++)s+=a[3*i+k]*b[3*k+j];r[3*i+j]=s;}return r;}
    internal static Vector3 Mul(float[] m,Vector3 v)=>new(m[0]*v.X+m[1]*v.Y+m[2]*v.Z,m[3]*v.X+m[4]*v.Y+m[5]*v.Z,m[6]*v.X+m[7]*v.Y+m[8]*v.Z);
    internal static (float[] R,Vector3 T) World(Dictionary<int,JsonElement> objects,JsonElement o)
    {
        var ts=o.GetProperty("transform");var rot=ts.GetProperty("rot");var pos=ts.GetProperty("pos");
        var r=Euler(rot[0].GetSingle(),rot[1].GetSingle(),rot[2].GetSingle());var p=new Vector3(pos[0].GetSingle(),pos[1].GetSingle(),pos[2].GetSingle());
        int parent=o.GetProperty("pvuid").GetInt32();if(parent<0||!objects.TryGetValue(parent,out var po))return (r,p);
        var (pr,pt)=World(objects,po);return (MM(pr,r),Mul(pr,p)+pt);
    }
    internal static (string Json,string? Name) Mesh(JsonElement root,int vuid)
    {
        var m=root.GetProperty("meshes").EnumerateArray().First(x=>x.GetProperty("vuid").GetInt32()==vuid).GetProperty("meshData");
        return (m.GetProperty("mesh").GetRawText(),m.TryGetProperty("name",out var n)&&n.ValueKind==JsonValueKind.String?n.GetString():null);
    }
    internal static string Thin(string json,ushort mm)
    {
        var n=JsonNode.Parse(json)!;
        foreach(var f in n["faces"]!.AsArray()){var t=f!["t"]!.AsArray();for(int i=0;i<t.Count;i++)t[i]=mm;}
        return n.ToJsonString();
    }

    // ---------------- pictures (drawn by preview/Make-Solid-Overlay-Sheet.ps1) ----------------
    internal sealed class Panel
    {
        public string title="",explanation="";public int width,height;public List<Dictionary<string,object?>> ops=new();
        public void Poly(IEnumerable<(double X,double Y)> xy,string fill,string? stroke=null,double strokePx=0)=>ops.Add(new(){["op"]="poly",["xy"]=xy.SelectMany(p=>new[]{Math.Round(p.X,2),Math.Round(p.Y,2)}).ToArray(),["fill"]=fill,["stroke"]=stroke,["strokePx"]=strokePx});
        public void Text(double x,double y,string text,string colour="#e8e2d4",string anchor="MiddleLeft",string? background=null,double size=9)=>ops.Add(new(){["op"]="text",["x"]=Math.Round(x,1),["y"]=Math.Round(y,1),["text"]=text,["colour"]=colour,["anchor"]=anchor,["background"]=background,["size"]=size});
        public void Rect(double x,double y,double w,double h,string fill,string? stroke=null,double px=0)=>Poly(new[]{(x,y),(x+w,y),(x+w,y+h),(x,y+h)},fill,stroke,px);
    }
    /// <summary>The overlay as the layer view draws it, painter's order: every triangle with area in the strip colour at its depth coordinate, shaded by its normal; the zero-area triangles draw nothing.</summary>
    internal static void Draw(Panel panel,OverlayCopy copy,uint[] strip,Func<Vector3,Vector3> world,double x0,double y0,double w,double h,string caption,Vector3? viewDir=null,Func<Vector3,bool>? only=null)
    {
        var view=Vector3.Normalize(viewDir??new Vector3(.55f,.45f,1));var up=Vector3.UnitY;var right=Vector3.Normalize(Vector3.Cross(up,view));var vup=Vector3.Cross(view,right);var light=Vector3.Normalize(new Vector3(.3f,.8f,.6f));
        var pts=copy.Vertices.Select(world).ToArray();
        var proj=pts.Select(q=>(X:(double)Vector3.Dot(q,right),Y:(double)Vector3.Dot(q,vup),Z:(double)Vector3.Dot(q,view))).ToArray();
        double minX=proj.Min(p=>p.X),maxX=proj.Max(p=>p.X),minY=proj.Min(p=>p.Y),maxY=proj.Max(p=>p.Y);double sc=Math.Min((w-20)/(maxX-minX),(h-34)/(maxY-minY));
        var tris=new List<(double Z,int T)>();
        for(int t=0;t+2<copy.Triangles.Length;t+=3)
        {
            int a=copy.Triangles[t],b=copy.Triangles[t+1],c=copy.Triangles[t+2];if(Area(pts[a],pts[b],pts[c])<1e-12)continue;
            double z=(proj[a].Z+proj[b].Z+proj[c].Z)/3;if(only!=null&&!only((copy.Vertices[a]+copy.Vertices[b]+copy.Vertices[c])/3))continue;
            tris.Add((z,t));
        }
        panel.Rect(x0,y0,w,h,"#0d1115");
        foreach(var (_,t) in tris.OrderByDescending(x=>x.Z))
        {
            int a=copy.Triangles[t],b=copy.Triangles[t+1],c=copy.Triangles[t+2];
            var n=Vector3.Cross(pts[b]-pts[a],pts[c]-pts[a]);n=Vector3.Normalize(n);
            double d=(copy.Uv[a].Y+copy.Uv[b].Y+copy.Uv[c].Y)/3;uint colour=strip[Math.Clamp((int)(d*CassetteDepthStrip.Height),0,CassetteDepthStrip.Height-1)];
            double shade=.45+.55*Math.Abs(Vector3.Dot(n,light));int R=(int)((colour>>24&0xff)*shade),G=(int)((colour>>16&0xff)*shade),B=(int)((colour>>8&0xff)*shade);var hex=$"#{R:x2}{G:x2}{B:x2}";
            panel.Poly(new[]{a,b,c}.Select(i=>(x0+10+(proj[i].X-minX)*sc,y0+h-10-(proj[i].Y-minY)*sc)),hex,hex,.6);
        }
        if(tris.Count==0)panel.Text(x0+w/2,y0+h/2,"nothing drawn","#8a8f96","MiddleCenter");
        panel.Text(x0+8,y0+12,caption,"#e8e2d4","MiddleLeft","#0d1115cc",8.5);
    }

    internal static void Run(Action<bool,string> sink)
    {
        int checks=0,failures=0;void Check(bool ok,string message){checks++;if(!ok)failures++;sink(ok,"FEAT83: "+message);}
        string root=Fix02228Tests.SourceRoot(),output=Fix02228Tests.OutputRoot(),parent=Fix02228Tests.SessionRoot(),preview=Path.Combine(output,"preview");
        Directory.CreateDirectory(preview);
        var materials=CassetteTechnology.ReadDirectory(Path.Combine(AppContext.BaseDirectory,"NativeData"));
        var catalogueText=File.ReadAllText(Path.Combine(AppContext.BaseDirectory,CassetteCatalogue.FileName));var presets=CassetteCatalogue.Parse(catalogueText).Presets;
        var userText=File.ReadAllText(Path.Combine(root,"tests/fixtures/object292-recipe.cassette.json"));var userPreset=CassetteCatalogue.Parse(userText).Presets.Single();
        var t80u=presets.First(p=>p.Id=="t80u");var burlington=presets.First(p=>p.Id=="burlington");
        var cases=new StringBuilder("plate\trecipe\tmaterial shown\tdepth\tstrip colour\tclosed\toverlay triangles\tnot drawn (off the drawn surface)\ton the surface (drawn)\toff the surface, outside the shape (not drawn)\tdrawn surface area m2\tarea of the surface triangles drawn m2\tcolours drawn\n");
        var panels=new List<Panel>();
        try
        {
            // ================= 1. the colour: the band of the thickest solid material =================
            foreach(var (tag,preset) in new[]{("T-80U",t80u),("Burlington",burlington),("Object 292 (user recipe)",userPreset)})
            {
                var strip=CassetteDepthStrip.Build(preset);var depth=SolidBlockOverlay.Depth(preset);var material=SolidBlockOverlay.Material(preset);
                var bounds=CassetteDepthStrip.Boundaries(preset);int i=SolidBlockOverlay.ThickestLayer(preset);
                var thickest=preset.Layers.Where(l=>l.Material!="air").OrderByDescending(l=>l.ThicknessMm/Math.Cos(l.TiltDegrees*Math.PI/180)).First();
                int texel=(int)(depth*CassetteDepthStrip.Height);
                Check(material==thickest.Material&&preset.Layers[i]==thickest,$"{tag}: the colour is the thickest solid layer of the recipe ({material}, {F(thickest.ThicknessMm,"0.#")} mm), as the proposal on the 0.22.82 contact sheet");
                Check(depth>bounds[i]&&depth<bounds[i+1]&&strip[texel]==CassetteDepthStrip.Colour(material)&&Math.Abs(texel-bounds[i]*CassetteDepthStrip.Height)>2&&Math.Abs(texel-bounds[i+1]*CassetteDepthStrip.Height)>2,$"{tag}: depth {F(depth,"0.####")} is the middle of its band {F(bounds[i],"0.####")}-{F(bounds[i+1],"0.####")}; the strip texel there is {CassetteDepthStrip.Colour(material):x8} (the colour of {material}), away from the dark band lines");
            }
            Check(SolidBlockOverlay.Depth(null)==.5f&&SolidBlockOverlay.Material(null)=="rha","no recipe loaded: the middle of the default strip (rha), as the native copy without a preset");

            // ================= 2. the overlay of a Solid block: T-80U proof box, Challenger box, Object 292 after Thickness only 5 mm + Solid block =================
            using var proof77=JsonDocument.Parse(File.ReadAllText(Path.Combine(root,"tests/fixtures/solid-block/Solid block proof.blueprint")));
            using var challenger=JsonDocument.Parse(File.ReadAllText(Path.Combine(root,"tests/fixtures/solid-block/Solid block proof - Challenger 1 Mk3.blueprint")));
            using var bp292=JsonDocument.Parse(File.ReadAllText(Path.Combine(root,"tests/fixtures/Object-292-1721.blueprint")));
            using var bp293=JsonDocument.Parse(File.ReadAllText(Path.Combine(root,"tests/fixtures/Object-293-2038.blueprint")));
            var (t80Json,t80Name)=Mesh(proof77.RootElement,4);var (boxJson,boxName)=Mesh(challenger.RootElement,4);var (json292,name292)=Mesh(bp292.RootElement,22);
            var thin292=Thin(json292,5);
            var objects292=bp292.RootElement.GetProperty("objects").EnumerateArray().ToDictionary(o=>o.GetProperty("vuid").GetInt32(),o=>o);var (rot292,pos292)=World(objects292,objects292[323]);
            Check(t80Name==CompositePlate.SolidBlockName&&boxName==CompositePlate.SolidBlockName,"the two proof boxes carry the mark");
            var solids=new (string Tag,string Json,CassettePreset Preset,Func<Vector3,Vector3> World)[]{("T-80U proof box (mesh 4)",t80Json,t80u,p=>p),("Challenger 1 box (mesh 4)",boxJson,burlington,p=>p),("Object 292 mesh 22, Thickness only 5 mm + Solid block",thin292,userPreset,p=>Mul(rot292,p)+pos292)};
            var drawn=new Dictionary<string,(OverlayCopy Before,OverlayCopy After)>();
            foreach(var (tag,json,preset,world) in solids)
            {
                var s=StandIn(json);var data=CassetteNativeOverlay.Copy(s.Front,s.Back,s.Sides,preset);
                var report=new SolidBlockOverlay.Report();var watch=System.Diagnostics.Stopwatch.StartNew();
                var result=Overlay83(CompositePlate.SolidBlockName,data,json,preset,false,report)!;watch.Stop();
                var painted=Overlay83(CompositePlate.SolidBlockName,data,json,preset,true)!;
                Console.WriteLine($"FEAT83 timing: {tag}: {data.Triangles.Length/3} triangles, {watch.ElapsedMilliseconds} ms");
                drawn[tag]=(data,result);
                var strip=CassetteDepthStrip.Build(preset);var colour=CassetteDepthStrip.Colour(SolidBlockOverlay.Material(preset));
                var (p,faces)=Read(json);var shape=SolidShape.FromJson(json);
                Check(shape.Closed&&report.Closed,$"{tag}: a closed shape ({shape.Pieces} piece(s), {F(shape.VolumeM3*1000,"0.#")} l)");
                Check(Hash(result)==Hash(painted),$"{tag}: the same block in both looks of the layer view (layers and painted): the look of a plate in layers does not apply");
                Check(result.Vertices.SequenceEqual(data.Vertices)&&result.Normals.SequenceEqual(data.Normals)&&result.Triangles.Length==data.Triangles.Length&&result.FallbackTriangles==data.FallbackTriangles&&result.SideArea==data.SideArea,$"{tag}: points, normals (the shading) and the number of triangles are the native copy's ({data.Triangles.Length/3}); the coverage test of the native renderers sees the same count");
                var colours=new HashSet<uint>();
                for(int t=0;t+2<result.Triangles.Length;t+=3){int a=result.Triangles[t],b=result.Triangles[t+1],c=result.Triangles[t+2];if(a==b&&b==c)continue;foreach(var i in new[]{a,b,c})colours.Add(strip[Math.Clamp((int)(result.Uv[i].Y*CassetteDepthStrip.Height),0,CassetteDepthStrip.Height-1)]);}
                Check(result.Uv.All(u=>u==new Vector2(.5f,report.Depth))&&colours.Count==1&&colours.Single()==colour,$"{tag}: every point at the same depth {F(report.Depth,"0.####")}: one colour ({colour:x8}, {report.Material}); no stripes (the 0.22.82 copy used {data.Uv.Select(u=>u.Y).Distinct().Count()} depths)");
                // the inside and the surface, independently
                int hidden=0,surfaceTris=0,outside=0,inside=0,wrongHidden=0,wrongDrawn=0;double surfaceArea=0,drawnSurfaceArea=0,backs=0,backsHidden=0;
                foreach(var f in faces)for(int j=1;j+1<f.Length;j++)surfaceArea+=Area(p[f[0]],p[f[j]],p[f[j+1]]);
                for(int t=0;t+2<data.Triangles.Length;t+=3)
                {
                    int a=data.Triangles[t],b=data.Triangles[t+1],c=data.Triangles[t+2];var centre=(data.Vertices[a]+data.Vertices[b]+data.Vertices[c])/3;
                    bool gone=result.Triangles[t]==result.Triangles[t+1]&&result.Triangles[t+1]==result.Triangles[t+2]&&result.Triangles[t]==a;
                    double distance=SurfaceDistance(centre,p,faces);bool on=distance<=SolidBlockOverlay.SurfaceM;bool within=!on&&InsideIndependent(centre,p,faces);
                    bool back=data.Uv[a].Y==1&&data.Uv[b].Y==1&&data.Uv[c].Y==1;if(back){backs++;if(gone)backsHidden++;}
                    if(gone)hidden++;if(on)surfaceTris++;else if(within)inside++;else outside++;
                    if(gone&&on)wrongHidden++;if(!gone&&!on)wrongDrawn++;
                    if(on&&!gone)drawnSurfaceArea+=Area(data.Vertices[a],data.Vertices[b],data.Vertices[c]);
                }
                Check(wrongDrawn==0&&hidden==inside+outside,$"{tag}: no triangle off the drawn surface is drawn: the inside of the skin ({inside} triangles inside the shape) and a skin outside the shape ({outside}) are not drawn");
                Check(wrongHidden==0&&hidden==report.Hidden&&report.Inside==inside&&report.Outside==outside&&report.Surface==surfaceTris,$"{tag}: every triangle on the drawn surface ({surfaceTris}) is drawn; the log figures match an independent count (inside {report.Inside}, outside {report.Outside})");
                Check(drawnSurfaceArea>=surfaceArea*(1-1e-4),$"{tag}: the drawn surface is covered: {F(drawnSurfaceArea,"0.#####")} m² of triangles on it are drawn, the drawn faces have {F(surfaceArea,"0.#####")} m²");
                if(tag.StartsWith("Object 292",StringComparison.Ordinal))Check(backs>0&&backsHidden>=backs*.95&&outside>0,$"{tag}: the backs of the 5 mm skin are not drawn ({F(backsHidden,"0")} of {F(backs,"0")}; the other {F(backs-backsHidden,"0")} lie on a drawn face within 0.6 mm and are part of the surface); where a face winds the other way (the {shape.ReversedFaces} faces turned for the volume) the game's skin lies outside the drawn shape: those {outside} triangles (a 5 mm step with its walls) are not drawn either, the block shows the drawn shape");
                else Check(backs>0&&backsHidden==backs&&outside==0,$"{tag}: every back triangle of the 5 mm skin is not drawn ({F(backsHidden,"0")} of {F(backs,"0")}); nothing lies outside the box; the side walls of the skin lie on the neighbouring faces and are drawn there in the same colour");
                cases.AppendLine(string.Join("\t",tag,preset.Id,report.Material,F(report.Depth,"0.####"),colour.ToString("x8"),report.Closed,report.Triangles,report.Hidden,surfaceTris,outside,F(surfaceArea,"0.######"),F(drawnSurfaceArea,"0.######"),colours.Count));
            }
            // a marked plate that is not closed (Object 293 20:38, two gaps of 7 edges): one colour, nothing left out
            {
                var (json293,_)=Mesh(bp293.RootElement,2);var s=StandIn(json293);var data=CassetteNativeOverlay.Copy(s.Front,s.Back,s.Sides,userPreset);var report=new SolidBlockOverlay.Report();
                var result=Overlay83(CompositePlate.SolidBlockName,data,json293,userPreset,false,report)!;
                Check(!report.Closed&&report.Hidden==0&&result.Triangles.SequenceEqual(data.Triangles)&&result.Uv.All(u=>u.Y==report.Depth)&&report.Refusal!.Contains("14 open edges",StringComparison.Ordinal),"a marked plate that is not closed (Object 293 20:38): one colour, every triangle drawn (inside is not defined); the reason is in the log line");
                var unread=SolidBlockOverlay.Apply(data,null,userPreset,report);
                Check(unread.Triangles.SequenceEqual(data.Triangles)&&unread.Uv.All(u=>u.Y==SolidBlockOverlay.Depth(userPreset))&&!report.Closed,"a marked plate whose mesh cannot be read at that moment: the colour without the inside test (the view is cached on the signature with the mark)");
                cases.AppendLine(string.Join("\t","Object 293 mesh 2 (20:38, not closed), marked",userPreset.Id,report.Material,F(report.Depth,"0.####"),CassetteDepthStrip.Colour(report.Material).ToString("x8"),false,data.Triangles.Length/3,0,"-","-","-","-",1));
            }

            // ================= 3. only the mark changes: a new signature and one rebuild (herpak: the comparisons with src-0.22.82 and its build are removed) =================
            {
                var queue=new OverlayBuildQueue<int>();var cache=new Dictionary<int,string>();int builds=0;float now=0;var preset=JsonSerializer.Serialize(t80u);
                string Sig(string? name)=>OverlayContent.Signature("mesh-content-A","ebCassette_t80u",preset)+(CompositePlate.IsSolidBlock(name)?SolidBlockOverlay.Mark:"");
                string current="";int Step(string? name,int polls=3){current=Sig(name);int before=builds;for(int k=0;k<polls;k++){queue.Schedule(1,1,now,0);now+=1.1f;foreach(var job in queue.TakeDue(now,8)){if(cache.TryGetValue(1,out var old)&&OverlayContent.Same(old,current))continue;cache[1]=current;builds++;}}return builds-before;}
                var steps=new (string Label,string? Name,int Expected)[]{("first draw, no mark",CompositePlate.Name,1),("polls, no change",CompositePlate.Name,0),("Apply Solid block (only the mark)",CompositePlate.SolidBlockName,1),("polls",CompositePlate.SolidBlockName,0),("Undo (mark off)",CompositePlate.Name,1),("Redo (mark on)",CompositePlate.SolidBlockName,1),("a saved-front suffix on the mark",CompositePlate.SolidBlockName+CompositePlate.FrontMarker+"x]",0),("Thickness only (mark off)",CompositePlate.Name,1),("polls",CompositePlate.Name,0)};
                var log=new List<string>();
                foreach(var (label,name,expected) in steps){int got=Step(name);log.Add($"{label}: {got}");Check(got==expected,$"overlay cache: {label} -> {got} rebuild(s) (expected {expected})");}
                Check(Sig(CompositePlate.SolidBlockName)!=Sig(CompositePlate.Name)&&Sig(CompositePlate.SolidBlockName).EndsWith("|[Solid block]",StringComparison.Ordinal),"with the mark the signature ends in |[Solid block]");
                File.WriteAllText(Path.Combine(output,"SOLID-OVERLAY-REBUILDS.txt"),string.Join("\n",log)+"\n");
                var fill=File.ReadAllText(Path.Combine(root,"RuntimeComposerFill.cs"));
                var overlay=fill[fill.IndexOf("internal static OverlayCopy? Overlay(PlateStructure plate,OverlayCopy data)",StringComparison.Ordinal)..];overlay=overlay[..overlay.IndexOf("    /// <summary>0.22.68: one file per plate",StringComparison.Ordinal)];
                Check(overlay.Contains("        try\r\n        {\r\n            if(SolidOverlay(plate,data) is {} solid)return solid; // 0.22.83 solid overlay\r\n            if(CompositePlate.LayoutFromName(MeshName(plate)) is not {} kind)return null;",StringComparison.Ordinal),"Overlay: the solid overlay is asked first, then the unchanged body of 0.22.82");
                var region=fill[fill.IndexOf(FillBegin,StringComparison.Ordinal)..fill.IndexOf(FillEnd,StringComparison.Ordinal)];
                Check(region.Contains("    private static OverlayCopy? SolidOverlay(PlateStructure plate,OverlayCopy data)\r\n    {\r\n        if(!CompositePlate.IsSolidBlock(MeshName(plate)))return null;",StringComparison.Ordinal),"SolidOverlay returns null for a plate without the mark before it reads anything else: such a plate goes on exactly as in 0.22.82");
                Check(!region.Contains("[HarmonyPatch",StringComparison.Ordinal)&&!File.ReadAllText(Path.Combine(root,"SolidBlockOverlay.cs")).Contains("[HarmonyPatch",StringComparison.Ordinal)&&!File.ReadAllText(Path.Combine(root,"SolidBlockOverlay.cs")).Contains("UnityEngine",StringComparison.Ordinal),"no new hook: SolidBlockOverlay.cs is pure code, the region has no [HarmonyPatch]");
                Check(!region.Contains("RaiseDirtyFlags",StringComparison.Ordinal)&&!region.Contains("SetMass",StringComparison.Ordinal)&&!System.Text.RegularExpressions.Regex.IsMatch(region,@"plate\.\w+\s*=(?!=)"),"the solid overlay writes nothing to the plate (only reads: name, mesh, recipe)");
            }

            // ================= 4. (herpak) plates without the mark against the 0.22.82 build, bit for bit: removed (process check against an earlier build) =================

            // ================= 5. the contact sheet =================
            {
                var t80=drawn["T-80U proof box (mesh 4)"];var box=drawn["Challenger 1 box (mesh 4)"];var o292=drawn["Object 292 mesh 22, Thickness only 5 mm + Solid block"];
                var p1=new Panel{title="Challenger 1 box (proof-challenger1, mesh 4, Burlington, 800 x 550 x 600 mm, 5 mm skin): Ctrl+L before and now",explanation="0.22.82: the native copy (front and back of every 5 mm skin in the recipe's first and last layer colour). 0.22.83: one block in the colour of the thickest solid layer, shaded by the normals. Right: only the triangles off the drawn surface (the inside of the skin).",width=980,height=330};
                var (bp,bf)=Read(boxJson);bool OffBox(Vector3 c)=>SurfaceDistance(c,bp,bf)>SolidBlockOverlay.SurfaceM;
                Draw(p1,box.Before,CassetteDepthStrip.Build(burlington),x=>x,0,0,320,330,"0.22.82 (stripes, thin skins)");
                Draw(p1,box.After,CassetteDepthStrip.Build(burlington),x=>x,330,0,320,330,"0.22.83: one block ("+SolidBlockOverlay.Material(burlington)+")");
                Draw(p1,box.Before,CassetteDepthStrip.Build(burlington),x=>x,660,0,155,330,"0.22.82: off the surface",only:OffBox);
                Draw(p1,box.After,CassetteDepthStrip.Build(burlington),x=>x,820,0,160,330,"0.22.83: off the surface",only:OffBox);
                panels.Add(p1);
                var p2=new Panel{title="Object 292 mesh 22 after Thickness only 5 mm + Solid block (user recipe): Ctrl+L before and now",explanation=$"Left: 0.22.82. Right: 0.22.83 in the colour of {SolidBlockOverlay.Material(userPreset)} (the thickest solid layer of the recipe). Where a face of the drawn mesh winds the other way, the game's 5 mm skin lies outside the shape (a 5 mm step with dark walls); 0.22.83 draws only the drawn surface. Below: only the triangles off the drawn surface.",width=980,height=720};
                var (op,of)=Read(thin292);bool Off292(Vector3 c)=>SurfaceDistance(c,op,of)>SolidBlockOverlay.SurfaceM;
                Draw(p2,o292.Before,CassetteDepthStrip.Build(userPreset),p=>Mul(rot292,p)+pos292,0,0,485,400,"0.22.82");
                Draw(p2,o292.After,CassetteDepthStrip.Build(userPreset),p=>Mul(rot292,p)+pos292,495,0,485,400,"0.22.83: one block ("+SolidBlockOverlay.Material(userPreset)+")");
                Draw(p2,o292.Before,CassetteDepthStrip.Build(userPreset),p=>Mul(rot292,p)+pos292,0,410,485,310,"0.22.82: off the drawn surface (inside skin and outside steps)",only:Off292);
                Draw(p2,o292.After,CassetteDepthStrip.Build(userPreset),p=>Mul(rot292,p)+pos292,495,410,485,310,"0.22.83: off the drawn surface",only:Off292);
                panels.Add(p2);
                var p3=new Panel{title="T-80U proof box (out-0.22.77 'Solid block proof', mesh 4, 600 x 400 x 500 mm): Ctrl+L before and now",explanation="One colour (the thickest solid layer of T-80U: rha, 135 mm), no thin skin, no stripes; right: only the triangles off the drawn surface (none).",width=980,height=300};
                var (tp,tf)=Read(t80Json);bool OffT80(Vector3 c)=>SurfaceDistance(c,tp,tf)>SolidBlockOverlay.SurfaceM;
                Draw(p3,t80.Before,CassetteDepthStrip.Build(t80u),x=>x,0,0,320,300,"0.22.82");
                Draw(p3,t80.After,CassetteDepthStrip.Build(t80u),x=>x,330,0,320,300,"0.22.83: one block ("+SolidBlockOverlay.Material(t80u)+")");
                Draw(p3,t80.After,CassetteDepthStrip.Build(t80u),x=>x,660,0,320,300,"0.22.83: off the drawn surface",only:OffT80);
                panels.Add(p3);
                // for comparison: a plate in layers, unchanged (Object 292 in layers, outer selection, as saved, T-80U)
                var layerJson=File.ReadAllText(Path.Combine(output,"user-outer-saved-t80u.json"));var ldata=DirectCopy(layerJson);
                var lname=CompositePlate.LayersName+CompositePlate.FrontMarker+"x]";
                var l83=Overlay83(lname,ldata,layerJson,t80u,false)!;var l83p=Overlay83(lname,ldata,layerJson,t80u,true)!;
                var p4=new Panel{title="For comparison: a plate in layers, unchanged (Object 292 mesh 22 in layers, outer selection, as saved, T-80U)",explanation=$"Plate in layers (layers {Hash(l83)}, painted {Hash(l83p)}): each face in the colour of its own layer.",width=980,height=400};
                Draw(p4,l83,CassetteDepthStrip.Build(t80u),p=>Mul(rot292,p)+pos292,0,0,485,400,"layers look (0.22.82 = 0.22.83)");
                Draw(p4,l83p,CassetteDepthStrip.Build(t80u),p=>Mul(rot292,p)+pos292,495,0,485,400,"painted look (0.22.82 = 0.22.83)");
                panels.Add(p4);
            }
        }
        catch(Exception error){Check(false,"FEAT83 threw "+error.GetType().Name+": "+error.Message+" "+error.StackTrace?.Split('\n').FirstOrDefault());}
        File.WriteAllText(Path.Combine(output,"SOLID-OVERLAY-CASES.tsv"),cases.ToString());
        File.WriteAllText(Path.Combine(preview,"solid-overlay-panels.json"),JsonSerializer.Serialize(panels,new JsonSerializerOptions{IncludeFields=true}));
        File.WriteAllText(Path.Combine(output,"FEAT83-RESULT.json"),JsonSerializer.Serialize(new{passed=failures==0,checks,failures},new JsonSerializerOptions{WriteIndented=true}));
        Console.WriteLine($"FEAT83 PASS: {checks} checks, {failures} failures");
    }
}
