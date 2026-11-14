using System.Numerics;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using SprocketReferenceImages;

string root=Path.GetFullPath(args[0]), game=args[1], dll=args[2];
int checks=0;
void Assert(bool ok,string label){checks++;if(!ok)throw new Exception("FAIL: "+label);Console.WriteLine("PASS "+label);}
void Throws(Action a,string label){bool threw=false;try{a();}catch(InvalidDataException){threw=true;}catch(OverflowException){threw=true;}Assert(threw,label);}
bool Near(float a,float b)=>Math.Abs(a-b)<1e-5f;
string Read(string p)=>File.ReadAllText(Path.Combine(root,p));
using var part=JsonDocument.Parse(Read("src/parts/referenceImagePart.json"));var p=part.RootElement;
Assert(p.GetProperty("guid").GetString()==BoardPolicy.PartGuid,"part GUID");
Assert(p.GetProperty("name").GetString()==BoardPolicy.PartName,"part name");
Assert(p.GetProperty("v").GetString()=="0.3" && new[]{"iconOffset","iconRotation","iconScale"}.All(x=>p.TryGetProperty(x,out _)),"v0.3 icon schema");
Assert(p.GetProperty("components").GetArrayLength()==1 && p.GetProperty("components")[0].GetProperty("type").GetString()=="model","model only");
var info=p.GetProperty("components")[0].GetProperty("info");
Assert(info.GetProperty("mass").GetInt32()==0 && !info.TryGetProperty("durableMaterialID",out _) && !info.GetProperty("placementSurface").GetBoolean(),"zero mass, no durable function");
Assert(info.GetProperty("scaling").GetBoolean() && p.GetProperty("transform").GetProperty("scaling").GetBoolean(),"native scaling enabled");
Assert(p.GetProperty("transform").GetProperty("minScale").EnumerateArray().All(x=>x.GetSingle()==.1f) && p.GetProperty("transform").GetProperty("maxScale").EnumerateArray().All(x=>x.GetSingle()==6),"native scale .1..6");
var native=Directory.GetFiles(Path.Combine(game,"Sprocket_Data/StreamingAssets/Parts"),"*.json");
var schema=new NativePartSchema(native,"referenceImagePart.json");
var schemaErrors=schema.Errors(p);
Assert(schemaErrors.Count==0,"exact native schema for every version block: "+string.Join("; ",schemaErrors));
using(var previous=JsonDocument.Parse(Read("tests/fixtures/referenceImagePart-0.1.0.json")))
{
    var oldErrors=schema.Errors(previous.RootElement);
    foreach(string error in oldErrors)Console.WriteLine("EXPECTED REJECTION 0.1.0: "+error);
    Assert(oldErrors.Count==1 && oldErrors[0].Contains("components[0].info") && oldErrors[0].Contains("closest native example:") && oldErrors[0].Contains(".json") && oldErrors[0].Contains("durableMaterialID") && oldErrors[0].Contains("damageable"),"old JSON fails native schema with closest native example and key differences");
    var expected=System.Text.Json.Nodes.JsonNode.Parse(previous.RootElement.GetRawText())!;
    expected["components"]![0]!["info"]!["v"]="0.2";
    expected["components"]![0]!["info"]!.AsObject().Remove("associatedComponentFileID");
    using var fixedPart=JsonDocument.Parse(Read("tests/fixtures/referenceImagePart-0.1.1.json"));
    Assert(System.Text.Json.Nodes.JsonNode.DeepEquals(expected,System.Text.Json.Nodes.JsonNode.Parse(fixedPart.RootElement.GetRawText())),"0.1.1 hotfix changes only info version and associatedComponentFileID");
}
using(var previous=JsonDocument.Parse(Read("tests/fixtures/referenceImagePart-0.1.1.json")))
{
    var expected=System.Text.Json.Nodes.JsonNode.Parse(previous.RootElement.GetRawText())!;
    expected["transform"]!["autoRotate"]=true;expected["transform"]!["placementMode"]="upright";
    Assert(System.Text.Json.Nodes.JsonNode.DeepEquals(expected,System.Text.Json.Nodes.JsonNode.Parse(p.GetRawText())),"0.1.2 JSON changes only upright/autoRotate; native transform key set retained");
    var example=JsonDocument.Parse(File.ReadAllText(Path.Combine(game,"Sprocket_Data/StreamingAssets/Parts/17pdrMuzzleBrakePart.json")));
    Assert(p.GetProperty("transform").EnumerateObject().Select(x=>x.Name).ToHashSet().SetEquals(example.RootElement.GetProperty("transform").EnumerateObject().Select(x=>x.Name)),"transform exact key set from native root v0.3 17pdrMuzzleBrakePart");
    Assert(p.GetProperty("transform").GetProperty("airPlacement").GetBoolean() && p.GetProperty("transform").GetProperty("rotation").GetBoolean(),"air placement and native rotation remain enabled");
}
// Exercise root validation and additional component blocks, independently of the hotfix fixture.
var changed=System.Text.Json.Nodes.JsonNode.Parse(p.GetRawText())!;
changed["unexpectedRootField"]=true;
using(var doc=JsonDocument.Parse(changed.ToJsonString()))
    Assert(schema.Errors(doc.RootElement).Any(x=>x.StartsWith("root:") && x.Contains("unexpectedRootField")),"unknown root key rejected");
changed.AsObject().Remove("unexpectedRootField");
changed["components"]!.AsArray().Add(changed["components"]![0]!.DeepClone());
changed["components"]![1]!["info"]!["unexpectedInfoField"]=true;
using(var doc=JsonDocument.Parse(changed.ToJsonString()))
    Assert(schema.Errors(doc.RootElement).Any(x=>x.StartsWith("components[1].info:") && x.Contains("unexpectedInfoField")),"every component info validated");
changed["components"]!.AsArray().RemoveAt(1);
changed["components"]![0]!["info"]!["v"]="999";
using(var doc=JsonDocument.Parse(changed.ToJsonString()))
    Assert(schema.Errors(doc.RootElement).Any(x=>x.Contains("block v999") && x.Contains("closest native example:")),"unknown info version rejected with nearest example");
var dates=new List<(string Date,string Name)>();
foreach(string path in native)
{
    string text=File.ReadAllText(path);
    var id=Regex.Match(text,"\"guid\"\\s*:\\s*\"([^\"]+)\"");
    // An existing installation of this same part is permitted; other files may not reuse its GUID.
    Assert(!id.Success || id.Groups[1].Value!=BoardPolicy.PartGuid || Path.GetFileName(path)=="referenceImagePart.json","GUID unique: "+Path.GetFileName(path));
    var date=Regex.Match(text,"\"date\"\\s*:\\s*\"(\\d{4}\\.\\d{2}\\.\\d{2})\"");
    if(date.Success)dates.Add((date.Groups[1].Value,Path.GetFileName(path)));
}
string earliest=dates.OrderBy(x=>x.Date,StringComparer.Ordinal).ThenBy(x=>x.Name,StringComparer.Ordinal).First().Date;
Assert(earliest==BoardPolicy.NativeDate && p.GetProperty("date").GetString()==earliest,"earliest native date "+earliest);
var xmlBytes=File.ReadAllBytes(Path.Combine(root,"src/localization/referenceImage.xml"));
Assert(xmlBytes[0]==255&&xmlBytes[1]==254,"UTF-16 BOM");
var xml=XDocument.Load(Path.Combine(root,"src/localization/referenceImage.xml"));
Assert(xml.Root?.Name=="Part" && xml.Root.Element("name")?.Value=="Reference Image","native Part XML");
var assembly=Assembly.LoadFile(Path.GetFullPath(dll));
var meshType=assembly.GetType("SprocketReferenceImages.BoardMeshData",true)!;
foreach(string kind in new[]{"Frame","Image"})
{
    string name="reference-board-"+kind.ToLowerInvariant()+".stl";
    byte[] stl=File.ReadAllBytes(Path.Combine(root,"model",name));
    string hash=Convert.ToHexString(SHA256.HashData(stl));
    Assert((string)meshType.GetField(kind+"Sha256",BindingFlags.Static|BindingFlags.NonPublic)!.GetRawConstantValue()! == hash,"compiled STL hash "+kind);
    using var stream=assembly.GetManifestResourceStream(name)!;using var memory=new MemoryStream();stream.CopyTo(memory);
    Assert(stl.SequenceEqual(memory.ToArray()),"embedded exact STL "+kind);
    float[] actual=(float[])meshType.GetField(kind+"Vertices",BindingFlags.Static|BindingFlags.NonPublic)!.GetValue(null)!;
    var matches=Regex.Matches(System.Text.Encoding.ASCII.GetString(stl),@"vertex\s+(\S+)\s+(\S+)\s+(\S+)");
    var expected=matches.SelectMany(m=>new[]{float.Parse(m.Groups[1].Value,System.Globalization.CultureInfo.InvariantCulture),float.Parse(m.Groups[3].Value,System.Globalization.CultureInfo.InvariantCulture),-float.Parse(m.Groups[2].Value,System.Globalization.CultureInfo.InvariantCulture)}).ToArray();
    Assert(actual.SequenceEqual(expected),"compiled mapped vertices "+kind);
    float[] normals=(float[])meshType.GetField(kind+"Normals",BindingFlags.Static|BindingFlags.NonPublic)!.GetValue(null)!;
    Assert(actual.Length==normals.Length && actual.Length/9==(kind=="Frame"?32:12),"mesh triangles "+kind);
    int front=0,back=0;
    for(int i=0;i<actual.Length;i+=9)
    {
        var a=new Vector3(actual[i],actual[i+1],actual[i+2]);var b=new Vector3(actual[i+3],actual[i+4],actual[i+5]);var c=new Vector3(actual[i+6],actual[i+7],actual[i+8]);
        Assert(Vector3.Dot(Vector3.Cross(b-a,c-a),new Vector3(normals[i],normals[i+1],normals[i+2]))>0,"outward winding "+kind+" "+i/9);
        if(normals[i+2]>.9)front++;if(normals[i+2]<-.9)back++;
        if(kind=="Image" && Math.Abs(normals[i+2])>.9)
            Assert(new[]{a,b,c}.All(v=>(v.X+1)/2>=0&&(v.X+1)/2<=1&&(v.Y+1)/2>=0&&(v.Y+1)/2<=1),"projected UV bounds "+i/9);
    }
    Assert(front>0&&back>0,"two broad faces "+kind);
}
using(var icon=assembly.GetManifestResourceStream("reference-image-icon.png")!)
{using var buffer=new MemoryStream();icon.CopyTo(buffer);Assert(buffer.ToArray().SequenceEqual(File.ReadAllBytes(Path.Combine(root,"model/reference-image-icon.png"))),"embedded approved icon");}
for(int i=0;i<BoardPolicy.PictureVertices.Length;i+=9)
{
    var v=BoardPolicy.PictureVertices;
    var a=new Vector3(v[i],v[i+1],v[i+2]);var b=new Vector3(v[i+3],v[i+4],v[i+5]);var c=new Vector3(v[i+6],v[i+7],v[i+8]);
    var normal=Vector3.Normalize(Vector3.Cross(b-a,c-a));
    Assert(Near(normal.X,i<18?1:-1) && Near(normal.Y,0) && Near(normal.Z,0),"production picture quad outward normal +/-X "+i/9);
    Assert(new[]{a,b,c}.All(x=>Near(Math.Abs(x.X),1)),"picture has only two planar faces, no side faces");
}
foreach(float sx in new[]{.1f,6f,-.1f,-6f})
{
    float worldPicture=BoardPolicy.PictureDepth(sx)*Math.Abs(sx),worldBacking=BoardPolicy.HalfDepth*Math.Abs(sx);
    Assert(Near(worldPicture-worldBacking,.003f),"picture offset 3 mm world at depth scale "+sx);
    Assert(worldPicture>worldBacking+.0029f,"letterbox/native solid backing separated, no coplanar picture at "+sx);
}
foreach(string kind in new[]{"Frame","Image"})
{
    var actual=(float[])meshType.GetField(kind+"Vertices",BindingFlags.Static|BindingFlags.NonPublic)!.GetValue(null)!;
    float maxDepth=0,maxWidth=0,maxHeight=0;
    for(int i=0;i<actual.Length;i+=3)
    {
        var v=BoardPolicy.SidePlane(new Vector3(actual[i],actual[i+1],actual[i+2]));
        maxDepth=Math.Max(maxDepth,Math.Abs(v.X));maxWidth=Math.Max(maxWidth,Math.Abs(v.Z));maxHeight=Math.Max(maxHeight,Math.Abs(v.Y));
    }
    Assert(maxDepth<=BoardPolicy.HalfDepth+1e-6f && maxWidth>=1 && maxHeight>=1,"approved "+kind+" rotates into thin local YZ board");
}
foreach(var (sx,sy,aspect) in new[]{(.1f,.1f,1f),(6f,6f,1f),(1f,1f,16f/9),(2f,.5f,16f/9),(.5f,2f,.5f),(-2f,3f,4f/3),(6f,.1f,.1f)})
{
    var fit=BoardPolicy.Fit(sx,sy,aspect);
    Assert(Near(fit.Width/fit.Height,aspect),$"letterbox aspect {sx},{sy},{aspect}");
    Assert(fit.Width<=2*Math.Abs(sx)+1e-5 && fit.Height<=2*Math.Abs(sy)+1e-5 && (Near(fit.X,1)||Near(fit.Y,1)),"letterbox inside actual rectangle");
    Assert(Near(BoardPolicy.Fit(sx,fit.ExactHeight,aspect).X,1)&&Near(BoardPolicy.Fit(sx,fit.ExactHeight,aspect).Y,1),"height exact fit");
}
Throws(()=>BoardPolicy.Fit(0,1,1),"zero board refused");
var pose=new Pose(new(1,2,3),Quaternion.Identity,Vector3.One);var session=new SessionMemory();
Assert(session.Match(1,pose,out int zero)==null && zero==0,"session no candidate");
var item=session.Remember(1,null,pose,"side.png",true);
Assert(ReferenceEquals(session.Match(1,pose,out int one),item) && one==1,"session exact candidate");
Assert(session.Match(2,pose,out _)==null,"session vehicle isolation");
Assert(session.Match(1,pose with { Position=new(1.00005f,2,3) },out _)!=null,"session 1e-4 tolerance");
Assert(session.Match(1,pose with { Position=new(1.0002f,2,3) },out _)==null,"session beyond tolerance");
Assert(session.Match(1,pose with { Rotation=new(0,0,0,-1) },out _)!=null,"equivalent quaternion sign");
var second=session.Remember(1,null,pose,"front.jpg",false);
Assert(session.Match(1,pose,out int two)==null && two==2,"session ambiguity refused");
var moved=pose with { Position=new(2,2,3) };
session.Remember(1,item,moved,"side.png",false);
Assert(ReferenceEquals(session.Match(1,pose,out _),second)&&ReferenceEquals(session.Match(1,moved,out _),item),"session movement updates entry");
Assert(!item.Mirror,"mirror updated in RAM");
foreach(string name in new[]{"side.png","front.JPG","image space 1.jpg","tank.front.png"})Assert(BoardPolicy.SafeName(name),"safe name "+name);
foreach(string name in new[]{"../x.png","..\\x.png","C:\\x.png","/x.jpg","x:ads.png","x..png","CON.png","com9.jpg","x.png.","x.png ","a\n.png","x.jpeg","","a/b.png","\\server\\x.png"})Assert(!BoardPolicy.SafeName(name),"unsafe name rejected "+name.Replace('\n','?'));
BoardPolicy.Dimensions(8192,3906,8192);Assert(true,"dimension boundary under 32 Mpx");
Throws(()=>BoardPolicy.Dimensions(8192,8192,8192),"32 Mpx limit");Throws(()=>BoardPolicy.Dimensions(4097,1,4096),"GPU limit");Throws(()=>BoardPolicy.Dimensions(8193,1,16384),"8192 app limit");
var expectations=JsonDocument.Parse(Read("tests/fixtures/dib-expected.json")).RootElement;
foreach(var fixture in expectations.EnumerateArray())
{
    string file=fixture.GetProperty("file").GetString()!;byte[] bytes=File.ReadAllBytes(Path.Combine(root,"tests/fixtures",file));
    if(fixture.GetProperty("valid").GetBoolean())
    {
        var image=DibParser.Parse(bytes,8192);var rgba=Convert.FromHexString(fixture.GetProperty("rgba").GetString()!);
        Assert(image.Width==2&&image.Height==2&&image.Rgba.SequenceEqual(rgba),"DIB "+file);
    }
    else Throws(()=>DibParser.Parse(bytes,8192),"broken DIB "+file);
}
foreach(string path in Directory.GetFiles(Path.Combine(root,"src"),"*.cs",SearchOption.AllDirectories).Concat(Directory.GetFiles(Path.Combine(root,"tests"),"*.cs",SearchOption.AllDirectories)))
{
    string text=File.ReadAllText(path);
    Assert(!Regex.IsMatch(text,@"[\u00c2-\u00f4][\u0080-\u00bf\u0152\u0153\u0160\u0161\u0178\u017d\u017e\u0192\u02c6\u02dc\u2013-\u2022\u2026\u2030\u2039\u203a\u20ac\u2122]|\ufffd"),"mojibake "+Path.GetRelativePath(root,path));
}
var sources=Directory.GetFiles(Path.Combine(root,"src"),"*.cs",SearchOption.AllDirectories).Select(File.ReadAllText).ToArray();
Assert(sources.Sum(x=>Regex.Matches(x,@"\bharmony\.Patch\(").Count)==1,"exactly one explicit Harmony hook");
Assert(sources.All(x=>!Regex.IsMatch(x,@"\b(ToBlueprint|ApplyBaseObjectState|SaveData|LoadData|SerializationInfo|BlueprintReader|BlueprintFactory)\b")),"no save/load or blueprint path");
Assert(sources.All(x=>!Regex.IsMatch(x,@"(?:VehicleTransform|placement|\bt)\.(?:Scale|LocalPosition|LocalRotation|Position|Rotation|MinScale|MaxScale|Options)\s*=")),"no native transform writes");
Assert(sources.All(x=>x.All(c=>c<128)),"ASCII C# source and UI separators");
string runtime=Read("src/BoardRuntime.cs"),driver=Read("src/Plugin.cs");
Assert(driver.Contains("LateUpdate()") && driver.Contains("BoardRuntime.UpdateKnown()") && runtime.Contains("b.Component.GetModelScale()") && !runtime.Contains("c.ModelScale"),"per-frame visuals read live native geometry scale");
Assert(runtime.Contains("nextDiscovery=Time.unscaledTime+1f") && runtime.Contains("if(!Designer){Selected=null") && Regex.Matches(runtime,@"FindObjectsOfType<").Count==2,"designer-only discovery of both types at most once per second");
Assert(!runtime.Contains("alive.Contains") && runtime.Contains("VehicleObject.Released"),"temporarily undiscovered/picked up boards retained until native release");
Assert(runtime.IndexOf("c.ForceRebuild();",StringComparison.Ordinal)>runtime.IndexOf("model.SetCollisionMesh",StringComparison.Ordinal),"native collider rebuild called after mesh setters");
string hot=runtime.Substring(runtime.IndexOf("private static void UpdateBoard(",StringComparison.Ordinal));hot=hot[..hot.IndexOf("private static float Div",StringComparison.Ordinal)];
Assert(!Regex.IsMatch(hot,@"new\s+(?:GameObject|Mesh|Material|\w+\[)|\.ToArray\(|\.Select\(|\.Where\(|Signature\(|\$"""),"scale hot path has no collections, meshes, materials, file IO or formatted string allocations");
Console.WriteLine($"RESULT: {checks} assertions PASS; 0 FAIL. Game behavior not exercised.");
