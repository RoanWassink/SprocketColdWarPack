using SprocketExpandedBallistics.ExperimentalArmour;
namespace SprocketExpandedBallistics;
internal static class DynamicEraTests
{
 internal static void Run(Action<bool,string> check)
 {
  var json=File.ReadAllText(Path.Combine(AppContext.BaseDirectory,"dynamic-era-template.json"));
  var disabled=SprocketExpandedBallistics.ExperimentalArmour.ExperimentalArmour.Parse(json);check(disabled.Enabled,"valid rod catalogue active without legacy opt-in");
  var cfg=SprocketExpandedBallistics.ExperimentalArmour.ExperimentalArmour.Parse(json.Replace("\"enabled\": false","\"enabled\": true"));
  check(cfg.Enabled&&cfg.Recipes.Count==3,"exact three new recipes enabled");
  check(disabled.Recipes.All(p=>cfg.Recipes[p.Key].BaseLoss==p.Value.BaseLoss&&cfg.Recipes[p.Key].MaximumLoss==p.Value.MaximumLoss),"removing opt-in preserves recipe coefficients");
  try {SprocketExpandedBallistics.ExperimentalArmour.ExperimentalArmour.Parse(json.Replace("\"enabled\": false","\"enabled\": \"false\""));check(false,"legacy enable field still validates boolean type");}
  catch(FormatException){check(true,"legacy enable field still validates boolean type");}
  var g=new ImpactGeometry(.9,.03,10,.12);
  check(g.Valid&&Math.Abs(g.LengthToDiameter-30)<1e-10&&Math.Abs(g.BoreLengthRatio-7.5)<1e-12,"actual rod/core geometry distinct from bore");
  foreach(var r in cfg.Recipes.Values)
  {
   check(Math.Abs(ExperimentalRodModel.Loss(r,g,1)-r.BaseLoss)<1e-12,"reference LD endpoint "+r.ResponseId);
   check(ExperimentalRodModel.Loss(r,g,0)==0,"zero angle has no response");
   check(ExperimentalRodModel.Loss(r,g with{CoreDiameterM=0},1)==0,"invalid core safe");
   check(ExperimentalRodModel.Loss(r,g with{LengthM=double.PositiveInfinity},1)==0,"nonfinite geometry safe");
   check(ExperimentalRodModel.Loss(r,null,1)==0,"missing geometry safe");
   foreach(var ld in new[]{20d,40d,80d,1000d})
    check(Math.Abs(ExperimentalRodModel.Loss(r,g with{LengthM=ld*.03},1)-r.BaseLoss*Math.Min(1.6,Math.Max(.7,Math.Sqrt(ld/30))))<1e-12,"calibrated LD endpoint");
  }
  var recipe=cfg.Recipes["experimentalNizhCassette"];
  var loss=ExperimentalRodModel.Loss(recipe,g with{LengthM=2.4},1);
  var once=ExperimentalRodModel.Next(1,loss,.35);var twice=ExperimentalRodModel.Next(once,loss,.35);
  check(Math.Abs(once-.68)<1e-12&&Math.Abs(twice-.65)<1e-12,"two real layers share cap");
  check(ExperimentalRodModel.Next(.65,.32,.1)==.65,"lower cap never refunds lost budget");
  check(ExperimentalRodModel.Next(1,.32,.1)==.9,"lower cap limits first reaction");
  check(cfg.EffectiveKineticCap(.05)==.05,"legacy cap authoritative");
  for(int n=0;n<=100;n++)for(int k=0;k<=35;k++)
  {
   var old=1-n*.0035;var next=ExperimentalRodModel.Next(old,.32,k*.01);
   check(next<=old&&next>=.65-1e-12,"monotonic budget under all supported caps");
  }
  var fixture=File.ReadAllText(Path.Combine(AppContext.BaseDirectory,"armour-response-fixture.json"));
  var root=System.Text.Json.Nodes.JsonNode.Parse(fixture)!;
  using(var stream=typeof(ArmourResponses).Assembly.GetManifestResourceStream("EraPreset.kontakt5Cassette-response.json")!)
   root["responses"]!.AsArray().Add(System.Text.Json.Nodes.JsonNode.Parse(new StreamReader(stream).ReadToEnd()));
  root["enabled"]=true;
  var legacy=ArmourResponses.Parse(root.ToJsonString());var original=legacy.Responses.Single(r=>r.ResponseId=="kontakt5Cassette");
  var fresh=original with{ResponseId=recipe.ResponseId,Calibration=original.Calibration with{IntactRodRetention=1,DisturbedRodRetention=1}};
  var angle=original.Geometry.KineticAngularCurve!.First(x=>x.Weight>0).Input;
  var thickness=original.Geometry.MinNormalThicknessMm;
  var oldOnly=new ArmourShotState();var oldFactor=ArmourResponseModel.Apply(legacy,original,oldOnly,"old","apfsds",true,true,thickness,angle,0,true);
  var mixed=new ArmourShotState();var unchangedOld=ArmourResponseModel.Apply(legacy,original,mixed,"old","apfsds",true,true,thickness,angle,0,true);
  check(Math.Abs(oldFactor-.9)<1e-12&&oldFactor==unchangedOld,"old adapter exact unchanged");
  var before=mixed.AdditionalRetention;
  var expFactor=ArmourResponseModel.Apply(legacy,fresh,mixed,"new","apfsds",true,true,thickness,angle,0,true,.32,.1);
  check(expFactor==1&&Math.Abs(mixed.AdditionalRetention-.9)<1e-12,"old then experimental never restores");
  var reversed=new ArmourShotState();ArmourResponseModel.Apply(legacy,fresh,reversed,"new","apfsds",true,true,thickness,angle,0,true,.32,.1);
  var expOnly=reversed.AdditionalRetention;
  ArmourResponseModel.Apply(legacy,original,reversed,"old","apfsds",true,true,thickness,angle,0,true);
  check(Math.Abs(expOnly-.9)<1e-12&&Math.Abs(reversed.AdditionalRetention-.81)<1e-12,"experimental cap does not suppress later legacy response");
  var dupeBefore=reversed.AdditionalRetention;
  check(ArmourResponseModel.Apply(legacy,fresh,reversed,"new","apfsds",true,true,thickness,angle,0,true,.32,.35)==1&&reversed.AdditionalRetention==dupeBefore,"duplicate callback neutral");
  var spent=new ArmourShotState();check(ArmourResponseModel.Apply(legacy,fresh,spent,"spent","apfsds",true,true,thickness,angle,0,false,.32,.35)==1,"spent cell neutral");
  check(ExperimentalRodModel.ValidConversion(100,80,80,900,1000),"valid native conversion commits");
  check(!ExperimentalRodModel.ValidConversion(0,0,0,0,1000),"zero budget does not commit");
  check(!ExperimentalRodModel.ValidConversion(100,80,100,900,1000),"unchanged budget does not commit");
  check(!ExperimentalRodModel.ValidConversion(100,80,81,900,1000),"wrong inverse budget does not commit");
  check(!ExperimentalRodModel.ValidConversion(100,80,80,double.NaN,1000),"invalid speed does not commit");
  check(ExperimentalRodModel.ValidConversion(.000001,.0000008,.0000008,.001,.002),"near threshold positive conversion");
  check(!ExperimentalRodModel.ValidConversion(.000001,.0000008,0,.001,.002),"underflow cannot spend charge");
  // Current 0.2.56.1 disassembly: centimetre conversion, powers .75/1.43 and reciprocal.
  static float NativeBudget(float diameter,float mass,float velocity,ushort k)
  {
   var denom=(float)Math.Pow(diameter*1000f*.01f,.75f)*k;
   return (float)Math.Pow((float)Math.Sqrt(mass)*velocity/denom,1.43f)*100f*.001f*1000f;
  }
  static float NativeInverse(float diameterMm,float mass,float target,ushort k)=>
   (float)Math.Pow(diameterMm*.01f,.75f)*k*(float)Math.Pow(target*.01f,.6993007063865662f)/(float)Math.Sqrt(mass);
  foreach(var diameter in new[]{.01f,.03f,.07f})foreach(var mass in new[]{.5f,5f,20f})
   foreach(var velocity in new[]{100f,500f,2000f})foreach(var k in new ushort[]{1000,1800,5000})
    foreach(var factor in new[]{.65f,.8f,.9f})
    {
     var incoming=NativeBudget(diameter,mass,velocity,k);var target=incoming*factor;
     var speed=NativeInverse(diameter*1000f,mass,target,k);var outgoing=NativeBudget(diameter,mass,speed,k);
     check(ExperimentalRodModel.ValidConversion(incoming,target,outgoing,speed,velocity),"current native float reconstruction tolerance");
    }
  var snapshot=cfg;var refreshed=SprocketExpandedBallistics.ExperimentalArmour.ExperimentalArmour.Parse(json);
  check(snapshot.Enabled&&refreshed.Enabled&&snapshot.Revision!=refreshed.Revision,"immutable configuration snapshot survives refresh");
  check(ExperimentalRodModel.Supported("reliktCassette")&&!cfg.Recipes.ContainsKey("reliktCassette"),"old identity admitted only by explicit future model recipe");
 }
}

