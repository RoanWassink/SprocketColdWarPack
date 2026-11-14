using SprocketVisualEffects;
using SprocketTelescopicMast.Io;
using UnityEngine;
using Fakes;
using N=System.Numerics;
// Render support: LampChecks --dump in.json out.json writes the runtime lens pose
// (LampLens.TryWorld, the exact runtime math) for model poses chosen by an external render script.
if(args.Length==3&&args[0]=="--dump") {
 var input=System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(args[1]))!.AsArray();var output=new System.Text.Json.Nodes.JsonArray();
 foreach(var c in input) {
  float F(string k,int i)=>c![k]![i]!.GetValue<float>();
  var ok=LampLens.TryWorld(c!["form"]!.GetValue<int>(),new N.Vector3(F("p",0),F("p",1),F("p",2)),new N.Quaternion(F("q",0),F("q",1),F("q",2),F("q",3)),new N.Vector3(F("s",0),F("s",1),F("s",2)),out var pose);
  System.Text.Json.Nodes.JsonArray A(params float[] v){var a=new System.Text.Json.Nodes.JsonArray();foreach(var x in v)a.Add(x);return a;}
  output.Add(new System.Text.Json.Nodes.JsonObject{["ok"]=ok,["centre"]=A(pose.Centre.X,pose.Centre.Y,pose.Centre.Z),["normal"]=A(pose.Normal.X,pose.Normal.Y,pose.Normal.Z),
   ["rotation"]=A(pose.Rotation.X,pose.Rotation.Y,pose.Rotation.Z,pose.Rotation.W),["discScale"]=A(pose.DiscScale.X,pose.DiscScale.Y,pose.DiscScale.Z),
   ["halo"]=A(pose.HaloPoint.X,pose.HaloPoint.Y,pose.HaloPoint.Z),["haloDiameter"]=LampLook.HaloDiameter(c["form"]!.GetValue<int>(),pose.InPlaneScale)});
 }
 File.WriteAllText(args[2],output.ToJsonString());Console.WriteLine("dumped "+output.Count+" lens poses");return;
}
int checks=0;
void Check(bool b,string text){checks++;if(!b)throw new Exception(text);}
Check(!IoApiClient.TryBind()&&!IoApiClient.Available,"No Mast discovery");
var part=new GameObject();
Check(!IoApiClient.SetPartData(part,"visualeffects.lamp","{}")&&IoApiClient.GetPartData(part,"visualeffects.lamp")==null,"No Mast methods no-op");
Check(!IoApiClient.RegisterManagedOutput("visualeffects.lamp","Lamp","vfxLamp",(_,_)=>{}),"No Mast registration no-op");
Check(!IoApiClient.Bind(typeof(InvalidVersion)),"Version zero refused");
Check(IoApiClient.Bind(typeof(Revision0))&&IoApiClient.ApiVersion==1&&IoApiClient.ApiRevision==0&&!IoApiClient.Revision1Available,"Missing revision field is zero");
string? opaque=null;int callback=0;
Check(IoApiClient.RegisterOutput("visualeffects.lamp","Lamp","vfxLamp",(_,on)=>callback+=on?1:-1,_=>opaque,(_,s)=>opaque=s,"Lamp on","Lamp off"),"Revision zero registration");
string flow=" {\"partId\":\"opaque\",\"flow\":{\"v\":1,\"rev\":11,\"nodes\":[{\"unknown\":true}],\"wires\":[]}} \n";
Revision0.Save!(part,flow);Check(Revision0.Load!(part)==flow,"Revision zero flow stays byte-for-byte opaque");
Revision0.Apply!(part,true);Revision0.Apply(part,false);Check(callback==0,"Apply on/off");
Check(!IoApiClient.SetPartData(part,"visualeffects.lamp","{}")&&!IoApiClient.RegisterPartIcon("x","x.png"),"Revision zero storage/icons disabled");
Check(IoApiClient.Bind(typeof(Revision1))&&IoApiClient.Revision1Available,"Revision one methods bound by reflection");
Check(IoApiClient.RegisterManagedOutput("visualeffects.lamp","Lamp","vfxLamp",(_,on)=>callback+=on?1:-1,"Lamp on","Lamp off"),"Managed output registration");
Check(Revision1.ComponentId=="vfxLamp"&&Revision1.OutputId=="visualeffects.lamp"&&Revision1.On=="Lamp on"&&Revision1.Off=="Lamp off"&&Revision1.Registrations==1,"Shared component and labels");
Check(!IoApiClient.RegisterManagedOutput("visualeffects.lamp","Lamp","vfxLamp",(_,_)=>{}),"Duplicate output refused");
var data=LampSettings.Default(1) with {Color="#28FF38",Intensity=18,Range=11,Angle=70};
Check(IoApiClient.SetPartData(part,"visualeffects.lamp",data.Json()),"Lamp settings write");
var second=new GameObject();Check(IoApiClient.GetPartData(second,"visualeffects.lamp")==null,"Per-instance isolation");
// Fake blueprint boundary: the fake owns the storage, exactly as the real Mast must.
foreach(var kv in part.Data)second.Data.Add(kv.Key,kv.Value);
Check(LampSettings.Parse(IoApiClient.GetPartData(second,"visualeffects.lamp"),LampSettings.Default(1))==data,"Fake save/clean-load roundtrip");
Check(!IoApiClient.SetPartData(new GameObject{ComponentId="other"},"visualeffects.lamp","{}"),"Unregistered component refused");
Check(!IoApiClient.SetPartData(part,"visualeffects.LAMP","{}")&&!IoApiClient.SetPartData(part,new string('x',65),"{}"),"Invalid keys refused");
Check(!IoApiClient.SetPartData(part,"visualeffects.lamp",new string('x',4097)),"4 KB value limit");
for(int i=0;i<3;i++)Check(IoApiClient.SetPartData(part,"visualeffects.extra"+i,new string('x',4096)),"Under 16 KB aggregate");
Check(!IoApiClient.SetPartData(part,"visualeffects.more",new string('x',4096)),"16 KB aggregate limit");
Check(IoApiClient.SetPartData(part,"visualeffects.lamp",null)&&IoApiClient.GetPartData(part,"visualeffects.lamp")==null,"Null deletes key");
for(int i=0;i<3;i++)Check(IoApiClient.RegisterPartIcon(LampSettings.Guids[i],Path.GetFullPath("lamp-"+LampSettings.Forms[i]+"-icon.png")),"Icon registration");
Check(!IoApiClient.RegisterPartIcon(LampSettings.Guids[0],"duplicate.png"),"Duplicate icon refused");
Revision1.Throw=true;
Check(!IoApiClient.SetPartData(part,"visualeffects.lamp","{}")&&IoApiClient.GetPartData(part,"visualeffects.lamp")==null&&!IoApiClient.RegisterPartIcon("x","x")&&!IoApiClient.RegisterManagedOutput("x","x","x",(_,_)=>{}),"Throwing facade contained");
Revision1.Throw=false;
Revision1.Apply!(part,true);Revision1.Apply(part,false);Check(callback==0,"Fake release/designer apply(false)");
Check(IoApiClient.Bind(typeof(IncompleteRevision1))&&!IoApiClient.Revision1Available&&!IoApiClient.SetPartData(part,"visualeffects.lamp","{}"),"Incomplete revision one falls back to base API without storage");
Check(!IoApiClient.Bind(null)&&IoApiClient.ApiRevision==0&&!IoApiClient.Available,"Rebind clears cached delegates");
// The mast is found by its plugin GUID and then its facade type by name (SprocketTelescopicMast.Io.IoApi).
Check(IoApiClient.MastGuid=="sprocket.telescopicmast","Mast GUID constant is sprocket.telescopicmast");
{
 var chain=BepInEx.Unity.IL2CPP.IL2CPPChainloader.Instance.Plugins;
 chain.Clear();
 chain["sprocket.other"]=new BepInEx.Unity.IL2CPP.Info{Instance=new Fakes.FakeMastPlugin()};
 Check(!IoApiClient.TryBind()&&!IoApiClient.Available,"A plugin under another GUID is not taken for the mast");
 chain["sprocket.telescopicmast"]=new BepInEx.Unity.IL2CPP.Info{Instance=new Fakes.FakeMastPlugin()};
 Check(IoApiClient.TryBind()&&IoApiClient.Available&&IoApiClient.ApiRevision==1&&IoApiClient.Revision1Available,"Mast found through GUID sprocket.telescopicmast and facade type SprocketTelescopicMast.Io.IoApi");
 Check(IoApiClient.RegisterPartIcon("guid-through-trybind","x.png"),"Facade bound through TryBind answers");
 chain.Clear();
 Check(!IoApiClient.TryBind()&&!IoApiClient.Available&&IoApiClient.ApiRevision==0,"No mast after the plugin list is empty again");
}
for(int i=0;i<3;i++) {
 var fallback=LampSettings.Default(i);
 Check(LampSettings.Parse(null,fallback)==fallback&&LampSettings.Parse("broken",fallback)==fallback,"Missing/malformed defaults");
 Check(LampSettings.Parse(fallback.Json(),fallback)==fallback,"Settings JSON roundtrip");
 Check(LampSettings.Parse("{\"color\":null,\"intensity\":-5,\"range\":1000,\"angle\":0}",fallback)==(fallback with {Intensity=0,Range=100,Angle=5}),"Sanitized values");
 Check((fallback with {Intensity=float.NaN,Range=float.PositiveInfinity}).Sanitize(fallback).Intensity==fallback.Intensity,"Nonfinite defaults");
}
Check(LampSettings.Default(0).Color=="#FFFFFF"&&LampSettings.Default(1).Color=="#FF1808"&&LampSettings.Default(2).Color=="#FF9A18","Shape defaults");
foreach(var color in LampSettings.Colors)Check(LampSettings.ValidColor(color),"All presets valid");
Check((new LampSettings{Color="#000000"}).Infrared,"IR suppresses visible light");
Check(!LampSettings.ValidColor("#GGGGGG")&&!LampSettings.ValidColor("FFFFFF"),"Invalid RGB");
Check(LampPolicy.Fade(0,true,.025f)==.5f&&LampPolicy.Fade(.5f,true,.025f)==1&&LampPolicy.Fade(1,false,.05f)==0,"50ms bidirectional fade");
Check(LampPolicy.Fade(0,true,float.NaN)==0&&LampPolicy.Fade(.5f,true,-1)==.5f,"Invalid delta contained");
Check(LampPolicy.Lod(100,250)==1&&LampPolicy.Lod(225,250)==.5f&&LampPolicy.Lod(250,250)==0&&LampPolicy.Lod(float.NaN,250)==0,"LOD boundaries and fade");
for(int count=0;count<=200;count++)for(int budget=0;budget<=128;budget++) {
 int allocated=Enumerable.Range(0,count).Count(i=>LampPolicy.Allocate(i,budget,true,true,1,1));
 Check(allocated==Math.Min(count,budget),"Scene budget over 0..200 lamps and 0..128 slots");
}
Check(!LampPolicy.Allocate(0,24,false,true,1,1)&&!LampPolicy.Allocate(0,24,true,false,1,1)&&!LampPolicy.Allocate(0,24,true,true,0,1)&&!LampPolicy.Allocate(0,24,true,true,1,0),"Designer/IR/off/distance use no Light");
// 0.4.4 colour retention: the brightest emitted channel is capped and channel ratios are kept.
Check(LampSettings.Default(1).Intensity==80&&LampSettings.Default(2).Intensity==20&&LampSettings.Default(0).Intensity==300,"0.4.4 day-visible defaults");
Check(LampSettings.MigrateIntensity(1,8)==80&&LampSettings.MigrateIntensity(2,2)==20&&LampSettings.MigrateIntensity(1,9)==9&&LampSettings.MigrateIntensity(0,300)==300,"Untouched legacy factory intensity migrates; customised values kept");
int colourChecks=0;
foreach(var hex in LampSettings.Colors.Concat(new[]{"#FF0000","#010000","#123456","#FFFF00","#7F7F7F","#00FF01"}))
 for(float cd=0;cd<=100000;cd=cd<10?cd+0.5f:cd*1.37f)
  foreach(var max in new[]{.25f,1f,LampLook.MaxChannelDefault,4f,float.NaN,100f})
   foreach(var fade in new[]{0f,.3f,1f,2f,float.NaN}) {
    var s=new LampSettings{Color=hex};var (r,g,b)=s.Rgb();
    var (er,eg,eb)=LampLook.Emission(r,g,b,cd,max,fade);
    float cap=float.IsFinite(max)?Math.Clamp(max,.25f,4f):LampLook.MaxChannelDefault;
    float top=MathF.Max(er,MathF.Max(eg,eb));
    Check(float.IsFinite(top)&&top<=cap+1e-5f&&er>=0&&eg>=0&&eb>=0,"Brightest channel capped "+hex);
    float m=MathF.Max(r,MathF.Max(g,b));
    if(top>1e-6f&&m>0){Check(MathF.Abs(er/top-r/m)<1e-4f&&MathF.Abs(eg/top-g/m)<1e-4f&&MathF.Abs(eb/top-b/m)<1e-4f,"Hue ratio kept "+hex);}
    float a=LampLook.HaloAlpha(cd,.45f,fade);Check(a>=0&&a<=1,"Halo alpha bounded");
    colourChecks++;
   }
{
 var red=new LampSettings{Color="#FF1808"};var (r,g,b)=red.Rgb();
 var (er,eg,eb)=LampLook.Emission(r,g,b,80,LampLook.MaxChannelDefault,1);
 Check(MathF.Abs(er-1.6f)<1e-5f&&eg<.16f&&eb<.06f&&er>eg*10,"Brake red: R at cap 1.6, G/B stay below 10% -> stays red (0.4.3 sent R up to 150000 nits)");
 Check(LampLook.Brightness(8,1.6f)>=.8f&&LampLook.Brightness(0,1.6f)==0&&LampLook.Brightness(1e9f,1.6f)==1.6f,"Old 8 cd lamps still read lit; off is dark; huge values capped");
 var (ir,ig,ib)=LampLook.Emission(0,0,0,300,1.6f,1);Check(ir==0&&ig==0&&ib==0,"IR black emits nothing");
}
Check(LampLook.HaloDiameter(0,1)>LampLens.Radius[0]*2&&LampLook.HaloDiameter(2,float.NaN)>0,"Halo larger than lens; NaN scale contained");
// 0.4.5 lens transform: LampLens follows the native model chain (position + rotation * (local x signed scale)).
int lensChecks=0;
{
 N.Vector3 Rot(N.Quaternion q,N.Vector3 v){var u=new N.Vector3(q.X,q.Y,q.Z);var t=2*N.Vector3.Cross(u,v);return v+q.W*t+N.Vector3.Cross(u,t);} // independent Hamilton form
 bool Near(N.Vector3 a,N.Vector3 b,float e=2e-4f)=>(a-b).Length()<e;
 var M=new N.Vector3(-1,1,1); // vehicle mirror plane x=0
 var rng=new Random(4512);
 float R(float a,float b)=>a+(float)rng.NextDouble()*(b-a);
 for(int k=0;k<20000;k++) {
  int form=k%3;
  var q=N.Quaternion.Normalize(new N.Quaternion(R(-1,1),R(-1,1),R(-1,1),R(-1,1)));
  var p=new N.Vector3(R(-3,3),R(-1,3),R(-4,4));
  var s=new N.Vector3(R(.5f,2)*(rng.Next(2)==0?1:-1),R(.5f,2),R(.5f,2)*(k%7==0?-1:1));
  Check(LampLens.TryWorld(form,p,q,s,out var o),"lens pose accepted");
  var c=LampLens.Centre[form];float r=LampLens.Radius[form];
  // Independent world matrix (scale, then rotation, then translation) for centre, normal and rim.
  Check(Near(o.Centre,p+Rot(q,c*s)),"lens centre = model matrix * stock lens centre");
  var nInv=Rot(q,new N.Vector3(0,0,1/s.Z));nInv=N.Vector3.Normalize(nInv);
  Check(Near(o.Normal,nInv,1e-4f),"lens normal = inverse-transpose transform of +Z");
  Check(Near(o.Normal,Rot(o.Rotation,N.Vector3.UnitZ),1e-4f),"disc faces along the lens normal");
  Check(MathF.Abs(o.DiscScale.X-r*MathF.Abs(s.X))<1e-6f&&MathF.Abs(o.DiscScale.Y-r*MathF.Abs(s.Y))<1e-6f,"disc size follows part scale magnitude");
  Check(Near(o.LightOrigin,p+Rot(q,(c+new N.Vector3(0,0,LampLens.LightOffset))*s))&&Near(o.HaloPoint,p+Rot(q,(c+new N.Vector3(0,0,LampLens.HaloOffset))*s)),"light/halo on the lens axis in front of the glass");
  // Every rim point of the stock lens circle lies on the drawn disc rim (same set, sign-symmetric disc).
  for(int a=0;a<16;a++) {
   float th=a*MathF.Tau/16;
   var model=p+Rot(q,(c+r*new N.Vector3(MathF.Cos(th),MathF.Sin(th),0))*s);
   bool hit=false;
   for(int b=0;b<16&&!hit;b++){float ph=b*MathF.Tau/16;hit=Near(model,o.Centre+Rot(o.Rotation,new N.Vector3(o.DiscScale.X*MathF.Cos(ph),o.DiscScale.Y*MathF.Sin(ph),0)));}
   Check(hit,"stock lens rim point lies on the disc rim");
  }
  // Mirrored twin as native mirroring builds it: reflected position, reflected rotation (x,-y,-z,w), negative x scale.
  var tq=new N.Quaternion(q.X,-q.Y,-q.Z,q.W);
  Check(Near(Rot(tq,N.Vector3.UnitX),-(Rot(q,N.Vector3.UnitX)*M))&&Near(Rot(tq,N.Vector3.UnitY),Rot(q,N.Vector3.UnitY)*M)&&Near(Rot(tq,N.Vector3.UnitZ),Rot(q,N.Vector3.UnitZ)*M),"twin rotation = M R M (proper rotation; M R S = (M R M)(M S))");
  Check(LampLens.TryWorld(form,p*M,tq,s*M,out var t),"twin pose accepted");
  Check(Near(t.Centre,o.Centre*M)&&Near(t.Normal,o.Normal*M,1e-4f)&&Near(t.LightOrigin,o.LightOrigin*M)&&Near(t.HaloPoint,o.HaloPoint*M),"twin lens = mirror image of the original lens (negative scale)");
  for(int a=0;a<16;a++) {
   float th=a*MathF.Tau/16;
   var mirrored=(o.Centre+Rot(o.Rotation,new N.Vector3(o.DiscScale.X*MathF.Cos(th),o.DiscScale.Y*MathF.Sin(th),0)))*M;
   bool hit=false;
   for(int b=0;b<16&&!hit;b++){float ph=b*MathF.Tau/16;hit=Near(mirrored,t.Centre+Rot(t.Rotation,new N.Vector3(t.DiscScale.X*MathF.Cos(ph),t.DiscScale.Y*MathF.Sin(ph),0)));}
   Check(hit,"twin disc rim = mirror image of the original disc rim");
  }
  lensChecks++;
 }
 // Stock tail light lens is off-centre: a mirrored twin must put the glow on the twin's lens, not at the original x.
 Check(LampLens.TryWorld(1,new N.Vector3(.6f,1,-2),N.Quaternion.CreateFromAxisAngle(N.Vector3.UnitY,MathF.PI),new N.Vector3(1,1,1),out var tail)&&LampLens.TryWorld(1,new N.Vector3(-.6f,1,-2),new N.Quaternion(0,-1,0,0),new N.Vector3(-1,1,1),out var twin)&&Near(twin.Centre,tail.Centre*M)&&MathF.Abs(tail.Centre.X-(.6f+.0019f))<1e-4f,"tail-light twin glow sits on the mirrored off-centre lens");
 foreach(var bad in new[]{new N.Vector3(0,1,1),new N.Vector3(float.NaN,1,1),new N.Vector3(1,float.PositiveInfinity,1),new N.Vector3(1000,1,1)})Check(!LampLens.TryWorld(0,N.Vector3.Zero,N.Quaternion.Identity,bad,out _),"degenerate scale refused");
 Check(!LampLens.TryWorld(3,N.Vector3.Zero,N.Quaternion.Identity,N.Vector3.One,out _)&&!LampLens.TryWorld(0,N.Vector3.Zero,new N.Quaternion(0,0,0,0),N.Vector3.One,out _)&&!LampLens.TryWorld(0,new N.Vector3(float.NaN,0,0),N.Quaternion.Identity,N.Vector3.One,out _),"bad form/rotation/position refused");
 var (dv,dt)=LampLens.Disc();
 Check(dv.Length==LampLens.DiscSegments+1&&dt.Length==LampLens.DiscSegments*3&&dv.All(v=>v.Z==0&&MathF.Abs(v.Length()-(v==N.Vector3.Zero?0:1))<1e-5f),"unit disc mesh");
 for(int i=0;i<dt.Length;i+=3)Check(N.Vector3.Cross(dv[dt[i+1]]-dv[dt[i]],dv[dt[i+2]]-dv[dt[i]]).Z<0,"disc triangles clockwise seen from +Z (Unity front face toward the lens normal)");
 Check(LampLens.Radius[1]==LampLens.Radius[2]&&LampLens.Centre[1]==LampLens.Centre[2],"marker uses the stock tail-light lens");
}
Console.WriteLine($"PASS {lensChecks} random lens poses x (matrix, normal, disc rim, mirrored twin with negative scale)");
Console.WriteLine($"PASS {colourChecks} colour-retention cases (presets + edge colours x intensities x caps x fades)");
Console.WriteLine($"PASS {checks} Lamp checks: fake IO-API revision 1 / revision 0 / absent / partial / throwing; settings; 50ms fade; budgets; LOD. Native persistence remains unverified.");
