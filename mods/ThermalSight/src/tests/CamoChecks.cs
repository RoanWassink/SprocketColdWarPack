using System.Diagnostics;
using System.Numerics;
using System.Security.Cryptography;
using System.Text.Json;

namespace SprocketThermalSight.Tests;

internal static class CamoChecks
{
    internal static void Run(Action<bool,string> check)
    {
        string root=Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,"../../../../../"));
        string assets=Path.Combine(root,"package/BepInEx/plugins/SprocketThermalSight/assets");
        using var manifest=JsonDocument.Parse(File.ReadAllText(Path.Combine(root,"uv_tools/manifest.json")));
        string Hash(string path)=>Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))).ToLowerInvariant();
        foreach(var item in manifest.RootElement.EnumerateObject())
        {
            string file=Path.Combine(assets,item.Name);var layout=CamoSurfaces.Layout(item.Name);var data=PaintedObj.Read(file);
            check(Hash(file)==item.Value.GetProperty("output_sha256").GetString(),item.Name+" package hash");
            check(Hash(Path.Combine(root,"uv_tools/add_uv.py"))==item.Value.GetProperty("generator_sha256").GetString(),item.Name+" generator hash");
            check(Hash(Path.Combine(root,"uv_tools/original",item.Name))==item.Value.GetProperty("source_sha256").GetString(),item.Name+" original hash");
            check(data.PositionCount==layout.Positions,item.Name+" positions");
            check(data.Vertices.Length==data.Uv.Length && data.Vertices.Length==data.Normals.Length,item.Name+" complete tuples");
            check(data.Triangles.SelectMany(t=>t).OrderBy(i=>i).SequenceEqual(Enumerable.Range(0,data.Vertices.Length)),item.Name+" unique valid corner indices");
            var bounds=item.Value.GetProperty("bounds");
            Vector3 V(JsonElement e)=>new(e[0].GetSingle(),e[1].GetSingle(),e[2].GetSingle());
            check(Vector3.Distance(data.Vertices.Aggregate(Vector3.Min),V(bounds[0]))<.000001f
                && Vector3.Distance(data.Vertices.Aggregate(Vector3.Max),V(bounds[1]))<.000001f,item.Name+" original bounds");
            for(int i=0;i<layout.Surfaces.Length;i++)
            {
                var surface=layout.Surfaces[i];check(data.Triangles[i].Length==surface.Triangles*3,item.Name+" count "+surface.Name);
                check(surface.Paint==(surface.Name is "Paint" or "PaintLight" or "Mount" or "Tube"),item.Name+" paint contract "+surface.Name);
                for(int j=0;j<data.Triangles[i].Length;j+=3)
                {
                    int a=data.Triangles[i][j],b=data.Triangles[i][j+1],c=data.Triangles[i][j+2];
                    var geometric=Vector3.Normalize(Vector3.Cross(data.Vertices[b]-data.Vertices[a],data.Vertices[c]-data.Vertices[a]));
                    var ab=data.Uv[b]-data.Uv[a];var ac=data.Uv[c]-data.Uv[a];
                    check(ab.X*ac.Y-ab.Y*ac.X>1e-12f,item.Name+" no collapsed or mirrored UV triangle");
                    check(Vector3.Dot(geometric,data.Normals[a])>.9999f,item.Name+" face normal/winding");
                }
            }
            var lines=File.ReadAllLines(file);
            string[] Change(string prefix,string replacement)
            {var changed=lines.ToArray();changed[Array.FindIndex(changed,l=>l.StartsWith(prefix))]=replacement;return changed;}
            void Reject(IEnumerable<string> changed,string reason)
            {bool rejected=false;try{PaintedObj.Parse(changed,layout);}catch{rejected=true;}check(rejected,item.Name+" rejects "+reason);}
            Reject(Change("v ","v NaN 0 0"),"nonfinite position");
            Reject(Change("vt ","vt Infinity 0"),"nonfinite UV");
            Reject(Change("vt ","vt 101 0"),"UV range");
            Reject(Change("vn ","vn 0 0 0"),"invalid normal");
            Reject(Change("f ","f 1/1 2/2 3/3"),"missing normal tuple");
            Reject(Change("f ","f 0/1/1 2/2/1 3/3/1"),"zero index");
            Reject(Change("f ","f 1/999999/1 2/2/1 3/3/1"),"UV index");
            Reject(Change("f ","f 1/1/999999 2/2/1 3/3/1"),"normal index");
            Reject(Change("f ","f 1/1/1 2/2/1 3/3/1 4/4/1"),"nontriangle");
            Reject(Change("f ","usemtl GlassAccident"),"unknown surface");
            Reject(lines.Where((_,i)=>i!=Array.FindIndex(lines,l=>l.StartsWith("f "))),"missing face");
        }
        // 0.4.1: authored meshes (FCS, T72, IR) get metric housing UVs from the same generator.
        using var housing=JsonDocument.Parse(File.ReadAllText(Path.Combine(root,"uv_tools/manifest-housing.json")));
        var housingNames=housing.RootElement.EnumerateObject().Select(p=>p.Name).ToHashSet();
        check(housingNames.SetEquals(new[]{"fcs-console.obj","t72-style-sight.obj","ir-searchlight.obj"}),"housing UV manifest lists FCS, T72 and IR");
        foreach(var item in housing.RootElement.EnumerateObject())
        {
            string file=Path.Combine(assets,item.Name);
            check(Hash(file)==item.Value.GetProperty("output_sha256").GetString(),item.Name+" package hash");
            check(Hash(Path.Combine(root,"uv_tools/add_uv.py"))==item.Value.GetProperty("generator_sha256").GetString(),item.Name+" generator hash");
            check(Hash(Path.Combine(root,"uv_tools/original",item.Name))==item.Value.GetProperty("source_sha256").GetString(),item.Name+" original hash");
            var faces=File.ReadLines(file).Where(l=>l.StartsWith("f ")).ToArray();
            check(faces.Length==item.Value.GetProperty("triangles").GetInt32() && faces.All(f=>f.Split(' ',StringSplitOptions.RemoveEmptyEntries).Skip(1).All(t=>t.Split('/').Length==3 && t.Split('/').All(x=>x.Length>0))),item.Name+" every face has v/vt/vn");
            check(File.ReadLines(file).Where(l=>l.StartsWith("vt ")).All(l=>l.Split(' ',StringSplitOptions.RemoveEmptyEntries).Skip(1).All(x=>float.TryParse(x,System.Globalization.NumberStyles.Float,System.Globalization.CultureInfo.InvariantCulture,out var v)&&float.IsFinite(v)&&Math.Abs(v)<=100)),item.Name+" finite UVs in reader range");
        }
        // The audit table is the runtime contract: no glass or dark surface may be listed as paint.
        using var audit=JsonDocument.Parse(File.ReadAllText(Path.Combine(root,"uv_tools/camo-surfaces.json")));
        var auditedAssets=new HashSet<string>();var auditedParts=new HashSet<string>();
        var uvAssets=new HashSet<string>{"thermal-head.obj","thermal-head-vertical.obj","automatic-lrf.obj","lrf-concept.obj","optical-rangefinder.obj"};
        foreach(var row in audit.RootElement.EnumerateArray())
        {
            string part=row.GetProperty("part").GetString()!,asset=row.GetProperty("asset").GetString()!;
            auditedParts.Add(part);
            if(!asset.EndsWith(".obj"))continue;
            auditedAssets.Add(asset);
            var paint=row.GetProperty("paint").EnumerateArray().Select(e=>e.GetString()!).ToHashSet();
            var materials=File.ReadLines(Path.Combine(assets,asset))
                .Where(l=>l.StartsWith("usemtl ")||l.StartsWith("o ")||l.StartsWith("g "))
                .Select(l=>l[(l.IndexOf(' ')+1)..].Trim()).Distinct().ToArray();
            if(materials.Length==0)materials=new[]{"Paint"};
            check(paint.All(materials.Contains),part+" audit paint exists in OBJ");
            check(!row.GetProperty("fixed").EnumerateArray().Select(e=>e.GetString()!)
                .Any(name=>materials.Contains(name)&&paint.Contains(name)),part+" audit does not both paint and fix a surface");
            if(!uvAssets.Contains(asset))continue;
            var layout=CamoSurfaces.Layout(asset);
            check(paint.SetEquals(layout.Surfaces.Where(s=>s.Paint).Select(s=>s.Name)),part+" audit paint list");
            foreach(var name in row.GetProperty("fixed").EnumerateArray().Select(e=>e.GetString()!))
                if(layout.Surfaces.FirstOrDefault(s=>s.Name==name) is {} surface)check(!surface.Paint,part+" audit keeps "+name);
        }
        var packageParts=Directory.GetFiles(Path.Combine(root,"package/Sprocket_Data/StreamingAssets/Parts"),"*.json")
            .Select(f=>JsonDocument.Parse(File.ReadAllText(f)).RootElement.GetProperty("name").GetString()!).ToHashSet();
        check(packageParts.SetEquals(auditedParts.Where(p=>p.StartsWith("thermalSight")||p is "irSearchlight" or "laserBoxSight")),"audit covers every package part");
        check(auditedAssets.SetEquals(Directory.GetFiles(assets,"*.obj").Select(Path.GetFileName)!),"audit covers every package OBJ");
        var start=new ProcessStartInfo("py"){WorkingDirectory=root,UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true,RedirectStandardError=true};
        start.ArgumentList.Add("-I");start.ArgumentList.Add(Path.Combine(root,"uv_tools/add_uv.py"));start.ArgumentList.Add("--check");
        using var process=Process.Start(start)!;string output=process.StandardOutput.ReadToEnd(),error=process.StandardError.ReadToEnd();process.WaitForExit();
        File.WriteAllText(Path.Combine(root,"uv_tools/UV-CHECK.txt"),output+error);
        check(process.ExitCode==0,"UV script reproduces every OBJ and geometry/material/hash manifest");
    }
}
