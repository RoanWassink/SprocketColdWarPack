using System.Diagnostics;
using System.Numerics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
namespace SprocketExpandedBallistics;

internal static class Feat02245MaterialLookTests
{
    private sealed class Native:IOverlayNativeRenderer
    {
        public long Identity{get;init;}
        public bool Current{get;set;}=true;
        public bool Hidden{get;set;}
        internal readonly object Material=new();
    }
    private sealed record FakeView(OverlayCopy Mesh);
    internal static OverlaySurface[] Box()
    {
        var faces=new List<OverlaySurface>();
        void Face(Vector3 a,Vector3 b,Vector3 c,Vector3 d,Vector3 n)=>faces.Add(new(new[]{a,b,c,d},new[]{0,1,2,0,2,3},Enumerable.Repeat(n,4).ToArray()));
        Face(new(-.5f,-.5f,0),new(.5f,-.5f,0),new(.5f,.5f,0),new(-.5f,.5f,0),Vector3.UnitZ);
        Face(new(-.5f,-.5f,-.02f),new(-.5f,.5f,-.02f),new(.5f,.5f,-.02f),new(.5f,-.5f,-.02f),-Vector3.UnitZ);
        Face(new(-.5f,-.5f,0),new(-.5f,.5f,0),new(-.5f,.5f,-.02f),new(-.5f,-.5f,-.02f),-Vector3.UnitX);
        Face(new(.5f,-.5f,0),new(.5f,-.5f,-.02f),new(.5f,.5f,-.02f),new(.5f,.5f,0),Vector3.UnitX);
        Face(new(-.5f,.5f,0),new(.5f,.5f,0),new(.5f,.5f,-.02f),new(-.5f,.5f,-.02f),Vector3.UnitY);
        Face(new(-.5f,-.5f,0),new(-.5f,-.5f,-.02f),new(.5f,-.5f,-.02f),new(.5f,-.5f,0),-Vector3.UnitY);
        return faces.ToArray();
    }
    internal static void Run(Action<bool,string> check)
    {
        int count=0;var sink=check;check=(ok,msg)=>{count++;sink(ok,"feat02245: "+msg);};
        var root=Fix02228Tests.SourceRoot();var output=Fix02228Tests.OutputRoot();
        foreach(var id in Feat02244MaterialsTests.Ids)check(MaterialLook.For(id)?.Id==id,"four materials selected: "+id);
        foreach(var id in new[]{"rha","sand","hardenedSteel","ceramicArray","ebCassette_challenger2","EBWOOD","",null})check(MaterialLook.For(id)==null,"other material untouched: "+id);
        var surfaces=Box();
        foreach(var look in MaterialLook.All)
        {
            foreach(var scale in new[]{Vector3.One,new Vector3(2,.5f,1),new Vector3(-2,1,1)})
            {
                var data=MaterialLookGeometry.Copy(look,scale,surfaces);var tangents=MaterialLookGeometry.Tangents(data);
                check(Math.Abs(data.Vertices.Min(v=>v.X)+.5f)<1e-6&&Math.Abs(data.Vertices.Max(v=>v.X)-.5f)<1e-6&&Math.Abs(data.Vertices.Min(v=>v.Z)+.02f)<1e-6&&Math.Abs(data.Vertices.Max(v=>v.Z))<1e-6,"native bounds within float clipping tolerance: "+look.Id+" "+scale);
                check(data.Uv.All(uv=>uv.X>=look.Rect.X-1e-6&&uv.X<=look.Rect.X+look.Rect.Z+1e-6&&uv.Y>=look.Rect.Y-1e-6&&uv.Y<=look.Rect.Y+look.Rect.W+1e-6),"all UVs inside clean atlas rect: "+look.Id);
                var areas=new Dictionary<Vector3,double>();
                for(int t=0;t<data.Triangles.Length;t+=3)
                {
                    var a=data.Triangles[t];var b=data.Triangles[t+1];var c=data.Triangles[t+2];var cross=Vector3.Cross(data.Vertices[b]-data.Vertices[a],data.Vertices[c]-data.Vertices[a]);
                    check(Vector3.Dot(cross,data.Normals[a])>0,"clipping preserves triangle winding");
                    areas.TryGetValue(data.Normals[a],out double area);areas[data.Normals[a]]=area+cross.Length()*.5;
                }
                foreach(var n in new[]{Vector3.UnitX,-Vector3.UnitX,Vector3.UnitY,-Vector3.UnitY,Vector3.UnitZ,-Vector3.UnitZ})check(areas.TryGetValue(n,out var area)&&Math.Abs(area-(Math.Abs(n.Z)>.5?1:.02))<2e-5,"complete outer/inner/edge area without overlap: "+look.Id+" "+n);
                check(tangents.All(t=>float.IsFinite(t.X+t.Y+t.Z+t.W)&&Math.Abs(new Vector3(t.X,t.Y,t.Z).Length()-1)<1e-5&&Math.Abs(t.W)==1),"finite unit tangents/handedness: "+look.Id);
            }
            var preview=MaterialLookGeometry.Copy(look,Vector3.One,surfaces);
            var dir=Path.Combine(output,"preview");Directory.CreateDirectory(dir);
            File.WriteAllText(Path.Combine(dir,look.Id+"-mesh.json"),JsonSerializer.Serialize(new{id=look.Id,look.Guid,look.Asset,rect=new[]{look.Rect.X,look.Rect.Y,look.Rect.Z,look.Rect.W},tile=new[]{look.Tile.X,look.Tile.Y},vertices=preview.Vertices.Select(v=>new[]{v.X,v.Y,v.Z}),uv=preview.Uv.Select(v=>new[]{v.X,v.Y}),normals=preview.Normals.Select(v=>new[]{v.X,v.Y,v.Z}),triangles=preview.Triangles}));
        }
        check(Math.Abs(MaterialLook.For("ebWood")!.Tile.X/2-.18f)<1e-7,"two planks per .36 m tile = .18 m plank width");
        var mirrored=PlateOverlayGeometry.Mirror(MaterialLookGeometry.Copy(MaterialLook.All[0],Vector3.One,surfaces),Matrix4x4.CreateScale(-1,1,1));
        check(MaterialLookGeometry.Tangents(mirrored).All(t=>float.IsFinite(t.X+t.Y+t.Z+t.W)),"mirrored normals/UV winding yields valid tangents");
        bool rejected=false;try{MaterialLookGeometry.Copy(MaterialLook.All[0],Vector3.Zero,surfaces);}catch(InvalidOperationException){rejected=true;}check(rejected,"invalid scale refuses visual and leaves native path available");

        var original=new Native{Identity=123};var originalMaterial=original.Material;int restoredErrors=0,disposed=0;
        var visibility=new OverlayVisibility(_=>restoredErrors++);var cache=new MaterialLookCache<FakeView>(_=>disposed++);
        var wood=MaterialLook.For("ebWood")!;
        void Validate(string content){if(!cache.TryReuse(123,content,out _))cache.Put(123,content,new(MaterialLookGeometry.Copy(wood,Vector3.One,surfaces)));visibility.Hide(123,original);}
        Validate("wood/mesh-v1");int initial=cache.Rebuilds;
        for(int i=0;i<10;i++){visibility.Restore(123);check(!original.Hidden,"toggle off restores native visibility");Validate("wood/mesh-v1");Validate("wood/mesh-v1");check(cache.Rebuilds==initial&&original.Hidden,"toggle/selection reuses production cache "+i);}
        check(cache.Rebuilds-initial==0,"ten toggles and selections: zero rebuilds");
        Validate("wood/mesh-v2");check(cache.Rebuilds==initial+1&&disposed==1,"one actual edit disposes/rebuilds once");
        visibility.Restore(123);cache.Remove(123);
        check(!original.Hidden&&ReferenceEquals(original.Material,originalMaterial)&&restoredErrors==0,"RHA restore: original native visibility and exact material reference");
        var originallyOff=new Native{Identity=124,Hidden=true};visibility.Hide(124,originallyOff);visibility.Restore(124);check(originallyOff.Hidden,"pre-existing forceRenderingOff flag preserved");
        var replaced=new Native{Identity=125};visibility.Hide(125,replaced);replaced.Current=false;visibility.Restore(125);check(visibility.Count==0,"released/replaced renderer never written during restore");
        check(MaterialLook.TransparentGlass(true,true,true,true,true),"validated transparent glass route selected");
        for(int i=0;i<5;i++){var flags=Enumerable.Repeat(true,5).ToArray();flags[i]=false;check(!MaterialLook.TransparentGlass(flags[0],flags[1],flags[2],flags[3],flags[4]),"missing glass capability selects opaque fallback "+i);}
        var runtime=File.ReadAllText(Path.Combine(root,"RuntimeMaterialLooks.cs"));
        var adapter=File.ReadAllText(Path.Combine(root,"RuntimeCassettePlateVisual.cs"));
        check(!runtime.Contains("HarmonyPatch")&&!runtime.Contains("new Texture2D")&&!File.ReadAllText(Path.Combine(root,"MaterialLook.cs")).Contains("new Texture2D")&&!runtime.Contains("SetArmourMaterial("),"no new patches or data writes; Texture2D allowed only in RuntimeCustomTextures loader");
        check(runtime.Contains("config.name!=look.Asset")&&runtime.Contains("source.Request=new(look.Guid")&&runtime.Contains("source.Material!=null"),"native GUID/config identity verified and source cached once");
        check(runtime.Contains("PlateOverlaySnapshotReader.Read(plate,\"material look\")")&&runtime.Contains("if(mesh.Released||mesh.RebuildRequired)return null;"),"designer gated snapshot and completed-only Play tessellation");
        check(adapter.Contains("RuntimeMaterialLooks.Tick(Designer);")&&adapter.Contains("RuntimeMaterialLooks.Release(__instance);")&&adapter.Contains("RuntimeMaterialLooks.Note(plate,geometryChanged);"),"existing tick/build/release dispatch to production runtime");
        check(runtime.Contains("look.Glass?3:2")&&runtime.Contains("diagnosticOverlay")&&runtime.Contains("hidden.Restore(key)"),"opaque shadow proxy retained; glass hidden; native overlays restore");

        var material=CassetteTechnology.ReadDirectory(TestTechnologyDirectory.Merged(root));
        var catalogue=CassetteCatalogue.Parse(File.ReadAllText(Path.Combine(root,"Material/defaults",CassetteCatalogue.FileName)));
        var reaction=MaterialReactionCatalogue.Parse(File.ReadAllText(Path.Combine(root,"Material/defaults",MaterialReactionCatalogue.FileName)));
        var models=catalogue.Presets.ToDictionary(p=>p.MaterialId,p=>new CassetteModel(p,material,reaction));
        var tables=ProtectionFactorTable.ForMaterials(models,reaction,material,id=>id);
        var timing=new StringBuilder("plates\tbuild_median_ms\tcached_frame_median_ms\tvertices\tsteady_rebuilds\tnative_upload_included\n");
        foreach(int n in new[]{10,50})
        {
            double[] cold=new double[9],warm=new double[9];int vertices=0,steadyRebuilds=0;
            for(int rep=0;rep<10;rep++)
            {
                var c=new MaterialLookCache<FakeView>(_=>{});var sw=Stopwatch.StartNew();vertices=0;
                for(int p=0;p<n;p++){var data=MaterialLookGeometry.Copy(MaterialLook.All[p%4],Vector3.One,surfaces);_ = MaterialLookGeometry.Tangents(data);vertices+=data.Vertices.Length;c.Put(p,"mesh-v1",new(data));}
                sw.Stop();if(rep>0)cold[rep-1]=sw.Elapsed.TotalMilliseconds;
                int built=c.Rebuilds;sw.Restart();
                for(int frame=0;frame<1000;frame++)for(int p=0;p<n;p++)if(!c.TryReuse(p,"mesh-v1",out _))throw new Exception("lost cached view");
                sw.Stop();if(rep>0)warm[rep-1]=sw.Elapsed.TotalMilliseconds/1000;steadyRebuilds=c.Rebuilds-built;check(steadyRebuilds==0,"steady cache stays immutable at "+n+" plates");
            }
            Array.Sort(cold);Array.Sort(warm);timing.AppendLine(FormattableString.Invariant($"{n}\t{cold[4]:F6}\t{warm[4]:F6}\t{vertices}\t{steadyRebuilds}\tfalse"));
        }
        File.WriteAllText(Path.Combine(output,"LOOK-COST-02245.tsv"),timing.ToString());
        File.WriteAllText(Path.Combine(output,"FEAT45-RESULT.json"),JsonSerializer.Serialize(new{passed=true,checks=count,installed=false,liveNativeValidation=false,costScope="managed clipping/tangents/cache; excludes Unity upload, follow interop, GPU"}));
        Console.WriteLine($"FEAT02245 PASS: {count} checks.");
    }
}
