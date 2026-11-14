using SprocketEngineExpansion.Core;
int checks=0;
void Check(bool ok,string why){checks++;if(!ok)throw new Exception(why);}
void Near(double a,double b,string why)=>Check(Math.Abs(a-b)<=1e-8*Math.Max(1,Math.Abs(b)),why);
// Profile dates: petrol 1914.07.28, diesel 1935.01.01, turbine 1967.01.01.
Check(!EngineDates.Allows(new DateTime(1934,12,31),EngineKind.Diesel),"diesel unavailable one day early");
Check(EngineDates.Allows(new DateTime(1935,1,1),EngineKind.Diesel),"diesel available on 1935.01.01");
Check(!EngineDates.Allows(new DateTime(1966,12,31),EngineKind.GasTurbine),"turbine unavailable one day early");
Check(EngineDates.Allows(new DateTime(1967,1,1),EngineKind.GasTurbine),"turbine available on 1967.01.01");
Check(EngineDates.Allows(new DateTime(9999,12,31),EngineKind.GasTurbine),"final-era sentinel makes the turbine available");
Check(EngineDates.Effective(new DateTime(1950,1,1),EngineKind.GasTurbine)==EngineKind.Petrol,"too-early saved turbine runs as native petrol");
Check(EngineDates.Effective(new DateTime(1950,1,1),EngineKind.Diesel)==EngineKind.Diesel,"diesel remains available in 1950");
Check(EngineDates.For(EngineKind.Petrol)==new DateTime(1914,7,28),"petrol keeps its 1914.07.28 floor");
var native=new Curve(new[]{new Point(0,300),new Point(.2,600),new Point(.5,1000),new Point(.8,900),new Point(1,700)});
foreach(var kind in Enum.GetValues<EngineKind>())
{
 var clean=new EnginePlan(new(kind),native,3000,600);
 foreach(var boost in new[]{0d,25d,215d,391d,400d,500d})
 {
  var saved=new EngineSelection(kind,kind!=EngineKind.GasTurbine,boost);
  string raw=SelectionCodec.Encode(saved);
  var doc=SelectionDocument.Read(raw);
  Check(doc.Editable&&doc.Selection==saved,"Legacy boosted selection remains readable");
  Check(doc.Save()==raw,"Untouched saved turbo selection preserved exactly");
  var dormant=new EnginePlan(saved,native,3000,600);
  for(int i=0;i<=400;i++)
  {
   double rpm=i*clean.MaxRPM/400;
   Near(dormant.Torque(rpm,true),clean.Torque(rpm,false),"No turbo torque for any supported boost/RPM");
   Near(dormant.Power(rpm,true),clean.Power(rpm,false),"No turbo power");
   Near(dormant.Gain(rpm,true),0,"No gain path");
   Near(dormant.FuelCoefficient(rpm,true),Catalog.Get(kind).Fuel,"Profile-only fuel");
  }
 }
 Near(clean.MaxRPM,3000*Catalog.Get(kind).RPM,"RPM profile retained");
 if(kind==EngineKind.Petrol)for(int i=0;i<=100;i++)Near(clean.Torque(i*30,true),native.At(i/100d),"Petrol native curve retained");
 else
 {
  var p=Catalog.Get(kind);
  Near(clean.Amplitude*p.Shape!.MaxProduct(clean.IdleRPM/clean.MaxRPM,1)*clean.MaxRPM/EnginePlan.NmRpmPerKW,native.MaxProduct(.2,1)*3000/EnginePlan.NmRpmPerKW*p.Power,"Profile peak calibration retained");
 }
}
string unknown="{\"Schema\":999,\"Turbo\":true,\"BoostPercent\":499}";
Check(!SelectionDocument.Read(unknown).Editable&&SelectionDocument.Read(unknown).Save()==unknown,"Unsupported save metadata retained exactly");
Near(DrivetrainRules.LengthScale(.5),Math.Pow(.5,1d/3),"Half volume length scale retained");
// Width cache: one build only, fallback never stored, reused pointer never inherits an old width.
var widthCache=new TransmissionExtentCache();
var measured=new TransmissionExtent(0,.2);
Check(widthCache.Remember((IntPtr)7,measured,new(0,.4))==measured,"A measured transmission width is cached");
Check(widthCache.Remember((IntPtr)7,new TransmissionExtent(0,.9),new(0,.4))==measured,"Within one build the measured width is reused");
Check(widthCache.Remember((IntPtr)8,null,new(0,.4))==new TransmissionExtent(0,.4),"Missing data uses the fallback");
Check(!widthCache.TryGet((IntPtr)8,out _),"The fallback is never cached");
Check(widthCache.Remember((IntPtr)8,new TransmissionExtent(0,.3),new(0,.4))==new TransmissionExtent(0,.3),"A later measurement is accepted after a fallback");
widthCache.BeginBuild();
Check(!widthCache.TryGet((IntPtr)7,out _)&&!widthCache.TryGet((IntPtr)8,out _),"A new layout build starts without old widths");
Check(widthCache.Remember((IntPtr)7,new TransmissionExtent(0,.6),new(0,.4))==new TransmissionExtent(0,.6),"A reused module pointer gets the new build's width");
Near(DrivetrainRules.Angle(5,2),10,"Native default angle doubled");
Near(DrivetrainRules.Angle(0,2),0,"Strict zero angle retained");
var route=RouteState.Encode(2.5);Near(RouteState.Decode(route),2.5,"Legacy route metadata round trip");
var marker=new AssemblyMarker(true,256);Check(AssemblyMarker.Decode(marker.Encode())==marker,"Legacy assembly metadata round trip");
// Same native technology/displacement baseline feeds all types. The adapter
// scales resolved torque once; it must never square a technology coefficient.
foreach(double baselineScale in new[]{.2,1,2.5,8})
foreach(double maxRPM in new[]{1200d,3000d,6000d})
{
 var baseline=new Curve(native.Points.Select(p=>new Point(p.X,p.Y*baselineScale)));
 foreach(var kind in Enum.GetValues<EngineKind>())
 {
  var plan=new EnginePlan(new(kind),baseline,maxRPM,maxRPM*.2);
  var sample=plan.Sample(false);
  double nativePeak=baseline.MaxProduct(.2,1)*maxRPM/EnginePlan.NmRpmPerKW;
  double previewPeak=sample.MaxProduct(plan.IdleRPM/plan.MaxRPM,1)*plan.MaxRPM/EnginePlan.NmRpmPerKW;
  Check(Math.Abs(previewPeak/nativePeak-Catalog.Get(kind).Power)<.003,"Profile sampled peak agrees with native baseline across sizes/RPM");
  Check(sample.Points.All(p=>double.IsFinite(p.Y)&&p.Y>0),"Type curve has finite positive startup/idle/operating torque");
  Near(plan.FuelCoefficient(plan.IdleRPM,false),Catalog.Get(kind).Fuel,"Fuel applies profile multiplier exactly once");
  var doubled=new EnginePlan(new(kind),new(baseline.Points.Select(p=>new Point(p.X,p.Y*2))),maxRPM,maxRPM*.2);
  Near(doubled.Torque(plan.MaxRPM*.6,false),plan.Torque(plan.MaxRPM*.6,false)*2,"Native torque or technology scaling remains linear");
 }
}
var dieselPlan=new EnginePlan(new(EngineKind.Diesel),native,3000,600);
var turbinePlan=new EnginePlan(new(EngineKind.GasTurbine),native,3000,600);
Check(dieselPlan.MaxRPM<3000&&turbinePlan.MaxRPM>3000,"Diesel lower safe RPM and turbine higher safe RPM remain distinct");
Check(turbinePlan.FuelCoefficient(2000,false)>2&&dieselPlan.FuelCoefficient(2000,false)<1,"Turbine fuel penalty and diesel efficiency retained");
Check(dieselPlan.Torque(1200,false)>new EnginePlan(new(),native,3000,600).Torque(1200,false),"Diesel supplies higher low-speed torque at same native engine size");
// Powerpack 0.2.0: selection schema 1 <-> 2.
string v1=SelectionCodec.Encode(new EngineSelection(EngineKind.Diesel));
Check(v1=="{\"Schema\":1,\"Kind\":1,\"Turbo\":false,\"BoostPercent\":25}","No powerpack keeps the 0.1.1 schema-1 bytes");
Check(SelectionCodec.Decode(v1).Powerpack==false,"Schema 1 reads as Powerpack=false");
string realV1="{\"Schema\":1,\"Kind\":2,\"Turbo\":false,\"BoostPercent\":25}";
Check(SelectionCodec.Decode(realV1)==new EngineSelection(EngineKind.GasTurbine),"Saved 0.1.1 payload (T-80BVM) still reads");
var pp=new EngineSelection(EngineKind.GasTurbine,false,25,true);
string v2=SelectionCodec.Encode(pp);
Check(v2=="{\"Schema\":2,\"Kind\":2,\"Turbo\":false,\"BoostPercent\":25,\"Powerpack\":true}","Powerpack writes schema 2");
Check(SelectionCodec.Decode(v2)==pp,"Schema 2 round trip");
var petrolPP=SelectionDocument.Default.Edit(new EngineSelection(Powerpack:true));
Check(petrolPP.Save()=="{\"Schema\":2,\"Kind\":0,\"Turbo\":false,\"BoostPercent\":25,\"Powerpack\":true}","Petrol powerpack is saved, not treated as native");
Check(SelectionDocument.Default.Edit(new EngineSelection()).Save()==null,"Plain petrol still writes nothing");
Check(SelectionDocument.Read(v2).Save()==v2,"Untouched schema 2 preserved exactly");
foreach(var bad in new[]{"{\"Schema\":2,\"Kind\":0,\"Turbo\":false,\"BoostPercent\":25}","{\"Schema\":1,\"Kind\":0,\"Turbo\":false,\"BoostPercent\":25,\"Powerpack\":true}","{\"Schema\":3,\"Kind\":0,\"Turbo\":false,\"BoostPercent\":25,\"Powerpack\":true,\"X\":1}","{\"Kind\":0}"})
{var doc=SelectionDocument.Read(bad);Check(!doc.Editable&&doc.Save()==bad,"Unknown or malformed schema preserved byte for byte: "+bad);}
// Date gate on the vehicle date only, with the final-era sentinel.
Check(EngineDates.PowerpackDate==new DateTime(1965,1,1),"Default powerpack date 1965.01.01");
Check(!EngineDates.AllowsPowerpack(new DateTime(1964,12,31)),"Powerpack unavailable on 1964.12.31");
Check(EngineDates.AllowsPowerpack(new DateTime(1965,1,1)),"Powerpack available on 1965.01.01");
Check(EngineDates.AllowsPowerpack(new DateTime(9999,12,31)),"Final-era sentinel allows the powerpack");
Check(!EngineDates.AllowsPowerpack(null),"Unknown date offers no new powerpack");
Check(EngineDates.PowerpackEffective(new DateTime(1964,12,31),true)==false&&EngineDates.PowerpackEffective(new DateTime(1965,1,1),true),"Saved powerpack effective from the date");
Check(EngineDates.PowerpackEffective(null,true)&&!EngineDates.PowerpackEffective(new DateTime(1990,1,1),false),"Unknown date keeps a saved powerpack; unsaved is never effective");
Check(EngineDates.ParseDate("1965.01.01",default)==new DateTime(1965,1,1)&&EngineDates.ParseDate("9999.12.31",default)==new DateTime(9999,12,31)&&EngineDates.ParseDate("1965-01-01x",new DateTime(1,1,1))==new DateTime(1,1,1),"Config date parsing");
EngineDates.PowerpackDate=EngineDates.ParseDate("9999.12.31",EngineDates.DefaultPowerpackDate);
Check(!EngineDates.AllowsPowerpack(new DateTime(1990,1,1))&&EngineDates.AllowsPowerpack(new DateTime(9999,12,31)),"AvailableFrom 9999.12.31 leaves only the final era");
EngineDates.PowerpackDate=EngineDates.DefaultPowerpackDate;
// Housing fractions.
Near(PowerpackRules.WithHousing(1345,.10,true),1479.5,"T-72A engine 1345 kg + 10% housing");
Near(PowerpackRules.WithHousing(1345*.6075,.15,true),1345*.6075*1.15,"Material cost + 15% housing");
Near(PowerpackRules.WithHousing(1345,.10,false),1345,"Not effective: no housing");
Near(PowerpackRules.WithHousing(1345*1.15,.10,true),1345*1.15*1.1,"Housing on top of the diesel mass factor");
foreach(var f in new[]{-.01,1.01,double.NaN}){bool threw=false;try{PowerpackRules.Fraction(f,"x");}catch(ArgumentOutOfRangeException){threw=true;}Check(threw,"Fraction outside 0..1 refused");}
// Flags on a reproduced T-72A (clutch braking) layout: engine 0 -> transmission 1 -> junctions 4,5 -> propulsion 2,3.
var t72=new List<LayoutModule>{new(0,ModuleKind.Engine,0),new(1,ModuleKind.Transmission,0),new(4,ModuleKind.Junction,6160),new(5,ModuleKind.Junction,6160),new(2,ModuleKind.Propulsion,10),new(3,ModuleKind.Propulsion,12)};
var t72c=new List<LayoutConnection>{new(8,0,1,0),new(11,1,4,0),new(14,4,2,1),new(17,1,5,0),new(20,5,3,1)};
var ppPlan=PowerpackRules.Find(t72,t72c,0);
Check(ppPlan==new PowerpackPlan(PowerpackStatus.Ready,1,8,false),"T-72A: engine feeds transmission 1 through connection 8");
var on=PowerpackRules.Apply(0,0);
Check(on==((short)0x1C00,(byte)1),"Aanvinken: PositionBetweenInputs on the transmission, Abstracted on the connection");
t72[1]=t72[1] with {Flags=on.Module};t72c[0]=t72c[0] with {Flags=on.Connection};
Check(PowerpackRules.Find(t72,t72c,0)==new PowerpackPlan(PowerpackStatus.Ready,1,8,true),"Marked layout recognised");
var off=PowerpackRules.Restore(on.Module,on.Connection);
Check(off==((short)0,(byte)0),"Uitvinken restores the original flags exactly");
Check(PowerpackRules.Restore(unchecked((short)(0x1C00|2)),(byte)(1|2))==((short)2,(byte)2),"Restore leaves LeftSided and TooLong alone");
Check(t72.Where(m=>m.Id!=1).All(m=>m.Flags==new Dictionary<int,short>{{0,0},{4,6160},{5,6160},{2,10},{3,12}}[m.Id]),"No other module flags touched");
// Leopard 2 (twin): engine 0 -> junction 1 (PositionBetweenOutputs) -> transmissions 2,3.
var leo=new List<LayoutModule>{new(0,ModuleKind.Engine,0),new(1,ModuleKind.Junction,112),new(2,ModuleKind.Transmission,2),new(3,ModuleKind.Transmission,4)};
var leoc=new List<LayoutConnection>{new(10,0,1,0),new(13,1,2,0),new(22,1,3,0)};
Check(PowerpackRules.Find(leo,leoc,0).Status==PowerpackStatus.ViaJunction,"Twin is reported as fed via a junction (toggle disabled)");
Check(PowerpackRules.Find(t72,new List<LayoutConnection>(),0).Status==PowerpackStatus.NoDirectTransmission,"Unconnected engine refused");
var conflict=new List<LayoutModule>{new(0,ModuleKind.Engine,0),new(1,ModuleKind.Transmission,0x10)};
Check(PowerpackRules.Find(conflict,new List<LayoutConnection>{new(8,0,1,0)},0).Status==PowerpackStatus.Conflict,"Existing auto-position bits are not taken over");
Check(PowerpackRules.Find(new List<LayoutModule>{new(0,ModuleKind.Engine,0),new(1,ModuleKind.Transmission,0)},new List<LayoutConnection>{new(8,0,1,1)},0).Status==PowerpackStatus.Conflict,"An already abstracted connection is not taken over");
// Twin 0.2.1: the Leopard 2a4 dump, engine 0 -> splitter 1 (0x70) -> transmissions 2 and 3.
var twinModules=new List<LayoutModule>{new(0,ModuleKind.Engine,0),new(1,ModuleKind.Junction,0x70),new(2,ModuleKind.Transmission,2,3),new(3,ModuleKind.Transmission,4,2)};
var twinConnections=new List<LayoutConnection>{new(10,0,1,0),new(13,1,2,0),new(22,1,3,0)};
var twinPlan=PowerpackRules.FindTwin(twinModules,twinConnections,0);
Check(twinPlan==new TwinPowerpackPlan(PowerpackStatus.Ready,0,1,2,3,10,13,22,false),"Leopard 2: twin plan names both modules and all three connections");
Check(twinPlan.ModuleIds.SequenceEqual(new[]{2,3})&&twinPlan.ConnectionIds.SequenceEqual(new[]{10,13,22}),"Twin plan lists are ordered left/right and engine/sides");
var twinOn=twinModules.Select(m=>m.Id is 2 or 3?m with {Flags=PowerpackRules.Apply(m.Flags,0).Module}:m).ToList();
twinOn[1]=twinOn[1] with {Flags=0x70}; // native splitter stays untouched and is not a conflict
var twinOnConnections=twinConnections.Select(c=>c with {Flags=PowerpackRules.Apply(0,c.Flags).Connection}).ToList();
var markedTwin=PowerpackRules.FindTwin(twinOn,twinOnConnections,0);
Check(markedTwin.Status==PowerpackStatus.Ready&&markedTwin.Marked,"Saved marked twin layout is recognised");
Check(markedTwin.LeftTransmissionId==2&&markedTwin.RightTransmissionId==3,"Marked twin keeps the mirror pair");
var restoredConnections=twinOnConnections.Select(c=>c with {Flags=PowerpackRules.Restore(0,c.Flags).Connection}).ToList();
var restoredModules=twinOn.Select(m=>m.Id is 2 or 3?m with {Flags=PowerpackRules.Restore(m.Flags,0).Module}:m).ToList();
Check(PowerpackRules.FindTwin(restoredModules,restoredConnections,0)==new TwinPowerpackPlan(PowerpackStatus.Ready,0,1,2,3,10,13,22,false),"Twin restore returns the original flags exactly and is recognised as unmarked");
Check(restoredModules.Where(m=>m.Id is 2 or 3).All(m=>m.Flags is 2 or 4),"Twin restore leaves the native Left/RightSided bits alone");
Check(restoredConnections.All(c=>c.Flags==0),"Twin restore clears only Abstracted on all three connections");
// The native 0x70 splitter is not a conflict, but other auto-position bits are.
Check(PowerpackRules.FindTwin(twinModules,twinConnections,0).Status==PowerpackStatus.Ready,"Native splitter 0x70 does not count as a conflict");
var badSplitter=twinModules.Select(m=>m.Id==1?m with {Flags=0x1C70}:m).ToList();
Check(PowerpackRules.FindTwin(badSplitter,twinConnections,0).Status==PowerpackStatus.Conflict,"An input-auto splitter is refused");
var badLeft=twinModules.Select(m=>m.Id==2?m with {Flags=0x10}:m).ToList();
Check(PowerpackRules.FindTwin(badLeft,twinConnections,0).Status==PowerpackStatus.Conflict,"Existing auto-position bits on a twin transmission are refused");
var badConnection=twinConnections.Select(c=>c.Id==13?c with {Flags=1}:c).ToList();
Check(PowerpackRules.FindTwin(twinModules,badConnection,0).Status==PowerpackStatus.Conflict,"An already abstracted splitter-to-transmission connection is refused");
var partial=twinModules.Select(m=>m.Id==2?m with {Flags=0x1C02}:m).ToList();
Check(PowerpackRules.FindTwin(partial,twinConnections,0).Status==PowerpackStatus.Conflict,"A partially marked saved twin layout is refused");
var noMirror=twinModules.Select(m=>m.Id is 2 or 3?m with {MirrorId=-1}:m).ToList();
Check(PowerpackRules.FindTwin(noMirror,twinConnections,0).Status==PowerpackStatus.NoDirectTransmission,"A junction feeding non-mirrored transmissions is not a twin plan");
var extraInput=twinConnections.Append(new LayoutConnection(99,0,1,0)).ToList();
Check(PowerpackRules.FindTwin(twinModules,extraInput,0).Status==PowerpackStatus.NoDirectTransmission,"A splitter with two inputs is not a twin plan");
// Twin geometry from the same dump. The input port is on the inner face; the local X width is 0.4 m.
var leoEnginePos=new Vec3(0.0233625174,0.114204019,-2.826404);
var leoEngineRot=Quat.AxisY(358.380676);
var leoOutput=leoEnginePos+leoEngineRot.Rotate(new Vec3(0,0.308080316,0.6232929));
var leoDirection=leoEngineRot.Rotate(new Vec3(0,0,1));
var leoInput=new Vec3(0,0.09434723,-0.4);
var leoLeftPos=new Vec3(-0.465239823,0.25,1.52446365);
var leoRightPos=new Vec3(0.465239823,0.25,1.52446365);
var extent=new TransmissionExtent(0,.2);
var leftGeometry=PowerpackRules.TwinOrigin(leoOutput,leoDirection,leoLeftPos,Quat.AxisY(270),leoInput,extent);
var rightGeometry=PowerpackRules.TwinOrigin(leoOutput,leoDirection,leoRightPos,Quat.AxisY(90),leoInput,extent);
Check(Math.Abs(Vec3.Dot(leftGeometry.MotorFace-leoOutput,leoDirection))<.01,"Left gearbox motor side is on the engine output plane (within 1 cm)");
Check(Math.Abs(Vec3.Dot(rightGeometry.MotorFace-leoOutput,leoDirection))<.01,"Right gearbox motor side is on the engine output plane (within 1 cm)");
Check(Math.Abs(leftGeometry.Input.Y-leoOutput.Y)<1e-9&&Math.Abs(rightGeometry.Input.Y-leoOutput.Y)<1e-9,"Both gearbox input ports have the engine output height");
var leoLateral=new Vec3(1,0,0);
Near(Vec3.Dot(leftGeometry.Origin-leoOutput,leoLateral),Vec3.Dot(leoLeftPos-leoOutput,leoLateral),"Left lateral coordinate is kept");
Near(Vec3.Dot(rightGeometry.Origin-leoOutput,leoLateral),Vec3.Dot(leoRightPos-leoOutput,leoLateral),"Right lateral coordinate is kept");
var nativeMid=(leoLeftPos+leoRightPos)*.5;var newMid=(leftGeometry.Origin+rightGeometry.Origin)*.5;Near(Vec3.Dot(newMid-leoOutput,leoLateral),Vec3.Dot(nativeMid-leoOutput,leoLateral),"Twin pair keeps its chosen lateral coordinate");Check(Math.Abs(Vec3.Dot(newMid-leoOutput,leoDirection)-.2)<.01,"Twin pair sits about one half-width in front of the engine output");
var nativeLeftInput=leoLeftPos+Quat.AxisY(270).Rotate(leoInput);var nativeRightInput=leoRightPos+Quat.AxisY(90).Rotate(leoInput);var nativeInputMid=(nativeLeftInput+nativeRightInput)*.5;var newInputMid=(leftGeometry.Input+rightGeometry.Input)*.5;Near(Vec3.Dot(newInputMid-leoOutput,leoLateral),Vec3.Dot(nativeInputMid-leoOutput,leoLateral),"Twin input ports keep their chosen lateral coordinate");Check(Math.Abs(Vec3.Dot(newInputMid-leoOutput,leoDirection)-.2)<.01,"Twin input midpoint sits about one half-width in front of the engine output");
Check(leftGeometry.Origin.Z<leoLeftPos.Z&&rightGeometry.Origin.Z<leoRightPos.Z,"Both gearboxes move back towards the engine from their native twin position");
// The user's Leopard 2a4-n has a different transmission input stub; the same rule must hold.
var a4nEnginePos=new Vec3(0.0127647324,0.140205875,-2.45152617);
var a4nEngineRot=Quat.AxisY(358.380676);
var a4nOutput=a4nEnginePos+a4nEngineRot.Rotate(new Vec3(0,0.2701711,0.7286987));
var a4nDirection=a4nEngineRot.Rotate(new Vec3(0,0,1));
var a4nInput=new Vec3(0,0.186501727,-0.333772928);
var a4nGeometry=PowerpackRules.TwinOrigin(a4nOutput,a4nDirection,leoLeftPos,Quat.AxisY(270),a4nInput,extent);
Check(Math.Abs(Vec3.Dot(a4nGeometry.MotorFace-a4nOutput,a4nDirection))<.01,"Leopard 2a4-n motor side is on the engine output plane");
Check(Math.Abs(a4nGeometry.Input.Y-a4nOutput.Y)<1e-9,"Leopard 2a4-n input port has the engine output height");
Check(Math.Abs(a4nGeometry.Origin.X-leoLeftPos.X)<1e-9,"Leopard 2a4-n lateral coordinate is kept");
// Any engine yaw: the motor face stays on the plane and the lateral coordinate is kept.
foreach(double yaw in new[]{0d,17,45,135,180,358.380676})
{
 var rot=Quat.AxisY(yaw);var engineOutput=leoEnginePos+rot.Rotate(new Vec3(0,0.308080316,0.6232929));var direction=rot.Rotate(new Vec3(0,0,1));
 var g=PowerpackRules.TwinOrigin(engineOutput,direction,leoLeftPos,Quat.AxisY(270),leoInput,extent);
 Check(Math.Abs(Vec3.Dot(g.MotorFace-engineOutput,direction))<1e-6,"Twin motor face follows engine yaw "+yaw);
 Check(Math.Abs(g.Input.Y-engineOutput.Y)<1e-6,"Twin input height follows the engine output at yaw "+yaw);
}
// Unticking widens the pair so both restored splitter shafts and the engine shaft reach the native minimum.
var splitterInput=new Vec3(7.559804e-05,0.00102484226,-0.0490193367);
var restore=PowerpackRules.RestoredTwinPositions(leftGeometry.Origin,Quat.AxisY(270),leoInput,rightGeometry.Origin,Quat.AxisY(90),leoInput,Quat.AxisY(0),splitterInput,leoOutput,leoDirection,0.1);
var restoredLeft=restore.Left+Quat.AxisY(270).Rotate(leoInput);
var restoredRight=restore.Right+Quat.AxisY(90).Rotate(leoInput);
var restoredCentre=(restoredLeft+restoredRight)*.5;
Check((restoredLeft-restoredCentre).Length>=0.1-1e-9,"Restored left splitter shaft is at least MinDriveShaftLength");
Check((restoredRight-restoredCentre).Length>=0.1-1e-9,"Restored right splitter shaft is at least MinDriveShaftLength");
Check((restoredCentre+splitterInput-leoOutput).Length>=0.1-1e-9,"Restored engine-to-splitter shaft is at least MinDriveShaftLength");
Near(restoredCentre.X,(restoredLeft.X+restoredRight.X)/2,"Restored splitter centre is the midpoint of the pair");
Check((restoredLeft-restoredCentre).Length>0.09&&(restoredRight-restoredCentre).Length>0.09,"Restored shafts are sane, not zero length");
foreach(double yaw in new[]{0d,31,135,180})
{
 var rot=Quat.AxisY(yaw);var engineOutput=leoEnginePos+rot.Rotate(new Vec3(0,0.308080316,0.6232929));var direction=rot.Rotate(new Vec3(0,0,1));
 var l=PowerpackRules.TwinOrigin(engineOutput,direction,leoLeftPos,Quat.AxisY(270),leoInput,extent);
 var r=PowerpackRules.TwinOrigin(engineOutput,direction,leoRightPos,Quat.AxisY(90),leoInput,extent);
 var restored=PowerpackRules.RestoredTwinPositions(l.Origin,Quat.AxisY(270),leoInput,r.Origin,Quat.AxisY(90),leoInput,Quat.AxisY(yaw),splitterInput,engineOutput,direction,0.1);
 var li=restored.Left+Quat.AxisY(270).Rotate(leoInput);var ri=restored.Right+Quat.AxisY(90).Rotate(leoInput);var c=(li+ri)*.5;
 Check((li-c).Length>=0.1-1e-9&&(ri-c).Length>=0.1-1e-9&&(c+rot.Rotate(splitterInput)-engineOutput).Length>=0.1-1e-9,"Restored twin shafts respect the minimum at yaw "+yaw);
}
// Flush placement with the T-72A numbers from the blueprint (engine and transmission both turned 180 degrees).
var engineRot=Quat.AxisY(180);var enginePos=new Vec3(1.07021236e-07,0.147543371,-1.97650039);
var output=enginePos+engineRot.Rotate(new Vec3(0,0.187079281,0.4848786));
var transRot=Quat.AxisY(180);var inputLocal=new Vec3(0,0.119487941,-0.332176477);
var origin=PowerpackRules.FlushOrigin(output,transRot,inputLocal);
var inputWorld=origin+transRot.Rotate(inputLocal);
Check((inputWorld-output).Length<1e-9,"Transmission input port lands exactly on the engine output");
Check(Math.Abs(origin.Z-(-2.793555))<1e-4&&Math.Abs(origin.Y-0.215134)<1e-4,"T-72A powerpack origin 0.10 m ahead of the native shaft position (-2.8947)");
Check((output-origin).Length<1.0,"Abstracted connection within the native 1 m ValidateLayout limit");
foreach(double deg in new[]{0d,37,90,180,271}){var q=Quat.AxisY(deg);var o=PowerpackRules.FlushOrigin(output,q,inputLocal);Check(((o+q.Rotate(inputLocal))-output).Length<1e-9,"Flush for any transmission rotation "+deg);}
Near(Quat.AxisY(90).Rotate(new Vec3(0,0,1)).X,1,"Quaternion convention matches Unity (+Z turns to +X at +90 deg yaw)");
// 0.2.2 obstruction fix. The native box-box narrow phase has no epsilon and treats touching as
// overlap, so the required clearance is any value above zero; the mod uses 1 mm.
Check(PowerpackRules.ObstructionClearance>0,"Native touching counts, so the clearance must be positive");
var unitBox=new OrientedBox(new Vec3(0,0,0),new Vec3(2,2,2),new Quat(0,0,0,1));
var touchingBox=new OrientedBox(new Vec3(2,0,0),new Vec3(2,2,2),new Quat(0,0,0,1));
Check(unitBox.Intersects(touchingBox),"Native replica: touching faces count as overlap");
Check(!unitBox.Intersects(touchingBox with {Centre=new Vec3(2.0005,0,0)}),"Native replica: 0.5 mm gap is free");
Check(!unitBox.Intersects(touchingBox with {Centre=new Vec3(2.01,0,0)}),"Native replica: 1 cm gap is free");
var yawedLong=new OrientedBox(new Vec3(0,0,0),new Vec3(2,2,2),Quat.AxisY(45));
Check(yawedLong.MaxProjection(new Vec3(0,0,1))>1.2,"A rotated engine box projects further than its unrotated face");
Check(!new OrientedBox(new Vec3(0,0,0),new Vec3(2,2,2),Quat.AxisY(45))
    .Intersects(new OrientedBox(new Vec3(0,0,10),new Vec3(2,2,2),Quat.AxisY(20))),"Native replica: a far rotated box is free");
// The user's Leopard 2a4-n pose: yaw 358.380676. The synthetic engine box protrudes 5 cm past
// the engine output plane, which is exactly why the old face-on-the-plane rule obstructed.
var obsEnginePos=new Vec3(0.0127647324,0.140205875,-2.45152617);
var obsEngineRot=Quat.AxisY(358.380676);
var obsOutput=obsEnginePos+obsEngineRot.Rotate(new Vec3(0,0.2701711,0.7286987));
var obsDirection=obsEngineRot.Rotate(new Vec3(0,0,1));
var obsInput=new Vec3(0,0.186501727,-0.333772928);
var obsEngineBox=new OrientedBox(
    obsOutput+obsDirection*(-0.45)+new Vec3(0,0.15,0),new Vec3(1.2,0.9,1.0),obsEngineRot);
var obsExtent=new TransmissionExtent(0,0.2,new Vec3(0,0.1,0),new Vec3(0.4,0.6,0.5));
Check(obsEngineBox.MaxProjection(obsDirection)>Vec3.Dot(obsOutput,obsDirection)+0.04,
    "Synthetic engine obstruction box reaches past the engine output plane");
var obsLeftStart=new Vec3(-0.465239823,0.25,1.52446365);
var obsRightStart=new Vec3(0.465239823,0.25,1.52446365);
var obsLeftRot=Quat.AxisY(270);var obsRightRot=Quat.AxisY(90);
var obsLeftOld=PowerpackRules.TwinOrigin(obsOutput,obsDirection,obsLeftStart,obsLeftRot,obsInput,obsExtent);
var obsRightOld=PowerpackRules.TwinOrigin(obsOutput,obsDirection,obsRightStart,obsRightRot,obsInput,obsExtent);
var obsOldLeftBox=new OrientedBox(obsLeftOld.Origin+obsLeftRot.Rotate(obsExtent.Centre),obsExtent.Size,obsLeftRot);
Check(obsOldLeftBox.Intersects(obsEngineBox),"The old face-on-the-output-plane placement overlaps the engine collider");
var obsLeft=PowerpackRules.TwinOriginAgainst(obsOutput,obsDirection,obsLeftStart,obsLeftRot,obsInput,obsExtent,obsEngineBox,PowerpackRules.ObstructionClearance);
var obsRight=PowerpackRules.TwinOriginAgainst(obsOutput,obsDirection,obsRightStart,obsRightRot,obsInput,obsExtent,obsEngineBox,PowerpackRules.ObstructionClearance);
var obsLeftBox=new OrientedBox(obsLeft.Origin+obsLeftRot.Rotate(obsExtent.Centre),obsExtent.Size,obsLeftRot);
var obsRightBox=new OrientedBox(obsRight.Origin+obsRightRot.Rotate(obsExtent.Centre),obsExtent.Size,obsRightRot);
Check(!obsLeftBox.Intersects(obsEngineBox),"0.2.2 left gearbox is clear of the rotated engine box");
Check(!obsRightBox.Intersects(obsEngineBox),"0.2.2 right gearbox is clear of the rotated engine box");
Near(obsLeft.Clearance,PowerpackRules.ObstructionClearance,"Left clearance is the native minimum");
Near(obsRight.Clearance,PowerpackRules.ObstructionClearance,"Right clearance is the native minimum");
Near(obsLeftBox.MinProjection(obsDirection)-obsEngineBox.MaxProjection(obsDirection),PowerpackRules.ObstructionClearance,"Left gap equals the clearance along the engine direction");
Near(obsRightBox.MinProjection(obsDirection)-obsEngineBox.MaxProjection(obsDirection),PowerpackRules.ObstructionClearance,"Right gap equals the clearance along the engine direction");
Near(obsLeftBox.MinProjection(obsDirection),obsRightBox.MinProjection(obsDirection),"Both gearboxes get the same depth along the engine direction");
Near(obsLeft.Origin.X,obsLeftStart.X,"Left lateral coordinate is kept by the obstruction placement");
Near(obsRight.Origin.X,obsRightStart.X,"Right lateral coordinate is kept by the obstruction placement");
Near(obsLeft.Origin.X,-obsRight.Origin.X,"The mirror pair stays laterally symmetric");
Check(Math.Abs(obsLeft.Input.Y-obsOutput.Y)<1e-9&&Math.Abs(obsRight.Input.Y-obsOutput.Y)<1e-9,"Both input ports keep the engine output height");
Check(!obsLeftBox.Intersects(obsRightBox),"The two transmission boxes do not overlap each other");
// Full centre/size on all three axes matters: shifting the local Y/Z centre shifts the placed box.
var shiftedExtent=obsExtent with {Centre=obsExtent.Centre+new Vec3(0,0.05,0.05)};
var shifted=PowerpackRules.TwinOriginAgainst(obsOutput,obsDirection,obsLeftStart,obsLeftRot,obsInput,shiftedExtent,obsEngineBox,PowerpackRules.ObstructionClearance);
var shiftedBox=new OrientedBox(shifted.Origin+obsLeftRot.Rotate(shiftedExtent.Centre),shiftedExtent.Size,obsLeftRot);
Near(shiftedBox.MinProjection(obsDirection)-obsEngineBox.MaxProjection(obsDirection),PowerpackRules.ObstructionClearance,"An off-centre 3D transmission box still lands at the clearance");
// 0.2.3 space fix. Layout space is the VehiclePowertrain Transform (native SetModuleTransform:
// InverseTransformPoint and Inverse(rotation) * rotation). The engine collider is read in world
// space and must go back through that Transform before it meets module and port positions.
var unitQ=new Quat(0,0,0,1);
foreach(var q in new[]{Quat.AxisY(3),Quat.AxisY(358.380676),Quat.AxisY(3)*new Quat(Math.Sin(.1),0,0,Math.Cos(.1))})
{
    var round=q*q.Inverse;
    Check(Math.Abs(round.W-1)<1e-12&&new Vec3(round.X,round.Y,round.Z).Length<1e-12,"Quaternion inverse undoes the rotation");
    var v=new Vec3(.3,-1.2,2.5);
    Check(((q*Quat.AxisY(40)).Rotate(v)-q.Rotate(Quat.AxisY(40).Rotate(v))).Length<1e-12,"Quaternion product matches Unity order (a*b rotates by b first)");
}
// The user's editor scene: the vehicle (and the powertrain object vuid 620 at pos 0 under it) is at
// about (0, 1.67, -345.4). A small extra yaw makes the test cover rotation too.
var vehicleFrames=new[]{Frame.Identity,new Frame(new Vec3(0,1.67,-345.4),unitQ,new Vec3(1,1,1)),new Frame(new Vec3(0,1.67,-345.4),Quat.AxisY(3),new Vec3(1,1,1))};
// The engine collider sits on the engine Transform; LocalBounds is in that space.
var engineLocalBounds=new OrientedBox(obsEngineRot.Inverse.Rotate(obsEngineBox.Centre-obsEnginePos),obsEngineBox.Size,unitQ);
(Vec3 Left,Vec3 Right)? reference=null;
foreach(var vehicle in vehicleFrames)
{
    var colliderFrame=new Frame(vehicle.TransformPoint(obsEnginePos),vehicle.Rotation*obsEngineRot,new Vec3(1,1,1));
    var worldEngine=colliderFrame.ToWorld(engineLocalBounds);
    var layoutEngine=vehicle.ToLocal(worldEngine);
    Check((layoutEngine.Centre-obsEngineBox.Centre).Length<1e-9,"Engine box centre returns to layout space for any vehicle offset/yaw");
    Check((layoutEngine.AxisX-obsEngineBox.AxisX).Length<1e-9&&(layoutEngine.AxisZ-obsEngineBox.AxisZ).Length<1e-9,"Engine box rotation returns to layout space");
    Check((layoutEngine.Size-obsEngineBox.Size).Length<1e-12,"Engine box size is unchanged at unit scale");
    var l=PowerpackRules.TwinOriginGuarded(obsOutput,obsDirection,obsLeftStart,obsLeftRot,obsInput,obsExtent,layoutEngine,PowerpackRules.ObstructionClearance);
    var r=PowerpackRules.TwinOriginGuarded(obsOutput,obsDirection,obsRightStart,obsRightRot,obsInput,obsExtent,layoutEngine,PowerpackRules.ObstructionClearance);
    Check(!l.FellBack&&!r.FellBack,"A correct engine box does not trip the safety net");
    reference??=(l.Origin,r.Origin);
    Check((l.Origin-reference.Value.Left).Length<1e-9&&(r.Origin-reference.Value.Right).Length<1e-9,"Same placement with and without the vehicle world offset/yaw");
    Check((l.Origin-obsLeft.Origin).Length<1e-9&&(r.Origin-obsRight.Origin).Length<1e-9,"In range, the guarded placement is the 0.2.2 placement unchanged");
    Near(Vec3.Dot(l.Origin-r.Origin,obsDirection),0,"Left and right keep the same depth");
    // 0.2.2 bug: the world box used as if it were layout space.
    if(vehicle.Position.Length>1)
    {
        var bad=PowerpackRules.TwinOriginAgainst(obsOutput,obsDirection,obsLeftStart,obsLeftRot,obsInput,obsExtent,worldEngine,PowerpackRules.ObstructionClearance);
        Check(Math.Abs(bad.Origin.Z-obsLeftStart.Z)>300,"Reproduces 0.2.2: a world-space engine box throws the gearbox hundreds of metres back");
        var guarded=PowerpackRules.TwinOriginGuarded(obsOutput,obsDirection,obsLeftStart,obsLeftRot,obsInput,obsExtent,worldEngine,PowerpackRules.ObstructionClearance);
        Check(guarded.FellBack&&Math.Abs(guarded.Depth)>300,"The safety net catches the world-space engine box");
    }
}
// The safety net: a box 344 m off, the user's 0.2.2 log replayed, and the 0.5 m boundary.
var farEngine=obsEngineBox with {Centre=obsEngineBox.Centre+new Vec3(0,0,-344)};
var farLeft=PowerpackRules.TwinOriginGuarded(obsOutput,obsDirection,obsLeftStart,obsLeftRot,obsInput,obsExtent,farEngine,PowerpackRules.ObstructionClearance);
var farRight=PowerpackRules.TwinOriginGuarded(obsOutput,obsDirection,obsRightStart,obsRightRot,obsInput,obsExtent,farEngine,PowerpackRules.ObstructionClearance);
Check(farLeft.FellBack&&farRight.FellBack,"Safety net trips on an engine box shifted 344 m");
var planeLeft=PowerpackRules.TwinOrigin(obsOutput,obsDirection,obsLeftStart,obsLeftRot,obsInput,obsExtent,PowerpackRules.ObstructionClearance);
var planeRight=PowerpackRules.TwinOrigin(obsOutput,obsDirection,obsRightStart,obsRightRot,obsInput,obsExtent,PowerpackRules.ObstructionClearance);
Check((farLeft.Origin-planeLeft.Origin).Length<1e-12&&(farRight.Origin-planeRight.Origin).Length<1e-12,"Fallback is the 0.2.1 output-plane placement plus the clearance");
Near(Vec3.Dot(planeLeft.MotorFace-obsOutput,obsDirection),PowerpackRules.ObstructionClearance,"Fallback motor face sits 1 mm past the output plane");
Near(Vec3.Dot(farLeft.Origin-farRight.Origin,obsDirection),0,"Fallback keeps the same depth left and right");
Check((farLeft.Origin-obsOutput).Length<2&&(farRight.Origin-obsOutput).Length<2,"Fallback stays next to the engine output");
var userOutput=new Vec3(-0.01,0.31,-1.81);
var userWorldEngine=new OrientedBox(new Vec3(0.02,1.983,-347.255),new Vec3(1.002,1.186,1.478),new Quat(0,0.0141,0,-0.9999));
var userLeft=PowerpackRules.TwinOriginGuarded(userOutput,obsDirection,obsLeftStart,obsLeftRot,obsInput,obsExtent,userWorldEngine,PowerpackRules.ObstructionClearance);
Check(userLeft.FellBack&&userLeft.Depth<-300,"User's 0.2.2 log (engine box at z -347.255 against output z -1.81) trips the safety net");
// Converted with a vehicle frame that puts that box around the output, it is placed normally.
var userLayoutEngine=new Frame(new Vec3(0,1.67,-344.7),unitQ,new Vec3(1,1,1)).ToLocal(userWorldEngine);
Check((userLayoutEngine.Centre-userOutput).Length<2,"Converted user engine box lies within 2 m of the engine output (TESTPLAN B)");
var userFixed=PowerpackRules.TwinOriginGuarded(userOutput,obsDirection,obsLeftStart,obsLeftRot,obsInput,obsExtent,userLayoutEngine,PowerpackRules.ObstructionClearance);
Check(!userFixed.FellBack&&(userFixed.Origin-userOutput).Length<2,"Converted user engine box gives a placement next to the engine");
foreach(var (shift,trips) in new[]{(0.45,false),(-0.45,false),(0.55,true),(-0.55,true)})
{
    // Shift the engine box along the engine direction relative to the 0.2.2 placement depth.
    var baseDepth=PowerpackRules.TwinOriginGuarded(obsOutput,obsDirection,obsLeftStart,obsLeftRot,obsInput,obsExtent,obsEngineBox,PowerpackRules.ObstructionClearance).Depth;
    var moved=obsEngineBox with {Centre=obsEngineBox.Centre+obsDirection*(shift-baseDepth)};
    var g=PowerpackRules.TwinOriginGuarded(obsOutput,obsDirection,obsLeftStart,obsLeftRot,obsInput,obsExtent,moved,PowerpackRules.ObstructionClearance);
    Near(g.Depth,shift,"Depth is measured from the output-plane placement");
    Check(g.FellBack==trips,$"Safety net boundary at 0.5 m (depth {shift})");
}
var nanEngine=obsEngineBox with {Centre=new Vec3(double.NaN,0,0)};
Check(PowerpackRules.TwinOriginGuarded(obsOutput,obsDirection,obsLeftStart,obsLeftRot,obsInput,obsExtent,nanEngine,PowerpackRules.ObstructionClearance).FellBack,"A NaN engine box falls back");
// Transmission collider: an axis-aligned module-local box keeps centre/size exactly (0.2.2 bytes);
// a collider on another Transform is converted into the module frame.
var aligned=TransmissionExtent.FromModuleBox(new OrientedBox(obsExtent.Centre,obsExtent.Size,unitQ));
Check(aligned==obsExtent,"Axis-aligned transmission collider gives the 0.2.2 extent exactly");
var moduleFrame=new Frame(new Vec3(0.5,1.92,-343.9),Quat.AxisY(93),new Vec3(1,1,1));
var colliderChild=new Frame(moduleFrame.TransformPoint(new Vec3(0,0.05,0.02)),moduleFrame.Rotation*Quat.AxisY(90),new Vec3(1,1,1));
var converted=TransmissionExtent.FromModuleBox(moduleFrame.ToLocal(colliderChild.ToWorld(new OrientedBox(new Vec3(0.1,0,0),new Vec3(0.4,0.6,0.5),unitQ))));
Check((converted.Centre-new Vec3(0,0.05,0.02-0.1)).Length<1e-9,"A child collider centre is converted into module space");
Check((converted.Size-new Vec3(0.5,0.6,0.4)).Length<1e-9&&Math.Abs(converted.HalfWidth-0.25)<1e-9,"A 90-degree child collider swaps its X/Z size in module space");
// 0.4.0: dimensional contract (whole length), per-instance definition lifecycle and bridge ownership.
var mtu=new PowerpackModelSettings().Validate();
var mb=mtu.Dimensions(12,3.97);var v8=mtu.Dimensions(8,4.07);
Check(Math.Abs(mb.X-1.602)<.003&&Math.Abs(mb.Y-1.108)<.003&&Math.Abs(mb.Z-.982)<.003,"MB873 whole-length factor: approximately 1.60 x 1.11 x .98m");
Check(Math.Abs(v8.X-1.272)<.003,"V8 4.07L whole length approximately 1.27m");
Near(mtu.Start+6*mtu.Pitch+mtu.Output,.9*(.405+6*.120),"LengthFactor includes start, pairs AND output");
foreach(double sf in new[]{.8,1d,1.25})foreach(double lf in new[]{.8,.9,1.25})foreach(double hf in new[]{.8,1d,1.25})
{
 var settings=new PowerpackModelSettings(SizeFactor:sf,LengthFactor:lf,HeightFactor:hf).Validate();
 var dims=settings.Dimensions(12,3.97);var k=Math.Pow(3.97,1d/3);
 Near(dims.X,(settings.Start+settings.Pitch*6+settings.Output)*k,"Definition spacing and outer length agree for all factors");
 Near(settings.Crank,.22*sf*hf,"Flange height follows size and height");
 Near(settings.CentreOfMass,.26*sf*hf,"COM follows size and height");
 Near(dims.Y,.70*k*sf,"Height and length leave width unchanged");
}
Near((mtu with{TurboLayout=PowerpackTurboLayout.Outboard}).Dimensions(12,3.97).Y,1.25*Math.Pow(3.97,1d/3),"Outboard width setting");
foreach(var bad in new[]{mtu with{SizeFactor=double.NaN},mtu with{LengthFactor=.79},mtu with{HeightFactor=1.26},mtu with{TransmissionVolumeFactor=.49},mtu with{TransmissionVolumeFactor=1.51},mtu with{TurboLayout=(PowerpackTurboLayout)2}})
{bool rejected=false;try{bad.Validate();}catch(ArgumentOutOfRangeException){rejected=true;}Check(rejected,"Invalid model factor rejected");}
var nativeDef=new Dictionary<string,double>{{"pitch",.14},{"output",.1226},{"crank",.2094},{"displacement",1.85}};
Dictionary<string,double> CloneDef(Dictionary<string,double> original,PowerpackModelSettings settings)=>new(original)
{["pitch"]=settings.Pitch,["output"]=settings.Output,["crank"]=settings.Crank,["displacement"]=1};
string DefHash(Dictionary<string,double> value)=>Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(System.Text.Json.JsonSerializer.Serialize(value.OrderBy(p=>p.Key)))));
var lease=new ModelDefinitionLease<Dictionary<string,double>>(nativeDef);string nativeHash=DefHash(nativeDef);
Check(lease.Select(true,mtu,CloneDef)&&lease.Active&&!ReferenceEquals(nativeDef,lease.Current),"Activation clones the per-model definition and invalidates");
string onHash=DefHash(lease.Current);
Check(!lease.Select(true,mtu,CloneDef),"Unchanged definition does not invalidate");
Check(lease.Select(false,mtu,CloneDef)&&ReferenceEquals(nativeDef,lease.Current)&&!lease.Active,"Off restores exact native definition and invalidates");
Check(lease.Select(true,mtu,CloneDef)&&DefHash(lease.Current)==onHash,"On/off/on gives identical definition hash");
Check(DefHash(nativeDef)==nativeHash,"Shared native definition never changes");
Check(lease.Select(true,mtu with{LengthFactor=1.1},CloneDef)&&DefHash(lease.Current)!=onHash,"Changed configuration clones again and invalidates");
var recreated=new ModelDefinitionLease<Dictionary<string,double>>(nativeDef);
Check(recreated.Select(true,mtu,CloneDef)&&DefHash(recreated.Current)==onHash,"Native layout recreation is rebound immediately before Generate");
var slot040=new RenderChildSlot<object>();int created040=0,retired040=0,alive040=0,peak040=0;
object Create040(){created040++;alive040++;peak040=Math.Max(peak040,alive040);return new object();}
void Retire040(object child){retired040++;alive040--;}
for(int i=0;i<100;i++)slot040.Rebuild(Create040,Retire040);
Check(created040==100&&retired040==99&&alive040==1&&peak040==1,"Repeated native rebuilds never duplicate a bridge");
slot040.Rebuild(()=>null,Retire040);slot040.Clear(Retire040);
Check(slot040.Value==null&&alive040==0&&retired040==100,"Disabled/non-powerpack rebuild removes bridge; cleanup is idempotent");
foreach(var (name,part) in new[]{("gearbox_tra_input_LOD0","tra-input"),("gearbox_tra_mid_LOD0","tra-shroud"),("gearbox_tra_outputL_LOD0","tra-output-l"),("gearbox_tra_outputR_LOD0","tra-output-r"),("LongitudinalGearboxInputInternalFaces_LOD0","lon-input"),("gearbox_lon_mid_LOD0","lon-shroud"),("LongitudinalGearboxOutputInternalFaces_LOD0","lon-output")})
 Check(PowerpackMeshNames.Transmission(name)==part,"Actual native transmission mesh name resolves");
Check(PowerpackMeshNames.Transmission("V60EngineCylinder_LOD0")==null,"Unrelated mesh never resolves as a gearbox");
var bridge040=PowerpackBridgeGeometry.Build(.55,.50,.70,0,.20);
Check(bridge040.Triangles.Length/3==72,"Procedural bridge includes 24 body + 48 bevel-cap triangles");
Check(bridge040.Vertices.All(v=>Math.Abs(v.X)<=.275+1e-9&&v.Y>=0&&v.Y<=.54+1e-9&&v.Z>=0&&v.Z<=.7),"Bridge stays in measured gap with its 8% bevel cover");
double volume040=0;
for(int i=0;i<bridge040.Triangles.Length;i+=3)
{
 var a=bridge040.Vertices[bridge040.Triangles[i]];var b=bridge040.Vertices[bridge040.Triangles[i+1]];var c=bridge040.Vertices[bridge040.Triangles[i+2]];
 Check(Vec3.Cross(b-a,c-a).Length>1e-12,"Bridge has no degenerate triangle");volume040+=Vec3.Dot(a,Vec3.Cross(b,c))/6;
}
Check(volume040>.1,"Bridge winding is outward (positive signed volume)");
if(args.Length==2&&args[0]=="--bridge-json")File.WriteAllText(args[1],System.Text.Json.JsonSerializer.Serialize(new {Vertices=bridge040.Vertices.Select(v=>new[]{v.X,v.Y,v.Z}),Triangles=bridge040.Triangles}));
// 0.5.0: casing fit, exact payload, visibility and one-child lifecycle.
var engineBox=new OrientedBox(new Vec3(0,.5,0),new Vec3(1,1,2),unitQ);
var gearboxBox=new OrientedBox(new Vec3(0,.4,3),new Vec3(.8,.6,.8),unitQ);
var casingFit=CasingFit.FromCorners(engineBox.Corners(),gearboxBox.Corners());
Near(casingFit.Motor.Z,1-CasingFit.SeamCover,"Casing starts at the engine rear face with the approved seam cover");
Near(casingFit.Gearbox.Z,3.4+CasingFit.SeamCover,"Casing ends at the gearbox box with the approved seam cover");
Near(casingFit.Motor.Width,1+CasingFit.SkinClearance*2,"Casing carries the engine width plus skin clearance");
Near(casingFit.Motor.Height,1+CasingFit.SkinClearance*2,"Casing carries the engine height plus skin clearance");
Near(casingFit.Gearbox.Width,.8+CasingFit.SkinClearance*2,"Casing carries the gearbox width plus skin clearance");
Near(casingFit.Gearbox.Height,.6+CasingFit.SkinClearance*2,"Casing carries the gearbox height plus skin clearance");
var unitCorner=casingFit.Warp(new Vec3(-.5,0,0));
Check(Math.Abs(unitCorner.X+(.5*(1+CasingFit.SkinClearance*2)))<1e-9&&Math.Abs(unitCorner.Y-(0-CasingFit.SkinClearance))<1e-9&&Math.Abs(unitCorner.Z-(1-CasingFit.SeamCover))<1e-9,"Unit casing corner maps to the motor rear cross-section");
var farCorner=casingFit.Warp(new Vec3(.5,1,1));
Check(Math.Abs(farCorner.X-(.5*(.8+CasingFit.SkinClearance*2)))<1e-9&&Math.Abs(farCorner.Y-((.1-CasingFit.SkinClearance)+(.6+CasingFit.SkinClearance*2)))<1e-9&&Math.Abs(farCorner.Z-(3.4+CasingFit.SeamCover))<1e-9,"Unit casing opposite corner maps to the gearbox cross-section");
double tRoof=.1/(casingFit.Gearbox.Z-casingFit.Motor.Z);
double engineBottom=engineBox.Corners().Min(p=>p.Y),gearboxBottom=gearboxBox.Corners().Min(p=>p.Y);
double roof=engineBottom-CasingFit.SkinClearance+(gearboxBottom-engineBottom)*tRoof
    +.746*(casingFit.Motor.Height+(casingFit.Gearbox.Height-casingFit.Motor.Height)*tRoof)-CasingFit.BridgeClearance;
var clamped=casingFit.ClearBridge(new PowerpackBridgeMesh(new[]{new Vec3(0,100,casingFit.Motor.Z+.1),new Vec3(0,100,casingFit.Motor.Z+1)},Array.Empty<int>()));
Check(Math.Abs(clamped.Vertices[0].Y-roof)<1e-9&&clamped.Vertices[1].Y==100,"Twin bridge is lowered below the casing roof only over the hood");
foreach(bool model in new[]{false,true})foreach(bool casing in new[]{false,true})foreach(bool effective in new[]{false,true})foreach(bool marked in new[]{false,true})
    Check(CasingVisibility.Visible(model,casing,effective,marked)==(model&&casing&&effective&&marked),"Casing visibility truth table");
var casingSlot=new RenderChildSlot<object>();int casingAlive=0;
object MakeCasing(){casingAlive++;return new object();}void RetireCasing(object child)=>casingAlive--;
casingSlot.Rebuild(MakeCasing,RetireCasing);casingSlot.Rebuild(MakeCasing,RetireCasing);
Check(casingAlive==1&&casingSlot.Value!=null,"A rebuild retires the old casing before creating exactly one child");
casingSlot.Clear(RetireCasing);Check(casingAlive==0&&casingSlot.Value==null,"Casing is gone when the powertrain is cleared");
string testRoot=FindRoot();
foreach(var (name,triangles,zMin,hash) in new[]{
    ("casing-lod0",556,.045,"B885D64FD5D909DDAB0F54AC832A6D3B2C22669012651F95E7041A33C84C971D"),
    ("casing-lod1",152,.045,"689DA04F36CAEDDEA078EFB22F92925942FDFFC769437A91456CA435CAF5EC54")})
{
    var bytes=File.ReadAllBytes(Path.Combine(testRoot,"Assets",name+".meshbin"));
    Check(Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(bytes))==hash,"Casing payload hash matches the embedded manifest: "+name);
    using var stream=new MemoryStream(bytes);var payload=MeshPayload.Read(stream);
    Check(payload.Triangles.Length/3==triangles,"Casing triangle count "+name);
    Check(payload.Vertices.Min(v=>v.X)>=-.500001&&payload.Vertices.Max(v=>v.X)<=.500001&&
        payload.Vertices.Min(v=>v.Y)>=zMin-.000001&&payload.Vertices.Max(v=>v.Y)<=1.000001&&
        payload.Vertices.Min(v=>v.Z)>=-.000001&&payload.Vertices.Max(v=>v.Z)<=1.000001,"Casing unit bounds "+name);
}
if(args.Length>=2&&args[0]=="--casing-preview")WriteCasingPreview(args[1],testRoot);
Console.WriteLine($"PASS {checks} no-turbo/profile/drivetrain/legacy-save/powerpack/obstruction/space/MTU/casing checks");

static string FindRoot()
{
    for(var d=new DirectoryInfo(AppContext.BaseDirectory);d!=null;d=d.Parent)
        if(File.Exists(Path.Combine(d.FullName,"Assets","casing-lod0.meshbin")))return d.FullName;
    return Directory.GetCurrentDirectory();
}
static void WriteCasingPreview(string directory,string root)
{
    Directory.CreateDirectory(directory);
    // V12 3.97 L and one Renk box in the same mm frame as view-casing-pack.scad.
    var engine=new OrientedBox(new Vec3(0,975.3883133057441/2,(862.1735983684703-741.0417704985199)/2),
        new Vec3(1073.5605136709326,975.3883133057441,1603.2153688669902),new Quat(0,0,0,1));
    var gearbox=new OrientedBox(new Vec3(0,(62.3+619.85)/2,(763.9735983684703+1561.9735983684702)/2),
        new Vec3(1342,557.55,798),new Quat(0,0,0,1));
    var fit=CasingFit.FromCorners(engine.Corners(),gearbox.Corners());
    foreach(int lod in new[]{0,1})
    {
        var bytes=File.ReadAllBytes(Path.Combine(root,"Assets","casing-lod"+lod+".meshbin"));
        var payload=MeshPayload.Read(new MemoryStream(bytes));
        var warped=fit.Warp(payload);
        using var writer=new StreamWriter(Path.Combine(directory,"casing-fit-v12-renk-lod"+lod+".stl"),false,new System.Text.UTF8Encoding(false));
        writer.WriteLine("solid casing-fit-v12-renk");
        for(int i=0;i<warped.Triangles.Length;i+=3)
        {
            writer.WriteLine("  facet normal 0 0 0");writer.WriteLine("    outer loop");
            foreach(int index in new[]{warped.Triangles[i],warped.Triangles[i+1],warped.Triangles[i+2]})
            {var v=warped.Vertices[index];writer.WriteLine(FormattableString.Invariant($"      vertex {v.X:0.######} {v.Z:0.######} {v.Y:0.######}"));}
            writer.WriteLine("    endloop");writer.WriteLine("  endfacet");
        }
        writer.WriteLine("endsolid casing-fit-v12-renk");
    }
    File.WriteAllText(Path.Combine(directory,"casing-fit-v12-renk.json"),System.Text.Json.JsonSerializer.Serialize(new
    {
        engine=new{centre=new[]{engine.Centre.X,engine.Centre.Y,engine.Centre.Z},size=new[]{engine.Size.X,engine.Size.Y,engine.Size.Z}},
        gearbox=new{centre=new[]{gearbox.Centre.X,gearbox.Centre.Y,gearbox.Centre.Z},size=new[]{gearbox.Size.X,gearbox.Size.Y,gearbox.Size.Z}},
        casing=new{start=fit.Motor.Z,end=fit.Gearbox.Z,motorWidth=fit.Motor.Width,motorHeight=fit.Motor.Height,
            gearboxWidth=fit.Gearbox.Width,gearboxHeight=fit.Gearbox.Height},
        triangles=new[]{556,152}
    },new System.Text.Json.JsonSerializerOptions{WriteIndented=true}));
}













