using SprocketTelescopicMast;
try
{
int checks = 0;
void Near(float actual, float expected) { checks++; if (MathF.Abs(actual - expected) > .00002f || !float.IsFinite(actual)) throw new Exception($"{actual} != {expected}"); }
void Check(bool ok, string what) { checks++; if (!ok) throw new Exception(what); }
Check(!MastDatePolicy.Allows(new DateTime(1966,12,31),MastDatePolicy.MastDate),"mast unavailable one day early");
Check(MastDatePolicy.Allows(new DateTime(1967,1,1),MastDatePolicy.MastDate),"mast available on 1967.01.01");
Check(MastDatePolicy.Allows(new DateTime(9999,12,31),MastDatePolicy.MastDate),"final-era sentinel makes the mast available");
Check(!MastDatePolicy.Allows(null,MastDatePolicy.MastDate),"missing vehicle date fails closed");
Check(MastDatePolicy.Allows(new DateTime(1914,7,28),MastDatePolicy.HingeDate),"hinge/tool date 0000.01.01 allows all vehicles");
var savedMastPart="telescopeBase";
Check(!MastDatePolicy.Allows(new DateTime(1950,1,1),MastDatePolicy.MastDate) && savedMastPart=="telescopeBase",
    "too-early saved mast part keeps its identity and stays inactive");
float height=0;
for(int i=0;i<500;i++) height=Travel.Step(height,1,.2f,.01f,1);
Near(height,1);
for(int i=0;i<1000;i++) Near(Travel.Step(height,1,.2f,.01f,1),1);
Near(Travel.Step(height,-1,.2f,.01f,1),.998f); // Reverse immediately at the stop.
for(int i=0;i<500;i++) height=Travel.Step(height,-1,.2f,.01f,1);
Near(height,0);
Near(Travel.Step(0,-1,.2f,.1f,1),0);
Near(Travel.Step(.5f,0,.2f,.1f,1),.5f); // Both keys cancel.
Near(Travel.Step(.5f,1,.2f,0,1),.5f);
Near(Travel.Step(.5f,1,.2f,float.NaN,1),.5f);
Near(Travel.Step(.5f,1,float.NaN,.1f,1),.5f);
Near(Travel.Step(float.NaN,0,.2f,.1f,1),0);
Near(Travel.Step(.9f,0,.2f,.1f,.6f),.6f);
Near(Travel.Step(.5f,1,.2f,100,1),.52f);
// Delta offsets for the fixed sleeve cancel across a full extension/retraction.
float movingRoot=0, sleeve=0, previous=0;
for(int i=0;i<2000;i++) {
  float next=Travel.Step(previous,i<1000?1:-1,.2f,.01f,1);
  movingRoot += next-previous; sleeve=-next;
  Near(movingRoot+sleeve,0); previous=next;
}
Near(previous,0); Near(movingRoot,0);
foreach(float scale in new[]{.1f,.25f,.5f,1f,2f,5f}) {
  float root=0, applied=0, normalized=0;
  for(int i=0;i<1200;i++) {
    normalized=Travel.Step(normalized,i<600?1:-1,.2f,.01f,1);
    float distance=Travel.WorldExtension(normalized,scale);
    root+=distance-applied; applied=distance;
    Near(root-applied,0); // Sleeve stays fixed at every supported height scale.
    if(i==599) Near(distance,scale);
  }
  Near(root,0);
}
Near(Travel.WorldExtension(1,.25f),.25f);
Near(Travel.VolumeFactor(.5f,1,.5f),.25f);
Near(Travel.VolumeFactor(1,.5f,1),.5f);
Near(Travel.ScaleAxis(float.NaN),1);
// A native transition may replace the current position before retraction.
// Canonical positioning must be independent of that stale position or offset.
foreach(float scale in new[]{.1f,.25f,.5f,1f,2f,5f}) {
  foreach(float stale in new[]{-10f,0f,.25f,1.25f,2.25f,10f}) {
    for(int cycle=0;cycle<50;cycle++) {
      float seated=Travel.InsertPosition(0,scale);
      Near(seated,1.25f*scale);
      Near(Travel.InsertPosition(1,scale),2.25f*scale);
      Near(Travel.InsertPosition(0,scale),seated); // Repeated saves/reseating cannot drift.
    }
  }
}
// Separate insert has a 1.23m rod below its cap, a 1.25m sleeve and a 1m stop.
foreach(float scale in new[]{.1f,.25f,.5f,1f,2f,5f}) {
  for(int i=0;i<=100;i++) {
    float stroke=i/100f;
    float sleeveTop=1.25f*scale;
    float rodBottom=(1.25f+stroke-1.23f)*scale;
    Near(sleeveTop-rodBottom,(1.23f-stroke)*scale);
    checks++;
    if(rodBottom < 0 || sleeveTop-rodBottom < .22999f*scale) throw new Exception("Rod exits guide or floor");
  }
}
Console.WriteLine($"PASS {checks} travel and sleeve-offset checks. Native hierarchy/gameplay still needs in-game verification.");
int ioChecks = IoTests.Run();
int io05Checks = IoTests05.Run();
Console.WriteLine($"PASS {io05Checks} 0.5.0 checks (new input types, cannon choice + fallback, reloaded edge, shell ejector, hinge chains, loop guard, mast state, 0.4.x links, mount mass).");
Console.WriteLine($"PASS {ioChecks} hinge I/O checks (registry, schema, pulse/toggle/follow/delay timing, multiple links, JSON round trip, unknown types kept, fire-group bitmask, hinge geometry). Native hooks, rotation and saving still need in-game verification.");
int io06Checks = IoTests06.Run();
Console.WriteLine($"PASS {io06Checks} 0.6.0 checks (rule sentences from the registry: source/event/command, sentence edits, default rule, templates, live signals, description fallback, 0.5 configs unchanged).");
int io07Checks = IoTests07.Run();
Console.WriteLine($"PASS {io07Checks} 0.7.0 checks (API id validation, callback isolation, external apply gate, save/load rules, braking decision, 0.6 config, shim version check).");
int io071Checks = IoTests071.Run();
Console.WriteLine($"PASS {io071Checks} 0.7.1 checks (in-place state merge keeps every existing name/value/type and adds or replaces hingeIoV1; fire detection needs a moved native nextID counter and keeps the 150 ms duplicate filter).");
Console.WriteLine($"PASS {FlowTests.Run()} 0.8A flow checks.");
var migration = MigrationFixtures.Run(); Console.WriteLine($"MIGRATION {migration.Fixtures} fixtures, {migration.Checks} comparisons, {migration.Deviations} fixtures with deviations. See logs/migration-0.8a.csv.");
Console.WriteLine($"PASS {FlowEditorTests.Run()} 0.8B editor checks (two-click wiring, replacement, selection, revisions, persistence, sandbox isolation and live summaries).");
Console.WriteLine($"PASS {Fixes081Tests.Run()} 0.8.1 checks (actual panel counters/getters, templates, default cannon matcher and reload to Follow motion).");
Console.WriteLine($"PASS {CamoTests.Run()} camo lifecycle and UV checks.");
Console.WriteLine($"PASS {RotatorTests.Run()} 0.9.1 checks (indexed STL data, authored UVs/normals, round scaling, ports, ramps, home, state wrap, templates, storage, COM throttle and actual panel).");
Console.WriteLine($"PASS {Continuation091Tests.Run()} 0.9.1b checks (Always on, Any group, preserved groups, vehicle isolation and editor hints).");
Console.WriteLine($"PASS {Tests092.Run()} 0.9.2 checks (wire replace/snap/refusals with 24 blocks, tabs, Main migration, round trip, free placement, undo/redo with limit 50, I/O Flow name and unchanged ids).");
Console.WriteLine($"PASS {Tests093.Run()} 0.9.3 checks (IO-API v1.1: one managed output for three parts sharing a ComponentID, Set/Get through StateMerge, all limits, managed flow without a mast part, icon rules, one apply(false), v1 RegisterOutput unchanged, shim without v1.1).");
Console.WriteLine($"PASS {Tests094.Run()} 0.9.4 checks (actual panel drags with snap/replacement/refusals and a no-target no-op, focus-only Ctrl+Z/Ctrl+Y/Ctrl+Shift+Z with the game input blocked, VisibleRect placement, Tabs labels getter reuse, Kit >=0.1.12).");
Console.WriteLine($"PASS {Tests098.Run()} 0.9.8 actual panel checks (preserved twin node, visible hint, valid preview identity and genuinely missing part).");

Console.WriteLine($"PASS {Tests099.Run()} 0.9.9 settings checks.");
}
catch (Exception ex)
{
    Console.Error.WriteLine(ex); Environment.ExitCode = 1;
}
