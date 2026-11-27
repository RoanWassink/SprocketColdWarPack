using SprocketThermalSight;
using System.Text.Json;
int checks=0;
void Check(bool value,string message){checks++;if(!value)throw new Exception(message);}
void Reject(Action action,string message){bool rejected=false;try{action();}catch{rejected=true;}Check(rejected,message);}
var row=new ConsoleLinkEntry(968,992);
var json=ConsoleLinkData.Encode(new[]{row});
Check(ConsoleLinkData.Decode(json).SequenceEqual(new[]{row}),"roundtrip VUID meaning");
Check(ConsoleLinkData.Decode(ConsoleLinkData.Encode(Array.Empty<ConsoleLinkEntry>())).Length==0,"unlink empty envelope");
var read=ConsoleLinkData.Read(new[]{ConsoleLinkData.LegacyKey},_=>throw new Exception(),_=>992);
Check(!read.Canonical&&read.Legacy==992,"legacy target read");
read=ConsoleLinkData.Read(new[]{ConsoleLinkData.Key,ConsoleLinkData.LegacyKey},_=>json,_=>throw new Exception());
Check(read.Canonical&&read.Legacy==-1&&read.Links[0]==row,"canonical precedence");
read=ConsoleLinkData.Read(new[]{ConsoleLinkData.Key,ConsoleLinkData.LegacyKey},_=>ConsoleLinkData.Encode(Array.Empty<ConsoleLinkEntry>()),_=>992);
Check(read.Canonical&&read.Links.Length==0,"canonical unlink wins legacy");
read=ConsoleLinkData.Read(Array.Empty<string>(),_=>throw new Exception(),_=>throw new Exception());
Check(!read.Canonical&&read.Legacy==-1,"old blueprint without link");
Reject(()=>ConsoleLinkData.Read(new[]{ConsoleLinkData.Key,ConsoleLinkData.LegacyKey},_=>"invalid",_=>992),"malformed canonical does not fall back");
foreach(var bad in new[]{
 "{\"Version\":2,\"Links\":[]}",
 "{\"Version\":1,\"Links\":[{\"Console\":992}]}",
 "{\"Version\":1,\"Links\":[{\"Sight\":968}]}",
 "{\"Version\":1,\"Links\":[{\"Sight\":-1,\"Console\":992}]}",
 "{\"Version\":1,\"Links\":[{\"Sight\":968,\"Console\":-1}]}",
 "{\"Version\":1,\"Links\":[{\"Sight\":\"968\",\"Console\":992}]}",
 "{\"Version\":1,\"Version\":1,\"Links\":[]}",
 "{\"Version\":1,\"Links\":[],\"unexpected\":true}",
 "{\"Version\":1,\"Links\":null}",
 "null"})
 Reject(()=>ConsoleLinkData.Decode(bad),"reject malformed envelope");
Reject(()=>ConsoleLinkData.Encode(new[]{row,row}),"duplicate source rejected");
Reject(()=>ConsoleLinkData.Encode(Enumerable.Range(0,65).Select(i=>new ConsoleLinkEntry(i,992))),"bounded rows");
Reject(()=>ConsoleLinkData.Decode(new string('x',16385)),"bounded bytes");
var max=Enumerable.Range(0,64).Select(i=>new ConsoleLinkEntry(i,992)).ToArray();
Check(ConsoleLinkData.Decode(ConsoleLinkData.Encode(max)).SequenceEqual(max),"64 row limit valid");
var map=new ConsoleVuidMap();map.Add(968,968);map.Add(992,992);
Check(ConsoleLinkData.Remap(new[]{row},map.Values).Single()==row,"normal identity restore");
map=new();map.Add(968,1968);map.Add(992,1992);
Check(ConsoleLinkData.Remap(new[]{row},map.Values).Single()==new ConsoleLinkEntry(1968,1992),"duplicate pair remaps both");
map=new();map.Add(968,1968);
Check(ConsoleLinkData.Remap(new[]{row},map.Values).Length==0,"sight only copy cannot retain raw consoleID");
map=new();map.Add(992,1992);
Check(ConsoleLinkData.Remap(new[]{row},map.Values).Length==0,"missing source skipped");
map=new();map.Add(968,1968);map.Add(992,1992);
Check(ConsoleLinkData.Remap(new[]{row,row},map.Values).Length==0,"duplicate source across envelopes skipped");
Check(ConsoleLinkData.ForOwner(new[]{row},new HashSet<int>{381}).Length==0,"foreign owner envelope skipped");
Check(ConsoleLinkData.ForOwner(new[]{row},new HashSet<int>{968}).Single()==row,"correct owner retained");
map=new();map.Add(1,100);map.Add(2,100);map.Add(3,100);
Check(map.Values.Count==0,"three actualID collisions permanently rejected");
map.Add(4,100);map.Add(1,101);
Check(map.Values.Count==0,"poison persists on repeat old and new IDs");
map.Add(5,105);Check(map.Values.Count==1&&map.Values[5]==105,"independent map remains");
map=new();map.Add(1,100);map.Add(2,200);map.Add(1,200);map.Add(3,100);map.Add(4,200);
Check(map.Values.Count==0,"oldID collision poisons both actual endpoints");
map=new();map.Add(-1,100);map.Add(1,-1);
Check(map.Values.Count==0,"invalid native VUID not mapped");
map=new();map.Add(1,100);map.Add(1,100);map.Add(2,100);
Check(map.Values.Count==0,"repeated identical old mapping ambiguous");
for(int offset=1;offset<=100;offset++)
{
 map=new();map.Add(992,992+offset*1000);map.Add(968,968+offset*1000);
 Check(ConsoleLinkData.Remap(new[]{row},map.Values).Single()==new ConsoleLinkEntry(968+offset*1000,992+offset*1000),"independent/reordered remaps");
}
var state=new Dictionary<string,object>{{"t72StyleSight",968},{"model",969},{ConsoleLinkData.Key,json}};
var outer=JsonSerializer.Serialize(state);
using var parsed=JsonDocument.Parse(outer);
Check(parsed.RootElement.GetProperty(ConsoleLinkData.Key).ValueKind==JsonValueKind.String,"wire FCS key is string not native topology int");
Check(parsed.RootElement.GetProperty("t72StyleSight").GetInt32()==968,"native topology untouched");
// 0.5.5: multi-source console links (sight + laying drive + traverse motor + driver steering controls) share the same codec & VUID remap.
var multi=new[]{new ConsoleLinkEntry(968,992),new ConsoleLinkEntry(307,992),new ConsoleLinkEntry(764,992),new ConsoleLinkEntry(512,880)};
var multiJson=ConsoleLinkData.Encode(multi);
Check(ConsoleLinkData.Decode(multiJson).SequenceEqual(multi),"multi-component console links roundtrip");
var multiMap=new ConsoleVuidMap();
foreach(var (o,n) in new[]{(968,1968),(307,1307),(764,1764),(992,1992),(512,1512),(880,1880)})multiMap.Add(o,n);
var remappedMulti=ConsoleLinkData.Remap(multi,multiMap.Values);
Check(remappedMulti.Length==4 && remappedMulti.Any(r=>r.Sight==1307&&r.Console==1992) && remappedMulti.Any(r=>r.Sight==1512&&r.Console==1880),"drive and driver console VUIDs remap");
// 0.5.5: OpenSCAD driver-console.obj export hash & geometry check (AGENTS.md rule).
var repoRoot=Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,"..","..","..","..",".."));
var exportedObj=Path.Combine(repoRoot,"driver-console","driver-console.obj");
var packagedObj=Path.Combine(repoRoot,"package","BepInEx","plugins","SprocketThermalSight","assets","driver-console.obj");
var meshCheck=Path.Combine(repoRoot,"driver-console","MESH-CHECK.txt");
if(File.Exists(exportedObj)&&File.Exists(packagedObj)&&File.Exists(meshCheck))
{
    var expectedHash=File.ReadAllLines(meshCheck).First(l=>l.StartsWith("sha256: ")).Substring(8).Trim();
    string Sha(string p)=>Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(p)));
    Check(Sha(exportedObj)==expectedHash,"driver-console.obj matches MESH-CHECK sha256");
    Check(Sha(packagedObj)==expectedHash,"packaged driver-console.obj matches OpenSCAD export");
}
var cmdExportedObj=Path.Combine(repoRoot,"commander-sight","commander-sight.obj");
var cmdPackagedObj=Path.Combine(repoRoot,"package","BepInEx","plugins","SprocketThermalSight","assets","commander-sight.obj");
var cmdMeshCheck=Path.Combine(repoRoot,"commander-sight","MESH-CHECK.txt");
if(File.Exists(cmdExportedObj)&&File.Exists(cmdPackagedObj)&&File.Exists(cmdMeshCheck))
{
    var expectedHash=File.ReadAllLines(cmdMeshCheck).First(l=>l.StartsWith("sha256: ")).Substring(8).Trim();
    string Sha(string p)=>Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(p)));
    Check(Sha(cmdExportedObj)==expectedHash,"commander-sight.obj matches MESH-CHECK sha256");
    Check(Sha(cmdPackagedObj)==expectedHash,"packaged commander-sight.obj matches OpenSCAD export");
}
var oesExportedObj=Path.Combine(repoRoot,"targeting-sensor","oes-op-sight.obj");
var oesPackagedObj=Path.Combine(repoRoot,"package","BepInEx","plugins","SprocketThermalSight","assets","oes-op-sight.obj");
var oesMeshCheck=Path.Combine(repoRoot,"targeting-sensor","MESH-CHECK.txt");
if(File.Exists(oesExportedObj)&&File.Exists(oesPackagedObj)&&File.Exists(oesMeshCheck))
{
    var expectedHash=File.ReadAllLines(oesMeshCheck).First(l=>l.StartsWith("sha256: ")).Substring(8).Trim();
    string Sha(string p)=>Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(p)));
    Check(Sha(oesExportedObj)==expectedHash,"oes-op-sight.obj matches MESH-CHECK sha256");
    Check(Sha(oesPackagedObj)==expectedHash,"packaged oes-op-sight.obj matches Blender export");
}
int beforeProfiles=checks;
string fixtures=args.Length>0?args[0]:Path.Combine(Path.GetTempPath(),"fixtures");
ProfilePersistenceChecks.Run(Check,Reject,fixtures);
Console.WriteLine($"PASS {beforeProfiles} production FCS codec/remap/ambiguity checks; Unity native serializer fixture remains pending.");
Console.WriteLine($"PASS {checks-beforeProfiles} 0.4.2 sight-profile checks (codec, remap, FCS coexistence, real blueprint fixture in {fixtures}); total {checks}.");
