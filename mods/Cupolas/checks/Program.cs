using System.Numerics;
using System.Text.Json;
using System.Reflection;
using SprocketCupolas;
int checks=0;
void Check(bool ok,string text){if(!ok)throw new Exception(text);checks++;Console.WriteLine("PASS "+text);}
void Throws(Action action,string text){try{action();}catch(InvalidDataException){Check(true,text);return;}throw new Exception("Did not reject "+text);}
var meshes=new[]{CupolaMeshData.Base,CupolaMeshData.Door};
var production=Assembly.LoadFrom(Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,"../../../../bin/Release/net6.0/SprocketCupolas.dll")));
var embeddedType=production.GetType("SprocketCupolas.CupolaMeshData",true)!;
foreach(var d in meshes) {
    var actual=embeddedType.GetField(d.Name=="base"?"Base":"Door",BindingFlags.Static|BindingFlags.NonPublic)!.GetValue(null)!;
    string actualJson=JsonSerializer.Serialize(actual,actual.GetType());
    Check(actualJson==JsonSerializer.Serialize(d),d.Name+" actual production DLL data equals validated source data");
    var g=MeshGeometry.Create(d);
    Check(g.Indices.Length==d.Indices.Length,d.Name+" triangle count");
    Check(g.Positions.Length>=d.Positions.Length/3,d.Name+" chart vertex splits only");
    Check(g.Tangents.All(t=>float.IsFinite(t.X)&&float.IsFinite(t.Y)&&float.IsFinite(t.Z)&&Math.Abs(new Vector3(t.X,t.Y,t.Z).Length()-1)<1e-5&&Math.Abs(t.W)==1),d.Name+" finite unit tangents");
    Check(g.UV.All(u=>float.IsFinite(u.X)&&float.IsFinite(u.Y)),d.Name+" finite metric UV");
    for(int i=0;i<g.Indices.Length;i++) {
        int j=d.Indices[i];var expected=new Vector3(d.Positions[j*3],d.Positions[j*3+1],d.Positions[j*3+2]);
        if(g.Positions[g.Indices[i]]!=expected)throw new Exception("winding/positions changed");
    }
    Check(true,d.Name+" every triangle corner preserves OBJ position and order");
    Console.WriteLine($"MESH {d.Name}: embedded vertices={d.Positions.Length/3}, runtime chart vertices={g.Positions.Length}, triangles={g.Indices.Length/3}, bounds={string.Join(',',d.Bounds)}, sourceHash={d.ObjSha256}");
    Throws(()=>MeshGeometry.Create(d with {DataSha256=new string('0',64)}),d.Name+" invalid hash rejected");
    if(args.Length>0)File.WriteAllText(Path.Combine(args[0],d.Name+"-embedded.json"),actualJson);
}
var good=new[]{new CupolaPolicy.Node(1,10,false,false),new CupolaPolicy.Node(2,10,false,false)};
string? Gate(bool installed=true,bool preview=false,bool dynamicRoot=true,IReadOnlyList<CupolaPolicy.Node>? chain=null,bool terminated=true,string? guid=null,long vehicle=10)
    =>CupolaPolicy.Eligible(guid??CupolaPolicy.PartGuid,vehicle,installed,preview,dynamicRoot,chain??good,terminated);
Check(Gate()==null,"installed owner accepted without independent model-installed bit");
Check(Gate(installed:false)!=null,"uninstalled ghost rejected");
Check(Gate(preview:true)!=null,"preview save flags rejected");
Check(Gate(dynamicRoot:false)!=null,"static root rejected");
Check(Gate(guid:"foreign")!=null,"foreign GUID rejected");
Check(Gate(vehicle:0)!=null,"missing vehicle rejected");
Check(Gate(terminated:false)!=null,"unterminated ancestry rejected");
foreach(int i in new[]{0,1}) {
    var chain=good.ToArray();chain[i]=chain[i] with {Placement=true};Check(Gate(chain:chain)!=null,"placement at ancestry depth "+i);
    chain=good.ToArray();chain[i]=chain[i] with {Released=true};Check(Gate(chain:chain)!=null,"released at ancestry depth "+i);
    chain=good.ToArray();chain[i]=chain[i] with {Vehicle=99};Check(Gate(chain:chain)!=null,"foreign at ancestry depth "+i);
}
Check(Gate(chain:new[]{good[0],good[0]})!=null,"cyclic ancestry rejected");
Check(Gate(chain:Enumerable.Range(0,257).Select(i=>new CupolaPolicy.Node(i,10,false,false)).ToArray())!=null,"deep ancestry rejected");
var models=new[]{new CupolaPolicy.Model(3,"baseModel",1,1,5),new CupolaPolicy.Model(4,"hatchModel",2,1,5)};
bool Resolve(CupolaPolicy.Model[] m,long hatch=4)=>CupolaPolicy.Resolve(1,5,hatch,m,out _,out _);
Check(Resolve(models),"exact base/door resolved under root and commander association");
Check(!Resolve(models,30),"foreign hatch reference rejected");
Check(!Resolve(new[]{models[0]}),"missing door rejected");
Check(!Resolve(models.Append(models[0]).ToArray()),"duplicate base rejected");
Check(!Resolve(models.Append(new(8,"extraModel",1,1,5)).ToArray()),"extra model fails atlas topology");
var foreign=models.Append(new CupolaPolicy.Model(9,"baseModel",90,90,95)).ToArray();
Check(Resolve(foreign),"unrelated neighbour model remains outside root");
Check(!Resolve(new[]{models[0],models[1] with {Association=99}}),"foreign association rejected");
// Fake driver uses the production cache, gate and resolver. No position-based merge.
var cache=new CupolaCache<object>();var a=new CupolaKey(10,1,101);var b=new CupolaKey(10,2,102);
int swaps=0,releases=0;
void Tick(CupolaKey key,bool eligible,object wrapper) {
    if(!eligible)return;
    if(cache.TryGet(key,out var old)&&!ReferenceEquals(old,wrapper))cache.Retire((k,v)=>k==key,v=>releases++);
    if(!cache.TryGet(key,out _)&&cache.Add(key,wrapper))swaps++;
}
var wa=new object();var wb=new object();
Tick(a,Gate(installed:false)==null,wa);Check(cache.Count==0&&swaps==0,"ghost allocates/swaps nothing");
Tick(a,true,wa);Tick(b,true,wb);Check(cache.Count==2&&swaps==2,"two centred mirrors with distinct VUIDs remain two instances");
Tick(a,true,wa);Check(swaps==2,"repeat scan does not swap again");
Tick(a,false,wa);Check(cache.Count==2&&releases==0,"temporary pickup preserves native references");
Tick(a,true,new object());Check(swaps==3&&releases==1&&cache.Count==2,"wrapper replacement retires generation and re-adopts");
cache.Retire((k,v)=>k==a,v=>releases++);Check(cache.Count==1&&releases==2,"real release frees only owner");
cache.Clear(v=>releases++);Check(cache.Count==0&&releases==3,"scene change clears generations");
for(int i=0;i<CupolaPolicy.Limit;i++)CheckCacheAdd(i);
void CheckCacheAdd(int i){if(!cache.Add(new(10,i,i),new object()))throw new Exception("cache early limit");}
Check(!cache.Add(new(10,99999,99999),new object()),"cache limit enforced");
Console.WriteLine($"PASS {checks} managed checks. Offline evidence only.");
