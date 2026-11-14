using SprocketSandboxTargets;

var count=0;
void Check(bool condition,string name) { if (!condition) throw new Exception("FAIL: " + name); count++; }
void Near(double a,double b,double tolerance,string name) => Check(Math.Abs(a-b)<tolerance,name+$" ({a:R} versus {b:R})");
double Angle(double x) => (x%360+540)%360-180;
var messages=new List<string>();
var defaults=TargetConfig.Parse(TargetConfig.DefaultJson,messages.Add);
Check(messages.Count==0 && defaults.Enabled && defaults.Targets.Length==4,"default JSON");
Check(defaults.Targets.Select(t=>t.Distance).SequenceEqual(new double[]{150,250,350,500}),"default distances");
Check(defaults.Targets.Select(t=>t.Lateral).SequenceEqual(new double[]{-15,15,-30,30}),"default lateral");
Check(defaults.Targets.All(t=>t.Blueprint==null && t.Facing==0),"default own/front");
foreach (var invalid in new[]{"{","null","[]","{}","{\"targets\":null}","{\"targets\":[{}]}",
 "{\"enabled\":0,\"targets\":[]}","{\"targets\":[{\"distance\":15,\"lateral\":15}]}",
 "{\"targets\":[{\"distance\":-1,\"lateral\":0}]}","{\"targets\":[{\"distance\":100,\"lateral\":0,\"facing\":\"bad\"}]}",
 "{\"targets\":[{\"distance\":1e400,\"lateral\":0}]}","{\"targets\":[{\"distance\":100,\"lateral\":0,\"blueprint\":1}]}"})
{
 messages.Clear();var c=TargetConfig.Parse(invalid,messages.Add);
 Check(c.Targets.SequenceEqual(TargetConfig.Defaults().Targets)&&messages.Count==1,"malformed fallback "+invalid);
}
var side=TargetConfig.Parse("{\"enabled\":false,\"targets\":[{\"distance\":200,\"lateral\":-50,\"blueprint\":\"Default/PZ4\",\"facing\":\"side\"}]}");
Check(!side.Enabled && side.Targets[0].Facing==90 && side.Targets[0].Blueprint=="Default/PZ4","side/custom/disabled");
var number=TargetConfig.Parse("{\"targets\":[{\"distance\":250,\"lateral\":15,\"facing\":-47.5}]}");
Check(number.Targets.Length==1 && number.Targets[0].Facing==-47.5,"numeric facing parsed from JSON");
Check(TargetConfig.Parse("{\"targets\":[]}").Targets.Length==0,"empty targets allowed");
foreach (var heading in new double[]{0,90,180,270,42.5,-90,721})
foreach (var spec in defaults.Targets.Append(new(100,0,null,0)).Append(new(100,-99.99,null,0)))
{
 var x=123.125;var z=-572.75;
 var pose=TargetGeometry.Place(x,z,heading,spec);
 var dx=pose.X-x;var dz=pose.Z-z;
 Near(Math.Sqrt(dx*dx+dz*dz),spec.Distance,.00000001,"Euclidean centre distance");
 var r=heading*Math.PI/180;
 Near(dx*Math.Cos(r)-dz*Math.Sin(r),spec.Lateral,.00000001,"signed right axis");
 Check(dx*Math.Sin(r)+dz*Math.Cos(r)>0,"ahead of player");
 Near(Angle(pose.Yaw-Math.Atan2(-dx,-dz)*180/Math.PI),0,.00000001,"front yaw looks at player");
 var floatDistance=Math.Sqrt(Math.Pow((float)pose.X-(float)x,2)+Math.Pow((float)pose.Z-(float)z,2));
 Near(floatDistance,spec.Distance,.01,"Unity float distance tolerance");
 var turned=TargetGeometry.Place(x,z,heading,spec with {Facing=90});
 Near(Angle(turned.Yaw-pose.Yaw),90,.00000001,"side=clockwise 90 (left side visible)");
 var numeric=TargetGeometry.Place(x,z,heading,spec with {Facing=-47.5});
 Near(Angle(numeric.Yaw-pose.Yaw),-47.5,.00000001,"numeric facing");
}
var random=new Random(1234);
for(var i=0;i<1000;i++)
{
 var d=1+random.NextDouble()*9999;var lat=(random.NextDouble()*1.98-.99)*d;
 var pose=TargetGeometry.Place(20,-30,random.NextDouble()*360,new(d,lat,null,0));
 Near(Math.Sqrt(Math.Pow((float)pose.X-20,2)+Math.Pow((float)pose.Z+30,2)),d,.01,"random float radius");
}
// Testbestanden komen buiten de bron: args[0] is de werkmap (Check-SandboxTargets.ps1 geeft die mee).
var scratch=Path.GetFullPath(args.Length>0 ? args[0] : Path.Combine(Path.GetTempPath(),"sandbox-targets-checks","scratch"));Directory.CreateDirectory(scratch);
var examplePath=Path.GetFullPath(args.Length>1 ? args[1] : Path.Combine(AppContext.BaseDirectory,"../../../../config/sprocket.sandboxtargets.example.json"));
var exampleMessages=new List<string>();
var example=TargetConfig.Parse(File.ReadAllText(examplePath),exampleMessages.Add);
Check(exampleMessages.Count==0 && example.Enabled && example.Targets.SequenceEqual(defaults.Targets),"example config file equals the built-in defaults");
var configPath=Path.Combine(scratch,"config.json");
File.WriteAllText(configPath,"{broken");
var before=File.ReadAllBytes(configPath);
Check(TargetConfig.Load(configPath,_=>{}).Targets.Length==4,"disk malformed fallback");
Check(File.ReadAllBytes(configPath).SequenceEqual(before),"broken JSON not overwritten");
File.WriteAllText(configPath,"{\"enabled\":false,\"targets\":[]}");
Check(!TargetConfig.Load(configPath,_=>{}).Enabled,"config reread without restart");
var newPath=Path.Combine(scratch,"first-start-"+Guid.NewGuid()+".json");
Check(TargetConfig.Load(newPath,_=>{}).Targets.Length==4 && File.Exists(newPath),"first start creates defaults");
var factions=Path.Combine(scratch,"Factions");
Check(TargetGeometry.BlueprintPath(factions,"Default/PZ4","Active").EndsWith(Path.Combine("Default","Blueprints","Vehicles","PZ4.blueprint")),"explicit faction path");
Check(TargetGeometry.BlueprintPath(factions,"PZ4","Active").EndsWith(Path.Combine("Active","Blueprints","Vehicles","PZ4.blueprint")),"current faction path");
foreach(var bad in new[]{"../secret","C:/foo","Default/../other","/root","Default//file","Default/..","Default/a:b"})
{
 var rejected=false;try{TargetGeometry.BlueprintPath(factions,bad,"Active");}catch(FormatException){rejected=true;}
 Check(rejected,"reject unsafe blueprint "+bad);
}
Console.WriteLine($"PASS: {count} assertions; production config and geometry linked directly.");
