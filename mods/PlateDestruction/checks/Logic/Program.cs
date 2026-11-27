using PlateDestructionP0;
int fail=0,pass=0;
void Check(bool ok,string name){if(ok)pass++;else{fail++;Console.WriteLine("FAIL "+name);}}

// LogBudget: 200 lines, then summary every 50 shots
var b=new LogBudget(200,50);
int written=0;for(int i=0;i<500;i++)if(b.TryLine())written++;
Check(written==200,"200 lines max");Check(b.Suppressed==300,"suppressed counted");Check(b.Exhausted,"exhausted");
int summaries=0;for(int s=0;s<200;s++)if(b.Shot())summaries++;
Check(summaries==4,"summary every 50 shots after limit (4 in 200)");
var b2=new LogBudget(200,50);int early=0;for(int s=0;s<120;s++)if(b2.Shot())early++;
Check(early==0,"no summary before the limit is reached");
for(int i=0;i<200;i++)b2.TryLine();
Check(!b2.Shot()||true,"shot after limit does not throw");
b2.Reset();Check(b2.Lines==0&&b2.Shots==0&&b2.Suppressed==0&&!b2.Exhausted,"budget reset");
Check(b2.TryLine(),"line allowed after reset");

// PlateState
var st=new PlateState();
Check(!st.IsExcluded(1,10),"empty not excluded");
Check(st.ToggleExcluded(1,10),"toggle on");Check(st.IsExcluded(1,10),"excluded after toggle");Check(!st.IsExcluded(2,10),"other root unaffected");
st.Pick(1,10);Check(st.Picked==(1L,10),"picked");
Check(!st.ToggleExcluded(1,10),"toggle off");Check(!st.IsExcluded(1,10),"restored");Check(st.Picked==null,"pick cleared when idle");
st.ToggleExcluded(1,10);st.ToggleHidden(1,11);st.ToggleHidden(2,12);st.Pick(1,10);
st.ClearRoot(1);Check(!st.IsExcluded(1,10)&&!st.IsHidden(1,11)&&st.IsHidden(2,12)&&st.Picked==null,"vehicle release clears only that root");
st.ClearAll();Check(st.ExcludedCount==0&&st.HiddenCount==0&&st.Picked==null,"clear all");

// SessionTracker
var t=new SessionTracker();
Check(t.Update(false,1)==null,"menu: nothing");
Check(t.Update(true,5)==null,"enter play: no reset");
Check(t.Update(true,5)==null,"steady play");
Check(t.Update(true,6)=="new battle","scene handle change while playing = new battle");
Check(t.Update(false,6)=="edit","play to edit");
Check(t.Update(false,6)==null,"steady edit");
Check(t.Update(true,6)==null,"edit to play same scene: nothing to reset");
Check(t.Update(false,6)=="edit","edit again");
Check(t.Update(true,9)==null,"play in a new scene after edit: already reset by the edit transition");
Check(t.Update(true,10)=="new battle","next battle while playing");

// HitTally
var h=new HitTally();h.Add(5);h.Add(5);h.Add(7);
Check(h.Total==3&&h.Top(1)=="vuid 5=2","tally top");h.Clear();Check(h.Total==0&&h.Top()=="","tally clear");

// TargetChooser: path 370 (skirt), 27, 27 (hull), 168 (gearbox, not eligible)
var path=new List<bool>{true,true,true,false};
Check(TargetChooser.Choose(path,TargetMode.FirstPlate)==0,"first plate = entry skirt (index 0)");
Check(TargetChooser.Choose(path,TargetMode.LastPlate)==2,"last plate = last hull hit (index 2)");
Check(TargetChooser.Choose(new List<bool>{false,true,true},TargetMode.FirstPlate)==1,"first skips ineligible entry");
Check(TargetChooser.Choose(new List<bool>{false,false},TargetMode.FirstPlate)==-1&&TargetChooser.Choose(new List<bool>(),TargetMode.LastPlate)==-1,"none eligible = -1");


// DestructibleFlag: token in the description
Check(!DestructibleFlag.Has(null)&&!DestructibleFlag.Has("")&&!DestructibleFlag.Has("skirt"),"flag: absent");
Check(DestructibleFlag.Has("a [destructible] b")&&DestructibleFlag.Has("[DESTRUCTIBLE]"),"flag: present, case-insensitive");
Check(DestructibleFlag.Set(null,true)=="[destructible]","tick on null");
Check(DestructibleFlag.Set("",true)=="[destructible]","tick on empty");
Check(DestructibleFlag.Set("  ",true)=="[destructible]","tick on blank");
Check(DestructibleFlag.Set("Side skirt",true)=="Side skirt [destructible]","tick keeps text");
Check(DestructibleFlag.Set("Side skirt [destructible]",true)=="Side skirt [destructible]","tick twice = once");
Check(DestructibleFlag.Set(null,false)==null,"untick null stays null");
Check(DestructibleFlag.Set("",false)=="","untick empty stays empty");
Check(DestructibleFlag.Set("Side skirt",false)=="Side skirt","untick without token changes nothing");
Check(DestructibleFlag.Set("[destructible]",false)==null,"untick only token gives null");
Check(DestructibleFlag.Set("Side skirt [destructible]",false)=="Side skirt","untick removes only the token (end)");
Check(DestructibleFlag.Set("[destructible] Side skirt",false)=="Side skirt","untick removes only the token (start)");
Check(DestructibleFlag.Set("a [destructible] b",false)=="a b","untick removes only the token (middle)");
Check(DestructibleFlag.Set(DestructibleFlag.Set("note",true),false)=="note","tick then untick round trip");
Check(DestructibleFlag.Set(DestructibleFlag.Set(null,true),false)==null,"tick then untick from null");

// Damage formula
Check(Math.Abs(DamageFormula.KineticMJ(5.224,1500)-5.877)<0.01,"kinetic MJ of 5.224 kg at 1500 m/s");
Check(Math.Abs(DamageFormula.Kinetic(5.224,1500)-58.77)<0.1,"D_kin = 10 x MJ");
Check(DamageFormula.KineticMJ(0,1500)==0&&DamageFormula.KineticMJ(5,-1)==0,"kinetic: no mass or speed gives 0");
Check(Math.Abs(DamageFormula.Blast(100,1.0)-100)<1e-9,"blast 100 mm weight 1 = 100");
Check(Math.Abs(DamageFormula.Blast(155,1.0)-372.4)<0.1,"blast 155 mm HE = 372");
Check(DamageFormula.Blast(155,0)==0&&DamageFormula.Blast(0,1)==0,"blast: weight 0 or no calibre = 0");
Check(Math.Abs(DamageFormula.Blast(120,1.2)-207.4)<0.1,"blast 120 mm HESH = 207");
Check(DamageFormula.AreaFactor(0.01)==1&&Math.Abs(DamageFormula.AreaFactor(1.0)-16)<1e-9,"area factor: minimum 1, 1 m2 = 16");
Check(Math.Abs(DamageFormula.Strength(0.45,20,1.0,150)-144)<1e-9,"strength 20 mm skirt of 1 m2 = 144");
Check(DamageFormula.Strength(0.45,0,1.0,150)==150&&DamageFormula.Strength(0.45,double.NaN,1.0,150)==150,"strength: unknown thickness uses the default");
Check(Math.Abs(DamageFormula.Strength(0.45,20,double.NaN,150)-9)<1e-9,"strength: unknown area counts as factor 1");
double kin=DamageFormula.Kinetic(5.224,1500);
Check(kin*2<144&&kin*3>=144,"defaults: 3 APFSDS hits break a 20 mm skirt (not 2)");
var gm=DamageFormula.FromMesh(0.02,0.02);Check(gm is {} g1&&Math.Abs(g1.ThicknessMm-20)<1e-9&&Math.Abs(g1.AreaM2-1.0)<1e-9,"mesh: metres");
var gmm=DamageFormula.FromMesh(20,0.02);Check(gmm is {} g2&&Math.Abs(g2.ThicknessMm-20)<1e-9,"mesh: values above 1 are millimetres");
Check(DamageFormula.FromMesh(0,0.02)==null&&DamageFormula.FromMesh(0.02,0)==null&&DamageFormula.FromMesh(double.NaN,1)==null,"mesh: unusable gives null");

// DamageLedger
var led=new DamageLedger();
Check(!led.Add(1,370,58.8,144,out var lh,out var lt)&&lh==1&&Math.Abs(lt-58.8)<1e-9,"hit 1 does not break");
Check(!led.Add(1,370,58.8,144,out lh,out _)&&lh==2,"hit 2 does not break");
Check(led.Add(1,370,58.8,144,out lh,out lt)&&lh==3&&led.IsBroken(1,370),"hit 3 breaks");
Check(!led.Add(1,370,58.8,144,out lh,out _)&&lh==3,"hit after break is ignored");
Check(!led.IsBroken(1,27)&&!led.IsBroken(2,370),"other plate / other vehicle unaffected");
Check(led.Add(1,5,500,144,out lh,out _)&&lh==1,"one hit above the strength breaks at once");
Check(led.ForceBreak(1,9)&&!led.ForceBreak(1,9),"force break once");
led.ClearRoot(1);Check(!led.IsBroken(1,370)&&led.Hits(1,370)==0&&led.BrokenCount==0,"clear root resets counters");
led.Add(2,1,1,100,out _,out _);led.Clear();Check(led.Hits(2,1)==0,"clear all");

// PlateState set/idempotence
var ps=new PlateState();
Check(ps.SetExcluded(3,7,true)&&!ps.SetExcluded(3,7,true)&&ps.IsExcluded(3,7),"set excluded is idempotent");
Check(ps.SetHidden(3,7,true)&&ps.IsHidden(3,7),"set hidden");
Check(ps.SetExcluded(3,7,false)&&!ps.IsExcluded(3,7),"restore excluded");

// Break order: decals, plate, explosion, damage model, log
var order=new List<BreakStep>();
var failed0=BreakPipeline.Run(s=>order.Add(s));
Check(failed0.Count==0&&order.SequenceEqual(new[]{BreakStep.HideDecals,BreakStep.HidePlate,BreakStep.HideAttached,BreakStep.PlayExplosion,BreakStep.RemoveFromModel,BreakStep.Log}),"break order: decals, plate, attached parts, explosion, route A, log");
var order2=new List<BreakStep>();
var failed1=BreakPipeline.Run(s=>{order2.Add(s);if(s==BreakStep.PlayExplosion)throw new InvalidOperationException("boom");});
Check(failed1.Count==1&&failed1[0].Step==BreakStep.PlayExplosion&&order2.Count==6,"a failing step does not skip the later steps");
Check(BreakPipeline.Order.Count==6&&BreakPipeline.Order[0]==BreakStep.HideDecals&&BreakPipeline.Order[2]==BreakStep.HideAttached&&BreakPipeline.Order[4]==BreakStep.RemoveFromModel,"order constant");

// ---- 0.2.0: mirrored pairs, per-side break ----
// Skirt pair: original half at x in [1.0,1.2], mirror plane x=0 -> mirrored half at x in [-1.2,-1.0].
var mirror=Mat4.Reflection(1,0,0,0,0,0);
Check(mirror.Det3()<0&&Mat4.Identity().Det3()>0&&Mat4.Sign(mirror.Det3())==-1&&Mat4.Sign(Mat4.Identity().Det3())==1,"mirror matrix has a negative determinant");
var mp=mirror.Point(new V3(1.1f,0.3f,2f));Check(Math.Abs(mp.X+1.1f)<1e-6&&Math.Abs(mp.Y-0.3f)<1e-6&&Math.Abs(mp.Z-2f)<1e-6,"reflection moves x to -x");
var geo=new SideGeometry(mirror,new V3(1.0f,0,0),new V3(1.2f,0.5f,3f));
Check(Math.Abs(geo.MirrMin.X+1.2f)<1e-6&&Math.Abs(geo.MirrMax.X+1.0f)<1e-6&&Math.Abs(geo.MirrMax.Z-3f)<1e-6,"mirror bounds = bounds times the matrix");

// side choice by bounds
Check(geo.Choose(new V3(1.1f,0.2f,1f))==PlateSide.Original,"side: point inside the original half");
Check(geo.Choose(new V3(-1.1f,0.2f,1f))==PlateSide.Mirror,"side: point inside the mirrored half");
Check(geo.Choose(new V3(1.6f,0.2f,1f))==PlateSide.Original,"side: point outside, nearer the original half");
Check(geo.Choose(new V3(-0.6f,0.2f,1f))==PlateSide.Mirror,"side: point outside, nearer the mirrored half");
Check(geo.Choose(new V3(0.05f,0.2f,1f))==PlateSide.Original&&geo.Choose(new V3(-0.05f,0.2f,1f))==PlateSide.Mirror,"side: between the halves, the nearer one wins");
Check(geo.Choose(new V3(0f,0.2f,1f))==PlateSide.Original,"side: exact tie is Original");
var overlap=new SideGeometry(mirror,new V3(-0.5f,0,0),new V3(0.7f,1,1)); // halves overlap: centre distance decides
Check(overlap.Choose(new V3(0.5f,0.5f,0.5f))==PlateSide.Original&&overlap.Choose(new V3(-0.4f,0.5f,0.5f))==PlateSide.Mirror,"side: overlapping bounds are decided by the centres");
Check(geo.Covers(new V3(-1.2f,0,0),new V3(1.2f,0.5f,3f),0.01f)&&!geo.Covers(new V3(5,0,0),new V3(6,1,1),0.01f)&&geo.Covers(new V3(-1.205f,0,0),new V3(1.2f,0.5f,3f),0.01f),"vertex space check (covers) accepts matching space and rejects a far box");

// triangle filter by centroid side: a combined mesh = quad on the original half (tris 0,1) + quad on the mirrored half (tris 2,3)
var cv=new[]{new V3(1.0f,0,0),new V3(1.2f,0,0),new V3(1.2f,0.5f,0),new V3(1.0f,0.5f,0),new V3(-1.0f,0,0),new V3(-1.2f,0,0),new V3(-1.2f,0.5f,0),new V3(-1.0f,0.5f,0)};
var ct=new[]{0,1,2, 0,2,3, 4,6,5, 4,7,6};
var cnt=SideTriangles.Count(cv,ct,geo);Check(cnt.Original==2&&cnt.Mirror==2,"triangle count by side: 2 + 2");
var keepO=SideTriangles.Keep(cv,ct,geo,PlateSide.Original);Check(keepO.SequenceEqual(new[]{0,1,2,0,2,3}),"filter keeps the original half's triangles in order");
var keepM=SideTriangles.Keep(cv,ct,geo,PlateSide.Mirror);Check(keepM.SequenceEqual(new[]{4,6,5,4,7,6}),"filter keeps the mirrored half's triangles in order");
Check(ct.Length==12&&cv.Length==8,"filter does not touch the input arrays");
Check(SideTriangles.Keep(cv,new[]{0,1,2,0,1,99,-1,2,3},geo,PlateSide.Original).SequenceEqual(new[]{0,1,2}),"filter drops triangles with invalid indices");
Check(SideTriangles.Keep(cv,ct,geo,PlateSide.Original).Length+SideTriangles.Keep(cv,ct,geo,PlateSide.Mirror).Length==ct.Length,"the two halves add up to the whole index list");
Check(SideTriangles.Count(cv,new[]{0,1,2},geo)==(1,0)&&SideTriangles.Count(cv,new[]{4,6,5},geo)==(0,1),"a call with one half only is counted as that half");

// winding flip + managed mirror copy
var flipped=SideTriangles.FlipWinding(new[]{0,1,2, 3,4,5});Check(flipped.SequenceEqual(new[]{0,2,1,3,5,4}),"winding flip swaps the 2nd and 3rd index");
Check(SideTriangles.FlipWinding(flipped).SequenceEqual(new[]{0,1,2,3,4,5}),"flipping twice gives the original");
Check(SideTriangles.FlipWinding(new[]{0,1,2,3}).SequenceEqual(new[]{0,2,1,3}),"flip leaves an incomplete last triangle alone");
var src=new[]{new V3(1.0f,0,0),new V3(1.2f,0,0),new V3(1.2f,0.5f,0)};var srcTri=new[]{0,1,2};
var half=SideTriangles.Transform(src,mirror);var halfTri=SideTriangles.FlipWinding(srcTri);
Check(Math.Abs(half[0].X+1.0f)<1e-6&&Math.Abs(half[1].X+1.2f)<1e-6&&half[2].Y==0.5f,"mirror copy: vertices reflected");
float Nz(V3[] v,int[] t){var a=v[t[0]];var b=v[t[1]];var c=v[t[2]];return (b.X-a.X)*(c.Y-a.Y)-(b.Y-a.Y)*(c.X-a.X);}
Check(Nz(src,srcTri)>0&&Nz(half,halfTri)>0&&Nz(half,srcTri)<0,"mirror copy + flipped winding keeps the face normal pointing the same way (unflipped would invert it)");
var nrm=SideTriangles.TransformDirs(new[]{new V3(1,0,0),new V3(0,0,1)},mirror);Check(Math.Abs(nrm[0].X+1)<1e-6&&Math.Abs(nrm[1].Z-1)<1e-6,"mirror copy: normals reflected (no translation)");
Check(half.All(p=>geo.Choose(p)==PlateSide.Mirror),"mirror copy lies on the mirrored half");

// armour decision per AddMesh call
Check(ArmourRule.Decide(false,100,100,false,false).Action==ArmourAction.Pass,"armour: nothing broken passes");
Check(ArmourRule.Decide(false,100,100,true,true).Action==ArmourAction.Skip,"armour: both broken skips");
var d1=ArmourRule.Decide(false,100,100,true,false);Check(d1.Action==ArmourAction.Filter&&d1.Keep==PlateSide.Mirror,"armour: combined call, original broken -> keep the mirrored triangles");
var d2=ArmourRule.Decide(false,100,100,false,true);Check(d2.Action==ArmourAction.Filter&&d2.Keep==PlateSide.Original,"armour: combined call, mirror broken -> keep the original triangles");
Check(ArmourRule.Decide(true,100,0,false,true).Action==ArmourAction.Skip,"armour: separate mirror-handed call, mirror broken -> skip the call");
Check(ArmourRule.Decide(true,100,0,true,false).Action==ArmourAction.Pass,"armour: separate mirror-handed call, only the original broken -> pass");
Check(ArmourRule.Decide(false,100,0,true,false).Action==ArmourAction.Skip,"armour: separate original call, original broken -> skip the call");
Check(ArmourRule.Decide(false,100,0,false,true).Action==ArmourAction.Pass,"armour: separate original call, mirror broken -> pass");
Check(ArmourRule.Decide(false,0,0,true,false).Action==ArmourAction.Pass,"armour: empty call passes");

// per-side counters
var sl=new DamageLedger();
Check(!sl.Add(1,2201,PlateSide.Original,58.8,144,out var sh,out _)&&sh==1,"side counter: original hit 1");
Check(!sl.Add(1,2201,PlateSide.Mirror,58.8,144,out sh,out _)&&sh==1,"side counter: the mirror side has its own count");
Check(!sl.Add(1,2201,PlateSide.Original,58.8,144,out sh,out _)&&sh==2,"side counter: original hit 2");
Check(sl.Add(1,2201,PlateSide.Original,58.8,144,out sh,out _)&&sh==3&&sl.IsBroken(1,2201,PlateSide.Original),"side counter: 3 hits break only the original half");
Check(!sl.IsBroken(1,2201,PlateSide.Mirror)&&!sl.IsBroken(1,2201)&&!sl.IsGone(1,2201),"side counter: the mirror half and the whole are still intact");
Check(!sl.Add(1,2201,PlateSide.Original,58.8,144,out sh,out _)&&sh==3,"side counter: a hit on a broken half is ignored");
Check(sl.Hits(1,2201,PlateSide.Mirror)==1&&sl.Hits(1,2201,PlateSide.Original)==3,"side counter: hit counts per side");
Check(!sl.Add(1,2201,PlateSide.Mirror,58.8,144,out sh,out _)&&sl.Add(1,2201,PlateSide.Mirror,58.8,144,out sh,out _)&&sh==3,"side counter: the mirror half breaks on its own 3rd hit");
Check(sl.IsGone(1,2201),"side counter: both halves broken = plate gone");
Check(!sl.IsGone(2,2201)&&!sl.IsGone(1,5),"side counter: other vehicle / plate unaffected");
sl.ClearRoot(1);Check(!sl.IsGone(1,2201)&&sl.Hits(1,2201,PlateSide.Original)==0&&sl.BrokenCount==0,"side counter: clear root resets both sides");
Check(sl.ForceBreak(1,7,PlateSide.Mirror)&&!sl.ForceBreak(1,7,PlateSide.Mirror)&&sl.IsBroken(1,7,PlateSide.Mirror)&&!sl.IsBroken(1,7,PlateSide.Original),"side counter: force break per side");

// both sides broken -> whole-plate route A
Check(SideBreak.Resolve(true,PlateSide.Original,false)==PlateSide.Original&&SideBreak.Resolve(true,PlateSide.Mirror,false)==PlateSide.Mirror,"first side breaks only that side");
Check(SideBreak.Resolve(true,PlateSide.Mirror,true)==PlateSide.Whole&&SideBreak.Resolve(true,PlateSide.Original,true)==PlateSide.Whole,"second side leads to the whole-plate route A");
Check(SideBreak.Resolve(false,PlateSide.Original,false)==PlateSide.Whole&&SideBreak.Resolve(true,PlateSide.Whole,false)==PlateSide.Whole,"unmirrored plate or per-side unavailable = whole plate as in 0.1.1");
Check(SideBreak.Other(PlateSide.Original)==PlateSide.Mirror&&SideBreak.Other(PlateSide.Mirror)==PlateSide.Original&&SideBreak.Other(PlateSide.Whole)==PlateSide.Whole,"other side");
var sps=new PlateState();
Check(sps.SetSideBroken(1,2201,PlateSide.Original,true)&&!sps.SetSideBroken(1,2201,PlateSide.Original,true)&&sps.IsSideBroken(1,2201,PlateSide.Original)&&!sps.IsSideBroken(1,2201,PlateSide.Mirror)&&sps.AnySideBroken(1,2201),"state: side flag set once");
Check(sps.SetSideBroken(1,2201,PlateSide.Mirror,true)&&sps.IsSideBroken(1,2201,PlateSide.Mirror)&&!sps.SetSideBroken(1,2201,PlateSide.Whole,true),"state: second side flag; Whole is not a side");
Check(sps.SetSideBroken(1,2201,PlateSide.Original,false)&&!sps.IsSideBroken(1,2201,PlateSide.Original)&&sps.IsSideBroken(1,2201,PlateSide.Mirror),"state: restore one side");
sps.SetSideBroken(2,5,PlateSide.Mirror,true);sps.ClearRoot(1);Check(!sps.AnySideBroken(1,2201)&&sps.AnySideBroken(2,5),"state: vehicle release clears only that root's sides");
sps.ClearAll();Check(sps.SideCount==0&&!sps.AnySideBroken(2,5),"state: clear all clears sides");
var sord=new List<BreakStep>();var sfailed=BreakPipeline.Run(s=>sord.Add(s),BreakPipeline.SideOrder);
Check(sfailed.Count==0&&sord.SequenceEqual(new[]{BreakStep.HideSideDecals,BreakStep.HideSideMesh,BreakStep.HideAttached,BreakStep.PlayExplosion,BreakStep.RaiseSide,BreakStep.Log}),"side break order: side decals, side mesh, attached parts, explosion, rebuild, log");
var sord2=new List<BreakStep>();var sf2=BreakPipeline.Run(s=>{sord2.Add(s);if(s==BreakStep.HideSideMesh)throw new InvalidOperationException("x");},BreakPipeline.SideOrder);
Check(sf2.Count==1&&sf2[0].Step==BreakStep.HideSideMesh&&sord2.Count==6,"side break: a failing step does not skip the later ones");
// full scenario: 3 hits left, then 3 hits right
var scen=new DamageLedger();var scenState=new PlateState();bool wholeRouteA=false;
foreach(var side in new[]{PlateSide.Original,PlateSide.Original,PlateSide.Original,PlateSide.Mirror,PlateSide.Mirror,PlateSide.Mirror})
{
    if(scen.Add(9,2201,side,58.8,144,out _,out _))
    {
        var r=SideBreak.Resolve(true,side,scen.IsBroken(9,2201,SideBreak.Other(side)));
        if(r==PlateSide.Whole)wholeRouteA=true;else scenState.SetSideBroken(9,2201,r,true);
        if(side==PlateSide.Original)Check(!wholeRouteA&&scenState.IsSideBroken(9,2201,PlateSide.Original)&&!scenState.IsSideBroken(9,2201,PlateSide.Mirror),"scenario: after 3 left hits only the left is broken");
    }
}
Check(wholeRouteA&&scen.IsGone(9,2201),"scenario: after 3 more on the right both are gone and route A takes over");


// ---- 0.3.0: blasts tear off, kinetic hits do not; AddMesh/9 markers; attached parts ----
const double Base=100,R100=0.5,PerMm=5,MinCal=20;
// falloff
Check(Math.Abs(BlastMath.Falloff(0,0.5)-1)<1e-12&&Math.Abs(BlastMath.Falloff(0.5,0.5)-0.5)<1e-12&&Math.Abs(BlastMath.Falloff(1.0,0.5)-0.2)<1e-12,"falloff: 1 at d=0, 1/2 at d=r, 1/5 at d=2r");
Check(BlastMath.Falloff(0.3,0)==0&&BlastMath.Falloff(double.NaN,1)==0&&BlastMath.Falloff(-1,0.5)==1,"falloff: invalid radius or NaN = 0, negative distance counts as 0");
Check(Math.Abs(BlastMath.Radius(R100,155)-0.775)<1e-9&&Math.Abs(BlastMath.Radius(R100,57)-0.285)<1e-9,"radius scales with the calibre");
// damage and strength
Check(Math.Abs(BlastMath.Damage(Base,100,0,R100,MinCal)-100)<1e-9,"blast: 100 mm at d=0 = BlastBase");
Check(Math.Abs(BlastMath.Damage(Base,155,0,R100,MinCal)-372.3875)<1e-3,"blast: 155 mm at d=0 = 372");
Check(Math.Abs(BlastMath.Damage(Base,100,0.5,R100,MinCal)-50)<1e-9,"blast: 100 mm at d=r = half");
Check(BlastMath.Damage(Base,15,0,R100,MinCal)==0&&BlastMath.Damage(Base,19.9,0,R100,MinCal)==0&&BlastMath.Damage(Base,20,0,R100,MinCal)>0,"blast: below MinCalibreMm = 0");
Check(BlastMath.Damage(Base,100,2.01,R100,MinCal)==0&&BlastMath.Damage(Base,100,1.99,R100,MinCal)>0,"blast: nothing beyond 4 radii");
Check(BlastMath.Damage(Base,100,double.NaN,R100,MinCal)==0&&BlastMath.Damage(Base,100,0,0,MinCal)==0,"blast: invalid input = 0");
Check(Math.Abs(BlastMath.Strength(PerMm,3.2,150)-16)<1e-9&&Math.Abs(BlastMath.Strength(PerMm,20,150)-100)<1e-9,"strength: 5/mm x thickness (3.2 mm = 16, 20 mm = 100), no area factor");
Check(BlastMath.Strength(PerMm,0,150)==150&&BlastMath.Strength(PerMm,double.NaN,150)==150&&BlastMath.Strength(0,3.2,150)==150,"strength: unknown thickness = default");
Check(Math.Abs(BlastMath.ThicknessMm(0.0032)-3.2)<1e-9&&Math.Abs(BlastMath.ThicknessMm(3.2)-3.2)<1e-9&&BlastMath.ThicknessMm(0)==0&&BlastMath.ThicknessMm(double.NaN)==0,"thickness: metres or millimetres to mm");
// calibration targets with the default BlastBase 190 (director decision 2026-11-15; the formula is unchanged, BlastBase 100 would not reach the 57 mm target)
const double DefBase=190;
double Dm(double cal,double d)=>BlastMath.Damage(DefBase,cal,d,R100,MinCal);
Check(Dm(57,0.3)>=16&&Math.Abs(Dm(57,0.3)-16.69)<0.05,"calibration: 57 mm HE breaks a 3.2 mm skirt (16) at d = 0.3 m (16.7)");
Check(Dm(30,0)<16&&Dm(30,0.1)<16&&Dm(20,0)<16,"calibration: 20-30 mm HE never breaks a 3.2 mm skirt, even touching it");
Check(Dm(155,1.0)>=100&&Math.Abs(Dm(155,1.0)-265.5)<0.5,"calibration: 155 mm HE breaks a 20 mm plate (100) at 1 m (265.5)");
Check(Dm(105,0.5)>=100&&Math.Abs(Dm(105,0.5)-115.3)<0.5&&Dm(105,0.7)<100,"calibration: 105 mm HE breaks a 20 mm plate at about 0.5 m (115; not beyond 0.7 m)");
Check(Dm(57,0)<100&&Dm(75,0)<100,"calibration: a 20 mm plate needs 105 mm or larger");
// accumulation on the ledger (same calls as the Runtime)
var bl=new DamageLedger();double dd=Dm(57,0.3);
Check(!bl.Add(1,2201,PlateSide.Original,dd,30,out _,out _)&&bl.Add(1,2201,PlateSide.Original,dd,30,out var bh,out _)&&bh==2&&bl.IsBroken(1,2201,PlateSide.Original),"accumulate: damage adds up per side (two 57 mm shells at 0.3 m = 33 break strength 30, one does not)");
// kinetic adds nothing
var kl=new DamageLedger();bool kbreak=false;double ke=0;
for(int i=0;i<1000;i++){ke=KineticPolicy.Apply(kl,1,2201,PlateSide.Original,5.224,1500,out var kb);kbreak|=kb;}
Check(!kbreak&&!kl.IsBroken(1,2201,PlateSide.Original)&&kl.Hits(1,2201,PlateSide.Original)==0&&kl.BrokenCount==0,"kinetic: 1000 APFSDS hits add nothing and break nothing");
Check(Math.Abs(ke-5.877)<0.01&&KineticPolicy.BreakDamage(5.224,1500)==0&&KineticPolicy.BreakDamage(1e6,1e6)==0,"kinetic: energy is reported for the log, break damage is always 0");
Check(Math.Abs(DamageFormula.Kinetic(5.224,1500)-58.77)<0.1,"the old kinetic formula is kept in Logic (for dents)");
// side choice for blasts: each half uses its own distance
var bg=new SideGeometry(Mat4.Reflection(1,0,0,0,0,0),new V3(1.0f,0,0),new V3(1.2f,0.5f,3f)); // original x 1.0..1.2, mirror x -1.2..-1.0
var h1=BlastMath.Halves(bg,new V3(1.3f,0.2f,1f),Base,155,R100,MinCal);
Check(Math.Abs(h1.DistOriginal-0.1)<1e-5&&Math.Abs(h1.DistMirror-2.3)<1e-5,"blast side choice: distances to each half's bounds");
Check(h1.Original>200&&h1.Mirror>0&&h1.Mirror<h1.Original/5,"blast side choice: 155 mm next to the original half hurts that half most, the far half much less");
PlateSide Hurt(SideGeometry g,V3 p){var r=BlastMath.Halves(g,p,Base,155,R100,MinCal);return r.Original>r.Mirror?PlateSide.Original:PlateSide.Mirror;}
Check(Hurt(bg,new V3(1.3f,0.2f,1f))==PlateSide.Original&&Hurt(bg,new V3(-1.3f,0.2f,1f))==PlateSide.Mirror,"blast side choice: the nearer half takes the larger damage");
var mid=BlastMath.Halves(bg,new V3(0f,0.2f,1f),Base,155,R100,MinCal);
Check(Math.Abs(mid.Original-mid.Mirror)<1e-6&&mid.Original>0,"blast side choice: a blast on the mirror plane hurts both halves equally");
var far=BlastMath.Halves(bg,new V3(1.2f,0.2f,10f),Base,57,R100,MinCal);
Check(far.Original==0&&far.Mirror==0,"blast side choice: a blast far away (beyond 4 radii) hurts nothing");
var near20=BlastMath.Halves(bg,new V3(1.1f,0.2f,1f),Base,155,R100,MinCal);
Check(near20.DistOriginal==0&&Math.Abs(near20.Original-372.3875)<1e-3&&near20.Mirror>0&&near20.Mirror<near20.Original,"blast side choice: inside the bounds the distance is 0 (full damage), the other half gets the falloff");
Check(Math.Abs(bg.Closest(new V3(1.5f,0.2f,1f),PlateSide.Original).X-1.2f)<1e-6&&Math.Abs(bg.Closest(new V3(1.5f,0.2f,1f),PlateSide.Mirror).X+1.0f)<1e-6&&bg.Closest(new V3(1.1f,0.2f,1f),PlateSide.Original).X==1.1f,"closest point of a half's bounds");
// full scenario: a blast at one side tears off that side only, a blast at the other side then the rest (whole plate)
var sc=new DamageLedger();var scState=new PlateState();bool whole=false;
void BlastAt(V3 pos,double cal)
{
    var r=BlastMath.Halves(bg,pos,Base,cal,R100,MinCal);
    foreach(var (side,dmg) in new[]{(PlateSide.Original,r.Original),(PlateSide.Mirror,r.Mirror)})
    {
        if(dmg<=0||sc.IsGone(5,2201)||sc.IsBroken(5,2201,side))continue;
        if(sc.Add(5,2201,side,dmg,16,out _,out _))
        {
            var res=SideBreak.Resolve(true,side,sc.IsBroken(5,2201,SideBreak.Other(side)));
            if(res==PlateSide.Whole){sc.ForceBreak(5,2201,PlateSide.Whole);whole=true;}else scState.SetSideBroken(5,2201,res,true);
        }
    }
}
BlastAt(new V3(1.25f,0.2f,1f),100);
Check(scState.IsSideBroken(5,2201,PlateSide.Original)&&!scState.IsSideBroken(5,2201,PlateSide.Mirror)&&!whole,"scenario: a 100 mm blast at the original side tears off only that side");
BlastAt(new V3(-1.25f,0.2f,1f),100);
Check(whole&&sc.IsGone(5,2201),"scenario: a blast at the other side tears off the second half: the whole plate is gone");
// dedup of the same explosion seen twice in one frame
var dd2=new BlastDedup();
Check(!dd2.IsDuplicate(10,new V3(1,2,3),155)&&dd2.IsDuplicate(10,new V3(1,2,3.01f),155)&&!dd2.IsDuplicate(10,new V3(1,2,3),57)&&!dd2.IsDuplicate(11,new V3(1,2,3),155),"blast dedup: same frame/place/calibre once; other calibre or frame counts");

// marker layout and filtering (AddMesh/9)
Check(MarkerFilter.Classify(0,0,8,12)==MarkerLayout.None,"markers: absent");
Check(MarkerFilter.Classify(4,4,8,12)==MarkerLayout.PerTriangle,"markers: one per triangle (12 indices = 4 triangles)");
Check(MarkerFilter.Classify(8,8,8,12)==MarkerLayout.PerVertex,"markers: one per vertex");
Check(MarkerFilter.Classify(4,0,8,12)==MarkerLayout.PerTriangle&&MarkerFilter.Classify(0,4,8,12)==MarkerLayout.PerTriangle,"markers: only one of the two arrays present");
Check(MarkerFilter.Classify(5,4,8,12)==MarkerLayout.Unknown&&MarkerFilter.Classify(8,4,8,12)==MarkerLayout.Unknown&&MarkerFilter.Classify(4,3,8,12)==MarkerLayout.Unknown,"markers: an inconsistent mixture (one array per triangle/vertex, the other not) is Unknown: not filtered");
Check(MarkerFilter.Classify(4,4,4,12)==MarkerLayout.PerTriangle,"markers: triangles count wins when both match");
var mkv=new[]{new V3(1.0f,0,0),new V3(1.2f,0,0),new V3(1.2f,0.5f,0),new V3(1.0f,0.5f,0),new V3(-1.0f,0,0),new V3(-1.2f,0,0),new V3(-1.2f,0.5f,0),new V3(-1.0f,0.5f,0)};
var mkt=new[]{0,1,2, 0,2,3, 4,6,5, 4,7,6};
uint[] mm=new uint[]{10,11,12,13};byte[] mtp=new byte[]{1,2,3,4};
var kidx=SideSides(mkv,mkt,bg,PlateSide.Mirror);
Check(kidx.SequenceEqual(new[]{2,3}),"marker filter: kept triangle numbers of the mirrored half");
Check(SideTriangles.Gather(mkt,kidx).SequenceEqual(new[]{4,6,5,4,7,6})&&MarkerFilter.Pick(mm,kidx).SequenceEqual(new uint[]{12,13})&&MarkerFilter.Pick(mtp,kidx).SequenceEqual(new byte[]{3,4}),"marker filter: triangles, markers and types are cut consistently");
var kidx2=SideSides(mkv,mkt,bg,PlateSide.Original);
Check(MarkerFilter.Pick(mm,kidx2).SequenceEqual(new uint[]{10,11})&&SideTriangles.Gather(mkt,kidx2).Length/3==MarkerFilter.Pick(mtp,kidx2).Length,"marker filter: the original half keeps markers 10,11 and as many types as triangles");
Check(SideSides(mkv,new[]{0,1,2,0,1,99,4,6,5},bg,PlateSide.Mirror).SequenceEqual(new[]{2}),"marker filter: invalid triangles are dropped and the numbers still refer to the original triangle list");
Check(SideTriangles.Keep(mkv,mkt,bg,PlateSide.Mirror).SequenceEqual(SideTriangles.Gather(mkt,kidx)),"Keep = Gather(KeepIndices)");
List<int> SideSides(V3[] v,int[] t,SideGeometry g,PlateSide s)=>SideTriangles.KeepIndices(v,t,g,s);

// attached parts: the side of a part is decided by its position like a hit (ERA on the left skirt goes with the left side)
Check(bg.Choose(new V3(1.1f,0.55f,1.5f))==PlateSide.Original&&bg.Choose(new V3(-1.1f,0.55f,1.5f))==PlateSide.Mirror,"attached: a part sitting on a half belongs to that half");
var so=BreakPipeline.SideOrder.ToList();var wo=BreakPipeline.Order.ToList();
Check(so.IndexOf(BreakStep.HideAttached)>=0&&so.IndexOf(BreakStep.HideAttached)<so.IndexOf(BreakStep.RaiseSide)&&wo.IndexOf(BreakStep.HideAttached)>=0&&wo.IndexOf(BreakStep.HideAttached)<wo.IndexOf(BreakStep.RemoveFromModel),"attached: hidden/excluded before the damage model rebuild in both break paths");


// ---- 0.3.1 ----
// 1. independent marker list (seen in the game: vertices 3648, triangles 1804 = 601 tris, markers 6, markerTypes 6)
Check(MarkerFilter.Classify(6,6,3648,5412)==MarkerLayout.Independent,"markers 0.3.1: 6 markers + 6 types on 3648 vertices / 5412 indices (1804 triangles) = Independent");
Check(MarkerFilter.Classify(6,0,3648,5412)==MarkerLayout.Independent&&MarkerFilter.Classify(0,6,3648,5412)==MarkerLayout.Independent,"markers 0.3.1: only one independent array present");
Check(MarkerFilter.Classify(1804,1804,3648,5412)==MarkerLayout.PerTriangle&&MarkerFilter.Classify(3648,3648,3648,5412)==MarkerLayout.PerVertex,"markers 0.3.1: per-triangle and per-vertex still recognised");
Check(MarkerFilter.Classify(1804,6,3648,5412)==MarkerLayout.Unknown,"markers 0.3.1: per-triangle markers with an independent types array stay refused");
// independent markers: the triangles are filtered, the marker arrays are not touched (what the prefix does for Independent)
var imk=new uint[]{1,2,3,4,5,6};var ikeep=SideTriangles.KeepIndices(mkv,mkt,bg,PlateSide.Mirror);
Check(SideTriangles.Gather(mkt,ikeep).Length==6&&imk.Length==6&&imk.SequenceEqual(new uint[]{1,2,3,4,5,6}),"markers 0.3.1: independent markers stay unchanged while the triangles are filtered");
// 4. torn-side check
Check(SegmentBox.Intersects(new V3(2,0.2f,1),new V3(0.5f,0.2f,1),new V3(1.0f,0,0),new V3(1.2f,0.5f,3f)),"segment: a shot across the original half's bounds intersects");
Check(!SegmentBox.Intersects(new V3(2,0.2f,1),new V3(1.5f,0.2f,1),new V3(1.0f,0,0),new V3(1.2f,0.5f,3f)),"segment: a segment that ends before the box does not");
Check(!SegmentBox.Intersects(new V3(2,0.2f,5),new V3(-2,0.2f,5),new V3(1.0f,0,0),new V3(1.2f,0.5f,3f)),"segment: passing beside the box (z=5) does not");
Check(SegmentBox.Intersects(new V3(1.1f,0.2f,1),new V3(1.1f,0.2f,1.0f),new V3(1.0f,0,0),new V3(1.2f,0.5f,3f)),"segment: a degenerate segment inside the box intersects");
Check(SegmentBox.Intersects(new V3(0,0.2f,1),new V3(-2,0.2f,1),bg.Bounds(PlateSide.Mirror).Min,bg.Bounds(PlateSide.Mirror).Max)&&!SegmentBox.Intersects(new V3(0,0.2f,1),new V3(-2,0.2f,1),bg.Bounds(PlateSide.Original).Min,bg.Bounds(PlateSide.Original).Max),"segment: bounds of each half from SideGeometry.Bounds");
var tc0=TornCheck.Evaluate(new List<PlateSide>(),PlateSide.Mirror);
Check(tc0.Hits==0&&tc0.Pass,"torn check: no hit on the plate = PASS");
var tc1=TornCheck.Evaluate(new[]{PlateSide.Original,PlateSide.Original},PlateSide.Mirror);
Check(tc1.Hits==0&&tc1.Pass,"torn check: hits only on the other (intact) side do not count");
var tc2=TornCheck.Evaluate(new[]{PlateSide.Original,PlateSide.Mirror},PlateSide.Mirror);
Check(tc2.Hits==1&&!tc2.Pass,"torn check: a hit on the torn side = FAIL (1)");
// hit side from the hit position, as the runtime does it
Check(TornCheck.Evaluate(new[]{bg.Choose(new V3(-1.1f,0.2f,1f)),bg.Choose(new V3(1.1f,0.2f,1f))},PlateSide.Mirror).Hits==1,"torn check: the hit position decides the side");

// ---- 0.4.0: dents ----
Check(DentMath.Falloff(0,0.12)==1.0&&DentMath.Falloff(0.12,0.12)==0&&DentMath.Falloff(0.2,0.12)==0&&Math.Abs(DentMath.Falloff(0.06,0.12)-0.5625)<1e-9,"dent falloff: 1 in the centre, (1-(d/R)^2)^2 inside, 0 at R and beyond");
Check(DentMath.Falloff(-1,0.12)==1.0&&DentMath.Falloff(0.05,0)==0&&DentMath.Falloff(double.NaN,0.1)==0,"dent falloff: odd input is harmless");
V3[] DGrid(float y,float half,float step){var l=new List<V3>();for(float x=-half;x<=half+1e-4f;x+=step)for(float z=-half;z<=half+1e-4f;z+=step)l.Add(new V3(x,y,z));return l.ToArray();}
var dg=DGrid(0,0.5f,0.02f);var df=new DentField(dg,null,0.15);
int dmoved=df.Apply(new V3(0,0,0),new V3(0,1,0),0.015,0.12,PlateSide.Whole);
int dcentre=Array.FindIndex(dg,p=>Math.Abs(p.X)<1e-5&&Math.Abs(p.Z)<1e-5);
Check(dmoved>50&&dmoved<150,"dent: vertices moved are those within R only ("+dmoved+")");
Check(Math.Abs(df.Disp[dcentre].Y-0.015)<1e-6&&Math.Abs(df.Disp[dcentre].X)<1e-9,"dent: centre vertex moves by exactly the depth along the normal");
bool dOutside=true,dAlong=true;for(int i=0;i<dg.Length;i++){double d=Math.Sqrt((double)dg[i].X*dg[i].X+(double)dg[i].Z*dg[i].Z);if(d>=0.12&&VM.Len(df.Disp[i])!=0)dOutside=false;if(Math.Abs(df.Disp[i].X)>1e-9||Math.Abs(df.Disp[i].Z)>1e-9)dAlong=false;}
Check(dOutside,"dent: no vertex at or beyond R moves");Check(dAlong,"dent: every displacement is along the normal");
// cap per vertex
var dcap=new DentField(DGrid(0,0.3f,0.02f),null,0.15);for(int i=0;i<30;i++)dcap.Apply(new V3(0,0,0),new V3(0,1,0),0.02,0.12,PlateSide.Whole);
Check(Math.Abs(dcap.MaxDisplacement()-0.15)<1e-6,"dent cap: 30 x 20 mm at one spot ends at exactly 150 mm");
var dcap2=new DentField(DGrid(0,0.3f,0.02f),null,0.15);for(int i=0;i<5;i++)dcap2.Apply(new V3(0,0,0),new V3(0,1,0),0.02,0.12,PlateSide.Whole);
Check(Math.Abs(dcap2.MaxDisplacement()-0.10)<1e-6,"dent cap: below the cap the dents add up (5 x 20 mm = 100 mm)");
var dcap3=new DentField(DGrid(0,0.3f,0.02f),null,0.15);dcap3.Apply(new V3(0,0,0),new V3(0,1,0),0.4,0.12,PlateSide.Whole);
Check(Math.Abs(dcap3.MaxDisplacement()-0.15)<1e-6,"dent cap: a single deep dent is capped too");
// both shells move by the same vector: the thickness stays
var shellA=DGrid(0,0.4f,0.02f);var shellB=DGrid(-0.02f,0.4f,0.02f);
var both=new DentField(shellA.Concat(shellB).ToArray(),null,0.15);
both.Apply(new V3(0.05f,-0.01f,0.02f),new V3(0,1,0),0.06,0.2,PlateSide.Whole);both.Apply(new V3(-0.1f,0,0.1f),new V3(0,1,0),0.04,0.15,PlateSide.Whole);
bool thick=true;for(int i=0;i<shellA.Length;i++){var pa=both.Position(i);var pb=both.Position(shellA.Length+i);if(Math.Abs((pa.Y-pb.Y)-0.02)>1e-6||Math.Abs(pa.X-pb.X)>1e-6||Math.Abs(pa.Z-pb.Z)>1e-6)thick=false;}
Check(thick,"dent: both shells move by the same vector (thickness 20 mm stays)");
var tilt=VM.Norm(new V3(0.3f,1,0.1f),new V3(0,1,0));
var shellT=shellA.Select(q=>new V3((float)(q.X-0.02*tilt.X),(float)(q.Y-0.02*tilt.Y),(float)(q.Z-0.02*tilt.Z))).ToArray();var both2=new DentField(shellA.Concat(shellT).ToArray(),null,0.15);both2.Apply(new V3(0,0,0),tilt,0.05,0.25,PlateSide.Whole);
bool same=true;for(int i=0;i<shellA.Length;i++){var da=both2.Disp[i];var db=both2.Disp[shellA.Length+i];if(Math.Abs(da.X-db.X)>1e-6||Math.Abs(da.Y-db.Y)>1e-6||Math.Abs(da.Z-db.Z)>1e-6)same=false;}
Check(same,"dent: the displacement vector of both shells is equal for a tilted normal too (shells offset along that normal)");
// side filter: original half at x=+1.2.., mirror half at x=-1.2..; a dent with a side moves only that side's vertices; Whole + normal reach never reaches the other half
var sv=new List<V3>();var ss=new List<byte>();
for(float z=-0.3f;z<=0.3f;z+=0.05f){sv.Add(new V3(1.2f,0,z));ss.Add((byte)PlateSide.Original);sv.Add(new V3(-1.2f,0,z));ss.Add((byte)PlateSide.Mirror);}
var sf=new DentField(sv.ToArray(),ss.ToArray(),0.15);
int mo=sf.Apply(new V3(1.2f,0,0),new V3(1,0,0),0.03,0.2,PlateSide.Original);
bool onlyOrig=true;for(int i=0;i<sv.Count;i++)if(ss[i]==(byte)PlateSide.Mirror&&VM.Len(sf.Disp[i])!=0)onlyOrig=false;
Check(mo>0&&onlyOrig,"dent side filter: an event on the original half moves no vertex of the mirrored half");
var dsf2=new DentField(sv.ToArray(),ss.ToArray(),0.15);dsf2.Apply(new V3(1.2f,0,0),new V3(1,0,0),0.03,0.2,PlateSide.Mirror);
Check(dsf2.MaxDisplacement()==0,"dent side filter: an event on the mirrored half at the original's place moves nothing");
var dsf3=new DentField(sv.ToArray(),null,0.15);dsf3.Apply(new V3(1.2f,0,0),new V3(1,0,0),0.03,0.2,PlateSide.Whole);
bool farOk=true;for(int i=0;i<sv.Count;i++)if(ss[i]==(byte)PlateSide.Mirror&&VM.Len(dsf3.Disp[i])!=0)farOk=false;
Check(farOk,"dent normal reach: without side information the half 2.4 m away along the normal stays still");
// depth and radius from q
Check(DentMath.BlastRatio(50,100)==0.5&&DentMath.BlastRatio(250,100)==1.0&&DentMath.BlastRatio(-3,100)==0&&DentMath.BlastRatio(10,0)==0&&DentMath.BlastRatio(10,double.PositiveInfinity)==0,"dent q = D/strength, clamped to 0..1, 0 for no strength");
Check(Math.Abs(DentMath.BlastDepthMm(0.5,120)-60)<1e-9&&DentMath.BlastDepthMm(1.7,120)==120&&DentMath.BlastDepthMm(0,120)==0,"dent depth = 120 mm x q");
Check(Math.Abs(DentMath.BlastRadiusM(0.6,0.6)-0.36)<1e-9&&DentMath.BlastRadiusM(0.05,0.6)==0.10&&DentMath.BlastRadiusM(5,0.6)==1.0,"dent radius = clamp(0.6 x r_blast, 0.10, 1.0) m");
Check(Math.Abs(BlastMath.Radius(0.5,57)*0.6-0.171)<1e-9,"dent radius: 57 mm blast (r 0.285 m) -> 0.171 m");
Check(Math.Abs(DentMath.BlastDepthMm(0.5,120)/1000-0.060)<1e-9&&DentMath.NormalReach(0.05)==0.25&&DentMath.NormalReach(0.8)==0.8,"dent: test key q 0.5 = 60 mm; normal reach");
// mesh queries (ray entry and closest point on a quad of two triangles at y=0)
var qv0=new[]{new V3(-1,0,-1),new V3(1,0,-1),new V3(1,0,1),new V3(-1,0,1)};var qt0=new[]{0,2,1,0,3,2};
bool rh=MeshQuery.RayFirst(qv0,qt0,new V3(0.2f,1,0.3f),new V3(0,-1,0),5,null,out double rd,out V3 rn,out int rtri);
Check(rh&&Math.Abs(rd-1)<1e-9&&Math.Abs(Math.Abs(rn.Y)-1)<1e-6,"mesh ray: hits the quad at distance 1 with a normal along Y");
Check(!MeshQuery.RayFirst(qv0,qt0,new V3(3,1,0),new V3(0,-1,0),5,null,out _,out _,out _),"mesh ray: a ray beside the quad misses");
Check(!MeshQuery.RayFirst(qv0,qt0,new V3(0.2f,1,0.3f),new V3(0,-1,0),0.5,null,out _,out _,out _),"mesh ray: limited by the maximum distance");
Check(!MeshQuery.RayFirst(qv0,qt0,new V3(0.2f,1,0.3f),new V3(0,-1,0),5,i=>false,out _,out _,out _),"mesh ray: the triangle filter is honoured");
bool ch=MeshQuery.Closest(qv0,qt0,new V3(0.5f,2,0.5f),null,out V3 cpnt,out _);
Check(ch&&Math.Abs(cpnt.X-0.5)<1e-6&&Math.Abs(cpnt.Y)<1e-6&&Math.Abs(cpnt.Z-0.5)<1e-6,"mesh closest: the point above the quad projects straight down");
MeshQuery.Closest(qv0,qt0,new V3(5,0,0),null,out V3 cpe,out _);
Check(Math.Abs(cpe.X-1)<1e-6&&Math.Abs(cpe.Z)<1e-6,"mesh closest: a point beside the quad lands on its edge");
Check(DentMath.Orient(new V3(0,1,0),new V3(0,-1,0)).Y==-1&&DentMath.Orient(new V3(0,1,0),new V3(0,1,0)).Y==1,"dent normal is oriented away from the attacker");

// ---- 0.4.0: debris ----

{
    var body=new DebrisBody(new V3(0,10,0),new V3(3,5,0));var ground=new FlatGround{Y=0};
    int steps=0;while(body.State==DebrisState.Flying&&steps++<2000)body.Step(1.0/60.0,ground);
    double analytic=(5+Math.Sqrt(25+2*9.81*10))/9.81;
    Check(body.State==DebrisState.Landed,"debris: lands on flat ground");
    Check(Math.Abs(body.LandedAt-analytic)<0.01,$"debris: time of flight {body.LandedAt:0.0000} s matches the analytic {analytic:0.0000} s");
    Check(Math.Abs(body.Pos.X-3*analytic)<0.05&&Math.Abs(body.Pos.Y)<1e-4,"debris: lands where vx x t says, on the ground");
    var before=body.Pos;body.Step(1.0/60.0,ground);Check(body.Pos.X==before.X&&body.Pos.Y==before.Y&&body.State==DebrisState.Landed,"debris: lies still after landing");
    var fast=new DebrisBody(new V3(0,2,0),new V3(0,0,0));int n2=0;while(fast.State==DebrisState.Flying&&n2++<2000)fast.Step(1.0/30.0,ground);
    Check(Math.Abs(fast.LandedAt-Math.Sqrt(2*2/9.81))<0.01,"debris: drop from 2 m with a coarser step still matches sqrt(2h/g)");
    var dearly=new DebrisBody(new V3(0,0.1f,0),new V3(0,-1,0));var slab=new FlatGround{Y=0,Slab=true};int n3=0;while(dearly.State==DebrisState.Flying&&n3++<2000)dearly.Step(1.0/60.0,slab);
    Check(dearly.State==DebrisState.Landed&&dearly.LandedAt>=0.15-1.0/60.0,"debris: a ground hit in the first 0.15 s is ignored");
    var lost=new DebrisBody(new V3(0,0,0),new V3(1,0,0));int n4=0;while(lost.State==DebrisState.Flying&&n4++<5000)lost.Step(1.0/60.0,null);
    Check(lost.State==DebrisState.Lost&&lost.Pos.Y<-50&&lost.Pos.Y>-60,"debris: removed below the start height minus 50 m (no ground)");
}
Check(Ballistics.Speed(3,25,8,0)==3&&Ballistics.Speed(3,25,8,1)==11&&Ballistics.Speed(3,25,8,5)==25&&Ballistics.Speed(3,25,8,-2)==3&&Ballistics.Speed(3,25,8,double.NaN)==3,"debris speed = clamp(3 + 8 x D/strength, 3, 25)");
{
    var dir=Ballistics.Direction(new V3(0,0,0),new V3(1,0,0));
    Check(Math.Abs(VM.Len(dir)-1)<1e-6&&Math.Abs(dir.X-0.9578)<1e-3&&Math.Abs(dir.Y-0.2873)<1e-3,"debris direction: away from the blast plus an upward share of 0.3");
    var dirUp=Ballistics.Direction(new V3(1,1,1),new V3(1,1,1));
    Check(dirUp.Y==1&&dirUp.X==0,"debris direction: blast at the centroid gives straight up");
    var s1=Ballistics.Spin(42);var s2=Ballistics.Spin(42);var s3=Ballistics.Spin(43);
    Check(s1.DegPerS==s2.DegPerS&&s1.Axis.X==s2.Axis.X&&s1.DegPerS!=s3.DegPerS&&s1.DegPerS>=0&&s1.DegPerS<=360&&Math.Abs(VM.Len(s1.Axis)-1)<1e-5,"debris spin: seeded, repeatable, up to 360 deg/s, unit axis");
}
{
    var book=new DebrisBook<string>(16,120);var removed=new List<string>();
    for(int i=0;i<20;i++)removed.AddRange(book.Add("d"+i,i));
    Check(book.Count==16&&removed.Count==4&&removed[0]=="d0"&&removed[3]=="d3","debris cap: 20 added to a book of 16 removes the 4 oldest, in order");
    Check(!book.Items.Contains("d3")&&book.Items.Contains("d4"),"debris cap: the survivors are the newest 16");
    var gone=book.Expire(130);
    Check(gone.Count==7&&book.Count==9&&gone[0]=="d4"&&gone[6]=="d10","debris age-out: at t=130 s everything born up to t=10 s is gone ("+gone.Count+")");
    Check(book.Expire(130).Count==0,"debris age-out: nothing twice");
    var gone2=book.Expire(130.9);Check(gone2.Count==0&&book.Count==9,"debris age-out: just below the lifetime nothing goes");
    Check(book.Expire(131).Count==1&&book.Count==8,"debris age-out: at exactly the lifetime the object goes");
    var all=book.Clear();Check(all.Count==8&&book.Count==0,"debris: Clear returns and empties");
    var b1=new DebrisBook<string>(0,10);b1.Add("a",0);var rm=b1.Add("b",1);Check(b1.Count==1&&rm.Count==1,"debris cap: a maximum below 1 still keeps one object");
}

// ---- 0.4.1: cells ----
{
    // T-72A skirt as measured in the 0.4.0 log: original half x -0.41..0.664, y -0.495..0.12, z -4.1..2.533; mirror x -> -x + 2.824
    var mir=new Mat4(new float[]{-1,0,0,2.824f, 0,1,0,0, 0,0,1,0, 0,0,0,1});
    var g=new SideGeometry(mir,new V3(-0.41f,-0.495f,-4.1f),new V3(0.664f,0.12f,2.533f));
    // a tessellated shell: a grid of quads (two triangles each) over both halves, in structure space
    (V3[],int[]) Grid(float step,float yy,bool flip,float jitterV=0f)
    {
        var vs=new List<V3>();var ts=new List<int>();
        void Quad(float xa,float xb,float za,float zb)
        {
            int b0=vs.Count;vs.Add(new V3(xa,yy,za));vs.Add(new V3(xb,yy,za));vs.Add(new V3(xb,yy,zb));vs.Add(new V3(xa,yy,zb));
            if(!flip){ts.AddRange(new[]{b0,b0+1,b0+2,b0,b0+2,b0+3});}else{ts.AddRange(new[]{b0+2,b0+1,b0,b0+3,b0+2,b0});}
        }
        for(float x=-0.41f;x<0.664f-step/2;x+=step)for(float z=-4.1f;z<2.533f-step/2;z+=step){Quad(x,x+step,z,z+step);Quad(2.824f-x-step,2.824f-x,z,z+step);}
        return (vs.ToArray(),ts.ToArray());
    }
    var (hv,ht)=Grid(0.05f,0.0f,false);
    var plan=CellPlan.Build(hv,ht,g,0.5f,0.08,12345u);
    var fo=plan.Frames[(int)PlateSide.Original]!;var fm=plan.Frames[(int)PlateSide.Mirror]!;
    Check(plan.Frames[0]==null&&plan.Frames[1]!=null&&plan.Frames[2]!=null,"cells: a mirrored plate with a split has a frame per half and none for Whole");
    Check(fo.AxisU==2&&fo.AxisV==0,"cells: the two largest extents of the skirt half are z (6.63 m) and x (1.07 m)");
    Check(fo.Nu==13&&fo.Nv==2&&fo.SizeU>0.49f&&fo.SizeU<0.53f&&fo.SizeV>0.5f&&fo.SizeV<0.56f,"cells: 0.5 m cells divide the half evenly (13 x 2, about 0.51 x 0.54 m; got "+fo.Nu+"x"+fo.Nv+" "+fo.SizeU+" x "+fo.SizeV+")");
    Check(fm.Nu==fo.Nu&&fm.Nv==fo.Nv,"cells: the mirror half gets the same grid");
    // deterministic
    var a1=plan.Assign(hv,ht);var plan2=CellPlan.Build(hv,ht,g,0.5f,0.08,12345u);var a2=plan2.Assign(hv,ht);
    Check(a1.SequenceEqual(a2),"cells: assignment is deterministic (same seed, same mesh)");
    var plan3=CellPlan.Build(hv,ht,g,0.5f,0.08,999u);var a3=plan3.Assign(hv,ht);
    int differs=0;for(int i=0;i<a1.Length;i++)if(a1[i]!=a3[i])differs++;
    Check(differs>0,"cells: another seed gives another ragged edge ("+differs+" triangles differ)");
    var planFlat=CellPlan.Build(hv,ht,g,0.5f,0.0,12345u);var af=planFlat.Assign(hv,ht);
    int rag=0,farc=0;for(int i=0;i<a1.Length;i++)if(a1[i]!=af[i]){rag++;
        var fr=plan.Frames[(int)CellPlan.SideOf(a1[i])]!;int di1=CellPlan.IndexOf(a1[i]),d0=CellPlan.IndexOf(af[i]);
        if(Math.Abs(di1%fr.Nu-d0%fr.Nu)>1||Math.Abs(di1/fr.Nu-d0/fr.Nu)>1)farc++;}
    Check(rag>20&&farc==0,"cells: the jitter makes the edge ragged ("+rag+" of "+a1.Length+" triangles moved) but never by more than one cell");
    Check(a1.All(c=>c>=0)&&a1.Select(CellPlan.SideOf).Distinct().Count()==2,"cells: every triangle has a cell, on exactly the two halves");
    // roughly 0.5 m cells, a ragged edge keeps about that area
    var counts=a1.GroupBy(c=>c).Select(x=>x.Count()).ToList();
    Check(counts.Count==2*13*2&&counts.Min()>0,"cells: 52 non-empty cells over both halves ("+counts.Count+")");
    // same structure-space triangle -> same cell in the render meshes and the armour (other vertex order, other winding, other position in the arrays, other tessellation point set)
    var tri=new V3[]{new V3(0.1f,0.0f,-1.02f),new V3(0.35f,0.0f,-0.88f),new V3(0.2f,0.0f,-0.6f)};
    var meshA=new List<V3>{new V3(9,9,9)};meshA.AddRange(tri);var triA=new[]{1,2,3};                    // 'render' mesh: padded, in order
    var meshB=new List<V3>(tri.Reverse());meshB.Add(new V3(-3,0,5));var triB=new[]{1,0,2};                // 'armour' mesh: reversed order, other winding
    Check(plan.CellOfTriangle(meshA.ToArray(),triA,0)==plan.CellOfTriangle(meshB.ToArray(),triB,0),"cells: the same structure-space triangle gets the same cell in the render mesh and the armour (vertex order does not matter)");
    var mtri=tri.Select(q=>mir.Point(q)).ToArray();
    Check(CellPlan.SideOf(plan.CellOfTriangle(mtri,new[]{0,1,2},0))==PlateSide.Mirror&&plan.CellOfTriangle(mtri,new[]{0,1,2},0)!=plan.CellOfTriangle(tri,new[]{0,1,2},0),"cells: the mirrored triangle sits in a mirror-half cell");
    Check(plan.CellOfTriangle(tri,new[]{0,1,7},0)==-1,"cells: a triangle with an invalid index has no cell");
    Check(CellPlan.IndexOf(plan.CellOfPoint(new V3(0.2f,0,-500f),PlateSide.Original))<fo.Nu*fo.Nv&&CellPlan.IndexOf(plan.CellOfPoint(new V3(90f,0,500f),PlateSide.Original))<fo.Nu*fo.Nv,"cells: a point far outside lands on an edge cell");

    // an unmirrored plate: one frame (Whole)
    var wholePlan=CellPlan.Build(hv.Take(0).ToArray(),ht.Take(0).ToArray(),null,0.5f,0.08,1u);
    Check(wholePlan.Sides.Count()==0,"cells: no triangle, no frame");
    var (wv,wt)=Grid(0.1f,0.0f,false);var wplan=CellPlan.Build(wv,wt,null,0.5f,0.08,7u);
    Check(wplan.Sides.SequenceEqual(new[]{PlateSide.Whole})&&wplan.Frames[0]!=null&&wplan.SideOfPoint(new V3(0,0,0))==PlateSide.Whole,"cells: an unsplit plate has a single Whole frame");
    // the grid size is capped
    var big=new CellFrame(PlateSide.Whole,new V3(0,0,0),new V3(100,100,0.1f),0.1f);
    Check(big.Count<=CellFrame.MaxCells,"cells: the cell count is capped ("+big.Count+")");

    // ---- cell damage and break ----
    var cellsInfo=new Dictionary<int,CellInfo>();
    for(int i=0;i<a1.Length;i++){int c=a1[i];if(!cellsInfo.TryGetValue(c,out var ci)){ci=new CellInfo{Min=new V3(float.MaxValue,float.MaxValue,float.MaxValue),Max=new V3(float.MinValue,float.MinValue,float.MinValue)};cellsInfo[c]=ci;}
        for(int k=0;k<3;k++){var q=hv[ht[i*3+k]];ci.Min=new V3(Math.Min(ci.Min.X,q.X),Math.Min(ci.Min.Y,q.Y),Math.Min(ci.Min.Z,q.Z));ci.Max=new V3(Math.Max(ci.Max.X,q.X),Math.Max(ci.Max.Y,q.Y),Math.Max(ci.Max.Z,q.Z));}ci.Tris++;}
    var pc=new PlateCells(plan,cellsInfo,16.2);
    Check(pc.Total==52&&pc.BrokenCount==0&&pc.CountIn(PlateSide.Original)==26&&pc.CountIn(PlateSide.Mirror)==26,"cell state: 52 cells, none broken");
    // 112 mm HE 0.3 m beside the original half, at z=-3.5
    var blast=new V3(-0.7f,0,-3.5f);
    var o1=pc.Blast(blast,112,190,0.5,20,true);
    Check(o1.Reached>0&&o1.NewBroken.Count>0&&o1.NewBroken.Count<26,"cell blast: 112 mm HE breaks some cells of the near half, not all ("+o1.NewBroken.Count+" of 26)");
    Check(o1.NewBroken.All(c=>CellPlan.SideOf(c)==PlateSide.Original),"cell blast: the far (mirror) half is out of reach");
    Check(pc.BrokenCount==o1.NewBroken.Count&&o1.NewBroken.All(c=>pc.IsBroken(c)&&pc.Damage(c)>=16.2),"cell blast: broken cells are marked and had damage >= strength");
    // per cell distance: the cell under the blast takes more than a cell 2 m away
    var near=o1.Dmg.Where(kv=>BoxMath.Distance(blast,cellsInfo[kv.Key].Min,cellsInfo[kv.Key].Max)<0.4).Select(kv=>kv.Value).Max();
    var farD=o1.Dmg.Where(kv=>BoxMath.Distance(blast,cellsInfo[kv.Key].Min,cellsInfo[kv.Key].Max)>1.5).Select(kv=>kv.Value).DefaultIfEmpty(0).Max();
    Check(near>farD&&farD>0,"cell blast: the damage falls off per cell with the distance to the cell's triangle bounds ("+near.ToString("0")+" near, "+farD.ToString("0.0")+" far)");
    double expect=BlastMath.Damage(190,112,BoxMath.Distance(blast,cellsInfo[o1.Dmg.Keys.First()].Min,cellsInfo[o1.Dmg.Keys.First()].Max),0.5,20);
    Check(Math.Abs(o1.Dmg[o1.Dmg.Keys.First()]-expect)<1e-9,"cell blast: D_cell = BlastBase x (cal/100)^3 x falloff(d_cell)");
    // damage accumulates per cell over blasts; a second equal blast breaks nothing that already broke and does not touch broken cells
    var before=pc.BrokenCount;var o2=pc.Blast(blast,112,190,0.5,20,true);
    Check(o2.NewBroken.All(c=>!o1.NewBroken.Contains(c))&&pc.BrokenCount==before+o2.NewBroken.Count,"cell blast: a broken cell takes no further damage and breaks no second time");
    // accumulation below the strength: weak blast (20 mm) twice
    var pcw=new PlateCells(plan,cellsInfo,500);
    var w1=pcw.Blast(blast,57,190,0.5,20,true);Check(w1.NewBroken.Count==0&&w1.Reached>0,"cell damage: a small blast below the strength breaks nothing but is counted ("+w1.Reached+" cells)");
    int c0=w1.Dmg.OrderByDescending(kv=>kv.Value).First().Key;double d1c=pcw.Damage(c0);
    pcw.Blast(blast,57,190,0.5,20,true);Check(Math.Abs(pcw.Damage(c0)-2*d1c)<1e-9,"cell damage: damage adds up per cell over blasts");
    int n=0;while(pcw.BrokenCount==0&&n++<200)pcw.Blast(blast,57,190,0.5,20,true);
    Check(pcw.BrokenCount>0&&n>1,"cell damage: repeated small blasts finally break the nearest cell ("+n+" blasts)");
    Check(new PlateCells(plan,cellsInfo,16.2).Blast(blast,112,190,0.5,20,false).NewBroken.Count==0,"cell blast: BreakEnabled=false breaks nothing");
    Check(new PlateCells(plan,cellsInfo,16.2).Blast(blast,15,190,0.5,20,true).Reached==0,"cell blast: below MinCalibreMm nothing happens");
    // all cells of a side
    var pca=new PlateCells(plan,cellsInfo,16.2);var rest=pca.BreakAll(PlateSide.Original);
    Check(rest.Count==26&&pca.AllBroken(PlateSide.Original)&&!pca.AllBroken(PlateSide.Mirror)&&pca.BrokenIn(PlateSide.Original)==26&&pca.Unbroken(PlateSide.Original).Count==0&&pca.BreakAll(PlateSide.Original).Count==0,"cell state: BreakAll takes every unbroken cell of one side once");

    // ---- dent q only from unbroken cells ----
    var pcd=new PlateCells(plan,cellsInfo,100);
    var od=pcd.Blast(new V3(-0.7f,0,-3.5f),112,190,0.5,20,true);
    Check(od.NewBroken.Count>0&&od.DentQ.TryGetValue(PlateSide.Original,out var qd)&&qd<1&&qd>0,"dent q: cells in reach that did not break give q in 0..1 ("+(od.DentQ.TryGetValue(PlateSide.Original,out var q0)?q0.ToString("0.00"):"none")+")");
    double maxBroken=od.BrokenRatio[PlateSide.Original];
    double maxUnbroken=od.Dmg.Where(kv=>!od.NewBroken.Contains(kv.Key)).Select(kv=>kv.Value/100.0).Max();
    Check(Math.Abs(od.DentQ[PlateSide.Original]-Math.Min(1,maxUnbroken))<1e-9&&maxBroken>=1.0,"dent q: = max D_cell/strength over the unbroken cells only (the broken cells' ratio "+maxBroken.ToString("0.00")+" is not used)");
    var pce=new PlateCells(plan,cellsInfo,1e9);var oe=pce.Blast(blast,112,190,0.5,20,true);
    Check(oe.NewBroken.Count==0&&oe.BrokenRatio.Count==0&&oe.DentQ[PlateSide.Original]>0&&oe.DentQ[PlateSide.Original]<1e-6,"dent q: with nothing breaking, q is the largest D/strength (tiny for a huge strength)");
    var pcf=new PlateCells(plan,cellsInfo,1e9);pcf.BreakAll(PlateSide.Original);
    Check(pcf.Blast(blast,112,190,0.5,20,true).DentQ.ContainsKey(PlateSide.Original)==false,"dent q: a side whose cells are all broken gets no dent");
    // alive vertices and dents
    var qv=new[]{new V3(0,0,0),new V3(1,0,0),new V3(1,0,1),new V3(0,0,1),new V3(5,0,5)};var qt=new[]{0,1,2,0,2,3};
    var alive=CellMask.AliveVertices(5,qt,new[]{10,20},c=>c==10);
    Check(alive[2]&&alive[3]&&alive[0]&&!alive[1]&&!alive[4],"cell mask: a vertex lives while one of its triangles is in an unbroken cell");
    var dfa=new DentField(qv,null,0.15);dfa.Alive=new[]{true,false,true,true,false};
    int dm=dfa.Apply(new V3(0.5f,0,0.5f),new V3(0,1,0),0.03,2.0,PlateSide.Whole);
    Check(dm==3&&VM.Len(dfa.Disp[1])==0&&VM.Len(dfa.Disp[0])>0,"dent: vertices of broken cells (not alive) do not move");
    Check(CellMask.KeepUnbroken(new[]{10,20,-1,10},c=>c==10).SequenceEqual(new[]{1}),"cell mask: the unbroken triangles are those of unbroken cells (cell -1 is dropped)");

    // ---- armour filter drops the triangles of broken cells ----
    var armour=ArmourCells.Keep(hv,ht,plan,pc.Broken,null);
    var allCells=plan.Assign(hv,ht);
    Check(armour.Count==allCells.Count(c=>!pc.IsBroken(c))&&armour.All(i=>!pc.IsBroken(allCells[i])),"armour filter: keeps exactly the triangles of unbroken cells");
    Check(armour.Count<allCells.Length&&armour.Count>allCells.Length/2,"armour filter: it drops some triangles ("+armour.Count+" of "+allCells.Length+" stay)");
    var cand=Enumerable.Range(0,allCells.Length).Where(i=>i%2==0).ToList();
    var armour2=ArmourCells.Keep(hv,ht,plan,pc.Broken,cand);
    Check(armour2.All(i=>i%2==0&&!pc.IsBroken(allCells[i]))&&armour2.Count>0,"armour filter: applied after the side rule it only removes from that candidate list (intersection)");
    var gathered=SideTriangles.Gather(ht,armour);
    Check(gathered.Length==armour.Count*3&&ArmourCells.Keep(hv,gathered,plan,pc.Broken,null).Count==armour.Count,"armour filter: the filtered index list holds no broken-cell triangle any more");
    Check(ArmourCells.Keep(hv,ht,plan,new HashSet<int>(),null).Count==allCells.Length,"armour filter: nothing broken, nothing dropped");
    // render and armour use the same rule: the kept render triangles and the kept armour triangles are the same set
    var renderKeep=CellMask.KeepUnbroken(allCells,pc.IsBroken);
    Check(renderKeep.SequenceEqual(armour),"render and armour: both drop exactly the same triangles");
    // a triangle that sits at a ragged edge is in the same cell for both
    Check(plan.CellOfPoint(new V3(0.12f,0,-1.0f))==plan.CellOfPoint(new V3(0.12f,0,-1.0f)),"cells: CellOfPoint is stable");
    // box cull (path b: the triangles of a part inside a merged mesh)
    var mv=new[]{new V3(0,0,0),new V3(0.1f,0,0),new V3(0,0.1f,0),new V3(5,5,5),new V3(5.1f,5,5),new V3(5,5.1f,5)};var mtt=new[]{0,1,2,3,4,5};
    var inside=BoxCull.Inside(mv,mtt,p=>new V3(p.X+1,p.Y,p.Z),new V3(0.9f,-0.1f,-0.1f),new V3(1.3f,0.3f,0.1f),0.005f);
    Check(inside.SequenceEqual(new[]{0}),"box cull: only the triangle whose centroid is inside the part box goes");
    Check(BoxCull.Inside(mv,mtt,p=>p,new V3(-9,-9,-9),new V3(9,9,9),0).Count==2&&BoxCull.Inside(mv,new[]{0,1,99},p=>p,new V3(-9,-9,-9),new V3(9,9,9),0).Count==0,"box cull: invalid triangles are skipped");
    // new debris defaults: clamp(3 + 4 x D/strength, 3, 12)
    Check(Ballistics.Speed(3,12,4,0)==3&&Ballistics.Speed(3,12,4,1)==7&&Ballistics.Speed(3,12,4,2.25)==12&&Ballistics.Speed(3,12,4,9)==12&&Ballistics.Speed(3,12,4,-1)==3,"debris speed 0.4.1 = clamp(3 + 4 x D/strength, 3, 12)");
    // noise: bounded and continuous
    double nmax=0,step=0;for(int i=0;i<2000;i++){double u=i*0.013,v=i*0.007;var (du,dv)=CellNoise.Offset(5u,u,v,0.08);nmax=Math.Max(nmax,Math.Max(Math.Abs(du),Math.Abs(dv)));var (du2,dv2)=CellNoise.Offset(5u,u+0.001,v,0.08);step=Math.Max(step,Math.Abs(du2-du));}
    Check(nmax<=0.08+1e-12&&nmax>0.02&&step<0.005,"cell noise: offsets stay within the jitter, reach most of it, and change smoothly ("+nmax.ToString("0.000")+", step "+step.ToString("0.0000")+")");
    // BreakPipeline: the cell order
    Check(BreakPipeline.CellOrder.SequenceEqual(new[]{BreakStep.HideCellDecals,BreakStep.HideCellMesh,BreakStep.HideAttached,BreakStep.PlayExplosion,BreakStep.RaiseCells,BreakStep.Log}),"break pipeline: cells = decals, mesh, attached parts, explosion, rebuild, log");
}

// ---- 0.5.0: local adaptive subdivision, smart normals, rich dent profiles, tear curl, debris warp, TriAccel, InPlaceArmour, Coalescer, AttachedFilter ----
{
    (V3[],int[]) MakeGrid(float step,float yy)
    {
        var vs=new List<V3>();var ts=new List<int>();
        for(float x=-0.4f;x<0.6f-step/2;x+=step)for(float z=-1.5f;z<0.0f-step/2;z+=step)
        {
            int b0=vs.Count;vs.Add(new V3(x,yy,z));vs.Add(new V3(x+step,yy,z));vs.Add(new V3(x+step,yy,z+step));vs.Add(new V3(x,yy,z+step));
            ts.AddRange(new[]{b0,b0+2,b0+1,b0,b0+3,b0+2});
        }
        return (vs.ToArray(),ts.ToArray());
    }
    // 1. Coarse 2-triangle 1m x 1m plate gets subdivided locally around impact so small kinetic dent hits many vertices (fixes verts=0!)
    var cv0=new[]{new V3(0,0,0),new V3(1,0,0),new V3(1,0,1),new V3(0,0,1)};
    var cn=new[]{new V3(0,1,0),new V3(0,1,0),new V3(0,1,0),new V3(0,1,0)};
    var ct0=new[]{new[]{0,2,1,0,3,2}};
    var ccells=new[]{new[]{10,20}};
    var csides=new[]{new[]{(byte)PlateSide.Original,(byte)PlateSide.Original}};
    var csm=new SubdivMesh(cv0,cn,null,null,null,null,ct0,null,ccells,csides,0.15);
    int added=LocalSubdiv.Refine(csm,new V3(0.5f,0,0.5f),0.20,new V3(0,1,0),0.25,0.065,4,16000,PlateSide.Whole,null);
    Check(added>15&&csm.Basis.Count>19&&csm.FlatTris.Length/3==csm.FlatTriCell!.Length&&csm.FlatTris.Length/3==csm.FlatTriSide!.Length,"subdiv: coarse 2-tri plate subdivides locally and preserves TriCell/TriSide alignment (added="+added+")");
    var richMoved=new List<int>();
    var kev=new DentEvent(new V3(0.5f,0,0.5f),new V3(0,-1,0),0.03,0.20,PlateSide.Whole,DentStyle.Kinetic,new V3(0,-1,0),0.01,0.28,0,123u);
    int kn=DentField.ApplyRich(kev,csm,richMoved);
    Check(kn>8&&richMoved.Count==kn,"subdiv+dent: coarse plate receives kinetic crater on >8 vertices instead of 0 (moved="+kn+")");

    // 2. Crack-free split-vertex seam across two quads with distinct vertex indices & distinct UV islands
    var sv0=new[]{
        new V3(0,0,0),new V3(0.5f,0,0),new V3(0.5f,0,1),new V3(0,0,1),       // Quad A (0..3)
        new V3(0.5f,0,0),new V3(1,0,0),new V3(1,0,1),new V3(0.5f,0,1)        // Quad B (4..7)
    };
    var sn0=Enumerable.Repeat(new V3(0,1,0),8).ToArray();
    var suv0=new[]{
        new V2(0,0),new V2(1,0),new V2(1,1),new V2(0,1),
        new V2(2,0),new V2(3,0),new V2(3,1),new V2(2,1)
    };
    var st0=new[]{new[]{0,2,1,0,3,2, 4,6,5,4,7,6}};
    var ssm=new SubdivMesh(sv0,sn0,null,suv0,null,null,st0,null,null,null,0.15);
    int sadd=LocalSubdiv.Refine(ssm,new V3(0.5f,0,0.5f),0.25,new V3(0,1,0),0.25,0.12,3,16000,PlateSide.Whole,null);
    Check(sadd>0,"subdiv seam: split-vertex quads refine around shared seam");
    var seamA=ssm.Basis.Select((p,idx)=>(p,idx)).Where(x=>Math.Abs(x.p.X-0.5f)<1e-4f&&ssm.UV[x.idx].X<=1.01f).Select(x=>SpatialPosKey.Quantize(x.p)).OrderBy(k=>k.Z).ToList();
    var seamB=ssm.Basis.Select((p,idx)=>(p,idx)).Where(x=>Math.Abs(x.p.X-0.5f)<1e-4f&&ssm.UV[x.idx].X>=1.99f).Select(x=>SpatialPosKey.Quantize(x.p)).OrderBy(k=>k.Z).ToList();
    Check(seamA.Count>2&&seamA.SequenceEqual(seamB),"subdiv seam: both sides of split UV seam match 1:1 with zero T-junctions or UV bleed");

    // 3. Tiny rivet triangles & maxVertices cap
    var rv=new[]{new V3(0,0,0),new V3(0.02f,0,0),new V3(0,0,0.02f)};
    var rsm=new SubdivMesh(rv,new[]{new V3(0,1,0),new V3(0,1,0),new V3(0,1,0)},null,null,null,null,new[]{new[]{0,1,2}},null,null,null,0.1);
    Check(LocalSubdiv.Refine(rsm,new V3(0,0,0),0.2,new V3(0,1,0),0.25,0.065,4,1000,PlateSide.Whole,null)==0,"subdiv: tiny rivet triangle below targetEdgeM is skipped");
    var capSm=new SubdivMesh(cv0,cn,null,null,null,null,ct0,null,null,null,0.15);
    LocalSubdiv.Refine(capSm,new V3(0.5f,0,0.5f),0.6,new V3(0,1,0),0.25,0.05,4,20,PlateSide.Whole,null);
    Check(capSm.Basis.Count<100,"subdiv: maxVertices cap bounds subdivision");

    // 4. SmartNormals: smooth across bent split-vertex seam (<50 deg), sharp on 90 deg corner & 3mm thin front/back shells
    var bv=new[]{
        // Front face left quad (tilted slightly) & right quad (bent by ~14 deg), separate vertex indices at x=0!
        new V3(-1,0,0),new V3(0,0.12f,0),new V3(0,0.12f,1),new V3(-1,0,1),
        new V3(0,0.12f,0),new V3(1,0,0),new V3(1,0,1),new V3(0,0.12f,1),
        // Back face 3mm below (indices 8..11)
        new V3(-1,-0.003f,0),new V3(0,0.117f,0),new V3(0,0.117f,1),new V3(-1,-0.003f,1),
        // 90 deg side wall at x=-1 going down (indices 12..15)
        new V3(-1,0,0),new V3(-1,0,1),new V3(-1,-1,1),new V3(-1,-1,0)
    };
    var bn=Enumerable.Repeat(new V3(0,1,0),16).ToArray();
    var btan=Enumerable.Repeat(new V4(1,0,0,1),16).ToArray();
    var bt=new[]{new[]{
        0,2,1, 0,3,2,
        4,7,5, 5,7,6,
        8,9,10, 8,10,11,
        12,14,13, 12,15,14
    }};
    var nsm=new SubdivMesh(bv,bn,btan,null,null,null,bt,null,null,null,0.1);
    SmartNormals.Compute(nsm,null,50.0,null);
    // Seam vertices 1 and 4 sit at (0,0.12,0) with ~14 deg bend: normals must match and point straight +Y
    Check(Math.Abs(nsm.Normals[1].X-nsm.Normals[4].X)<1e-4&&Math.Abs(nsm.Normals[1].Y-1.0)<1e-4,"smart normals: bent split-vertex seam smooths smoothly across UV split");
    // 3mm back face vertex 9 must still point -Y (not blended with front face!)
    Check(nsm.Normals[9].Y<-0.9,"smart normals: 3mm back face keeps opposite normal without bleeding from front face");
    // 90 deg corner: vertex 0 (top) keeps +Y-ish normal, vertex 12 (side wall) keeps -X normal
    Check(nsm.Normals[0].Y>0.9&&nsm.Normals[12].X<-0.95,"smart normals: 90 deg corner stays crisp");
    // Tangents orthogonal & preserve W
    bool tanOk=true;for(int i=0;i<nsm.Normals.Count;i++){var t3=new V3(nsm.Tangents[i].X,nsm.Tangents[i].Y,nsm.Tangents[i].Z);if(Math.Abs(VM.Dot(t3,nsm.Normals[i]))>1e-4||nsm.Tangents[i].W!=1f)tanOk=false;}
    Check(tanOk,"smart normals: tangents re-orthogonalized against new normals and keep W");
    // Incremental update matches full recompute on dirty region
    var nsm2=new SubdivMesh(bv,bn,btan,null,null,null,bt,null,null,null,0.1);
    SmartNormals.Compute(nsm2,new[]{1},50.0,null);
    Check(Math.Abs(nsm2.Normals[1].Y-nsm.Normals[1].Y)<1e-5&&Math.Abs(nsm2.Normals[4].Y-nsm.Normals[4].Y)<1e-5,"smart normals: incremental dirty update matches full pass on seam group");

    // 5. Kinetic crater profile, oblique elongation, back-face attenuation, blast buckling, tear curl, debris warp
    Check(DentMath.CraterProfile(0,0.28)==1.0&&DentMath.CraterProfile(0.72,0.28)<0&&DentMath.CraterProfile(1.0,0.28)==0,"dent math: kinetic crater has deep inward core (1.0), outward raised lip (<0), 0 at rim");
    Check(DentMath.KineticDepthMm(30,24,20)>DentMath.KineticDepthMm(30,6,20)&&DentMath.KineticDepthMm(30,6,80)<DentMath.KineticDepthMm(30,6,10),"dent math: kinetic depth scales up with energy and down with thickness");
    double bfThin=DentMath.BackFaceAttenuation(0.012,0.010,out bool thinLip);
    double bfThick=DentMath.BackFaceAttenuation(0.080,0.080,out bool thickLip);
    Check(bfThin==1.0&&thinLip&&bfThick<0.6&&!thickLip,"dent math: thin plate bends uniformly with lip, thick back face attenuates without lip");

    // Oblique kinetic hit test on dense flat grid
    var (gv,gt)=MakeGrid(0.04f,0);
    var gsm=new SubdivMesh(gv,Enumerable.Repeat(new V3(0,1,0),gv.Length).ToArray(),null,null,null,null,new[]{gt},null,null,null,0.2);
    var obliqueDir=VM.Norm(new V3(0.707f,-0.707f,0),new V3(0,-1,0));
    var okEv=new DentEvent(new V3(0.1f,0,-0.8f),new V3(0,-1,0),0.04,0.18,PlateSide.Whole,DentStyle.Kinetic,obliqueDir,0.01,0.3,0,77u);
    DentField.ApplyRich(okEv,gsm);
    int coreIdx=-1;double bestD=double.MaxValue;
    for(int i=0;i<gsm.Basis.Count;i++){var q=gsm.Basis[i];double d=Math.Abs(q.X-0.1)+Math.Abs(q.Z+0.8);if(d<bestD){bestD=d;coreIdx=i;}}
    bool hasLip=gsm.Disp.Any(d=>d.Y>0.001f);
    bool hasDeep=coreIdx>=0&&gsm.Disp[coreIdx].Y<-0.02f;
    bool hasDownstreamElongation=gsm.Basis.Where((q,i)=>Math.Abs(q.Z+0.8)<0.05&&q.X-0.1>0.19&&q.X-0.1<0.28&&Math.Abs(gsm.Disp[i].Y)>1e-5).Any();
    Check(hasDeep&&hasLip&&hasDownstreamElongation,"dent rich kinetic: deep inward core, raised outward lip, and oblique downstream furrow beyond R");

    // Blast buckling & radial pull
    var bsm=new SubdivMesh(gv,Enumerable.Repeat(new V3(0,1,0),gv.Length).ToArray(),null,null,null,null,new[]{gt},null,null,null,0.2);
    var bev=new DentEvent(new V3(0.1f,0,-0.8f),new V3(0,-1,0),0.08,0.35,PlateSide.Whole,DentStyle.Blast,new V3(0,-1,0),0.004,0,0.45,555u);
    DentField.ApplyRich(bev,bsm);
    bool hasRadial=bsm.Disp.Any(d=>Math.Abs(d.X)>1e-4f||Math.Abs(d.Z)>1e-4f);
    Check(bsm.MaxDisplacement()>0.03&&hasRadial,"dent rich blast: deep buckled displacement with in-plane radial tension pull");

    // TearCurl + Boundary refinement (including 1.0 m long edge whose midpoints sit > 0.22 m from corners)
    var tv=new[]{new V3(0,0,0),new V3(0.5f,0,0),new V3(0.5f,0,1.0f),new V3(0,0,1.0f), new V3(0.5f,0,0),new V3(1.0f,0,0),new V3(1.0f,0,1.0f),new V3(0.5f,0,1.0f)};
    var tn=Enumerable.Repeat(new V3(0,1,0),8).ToArray();
    var ttris=new[]{new[]{0,2,1,0,3,2, 4,6,5,4,7,6}};
    var tcells=new[]{new[]{1,1, 2,2}}; // cell 1 broken, cell 2 survives
    var tsm=new SubdivMesh(tv,tn,null,null,null,null,ttris,null,tcells,null,0.2);
    var brk=new HashSet<int>{1};
    int bAdded=LocalSubdiv.RefineBoundary(tsm,brk,c=>brk.Contains(c),0.22,0.08,3,5000);
    tsm.Alive=new List<bool>(CellMask.AliveVertices(tsm.FlatBasis.Length,tsm.FlatTris,tsm.FlatTriCell!,c=>brk.Contains(c)));
    int curled=TearCurl.Apply(tsm,brk,c=>brk.Contains(c),new V3(0.25f,0.1f,0.5f),0.09,0.22,0.005,0.2,99u);
    Check(bAdded>0&&curled>0,"tear curl: refines and curls surviving border next to broken cell (added="+bAdded+", curled="+curled+")");
    bool midCurled=false;for(int i=0;i<tsm.Basis.Count;i++)if(Math.Abs(tsm.Basis[i].X-0.5f)<1e-4&&tsm.Basis[i].Z>0.3f&&tsm.Basis[i].Z<0.7f&&VM.Len(tsm.Disp[i])>0.02){midCurled=true;break;}
    Check(midCurled,"tear curl: midpoints along 1m border edge curl smoothly via segment distance");
    bool farStill=true;for(int i=0;i<tsm.Basis.Count;i++)if(tsm.Basis[i].X>0.95f&&VM.Len(tsm.Disp[i])>1e-6)farStill=false;
    Check(farStill,"tear curl: far edge beyond TearCurlWidthM remains uncurled");

    // Thick plate back-face attenuation + side isolation in LocalSubdiv + ComputeSimple
    var thV=new[]{new V3(0,0,0),new V3(1,0,0),new V3(1,0,1),new V3(0,0,1), new V3(0,-0.08f,0),new V3(1,-0.08f,0),new V3(1,-0.08f,1),new V3(0,-0.08f,1)};
    var thN=new[]{new V3(0,1,0),new V3(0,1,0),new V3(0,1,0),new V3(0,1,0), new V3(0,-1,0),new V3(0,-1,0),new V3(0,-1,0),new V3(0,-1,0)};
    var thT=new[]{new[]{0,2,1,0,3,2, 4,5,6,4,6,7}};
    var thSm=new SubdivMesh(thV,thN,null,null,null,null,thT,new byte[]{1,1,1,1,2,2,2,2},null,new[]{new byte[]{1,1,2,2}},0.2);
    int sideAdded=LocalSubdiv.Refine(thSm,new V3(0.5f,0,0.5f),0.25,new V3(0,-1,0),0.25,0.1,2,1000,PlateSide.Original,null);
    bool mirrorUntouched=true;for(int ti=0;ti<thSm.SubTriSide![0].Count;ti++)if(thSm.SubTriSide[0][ti]==(byte)PlateSide.Mirror){int a=thSm.SubTris[0][ti*3];if(a>=8)mirrorUntouched=false;}
    Check(sideAdded>0&&mirrorUntouched,"subdiv side isolation: Original-only refinement never splits Mirror triangles");
    var thSm2=new SubdivMesh(thV,thN,null,null,null,null,thT,null,null,null,0.2);
    LocalSubdiv.Refine(thSm2,new V3(0.5f,0,0.5f),0.25,new V3(0,-1,0),0.25,0.08,3,5000,PlateSide.Whole,null);
    DentField.ApplyRich(new DentEvent(new V3(0.5f,0,0.5f),new V3(0,-1,0),0.04,0.2,PlateSide.Whole,DentStyle.Kinetic,new V3(0,-1,0),0.08,0.3,0,5u),thSm2);
    double frontMax=0,backMax=0;for(int i=0;i<thSm2.Basis.Count;i++){if(thSm2.Basis[i].Y>-0.01f)frontMax=Math.Max(frontMax,-thSm2.Disp[i].Y);else backMax=Math.Max(backMax,-thSm2.Disp[i].Y);}
    Check(frontMax>0.02&&backMax>0&&backMax<frontMax*0.7,"dent rich thick plate: back face bulges gently at <70% of front crater depth");

    // DebrisWarp + ComputeSimple
    var dv0=new[]{new V3(0,0,0),new V3(0.5f,0,0),new V3(0.5f,0,0.5f),new V3(0,0,0.5f)};
    int warped=DebrisWarp.Apply(dv0,new V3(0.25f,0.2f,0.25f),0.07,42u);
    var snSimple=SmartNormals.ComputeSimple(dv0,new[]{0,2,1,0,3,2},50.0);
    Check(warped==4&&dv0.Any(p=>Math.Abs(p.Y)>0.01f)&&snSimple.Length==4&&snSimple.All(n=>Math.Abs(VM.Len(n)-1.0)<1e-4),"debris warp + ComputeSimple: bends vertices and outputs unit normals");

    // 6. TriAccel matches MeshQuery
    var accel=new TriAccel();
    bool ar=accel.RayFirst(gv,gt,1,new V3(0.1f,2,-0.8f),new V3(0,-1,0),5,null,out double ad,out V3 an,out int atri);
    bool mr=MeshQuery.RayFirst(gv,gt,new V3(0.1f,2,-0.8f),new V3(0,-1,0),5,null,out double md,out V3 mn,out int mtri);
    Check(ar==mr&&Math.Abs(ad-md)<1e-6&&atri==mtri,"TriAccel.RayFirst: matches MeshQuery exactly");
    bool ac=accel.Closest(gv,gt,1,new V3(0.2f,1.5f,-0.5f),null,out V3 ap,out _);
    bool mc=MeshQuery.Closest(gv,gt,new V3(0.2f,1.5f,-0.5f),null,out V3 mp2,out _);
    Check(ac==mc&&VM.Len(VM.Sub(ap,mp2))<1e-6,"TriAccel.Closest: matches MeshQuery exactly");

    // 7. InPlaceArmour, RebuildCoalescer, AttachedFilter, PartKeyRule
    var armTris=new[]{0,1,2, 3,4,5};
    Check(InPlaceArmour.TryDegenerate(armTris,3,0,out var bk)&&InPlaceArmour.IsDegenerate(armTris[3],armTris[4],armTris[5]),"InPlaceArmour: degenerates triangle 1 -> (3,3,3)");
    Check(!InPlaceArmour.TryDegenerate(armTris,3,0,out _),"InPlaceArmour: idempotent on already degenerated triangle");
    Check(InPlaceArmour.Restore(armTris,new[]{bk},0)==1&&armTris[4]==4&&armTris[5]==5,"InPlaceArmour: restores original indices cleanly");

    var coal=new RebuildCoalescer();
    coal.Request(100,1.00,0.20);coal.Request(100,1.05,0.20);coal.Request(100,1.15,0.20);
    Check(coal.TakeReady(1.19,null).Count==0&&coal.TakeReady(1.21,new HashSet<long>{100}).Count==0,"coalescer: keeps earliest deadline and respects busyRoots");
    Check(coal.TakeReady(1.21,null).SequenceEqual(new[]{100L})&&coal.Count==0,"coalescer: releases batched root once window expires");

    Check(AttachedFilter.IsCoreStructuralPart("TurretRing")&&AttachedFilter.IsCoreStructuralPart("WheelMount")&&AttachedFilter.IsCoreStructuralPart("TrackAssembly")&&AttachedFilter.IsCoreStructuralPart("PlateStructure")&&!AttachedFilter.IsCoreStructuralPart("VehicleObjectModel"),"AttachedFilter: excludes core chassis/turret/wheel and independent child PlateStructures while keeping attached model parts");
    Check(PartKeyRule.Make(691,0,10,"LOD0")!=PartKeyRule.Make(692,0,10,"LOD0"),"PartKeyRule: distinct attached ERA vuids under the same parent never collide");
}

// ---- 0.6.0: Photo Mode session stability, Ricochet/Embedded/ExitBlowout profiles, calibrated kinetic scaling, graded RefineGraded ----
{
    // 1. SessionTracker Photo Mode suite
    var sess=new SessionTracker();
    Check(sess.Update(true,false,false,false,100,out bool s1,out bool p1)==null&&s1&&!p1,"session: Play activates session, photomode=false");
    Check(sess.Update(false,false,false,true,100,out bool s2,out bool p2)==null&&s2&&p2,"session: entering Photo Mode keeps session alive without reset");
    Check(sess.Update(false,false,false,false,100,out bool s3,out bool p3)==null&&s3&&p3,"session: async UI transition gap without ControlUI keeps session alive");
    Check(sess.Update(true,false,false,false,100,out bool s4,out bool p4)==null&&s4&&!p4,"session: returning from Photo Mode to Play clears photomode without reset");
    Check(sess.Update(false,true,false,false,100,out bool s5,out bool p5)=="edit"&&!s5,"session: entering Designer triggers 'edit' reset");
    sess=new SessionTracker();
    sess.Update(true,false,false,false,100,out _,out _);
    Check(sess.Update(false,false,true,false,100,out bool s6,out _)=="menu"&&!s6,"session: returning to MainMenu triggers 'menu' reset");
    sess=new SessionTracker();
    sess.Update(true,false,false,false,100,out _,out _);
    Check(sess.Update(true,false,false,false,200,out bool s7,out _)=="new battle"&&s7,"session: active scene handle change triggers 'new battle' reset");
    sess=new SessionTracker();
    Check(sess.Update(false,true,false,true,100,out bool s8,out bool p8)==null&&!s8&&!p8,"session: Photo Mode inside Designer never activates a combat session");

    // 2. Calibrated kinetic radius & depth scaling
    double rBig=DentMath.KineticRadiusM(0.15,120,12,DentStyle.Ricochet);
    double rSmall=DentMath.KineticRadiusM(0.15,25,1.5,DentStyle.Kinetic);
    Check(rBig>rSmall&&rBig<=0.40&&rSmall>=0.06,"dent math: calibre/energy/style scales kinetic radius cleanly within [0.06, 0.40]");
    double dEmb=DentMath.KineticDepthMm(30,8,50,DentStyle.Embedded,1.45);
    double dKin=DentMath.KineticDepthMm(30,8,50,DentStyle.Kinetic,1.0);
    double dRic=DentMath.KineticDepthMm(30,8,50,DentStyle.Ricochet,1.45);
    double dExit=DentMath.KineticDepthMm(30,8,50,DentStyle.ExitBlowout,1.45);
    Check(dEmb>dExit&&dExit>dRic&&dEmb>dKin,"dent math: Embedded > ExitBlowout > Ricochet and HullTurretDentScale boosts thick plate visibility");

    // 3. LocalSubdiv.RefineGraded: finer inside core (<0.88R) than outer band
    var cv2=new[]{new V3(-1,0,-1),new V3(1,0,-1),new V3(1,0,1),new V3(-1,0,1)};
    var cn2=Enumerable.Repeat(new V3(0,1,0),4).ToArray();
    var ct2=new[]{new[]{0,2,1,0,3,2}};
    var gsm2=new SubdivMesh(cv2,cn2,null,null,null,null,ct2,null,null,null,0.2);
    int gadd=LocalSubdiv.RefineGraded(gsm2,new V3(0,0,0),0.35,new V3(0,1,0),0.25,0.14,0.032,6,20000,PlateSide.Whole,null);
    double minCoreEdge=double.MaxValue;int coreTris=0;
    for(int ti=0;ti<gsm2.FlatTris.Length/3;ti++)
    {
        int ia=gsm2.FlatTris[ti*3],ib=gsm2.FlatTris[ti*3+1],ic=gsm2.FlatTris[ti*3+2];
        V3 va=gsm2.Basis[ia],vb=gsm2.Basis[ib],vc=gsm2.Basis[ic];
        var cen=new V3((va.X+vb.X+vc.X)/3f,(va.Y+vb.Y+vc.Y)/3f,(va.Z+vb.Z+vc.Z)/3f);
        if(VM.Len(cen)<0.22){coreTris++;minCoreEdge=Math.Min(minCoreEdge,VM.Len(VM.Sub(vb,va)));}
    }
    Check(gadd>60&&coreTris>12&&minCoreEdge<=0.045,"RefineGraded: refines crater core finely (<=4.5cm edges) on a 2m coarse plate (added="+gadd+")");

    // 4. Ricochet gouge + stretched capsule subdivision on split-seam coarse quads (crack-free)
    var rqv=new[]{
        new V3(-1,0,-1),new V3(0,0,-1),new V3(0,0,1),new V3(-1,0,1),
        new V3(0,0,-1),new V3(1,0,-1),new V3(1,0,1),new V3(0,0,1)
    };
    var rqn=Enumerable.Repeat(new V3(0,1,0),8).ToArray();
    var rquv=new[]{
        new V2(0,0),new V2(1,0),new V2(1,1),new V2(0,1),
        new V2(2,0),new V2(3,0),new V2(3,1),new V2(2,1)
    };
    var rqt=new[]{new[]{0,2,1,0,3,2, 4,7,5,5,7,6}};
    var rqsm=new SubdivMesh(rqv,rqn,null,rquv,null,null,rqt,null,null,null,0.2);
    var grazeDir=VM.Norm(new V3(0.92f,-0.39f,0),new V3(1,-0.3f,0));
    int radd=LocalSubdiv.RefineGraded(rqsm,new V3(0,0,0),0.18,new V3(0,-1,0),0.25,0.065,0.032,6,20000,PlateSide.Whole,null,new V3(1,0,0),0.18*1.65);
    var rEv=new DentEvent(new V3(0,0,0),new V3(0,-1,0),0.045,0.18,PlateSide.Whole,DentStyle.Ricochet,grazeDir,0.08,0.28,0,321u);
    int rmoved=DentField.ApplyRich(rEv,rqsm);
    bool rScoop=false,rExitLip=false,rForwardPush=false,rShoulder=false;
    for(int i=0;i<rqsm.Basis.Count;i++)
    {
        var rpos=rqsm.Basis[i];var rdisp=rqsm.Disp[i];
        if(rpos.X>-0.10&&rpos.X<0.12&&Math.Abs(rpos.Z)<0.05&&rdisp.Y<-0.015f)rScoop=true;
        if(rpos.X>0.18&&rpos.X<0.36&&Math.Abs(rpos.Z)<0.07&&rdisp.Y>0.005f)rExitLip=true;
        if(rpos.X>0.18&&rpos.X<0.36&&rdisp.X>0.0005f)rForwardPush=true;
        if(rpos.X>-0.08&&rpos.X<0.15&&Math.Abs(rpos.Z)>0.08&&Math.Abs(rpos.Z)<0.17&&rdisp.Y>0.001f)rShoulder=true;
    }
    var rSeamL=rqsm.Basis.Select((q,idx)=>(q,idx)).Where(x=>Math.Abs(x.q.X)<1e-4f&&rqsm.UV[x.idx].X<=1.01f).Select(x=>SpatialPosKey.Quantize(x.q)).OrderBy(k=>k.Z).ToList();
    var rSeamR=rqsm.Basis.Select((q,idx)=>(q,idx)).Where(x=>Math.Abs(x.q.X)<1e-4f&&rqsm.UV[x.idx].X>=1.99f).Select(x=>SpatialPosKey.Quantize(x.q)).OrderBy(k=>k.Z).ToList();
    Check(radd>40&&rmoved>20&&rScoop&&rExitLip&&rForwardPush&&rShoulder&&rSeamL.SequenceEqual(rSeamR),"dent Ricochet: scooped groove, plowed exit bow-wave lip, forward push, sheared side shoulders, crack-free seam (added="+radd+", moved="+rmoved+")");
    Check(DentMath.RicochetProfile(0.15,0,0.28)>0.8&&DentMath.RicochetProfile(0.825,0,0.28)<-0.3,"dent math RicochetProfile: inward groove (+u) and outward plowed bow-wave (-u) have expected signs");

    // 5. Embedded crater (deeper core + higher rolled lip + radial push than Kinetic)
    var emV=new List<V3>();var emT=new List<int>();
    float stp=0.04f;
    for(float x=-0.5f;x<0.5f-stp/2;x+=stp)for(float z=-0.5f;z<0.5f-stp/2;z+=stp)
    {
        int b0=emV.Count;emV.Add(new V3(x,0,z));emV.Add(new V3(x+stp,0,z));emV.Add(new V3(x+stp,0,z+stp));emV.Add(new V3(x,0,z+stp));
        emT.Add(b0);emT.Add(b0+2);emT.Add(b0+1);emT.Add(b0);emT.Add(b0+3);emT.Add(b0+2);
    }
    var emN=Enumerable.Repeat(new V3(0,1,0),emV.Count).ToArray();
    var emSm=new SubdivMesh(emV.ToArray(),emN,null,null,null,null,new[]{emT.ToArray()},null,null,null,0.2);
    var emEv=new DentEvent(new V3(0,0,0),new V3(0,-1,0),0.05,0.20,PlateSide.Whole,DentStyle.Embedded,new V3(0,-1,0),0.08,0.28,0,654u);
    int emMoved=DentField.ApplyRich(emEv,emSm);
    bool emDeep=emSm.Disp.Any(d=>d.Y<-0.035f);
    bool emLip=emSm.Disp.Any(d=>d.Y>0.008f&&(Math.Abs(d.X)>0.0005f||Math.Abs(d.Z)>0.0005f));
    Check(emMoved>20&&emDeep&&emLip&&Math.Abs(DentMath.EmbeddedProfile(0.70,0.28))>Math.Abs(DentMath.CraterProfile(0.725,0.28)),"dent Embedded: deeper bowl, bolder outward rolled-steel lip, and radial mushrooming");

    // 6. ExitBlowout profile: outward volcano/petal displacement in +n direction + radial splay + BackFaceAttenuationAbs
    var exV=new List<V3>();var exT=new List<int>();
    for(int i=0;i<emV.Count;i++){var q=emV[i];exV.Add(new V3(q.X,-0.08f,q.Z));} // outer exit face at y=-0.08
    for(int i=0;i<emT.Count;i++)exT.Add(emT[i]);
    int backStart=exV.Count;
    for(int i=0;i<emV.Count;i++){var q=emV[i];exV.Add(new V3(q.X,0.0f,q.Z));}    // interior/entry face 80mm behind at y=0
    for(int i=0;i<emT.Count;i++)exT.Add(emT[i]+backStart);
    var exN=Enumerable.Repeat(new V3(0,-1,0),exV.Count).ToArray();
    var exSm=new SubdivMesh(exV.ToArray(),exN,null,null,null,null,new[]{exT.ToArray()},null,null,null,0.2);
    var exEv=new DentEvent(new V3(0,-0.08f,0),new V3(0,-1,0),0.05,0.18,PlateSide.Whole,DentStyle.ExitBlowout,new V3(0,-1,0),0.08,0.28,0,987u);
    int exMoved=DentField.ApplyRich(exEv,exSm);
    double exitOut=0,entryOut=0;
    for(int i=0;i<backStart;i++)exitOut=Math.Max(exitOut,-exSm.Disp[i].Y);
    for(int i=backStart;i<exSm.Basis.Count;i++)entryOut=Math.Max(entryOut,-exSm.Disp[i].Y);
    bool exitRadial=exSm.Disp.Take(backStart).Any(d=>Math.Abs(d.X)>0.001f||Math.Abs(d.Z)>0.001f);
    Check(exMoved>20&&exitOut>0.03&&exitRadial&&entryOut<exitOut*0.65,"dent ExitBlowout: outward petaled blowout on exit face (-Y), radial petal splay, attenuated back face");
}

// 0.7.0 BlastSnap + HoleMath + HolePunch tests
{
    // 1. BlastSnap.TrySnap (exact Shot 2 coords from user log + rejection bounds)
    var snPos=new V3(8.50f,2.40f,142.18f);
    var snBlast=new V3(8.44f,2.41f,138.36f);
    var snDir=VM.Norm(VM.Sub(snPos,snBlast),new V3(0,0,1));
    var snMarks=new List<SimHitMark>{new(1000,snPos,snDir,137,293L,384)};
    bool snOk=BlastSnap.TrySnap(snMarks,1001,snBlast,136,out var snMatched,out double snShift);
    Check(snOk&&snMatched.Vuid==384&&Math.Abs(snShift-3.8205)<0.05,"BlastSnap: snaps 3.82m upstream HE effect onto recent Shot 2 vuid=384 hit");

    bool snPerpReject=BlastSnap.TrySnap(snMarks,1001,new V3(9.20f,2.41f,138.36f),136,out _,out _);
    bool snFrameOk=BlastSnap.TrySnap(snMarks,1003,snBlast,136,out _,out _);
    bool snFrameReject=BlastSnap.TrySnap(snMarks,1004,snBlast,136,out _,out _);
    bool snCalReject=BlastSnap.TrySnap(snMarks,1001,snBlast,75,out _,out _);
    bool snNearReject=BlastSnap.TrySnap(snMarks,1001,new V3(8.50f,2.40f,142.00f),136,out _,out _);
    Check(!snPerpReject&&snFrameOk&&!snFrameReject&&!snCalReject&&!snNearReject,"BlastSnap: rejects perp>0.60m, frameDiff>3, calibre mismatch, and near-hit <=0.25m while allowing frameDiff==3");

    // 2. HoleMath.HoleRadiusM & RaggedRadius
    double hrZero=HoleMath.HoleRadiusM(0.99,0.68);
    double hrSample=HoleMath.HoleRadiusM(4.78,0.68);
    double rMin=double.MaxValue,rMax=double.MinValue;
    for(int ai=0;ai<64;ai++)
    {
        double ang=ai*(Math.PI*2.0/64.0);
        double rr=HoleMath.RaggedRadius(hrSample,ang,777u);
        if(rr<rMin)rMin=rr;if(rr>rMax)rMax=rr;
    }
    Check(hrZero==0.0&&Math.Abs(hrSample-0.2647)<0.01&&rMin>=hrSample*0.65-1e-6&&rMax<=hrSample*1.38+1e-6&&rMax>rMin*1.15,
        $"HoleMath: zero below strength, ~0.265m at dmgRatio=4.78/blastR=0.68 ({hrSample:F3}m), ragged bounds [{rMin/hrSample:F2}R, {rMax/hrSample:F2}R]");

    // 3. HolePunch end-to-end on a 12-triangle 1.4m x 1.0m x 5mm double-sided box plate
    var bxV=new[]
    {
        new V3(-0.7f,+0.0025f,-0.5f),new V3(+0.7f,+0.0025f,-0.5f),new V3(+0.7f,+0.0025f,+0.5f),new V3(-0.7f,+0.0025f,+0.5f),
        new V3(-0.7f,-0.0025f,-0.5f),new V3(+0.7f,-0.0025f,-0.5f),new V3(+0.7f,-0.0025f,+0.5f),new V3(-0.7f,-0.0025f,+0.5f)
    };
    var bxN=new[]
    {
        new V3(0,+1,0),new V3(0,+1,0),new V3(0,+1,0),new V3(0,+1,0),
        new V3(0,-1,0),new V3(0,-1,0),new V3(0,-1,0),new V3(0,-1,0)
    };
    var bxT=new[]
    {
        new[]
        {
            0,2,1, 0,3,2, // +Y front (y=+0.0025)
            4,5,6, 4,6,7, // -Y back (y=-0.0025)
            0,1,5, 0,5,4, // -Z edge
            1,2,6, 1,6,5, // +X edge
            2,3,7, 2,7,6, // +Z edge
            3,0,4, 3,4,7  // -X edge (12 tris total)
        }
    };
    var bxSm=new SubdivMesh(bxV,bxN,null,null,null,null,bxT,null,null,null,0.2);
    var bxBroken=new HashSet<int>();
    var hole1Pos=new V3(-0.15f,+0.0025f,0f);
    double hole1R=0.18;
    int holeId1=HoleMath.HoleIdBase+1;
    LocalSubdiv.RefineGraded(bxSm,hole1Pos,hole1R*1.5,new V3(0,1,0),hole1R*0.85,0.12,0.032,4,20000,PlateSide.Whole,null);
    int punched1=HolePunch.Punch(bxSm,hole1Pos,new V3(0,1,0),hole1R,PlateSide.Whole,holeId1,bxBroken,101u,out int bxRem1,out int bxTot1);
    bxBroken.Add(holeId1);
    bool punchFront=false,punchBack=false;
    for(int ti=0;ti<bxSm.SubTris[0].Count/3;ti++)
    {
        if(bxSm.SubTriCell![0][ti]!=holeId1)continue;
        float cy=(bxSm.Basis[bxSm.SubTris[0][ti*3]].Y+bxSm.Basis[bxSm.SubTris[0][ti*3+1]].Y+bxSm.Basis[bxSm.SubTris[0][ti*3+2]].Y)/3f;
        if(cy>0.002f)punchFront=true;if(cy<-0.002f)punchBack=true;
    }
    var bxMovedList=new List<int>();
    int bxCurl=TearCurl.Apply(bxSm,new HashSet<int>{holeId1},cid=>bxBroken.Contains(cid),hole1Pos,0.035,0.12,0.005,0.28,101u,bxMovedList);
    SmartNormals.Compute(bxSm,null,35.0,cid=>bxBroken.Contains(cid));
    bool unitNormals=true;
    foreach(var bxNrm in bxSm.Normals){double nl=VM.Len(bxNrm);if(double.IsNaN(nl)||Math.Abs(nl-1.0)>1e-3){unitNormals=false;break;}}
    int hole1CountBefore=bxSm.SubTriCell![0].Count(c=>c==holeId1);

    // Second hole 0.45 m away preserves hole 1 and punches distinct holeId2
    var hole2Pos=new V3(+0.30f,+0.0025f,0f);
    int holeId2=HoleMath.HoleIdBase+2;
    LocalSubdiv.RefineGraded(bxSm,hole2Pos,hole1R*1.5,new V3(0,1,0),hole1R*0.85,0.12,0.032,4,20000,PlateSide.Whole,null);
    int punched2=HolePunch.Punch(bxSm,hole2Pos,new V3(0,1,0),hole1R,PlateSide.Whole,holeId2,bxBroken,202u,out int bxRem2,out int bxTot2);
    bxBroken.Add(holeId2);
    int hole1CountAfter=bxSm.SubTriCell[0].Count(c=>c==holeId1);
    int hole2CountAfter=bxSm.SubTriCell[0].Count(c=>c==holeId2);
    double remFrac1=(double)bxRem1/bxTot1,remFrac2=(double)bxRem2/bxTot2;
    Check(punched1>8&&punchFront&&punchBack&&bxRem1>0&&bxTot1==punched1+bxRem1&&bxCurl>0&&unitNormals&&punched2>8&&hole1CountAfter>=hole1CountBefore&&hole2CountAfter==punched2&&remFrac2<remFrac1&&bxTot2==bxRem2+hole1CountAfter+hole2CountAfter,
        $"HolePunch: 12-tri 5mm box punches both faces (p1={punched1}, p2={punched2}, remFrac {remFrac1:F2}->{remFrac2:F2}, curl={bxCurl}, h1After={hole1CountAfter}>={hole1CountBefore})");
}

// 0.7.1 Blast direction fix on exit-face snap + SurfaceSnap for floating impact flames
{
    // 1. 1.0m x 1.0m x 20mm box plate (front z=0 facing shooter at -Z, exit/back face z=+0.020)
    var p20V=new[]
    {
        new V3(-0.5f,-0.5f,0f),new V3(+0.5f,-0.5f,0f),new V3(+0.5f,+0.5f,0f),new V3(-0.5f,+0.5f,0f),
        new V3(-0.5f,-0.5f,0.02f),new V3(+0.5f,-0.5f,0.02f),new V3(+0.5f,+0.5f,0.02f),new V3(-0.5f,+0.5f,0.02f)
    };
    var p20N=new[]
    {
        new V3(0,0,-1),new V3(0,0,-1),new V3(0,0,-1),new V3(0,0,-1),
        new V3(0,0,+1),new V3(0,0,+1),new V3(0,0,+1),new V3(0,0,+1)
    };
    var p20T=new[]
    {
        new[]
        {
            0,1,2, 0,2,3, // -Z front face
            4,6,5, 4,7,6, // +Z exit/back face
            0,4,5, 0,5,1, // -Y
            1,5,6, 1,6,2, // +X
            2,6,7, 2,7,3, // +Y
            3,7,4, 3,4,0  // -X
        }
    };
    var p20Sm=new SubdivMesh(p20V,p20N,null,null,null,null,p20T,null,null,null,0.2);
    var p20Acc=new TriAccel();
    var snapExitHit=new V3(0f,0f,0.020f);
    var shotDirZ=new V3(0f,0f,+1f);
    var rayOrigZ=VM.Sub(snapExitHit,VM.Mul(shotDirZ,0.20));
    bool rayOk=p20Acc.RayFirst(p20Sm.FlatBasis,p20Sm.FlatTris,p20Sm.TopologyVersion,rayOrigZ,shotDirZ,0.65,null,out double rayDist,out V3 rayNorm,out _);
    var rayCenter=VM.Add(rayOrigZ,VM.Mul(shotDirZ,rayDist));
    var orientedNorm=DentMath.Orient(rayNorm,shotDirZ);
    int subAdded=LocalSubdiv.RefineGraded(p20Sm,rayCenter,0.32,orientedNorm,DentMath.NormalReach(0.32),0.065,0.032,5,20000,PlateSide.Whole,null);
    p20Acc.Invalidate();
    var blEv1=new DentEvent(rayCenter,orientedNorm,0.08,0.25,PlateSide.Whole,DentStyle.Blast,shotDirZ,0.020,0.28,0.45,71u);
    int blMoved1=DentField.ApplyRich(blEv1,p20Sm);
    double frontCoreZ=0,backCoreZ=0,minAnyZ=double.MaxValue;
    int coreFrontN=0,coreBackN=0;
    for(int i=0;i<p20Sm.Basis.Count;i++)
    {
        var bp=p20Sm.Basis[i];var dp=p20Sm.Disp[i];
        minAnyZ=Math.Min(minAnyZ,dp.Z);
        if(bp.X*bp.X+bp.Y*bp.Y<0.12f*0.12f)
        {
            if(Math.Abs(bp.Z)<0.002f){coreFrontN++;frontCoreZ+=dp.Z;}
            else if(Math.Abs(bp.Z-0.02f)<0.002f){coreBackN++;backCoreZ+=dp.Z;}
        }
    }
    double avgFrontZ=coreFrontN>0?frontCoreZ/coreFrontN:0;
    double avgBackZ=coreBackN>0?backCoreZ/coreBackN:0;
    Check(rayOk&&Math.Abs(rayCenter.Z)<1e-4&&orientedNorm.Z>0.99&&subAdded>20&&blMoved1>20&&avgFrontZ>0.035&&avgBackZ>0.025&&minAnyZ>-0.035,
        $"0.7.1 BlastDir: exit-hit ray hits front z={rayCenter.Z:F3}, normZ={orientedNorm.Z:F2}, frontCore={avgFrontZ:F3}m, backCore={avgBackZ:F3}m, minZ={minAnyZ:F3}m");

    // 2. SurfaceSnap.DeformedPoint (floating impact flame snap)
    var qMark=new V3(0f,0f,-0.002f);
    bool snOk1=SurfaceSnap.DeformedPoint(p20Sm,p20Acc,qMark,0.12,null,out var snPt1,out var snDp1);
    Check(snOk1&&snDp1.Z>0.05&&Math.Abs((snPt1.Z-snDp1.Z)-(-0.002))<1e-4,
        $"SurfaceSnap: snaps pre-dent flame mark onto deformed mesh (dispZ={snDp1.Z:F4}m, snappedZ={snPt1.Z:F4}m)");

    var blEv2=new DentEvent(rayCenter,orientedNorm,0.05,0.25,PlateSide.Whole,DentStyle.Blast,shotDirZ,0.020,0.28,0.45,72u);
    DentField.ApplyRich(blEv2,p20Sm);
    bool snOk2=SurfaceSnap.DeformedPoint(p20Sm,p20Acc,qMark,0.12,null,out var snPt2,out var snDp2);
    bool snFar=SurfaceSnap.DeformedPoint(p20Sm,p20Acc,new V3(0f,0f,-0.50f),0.12,null,out _,out _);
    var flatSm=new SubdivMesh(p20V,p20N,null,null,null,null,p20T,null,null,null,0.2);
    var flatAcc=new TriAccel();
    bool snFlat=SurfaceSnap.DeformedPoint(flatSm,flatAcc,qMark,0.12,null,out _,out _);
    Check(snOk2&&snPt2.Z>snPt1.Z+0.01&&Math.Abs((snPt2.Z-snDp2.Z)-(-0.002))<1e-4&&!snFar&&!snFlat,
        $"SurfaceSnap: cumulative re-evaluation stays exact ({snPt1.Z:F4}->{snPt2.Z:F4}m), far={snFar}, undented={snFlat}");
}

// 0.8.0 Per-cell local thickness + RimCapBuilder solid torn cross-section walls
{
    // 1. Per-cell local thickness: 5mm fender vs 80mm glacis
    int cFend=CellPlan.Encode(PlateSide.Whole,0),cGlacis=CellPlan.Encode(PlateSide.Whole,1);
    var pcPlan=new CellPlan(new[]{new CellFrame(PlateSide.Whole,new V3(-0.2f,-0.08f,-0.2f),new V3(0.2f,0.005f,0.2f),0.5f),null,null},null,1u,0.0);
    var pcInfo=new Dictionary<int,CellInfo>
    {
        [cFend]=new CellInfo{Min=new V3(-0.2f,0f,-0.2f),Max=new V3(0.2f,0.005f,0.2f),Tris=2,ThicknessMm=5,Strength=100},
        [cGlacis]=new CellInfo{Min=new V3(-0.2f,-0.08f,-0.2f),Max=new V3(0.2f,0f,0.2f),Tris=2,ThicknessMm=80,Strength=1600}
    };
    var pc80=new PlateCells(pcPlan,pcInfo,423.0);
    bool strOk=pc80.MinStrength==100&&pc80.MaxStrength==1600&&pc80.StrengthOf(cFend)==100&&pc80.StrengthOf(cGlacis)==1600;
    var pcB1=pc80.Blast(new V3(0,0,0),136,190,0.5,20,true);
    var pcB2=pc80.Blast(new V3(0,0,0),136,190,0.5,20,true);
    var pcB3=pc80.Blast(new V3(0,0,0),136,190,0.5,20,true);
    var pcB4=pc80.Blast(new V3(0,0,0),136,190,0.5,20,true);
    bool haveB1q=pcB1.DentQ.TryGetValue(PlateSide.Whole,out double pcB1q);
    Check(strOk&&pcB1.NewBroken.Count==1&&pcB1.NewBroken[0]==cFend&&haveB1q&&pcB1q>0&&pcB2.NewBroken.Count==0&&pcB3.NewBroken.Count==0&&pcB4.NewBroken.Count==1&&pcB4.NewBroken[0]==cGlacis,
        $"0.8.0 PerCellThickness: 152mm HE tears 5mm cell on shot 1 (qGlacis={pcB1q:F2}) and needs 4 direct hits for 80mm glacis");

    // 2. RimCapBuilder with ThickVec (80mm hull plate)
    var r80V=new[]
    {
        new V3(-0.5f,0f,-0.5f),new V3(0.5f,0f,-0.5f),new V3(0.5f,0f,0.5f),new V3(-0.5f,0f,0.5f),
        new V3(-0.5f,-0.08f,-0.5f),new V3(0.5f,-0.08f,-0.5f),new V3(0.5f,-0.08f,0.5f),new V3(-0.5f,-0.08f,0.5f)
    };
    var r80N=new[]
    {
        new V3(0,1,0),new V3(0,1,0),new V3(0,1,0),new V3(0,1,0),
        new V3(0,-1,0),new V3(0,-1,0),new V3(0,-1,0),new V3(0,-1,0)
    };
    var r80Tv=new[]
    {
        new V3(0,-0.08f,0),new V3(0,-0.08f,0),new V3(0,-0.08f,0),new V3(0,-0.08f,0),
        default,default,default,default
    };
    var r80T=new[]
    {
        new[]
        {
            0,2,1, 0,3,2,
            4,5,6, 4,6,7,
            0,1,5, 0,5,4,
            1,2,6, 1,6,5,
            2,3,7, 2,7,6,
            3,0,4, 3,4,7
        }
    };
    static bool InspectRimQuads(RimCapData? rim,float midY,bool checkNoDups,out double spanY,out double meanDot,out double minDot)
    {
        spanY=0;meanDot=0;minDot=double.MaxValue;
        if(rim==null||rim.Tris.Count<12||rim.Verts.Count!=(rim.Tris.Count/6)*4||rim.Normals.Count!=rim.Verts.Count)return false;
        double minY=double.MaxValue,maxY=double.MinValue,dotSum=0;
        int nQuads=rim.Tris.Count/6;
        var cens=checkNoDups?new List<V3>(nQuads):null;
        for(int i=0;i<rim.Verts.Count;i++){minY=Math.Min(minY,rim.Verts[i].Y);maxY=Math.Max(maxY,rim.Verts[i].Y);}
        spanY=maxY-minY;
        for(int q=0;q<nQuads;q++)
        {
            var vn=rim.Normals[q*4];
            if(Math.Abs(VM.Len(vn)-1.0)>1e-3)return false;
            int i0=rim.Tris[q*6],i1=rim.Tris[q*6+1],i2=rim.Tris[q*6+2];
            int j0=rim.Tris[q*6+3],j1=rim.Tris[q*6+4],j2=rim.Tris[q*6+5];
            var tn1=VM.Norm(VM.Cross(VM.Sub(rim.Verts[i1],rim.Verts[i0]),VM.Sub(rim.Verts[i2],rim.Verts[i0])),default);
            var tn2=VM.Norm(VM.Cross(VM.Sub(rim.Verts[j1],rim.Verts[j0]),VM.Sub(rim.Verts[j2],rim.Verts[j0])),default);
            if(VM.Dot(tn1,vn)<=0.99||VM.Dot(tn2,vn)<=0.99)return false;
            var qc=new V3((rim.Verts[q*4].X+rim.Verts[q*4+1].X+rim.Verts[q*4+2].X+rim.Verts[q*4+3].X)/4f,(rim.Verts[q*4].Y+rim.Verts[q*4+1].Y+rim.Verts[q*4+2].Y+rim.Verts[q*4+3].Y)/4f,(rim.Verts[q*4].Z+rim.Verts[q*4+1].Z+rim.Verts[q*4+2].Z+rim.Verts[q*4+3].Z)/4f);
            var toC=VM.Norm(new V3(-qc.X,midY-qc.Y,-qc.Z),vn);
            double d=VM.Dot(vn,toC);
            dotSum+=d;if(d<minDot)minDot=d;
            if(cens!=null)
            {
                foreach(var prev in cens)if(VM.Len(VM.Sub(qc,prev))<=1e-3)return false;
                cens.Add(qc);
            }
        }
        meanDot=dotSum/nQuads;
        return true;
    }

    // 2a. Convex 3x3 double-sided 80mm grid: center cell torn -> 100% strict inward normals on all 4 walls
    var grV=new List<V3>(32);var grN=new List<V3>(32);var grTv=new List<V3>(32);
    for(int f=0;f<2;f++)
    {
        float y=f==0?0f:-0.08f;
        var nrmY=new V3(0,f==0?1f:-1f,0);
        var tvY=f==0?new V3(0,-0.08f,0):default;
        for(int iz=0;iz<4;iz++)for(int ix=0;ix<4;ix++)
        {
            grV.Add(new V3(-0.45f+ix*0.30f,y,-0.45f+iz*0.30f));
            grN.Add(nrmY);grTv.Add(tvY);
        }
    }
    var grT=new List<int>();var grCells=new List<int>();
    for(int iz=0;iz<3;iz++)for(int ix=0;ix<3;ix++)
    {
        int cid=(ix==1&&iz==1)?5:0;
        int t0=iz*4+ix,t1=t0+1,t2=t0+5,t3=t0+4;
        grT.AddRange(new[]{t0,t2,t1, t0,t3,t2});grCells.Add(cid);grCells.Add(cid);
        int gb0=16+t0,gb1=16+t1,gb2=16+t2,gb3=16+t3;
        grT.AddRange(new[]{gb0,gb1,gb2, gb0,gb2,gb3});grCells.Add(cid);grCells.Add(cid);
    }
    var grSm=new SubdivMesh(grV.ToArray(),grN.ToArray(),null,null,null,null,new[]{grT.ToArray()},null,new[]{grCells.ToArray()},null,0.2,grTv.ToArray());
    var grRim=RimCapBuilder.Build(grSm,cid=>cid==5,0.08);
    bool grOk=InspectRimQuads(grRim,-0.04f,true,out double grSpanY,out _,out double grMinDot)&&grRim!.Tris.Count==24&&grMinDot>0.95&&grSpanY>=0.079;
    Check(grOk,$"0.8.0 RimCapBuilder convex 3x3 cell tear: 4 strict inward 80mm walls (tris={grRim?.Tris.Count}, minDot={grMinDot:F3}, spanY={grSpanY:F3}m)");

    // 2b. RimCapBuilder with ThickVec (80mm hull plate, ragged hole + TearCurl)
    var r80Sm=new SubdivMesh(r80V,r80N,null,null,null,null,r80T,null,null,null,0.2,r80Tv);
    var r80Brk=new HashSet<int>();
    int rh1=HoleMath.HoleIdBase+1,rh2=HoleMath.HoleIdBase+2;
    LocalSubdiv.RefineGraded(r80Sm,new V3(0,0,0),0.27,new V3(0,1,0),0.20,0.10,0.036,4,20000,PlateSide.Whole,null);
    HolePunch.Punch(r80Sm,new V3(0,0,0),new V3(0,1,0),0.18,PlateSide.Whole,rh1,r80Brk,51u,out _,out _);
    r80Brk.Add(rh1);
    TearCurl.Apply(r80Sm,new HashSet<int>{rh1},cid=>r80Brk.Contains(cid),new V3(0,0.05f,0),0.025,0.10,0.08,0.20,51u);
    SmartNormals.Compute(r80Sm,null,35.0,cid=>r80Brk.Contains(cid));

    var rim1=RimCapBuilder.Build(r80Sm,cid=>r80Brk.Contains(cid),0.08);
    bool r1Ok=InspectRimQuads(rim1,-0.04f,true,out double r1SpanY,out double r1MeanDot,out _)&&r1SpanY>=0.065&&r1MeanDot>0.50;
    Check(r1Ok,$"0.8.0 RimCapBuilder (ThickVec 80mm): solid outward walls (tris={rim1?.Tris.Count}, spanY={r1SpanY:F3}m, meanDot={r1MeanDot:F2})");

    // Overlapping second hole expands perimeter cleanly with zero orphan interior walls
    LocalSubdiv.RefineGraded(r80Sm,new V3(0.12f,0,0),0.27,new V3(0,1,0),0.20,0.10,0.036,4,20000,PlateSide.Whole,cid=>r80Brk.Contains(cid));
    HolePunch.Punch(r80Sm,new V3(0.12f,0,0),new V3(0,1,0),0.18,PlateSide.Whole,rh2,r80Brk,52u,out _,out _);
    r80Brk.Add(rh2);
    var rim2=RimCapBuilder.Build(r80Sm,cid=>r80Brk.Contains(cid),0.08);
    bool r2Clean=rim2!=null&&rim2.Tris.Count>=12;
    if(rim2!=null)
    {
        for(int q=0;q<rim2.Tris.Count/6;q++)
        {
            double cx=(rim2.Verts[q*4].X+rim2.Verts[q*4+1].X+rim2.Verts[q*4+2].X+rim2.Verts[q*4+3].X)*0.25;
            double cz=(rim2.Verts[q*4].Z+rim2.Verts[q*4+1].Z+rim2.Verts[q*4+2].Z+rim2.Verts[q*4+3].Z)*0.25;
            if((cx-0.06)*(cx-0.06)+cz*cz<0.04*0.04){r2Clean=false;break;}
        }
    }
    Check(r2Clean,$"0.8.0 RimCapBuilder overlapping hole: clean expanded perimeter with zero orphan interior walls (tris={rim2?.Tris.Count})");

    // 3. RimCapBuilder fallback without ThickVec (20mm LowDetail path)
    var r20V=new[]
    {
        new V3(-0.5f,0f,-0.5f),new V3(0.5f,0f,-0.5f),new V3(0.5f,0f,0.5f),new V3(-0.5f,0f,0.5f),
        new V3(-0.5f,-0.02f,-0.5f),new V3(0.5f,-0.02f,-0.5f),new V3(0.5f,-0.02f,0.5f),new V3(-0.5f,-0.02f,0.5f)
    };
    var r20Sm=new SubdivMesh(r20V,r80N,null,null,null,null,r80T,null,null,null,0.2,null);
    var r20Brk=new HashSet<int>();
    LocalSubdiv.RefineGraded(r20Sm,new V3(0,0,0),0.27,new V3(0,1,0),0.20,0.10,0.036,4,20000,PlateSide.Whole,null);
    HolePunch.Punch(r20Sm,new V3(0,0,0),new V3(0,1,0),0.18,PlateSide.Whole,rh1,r20Brk,61u,out _,out _);
    r20Brk.Add(rh1);
    var lowRim=RimCapBuilder.Build(r20Sm,cid=>r20Brk.Contains(cid),0.020);
    bool lowOk=InspectRimQuads(lowRim,-0.01f,true,out double lSpanY,out double lMeanDot,out _)&&lSpanY>=0.015&&lMeanDot>0.50;
    Check(lowOk,$"0.8.0 RimCapBuilder fallback (no ThickVec 20mm): unique non-inverted walls (tris={lowRim?.Tris.Count}, spanY={lSpanY:F3}m, meanDot={lMeanDot:F2})");
}

// 0.8.1 Surface contact blast matching (HESH/HEAT/ERA) + surface-band inward orientation
{
    // 1. BlastSnap.TryMatchContact: HESH exact shot coordinates (dist=0.00m, cal 112->125mm), HEAT jet (6->125mm), ERA (6->35mm), APHE rejection
    var hPos=new V3(63.22f,1.28f,241.63f);
    var hDir=VM.Norm(new V3(0.15f,-0.45f,0.88f),new V3(0,0,1));
    var hMarks=new List<SimHitMark>{new(2000,hPos,hDir,112,5900L,59)};
    bool heshOk=BlastSnap.TryMatchContact(hMarks,2000,hPos,125,out var mHesh,out double dHesh,out double aHesh);
    Check(heshOk&&mHesh.Vuid==59&&dHesh<1e-4&&Math.Abs(aHesh)<1e-4,
        $"0.8.1 ContactMatch HESH: matches exact log hit vuid=59 at dist={dHesh:F4}m, along={aHesh:F4}m");

    var heatMarks=new List<SimHitMark>{new(2010,hPos,hDir,6,5900L,59)};
    var heatBlast=VM.Sub(hPos,VM.Mul(hDir,0.08));
    var eraBlast=VM.Sub(hPos,VM.Mul(hDir,0.06));
    bool heatOk=BlastSnap.TryMatchContact(heatMarks,2011,heatBlast,125,out var mHeat,out double dHeat,out _);
    bool eraOk=BlastSnap.TryMatchContact(heatMarks,2011,eraBlast,35,out var mEra,out double dEra,out _);
    Check(heatOk&&mHeat.Vuid==59&&Math.Abs(dHeat-0.08)<1e-3&&eraOk&&mEra.Vuid==59&&Math.Abs(dEra-0.06)<1e-3,
        $"0.8.1 ContactMatch HEAT/ERA: matches 6mm->125mm (d={dHeat:F2}m) and 6mm->35mm (d={dEra:F2}m)");

    var aphePos=VM.Add(hPos,VM.Mul(hDir,0.60));
    bool apheSnap=BlastSnap.TrySnap(hMarks,2000,aphePos,125,out _,out _);
    bool apheContact=BlastSnap.TryMatchContact(hMarks,2000,aphePos,125,out _,out _,out _);
    bool farReject=BlastSnap.TryMatchContact(hMarks,2000,VM.Sub(hPos,VM.Mul(hDir,0.80)),125,out _,out _,out _);
    bool staleReject=BlastSnap.TryMatchContact(hMarks,2004,hPos,125,out _,out _,out _);
    Check(!apheSnap&&!apheContact&&!farReject&&!staleReject,
        "0.8.1 ContactMatch guards: rejects internal APHE (along=+0.60m), distant (0.80m), and stale (fdiff=4)");

    // 2. FaceOutwardNormal + ResolveBlastAway on double-sided 58mm plate (outer y=0 +Y, inner y=-0.058 -Y)
    var p58V=new[]
    {
        new V3(-0.6f,0f,-0.6f),new V3(0.6f,0f,-0.6f),new V3(0.6f,0f,0.6f),new V3(-0.6f,0f,0.6f),
        new V3(-0.6f,-0.058f,-0.6f),new V3(0.6f,-0.058f,-0.6f),new V3(0.6f,-0.058f,0.6f),new V3(-0.6f,-0.058f,0.6f)
    };
    var p58N=new[]
    {
        new V3(0,1,0),new V3(0,1,0),new V3(0,1,0),new V3(0,1,0),
        new V3(0,-1,0),new V3(0,-1,0),new V3(0,-1,0),new V3(0,-1,0)
    };
    var p58T=new[]{new[]{0,2,1,0,3,2, 4,5,6,4,6,7}};
    var p58Sm=new SubdivMesh(p58V,p58N,null,null,null,null,p58T,null,null,null,0.2);
    var p58Acc=new TriAccel();
    LocalSubdiv.RefineGraded(p58Sm,new V3(0,0,0),0.45,new V3(0,-1,0),0.25,0.08,0.04,4,20000,PlateSide.Whole,null);
    p58Acc.Invalidate();

    var flippedCheck=DentMath.FaceOutwardNormal(p58Sm,0,new V3(0,-1,0));
    bool gotTop=p58Acc.Closest(p58Sm.FlatBasis,p58Sm.FlatTris,p58Sm.TopologyVersion,new V3(0,-0.005f,0),null,out var cTop,out var nTop,out int tTop);
    var outTop=DentMath.FaceOutwardNormal(p58Sm,tTop,nTop);
    var badFb=new V3(0,1,0);
    var rInside5=DentMath.ResolveBlastAway(new V3(0,-0.005f,0),cTop,outTop,badFb);
    var rOnSkin=DentMath.ResolveBlastAway(new V3(0,0f,0),cTop,outTop,badFb);
    var rOut5=DentMath.ResolveBlastAway(new V3(0,0.005f,0),cTop,outTop,badFb);
    var rOut30=DentMath.ResolveBlastAway(new V3(0,0.30f,0),cTop,outTop,badFb);

    bool gotBot=p58Acc.Closest(p58Sm.FlatBasis,p58Sm.FlatTris,p58Sm.TopologyVersion,new V3(0,-0.358f,0),null,out var cBot,out var nBot,out int tBot);
    var outBot=DentMath.FaceOutwardNormal(p58Sm,tBot,nBot);
    var rAphe=DentMath.ResolveBlastAway(new V3(0,-0.358f,0),cBot,outBot,badFb);

    Check(gotTop&&flippedCheck.Y>0.99&&outTop.Y>0.99&&rInside5.Y<-0.99&&rOnSkin.Y<-0.99&&rOut5.Y<-0.99&&rOut30.Y<-0.99&&gotBot&&outBot.Y<-0.99&&rAphe.Y>0.99,
        $"0.8.1 ResolveBlastAway: flipped->+{flippedCheck.Y:F1}, in5mm={rInside5.Y:F2}, on={rOnSkin.Y:F2}, out5mm={rOut5.Y:F2}, out30cm={rOut30.Y:F2}, internalAPHE={rAphe.Y:F2}");

    // 3. End-to-end 58mm dent pushed strictly inward even from a -5mm inside blastLocal
    var dentNorm=DentMath.Orient(nTop,rInside5);
    var ev58=new DentEvent(cTop,dentNorm,0.0729,0.375,PlateSide.Whole,DentStyle.Blast,rInside5,0.058,0.28,0.45,81u);
    int mv58=DentField.ApplyRich(ev58,p58Sm);
    double outerY=0,innerY=0,maxOutY=-1;
    int oCnt=0,iCnt=0;
    for(int i=0;i<p58Sm.Basis.Count;i++)
    {
        var bp=p58Sm.Basis[i];var dp=p58Sm.Disp[i];
        maxOutY=Math.Max(maxOutY,dp.Y);
        if(bp.X*bp.X+bp.Z*bp.Z<0.12f*0.12f)
        {
            if(Math.Abs(bp.Y)<0.002f){oCnt++;outerY+=dp.Y;}
            else if(Math.Abs(bp.Y+0.058f)<0.002f){iCnt++;innerY+=dp.Y;}
        }
    }
    outerY=oCnt>0?outerY/oCnt:0;innerY=iCnt>0?innerY/iCnt:0;
    Check(mv58>20&&outerY<-0.04&&innerY<-0.01&&maxOutY<=1e-6,
        $"0.8.1 End-to-end HESH 58mm dent: inward outer={outerY:F3}m, inner={innerY:F3}m, maxOutY={maxOutY:F5}m");
}

// ---- 0.8.2: HEAT/ERA blast effect filtering & per-frame dent queue budget ----
{
    var dEra=BlastEffectFilter.Classify("era",true,0.35,3);
    var dAphe=BlastEffectFilter.Classify("aphe",true,0.35,3);
    var dHeat=BlastEffectFilter.Classify("heat",true,0.35,3);
    var dHeatOff=BlastEffectFilter.Classify("heat",true,0.0,3);
    var dHesh=BlastEffectFilter.Classify("hesh",false,0.35,3);
    var dHe=BlastEffectFilter.Classify("",false,0.35,3);
    var dOtherVisual=BlastEffectFilter.Classify("spark",true,0.35,3);
    Check(dEra.SkipBlast&&dAphe.SkipBlast&&dOtherVisual.SkipBlast,
        "0.8.2 BlastEffectFilter skips ERA/APHE/other non-HE visual effects");
    Check(!dHeat.SkipBlast&&Math.Abs(dHeat.DamageScale-0.35)<1e-9&&dHeat.MaxNonDestructibleDents==1,
        "0.8.2 BlastEffectFilter scales HEAT to 0.35 and caps to 1 non-destructible dent");
    Check(!dHeatOff.SkipBlast&&dHeatOff.DamageScale==0.0&&dHeatOff.MaxNonDestructibleDents==0,
        "0.8.2 BlastEffectFilter with heatScale=0 produces zero blast dents");
    Check(!dHesh.SkipBlast&&dHesh.DamageScale==1.0&&dHesh.MaxNonDestructibleDents==3
        &&!dHe.SkipBlast&&dHe.DamageScale==1.0&&dHe.MaxNonDestructibleDents==3,
        "0.8.2 BlastEffectFilter retains full 1.0 scale & cap 3 for native HE and HESH");

    Check(DentQueuePolicy.ShouldContinueFrame(0,2,99.0,0.5),
        "0.8.2 DentQueuePolicy always processes at least 1 dent per frame even when budget is exceeded");
    Check(DentQueuePolicy.ShouldContinueFrame(1,2,1.5,4.0),
        "0.8.2 DentQueuePolicy continues when below maxPerFrame and within ms budget");
    Check(!DentQueuePolicy.ShouldContinueFrame(2,2,0.5,4.0),
        "0.8.2 DentQueuePolicy stops at MaxDentsPerFrame");
    Check(!DentQueuePolicy.ShouldContinueFrame(1,4,4.0,4.0),
        "0.8.2 DentQueuePolicy stops when frame budget ms reached");
}

// ---- 0.8.2: T-72B indirect blast orientation, occlusion & edge-on rim scaling ----
{
    // T-72B vuid=460 geometry: 20mm sloped (21.7 deg) 1.57m applique glacis box with top face triangles ordered BEFORE rear rim.
    double rad=21.7*Math.PI/180.0;
    float sinA=(float)Math.Sin(rad),cosA=(float)Math.Cos(rad);
    var tDir=new V3(0,-sinA,cosA);   // along slope toward front
    var nTop=new V3(0,cosA,sinA);    // outward top normal (+0.929 Y, +0.370 Z)
    var nBot=VM.Mul(nTop,-1.0);
    var nRear=VM.Mul(tDir,-1.0);     // outward rear rim normal (+0.370 Y, -0.929 Z)
    var nFront=tDir;
    float plateLen=1.57f,th=0.020f;
    V3 rLt=new(-1.03f,1.040f,1.421f),rRt=new(1.03f,1.040f,1.421f);
    V3 fLt=VM.Add(rLt,VM.Mul(tDir,plateLen)),fRt=VM.Add(rRt,VM.Mul(tDir,plateLen));
    V3 rLb=VM.Add(rLt,VM.Mul(nBot,th)),rRb=VM.Add(rRt,VM.Mul(nBot,th));
    V3 fLb=VM.Add(fLt,VM.Mul(nBot,th)),fRb=VM.Add(fRt,VM.Mul(nBot,th));

    var boxVerts=new V3[]{
        rLt,fLt,fRt,rRt,   // 0..3 top face
        rLb,rRb,fRb,fLb,   // 4..7 bottom face
        rLt,rRt,rRb,rLb,   // 8..11 rear rim
        fLt,fLb,fRb,fRt    // 12..15 front rim
    };
    var boxNorms=new V3[]{
        nTop,nTop,nTop,nTop,
        nBot,nBot,nBot,nBot,
        nRear,nRear,nRear,nRear,
        nFront,nFront,nFront,nFront
    };
    int[] topFirstTris={
        0,1,2, 0,2,3,       // top face FIRST (reproduces TriAccel.Closest tie bug)
        4,5,6, 4,6,7,       // bottom
        8,9,10, 8,10,11,    // rear rim
        12,13,14, 12,14,15  // front rim
    };
    var acc1=new TriAccel();
    var blast4=new V3(0.37f,1.105f,0.915f); // T-72B Shot 4 turret blast above hull roof, behind vuid=460 top plane
    bool ok1=acc1.ClosestBlast(boxVerts,topFirstTris,0,boxNorms,blast4,null,out var c1,out var outN1,out _,out double fc1,0.005);
    var away1=DentMath.ResolveBlastAway(blast4,c1,outN1,new V3(0,-1,0));
    var inN1=DentMath.Orient(outN1,away1);
    double dist1=VM.Len(VM.Sub(c1,blast4));
    Check(ok1&&dist1>0.04&&fc1>0.9&&outN1.Z<-0.9&&inN1.Y<-0.3&&inN1.Z>0.9,
        $"0.8.2 ClosestBlast picks front-facing rear rim (fc={fc1:F3}, inN=({inN1.X:F2},{inN1.Y:F2},{inN1.Z:F2}))");

    bool hitFront=acc1.RayFirst(boxVerts,topFirstTris,0,VM.Add(c1,VM.Mul(inN1,0.002)),inN1,6.0,null,out double spanDist,out _,out _);
    double spanMm=(spanDist+0.002)*1000.0;
    Check(hitFront&&spanMm>1000.0&&DentMath.IsEdgeOnRim(spanMm,20.0),
        $"0.8.2 Long edge-on probe span={spanMm:F0}mm flagged by IsEdgeOnRim");
    Check(!DentMath.IsEdgeOnRim(20.0,20.0)&&!DentMath.IsEdgeOnRim(260.0,100.0)&&!DentMath.IsEdgeOnRim(double.NaN,20.0),
        "0.8.2 IsEdgeOnRim leaves broadside 20mm and thick 100mm plates unscaled");

    // Reversed triangle order must produce identical selection
    int[] rimFirstTris={
        12,13,14, 12,14,15,
        8,9,10, 8,10,11,
        4,5,6, 4,6,7,
        0,1,2, 0,2,3
    };
    var acc2=new TriAccel();
    bool ok2=acc2.ClosestBlast(boxVerts,rimFirstTris,0,boxNorms,blast4,null,out var c2,out var outN2,out _,out double fc2,0.005);
    Check(ok2&&fc2>0.9&&Math.Abs(outN2.Z-outN1.Z)<1e-3&&VM.Len(VM.Sub(c2,c1))<1e-3,
        "0.8.2 ClosestBlast is triangle-order independent");

    // Single-sided open sloped skin with blast behind its plane -> frontCos <= 0, rejected when dist > 0.04
    V3[] openVerts={rLt,fLt,fRt,rRt};
    V3[] openNorms={nTop,nTop,nTop,nTop};
    int[] openTris={0,1,2, 0,2,3};
    var accOpen=new TriAccel();
    bool okOpen=accOpen.ClosestBlast(openVerts,openTris,0,openNorms,blast4,null,out var cOpen,out _,out _,out double fcOpen,0.005);
    double dOpen=VM.Len(VM.Sub(cOpen,blast4));
    Check(okOpen&&dOpen>0.04&&fcOpen<-0.1,
        $"0.8.2 Open single-sided back-facing blast gives negative frontCos={fcOpen:F3} at d={dOpen:F2}m");

    // Inter-plate occlusion (belly blast occluded by hull floor/roof, hole unblocks underlying composite)
    V3[] barrierV={new(-1.5f,0.45f,0f),new(1.5f,0.45f,0f),new(1.5f,0.45f,3f),new(-1.5f,0.45f,3f)};
    int[] barrierT={0,2,1, 0,3,2}; // tri 0 & tri 1 represent intact cells
    var bAcc=new TriAccel();
    Check(BlastOcclusion.SegmentBlocked(bAcc,barrierV,barrierT,0,new V3(0,0.0f,1.5f),new V3(0,0.70f,2.0f),0.015,0.025),
        "0.8.2 BlastOcclusion blocks ground/belly blast behind intact hull barrier");
    Check(!BlastOcclusion.SegmentBlocked(bAcc,barrierV,barrierT,0,new V3(0,1.11f,0.9f),new V3(0,0.50f,0.9f),0.015,0.025),
        "0.8.2 BlastOcclusion does not block open-air blast above intact hull barrier");
    Check(!BlastOcclusion.SegmentBlocked(bAcc,barrierV,barrierT,0,new V3(0,0.80f,1.5f),new V3(0,0.10f,1.5f),0.015,0.025,ti=>false),
        "0.8.2 BlastOcclusion stops blocking when all barrier triangles along ray belong to broken cells");
}

// ---- 0.9.0: Destructible loose exterior parts & cannon/core hierarchy guards ----
{
    // Core / cannon / mantlet / drivetrain components must ALWAYS be protected
    Check(PartFilter.IsCoreComponent("Cannon")&&PartFilter.IsCoreComponent("Mantlet")
        &&PartFilter.IsCoreComponent("CannonBehaviour")&&PartFilter.IsCoreComponent("VehicleObjectModel","cannonBarrelSegment")
        &&PartFilter.IsCoreComponent("VehicleObjectModel","motorModel")&&PartFilter.IsCoreComponent("VehicleObjectModel","sprocketHingeBaseModel"),
        "0.9.0 PartFilter protects cannons, mantlets, barrel segments, motors, and hinges");

    // Loose exterior parts: smoke launchers & decorative VehicleObjectModel accepted by default
    Check(PartFilter.IsLoosePartCandidate("VehicleObjectModel","smokeLauncherBankModel","SmokeLauncher",false,false)
        &&PartFilter.IsLoosePartCandidate("VehicleObjectModel","model","Headlight",false,false)
        &&PartFilter.IsLoosePartCandidate("VehicleObjectModel","model","GeneratorBox",false,false),
        "0.9.0 PartFilter accepts smoke launchers, headlights, and stowage (no false 'era' match on GeneratorBox)");

    // Cannon/mantlet/barrel/wheel models rejected as loose parts
    Check(!PartFilter.IsLoosePartCandidate("VehicleObjectModel","cannonBarrelSegment","Barrel",false,false)
        &&!PartFilter.IsLoosePartCandidate("VehicleObjectModel","mantletModel","Mantlet",false,false)
        &&!PartFilter.IsLoosePartCandidate("VehicleObjectModel","axleModel","Axle",false,false),
        "0.9.0 PartFilter rejects cannon/mantlet/axle models as loose parts");

    // Optics & ERA excluded by default, allowed when opted in
    Check(!PartFilter.IsLoosePartCandidate("VehicleObjectModel","t72StyleSight","T72Sight",false,false)
        &&PartFilter.IsLoosePartCandidate("VehicleObjectModel","t72StyleSight","T72Sight",true,false),
        "0.9.0 PartFilter excludes optics by default and includes them when includeOptics=true");
    Check(!PartFilter.IsLoosePartCandidate("VehicleObjectModel","kontakt1Housing","Kontakt1",false,false)
        &&!PartFilter.IsLoosePartCandidate("VehicleObjectModel","model","ERA_Block_01",false,false)
        &&PartFilter.IsLoosePartCandidate("VehicleObjectModel","kontakt1Housing","Kontakt1",false,true),
        "0.9.0 PartFilter excludes ERA by default and includes it when includeEra=true");

    // LoosePartMath: durability scales with size, kinetic hits break light parts, SegmentDistSq works
    double sSmall=LoosePartMath.Strength(35.0,0.15);
    double sMed=LoosePartMath.Strength(35.0,0.35);
    double sLarge=LoosePartMath.Strength(35.0,0.90);
    Check(sSmall>=10.0&&sSmall<sMed&&sMed<sLarge&&sLarge<=250.0,
        $"0.9.0 LoosePartMath.Strength scales with size ({sSmall:F1} < {sMed:F1} < {sLarge:F1})");
    Check(LoosePartMath.KineticDamage(0.05,25)>=35.0&&LoosePartMath.KineticDamage(6.0,120)>=200.0,
        "0.9.0 LoosePartMath.KineticDamage breaks loose parts on direct 25mm+ / APFSDS hits");
    Check(Math.Abs(LoosePartMath.SegmentDistSq(new V3(0,1,0),new V3(0,0,0),new V3(2,0,0))-1.0)<1e-6
        &&Math.Abs(LoosePartMath.SegmentDistSq(new V3(3,0,0),new V3(0,0,0),new V3(2,0,0))-1.0)<1e-6,
        "0.9.0 LoosePartMath.SegmentDistSq exact for perpendicular and clamped endpoint queries");
}

// ---- 1.0.0: Whole-tank toggle, EB-integrated blast breach math, and inward breach crew/module damage ----
{
    // 1. TankDestructionFlag roundtrip & case-insensitivity
    Check(!TankDestructionFlag.IsDisabled(null)&&!TankDestructionFlag.IsDisabled("")&&!TankDestructionFlag.IsDisabled("Main hull"),
        "1.0.0 TankDestructionFlag: enabled by default when [no-destruction] is absent");
    string? td1=TankDestructionFlag.SetEnabled("Main hull",false);
    Check(TankDestructionFlag.IsDisabled(td1)&&td1=="Main hull [no-destruction]",
        "1.0.0 TankDestructionFlag: SetEnabled(false) appends [no-destruction]");
    Check(TankDestructionFlag.SetEnabled(td1,false)==td1,"1.0.0 TankDestructionFlag: idempotent disable");
    Check(TankDestructionFlag.SetEnabled(td1,true)=="Main hull","1.0.0 TankDestructionFlag: SetEnabled(true) restores original description");
    Check(TankDestructionFlag.SetEnabled("[NO-DESTRUCTION]",true)==null,"1.0.0 TankDestructionFlag: removes case-insensitive token cleanly to null");

    // 2. BlastBreachMath: EB override, fallback HE/HESH/HEAT/ERA, steep distance falloff, accumulation guard, bounded hole radius, 0.4 m zones
    Check(Math.Abs(BlastBreachMath.EffectiveCalibreMm(100,true,152.0)-152.0)<1e-6
        &&Math.Abs(BlastBreachMath.EffectiveCalibreMm(125,false,152.0)-125.0)<1e-6,
        "1.0.0 BlastBreachMath.EffectiveCalibreMm prefers EB telemetry when present");
    double he100=BlastBreachMath.EffectiveBreachMm("he",100,false,0,20);
    double he152=BlastBreachMath.EffectiveBreachMm("he",152,false,0,20);
    double hesh152=BlastBreachMath.EffectiveBreachMm("hesh",152,false,0,20);
    double heat125=BlastBreachMath.EffectiveBreachMm("heat",125,false,0,20);
    double ebOverride=BlastBreachMath.EffectiveBreachMm("he",152,true,55.0,20);
    Check(Math.Abs(he100-20.0)<1e-6&&he152>45.0&&he152<47.0&&hesh152>24.0&&hesh152<28.0&&heat125==0.0&&Math.Abs(ebOverride-55.0)<1e-6,
        $"1.0.0 BlastBreachMath.EffectiveBreachMm: HE100={he100:F1}mm, HE152={he152:F1}mm, HESH152={hesh152:F1}mm, HEAT={heat125:F1}mm, EB={ebOverride:F1}mm");

    // Steep breach distance falloff: 152 mm HE (46.2 mm contact) drops sharply with distance so 1.76 m away cannot breach a 20 mm roof or 5 mm turret
    double cap0=BlastBreachMath.BreachCapacityAtDistanceMm(he152,152,0.0);
    double capHalf=BlastBreachMath.BreachCapacityAtDistanceMm(he152,152,0.4256);
    double capFar=BlastBreachMath.BreachCapacityAtDistanceMm(he152,152,1.76);
    Check(Math.Abs(cap0-he152)<1e-6&&Math.Abs(capHalf-he152*0.5)<0.5&&capFar<1.0,
        $"1.0.0 BlastBreachMath distance falloff: 0m={cap0:F1}mm, rCore={capHalf:F1}mm, 1.76m={capFar:F2}mm (<1mm)");

    // Accumulation guard: 152mm HE (46mm) can chip away at an 80mm plate (<=101mm), NOT a 200mm turret cheek (>101mm)
    Check(BlastBreachMath.CanAccumulateBreach(80.0,he152)&&!BlastBreachMath.CanAccumulateBreach(200.0,he152)&&!BlastBreachMath.CanAccumulateBreach(10.0,0.0),
        "1.0.0 BlastBreachMath.CanAccumulateBreach allows <=2.2x contact breach and blocks tiny shells chipping 200mm armor");

    // Localized hole radius stays realistic (0.09..0.38 m)
    double rSmall=BlastBreachMath.BreachHoleRadiusM(22.0,20.0,100.0,0.38);
    double rBig=BlastBreachMath.BreachHoleRadiusM(46.2,10.0,152.0,0.38);
    Check(rSmall>=0.09&&rSmall<0.16&&rBig>rSmall&&rBig<=0.38,
        $"1.0.0 BlastBreachMath.BreachHoleRadiusM: bounded localized radii (small={rSmall:F3}m, big={rBig:F3}m <= 0.38m)");

    // 0.4 m zone buckets group nearby hits and separate opposite ends of the hull
    var z1=BlastBreachMath.ZoneKey(new V3(0.10f,0.10f,0.10f));
    var z2=BlastBreachMath.ZoneKey(new V3(0.35f,0.20f,0.15f));
    var z3=BlastBreachMath.ZoneKey(new V3(1.50f,0.10f,0.10f));
    Check(z1==z2&&z1!=z3,"1.0.0 BlastBreachMath.ZoneKey groups same-spot hits and isolates distant hits");

    // 3. BreachDamageMath: excess, cone radius, behind-hole cone check, crew vs module damage falloff
    double ex152=BreachDamageMath.ExcessMm(46.2,15.0);
    double exThin=BreachDamageMath.ExcessMm(20.0,20.0);
    double cone152=BreachDamageMath.ConeRadiusM(152.0,46.2,15.0);
    double cone100=BreachDamageMath.ConeRadiusM(100.0,20.0,20.0);
    Check(ex152>35.0&&exThin>6.0&&cone152>cone100&&cone152<=3.00&&cone100>=0.85,
        $"1.0.0/1.0.2 BreachDamageMath cone: 152mm={cone152:F2}m (excess={ex152:F1}mm) > 100mm={cone100:F2}m (excess={exThin:F1}mm)");
    Check(BreachDamageMath.IsBehindHole(new V3(0,0,0),new V3(0,0,1),new V3(0.1f,0,0.8f))
        &&!BreachDamageMath.IsBehindHole(new V3(0,0,0),new V3(0,0,1),new V3(0,0,-0.8f))
        &&BreachDamageMath.IsBehindHole(new V3(0,0,0),new V3(0,0,1),new V3(0,0,-0.20f),0.35),
        "1.0.0 BreachDamageMath.IsBehindHole accepts inward hemisphere and close-contact modules, rejects exterior behind-blast points");
    double crewNear=BreachDamageMath.ModuleDamageFraction(0.30,cone152,152.0,ex152,46.2,true);
    double crewFar=BreachDamageMath.ModuleDamageFraction(1.60,cone152,152.0,ex152,46.2,true);
    double modFar=BreachDamageMath.ModuleDamageFraction(1.60,cone152,152.0,ex152,46.2,false);
    double outOfCone=BreachDamageMath.ModuleDamageFraction(cone152+0.1,cone152,152.0,ex152,46.2,true);
    Check(crewNear>=0.95&&crewFar>modFar&&modFar>0&&crewFar<crewNear&&outOfCone==0.0,
        $"1.0.0/1.0.2 BreachDamageMath.ModuleDamageFraction: crewNear={crewNear:P0}, crewFar={crewFar:P0} > modFar={modFar:P0}, out={outOfCone:P0}");

    // 5. 1.0.2 Shot 3 turret-roof breach lethality (152mm HE on 28.7mm roof -> crew at 1.10m takes >=75% lethal overpressure, 75mm HE is gentler) + ERA stripping helpers
    double exRoof=BreachDamageMath.ExcessMm(46.2,28.7);
    double coneRoof=BreachDamageMath.ConeRadiusM(152.0,46.2,28.7);
    double crewRoof110=BreachDamageMath.ModuleDamageFraction(1.10,coneRoof,152.0,exRoof,46.2,true);
    double ex75=BreachDamageMath.ExcessMm(11.25,10.0);
    double cone75=BreachDamageMath.ConeRadiusM(75.0,11.25,10.0);
    double crew75_110=BreachDamageMath.ModuleDamageFraction(1.10,cone75,75.0,ex75,11.25,true);
    Check(coneRoof>2.3&&crewRoof110>=0.75&&crew75_110>0.05&&crew75_110<0.40,
        $"1.0.2 Enclosed-compartment breach: 152mm roof breach cone={coneRoof:F2}m crew@1.10m={crewRoof110:P0} (>=75%), 75mm crew@1.10m={crew75_110:P0}");
    Check(Math.Abs(BlastBreachMath.EraStripRadiusM(152.0,0.45)-0.684)<1e-3&&BlastBreachMath.EraStripRadiusM(30.0,0.45)==0.0
        &&PartFilter.IsEraComponent("VehicleObjectModel","kontakt5WtTurretSHousing","")
        &&PartFilter.IsEraComponent("VehicleObjectModel","nizhPanel","")
        &&PartFilter.IsEraActiveCell("kontakt5Stage1Cell1","")&&!PartFilter.IsEraActiveCell("kontakt5WtTurretSHousing",""),
        "1.0.2 ERA blast strip radius (152mm->0.684m, 30mm->0m) and ERA housing vs active cell classification");

    // 4. 1.0.1 [Solid block] recognition, RayFarthest full-block depth vs 4mm visual shell, and thick-armor blast dent cap
    Check(BlastBreachMath.IsSolidBlockName("Composite armour [Solid block]")&&!BlastBreachMath.IsSolidBlockName("Composite armour [Layers]")&&!BlastBreachMath.IsSolidBlockName(null),
        "1.0.1 IsSolidBlockName detects EB [Solid block] marker accurately");

    // Hollow 300mm solid-block cheek with 4mm front/back skins: RayFirst sees 4mm skin, RayFarthest sees full 300mm block!
    V3[] sbV={
        new(-0.5f,-0.5f,0.000f),new(0.5f,-0.5f,0.000f),new(0.5f,0.5f,0.000f),new(-0.5f,0.5f,0.000f), // front outer z=0
        new(-0.5f,-0.5f,0.004f),new(0.5f,-0.5f,0.004f),new(0.5f,0.5f,0.004f),new(-0.5f,0.5f,0.004f), // front inner z=4mm
        new(-0.5f,-0.5f,0.296f),new(0.5f,-0.5f,0.296f),new(0.5f,0.5f,0.296f),new(-0.5f,0.5f,0.296f), // back inner z=296mm
        new(-0.5f,-0.5f,0.300f),new(0.5f,-0.5f,0.300f),new(0.5f,0.5f,0.300f),new(-0.5f,0.5f,0.300f)  // back outer z=300mm
    };
    int[] sbT={
        0,1,2, 0,2,3,
        4,6,5, 4,7,6,
        8,9,10, 8,10,11,
        12,14,13, 12,15,14
    };
    var sbAcc=new TriAccel();
    bool rfOk=sbAcc.RayFirst(sbV,sbT,0,new V3(0,0,0.002f),new V3(0,0,1),1.5,null,out double rfD,out _,out _);
    bool rlOk=sbAcc.RayFarthest(sbV,sbT,0,new V3(0,0,0.002f),new V3(0,0,1),1.5,null,out double rlD,out _);
    double firstMm=(rfD+0.002)*1000.0,farMm=(rlD+0.002)*1000.0;
    Check(rfOk&&rlOk&&Math.Abs(firstMm-4.0)<0.2&&Math.Abs(farMm-300.0)<0.2,
        $"1.0.1 RayFarthest measures full [Solid block] depth={farMm:F1}mm instead of hollow visual skin={firstMm:F1}mm");

    double dmg152=BlastMath.Damage(190.0,152.0,0.0,0.5,20.0);
    double dThin=BlastBreachMath.RealisticBlastDentDepthMm(dmg152,20.0,20.0,150.0,1.8,120.0,out _);
    double d130=BlastBreachMath.RealisticBlastDentDepthMm(dmg152,130.0,20.0,150.0,1.8,120.0,out _);
    double d350=BlastBreachMath.RealisticBlastDentDepthMm(dmg152,350.0,20.0,150.0,1.8,120.0,out _);
    Check(dThin>80.0&&d130>=12.0&&d130<=50.0&&d350>=5.0&&d350<=25.0&&d130>d350,
        $"1.0.1 RealisticBlastDentDepthMm: 20mm={dThin:F1}mm, 130mm={d130:F1}mm (<=50mm), 350mm={d350:F1}mm (<=25mm)");
}

Console.WriteLine($"SUMMARY {pass} PASS, {fail} FAIL");
return fail==0?0:1;

sealed class FlatGround:IGround
{
    public double Y;public bool Slab;
    public bool Cast(V3 from,V3 to,out V3 point,out V3 normal,out double fraction)
    {
        point=to;normal=new V3(0,1,0);fraction=1;
        bool cross=Slab?to.Y<=Y:(from.Y>Y&&to.Y<=Y);
        if(!cross)return false;
        fraction=from.Y==to.Y?1:(from.Y-Y)/(from.Y-to.Y);point=new V3(from.X+(to.X-from.X)*(float)fraction,(float)Y,from.Z+(to.Z-from.Z)*(float)fraction);return true;
    }
}
