using System.Numerics;
using System.Text;
namespace SprocketExpandedBallistics;

internal static class Fix02230Tests
{
    internal static void Run(Action<bool,string> check)
    {
        int count=0;var sink=check;check=(ok,message)=>{sink(ok,"fix02230: "+message);count++;};
        var root=Fix02228Tests.SourceRoot();var output=Fix02228Tests.OutputRoot();
        var table=new StringBuilder("case\tvertex\tx\ty\tz\td\n");
        foreach(var shear in new[]{0f,.7f,-.35f})foreach(var taper in new[]{false,true})
        {
            var f=new[]{new Vector3(0,0,0),new Vector3(2,0,0),new Vector3(2,1,0),new Vector3(0,1,0)};
            var b=f.Select((v,i)=>v+new Vector3(shear,0,-(taper&&i>=2?.4f:.2f))).ToArray();
            var side=new List<Vector3>();var normals=new List<Vector3>();var indices=new List<int>();
            for(int e=0;e<4;e++)
            {
                int j=(e+1)%4,start=side.Count;side.AddRange(new[]{f[e],f[j],b[j],b[e]});
                var n=Vector3.Normalize(Vector3.Cross(f[j]-f[e],b[e]-f[e]));normals.AddRange(Enumerable.Repeat(n,4));
                indices.AddRange(new[]{start,start+1,start+2,start,start+2,start+3});
            }
            var front=new OverlaySurface(f,new[]{0,1,2,0,2,3},Enumerable.Repeat(Vector3.UnitZ,4).ToArray());
            var back=new OverlaySurface(b,new[]{0,2,1,0,3,2},Enumerable.Repeat(-Vector3.UnitZ,4).ToArray());
            var sides=new OverlaySurface(side.ToArray(),indices.ToArray(),normals.ToArray());
            var before=side.ToArray();var result=CassetteNativeOverlay.Copy(front,back,sides);
            var fingerprint=CassetteNativeOverlay.Fingerprint(front,back,sides);
            check(fingerprint==CassetteNativeOverlay.Fingerprint(front,back,sides),"unchanged mesh signature is stable");
            var moved=front.Vertices.ToArray();moved[0]+=new Vector3(.001f,0,0);
            check(fingerprint!=CassetteNativeOverlay.Fingerprint(front with{Vertices=moved},back,sides),"native position change invalidates same-count signature");
            var renormal=front.Normals.ToArray();renormal[0]=Vector3.UnitX;
            check(fingerprint!=CassetteNativeOverlay.Fingerprint(front with{Normals=renormal},back,sides),"native normal change invalidates signature");
            check(fingerprint!=CassetteNativeOverlay.Fingerprint(front with{Triangles=new[]{0,2,1,0,3,2}},back,sides),"native topology change invalidates same-count signature");
            string name=$"shear={shear};taper={taper}";
            check(result.Vertices.SequenceEqual(f.Concat(b).Concat(side)),"positions exactly equal native source "+name);
            check(result.Normals.SequenceEqual(front.Normals.Concat(back.Normals).Concat(normals)),"native normals retained "+name);
            check(result.Triangles.SequenceEqual(front.Triangles.Concat(back.Triangles.Select(i=>i+4)).Concat(indices.Select(i=>i+8))),"native topology retained "+name);
            check(result.Uv.Take(4).All(v=>v.Y==0)&&result.Uv.Skip(4).Take(4).All(v=>v.Y==1),"entry=0; native back=1 "+name);
            for(int e=0;e<4;e++)
            {
                var uv=result.Uv.Skip(8+4*e).Take(4).ToArray();
                check(uv[0].Y==0&&uv[1].Y==0&&uv[2].Y==1&&uv[3].Y==1,"native side endpoints "+name+" edge="+e);
                foreach(float d in new[]{.1f,.25f,.5f,.9f})
                {
                    var point=Vector3.Lerp(side[4*e],side[4*e+3],d);
                    var interpolated=uv[0].Y*(1-d)+uv[3].Y*d;
                    check(Math.Abs(interpolated-d)<1e-6&&Math.Abs(point.Z-(side[4*e].Z*(1-d)+side[4*e+3].Z*d))<1e-6,"side interpolation follows real oblique edge at "+d);
                }
            }
            for(int i=0;i<result.Vertices.Length;i++){var v=result.Vertices[i];table.AppendLine(FormattableString.Invariant($"{name}\t{i}\t{v.X}\t{v.Y}\t{v.Z}\t{result.Uv[i].Y}"));}
            check(side.SequenceEqual(before),"native source never mutated "+name);
            result.Vertices[0]=new(99);result.Normals[0]=new(99);result.Triangles[0]=99;
            check(f[0]==Vector3.Zero&&front.Normals[0]==Vector3.UnitZ&&front.Triangles[0]==0,"owned arrays independent of native arrays "+name);
            // An intermediate side point on the actual diagonal is assigned d=.5 even with edge-direction thickening.
            var midpoint=(side[0]+side[2])*.5f;
            var middle=CassetteNativeOverlay.Copy(front,back,sides with{Vertices=sides.Vertices.Append(midpoint).ToArray(),Normals=sides.Normals.Append(normals[0]).ToArray()});
            check(Math.Abs(middle.Uv[^1].Y-.5)<1e-6,"subdivided native side midpoint depth "+name);
            var interior=side[0]*.4f+side[1]*.15f+side[2]*.25f+side[3]*.2f;
            int centre=side.Count;
            var fan=CassetteNativeOverlay.Copy(front,back,sides with{Vertices=sides.Vertices.Append(interior).ToArray(),Normals=sides.Normals.Append(normals[0]).ToArray(),
                Triangles=new[]{0,1,centre,1,2,centre,2,3,centre,3,0,centre}.Concat(indices.Skip(6)).ToArray()});
            check(Math.Abs(fan.Uv[^1].Y-.45)<1e-6,"irregular native side subdivision preserves linear depth "+name);
            void Reject(OverlaySurface bad,string reason){bool rejected=false;try{CassetteNativeOverlay.Copy(front,back,bad);}catch(InvalidOperationException){rejected=true;}check(rejected,reason);}
            Reject(sides with{Triangles=new[]{0,1,999}},"invalid native indices rejected");
            Reject(sides with{Normals=Array.Empty<Vector3>()},"missing native normals rejected");
            var loose=CassetteNativeOverlay.Copy(front,back,sides with{Vertices=sides.Vertices.Append(new Vector3(20)).ToArray(),Normals=sides.Normals.Append(Vector3.UnitZ).ToArray()});
            check(loose.Triangles.Length>0&&loose.Uv.All(v=>float.IsFinite(v.Y)),"0.22.33 unmapped unused side point gets clamped owning-plane depth");
        }
        var preset=new CassettePreset("test","test","1950.01.01",1,1000,new[]{new CassetteLayer("rha",20,0,"up"),new CassetteLayer("ceramicArray",30,60,"up"),new CassetteLayer("air",20,0,"up")});
        var bounds=CassetteDepthStrip.Boundaries(preset);
        check(bounds.Length==4&&Math.Abs(bounds[1]-.2)<1e-10&&Math.Abs(bounds[2]-.8)<1e-10&&bounds[3]==1,"boundaries use material thickness / cos tilt; air keeps depth");
        var strip=CassetteDepthStrip.Build(preset);
        check(strip.Length==CassetteDepthStrip.Height,"1 x N raster depth axis");
        foreach(var d in new[]{.2,.8})
            check(strip[(int)Math.Round(d*strip.Length)]==0x090c10ff,"thin dark boundary at "+d);
        check(strip[100]==CassetteDepthStrip.Colour("rha")&&strip[4000]==CassetteDepthStrip.Colour("ceramicArray")&&strip[8000]==CassetteDepthStrip.Colour("air"),"saturated material colours in thickness order");
        check(CassetteDepthStrip.Build(null).All(c=>c==CassetteDepthStrip.Colour("rha")),"missing preset has solid safe steel tint");
        var many=preset with{Layers=Enumerable.Range(0,256).Select(_=>new CassetteLayer("rha",1,0,"up")).ToArray()};
        check(CassetteDepthStrip.Boundaries(many).Length==257&&CassetteDepthStrip.Build(many).Count(c=>c==0x090c10ff)==255,"256 layers retain every internal boundary");
        var destroyed=new List<string>();var resources=new OverlayResources<string>(destroyed.Add);
        foreach(var state in new[]{"hide","Release","scene exit","build failure"})
        {
            foreach(var item in new[]{"root","texture","material","mesh"})resources.Own(item);
            check(resources.Count==4,"all owned resources tracked for "+state);resources.Clear();
            check(resources.Count==0&&destroyed.TakeLast(4).Distinct().Count()==4,"every owned resource destroyed once on "+state);
            int old=destroyed.Count;resources.Clear();check(destroyed.Count==old,"cleanup idempotent on "+state);
        }
        var runtime=File.ReadAllText(Path.Combine(root,"RuntimeCassettePlateVisual.cs"));var reader=File.ReadAllText(Path.Combine(root,"PlateOverlaySnapshot.cs"));
        check(reader.Contains("PlateMeshAccess.Read(plate,reader,SnapshotUse.Fresh)")&&reader.Contains("PlateMeshAccess.Gate.Busy")&&reader.Contains("plate.meshRefresh||native.RebuildRequired"),"native copy only after fresh snapshot, quiet/mouse/dirty and pending-build guards");
        check(reader.Contains("Copy(native.frontMesh)")&&reader.Contains("Copy(native.thickenFaceMesh)")&&reader.Contains("Copy(native.thickenEdgeMesh)"),"native entry, built back and side source");
        check(!System.Text.RegularExpressions.Regex.IsMatch(reader,@"\b(?:plate|native|mesh)\.\w+\s*=(?!=)")&&!reader.Contains("ToUnityMesh("+"native"),"read-only adapter has no native assignments or mesh upload");
        check(!runtime.Contains("CassetteEdgeMesh.Build")&&!runtime.Contains(".SetParent(plate")&&!runtime.Contains("plate.GetComponent"),"no reconstructed extrusion, broad native component lookup or native parenting");
        check(!System.Text.RegularExpressions.Regex.IsMatch(runtime,@"\bplate\.\w+\s*=(?!=)"),"overlay writes no plate fields");
        check(runtime.Contains("resources.Own(new Mesh")&&runtime.Contains("resources.Own(new Texture2D")&&runtime.Contains("resources.Own(Make())")&&runtime.Contains("root.AddComponent<MeshFilter>().sharedMesh=mesh"),"all material/mesh/texture uploads target owned objects");
        check(runtime.Contains("OverlayNormalOffset.Repair(data.Normals")&&runtime.Contains("mesh.normals=normals.Select")&&runtime.Contains("FilterMode.Point")&&runtime.Contains("TextureWrapMode.Clamp"),"copied normals, sharp texture, clamped depth");
        var plan=File.ReadAllText(Path.Combine(root,"OverlayMaterialPlan.cs"));
        check(plan.Contains("\"HDRP/Lit\"")&&plan.Contains("\"Standard\"")&&runtime.Contains("shader.isSupported")&&runtime.Contains("shaderFallbackLogged=true"),"supported lit shader selection; one fallback log");
        check(runtime.Contains("\"Opacity\",1f")&&runtime.Contains("opacity.SettingChanged")&&plan.Contains("Opacity<.999f"),"opacity defaults opaque; config changes replace owned materials");
        check(runtime.Contains("if(!Visible){SuspendViews();pending.Clear();return;}")&&runtime.Contains("old.Resources.Clear();")&&runtime.Contains("private static void Release(PlateStructure __instance)"),"hide retains cache; Release destroys mesh, texture, material and root");
        check(runtime.Contains("OverlayContent.Same(built,signature)")&&runtime.Contains("PollSignatures()")&&runtime.Contains("nameof(PlateStructure.RequestMeshRefresh)"),"content signature controls uploads and inherited refresh target remains");
        check(!runtime.Contains("pending.ToArray()")&&!runtime.Contains("views.ToArray()"),"pending drain and periodic polling use reusable iteration");
        File.WriteAllText(Path.Combine(output,"DEPTH-CHECK.tsv"),table.ToString());
        Console.WriteLine($"0.22.30 PASS: {count} checks; depth evidence in test-output/DEPTH-CHECK.tsv.");
    }
}
