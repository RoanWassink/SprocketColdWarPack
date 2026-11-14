using System.Diagnostics;
using System.Numerics;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace SprocketThermalSight.Tests;

internal static class BoxSightChecks
{
    internal static void Run(Action<bool,string> check)
    {
        string root=Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,"../../../../../"));
        string asset=Path.Combine(root,"package/BepInEx/plugins/SprocketThermalSight/assets/box-sight.obj");
        string checkPath=Path.Combine(root,"box-sight/MESH-CHECK.txt");
        string Hash(string path)=>Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))).ToLowerInvariant();
        using var manifest=JsonDocument.Parse(File.ReadAllText(checkPath));var m=manifest.RootElement;
        var obj=BoxSightObj.Read(asset);
        string[] surfaces={"paint","steel","dark","lens"};
        check(obj.PositionCount==m.GetProperty("positions").GetInt32(),"box source position count");
        check(obj.TexcoordCount==m.GetProperty("texcoords").GetInt32(),"box source UV count");
        check(obj.NormalCount==m.GetProperty("normals").GetInt32(),"box source normal count");
        for(int i=0;i<4;i++)check(obj.Triangles[i].Length/3==m.GetProperty("triangles").GetProperty(surfaces[i]).GetInt32(),"box triangles: "+surfaces[i]);
        check(obj.Triangles.Sum(t=>t.Length)/3==5034 && m.GetProperty("totalTriangles").GetInt32()==5034,"box total triangles");
        Vector3 V(JsonElement e)=>new(e[0].GetSingle(),e[1].GetSingle(),e[2].GetSingle());
        check(Vector3.Distance(obj.Min,V(m.GetProperty("boundsMin")))<.000002f
            && Vector3.Distance(obj.Max,V(m.GetProperty("boundsMax")))<.000002f,"box exact rotated bounds; scene excluded");
        check(obj.Vertices.Length==15102 && obj.Uv.Length==obj.Vertices.Length && obj.Normals.Length==obj.Vertices.Length,"box complete v/vt/vn tuples");
        check(obj.Triangles.SelectMany(t=>t).All(i=>i>=0 && i<obj.Vertices.Length),"box indices valid");
        check(obj.Vertices.Concat(obj.Normals).All(v=>float.IsFinite(v.X)&&float.IsFinite(v.Y)&&float.IsFinite(v.Z))
            && obj.Uv.All(v=>float.IsFinite(v.X)&&float.IsFinite(v.Y)),"box attributes finite");
        var lenses=obj.Triangles[3].Select(i=>obj.Vertices[i]).ToArray();
        check(lenses.All(v=>v.Z>0),"all three lenses face +Z");
        // The source front panel is tilted: its small upper lens is set back.
        foreach(var lens in new[]{(X:.085f,Y:.138722f,Z:.263302f,Radius:.049f), (X:-.085f,Y:.138722f,Z:.263302f,Radius:.034f), (X:0f,Y:.198722f,Z:.250549f,Radius:.014f)})
        {
            var points=lenses.Where(v=>lens.X==0 ? Math.Abs(v.X)<.02f : lens.X>0 ? v.X>.02f : v.X<-.02f).ToArray();
            var lo=points.Aggregate(Vector3.Min);var hi=points.Aggregate(Vector3.Max);
            check(Math.Abs((lo.X+hi.X)/2-lens.X)<.0001f && Math.Abs((lo.Y+hi.Y)/2-lens.Y)<.0001f
                && Math.Abs((hi.X-lo.X)/2-lens.Radius)<.0001f && Math.Abs((lo.Z+hi.Z)/2-lens.Z)<.0001f,"rotated lens centre and diameter: "+lens.X);
        }
        string[] lines=File.ReadAllLines(asset);
        void Reject(string[] changed,string message)
        {
            bool rejected=false;try{BoxSightObj.Parse(changed);}catch(InvalidDataException ex){rejected=ex.Message.Contains(message);}
            check(rejected,"box malformed data: "+message);
        }
        string[] Change(string prefix,string replacement)
        {var changed=(string[])lines.Clone();changed[Array.FindIndex(changed,l=>l.StartsWith(prefix))]=replacement;return changed;}
        Reject(Change("v ","v NaN 0 0"),"non-finite");
        Reject(Change("vt ","vt 0 Infinity"),"non-finite");
        Reject(Change("vn ","vn 0 0 NaN"),"non-finite");
        Reject(Change("f ","f 999999/1/1 1/1/1 1/1/1"),"index out of bounds");
        Reject(Change("f ","f 1/999999/1 1/1/1 1/1/1"),"index out of bounds");
        Reject(Change("f ","f 1/1/999999 1/1/1 1/1/1"),"index out of bounds");
        Reject(Change("f ","f 1/1/1 1/1/1 1/1/1 1/1/1"),"triangulated");
        Reject(Change("usemtl paint","usemtl roof"),"material order");
        Reject(Change("v ","v 100 0 0"),"bounds");
        Reject(lines.Where(l=>l!=lines.First(l=>l.StartsWith("f "))).ToArray(),"counts");
        foreach(var id in new[]{"t72StyleSight",BoxSightObj.Id})
        {check(RangefinderDevicePolicy.Kind(id)==RangefinderKind.ManualLaser,"integrated manual laser: "+id);check(!RangefinderDevicePolicy.IsStandalone(id),"sight supplies own laser: "+id);}
        check(RangefinderDevicePolicy.IsStandalone("laserRangefinderSight") && RangefinderDevicePolicy.IsStandalone("automaticLaserRangefinder"),"standalone lasers unchanged");

        string game=Environment.GetEnvironmentVariable("SPROCKET_GAME_DIR")??@"C:\Program Files (x86)\Steam\steamapps\common\Sprocket";
        string parts=Path.Combine(root,"package/Sprocket_Data/StreamingAssets/Parts");
        string nativeParts=Path.Combine(game,"Sprocket_Data/StreamingAssets/Parts");
        var actual=JsonNode.Parse(File.ReadAllText(Path.Combine(parts,"laserBoxSightPart.json")))!;
        var expected=JsonNode.Parse(File.ReadAllText(Path.Combine(nativeParts,"t72StyleSightPart.json")))!;
        expected["guid"]=BoxSightObj.Guid;expected["name"]=BoxSightObj.Id;
        foreach(var component in expected["components"]!.AsArray())
            if(component!["type"]!.GetValue<string>()=="sight")
            {component["fileID"]=BoxSightObj.Id;component["info"]!["objectiveLensPosition"]=new JsonArray(.085,.139,.263);component["info"]!["eyepiecePosition"]=new JsonArray(.085,.139,-.255);}
        using var a=JsonDocument.Parse(actual.ToJsonString());using var e=JsonDocument.Parse(expected.ToJsonString());
        check(Equivalent(a.RootElement,e.RootElement),"part equals installed T72 except authorized fields");
        check(System.Guid.TryParse(actual["guid"]!.GetValue<string>(),out var guid),"valid part GUID");
        int occurrences=0;
        foreach(var directory in new[]{parts,nativeParts})
            foreach(var file in Directory.GetFiles(directory,"*.json"))
            {using var part=JsonDocument.Parse(File.ReadAllText(file),new JsonDocumentOptions{AllowTrailingCommas=true,CommentHandling=JsonCommentHandling.Skip});if(part.RootElement.TryGetProperty("guid",out var g)&&System.Guid.TryParse(g.GetString(),out var value)&&value==guid)occurrences++;}
        int installed=Directory.GetFiles(nativeParts,"*.json").Count(file=>
        {
            using var part=JsonDocument.Parse(File.ReadAllText(file),new JsonDocumentOptions{AllowTrailingCommas=true,CommentHandling=JsonCommentHandling.Skip});
            if(!part.RootElement.TryGetProperty("guid",out var g) || !System.Guid.TryParse(g.GetString(),out var value) || value!=guid)return false;
            check(Equivalent(a.RootElement,part.RootElement),"installed box sight equals package part");return true;
        });
        check(installed<=1 && occurrences==1+installed,"GUID unique, allowing the identical installed package part");
        var sight=actual["components"]!.AsArray().Single(c=>c!["type"]!.GetValue<string>()=="sight")!;
        foreach(var key in new[]{"objectiveLensPosition","eyepiecePosition"})
        {
            var point=sight["info"]![key]!.AsArray().Select(n=>n!.GetValue<float>()).ToArray();
            check(point[0]>=obj.Min.X && point[0]<=obj.Max.X && point[1]>=obj.Min.Y && point[1]<=obj.Max.Y && point[2]>=obj.Min.Z && point[2]<=obj.Max.Z,"part point within model bounds: "+key);
        }
        check(Hash(asset)==m.GetProperty("sha256").GetString(),"packaged OBJ hash");
        check(Hash(Path.Combine(root,"box-sight/source/lrf down.obj"))==m.GetProperty("sourceSha256").GetString(),"current creator source hash");
        check(Hash(Path.Combine(root,"box-sight/convert_box_sight.py"))==m.GetProperty("converterSha256").GetString(),"current converter hash");
        // Re-run the real converter, verifying every delivered mesh output byte-for-byte.
        var outputs=new[]{asset,checkPath}.Concat(surfaces.Select(n=>Path.Combine(root,"box-sight/stl/"+n+".stl"))).ToDictionary(p=>p,Hash);
        var start=new ProcessStartInfo("py"){WorkingDirectory=root,UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true,RedirectStandardError=true};
        start.ArgumentList.Add("-I");start.ArgumentList.Add(Path.Combine(root,"box-sight/convert_box_sight.py"));
        using var process=Process.Start(start)!;string output=process.StandardOutput.ReadToEnd(),error=process.StandardError.ReadToEnd();process.WaitForExit();
        File.WriteAllText(Path.Combine(root,"box-sight/CONVERTER-CHECK.txt"),output+error);
        check(process.ExitCode==0 && outputs.All(k=>Hash(k.Key)==k.Value),"current converter reproduces package OBJ, manifest and four STLs");
    }
    private static bool Equivalent(JsonElement a,JsonElement b)
    {
        if(a.ValueKind!=b.ValueKind)return false;
        return a.ValueKind switch
        {
            JsonValueKind.Object=>a.EnumerateObject().Count()==b.EnumerateObject().Count()
                && a.EnumerateObject().All(p=>b.TryGetProperty(p.Name,out var v)&&Equivalent(p.Value,v)),
            JsonValueKind.Array=>a.GetArrayLength()==b.GetArrayLength()&&a.EnumerateArray().Zip(b.EnumerateArray()).All(p=>Equivalent(p.First,p.Second)),
            JsonValueKind.Number=>a.GetDouble()==b.GetDouble(),
            _=>a.GetRawText()==b.GetRawText()
        };
    }
}
