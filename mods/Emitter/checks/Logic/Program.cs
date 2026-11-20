using SprocketEmitter;
using System.Numerics;
using System.Text.Json;
using SprocketTelescopicMast.Io;
int checks=0;
void Check(bool pass,string message){checks++;if(!pass){Console.Error.WriteLine("FAIL "+message);Environment.Exit(1);}Console.WriteLine("PASS "+message);}
FollowCaliberTests.Run(Check);
// Exhaust all source-combination states, then model native texture loss and reharvest.
for(int mask=0;mask<8;mask++) {
    bool a=(mask&1)!=0,b=(mask&2)!=0,c=(mask&4)!=0;
    Check(StockSource.MaterialValid(a,b,c)==(mask==7),"material validity state "+mask);
    Check(StockSource.Complete(a,b,c)==(mask==7),"source combination state "+mask);
}
var fake=new FakeStockSource();
Check(fake.Complete,"fake source initially complete");
fake.Texture=false;Check(!fake.Complete,"native texture loss invalidates combination while meshes remain");
fake.Texture=true;Check(fake.Complete,"reharvest can restore the combination");
fake.Material=false;Check(!fake.Complete,"missing owned material requires harvest");
fake.Material=true;fake.Shader=false;Check(!fake.Complete,"lost shader requires harvest");
var budget=new EjectLogBudget();
for(int i=0;i<40;i++)Check(budget.Next()==EjectLogDecision.Line,"eject diagnostic "+(i+1));
Check(budget.Next()==EjectLogDecision.Suppression,"exactly one suppression line after forty batches");
for(int i=0;i<10000;i++)if(budget.Next()!=EjectLogDecision.None)throw new Exception("eject budget overflow");
Check(budget.Next()==EjectLogDecision.None,"suppression remains bounded after repeated calls");
var d=new Settings();
var s=Settings.Parse("{\"speed\":999,\"count\":3.5,\"caliber\":-1,\"item\":\"wrong\",\"spin\":\"NaN\",\"continuous\":true}",d);
Check(s.Speed==15&&s.Count==4&&s.Caliber==5&&s.Item=="casing"&&s.Spin==180&&s.Continuous,"settings clamp, integer rounding, invalid field defaults");
Check(Settings.Parse("[]",d)==d&&Settings.Parse("{",d)==d&&Settings.Parse(null,d)==d,"invalid root, malformed and absent data");
Check(Settings.Parse("{}",d with {Speed=7}) .Speed==7,"missing field uses configured default");
Check(float.IsFinite((d with {Speed=float.NaN,Caliber=float.PositiveInfinity}).Sanitize().Speed),"nonfinite configuration sanitized");
using(var doc=JsonDocument.Parse(Settings.Schema(false))){Check(doc.RootElement.GetArrayLength()==11,"revision 2 schema has eleven fields");Check(System.Text.Encoding.UTF8.GetByteCount(Settings.Schema(true))<4096,"extended schema within 4 KiB");}
// IO-API §7 rules, checked by an independent validator; negative fixtures prove the validator rejects.
Check(SchemaRules.Validate(Settings.Schema(false)) is null,"0.1.0 schema satisfies IO-API §7: "+(SchemaRules.Validate(Settings.Schema(false))??"ok"));
Check(SchemaRules.Validate(Settings.Schema(true)) is null,"0.2.0 schema (flash, smoke) satisfies IO-API §7");
Check(!Settings.Schema(false).Contains("flash")&&!Settings.Schema(false).Contains("smoke"),"0.1.0 schema omits flash and smoke");
foreach(var (bad,why) in SchemaRules.Negatives())Check(SchemaRules.Validate(bad) is not null,"validator rejects "+why);
using(var doc=JsonDocument.Parse(Settings.Schema(false))) {
    var f=doc.RootElement.EnumerateArray().ToDictionary(x=>x.GetProperty("key").GetString()!,x=>x.GetProperty("default"));
    Check(f["follow_caliber"].GetBoolean()&&f["item"].GetString()=="casing"&&f["caliber"].GetSingle()==30&&f["speed"].GetSingle()==3&&f["spread"].GetSingle()==8&&f["spin"].GetSingle()==180&&f["count"].GetInt32()==1&&!f["continuous"].GetBoolean()&&f["interval"].GetSingle()==.5f&&f["lifetime"].GetSingle()==10&&f["inherit"].GetBoolean(),"frozen schema defaults are the fixed 0.1.0 constants");
}
Check(Settings.Schema(false)==Settings.Schema(false)&&Settings.Parse("{}",Settings.Fixed)==Settings.Fixed&&Settings.Parse("{\"lifetime\":12,\"future_key\":1}",Settings.Fixed).Lifetime==12,"revision-2 reader uses fixed defaults; unknown keys tolerated");
Check(Settings.Parse("{\"item\":\"projectile\",\"interval\":0.01,\"count\":0}",d) is {Item:"projectile",Interval:.05f,Count:1},"enum value, minimum clamps");
Check(Settings.Parse("{\"item\":7,\"inherit\":\"yes\"}",d) is {Item:"casing",Inherit:true},"wrong types use defaults");
int warned=0;Settings.Parse("{\"speed\":\"fast\",\"count\":2}",d,()=>warned++);Check(warned==1,"one invalid field gives one warning callback");
var signal=new Signal();var repeat=d with{Continuous=true,Count=3};
{
    var g=new Signal();
    Check(!g.Apply(true,true,true),"play baseline on=true does not emit");
    Check(!g.Apply(false,false,true)&&!g.Initialized,"designer apply(false) is not stored as baseline");
    Check(!g.Apply(true,true,true),"next Play baseline after designer still does not emit");
    Check(!g.Apply(false,true,true)&&g.Apply(true,true,true),"rising edge after the new baseline emits");
}
Check(!signal.Apply(true,true)&&!signal.Tick(1,repeat),"initial on is baseline, no continuous start");
Check(!signal.Apply(false,true)&&signal.Apply(true,true),"rising edge fires after baseline");
Check(!signal.Apply(true,true),"duplicate on never repeats an edge");
Check(!signal.Tick(.49f,repeat)&&signal.Tick(.02f,repeat),"continuous interval");
Check(!signal.Tick(0,repeat),"pause stops repeat clock");
Check(!signal.Apply(false,true)&&!signal.Tick(2,repeat),"off stops repeating");
Check(!signal.Apply(true,false)&&!signal.Tick(1,repeat),"paused rising edge discarded, no deferred batch");
var world=new PlaneWorld();var sim=new Simulation(64);
int landEvents=0;sim.Landed=(_,_)=>landEvents++;
int Spawn(Vector3 p,Vector3? velocity=null)=>sim.Spawn(1,p,velocity??Vector3.Zero,Quaternion.Identity,Vector3.UnitY,180,.015f,10);
int slot=Spawn(new(0,1,0));
for(int i=0;i<200;i++)sim.Tick(.01f,new(0,-9.81f,0),world,512);
Check(sim.Pieces[slot].Resting&&MathF.Abs(sim.Pieces[slot].Position.Y-.016f)<.001f,"fall onto plane, settle at radius offset");
Check(landEvents==1,"landing notification once after bounded bounce");
var landed=sim.Pieces[slot];var local=world.Pose.ToLocal(landed.Position);world.Pose=new(new(2,1,3),Quaternion.CreateFromAxisAngle(Vector3.UnitY,.7f));sim.Tick(.01f,new(0,-9.81f,0),world,512);
Check(Vector3.Distance(landed.Position,world.Pose.ToWorld(local))<1e-5,"resting piece follows translated and rotated support");
world.Pose=world.Pose with{Rotation=Quaternion.CreateFromAxisAngle(Vector3.UnitZ,MathF.PI/3)};var previous=landed.Position;sim.Tick(.05f,new(0,-9.81f,0),world,512);
Check(!landed.Resting&&landed.Velocity.Length()>.01,"tilted support over 35 degrees starts sliding");
world.Exists=false;float before=landed.Position.Y;sim.Tick(.05f,new(0,-9.81f,0),world,512);
Check(landed.Surface==0&&!landed.Resting&&landed.Position.Y<before,"missing support releases into free fall");
sim.Clear();world=new PlaneWorld{Pose=new(Vector3.Zero,Quaternion.CreateFromAxisAngle(Vector3.UnitZ,.8f))};slot=Spawn(new(0,1,0));
for(int i=0;i<100;i++)sim.Tick(.01f,new(0,-9.81f,0),world,512);
Check(!sim.Pieces[slot].Resting&&sim.Pieces[slot].Velocity.X<-.1,"fall on steep slope slides downhill");
sim.Clear();for(int i=0;i<64;i++)Spawn(new(i,10,0));sim.Pieces[0].Resting=true;var oldest=sim.Pieces[0].Serial;slot=Spawn(new(99,10,0));
Check(sim.ActiveCount==64&&slot==0&&sim.Pieces[0].Serial>oldest,"pool capacity 64 replaces oldest including resting slots");
sim.Tick(10,new(0,-9.81f,0),world,512);Check(sim.ActiveCount==0,"lifetime expiration");
sim.Clear();for(int i=0;i<64;i++)Spawn(new(0,10,0));int calls=world.Calls;int used=sim.Tick(.1f,new(0,-9.81f,0),world,7);Check(used==7&&world.Calls-calls==7,"hard global cast budget");
var frozen=sim.Pieces[0].Position;sim.Tick(0,new(0,-9.81f,0),world,512);Check(sim.Pieces[0].Position==frozen,"pause freezes motion");
signal=new Signal();signal.Apply(false,true);int thrown=0;if(signal.Apply(true,true))for(int i=0;i<repeat.Count;i++){Spawn(new(0,10,0));thrown++;}signal.Apply(false,true);Check(thrown==3&&sim.ActiveCount>0,"one rising batch has exactly count pieces; off leaves objects");
sim.RemoveOwner(1);Check(sim.ActiveCount==0,"owner release cleanup");Spawn(new(0,1,0));sim.Clear();Check(sim.ActiveCount==0,"scene and designer clear all slots");
var rng=new Random(1);float minZ=MathF.Cos(8*MathF.PI/180);bool coneOk=true;
for(int i=0;i<10000;i++){var v=Simulation.Cone(8,(float)rng.NextDouble(),(float)rng.NextDouble());coneOk&=v.Z>=minZ-1e-6&&MathF.Abs(v.Length()-1)<1e-5;}
Check(coneOk,"10000 deterministic directions inside uniform solid-angle cone");
Check(!IoApiClient.Bind(null)&&!IoApiClient.Available,"missing facade inert");
Check(!IoApiClient.Bind(typeof(Fakes.InvalidVersion)),"invalid API version refused");
Check(IoApiClient.Bind(typeof(Fakes.Revision0))&&IoApiClient.ApiRevision==0&&!IoApiClient.Revision1Available,"revision 0 has no storage or managed output");
Check(IoApiClient.Bind(typeof(Fakes.IncompleteRevision1))&&IoApiClient.ApiRevision==0&&!IoApiClient.Revision1Available,"partial revision 1 downgraded completely");
Check(IoApiClient.Bind(typeof(Fakes.Revision1))&&IoApiClient.Revision1Available&&!IoApiClient.Revision2Available,"complete revision 1");
var go=new UnityEngine.GameObject();Check(IoApiClient.RegisterManagedOutput("sprocket.emitter","Emitter","sprocketEmitterModel",(_,_)=>{},"Eject","Stop"),"managed registration accepted");
Check(!IoApiClient.RegisterManagedOutput("sprocket.emitter","Emitter","sprocketEmitterModel",(_,_)=>{},"Eject","Stop"),"false registration propagated");
Check(IoApiClient.SetPartData(go,"sprocket.emitter","{\"speed\":4}")&&IoApiClient.GetPartData(go,"sprocket.emitter")=="{\"speed\":4}","revision 1 opaque data roundtrip");
Fakes.Revision1.Throw=true;Check(!IoApiClient.SetPartData(go,"sprocket.emitter","{}")&&IoApiClient.GetPartData(go,"sprocket.emitter")==null&&!IoApiClient.RegisterPartIcon("guid","icon"),"throwing revision 1 guarded");
Check(IoApiClient.Bind(typeof(Fakes.Revision2))&&IoApiClient.Revision2Available&&IoApiClient.RegisterPartSettings("sprocketEmitterModel","sprocket.emitter",Settings.Schema(false),null),"revision 2 exact settings signature");
Fakes.Revision2.Throw=true;Check(!IoApiClient.RegisterPartSettings("sprocketEmitterModel","sprocket.emitter","[]",null),"throwing revision 2 guarded");
Check(IoApiClient.Bind(typeof(Fakes.IncompleteRevision2))&&!IoApiClient.Revision2Available&&IoApiClient.ApiRevision==1,"partial revision 2 keeps complete revision 1");
Check(!IoApiClient.Bind(typeof(Fakes.ThrowingVersion)),"throwing facade initialization guarded");
// Lookup by name (RENAME-MAP section 4): the Mast is found by plugin GUID in the chainloader; its facade type is resolved by
// full name from the assembly of that plugin instance. Both names are part of the contract with the Mast.
Check(IoApiClient.MastGuid=="sprocket.telescopicmast","Mast plugin GUID is sprocket.telescopicmast");
Check(typeof(SprocketTelescopicMast.Io.IoApi).FullName=="SprocketTelescopicMast.Io.IoApi","facade type is SprocketTelescopicMast.Io.IoApi");
var chain=BepInEx.Unity.IL2CPP.IL2CPPChainloader.Instance.Plugins;chain.Clear();
Check(!IoApiClient.TryBind()&&!IoApiClient.Available,"no Mast plugin loaded: client inert");
chain["sprocket.telescopicmast.other"]=new BepInEx.Unity.IL2CPP.Info{Instance=new Fakes.FakeMastPlugin()};
Check(!IoApiClient.TryBind()&&!IoApiClient.Available,"a plugin under another GUID is not taken for the Mast");
chain[IoApiClient.MastGuid]=new BepInEx.Unity.IL2CPP.Info{Instance=null};
Check(!IoApiClient.TryBind()&&!IoApiClient.Available,"Mast GUID without a plugin instance: client inert");
chain[IoApiClient.MastGuid]=new BepInEx.Unity.IL2CPP.Info{Instance=new Fakes.FakeMastPlugin()};
Check(IoApiClient.TryBind()&&IoApiClient.Revision2Available&&IoApiClient.ApiVersion==1&&IoApiClient.ApiRevision==2,"TryBind finds the Mast by GUID and binds the facade (IO-API v1, revision 2)");
Console.WriteLine($"SUMMARY checks={checks} fail=0; cone samples=10000");

static class SchemaRules {
    static readonly System.Text.RegularExpressions.Regex KeyRx=new("^[a-z0-9_]{1,32}$");
    // Returns null when valid, else the first violated IO-API v1.2 §7 rule.
    internal static string? Validate(string json) {
        if(System.Text.Encoding.UTF8.GetByteCount(json)>4096)return "schema > 4 KB";
        JsonDocument doc;try{doc=JsonDocument.Parse(json);}catch(JsonException){return "not JSON";}
        using(doc){
        var root=doc.RootElement;if(root.ValueKind!=JsonValueKind.Array)return "not an array";
        if(root.GetArrayLength()>16)return "> 16 fields";
        var types=new Dictionary<string,(string type,JsonElement field)>();
        foreach(var f in root.EnumerateArray()) {
            if(f.ValueKind!=JsonValueKind.Object)return "field not an object";
            if(!f.TryGetProperty("key",out var k)||k.ValueKind!=JsonValueKind.String||!KeyRx.IsMatch(k.GetString()!))return "bad key";
            string key=k.GetString()!;if(types.ContainsKey(key))return "duplicate key "+key;
            if(!f.TryGetProperty("label",out var l)||l.ValueKind!=JsonValueKind.String||l.GetString()!.Length is 0 or >40)return "bad label "+key;
            if(f.TryGetProperty("unit",out var u)&&(u.ValueKind!=JsonValueKind.String||u.GetString()!.Length is 0 or >8))return "bad unit "+key;
            if(!f.TryGetProperty("type",out var t)||t.ValueKind!=JsonValueKind.String)return "no type "+key;
            string type=t.GetString()!;
            if(!f.TryGetProperty("default",out var def))return "no default "+key;
            switch(type) {
                case "float": case "int": {
                    if(!f.TryGetProperty("min",out var mn)||!f.TryGetProperty("max",out var mx)||mn.ValueKind!=JsonValueKind.Number||mx.ValueKind!=JsonValueKind.Number||def.ValueKind!=JsonValueKind.Number)return "numeric bounds "+key;
                    double a=mn.GetDouble(),b=mx.GetDouble(),v=def.GetDouble();
                    if(type=="int"){a=Math.Ceiling(a);b=Math.Floor(b);if(v!=Math.Floor(v))return "int default not integral "+key;}
                    if(a>b||v<a||v>b)return "default outside range "+key;
                    if(f.TryGetProperty("step",out var st)&&(st.ValueKind!=JsonValueKind.Number||st.GetDouble()<=0))return "bad step "+key;
                    break;}
                case "bool": if(def.ValueKind is not (JsonValueKind.True or JsonValueKind.False))return "bool default "+key;break;
                case "enum": {
                    if(!f.TryGetProperty("options",out var o)||o.ValueKind!=JsonValueKind.Array||o.GetArrayLength() is 0 or >16)return "enum options "+key;
                    var values=o.EnumerateArray().Select(x=>x.TryGetProperty("value",out var vv)&&vv.ValueKind==JsonValueKind.String&&x.TryGetProperty("label",out var ll)&&ll.ValueKind==JsonValueKind.String?vv.GetString():null).ToList();
                    if(values.Contains(null)||values.Distinct().Count()!=values.Count)return "enum option shape "+key;
                    if(def.ValueKind!=JsonValueKind.String||!values.Contains(def.GetString()))return "enum default "+key;
                    break;}
                default: return "unknown type "+type;
            }
            types[key]=(type,f);
        }
        foreach(var f in root.EnumerateArray()) {
            if(!f.TryGetProperty("showIf",out var s))continue;
            if(s.ValueKind!=JsonValueKind.Object||!s.TryGetProperty("key",out var sk)||sk.ValueKind!=JsonValueKind.String||!s.TryGetProperty("value",out var sv))return "showIf shape";
            if(s.EnumerateObject().Count()!=2)return "showIf has extra members";
            if(!types.TryGetValue(sk.GetString()!,out var target))return "showIf unknown key";
            if(target.type=="bool"&&sv.ValueKind is JsonValueKind.True or JsonValueKind.False)continue;
            if(target.type=="enum"&&sv.ValueKind==JsonValueKind.String)continue;
            return "showIf not equality on bool/enum";
        }
        return null;}
    }
    internal static IEnumerable<(string,string)> Negatives() {
        string ok="{\"key\":\"a\",\"label\":\"A\",\"type\":\"bool\",\"default\":true}";
        yield return ("[{\"key\":\"Speed\",\"label\":\"S\",\"type\":\"bool\",\"default\":true}]","uppercase key");
        yield return ("[{\"key\":\""+new string('k',33)+"\",\"label\":\"S\",\"type\":\"bool\",\"default\":true}]","33-char key");
        yield return ("[{\"key\":\"a\",\"label\":\""+new string('l',41)+"\",\"type\":\"bool\",\"default\":true}]","41-char label");
        yield return ("[{\"key\":\"a\",\"label\":\"A\",\"type\":\"float\",\"unit\":\"degrees/s\",\"min\":0,\"max\":1,\"default\":0}]","9-char unit");
        yield return ("[{\"key\":\"a\",\"label\":\"A\",\"type\":\"float\",\"unit\":\"\",\"min\":0,\"max\":1,\"default\":0}]","empty unit");
        yield return ("["+string.Join(",",Enumerable.Range(0,17).Select(i=>ok.Replace("\"a\"","\"a"+i+"\"")))+"]","17 fields");
        yield return ("[{\"key\":\"a\",\"label\":\"A\",\"type\":\"text\",\"default\":\"x\"}]","unknown type");
        yield return ("[{\"key\":\"a\",\"label\":\"A\",\"type\":\"float\",\"min\":0,\"max\":1,\"default\":0},{\"key\":\"b\",\"label\":\"B\",\"type\":\"bool\",\"default\":true,\"showIf\":{\"key\":\"a\",\"value\":0}}]","showIf on float");
        yield return ("["+ok+",{\"key\":\"b\",\"label\":\"B\",\"type\":\"bool\",\"default\":true,\"showIf\":{\"key\":\"a\",\"value\":true,\"op\":\"ne\"}}]","showIf non-equality");
        yield return ("[{\"key\":\"a\",\"label\":\"A\",\"type\":\"int\",\"min\":1,\"max\":8,\"default\":9}]","default above max");
        yield return ("[{\"key\":\"a\",\"label\":\"A\",\"type\":\"enum\",\"options\":["+string.Join(",",Enumerable.Range(0,17).Select(i=>"{\"value\":\"v"+i+"\",\"label\":\"V\"}"))+"],\"default\":\"v0\"}]","17 enum options");
        yield return ("[{\"key\":\"a\",\"label\":\"A\",\"type\":\"enum\",\"options\":[{\"value\":\"x\",\"label\":\"X\"}],\"default\":\"y\"}]","enum default not an option");
        yield return ("[{\"key\":\"a\",\"label\":\""+new string('x',40)+"\",\"type\":\"bool\",\"default\":true,\"pad\":\""+new string('p',4100)+"\"}]","schema over 4 KB");
    }
}
sealed class FakeStockSource {
    internal bool Material=true,Shader=true,Texture=true;
    internal bool Complete=>StockSource.Complete(true,true,StockSource.MaterialValid(Material,Shader,Texture));
}
sealed class PlaneWorld:IWorld {
    internal bool Exists=true;internal int Calls;
    internal SurfacePose Pose=new(Vector3.Zero,Quaternion.Identity);
    public bool Cast(Vector3 from,Vector3 to,float radius,out Contact hit){Calls++;hit=default;if(!Exists)return false;var n=Vector3.Transform(Vector3.UnitY,Pose.Rotation);float a=Vector3.Dot(from-Pose.Position,n),b=Vector3.Dot(to-Pose.Position,n);if(a>=0&&b<=0){hit=new(1,Vector3.Lerp(from,to,a/(a-b)),n);return true;}return false;}
    public bool Surface(long id,out SurfacePose pose){pose=Pose;return Exists;}
    public Vector3 ToLocal(long id,Vector3 p)=>Pose.ToLocal(p);
    public Vector3 ToWorld(long id,Vector3 p)=>Pose.ToWorld(p);
}
