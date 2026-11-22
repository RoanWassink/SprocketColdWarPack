using SprocketExpandedBallistics;
if(args.Contains("--feat104-only")){var n=0;var bad=0;Feat022104ApheDetonationTests.Run((ok,t)=>{n++;Console.WriteLine((ok?"PASS ":"FAIL ")+t);if(!ok){bad++;Environment.ExitCode=1;}});Console.WriteLine($"FEAT104 ONLY: {n} checks, {bad} failures");return;}
if(args.Contains("--feat101-only")){var n=0;var bad=0;Feat022101ShellEraFallbackAndWarningsTests.Run((ok,t)=>{n++;Console.WriteLine((ok?"PASS ":"FAIL ")+t);if(!ok){bad++;Environment.ExitCode=1;}});Console.WriteLine($"FEAT101 ONLY: {n} checks, {bad} failures");return;}
if(args.Contains("--shell-era-regression")){var n=0;var bad=0;void C(bool ok,string t){n++;if(!ok){bad++;Environment.ExitCode=1;Console.WriteLine("FAIL "+t);}}n+=ShellEraListTests.Run();EraDateGateTests.Run(C);Release0223Tests.Run(C);Wave2bUiTests.Run(C);Console.WriteLine($"SHELL-ERA REGRESSION: {n} checks, {bad} failures");return;}
if(args.Contains("--feat100-only")){var n=0;var bad=0;Feat022100FloppyPartsHandoffTests.Run((ok,t)=>{n++;Console.WriteLine((ok?"PASS ":"FAIL ")+t);if(!ok){bad++;Environment.ExitCode=1;}});Console.WriteLine($"FEAT100 ONLY: {n} checks, {bad} failures");return;}
if(args.Contains("--feat99-only")){var n=0;var bad=0;Feat02299HeatAndAmmoTests.Run((ok,t)=>{n++;Console.WriteLine((ok?"PASS ":"FAIL ")+t);if(!ok){bad++;Environment.ExitCode=1;}});Console.WriteLine($"FEAT99 ONLY: {n} checks, {bad} failures");return;}
if(args.Contains("--feat98-only")){var n=0;var bad=0;Feat02298ReportPolishTests.Run((ok,t)=>{n++;Console.WriteLine((ok?"PASS ":"FAIL ")+t);if(!ok){bad++;Environment.ExitCode=1;}});Console.WriteLine($"FEAT98 ONLY: {n} checks, {bad} failures");return;}
if(args.Contains("--feat97-only")){var n=0;var bad=0;Feat02297SolidColliderTests.Run((ok,t)=>{n++;Console.WriteLine((ok?"PASS ":"FAIL ")+t);if(!ok){bad++;Environment.ExitCode=1;}});Console.WriteLine($"FEAT97 ONLY: {n} checks, {bad} failures");return;}
if(args.Contains("--feat96-only")){var n=0;var bad=0;Feat02296StageCellBindingTests.Run((ok,t)=>{n++;Console.WriteLine((ok?"PASS ":"FAIL ")+t);if(!ok){bad++;Environment.ExitCode=1;}});Console.WriteLine($"FEAT96 ONLY: {n} checks, {bad} failures");return;}
if(args.Contains("--feat95-only")){var n=0;var bad=0;Feat02295GameBuildGateTests.Run((ok,t)=>{n++;Console.WriteLine((ok?"PASS ":"FAIL ")+t);if(!ok){bad++;Environment.ExitCode=1;}});Console.WriteLine($"FEAT95 ONLY: {n} checks, {bad} failures");return;}
if(args.Contains("--feat94-only")){var n=0;var bad=0;Feat02294EraPlacementTests.Run((ok,t)=>{n++;Console.WriteLine((ok?"PASS ":"FAIL ")+t);if(!ok){bad++;Environment.ExitCode=1;}});Console.WriteLine($"FEAT94 ONLY: {n} checks, {bad} failures");return;}
if(args.Contains("--feat92-only")){var n=0;var bad=0;Feat02292AtgmSightHudTests.Run((ok,t)=>{n++;Console.WriteLine((ok?"PASS ":"FAIL ")+t);if(!ok){bad++;Environment.ExitCode=1;}});Console.WriteLine($"FEAT92 ONLY: {n} checks, {bad} failures");return;}
if(args.Contains("--feat91-only")){var n=0;var bad=0;Feat02291ModuleLayersTests.Run((ok,t)=>{n++;Console.WriteLine((ok?"PASS ":"FAIL ")+t);if(!ok){bad++;Environment.ExitCode=1;}});Console.WriteLine($"FEAT91 ONLY: {n} checks, {bad} failures");return;}
if(args.Contains("--feat90-only")){var n=0;var bad=0;Feat02290NativeDivisorTests.Run((ok,t)=>{n++;Console.WriteLine((ok?"PASS ":"FAIL ")+t);if(!ok){bad++;Environment.ExitCode=1;}});Console.WriteLine($"FEAT90 ONLY: {n} checks, {bad} failures");return;}
// 0.22.89: targeted runs while building ("--classes A,B": the static Run(Action<bool,string>) of each named test class, in that order)
if(args.Length>1&&args[0]=="--classes"){var n=0;var bad=0;foreach(var name in args[1].Split(',',StringSplitOptions.RemoveEmptyEntries)){var type=typeof(Feat02289AtgmBoxHeSkirtTests).Assembly.GetType("SprocketExpandedBallistics."+name)??throw new Exception("no test class "+name);var run=type.GetMethod("Run",System.Reflection.BindingFlags.Static|System.Reflection.BindingFlags.Public|System.Reflection.BindingFlags.NonPublic,new[]{typeof(Action<bool,string>)})??throw new Exception("no Run(Action<bool,string>) in "+name);var before=bad;run.Invoke(null,new object[]{(Action<bool,string>)((ok,text)=>{n++;if(!ok){bad++;Environment.ExitCode=1;Console.WriteLine("FAIL "+text);}})});Console.WriteLine($"{name}: {(bad==before?"pass":(bad-before)+" failure(s)")}");}Console.WriteLine($"CLASSES: {n} checks, {bad} failures");return;}
if(args.Contains("--feat89-only")){var n=0;var bad=0;Feat02289AtgmBoxHeSkirtTests.Run((ok,t)=>{n++;Console.WriteLine((ok?"PASS ":"FAIL ")+t);if(!ok){bad++;Environment.ExitCode=1;}});Console.WriteLine($"FEAT89 ONLY: {n} checks, {bad} failures");return;}
if(args.Contains("--feat88-only")){var n=0;var bad=0;Feat02288DerivedThicknessTests.Run((ok,t)=>{n++;Console.WriteLine((ok?"PASS ":"FAIL ")+t);if(!ok){bad++;Environment.ExitCode=1;}});Console.WriteLine($"FEAT88 ONLY: {n} checks, {bad} failures");return;}
if(args.Contains("--feat87-only")){var n=0;var bad=0;Feat02287AtgmSightTests.Run((ok,t)=>{n++;Console.WriteLine((ok?"PASS ":"FAIL ")+t);if(!ok){bad++;Environment.ExitCode=1;}});Console.WriteLine($"FEAT87 ONLY: {n} checks, {bad} failures");return;}
if(args.Contains("--feat86-only")){var n=0;var bad=0;Feat02286HeTests.Run((ok,t)=>{n++;Console.WriteLine((ok?"PASS ":"FAIL ")+t);if(!ok){bad++;Environment.ExitCode=1;}});Console.WriteLine($"FEAT86 ONLY: {n} checks, {bad} failures");return;}
if(args.Contains("--feat84-only")){var n=0;var bad=0;Feat02284SolidBandsTests.Run((ok,t)=>{n++;Console.WriteLine((ok?"PASS ":"FAIL ")+t);if(!ok){bad++;Environment.ExitCode=1;}});Console.WriteLine($"FEAT84 ONLY: {n} checks, {bad} failures");return;}
if(args.Contains("--feat83-only")){var n=0;var bad=0;Feat02283SolidOverlayTests.Run((ok,t)=>{n++;Console.WriteLine((ok?"PASS ":"FAIL ")+t);if(!ok){bad++;Environment.ExitCode=1;}});Console.WriteLine($"FEAT83 ONLY: {n} checks, {bad} failures");return;}
if(args.Contains("--feat82-only")){var n=0;var bad=0;Feat02282SolidBlockFullTests.Run((ok,t)=>{n++;Console.WriteLine((ok?"PASS ":"FAIL ")+t);if(!ok){bad++;Environment.ExitCode=1;}});Console.WriteLine($"FEAT82 ONLY: {n} checks, {bad} failures");return;}
if(args.Contains("--feat81-only")){var n=0;var bad=0;Feat02281SolidForensicTests.Run((ok,t)=>{n++;Console.WriteLine((ok?"PASS ":"FAIL ")+t);if(!ok){bad++;Environment.ExitCode=1;}});Console.WriteLine($"FEAT81 ONLY: {n} checks, {bad} failures");return;}
if(args.Contains("--feat80-only")){var n=0;var bad=0;Feat02280FollowFrontTests.Run((ok,t)=>{n++;Console.WriteLine((ok?"PASS ":"FAIL ")+t);if(!ok){bad++;Environment.ExitCode=1;}});Console.WriteLine($"FEAT80 ONLY: {n} checks, {bad} failures");return;}
if(args.Contains("--feat79-only")){var n=0;var bad=0;Feat02279SolidMarkTests.Run((ok,t)=>{n++;Console.WriteLine((ok?"PASS ":"FAIL ")+t);if(!ok){bad++;Environment.ExitCode=1;}});Console.WriteLine($"FEAT79 ONLY: {n} checks, {bad} failures");return;}
if(args.Contains("--feat78-only")){var n=0;var bad=0;Feat02278HeshTests.Run((ok,t)=>{n++;Console.WriteLine((ok?"PASS ":"FAIL ")+t);if(!ok){bad++;Environment.ExitCode=1;}});Console.WriteLine($"FEAT78 ONLY: {n} checks, {bad} failures");return;}
if(args.Contains("--feat77-only")){var n=0;var bad=0;Feat02277SolidBlockTests.Run((ok,t)=>{n++;Console.WriteLine((ok?"PASS ":"FAIL ")+t);if(!ok){bad++;Environment.ExitCode=1;}});Console.WriteLine($"FEAT77 ONLY: {n} checks, {bad} failures");return;}
if(args.Contains("--trace76")){Trace76.Run();return;}
if(args.Contains("--feat76-only")){Feat02276RobustTests.Run((ok,t)=>{Console.WriteLine((ok?"PASS ":"FAIL ")+t);if(!ok)Environment.ExitCode=1;},args.Contains("--before"),!args.Contains("--no-measure"));return;}
if(args.Contains("--review75-only")){Feat02275ReviewFixTests.Run((ok,t)=>{Console.WriteLine((ok?"PASS ":"FAIL ")+t);if(!ok)Environment.ExitCode=1;},args.Contains("--before")?"before":"after");return;}
if(args.Contains("--feat02275-only")){var n=0;Feat02275FollowWallTests.Run((ok,t)=>{n++;Console.WriteLine((ok?"PASS ":"FAIL ")+t);if(!ok)Environment.ExitCode=1;});Console.WriteLine($"FEAT75: {n} checks");return;}
if(args.Contains("--feat02268-only")){var failures=new List<string>();var n=0;try{ComposerFillBlueprintEvidence.RunRobust((ok,text)=>{n++;if(!ok&&failures.Count<80)failures.Add(text);});}catch(Exception error){Console.Error.WriteLine(error);Environment.ExitCode=1;}foreach(var f in failures)Console.Error.WriteLine("FAIL: "+f);if(failures.Count>0)Environment.ExitCode=1;Console.WriteLine($"FEAT68 TOTAL: {n} checks, {failures.Count} failures listed.");return;}
if(args.Contains("--feat02269-only")){var failures=new List<string>();var n=0;try{ComposerFillBlueprintEvidence.RunUniform((ok,text)=>{n++;if(!ok&&failures.Count<80)failures.Add(text);});}catch(Exception error){Console.Error.WriteLine(error);Environment.ExitCode=1;}foreach(var f in failures)Console.Error.WriteLine("FAIL: "+f);if(failures.Count>0)Environment.ExitCode=1;Console.WriteLine($"FEAT69 TOTAL: {n} checks, {failures.Count} failures listed.");return;}
if(args.Contains("--feat02271-only")){var failures=new List<string>();var n=0;try{ComposerFillBlueprintEvidence.RunRelayer((ok,text)=>{n++;if(!ok&&failures.Count<80)failures.Add(text);});}catch(Exception error){Console.Error.WriteLine(error);Environment.ExitCode=1;}foreach(var f in failures)Console.Error.WriteLine("FAIL: "+f);if(failures.Count>0)Environment.ExitCode=1;Console.WriteLine($"FEAT71 TOTAL: {n} checks, {failures.Count} failures listed.");return;}
if(args.Contains("--feat02267-only")){var failures=new List<string>();var n=0;try{ComposerFillBlueprintEvidence.RunPainted((ok,text)=>{n++;if(!ok&&failures.Count<80)failures.Add(text);});}catch(Exception error){Console.Error.WriteLine(error);Environment.ExitCode=1;}foreach(var f in failures)Console.Error.WriteLine("FAIL: "+f);if(failures.Count>0)Environment.ExitCode=1;Console.WriteLine($"FEAT67 TOTAL: {n} checks, {failures.Count} failures listed.");return;}
if(args.Contains("--feat02266-only")){var failures=new List<string>();var n=0;try{ComposerFillBlueprintEvidence.RunOpenBox((ok,text)=>{n++;if(!ok&&failures.Count<80)failures.Add(text);});}catch(Exception error){Console.Error.WriteLine(error);Environment.ExitCode=1;}foreach(var f in failures)Console.Error.WriteLine("FAIL: "+f);if(failures.Count>0)Environment.ExitCode=1;Console.WriteLine($"FEAT66 TOTAL: {n} checks, {failures.Count} failures listed.");return;}
if(args.Contains("--feat02265-only")){var failures=new List<string>();var n=0;try{ComposerFillBlueprintEvidence.RunNegativeTm((ok,text)=>{n++;if(!ok&&failures.Count<80)failures.Add(text);});}catch(Exception error){Console.Error.WriteLine(error);Environment.ExitCode=1;}foreach(var f in failures)Console.Error.WriteLine("FAIL: "+f);if(failures.Count>0)Environment.ExitCode=1;Console.WriteLine($"FEAT65 TOTAL: {n} checks, {failures.Count} failures listed.");return;}
if(args.Contains("--feat02264-only")){var failures=new List<string>();var n=0;try{ComposerFillBlueprintEvidence.RunLayerDirection((ok,text)=>{n++;if(!ok&&failures.Count<80)failures.Add(text);});}catch(Exception error){Console.Error.WriteLine(error);Environment.ExitCode=1;}foreach(var f in failures)Console.Error.WriteLine("FAIL: "+f);if(failures.Count>0)Environment.ExitCode=1;Console.WriteLine($"FEAT64 TOTAL: {n} checks, {failures.Count} failures listed.");return;}
if(args.Contains("--feat02263-only")){var failures=new List<string>();var n=0;try{ComposerFillBlueprintEvidence.RunLayerAccept((ok,text)=>{n++;if(!ok&&failures.Count<80)failures.Add(text);});}catch(Exception error){Console.Error.WriteLine(error);Environment.ExitCode=1;}foreach(var f in failures)Console.Error.WriteLine("FAIL: "+f);if(failures.Count>0)Environment.ExitCode=1;Console.WriteLine($"FEAT63 TOTAL: {n} checks, {failures.Count} failures listed.");return;}
if(args.Contains("--feat02262-only")){var failures=new List<string>();var n=0;try{ComposerFillBlueprintEvidence.RunLayerShape((ok,text)=>{n++;if(!ok&&failures.Count<80)failures.Add(text);});}catch(Exception error){Console.Error.WriteLine(error);Environment.ExitCode=1;}foreach(var f in failures)Console.Error.WriteLine("FAIL: "+f);if(failures.Count>0)Environment.ExitCode=1;Console.WriteLine($"FEAT62 TOTAL: {n} checks, {failures.Count} failures listed.");return;}
if(args.Contains("--feat02261-only")){var failures=new List<string>();var n=0;try{ComposerFillBlueprintEvidence.RunLayerBox((ok,text)=>{n++;if(!ok&&failures.Count<80)failures.Add(text);});}catch(Exception error){Console.Error.WriteLine(error);Environment.ExitCode=1;}foreach(var f in failures)Console.Error.WriteLine("FAIL: "+f);if(failures.Count>0)Environment.ExitCode=1;Console.WriteLine($"FEAT61 TOTAL: {n} checks, {failures.Count} failures listed.");return;}
if(args.Contains("--feat02260-only")){var failures=new List<string>();var n=0;try{ComposerFillBlueprintEvidence.RunLayerFaces((ok,text)=>{n++;if(!ok&&failures.Count<80)failures.Add(text);});}catch(Exception error){Console.Error.WriteLine(error);Environment.ExitCode=1;}foreach(var f in failures)Console.Error.WriteLine("FAIL: "+f);if(failures.Count>0)Environment.ExitCode=1;Console.WriteLine($"FEAT60 TOTAL: {n} checks, {failures.Count} failures listed.");return;}
if(args.Contains("--feat02259-only")){var failures=new List<string>();var n=0;try{ComposerFillBlueprintEvidence.RunShellDepth((ok,text)=>{n++;if(!ok&&failures.Count<80)failures.Add(text);});}catch(Exception error){Console.Error.WriteLine(error);Environment.ExitCode=1;}foreach(var f in failures)Console.Error.WriteLine("FAIL: "+f);if(failures.Count>0)Environment.ExitCode=1;Console.WriteLine($"FEAT59 TOTAL: {n} checks, {failures.Count} failures listed.");return;}
if(args.Contains("--feat02257-only")||args.Contains("--feat02258-only")){var failures=new List<string>();var n=0;try{ComposerFillBlueprintEvidence.RunShell((ok,text)=>{n++;if(!ok&&failures.Count<80)failures.Add(text);});}catch(Exception error){Console.Error.WriteLine(error);Environment.ExitCode=1;}foreach(var f in failures)Console.Error.WriteLine("FAIL: "+f);if(failures.Count>0)Environment.ExitCode=1;Console.WriteLine($"FEAT57 TOTAL: {n} checks, {failures.Count} failures listed.");return;}
if(args.Contains("--feat02256-only")){ComposerFillBlueprintEvidence.RunConform((ok,text)=>{if(!ok)throw new Exception(text);});return;}
if(args.Contains("--feat02255-only")){var n=0;ComposerFillBlueprintEvidence.RunCoverage((ok,text)=>{n++;if(!ok)throw new Exception(text);});Console.WriteLine($"FEAT55 TOTAL: {n} checks.");return;}
if(args.Contains("--feat02253-only")){try{var n=0;Feat02254ComposerFixTests.Run((ok,text)=>{n++;if(!ok)throw new Exception(text);});Feat02253ComposerFillTests.Run((ok,text)=>{n++;if(!ok)throw new Exception(text);});Console.WriteLine($"FEAT53 TOTAL: {n} checks.");}catch(Exception error){Console.Error.WriteLine(error);Environment.ExitCode=1;}return;}
if(args.Contains("--feat02272-only")){var n=0;Feat02272ModuleIconTests.Run((ok,text)=>{n++;if(!ok)throw new Exception(text);});Console.WriteLine($"FEAT72 TOTAL: {n} checks.");return;}
if(args.Contains("--feat02273-only")){var n=0;Feat02273WtVariantTests.Run((ok,text)=>{n++;if(!ok)throw new Exception(text);});Console.WriteLine($"FEAT73 TOTAL: {n} checks.");return;}
if(args.Contains("--feat02274-only")){var n=0;Feat02274OverlayApplyTests.Run((ok,text)=>{n++;if(!ok)throw new Exception(text);});Console.WriteLine($"FEAT74 TOTAL: {n} checks.");return;}
if(args.Contains("--feat02270-only")){var n=0;Feat02270HousingLookTests.Run((ok,text)=>{n++;if(!ok)throw new Exception(text);});Console.WriteLine($"FEAT70 TOTAL: {n} checks.");return;}
if(args.Contains("--feat02252-only")){var n=0;Feat02252SpentRenderTests.Run((ok,text)=>{n++;if(!ok)throw new Exception(text);});Console.WriteLine($"FEAT52 TOTAL: {n} checks.");return;}
if(args.Contains("--fix02251-only")){try{var n=0;Fix02251DuplicateRowTests.Run((ok,text)=>{n++;if(!ok)throw new Exception(text);});Fix02251EraPlayStabilityTests.Run((ok,text)=>{n++;if(!ok)throw new Exception(text);});Fix02251ChainReviewTests.Run((ok,text)=>{n++;if(!ok)throw new Exception(text);});Console.WriteLine($"FIX51 TOTAL: {n} checks.");}catch(Exception error){Console.Error.WriteLine(error);Environment.ExitCode=1;}return;}
if(args.Contains("--feat02250-only")){try{var n=0;Feat02250ApplyScaleTests.Run((ok,text)=>{n++;if(!ok)throw new Exception(text);});Console.WriteLine($"FEAT50 TOTAL: {n} checks.");}catch(Exception error){Console.Error.WriteLine(error);Environment.ExitCode=1;}return;}
if(args.Contains("--fix02249-only")){try{var n=0;Fix02249EraFingerprintTests.Run((ok,text)=>{n++;if(!ok)throw new Exception(text);});Console.WriteLine($"FIX49 TOTAL: {n} checks.");}catch(Exception error){Console.Error.WriteLine(error);Environment.ExitCode=1;}return;}
if(args.Contains("--fix02248-only")){try{var n=0;Fix02248ReadableIntervalTests.Run((ok,text)=>{n++;if(!ok)throw new Exception(text);});Console.WriteLine($"FIX48 TOTAL: {n} checks.");}catch(Exception error){Console.Error.WriteLine(error);Environment.ExitCode=1;}return;}
if(args.Contains("--fix02247-only")){try{var n=0;Fix02247IntervalTests.Run((ok,text)=>{n++;if(!ok)throw new Exception(text);});Console.WriteLine($"FIX47 TOTAL: {n} checks.");}catch(Exception error){Console.Error.WriteLine(error);Environment.ExitCode=1;}return;}

if(args.Contains("--feat02246-only")){try{var n=0;Feat02246CustomTextureTests.Run((ok,text)=>{n++;if(!ok)throw new Exception(text);});Console.WriteLine($"FEAT46 TOTAL: {n} checks.");}catch(Exception error){Console.Error.WriteLine(error);Environment.ExitCode=1;}return;}


if(args.Contains("--feat02245-only")){try{var n=0;Feat02245MaterialLookTests.Run((ok,text)=>{n++;if(!ok)throw new Exception(text);});Console.WriteLine($"FEAT45 TOTAL: {n} checks.");}catch(Exception error){Console.Error.WriteLine(error);Environment.ExitCode=1;}return;}
if(args.Contains("--feat02244-only")){try{var n=0;Feat02244MaterialsTests.Run((ok,text)=>{n++;if(!ok)throw new Exception(text);});Console.WriteLine($"FEAT44 TOTAL: {n} checks.");}catch(Exception error){Console.Error.WriteLine(error);Environment.ExitCode=1;}return;}
if(args.Contains("--fix02243-only")){try{var n=0;Fix02243Tests.Run((ok,text)=>{n++;if(!ok)throw new Exception(text);});Console.WriteLine($"FIX43 TOTAL: {n} checks.");}catch(Exception error){Console.Error.WriteLine(error);Environment.ExitCode=1;}return;}
if(args.Contains("--fix02242-only")){try{var n=0;Fix02242Tests.Run((ok,text)=>{n++;if(!ok)throw new Exception(text);});Console.WriteLine($"FIX42 TOTAL: {n} checks.");}catch(Exception error){Console.Error.WriteLine(error);Environment.ExitCode=1;}return;}
if(args.Contains("--fix02241-only")){try{var n=0;Fix02241Tests.Run((ok,text)=>{n++;if(!ok)throw new Exception(text);});Console.WriteLine($"FIX41 TOTAL: {n} checks.");}catch(Exception error){Console.Error.WriteLine(error);Environment.ExitCode=1;}return;}
if(args.Contains("--forensic-only")){try{var n=0;Feat02240ForensicTests.Run((ok,text)=>{n++;if(!ok)throw new Exception(text);});Console.WriteLine($"FORENSIC TOTAL: {n} checks.");}catch(Exception error){Console.Error.WriteLine(error);Environment.ExitCode=1;}return;}
if(args.Contains("--fix02238-only")){try{var n=0;Fix02238Tests.Run((ok,text)=>{n++;if(!ok)throw new Exception(text);});Console.WriteLine($"FIX38 PASS: {n} checks.");}catch(Exception error){Console.Error.WriteLine(error);Environment.ExitCode=1;}return;}
if(args.Contains("--module-only")){try{var n=0;Feat02237CompositeModuleTests.Run((ok,text)=>{n++;if(!ok)throw new Exception(text);});Console.WriteLine($"MODULE PASS: {n} checks.");}catch(Exception error){Console.Error.WriteLine(error);Environment.ExitCode=1;}return;}
if(args.Contains("--fix02236-only")){try{var n=0;Fix02236Tests.Run((ok,text)=>{n++;if(!ok)throw new Exception(text);});Console.WriteLine($"FIX36 PASS: {n} checks.");}catch(Exception error){Console.Error.WriteLine(error);Environment.ExitCode=1;}return;}
if(args.Contains("--fix02235-only")){try{var n=0;Fix02235Tests.Run((ok,text)=>{n++;if(!ok)throw new Exception(text);});Console.WriteLine($"FIX35 PASS: {n} checks.");}catch(Exception error){Console.Error.WriteLine(error);Environment.ExitCode=1;}return;}
if(args.Contains("--fix02234-only")){try{var n=0;Fix02234Tests.Run((ok,text)=>{n++;if(!ok)throw new Exception(text);});Console.WriteLine($"FIX34 PASS: {n} checks.");}catch(Exception error){Console.Error.WriteLine(error);Environment.ExitCode=1;}return;}
if(args.Contains("--fix02233-only")){try{var n=0;Fix02233Tests.Run((ok,text)=>{n++;if(!ok)throw new Exception(text);});Console.WriteLine($"FIX33 PASS: {n} checks.");}catch(Exception error){Console.Error.WriteLine(error);Environment.ExitCode=1;}return;}
if(args.Contains("--fix02232-only")){try{var n=0;Fix02232Tests.Run((ok,text)=>{n++;if(!ok)throw new Exception(text);});Console.WriteLine($"FIX32 PASS: {n} checks.");}catch(Exception error){Console.Error.WriteLine(error);Environment.ExitCode=1;}return;}
if(args.Length==2&&args[0]=="--verify-install"){Wave1Tests.VerifyInstall(args[1]);return;}
if(args.Length==3&&args[0]=="--verify-user-rha"){Wave1Tests.VerifyUserRha(args[1],args[2]);return;}
if(args.Length==2&&args[0]=="--verify-spall"){var parsed=SpallBalance.Parse(File.ReadAllText(args[1]));Console.WriteLine($"SPALL-OK minSpallGapMm={parsed.MinSpallGapMm} thinGapKillMm={parsed.FragmentRicochet?.ThinGapKillMm} grazing={parsed.FragmentRicochet?.GrazingDoubleDegrees} energyLoss={parsed.FragmentRicochet?.EnergyLoss} exponents apfsds/heat/hesh={parsed.ApfsdsObliquityExponent}/{parsed.HeatObliquityExponent}/{parsed.HeshObliquityExponent}");return;}
if(args.Contains("--fix02231-only")){var n=0;Fix02231Tests.Run((ok,text)=>{n++;if(!ok)throw new Exception(text);});Console.WriteLine($"FIX31 PASS: {n} checks.");return;}
if(args.Contains("--fix02230-only")){var n=0;Fix02230Tests.Run((ok,text)=>{n++;if(!ok)throw new Exception(text);});Console.WriteLine($"FIX30 PASS: {n} checks.");return;}
if(args.Contains("--fix02228-only")){var checks28=0;Fix02228Tests.Run((ok,text)=>{checks28++;if(!ok)throw new Exception(text);});Console.WriteLine($"R2 PASS: {checks28} checks.");return;}
if(args.Contains("--r2-tail")){var n=0;void CheckTail(bool ok,string text){n++;if(!ok)throw new Exception(text);}Fix02219Tests.Run(CheckTail);Fix02220Tests.Run(CheckTail);Fix02221Tests.Run(CheckTail);Fix02222Tests.Run(CheckTail);Fix02223Tests.Run(CheckTail);Fix02224Tests.Run(CheckTail);Fix02227Tests.Run(CheckTail);Fix02228Tests.Run(CheckTail);return;}
if(Fix02220Tests.ReportIfRequested())return;
try {

var checks = 0; var collectFailures=Environment.GetEnvironmentVariable("EB_COLLECT_FAILURES")=="1"; var collected=new List<string>(); void Check(bool condition, string message) { checks++; if (!condition) { if(collectFailures){collected.Add(message);return;} throw new Exception(message); } }
var defaults = new DartSettings();
var dart120 = ShellBallistics.Calculate(120, 800, 1800, defaults);
Check(Math.Abs(dart120.Diameter - .0264) < 1e-7, "120mm example subcalibre diameter");
Check(Math.Abs(dart120.Length - .6) < 1e-6, "120mm example rod length");
Check(dart120.Mass > 5.74 && dart120.Mass < 5.76, "120mm example cylinder mass");
Check(dart120.Velocity > 1480 && dart120.Velocity < 1490, "120mm example bounded velocity");
Check(dart120.PenetratorConstant > 1800, "quality below one must reduce penetration at equal geometry");
foreach (var calibre in new[] { 10d, 20d, 75d, 90d, 150d, 300d })
{
    var dart = ShellBallistics.Calculate(calibre, 800, 1800, defaults);
    var scale = calibre / 120;
    Check(Math.Abs(dart.Diameter / dart120.Diameter - scale) < 1e-6, "diameter scales with selected calibre");
    Check(Math.Abs(dart.Length / dart120.Length - scale) < 1e-6, "length scales with selected calibre");
    Check(Math.Abs(dart.Mass / dart120.Mass - scale * scale * scale) < 1e-4, "mass scales with volume");
    Check(Math.Abs(dart.Velocity - dart120.Velocity) < .001, "scale preserves uncapped mass-based velocity factor");
}
Check(ShellBallistics.Calculate(120, 2000, 1800, defaults).Velocity == 2200, "absolute speed cap");
Check(ShellBallistics.Calculate(120, 800, 1800, defaults with { MaxVelocityFactor = 1.2 }).Velocity == 960, "relative speed cap");
var heavier = ShellBallistics.Calculate(120, 800, 1800, defaults with { Density = 22000 });
Check(heavier.Mass > dart120.Mass && heavier.Velocity < dart120.Velocity, "heavier rod trades speed for mass");
Check(ShellBallistics.Calculate(120, 800, 1800, defaults with { PenetrationQuality = 1.5 }).PenetratorConstant < dart120.PenetratorConstant,
    "higher quality improves K");
Check(ShellBallistics.Calculate(120, 800, 1800, defaults with { DamageMultiplier = .2 }) with { DamageMultiplier = dart120.DamageMultiplier } == dart120,
    "damage tuning leaves ballistics unchanged");
void Reject(Action work, string message)
{
    var rejected = false;
    try { work(); } catch (ArgumentOutOfRangeException) { rejected = true; }
    Check(rejected, message);
}
foreach (var invalid in new[] { 0d, -1d, double.NaN, double.PositiveInfinity, 70000d })
    Reject(() => ShellBallistics.Calculate(invalid, 800, 1800, defaults), "reject invalid calibre");
foreach (var invalid in new[] { 0d, double.NaN, double.PositiveInfinity })
    Reject(() => ShellBallistics.Calculate(120, invalid, 1800, defaults), "reject invalid velocity");
Reject(() => ShellBallistics.Calculate(120, 800, 0, defaults), "reject zero K");
Reject(() => ShellBallistics.Calculate(120, 800, 1800, defaults with { DiameterRatio = double.NaN }), "reject NaN setting");
Reject(() => ShellBallistics.Calculate(120, 800, 1800, defaults with { Density = -1 }), "reject invalid density");
Reject(() => ShellBallistics.Calculate(120, 800, 1800, defaults with { DamageMultiplier = 2 }), "reject invalid damage scale");
var profileJson = ShellProfiles.CreateDefault(defaults);
var profileList = ShellProfiles.Parse(profileJson);
Check(profileList.Count == 2 && profileList[0].Id == "apfsds", "default profile ID");
Check(profileList[0].Settings == defaults, "JSON preserves complete default settings");
Check(ShellBallistics.Calculate(120, 800, 1800, profileList[0].Settings) == dart120, "profile migration preserves ballistics");
Check(ShellProfiles.Resolve(null, true, profileList) == "apfsds", "old selected save migrates");
Check(ShellProfiles.Resolve(null, false, profileList) == "vanilla", "old unselected save stays vanilla");
Check(ShellProfiles.Resolve("vanilla", true, profileList) == "vanilla", "new selection overrides legacy flag");
Check(ShellProfiles.Resolve("removed", true, profileList) == "vanilla", "missing ID falls back to vanilla");
Check(ShellProfiles.Resolve("apfsds", false, profileList) == "apfsds", "new custom selection overrides legacy flag");
var secondJson = profileJson.Replace("apfsds", "apds").Replace("APFSDS", "APDS");
var reordered = ShellProfiles.Parse(secondJson);
Check(ShellProfiles.Resolve("apds", false, reordered) == "apds", "selection is by stable ID");
var slower = defaults with { VelocityMultiplier = .8 };
Check(ShellBallistics.Calculate(120, 800, 1800, slower).Velocity < dart120.Velocity, "explicit velocity multiplier works");
Reject(() => ShellBallistics.Calculate(120, 800, 1800, defaults with { VelocityMultiplier = double.NaN }), "invalid velocity multiplier");
void BadJson(string json, string message)
{
    var rejected = false;
    try { ShellProfiles.Parse(json); }
    catch (Exception ex) when (ex is FormatException or System.Text.Json.JsonException or ArgumentOutOfRangeException or InvalidOperationException)
    { rejected = true; }
    Check(rejected, message);
}
BadJson(profileJson.Replace("\"schemaVersion\": 1", "\"schemaVersion\": 2"), "unsupported schema");
BadJson(profileJson.Replace("\"apfsds\"", "\"vanilla\""), "reserved ID");
BadJson(profileJson.Replace("\"apfsds\"", "\"AP FSDS\""), "invalid ID");
BadJson(profileJson.Replace("\"penetratorDensity\": 17500", "\"penetratorDensity\": -1"), "invalid profile density");
BadJson(profileJson.Replace("\"penetratorDensity\"", "\"densityTypo\""), "unknown field rejected");
BadJson(profileJson.Replace("\"penetratorDensity\": 17500,", ""), "missing field rejected");
BadJson(profileJson.Replace("\"penetratorDensity\": 17500,", "\"penetratorDensity\": 17500, \"penetratorDensity\": 17500,"), "duplicate field rejected");
var objectJson = System.Text.Json.JsonDocument.Parse(profileJson).RootElement.GetProperty("profiles")[0].GetRawText();
BadJson("{\"schemaVersion\":1,\"profiles\":[" + objectJson + "," + objectJson + "]}", "duplicate profile IDs rejected");
BadJson("{\"schemaVersion\":1,\"profiles\":[]}", "empty profile list rejected");
var twoProfiles = ShellProfiles.Parse("{\"schemaVersion\":1,\"profiles\":[" + objectJson + "," +
    objectJson.Replace("apfsds", "apds").Replace("APFSDS", "APDS") + "]}");
Check(twoProfiles.Count == 2 && ShellProfiles.Resolve("apds", false, twoProfiles) == "apds", "multiple profiles supported");
Console.WriteLine($"PASS: {checks} shell ballistics and profile checks.");

var temp = Path.Combine(SprocketExpandedBallistics.Fix02228Tests.FixtureRoot(), "expanded-ballistics-" + Guid.NewGuid());
Directory.CreateDirectory(temp);
try
{
    var dest = Path.Combine(temp, "new.json");
    var seeded = defaults with { Density = 22000 };
    ShellConfigMigration.EnsureProfileFile(dest, seeded);
    Check(ShellProfiles.Parse(File.ReadAllText(dest))[0].Settings.Density == 22000, "numeric CFG values seed the first profile file");
    var firstFile = File.ReadAllText(dest);
    ShellConfigMigration.EnsureProfileFile(dest, defaults);
    Check(File.ReadAllText(dest) == firstFile, "profile file is created once and never overwritten");
}
finally { Directory.Delete(temp, true); }
Console.WriteLine($"PASS total: {checks} shell checks including profile file creation.");
var spallSettings = new SpallSettings();
SpallBalance.Validate(spallSettings);
Check(SpallBalance.Parse(SpallBalance.ToJson(spallSettings)) == spallSettings, "complete spall JSON round trip");
Check(SpallBalance.ThicknessRatio(.15, spallSettings) == .08, "thin anchor 8 percent AP volume");
Check(SpallBalance.ThicknessRatio(.75, spallSettings) == 1, "middle anchor same AP volume");
Check(SpallBalance.ThicknessRatio(1.5, spallSettings) == 1.25, "thick anchor 125 percent AP volume");
Check(SpallBalance.ThicknessRatio(10, spallSettings) == 1.25, "bounded thick response");
Check(SpallBalance.ReferenceApVolume(.13,.13) == SpallBalance.ReferenceApVolume(.13,.26), "native AP spall fraction saturates");
Check(SpallBalance.ReferenceApVolume(.13,.004) == 0, "native AP below four percent calibre produces no spall");
Check(SpallBalance.ReferenceApVolume(.13,.1) * SpallBalance.ThicknessRatio(.1/.13,spallSettings) > SpallBalance.ReferenceApVolume(.13,.02) * SpallBalance.ThicknessRatio(.02/.13,spallSettings), "thin mass lower than thick mass");
var previousRatio=0d;
var monotonic=true;
for(var i=0;i<=1000;i++) { var ratio=SpallBalance.ThicknessRatio(i*.003,spallSettings); monotonic &= ratio>=previousRatio; previousRatio=ratio; }
Check(monotonic,"continuous thickness curve monotonic over full sweep");
foreach(var bad in new[] {spallSettings with {ConeMultiplier=0},spallSettings with {ThinCalibres=2},spallSettings with {ApheFragmentCount=257},spallSettings with {ApheFragmentMass=double.NaN},spallSettings with {ApheFragmentK=2000}})
{
    var invalid=false; try {SpallBalance.Validate(bad);} catch(FormatException){invalid=true;}
    Check(invalid,"invalid experimental spall setting rejected");
}
var unknown=false; try {SpallBalance.Parse(SpallBalance.ToJson(spallSettings).Replace("coneMultiplier","coneTypo"));} catch(FormatException){unknown=true;}
Check(unknown,"unknown spall field rejected");
var apheSettings=new DartSettings(.9,2,7800,.1,1,2200,.65,1);
var aphe=ShellBallistics.Calculate(130,861,1800,apheSettings);
var aphePen=Math.Pow(Math.Sqrt(aphe.Mass)*aphe.Velocity/(aphe.PenetratorConstant*Math.Pow(aphe.Diameter*1000*.01,.75)),1.43);
var apPen=Math.Pow(Math.Sqrt(1.59e-5*130*130*130)*861/(1800*Math.Pow(130*.01,.75)),1.43);
Check(aphePen<apPen,"APHE experiment lower native penetration than vanilla AP");
Check(aphe.Velocity==861,"APHE does not gain dart muzzle speed");
Console.WriteLine($"PASS total: {checks} shell, migration and spall checks.");
var sumX=0d;var sumY=0d;var sumZ=0d;var unitSphere=true;
for(var i=0;i<2048;i++) {
    var v=SpallBalance.Sphere((i*.6180339887498949)%1,(i+.5)/2048);
    unitSphere &= Math.Abs(v.X*v.X+v.Y*v.Y+v.Z*v.Z-1)<1e-6;
    sumX+=v.X;sumY+=v.Y;sumZ+=v.Z;
}
Check(unitSphere,"spherical fragments keep normalized directions");
Check(Math.Abs(sumX/2048)<.001 && Math.Abs(sumY/2048)<.001 && Math.Abs(sumZ/2048)<.001,"spherical distribution has no preferred hemisphere");
Check(SpallBalance.Sphere(0,0).Z==-1 && SpallBalance.Sphere(0,1).Z==1,"sphere includes forward and backward directions");
Console.WriteLine($"FINAL PASS: {checks} checks.");

var oldQuality=ShellBallistics.Calculate(125,890,1800,new DartSettings() with {PenetrationQuality=.75});
var newQuality=ShellBallistics.Calculate(125,890,1800,new DartSettings());
Check(newQuality with {PenetratorConstant=oldQuality.PenetratorConstant} == oldQuality,"quality adjustment changes only K, not geometry/mass/speed");
var relativePen=Math.Pow((double)oldQuality.PenetratorConstant/newQuality.PenetratorConstant,1.43);
Check(Math.Abs(relativePen-.6)<.001,"quality constant reduces native penetration to sixty percent across calibres");
var calibreInvariant=true;
foreach(var calibre in new[]{20d,75d,125d,200d}) {
 var oldK=ShellBallistics.Calculate(calibre,900,1800,new DartSettings() with{PenetrationQuality=.75}).PenetratorConstant;
 var newK=ShellBallistics.Calculate(calibre,900,1800,new DartSettings()).PenetratorConstant;
 calibreInvariant &= oldK==oldQuality.PenetratorConstant && newK==newQuality.PenetratorConstant;
}
Check(calibreInvariant,"balance constant has no special case for 125mm");
Console.WriteLine($"v0.3.0 PASS: {checks} checks.");
Check(SpallBalance.RemainingPenetrationRatio(1,spallSettings)==spallSettings.ThinRatio,"full remaining penetration produces minimum spall");
Check(SpallBalance.RemainingPenetrationRatio(0,spallSettings)==spallSettings.ThickRatio,"exhausted penetration produces maximum spall ratio");
Check(SpallBalance.RemainingPenetrationRatio(2,spallSettings)==spallSettings.ThinRatio,"remaining fraction above one is bounded");
Check(Math.Abs(SpallBalance.RemainingPenetrationRatio(.5,spallSettings)-.665)<1e-12,"half remaining penetration gives broad linear response");
var priorRemainingRatio=double.PositiveInfinity;
var remainingMonotonic=true;
for(var i=0;i<=1000;i++) {
 var ratio=SpallBalance.RemainingPenetrationRatio(i*.001,spallSettings);
 remainingMonotonic &= ratio<=priorRemainingRatio && ratio>=spallSettings.ThinRatio && ratio<=spallSettings.ThickRatio;
 priorRemainingRatio=ratio;
}
Check(remainingMonotonic,"spall decreases monotonically with remaining penetration across full sweep");
Check(SpallBalance.RemainingPenetrationRatio(.4,spallSettings with{ThinCalibres=.1,MiddleCalibres=1,ThickCalibres=2})==SpallBalance.RemainingPenetrationRatio(.4,spallSettings),"remaining model independent of legacy plate thickness anchors");
foreach(var invalidRemaining in new[]{double.NaN,double.PositiveInfinity,-.1}) {
 var rejected=false;try{SpallBalance.RemainingPenetrationRatio(invalidRemaining,spallSettings);}catch(ArgumentOutOfRangeException){rejected=true;}
 Check(rejected,"invalid remaining penetration rejected");
}
var oldSpallJson=SpallBalance.ToJson(spallSettings).Replace(",\n  \"remainingPenetrationExponent\": 1","");
Check(SpallBalance.Parse(oldSpallJson).RemainingPenetrationExponent==1,"existing spall configuration defaults to linear remaining penetration model");
Console.WriteLine($"v0.4.0 PASS: {checks} checks.");
Check(SpallBalance.ConeFactor(0,spallSettings)==.3,"exhausted kinetic energy retains base cone");
Check(Math.Abs(SpallBalance.ConeFactor(.5,spallSettings)-.3225)<1e-12,"half kinetic energy widens cone by seven point five percent");
Check(Math.Abs(SpallBalance.ConeFactor(1,spallSettings)-.345)<1e-12,"full kinetic energy widens cone by fifteen percent");
Check(SpallBalance.ConeFactor(2,spallSettings)==SpallBalance.ConeFactor(1,spallSettings),"cone widening capped at fifteen percent");
Console.WriteLine($"v0.5.0 PASS: {checks} checks.");
foreach(var invalidMultiplier in new[]{0d,13d,double.NaN}){
 var rejected=false;try{SpallBalance.Validate(spallSettings with{ApheSpallMultiplier=invalidMultiplier});}catch(FormatException){rejected=true;}
 Check(rejected,"invalid APHE native spall multiplier rejected");
}
Console.WriteLine($"v0.6.0 PASS: {checks} checks.");
foreach(var halfAngle in new[]{30d,60d,90d}){
 var bounded=true;var normalized=true;
 for(var i=0;i<2048;i++){
  var point=SpallBalance.Cone((i*.6180339887498949)%1,(i+.5)/2048,halfAngle);
  bounded &= point.Z>=Math.Cos(halfAngle*Math.PI/180)-1e-6 && point.Z<=1;
  normalized &= Math.Abs(point.X*point.X+point.Y*point.Y+point.Z*point.Z-1)<1e-6;
 }
 Check(bounded,"cone stays within requested forward half-angle");
 Check(normalized,"cone directions remain unit length");
}
Check(Math.Abs(SpallBalance.Cone(0,1,90).Z)<1e-6,"180 degree full cone reaches hemisphere boundary without backwards fragments");
foreach(var bad in new[]{new SpallSettings() with{ApheConeHalfAngleDegrees=91},new SpallSettings() with{ApheExplosionScale=0}}){
 var rejected=false;try{SpallBalance.Validate(bad);}catch(FormatException){rejected=true;}
 Check(rejected,"invalid experimental cone or effect scale rejected");
}
Console.WriteLine($"v0.7.0 EXPERIMENT PASS: {checks} checks.");
var incoming=new System.Numerics.Vector3(1,0,0);
var slopeNormal=System.Numerics.Vector3.Normalize(new System.Numerics.Vector3(1,1,0));
var downward=PlateDeflection.Bend(incoming,slopeNormal,8,30);
Check(downward.Y<0,"sloping plate bends shot downward without gravity hardcoding");
Check(System.Numerics.Vector3.Dot(downward,slopeNormal)>0,"deflected shot remains directed into plate");
Check(Math.Abs(downward.Length()-1)<1e-6,"deflection preserves unit direction and thus speed");
Check(System.Numerics.Vector3.Distance(downward,PlateDeflection.Bend(incoming,-slopeNormal,8,30))<1e-6,"plate-normal orientation does not change deflection");
Check(PlateDeflection.Bend(incoming,new(1,-1,0),8,30).Y>0,"mirrored slope produces mirrored bend");
Check(PlateDeflection.Bend(incoming,incoming,8,30)==incoming,"normal incidence stays straight");
Check(PlateDeflection.Bend(incoming,slopeNormal,0,30)==incoming,"zero maximum angle disables bend");
var turn=System.Numerics.Quaternion.CreateFromAxisAngle(System.Numerics.Vector3.UnitZ,1);
Check(System.Numerics.Vector3.Distance(System.Numerics.Vector3.Transform(downward,turn),PlateDeflection.Bend(System.Numerics.Vector3.Transform(incoming,turn),System.Numerics.Vector3.Transform(slopeNormal,turn),8,30))<1e-6,"deflection rotates with plate and incoming shot");
Console.WriteLine($"v0.8.0 EXPERIMENT PASS: {checks} checks.");
var repairDir = Path.Combine(SprocketExpandedBallistics.Fix02228Tests.FixtureRoot(), "shell-repair-"+Guid.NewGuid());
Directory.CreateDirectory(repairDir);
try {
 var path=Path.Combine(repairDir,"sprocket.expandedballistics.shells.json");
 var old="{\"schemaVersion\":1,\"profiles\":["+objectJson+"]}";
 File.WriteAllText(path,old);
 Check(ShellConfigMigration.EnsureApheProfile(path),"missing APHE repaired");
 Check(ShellProfiles.Parse(File.ReadAllText(path)).Count==2,"repair adds APHE");
 Check(ShellProfiles.Parse(File.ReadAllText(path))[0].Settings==defaults,"repair preserves APFSDS values");
 Check(File.ReadAllText(path+".pre-aphe-backup")==old,"repair backs up exact original");
 var repaired=File.ReadAllText(path);
 Check(!ShellConfigMigration.EnsureApheProfile(path)&&File.ReadAllText(path)==repaired,"repair is idempotent");
 File.WriteAllText(path,profileJson.Replace("\"penetrationQuality\": 0.65","\"penetrationQuality\": 0.7"));
 var custom=File.ReadAllText(path);
 Check(!ShellConfigMigration.EnsureApheProfile(path)&&File.ReadAllText(path)==custom,"existing APHE preserved byte for byte");
 File.WriteAllText(path,"{}");
 try {ShellConfigMigration.EnsureApheProfile(path);} catch(FormatException) {}
 Check(File.ReadAllText(path)=="{}","invalid configuration not overwritten");
} finally {Directory.Delete(repairDir,true);}
Console.WriteLine($"v0.8.2 PASS: {checks} checks.");
var copyNode=System.Text.Json.Nodes.JsonNode.Parse(profileJson)!;
copyNode["profiles"]![0]!["id"]="long_rod";
copyNode["profiles"]![0]!["label"]="Long rod";
copyNode["profiles"]![0]!["behavior"]="apfsds";
var copied=ShellProfiles.Parse(copyNode.ToJsonString())[0];
Check(copied.Behavior=="apfsds" && copied.Id=="long_rod","APFSDS behavior independent of id");
Check(profileList[0].Behavior=="apfsds" && profileList[1].Behavior=="aphe","old configurations retain dedicated behaviors");
copyNode["profiles"]![0]!["behavior"]="ap";
Check(ShellProfiles.Parse(copyNode.ToJsonString())[0].Behavior=="ap","explicit AP overrides legacy semantics");
foreach(var behavior in new[]{"heat","hesh"}){
 copyNode["profiles"]![0]!["behavior"]=behavior;
 copyNode["profiles"]![0]!["chemicalPenetrationMm"]=350;
 var p=ShellProfiles.Parse(copyNode.ToJsonString())[0];
 Check(p.Behavior==behavior && p.ChemicalPenetrationMm==350,"chemical payload parses");
}
copyNode["profiles"]![0]!["behavior"]="he";
copyNode["profiles"]![0]!["nativeExplosivePower"]=40;
Check(ShellProfiles.Parse(copyNode.ToJsonString())[0].NativeExplosivePower==40,"native HE power parses");
foreach(var invalid in new[]{copied with{Behavior="unknown"},copied with{Behavior="heat"},copied with{Behavior="he"},copied with{ChemicalPenetrationMm=2001},copied with{NativeExplosivePower=501},copied with{ExplosionScale=double.NaN},copied with{ConeHalfAngleDegrees=91}}){
 var rejected=false;try{ShellPayload.Validate(invalid);}catch(FormatException){rejected=true;}
 Check(rejected,"unsupported payload rejected");
}
Console.WriteLine($"v0.9.0 EXPERIMENT PASS: {checks} checks.");
if(args.Length==1){
 var presets=ShellProfiles.Parse(File.ReadAllText(args[0]));
 Check(presets.Count is 5 or 6 or 7 or 8 or 9,"preset file loads");
 Check(presets.Count(p=>p.Behavior=="apfsds")==2,"two APFSDS variants load");
 Check(presets.Any(p=>p.Behavior=="he")&&presets.Any(p=>p.Behavior=="heat")&&presets.Any(p=>p.Behavior=="hesh"),"all payload examples present");
 Console.WriteLine($"PRESETS PASS: {checks} checks.");
}
var heatPreset=copied with{Behavior="heat",ChemicalPenetrationMm=400,NativeExplosivePower=40};
Check(ShellBalance.ChemicalPenetration(heatPreset,75)==300,"HEAT scales penetration with calibre");
Check(ShellBalance.ChemicalPenetration(heatPreset,125)==500,"125mm chemical capacity");
Check(ShellBalance.BlastPower(heatPreset,100)==40 && Math.Abs(ShellBalance.BlastPower(heatPreset,200)-320)<1e-9,"HE power scales with calibre cubed");
Check(ShellBalance.BlastPower(heatPreset,500)==500,"HE power has upper bound");
Check(ShellBalance.FragmentTarget("heat",75)>=12,"75mm HEAT has useful fragment budget");
Check(ShellBalance.FragmentTarget("hesh",300)==32,"large HESH stays within native fragment cap");
Check(Math.Abs(ShellBalance.FragmentVolume("heat",200)/ShellBalance.FragmentVolume("heat",100)-8)<1e-9,"spall volume scales with calibre cubed");
var shortProfile=copied with{Settings=copied.Settings with{LengthInCalibres=3}};
var longProfile=copied with{Settings=copied.Settings with{LengthInCalibres=6}};
Check(ShellBalance.BallisticSettings(shortProfile).PenetrationQuality==copied.Settings.PenetrationQuality,"short rod has no extra length quality modifier");
Check(ShellBalance.BallisticSettings(longProfile).PenetrationQuality==copied.Settings.PenetrationQuality,"long rod has no duplicate length quality bonus");
Check(ShellBalance.RodCone(shortProfile.Settings)>ShellBalance.RodCone(longProfile.Settings),"short rod has wider cone than long rod");
Check(ShellBalance.BallisticSettings(profileList[1])==profileList[1].Settings,"APHE geometry not affected by rod modifier");
Check(ShellBalance.Visual(.65,125)<ShellBalance.Visual(1.5,125),"HE visual larger than APHE at equal calibre");
Check(ShellBalance.FragmentSpeed("heat",100)>ShellBalance.FragmentSpeed("hesh",100),"HEAT concentrated fast fragments HESH slower broad fragments");
Console.WriteLine($"v0.9.1 BALANCE PASS: {checks} checks.");
var layers=new ChemicalLayers();
Check(!layers.Observe(false),"initial air does not consume first plate");
Check(!layers.Observe(true),"first solid keeps initial budget");
Check(!layers.Observe(true),"contiguous solid does not count as spaced plate");
Check(!layers.Observe(false),"air gap arms degradation without changing air simulation");
Check(layers.Observe(true)&&layers.Degraded,"second solid after gap degrades budget");
Check(layers.Observe(true),"degraded budget persists through later solids");
Check(ShellBalance.SecondPlateBudget(heatPreset,100,350)==60,"HEAT second plate capped to 15 percent");
Check(ShellBalance.SecondPlateBudget(heatPreset,100,20)==20,"second plate never restores lost penetration");
Check(ShellBalance.SecondPlateBudget(heatPreset,200,700)==120,"second plate capacity scales with calibre");
Check(ShellBalance.FragmentTarget("aphe",75)>=21 && ShellBalance.FragmentTarget("hesh",100)==32,"stronger APHE HESH fragment budgets");
copyNode["profiles"]![0]!["behavior"]="heat";
copyNode["profiles"]![0]!["secondPlatePenetrationFactor"]=.2;
Check(ShellProfiles.Parse(copyNode.ToJsonString())[0].SecondPlatePenetrationFactor==.2,"second plate factor configurable");
foreach(var factor in new[]{0d,1.1,double.NaN}){
 var rejected=false;try{ShellPayload.Validate(heatPreset with{SecondPlatePenetrationFactor=factor});}catch(FormatException){rejected=true;}
 Check(rejected,"invalid layer factor rejected");
}
Console.WriteLine($"v0.9.2 LAYERS PASS: {checks} checks.");
foreach(var behavior in new[]{"aphe","hesh","heat"}){
 var target=ShellBalance.FragmentSpeed(behavior,100);
 Check(ShellBalance.PayloadFragmentSpeed(behavior,100,0)==target,"barely perforating payload retains fragment energy");
 Check(ShellBalance.PayloadFragmentSpeed(behavior,100,5000)==target*1.25,"payload fragment energy bounded");
 Check(ShellBalance.PayloadFragmentSpeed(behavior,100,double.NaN)==target,"invalid native speed has finite fallback");
 Check(ShellBalance.FragmentTarget(behavior,75)>=26,"75mm payload has dense burst");
}
Check(ShellBalance.FragmentVolume("hesh",100)>ShellBalance.FragmentVolume("aphe",100),"HESH heavier broad burst than APHE");
Check(ShellBalance.FragmentVolume("aphe",100)>ShellBalance.FragmentVolume("heat",100),"APHE broad damage HEAT concentrated damage");
Console.WriteLine($"v0.9.3 PAYLOAD PASS: {checks} checks.");
var releaseDefaults=ShellProfiles.Parse(ReleaseProfiles.Defaults());
Check(releaseDefaults.Count==21 && !releaseDefaults.Any(p=>p.Id=="apfsds"),"release retains the expanded kinetic, chemical and ATGM catalogue without standard dart"); // 0.21.1 adds seven dated profiles.
Check(releaseDefaults.Count(p=>p.Behavior=="apfsds")==4,"release includes four dated APFSDS generations"); // 0.21.1 APFSDS ladder.
Check(ShellProfiles.Resolve("apfsds",false,releaseDefaults)=="apfsds_long","old dart saves map to long rod");
Check(ShellProfiles.Resolve(null,true,releaseDefaults)=="apfsds_long","legacy selected flag maps to long rod");
var upgradeDir=Path.Combine(SprocketExpandedBallistics.Fix02228Tests.FixtureRoot(),"shell-release-test-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(upgradeDir);
try{
 var path=Path.Combine(upgradeDir,"profiles.json");File.WriteAllText(path,profileJson);
 Check(ReleaseProfiles.Upgrade(path),"old config upgraded");
 var upgraded=ShellProfiles.Parse(File.ReadAllText(path));
 Check(upgraded.Count==21 && !upgraded.Any(p=>p.Id=="apfsds"),"stock old dart removed while new presets added"); // 0.21.1 profile count.
 Check(upgraded.Single(p=>p.Id=="aphe").Settings==profileList[1].Settings,"existing APHE tuning preserved");
 Check(File.ReadAllText(path+".pre-v094-backup")==profileJson,"upgrade backs up exact config");
 Check(!ReleaseProfiles.Upgrade(path),"release upgrade idempotent");
 File.WriteAllText(path,profileJson.Replace("APFSDS","My custom dart"));ReleaseProfiles.Upgrade(path);
 Check(ShellProfiles.Parse(File.ReadAllText(path)).Any(p=>p.Id=="apfsds"),"named custom old dart retained");
 File.WriteAllText(path,"{}");try{ReleaseProfiles.Upgrade(path);}catch(Exception){}
 Check(File.ReadAllText(path)=="{}","invalid upgrade input preserved");
}finally{Directory.Delete(upgradeDir,true);}
Console.WriteLine($"v0.9.4 RELEASE PASS: {checks} checks.");
var heatGap=heatPreset with{AirGapLossPerCalibre=.35,SecondPlatePenetrationFactor=.15,StandoffFreeCalibres=2}; // 0.21.1 HEAT standoff onset.
var heshGap=heatGap with{Behavior="hesh",AirGapLossPerCalibre=12,SecondPlatePenetrationFactor=.1};
Check(ShellBalance.SpacedRetention(heatGap,100,10,0)==1,"no gap no added HEAT loss");
Check(ShellBalance.SpacedRetention(heatGap,100,10,5)==1,"tiny gap preserves HEAT jet budget");
Check(ShellBalance.SpacedRetention(heatGap,100,10,250)>.8,"thin skirt and modest gap cannot erase HEAT capacity");
Check(ShellBalance.SpacedRetention(heatGap,100,50,300)<ShellBalance.SpacedRetention(heatGap,100,5,300),"thicker front plate disrupts HEAT more");
Check(ShellBalance.SpacedRetention(heatGap,100,10,500)<ShellBalance.SpacedRetention(heatGap,100,10,250),"longer gap reduces HEAT retention");
Check(ShellBalance.SpacedRetention(heshGap,100,10,50)<.11,"separated HESH strongly decoupled");
Check(ShellBalance.SpacedRetention(heshGap,100,10,0)==1,"contiguous HESH has no added loss");
Check(ShellBalance.SpacedRetention(heshGap,100,10,5)<ShellBalance.SpacedRetention(heatGap,100,10,5),"HESH more sensitive to small real gap");
Check(Math.Abs(ShellBalance.SpacedRetention(heatGap,100,10,250)-ShellBalance.SpacedRetention(heatGap,200,20,500))<1e-10,"relative plate and gap scale with calibre");
Check(ShellBalance.SpacedRetention(heatGap with{AirGapLossPerCalibre=0},100,10,500)==1,"per-shell zero sensitivity disables additional loss");
var distanceLayers=new ChemicalLayers();
Check(distanceLayers.ObserveBlock(false,1000)==null,"initial free flight excluded");
Check(distanceLayers.ObserveBlock(true,4)==null,"first solid no degradation");
Check(distanceLayers.ObserveBlock(true,6)==null,"contiguous solid thickness accumulates");
Check(distanceLayers.ObserveBlock(false,20)==null && distanceLayers.ObserveBlock(false,30)==null,"gap segments accumulate without loss in air");
var measuredGap=distanceLayers.ObserveBlock(true,40);
Check(measuredGap is {} mg && mg.PlateRhaMm==10 && mg.GapMm==50,"next solid reports exact preceding plate and gap once");
Check(distanceLayers.ObserveBlock(true,5)==null,"same solid never repeats previous gap penalty");
distanceLayers.ObserveBlock(false,100);
Check(distanceLayers.ObserveBlock(true,5) is {} nextGap && nextGap.PlateRhaMm==45 && nextGap.GapMm==100,"later gap tracked independently");
foreach(var behavior in new[]{heatGap,heshGap}){
 double last=1;var bounded=true;
 for(var gap=0;gap<2000;gap++){var value=ShellBalance.SpacedRetention(behavior,100,20,gap);bounded &= value<=last && value>=behavior.SecondPlatePenetrationFactor && value<=1;last=value;}
 Check(bounded,"gap curve monotonic bounded and cannot restore penetration");
}
Console.WriteLine($"v0.9.6 DISTANCE PASS: {checks} checks.");
Check(!ShellBalance.SuppressPayloadBurst("heat",true,true),"HEAT original can spall again after a later perforation");
Check(ShellBalance.SuppressPayloadBurst("heat",false,true),"HEAT secondary fragments do not trigger repeated payload bursts");
Check(ShellBalance.SuppressPayloadBurst("hesh",true,true),"HESH retains single payload burst");
Check(ShellBalance.SuppressPayloadBurst("aphe",true,true),"APHE retains single payload burst");
Check(!ShellBalance.SuppressPayloadBurst("heat",true,false),"first HEAT burst allowed");
Console.WriteLine($"v0.9.7 MULTIPLATE PASS: {checks} checks.");

// A valid APFSDS entry must not be blamed for another behavior's invalid settings.
var earlyHeatNode = System.Text.Json.Nodes.JsonNode.Parse(objectJson)!;
earlyHeatNode["id"] = "early_heat";
earlyHeatNode["label"] = "Early HEAT";
earlyHeatNode["behavior"] = "heat";
earlyHeatNode["chemicalPenetrationMm"] = 250;
earlyHeatNode["maximumVelocityFactor"] = .75;
var mixedInvalidJson = "{\"schemaVersion\":1,\"profiles\":[" + objectJson + "," + earlyHeatNode.ToJsonString() + "]}";
var previousCulture = System.Globalization.CultureInfo.CurrentCulture;
try
{
 System.Globalization.CultureInfo.CurrentCulture = new System.Globalization.CultureInfo("nl-NL");
 try { ShellProfiles.Parse(mixedInvalidJson); Check(false,"invalid HEAT setting rejected"); }
 catch(ArgumentOutOfRangeException ex)
 {
  Check(ex.Message.StartsWith("Profile 'early_heat': maximumVelocityFactor is 0.75; expected 1–4"),"diagnostic identifies HEAT profile, JSON field, value and inclusive limits using invariant numbers");
  Check(ex.ParamName=="maximumVelocityFactor" && !ex.Message.Contains("APFSDS"),"shared validation no longer incorrectly names APFSDS");
 }
} finally { System.Globalization.CultureInfo.CurrentCulture = previousCulture; }
earlyHeatNode["maximumVelocityFactor"] = 1;
Check(ShellProfiles.Parse("{\"schemaVersion\":1,\"profiles\":["+objectJson+","+earlyHeatNode.ToJsonString()+"]}").Count==2,"minimum valid factor still accepts APFSDS and HEAT together");
earlyHeatNode["spallMultiplier"] = 13;
try { ShellProfiles.Parse("{\"schemaVersion\":1,\"profiles\":["+objectJson+","+earlyHeatNode.ToJsonString()+"]}"); Check(false,"invalid payload rejected"); }
catch(FormatException ex) { Check(ex.Message.Contains("Profile 'early_heat': spallMultiplier is 13; expected 1–12"),"payload diagnostic includes profile and field limits"); }
var diagnosticsDir=Path.Combine(SprocketExpandedBallistics.Fix02228Tests.FixtureRoot(),"shell-diagnostics-"+Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(diagnosticsDir);
try
{
 var path=Path.Combine(diagnosticsDir,"sprocket.expandedballistics.shells.json");
 File.WriteAllText(path,mixedInvalidJson);
 foreach(var migrate in new Func<string,bool>[] {ShellConfigMigration.EnsureApheProfile,ReleaseProfiles.Upgrade})
 {
  try {migrate(path);Check(false,"migration rejects invalid custom profile");}
  catch(ArgumentOutOfRangeException ex) {Check(ex.Message.Contains("Profile 'early_heat': maximumVelocityFactor is 0.75"),"migration preserves actionable parse error");}
  Check(File.ReadAllText(path)==mixedInvalidJson && Directory.GetFiles(diagnosticsDir).Length==1,"invalid custom config remains exact with no backups or temporary writes");
 }
} finally {Directory.Delete(diagnosticsDir,true);}
Console.WriteLine($"PROFILE DIAGNOSTICS PASS: {checks} checks.");

Check(!releaseDefaults.Any(p=>p.Id.StartsWith("atgm_feel_") || new[]{"atgm_konkurs_test","atgm_mclos_test","atgm_gun_saclos"}.Contains(p.Id)),"shipping catalogue excludes disposable comparison and redundant ATGMs");
Check(releaseDefaults.Single(p=>p.Id=="atgm_saclos_direct_v1").Label=="BGM-71C I-TOW","named GHPC variant BGM-71C I-TOW");
Check(releaseDefaults.Single(p=>p.Id=="atgm_tow_bgm71a").Label=="BGM-71A TOW","named GHPC variant BGM-71A TOW");
Check(releaseDefaults.Single(p=>p.Id=="atgm_mclos_manual_v1").Label=="9M14M Malyutka-M","named GHPC variant 9M14M Malyutka-M");
Check(releaseDefaults.Single(p=>p.Id=="atgm_konkurs_9m113").Label=="9M113 Konkurs","named GHPC variant 9M113 Konkurs");
var swingfire=releaseDefaults.Single(p=>p.Id=="atgm_swingfire");
Check(swingfire.Label=="Swingfire"&&swingfire.Atgm?.GuidanceMode=="sight"&&swingfire.Atgm.FlightSpeed==185&&swingfire.ChemicalPenetrationMm==800,"Swingfire SACLOS preset: 185 m/s, 800 mm");
Check(releaseDefaults.Single(p=>p.Id=="atgm_gun_hump_v1").Label=="9M112 Kobra","named GHPC variant 9M112 Kobra");
Check(releaseDefaults.Single(p=>p.Id=="atgm_kobra_9m112m").Label=="9M112M Kobra","named GHPC variant 9M112M Kobra");
var legacyAtgmJson="{\n  \"schemaVersion\": 1,\n  \"profiles\": [\n    {\n      \"id\": \"apfsds_long\",\n      \"label\": \"APFSDS long rod\",\n      \"penetratorDiameterFactor\": 0.2199999988079071,\n      \"penetratorLengthInCalibres\": 6,\n      \"penetratorDensity\": 17500,\n      \"velocityEfficiency\": 0.8500000238418579,\n      \"velocityMultiplier\": 1,\n      \"maximumVelocityFactor\": 2.200000047683716,\n      \"maximumVelocity\": 2200,\n      \"penetrationQuality\": 0.45,\n      \"fragmentDamageMultiplier\": 0.5,\n      \"behavior\": \"apfsds\"\n    },\n    {\n      \"id\": \"apfsds_short\",\n      \"label\": \"APFSDS short rod\",\n      \"penetratorDiameterFactor\": 0.2199999988079071,\n      \"penetratorLengthInCalibres\": 3,\n      \"penetratorDensity\": 17500,\n      \"velocityEfficiency\": 0.8500000238418579,\n      \"velocityMultiplier\": 1,\n      \"maximumVelocityFactor\": 2.200000047683716,\n      \"maximumVelocity\": 2200,\n      \"penetrationQuality\": 0.45,\n      \"fragmentDamageMultiplier\": 0.5,\n      \"behavior\": \"apfsds\"\n    },\n    {\n      \"id\": \"he\",\n      \"label\": \"HE\",\n      \"penetratorDiameterFactor\": 0.9,\n      \"penetratorLengthInCalibres\": 2,\n      \"penetratorDensity\": 1800,\n      \"velocityEfficiency\": 0.1,\n      \"velocityMultiplier\": 1,\n      \"maximumVelocityFactor\": 1,\n      \"maximumVelocity\": 600,\n      \"penetrationQuality\": 0.45,\n      \"fragmentDamageMultiplier\": 1,\n      \"behavior\": \"he\",\n      \"explosionScale\": 1.5,\n      \"nativeExplosivePower\": 40\n    },\n    {\n      \"id\": \"heat\",\n      \"label\": \"HEAT\",\n      \"penetratorDiameterFactor\": 0.9,\n      \"penetratorLengthInCalibres\": 2,\n      \"penetratorDensity\": 1800,\n      \"velocityEfficiency\": 0.1,\n      \"velocityMultiplier\": 1,\n      \"maximumVelocityFactor\": 1,\n      \"maximumVelocity\": 600,\n      \"penetrationQuality\": 0.45,\n      \"fragmentDamageMultiplier\": 1,\n      \"behavior\": \"heat\",\n      \"explosionScale\": 0.7,\n      \"chemicalPenetrationMm\": 400,\n      \"spallMultiplier\": 1.5,\n      \"coneHalfAngleDegrees\": 8,\n      \"secondPlatePenetrationFactor\": 0.15\n    },\n    {\n      \"id\": \"hesh\",\n      \"label\": \"HESH\",\n      \"penetratorDiameterFactor\": 0.9,\n      \"penetratorLengthInCalibres\": 2,\n      \"penetratorDensity\": 1800,\n      \"velocityEfficiency\": 0.1,\n      \"velocityMultiplier\": 1,\n      \"maximumVelocityFactor\": 1,\n      \"maximumVelocity\": 600,\n      \"penetrationQuality\": 0.45,\n      \"fragmentDamageMultiplier\": 1,\n      \"behavior\": \"hesh\",\n      \"explosionScale\": 0.65,\n      \"chemicalPenetrationMm\": 300,\n      \"spallMultiplier\": 8,\n      \"coneHalfAngleDegrees\": 70,\n      \"secondPlatePenetrationFactor\": 0.1\n    },\n    {\n      \"id\": \"aphe\",\n      \"label\": \"APHE (enhanced spall)\",\n      \"behavior\": \"aphe\",\n      \"penetratorDiameterFactor\": 0.9,\n      \"penetratorLengthInCalibres\": 2,\n      \"penetratorDensity\": 7800,\n      \"velocityEfficiency\": 0.1,\n      \"velocityMultiplier\": 1,\n      \"maximumVelocityFactor\": 1,\n      \"maximumVelocity\": 2200,\n      \"penetrationQuality\": 0.65,\n      \"fragmentDamageMultiplier\": 1\n    },\n    {\n      \"id\": \"atgm_konkurs_test\",\n      \"label\": \"SACLOS ATGM\",\n      \"behavior\": \"atgm\",\n      \"penetratorDiameterFactor\": 0.9,\n      \"penetratorLengthInCalibres\": 5,\n      \"penetratorDensity\": 1800,\n      \"velocityEfficiency\": 0.1,\n      \"velocityMultiplier\": 1,\n      \"maximumVelocityFactor\": 1,\n      \"maximumVelocity\": 200,\n      \"penetrationQuality\": 0.45,\n      \"fragmentDamageMultiplier\": 1,\n      \"referenceCalibreMm\": 135,\n      \"chemicalPenetrationMm\": 600,\n      \"spallMultiplier\": 1.5,\n      \"coneHalfAngleDegrees\": 8,\n      \"explosionScale\": 0.7,\n      \"secondPlatePenetrationFactor\": 0.15,\n      \"airGapLossPerCalibre\": 0.35,\n      \"flightSpeed\": 200,\n      \"maxTurnRate\": 20,\n      \"maximumFlightTime\": 25,\n      \"guidanceDelay\": 0.25,\n      \"guidanceMode\": \"sight\",\n      \"launchSpeed\": 50,\n      \"launchSpeedMode\": \"fixed\",\n      \"launchSpeedMultiplier\": 1,\n      \"acceleration\": 150,\n      \"motorDelay\": 0.12,\n      \"motorBurnTime\": 4,\n      \"coastDeceleration\": 1\n    },\n    {\n      \"id\": \"atgm_mclos_test\",\n      \"label\": \"MCLOS ATGM\",\n      \"behavior\": \"atgm\",\n      \"penetratorDiameterFactor\": 0.9,\n      \"penetratorLengthInCalibres\": 5,\n      \"penetratorDensity\": 1800,\n      \"velocityEfficiency\": 0.1,\n      \"velocityMultiplier\": 1,\n      \"maximumVelocityFactor\": 1,\n      \"maximumVelocity\": 200,\n      \"penetrationQuality\": 0.45,\n      \"fragmentDamageMultiplier\": 1,\n      \"referenceCalibreMm\": 135,\n      \"chemicalPenetrationMm\": 600,\n      \"spallMultiplier\": 1.5,\n      \"coneHalfAngleDegrees\": 8,\n      \"explosionScale\": 0.7,\n      \"secondPlatePenetrationFactor\": 0.15,\n      \"airGapLossPerCalibre\": 0.35,\n      \"flightSpeed\": 200,\n      \"maxTurnRate\": 20,\n      \"maximumFlightTime\": 25,\n      \"guidanceDelay\": 0.25,\n      \"guidanceMode\": \"keyboard\",\n      \"launchSpeed\": 50,\n      \"launchSpeedMode\": \"fixed\",\n      \"launchSpeedMultiplier\": 1,\n      \"acceleration\": 150,\n      \"motorDelay\": 0.12,\n      \"motorBurnTime\": 4,\n      \"coastDeceleration\": 1\n    },\n    {\n      \"id\": \"atgm_topattack_test\",\n      \"label\": \"Top-attack ATGM\",\n      \"behavior\": \"atgm\",\n      \"penetratorDiameterFactor\": 0.9,\n      \"penetratorLengthInCalibres\": 5,\n      \"penetratorDensity\": 1800,\n      \"velocityEfficiency\": 0.1,\n      \"velocityMultiplier\": 1,\n      \"maximumVelocityFactor\": 1,\n      \"maximumVelocity\": 200,\n      \"penetrationQuality\": 0.45,\n      \"fragmentDamageMultiplier\": 1,\n      \"referenceCalibreMm\": 135,\n      \"chemicalPenetrationMm\": 600,\n      \"spallMultiplier\": 1.5,\n      \"coneHalfAngleDegrees\": 8,\n      \"explosionScale\": 0.7,\n      \"secondPlatePenetrationFactor\": 0.15,\n      \"airGapLossPerCalibre\": 0.35,\n      \"flightSpeed\": 200,\n      \"maxTurnRate\": 90,\n      \"maximumFlightTime\": 25,\n      \"guidanceDelay\": 0.25,\n      \"guidanceMode\": \"topattack\",\n      \"launchSpeed\": 50,\n      \"launchSpeedMode\": \"fixed\",\n      \"launchSpeedMultiplier\": 1,\n      \"acceleration\": 150,\n      \"motorDelay\": 0.12,\n      \"motorBurnTime\": 4,\n      \"coastDeceleration\": 1,\n      \"topAttackAltitude\": 90,\n      \"topAttackClimbAngle\": 45,\n      \"topAttackDiveAngle\": 70\n    },\n    {\n      \"id\": \"atgm_gun_saclos\",\n      \"label\": \"Gun-launched SACLOS ATGM\",\n      \"behavior\": \"atgm_gun\",\n      \"penetratorDiameterFactor\": 0.9,\n      \"penetratorLengthInCalibres\": 5,\n      \"penetratorDensity\": 1800,\n      \"velocityEfficiency\": 0.1,\n      \"velocityMultiplier\": 1,\n      \"maximumVelocityFactor\": 1,\n      \"maximumVelocity\": 200,\n      \"penetrationQuality\": 0.45,\n      \"fragmentDamageMultiplier\": 1,\n      \"referenceCalibreMm\": 135,\n      \"chemicalPenetrationMm\": 600,\n      \"spallMultiplier\": 1.5,\n      \"coneHalfAngleDegrees\": 8,\n      \"explosionScale\": 0.7,\n      \"secondPlatePenetrationFactor\": 0.15,\n      \"airGapLossPerCalibre\": 0.35,\n      \"flightSpeed\": 200,\n      \"maxTurnRate\": 20,\n      \"maximumFlightTime\": 25,\n      \"guidanceDelay\": 0.25,\n      \"guidanceMode\": \"sight\",\n      \"launchSpeed\": 50,\n      \"launchSpeedMode\": \"cannon\",\n      \"launchSpeedMultiplier\": 0.2,\n      \"acceleration\": 150,\n      \"motorDelay\": 0.08,\n      \"motorBurnTime\": 4,\n      \"coastDeceleration\": 1\n    },\n    {\n      \"id\": \"atgm_saclos_direct_v1\",\n      \"label\": \"SACLOS ATGM (LOS lead)\",\n      \"behavior\": \"atgm\",\n      \"penetratorDiameterFactor\": 0.9,\n      \"penetratorLengthInCalibres\": 5,\n      \"penetratorDensity\": 1800,\n      \"velocityEfficiency\": 0.1,\n      \"velocityMultiplier\": 1,\n      \"maximumVelocityFactor\": 1,\n      \"maximumVelocity\": 200,\n      \"penetrationQuality\": 0.45,\n      \"fragmentDamageMultiplier\": 1,\n      \"referenceCalibreMm\": 135,\n      \"chemicalPenetrationMm\": 600,\n      \"spallMultiplier\": 1.5,\n      \"coneHalfAngleDegrees\": 8,\n      \"explosionScale\": 0.7,\n      \"secondPlatePenetrationFactor\": 0.15,\n      \"airGapLossPerCalibre\": 0.35,\n      \"flightSpeed\": 278,\n      \"maxTurnRate\": 6.87549354,\n      \"maximumFlightTime\": 13.6,\n      \"guidanceDelay\": 0,\n      \"guidanceMode\": \"sight\",\n      \"launchSpeed\": 50,\n      \"launchSpeedMode\": \"fixed\",\n      \"launchSpeedMultiplier\": 1,\n      \"acceleration\": 180,\n      \"motorDelay\": 0.1,\n      \"motorBurnTime\": 4,\n      \"coastDeceleration\": 0,\n      \"guidanceLaw\": \"losLead\",\n      \"leadDistanceM\": 30,\n      \"rangePattern\": \"none\",\n      \"spiralAmplitudeM\": 0.15,\n      \"spiralFrequencyHz\": 1.2,\n      \"disturbanceAmplitudeM\": 0.1,\n      \"disturbanceFrequencyHz\": 0.7,\n      \"speedCalibration\": \"motorSmooth\"\n    },\n    {\n      \"id\": \"atgm_mclos_manual_v1\",\n      \"label\": \"MCLOS ATGM (manual)\",\n      \"behavior\": \"atgm\",\n      \"penetratorDiameterFactor\": 0.9,\n      \"penetratorLengthInCalibres\": 5,\n      \"penetratorDensity\": 1800,\n      \"velocityEfficiency\": 0.1,\n      \"velocityMultiplier\": 1,\n      \"maximumVelocityFactor\": 1,\n      \"maximumVelocity\": 200,\n      \"penetrationQuality\": 0.45,\n      \"fragmentDamageMultiplier\": 1,\n      \"referenceCalibreMm\": 135,\n      \"chemicalPenetrationMm\": 600,\n      \"spallMultiplier\": 1.5,\n      \"coneHalfAngleDegrees\": 8,\n      \"explosionScale\": 0.7,\n      \"secondPlatePenetrationFactor\": 0.15,\n      \"airGapLossPerCalibre\": 0.35,\n      \"flightSpeed\": 115,\n      \"maxTurnRate\": 11.4591559,\n      \"maximumFlightTime\": 26.2,\n      \"guidanceDelay\": 0,\n      \"guidanceMode\": \"keyboard\",\n      \"launchSpeed\": 50,\n      \"launchSpeedMode\": \"fixed\",\n      \"launchSpeedMultiplier\": 1,\n      \"acceleration\": 140,\n      \"motorDelay\": 0.12,\n      \"motorBurnTime\": 4,\n      \"coastDeceleration\": 0,\n      \"guidanceLaw\": \"losLead\",\n      \"leadDistanceM\": 30,\n      \"spiralAmplitudeM\": 0.08,\n      \"spiralFrequencyHz\": 0.9,\n      \"disturbanceAmplitudeM\": 0.06,\n      \"disturbanceFrequencyHz\": 0.6,\n      \"speedCalibration\": \"motorSmooth\"\n    },\n    {\n      \"id\": \"atgm_gun_hump_v1\",\n      \"label\": \"Gun-launched SACLOS ATGM (hump)\",\n      \"behavior\": \"atgm_gun\",\n      \"penetratorDiameterFactor\": 0.9,\n      \"penetratorLengthInCalibres\": 5,\n      \"penetratorDensity\": 1800,\n      \"velocityEfficiency\": 0.1,\n      \"velocityMultiplier\": 1,\n      \"maximumVelocityFactor\": 1,\n      \"maximumVelocity\": 200,\n      \"penetrationQuality\": 0.45,\n      \"fragmentDamageMultiplier\": 1,\n      \"referenceCalibreMm\": 135,\n      \"chemicalPenetrationMm\": 600,\n      \"spallMultiplier\": 1.5,\n      \"coneHalfAngleDegrees\": 8,\n      \"explosionScale\": 0.7,\n      \"secondPlatePenetrationFactor\": 0.15,\n      \"airGapLossPerCalibre\": 0.35,\n      \"flightSpeed\": 375,\n      \"maxTurnRate\": 5.72957795,\n      \"maximumFlightTime\": 10.8,\n      \"guidanceDelay\": 1,\n      \"guidanceMode\": \"sight\",\n      \"launchSpeed\": 50,\n      \"launchSpeedMode\": \"cannon\",\n      \"launchSpeedMultiplier\": 0.2,\n      \"acceleration\": 150,\n      \"motorDelay\": 0.08,\n      \"motorBurnTime\": 4,\n      \"coastDeceleration\": 0,\n      \"guidanceLaw\": \"losLead\",\n      \"leadDistanceM\": 200,\n      \"rangePattern\": \"hump\",\n      \"humpHeightM\": 4,\n      \"humpRangeMinM\": 1920,\n      \"humpRangeMaxM\": 4000,\n      \"humpTerminalDistanceM\": 700,\n      \"humpRampDistanceM\": 100,\n      \"directRangeBelowM\": 1000,\n      \"shortRangeThresholdM\": 1000,\n      \"shortRangeGuidanceDelay\": 0,\n      \"spiralAmplitudeM\": 0.06,\n      \"spiralFrequencyHz\": 1.2,\n      \"disturbanceAmplitudeM\": 0.05,\n      \"disturbanceFrequencyHz\": 0.9,\n      \"speedCalibration\": \"motorSmooth\",\n      \"initialElevationDegrees\": 0.5\n    }\n  ]\n}\n";
var legacyAtgmProfiles=ShellProfiles.Parse(legacyAtgmJson);
var konkurs=legacyAtgmProfiles.Single(p=>p.Id=="atgm_konkurs_test");
Check(konkurs.Behavior=="atgm" && konkurs.Atgm is {FlightSpeed:200,GuidanceMode:"sight"},"Konkurs-like example enables experimental sight guidance");
Check(Math.Abs(ShellBalance.ChemicalPenetration(konkurs,135)-600)<1e-9,"ATGM has 600mm budget at 135mm reference calibre");
Check(Math.Abs(ShellBalance.ChemicalPenetration(konkurs,90)-400)<1e-9,"ATGM remains selectable and chemical penetration scales with calibre");
Check(ShellBalance.Calculate(135,800,1800,konkurs).Velocity==50 && ShellBalance.Calculate(90,100,1800,konkurs).Velocity==50,"ATGM flight speed is independent of calibre and cannon charge");
Check(ShellBalance.ImpactBehavior(konkurs)=="heat","ATGM reuses HEAT impact mechanics without changing its flight behavior");
Check(Math.Abs(ShellBalance.SpacedRetention(konkurs,135,20,250)-ShellBalance.SpacedRetention(konkurs with {Behavior="heat"},135,20,250))<1e-12,"ATGM uses same HEAT air-gap curve");
var flightSettings=konkurs.Atgm!;
var forward=System.Numerics.Vector3.UnitZ;
var offAxis=System.Numerics.Vector3.Normalize(new System.Numerics.Vector3(1,0,1));
var guided=AtgmGuidance.Step(forward,offAxis,flightSettings,.02);
var missileTurn=Math.Acos(Math.Clamp(System.Numerics.Vector3.Dot(forward,System.Numerics.Vector3.Normalize(guided)),-1,1))*180/Math.PI;
Check(Math.Abs(guided.Length()-200)<.001 && missileTurn<=.405 && missileTurn>.39,"guidance preserves commanded speed and limits turning to .4 degrees per 20ms tick");
var leftGuided=AtgmGuidance.Step(forward,new(-1,0,1),flightSettings,.02);
Check(Math.Abs(leftGuided.X+guided.X)<1e-5 && Math.Abs(leftGuided.Z-guided.Z)<1e-5,"guidance has mirrored left/right behavior");
var upGuided=AtgmGuidance.Step(forward,new(0,1,1),flightSettings,.02);
Check(upGuided.Y>0 && Math.Abs(upGuided.Y-guided.X)<1e-5,"guidance turns toward upward aim in correct axis");
Check(AtgmGuidance.Step(forward,null,flightSettings,.02)==forward*200,"lost scope continues straight powered flight");
Check(AtgmGuidance.Step(forward,-forward,flightSettings,.02)==forward*200,"passed aim point cannot make missile U-turn");
Check(AtgmGuidance.Step(forward,offAxis,flightSettings with {GuidanceMode="none"},.02)==forward*200,"unguided custom missile mode remains straight");
Check(AtgmGuidance.Step(forward,offAxis,flightSettings with {MaxTurnRate=0},.02)==forward*200,"zero turn rate prevents steering");
var smallAngle=System.Numerics.Vector3.Normalize(new System.Numerics.Vector3(.001f,0,1));
Check(System.Numerics.Vector3.Distance(AtgmGuidance.Step(forward,smallAngle,flightSettings,.02),smallAngle*200)<.001,"small corrections reach aim without overshoot");
var oneSecond=forward;for(var i=0;i<50;i++)oneSecond=System.Numerics.Vector3.Normalize(AtgmGuidance.Step(oneSecond,offAxis,flightSettings,.02));
Check(System.Numerics.Vector3.Distance(oneSecond,System.Numerics.Vector3.Normalize(AtgmGuidance.Step(forward,offAxis,flightSettings,1)))<.001,"turn integration is stable across timestep subdivision");
foreach(var badFlight in new[]{flightSettings with{FlightSpeed=double.NaN},flightSettings with{MaxTurnRate=-1},flightSettings with{MaximumFlightTime=0},flightSettings with{GuidanceDelay=30},flightSettings with{GuidanceMode="lock"}})
{
 var rejected=false;try{ShellPayload.Validate(konkurs with{Atgm=badFlight});}catch(FormatException ex){rejected=ex.Message.Contains("atgm_konkurs_test");}
 Check(rejected,"invalid ATGM settings rejected with profile context");
}
Check(ShellProfiles.Parse(legacyAtgmJson).Single(p=>p.Id=="atgm_konkurs_test")==konkurs,"ATGM optional fields survive JSON parsing");
var upgradeAtgmDir=Path.Combine(SprocketExpandedBallistics.Fix02228Tests.FixtureRoot(),"atgm-upgrade-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(upgradeAtgmDir);
try
{
 var originalNode=System.Text.Json.Nodes.JsonNode.Parse(ReleaseProfiles.Defaults())!;
 var entries=originalNode["profiles"]!.AsArray();entries.Remove(entries.Single(p=>p!["id"]!.GetValue<string>()=="atgm_konkurs_9m113"));
 entries.Remove(entries.Single(p=>p!["id"]!.GetValue<string>()=="atgm_mclos_manual_v1"));
 entries.Single(p=>p!["id"]!.GetValue<string>()=="heat")!["chemicalPenetrationMm"]=555;
 var path=Path.Combine(upgradeAtgmDir,"shells.json");var original=originalNode.ToJsonString();File.WriteAllText(path,original);
 Check(ReleaseProfiles.Upgrade(path),"existing v0.9.8 config gains ATGM example");
 var upgraded=ShellProfiles.Parse(File.ReadAllText(path));
 Check(upgraded.Count==21 && upgraded.Single(p=>p.Id=="heat").ChemicalPenetrationMm==555,"ATGM migration preserves user HEAT tuning"); // 0.21.1 profile count.
 Check(File.ReadAllText(path+".pre-v094-backup")==original && !ReleaseProfiles.Upgrade(path),"ATGM migration backs up exact input and is idempotent");
}finally{Directory.Delete(upgradeAtgmDir,true);}
Console.WriteLine($"ATGM TEST 1 PASS: {checks} checks.");
var distantMissile=new System.Numerics.Vector3(0,0,1500);
var beamTarget=AtgmGuidance.AimAlongRay(System.Numerics.Vector3.Zero,forward,distantMissile,200);
Check(beamTarget==new System.Numerics.Vector3(0,0,1700),"sight-ray guidance remains forward beyond fixed scope convergence distance");
var movingOrigin=new System.Numerics.Vector3(100,0,0);
Check(AtgmGuidance.AimAlongRay(movingOrigin,forward,new(120,0,1000),200)==new System.Numerics.Vector3(100,0,1200),"beam aim respects launcher translation and corrects lateral error");
foreach(var existing in releaseDefaults.Where(p=>!ShellBalance.IsAtgm(p.Behavior)&&p.Behavior!="apfsds"))
 Check(ShellBalance.Calculate(135,800,1800,existing)==ShellBallistics.Calculate(135,800,1800,ShellBalance.BallisticSettings(existing)),"existing profile ballistics are unchanged by ATGM launch path");
Console.WriteLine($"ATGM FINAL PASS: {checks} checks.");
Check(AtgmGuidance.FreshAim(10,9.9),"recent player aim remains usable during reload");
Check(!AtgmGuidance.FreshAim(10,9.5) && !AtgmGuidance.FreshAim(10,11),"stale and future aim samples rejected");
Check(!AtgmGuidance.FreshAim(double.NaN,10),"invalid player clock cannot activate guidance");
Console.WriteLine($"ATGM TEST 2 PASS: {checks} checks.");

var keyboardProfile=legacyAtgmProfiles.Single(p=>p.Id=="atgm_mclos_test");
var keyboardSettings=keyboardProfile.Atgm!;
Check(keyboardSettings.GuidanceMode=="keyboard" && flightSettings.GuidanceMode=="sight", "manual and sight guidance are separate presets");
var kr=AtgmGuidance.KeyboardStep(forward,1,0,keyboardSettings,.02);
var kl=AtgmGuidance.KeyboardStep(forward,-1,0,keyboardSettings,.02);
var ku=AtgmGuidance.KeyboardStep(forward,0,1,keyboardSettings,.02);
var kd=AtgmGuidance.KeyboardStep(forward,0,-1,keyboardSettings,.02);
Check(kr.X>0 && kl.X<0 && ku.Y>0 && kd.Y<0,"MCLOS keys turn right/left/up/down correctly");
Check(Math.Abs(kr.X+kl.X)<1e-5 && Math.Abs(ku.Y+kd.Y)<1e-5,"MCLOS opposite commands are symmetric");
Check(AtgmGuidance.KeyboardStep(forward,0,0,keyboardSettings,.02)==forward*200,"no keyboard input preserves last heading");
var diagonal=AtgmGuidance.KeyboardStep(forward,1,1,keyboardSettings,.02);
Check(Math.Abs(diagonal.Length()-200)<.001 && Math.Acos(System.Numerics.Vector3.Dot(forward,System.Numerics.Vector3.Normalize(diagonal)))*180/Math.PI<.405,"diagonal keys cannot double turn rate or change speed");
Check(AtgmGuidance.KeyboardStep(forward,1,1,flightSettings,.02)==forward*200,"keyboard cannot steer sight-guided preset");
var kbSub=forward;for(var i=0;i<50;i++)kbSub=System.Numerics.Vector3.Normalize(AtgmGuidance.KeyboardStep(kbSub,1,0,keyboardSettings,.02));
Check(System.Numerics.Vector3.Distance(kbSub,System.Numerics.Vector3.Normalize(AtgmGuidance.KeyboardStep(forward,1,0,keyboardSettings,1)))<.001,"keyboard yaw stable across timestep subdivision");
Check(float.IsFinite(AtgmGuidance.KeyboardStep(System.Numerics.Vector3.UnitY,0,1,keyboardSettings,.02).Z),"vertical missile heading remains finite");
Console.WriteLine($"ATGM TEST 3 PASS: {checks} checks.");

var topattack=releaseDefaults.Single(p=>p.Id=="atgm_topattack_test");
Check(topattack.Atgm is {GuidanceMode:"topattack",TopAttackAltitude:90,TopAttackClimbAngle:45,TopAttackDiveAngle:70},"top-attack preset carries the revised loft settings");
// Retain the existing geometry checks for independently authored legacy profiles.
// The new stock flight is covered by the trajectory and migration sweeps.
var topSettings=topattack.Atgm! with {TopAttackAltitude=60,TopAttackClimbAngle=25,TopAttackDiveAngle=55};
var topSteered=AtgmGuidance.Step(forward,offAxis,flightSettings with{GuidanceMode="topattack"},.02);
Check(topSteered!=forward*200 && Math.Abs(topSteered.Length()-200)<.001,"top-attack mode steers with the bounded sight-style turn");
var diveTrigger=AtgmGuidance.TopAttackDiveTrigger(200,topSettings.MaxTurnRate,topSettings);
Check(Math.Abs(diveTrigger-108.3)<1.5,"dive trigger separates the turn arc and the straight descent leg");
Check(AtgmGuidance.TopAttackDiveTrigger(200,20,topSettings)>diveTrigger,"lower turn rate widens the top-attack standoff");
Check(Math.Abs(AtgmGuidance.LockDwell(.4,.7)-1.1)<1e-9 && AtgmGuidance.LockDwell(double.NaN,.5)==.5 && AtgmGuidance.LockDwell(2,double.NaN)==2,"lock dwell accumulates held seconds and tolerates invalid clocks");
var topClimb=AtgmGuidance.TopAttackSteer(new(0,2,0),new(0,0,500),forward,200,AtgmGuidance.TopAttackClimb,topSettings);
Check(topClimb is {Phase:AtgmGuidance.TopAttackClimb} topClimbCommand && topClimbCommand.Direction.Y>0 && topClimbCommand.Direction.Z>0,"climb phase raises the nose along the configured climb angle");
var topCruise=AtgmGuidance.TopAttackSteer(new(0,90,300),new(0,0,0),-forward,200,AtgmGuidance.TopAttackCruise,topSettings);
Check(topCruise is {Phase:AtgmGuidance.TopAttackCruise} topCruiseCommand && topCruiseCommand.Direction.Y<0,"cruise phase descends from altitude toward the waypoint");
var topDive=AtgmGuidance.TopAttackSteer(new(0,60,60),new(0,0,0),-forward,200,AtgmGuidance.TopAttackCruise,topSettings);
Check(topDive is {Phase:AtgmGuidance.TopAttackDive} topDiveCommand && topDiveCommand.Direction.Y<-.4,"dive presses the configured steep angle once inside the trigger");
var topPlunge=AtgmGuidance.TopAttackSteer(new(0,120,15),new(0,0,0),-forward,200,AtgmGuidance.TopAttackDive,topSettings);
Check(topPlunge is {} topPlungeCommand && topPlungeCommand.Direction.Y<-.9,"steep final approach aims straight at the target");
Check(AtgmGuidance.TopAttackSteer(new(float.NaN,0,0),new(0,0,500),forward,200,0,topSettings)==null,"nonfinite top-attack input is refused");
foreach(var badTop in new[]{topSettings with{TopAttackAltitude=400},topSettings with{TopAttackAltitude=4},topSettings with{TopAttackClimbAngle=4},topSettings with{TopAttackClimbAngle=90},topSettings with{TopAttackDiveAngle=95},topSettings with{TopAttackDiveAngle=5}})
{
 var rejected=false;try{ShellPayload.Validate(topattack with{Atgm=badTop});}catch(FormatException ex){rejected=ex.Message.Contains("atgm_topattack_test");}
 Check(rejected,"invalid top-attack settings rejected with profile context");
}
Check(ShellProfiles.Parse(ReleaseProfiles.Defaults()).Single(p=>p.Id=="atgm_topattack_test").Atgm==topattack.Atgm,"top-attack settings round-trip from embedded JSON");
Console.WriteLine($"TOPATTACK PASS: {checks} checks.");

var motorTestSettings=flightSettings with {MotorDelay=.15,MotorBurnTime=0,CoastDeceleration=0};
Check(AtgmGuidance.InitialSpeed(flightSettings,800)==50,"fixed motor launch speed ignores cannon charge");
var chargeFlight=flightSettings with {LaunchSpeedMode="cannon",LaunchSpeedMultiplier=.2};
Check(AtgmGuidance.InitialSpeed(chargeFlight,400)==80 && AtgmGuidance.InitialSpeed(chargeFlight,800)==160,"cannon mode follows native muzzle velocity and multiplier");
Check(AtgmGuidance.InitialSpeed(chargeFlight,2000)==200 && AtgmGuidance.InitialSpeed(chargeFlight,0)==10,"cannon launch speed has finite lower and cruise caps");
Check(AtgmGuidance.SpeedAtAge(motorTestSettings,50,.1)==50 && AtgmGuidance.SpeedAtAge(motorTestSettings,50,.15)==50,"motor delay preserves launch speed");
Check(Math.Abs(AtgmGuidance.SpeedAtAge(motorTestSettings,50,.65)-125)<1e-9 && AtgmGuidance.SpeedAtAge(motorTestSettings,50,10)==200,"motor acceleration follows elapsed flight time and cruise cap");
Check(AtgmGuidance.SpeedAtAge(motorTestSettings with {Acceleration=0},50,10)==50,"zero acceleration preserves chosen launch speed");
Check(AtgmGuidance.InitialSpeed(new AtgmSettings(FlightSpeed:300),800)==300 && AtgmGuidance.SpeedAtAge(new AtgmSettings(FlightSpeed:300),300,10)==300,"omitted motor fields retain legacy constant speed");
var oldAtgmNode=System.Text.Json.Nodes.JsonNode.Parse(ReleaseProfiles.Defaults())!;
foreach(var node in oldAtgmNode["profiles"]!.AsArray().Where(n=>n!["behavior"]?.GetValue<string>()=="atgm"))
 foreach(var name in new[]{"launchSpeed","launchSpeedMode","launchSpeedMultiplier","acceleration","motorDelay","motorBurnTime","coastDeceleration"})node!.AsObject().Remove(name);
Check(ShellProfiles.Parse(oldAtgmNode.ToJsonString()).Where(p=>p.Behavior=="atgm").All(p=>p.Atgm is {LaunchSpeed:null,Acceleration:0,MotorDelay:0}),"old custom ATGM configs parse without changing flight");
foreach(var invalidMotor in new[]{flightSettings with {LaunchSpeed=201},flightSettings with {LaunchSpeed=double.NaN},flightSettings with {LaunchSpeedMode="charge"},flightSettings with {Acceleration=-1},flightSettings with {LaunchSpeedMultiplier=0},flightSettings with {MotorDelay=6}})
{
 var rejected=false;try{ShellPayload.Validate(konkurs with{Atgm=invalidMotor});}catch(FormatException ex){rejected=ex.Message.Contains(konkurs.Id);}
 Check(rejected,"invalid motor fields rejected with profile context");
}
Console.WriteLine($"ATGM MOTOR PASS: {checks} checks.");

var motorMigrationDir=Path.Combine(SprocketExpandedBallistics.Fix02228Tests.FixtureRoot(),"motor-migration-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(motorMigrationDir);
try
{
 var path=Path.Combine(motorMigrationDir,"shells.json");var before=oldAtgmNode.ToJsonString();File.WriteAllText(path,before);
 Check(ReleaseProfiles.Upgrade(path),"legacy motor fields are exposed by safe config migration");
 var after=ShellProfiles.Parse(File.ReadAllText(path));
 Check(after.Where(p=>p.Behavior=="atgm").All(p=>AtgmGuidance.InitialSpeed(p.Atgm!,800)==p.Atgm!.FlightSpeed && p.Atgm!.Acceleration==0),"motor migration preserves legacy constant-speed behavior");
 Check(File.ReadAllText(path+".pre-v094-backup")==before && !ReleaseProfiles.Upgrade(path),"motor migration has exact backup and is idempotent");
}finally{Directory.Delete(motorMigrationDir,true);}
Console.WriteLine($"RELEASE CANDIDATE PASS: {checks} checks.");

var gunMissile=legacyAtgmProfiles.Single(p=>p.Id=="atgm_gun_saclos");
Check(gunMissile.Behavior=="atgm_gun" && gunMissile.Atgm is {GuidanceMode:"sight",LaunchSpeedMode:"cannon"},"gun-launched example parses complete guidance and motor settings");
Check(ShellBalance.ImpactBehavior(gunMissile)=="heat" && ShellBalance.Calculate(135,800,1800,gunMissile).Velocity==160,"gun missile uses chemical impact and charge-dependent launch");
Check(Math.Abs(ShellBalance.ChemicalPenetration(gunMissile,135)-600)<1e-9 && Math.Abs(ShellBalance.SpacedRetention(gunMissile,135,20,250)-ShellBalance.SpacedRetention(konkurs,135,20,250))<1e-12,"gun-launch audio type retains original warhead and airgap mechanics");
Check(ShellProfiles.Parse(legacyAtgmJson).Single(p=>p.Id==gunMissile.Id).Atgm==gunMissile.Atgm,"gun missile settings round-trip from embedded JSON");
Console.WriteLine($"ATGM AUDIO TYPE PASS: {checks} checks.");

foreach(var sound in new[]{"atgm","atgm_gun"})
{
 using var stream=typeof(ShellProfiles).Assembly.GetManifestResourceStream($"ExpandedBallistics.Audio.{sound}.wav")!;
 var decoded=PcmWave.Read(stream);
 Check(decoded.Frequency==48000 && decoded.Channels==1 && decoded.Samples.Length>48000,"bundled launch WAV decodes as 48kHz mono");
 Check(decoded.Samples.All(float.IsFinite) && decoded.Samples.Max(Math.Abs)<.85f,"launch WAV samples are finite and not clipped");
 Check(Math.Abs(decoded.Samples[0])<.001 && Math.Abs(decoded.Samples[^1])<.001,"launch WAV has smooth silence at both endpoints");
}
using(var invalidWave=new MemoryStream(new byte[16]))
{
 var rejected=false;try{PcmWave.Read(invalidWave);}catch(InvalidDataException){rejected=true;}
 Check(rejected,"invalid replacement WAV fails safely before upload");
}
Console.WriteLine($"LAUNCH AUDIO PASS: {checks} checks.");

var flatAi=AtgmGuidance.AiIntercept(new(0,0,1000),System.Numerics.Vector3.Zero,System.Numerics.Vector3.Zero,flightSettings);
Check(flatAi==new System.Numerics.Vector3(0,0,1000),"AI missile aim has no artillery elevation for stationary target");
var movingAi=AtgmGuidance.AiIntercept(new(0,0,1000),new(10,0,0),System.Numerics.Vector3.Zero,flightSettings);
Check(movingAi.X>49 && movingAi.X<51 && movingAi.Y==0,"AI missile lead uses its native estimated velocity without adding shell drop");
var burnout=flightSettings with {MotorDelay=.1,MotorBurnTime=1,Acceleration=150,CoastDeceleration=5};
Check(AtgmGuidance.SpeedAtAge(burnout,50,1.1)==200 && AtgmGuidance.SpeedAtAge(burnout,50,3.1)==190,"finite boost ends then coasting speed decays");
Check(AtgmGuidance.SpeedAtAge(burnout,50,100)==10,"coast model retains a finite minimum speed");
Check(AtgmGuidance.SpeedAtAge(burnout with {MotorBurnTime=0},50,100)==200,"zero burn time preserves old sustained motor semantics");
Console.WriteLine($"AI / FLIGHT PASS: {checks} checks.");
using(var fixture=typeof(ShellProfiles).Assembly.GetManifestResourceStream("ArmourResponse.Fixture")!)
{
 var json=new StreamReader(fixture).ReadToEnd();
 var cfg=ArmourResponses.Parse(json) with {Enabled=true};
 var glass=cfg.Responses.Single(r=>r.Kind=="glassTextolite");
 var nera=cfg.Responses.Single(r=>r.Kind=="nera");
 var era=cfg.Responses.Single(r=>r.Kind=="lightEra");
 var composite=cfg.Responses.Single(r=>r.Kind=="passiveComposite");
 Check(cfg.Responses.Count==4,"canonical catalogue binds four distinct exact material IDs");
 Check(!ArmourResponses.Parse(json).Enabled,"standalone response catalogue defaults disabled");
 Check(ArmourResponses.PassiveMatches(nera,5633.3335f,.55f,.2f),"float passive recipe matches after exact ID resolution");
 Check(!ArmourResponses.PassiveMatches(nera,7850,1,.2f),"custom passive recipe disables additional response");
 var shot=new ArmourShotState();
 Check(ArmourResponseModel.Apply(cfg,glass,shot,"glass0","apfsds",true,true,100,30,25,false)==1,"unconditioned rod has no glass bonus");
 Check(ArmourResponseModel.PerforatedSteel(shot,cfg.Preconditioning,"rha",20,50,0,"steel1"),"traversed oblique steel conditions rod");
 Check(Math.Abs(shot.Disturbance-.35)<1e-12,"steel disturbance follows candidate curve");
 Check(!ArmourResponseModel.PerforatedSteel(shot,cfg.Preconditioning,"rha",20,50,0,"steel1")&&shot.Disturbance==.35,"repeat callback cannot precondition twice");
 Check(Math.Abs(ArmourResponseModel.Apply(cfg,glass,shot,"glass1","apfsds",true,true,100,30,25,false)-.93)<1e-12,"downstream glass applies proportional disturbance loss");
 Check(ArmourResponseModel.Apply(cfg,glass,shot,"glass1","apfsds",true,true,100,30,25,false)==1,"same layer applies once");
 foreach(var gap in new[]{double.NaN,-5,0,4,301})
  Check(ArmourResponseModel.Apply(cfg,glass,shot,"gap"+gap,"apfsds",true,true,100,30,gap,false)==1,"unproven/out-of-band gap cannot grant synergy");
 Check(ArmourResponseModel.Apply(cfg,nera,new(),"n0","heat",true,true,30,0,0,false)==1,"NERA needs obliquity");
 Check(ArmourResponseModel.Apply(cfg,nera,new(),"n45","heat",true,true,30,45,0,false)==.75,"declared NERA works without fictitious outside gap");
 Check(ArmourResponseModel.Apply(cfg,nera,new(),"n85","heat",true,true,30,85,0,false)==1,"outside angular curve no extrapolation benefit");
 foreach(var mode in new[]{"ap","aphe","hesh"})Check(ArmourResponseModel.Apply(cfg,era,new(),mode,mode,true,true,30,30,0,true)==1,"light ERA has no invented anti-AP/HESH bonus");
 Check(ArmourResponseModel.Apply(cfg,era,new(),"rod","apfsds",true,true,30,30,0,true)==1,"light ERA adds no anti-rod loss");
 Check(ArmourResponseModel.Apply(cfg,era,new(),"heat","heat",true,true,30,30,0,true)==era.Calibration.HeatRetention,"intact eligible ERA follows canonical catalogue retention");
 Check(ArmourResponseModel.Apply(cfg,era,new(),"spent","heat",true,true,30,30,0,false)==1,"spent cell retains only native passive resistance");
 Check(ArmourResponseModel.Apply(cfg,era,new(),"frag","heat",false,true,30,30,0,true)==1,"secondary fragment receives no response");
 Check(ArmourResponseModel.Apply(cfg,era,new(),"old","heat",true,false,30,30,0,true)==1,"non-ColdWar target receives no response");
 Check(ArmourResponseModel.Apply(cfg with {Enabled=false},era,new(),"off","heat",true,true,30,30,0,true)==1,"disabled catalogue keeps passive simulation");
 var many=new ArmourShotState();
 for(var i=0;i<20;i++)ArmourResponseModel.Apply(cfg,nera,many,"many"+i,"heat",true,true,30,45,0,false);
 Check(Math.Abs(many.AdditionalRetention-.4)<1e-12,"additional HEAT losses capped cumulatively");
 var disturbed=new ArmourShotState();
 for(var i=0;i<10;i++)ArmourResponseModel.PerforatedSteel(disturbed,cfg.Preconditioning,"sheetMetal",20,60,0,"s"+i);
 Check(disturbed.Disturbance==.6,"disturbance capped across traversed layers");
 for(var i=0;i<30;i++)ArmourResponseModel.Apply(cfg,composite,disturbed,"c"+i,"apfsds",true,true,100,30,0,false);
 Check(Math.Abs(disturbed.AdditionalRetention-.65)<1e-12,"additional rod loss capped cumulatively");
 var cells=new EraCells();var cell=new EraCell(1,2,1,0,0);var preview=new EraCells();
 Check(cells.Consume(cell)&&!cells.Consume(cell),"ERA cell benefits exactly once");
 Check(cells.Consume(cell with {X=1}),"adjacent cell remains intact");
 Check(preview.Consume(cell)&&cells.Count==2,"preview ledger cannot change combat ledger");
 Check(cells.Consume(cell with {Spawn=2}),"new spawn has fresh ERA cells");
 cells.RemoveElement(1,2);Check(cells.Count==1,"element cleanup removes only its consumed cells");
 cells.RemoveSpawn(2);Check(cells.Count==0,"spawn removal clears transient battle damage");
 var concurrent=new EraCells();var winners=0;
 Parallel.For(0,64,_=>{if(concurrent.Consume(cell))Interlocked.Increment(ref winners);});
 Check(winners==1,"concurrent impacts receive only one intact ERA benefit");
 var invalid=json.Replace("\"angleFullDegrees\": 50","\"angleFullDegrees\": 20");
 var rejected=false;try{ArmourResponses.Parse(invalid);}catch(FormatException){rejected=true;}
 Check(rejected,"equal full/min angle is rejected before division");
 var invalidShot=new ArmourShotState();
 Check(!ArmourResponseModel.PerforatedSteel(invalidShot,cfg.Preconditioning with {FullAngle=20},"rha",20,20,0,"invalid")&&double.IsFinite(invalidShot.Disturbance),"model defensively rejects zero slope");
}
Console.WriteLine($"ARMOUR RESPONSE PASS: {checks} checks.");
Check(ArmourResponseModel.ResidualSpeed(800,.5f)==400,"native continuation preserves parent speed reduction after cached KE");
Check(ArmourResponseModel.ResidualSpeed(800,1)==800,"unmodified material leaves native continuation alone");
Check(ArmourResponseModel.ResidualSpeed(800,2)==800,"energy correction cannot accelerate native child");
Check(ArmourResponseModel.ResidualSpeed(800,float.NaN)==800,"invalid cache correction fails closed");
Console.WriteLine($"ARMOUR ENERGY PASS: {checks} checks.");
var nativeProportionalSpeed=(float)Math.Sqrt(2*100*.5/2);
var correctedProportionalSpeed=ArmourResponseModel.ResidualSpeed(nativeProportionalSpeed,.8f);
Check(Math.Abs(.5*2*correctedProportionalSpeed*correctedProportionalSpeed-32)<.00001,"native proportional passive energy loss uses corrected input energy");
var changedMassSpeed=(float)Math.Sqrt(2*100*.5/5);
var correctedMassSpeed=ArmourResponseModel.ResidualSpeed(changedMassSpeed,.8f);
Check(Math.Abs(.5*5*correctedMassSpeed*correctedMassSpeed-32)<.00001,"native changed child mass retains proportional KE correction");
Check(ArmourResponseModel.ResidualSpeed(0,.8f)==0,"native stopped continuation is not revived");
Console.WriteLine($"ARMOUR NATIVE FORMULA PASS: {checks} checks.");
var actualEraNames=new[]{"WWI","Interwar","Earlywar","Midwar","Latewar","Coldwar","Coldwar1","Coldwar2","Coldwar3"};
var actualStarts=new[]{new ShellNativeDate(1914,7,28),new ShellNativeDate(1920,1,1),new ShellNativeDate(1939,9,2),new ShellNativeDate(1942,1,1),new ShellNativeDate(1944,1,1),new ShellNativeDate(1945,9,3),new ShellNativeDate(1945,9,4),new ShellNativeDate(1960,1,1),new ShellNativeDate(1975,1,1)};
var allBehaviors=new[]{"ap","he","aphe","heat","hesh","apfsds","atgm","atgm_gun"};
ShellEraContext NativeContext(int index, params string[] enabled)=>new(actualStarts[index],actualEraNames,actualStarts,index,new HashSet<string>(enabled));
foreach(var behavior in allBehaviors)
foreach(var index in Enumerable.Range(0,actualEraNames.Length))
{
 var custom=new ShellProfile("renamed_"+behavior,"Custom",defaults,behavior);
 Check(ShellEraPolicy.Allowed(custom,NativeContext(index,behavior)),"native available tech permits behavior in ANY registered era");
 Check(!ShellEraPolicy.Allowed(custom,NativeContext(index)),"absent native behavior tech denies in ANY era");
}
var storedAtgm=new ShellProfile("custom_alias","User missile",defaults,"atgm");var savedId=storedAtgm.Id;
Check(ShellEraPolicy.Effective(storedAtgm,NativeContext(4))==null&&storedAtgm.Id==savedId,"unavailable stored profile retains identity");
Check(ShellEraPolicy.Effective(storedAtgm,NativeContext(0,"atgm"))?.Id==savedId,"technology moved to WWI permits missile without hidden floor");
Check(ShellEraPolicy.Effective(storedAtgm,null)==null,"missing native context fails closed");
var strictHe=storedAtgm with{Behavior="he",MinimumDate=new ShellNativeDate(1945,9,3)};
Check(!ShellEraPolicy.Allowed(strictHe,NativeContext(4,"he"))&&ShellEraPolicy.Allowed(strictHe,NativeContext(5,"he")),"explicit minimumDate gates the day before/on the boundary");
Check(!ShellEraPolicy.Allowed(strictHe with{MinimumDate=new ShellNativeDate(2009,1,1)},NativeContext(5,"he")),"finite date before Duplet 2009 fails");
var eraJson=ReleaseProfiles.Defaults().Replace("\"behavior\": \"he\"","\"behavior\": \"he\", \"minimumDate\": \"2100.01.01\"");
Check(ShellProfiles.Parse(eraJson).Single(p=>p.Behavior=="he").MinimumDate==new ShellNativeDate(2100,1,1),"explicit minimumDate parses and is preserved");
var badEraRejected=false;try{ShellProfiles.Parse(eraJson.Replace("2100.01.01","not-a-date"));}catch(FormatException){badEraRejected=true;}
Check(badEraRejected,"invalid minimumDate rejected");
var nativeTypes=new ShellNativeTypeCache<string>();
nativeTypes.Remember((IntPtr)1,"custom missile cached id","actual native APHE id");
var cachedType="custom missile cached id";
Check(nativeTypes.Restore((IntPtr)1,cachedType,ref cachedType)&&cachedType=="actual native APHE id","cached custom projectile ID restores exact native input before blocked launch");
var otherRegisterType="custom missile cached id";
Check(!nativeTypes.Restore((IntPtr)2,otherRegisterType,ref otherRegisterType)&&otherRegisterType=="custom missile cached id","cached mapping is scoped to actual native register");
nativeTypes.Clear((IntPtr)1);cachedType="custom missile cached id";
Check(!nativeTypes.Restore((IntPtr)1,cachedType,ref cachedType),"register release clears stale type fallback mappings");
var shooterProfile=ShellEraPolicy.Effective(storedAtgm,NativeContext(5,"atgm"));
Check(shooterProfile!=null&&shooterProfile.Id==savedId,"legitimate launched ColdWar snapshot remains usable against earlier target");
Console.WriteLine($"SHELL ERA REPAIR PASS: {checks} checks.");
nativeTypes.Remember((IntPtr)1,"shared custom id","native AP for cannon A",(IntPtr)10);
nativeTypes.Remember((IntPtr)1,"shared custom id","native APHE for cannon B",(IntPtr)20);
var cannonAType="shared custom id";var cannonBType="shared custom id";
Check(nativeTypes.Restore((IntPtr)1,cannonAType,ref cannonAType,(IntPtr)10)&&cannonAType=="native AP for cannon A","cached fallback retains actual native ammo for first cannon");
Check(nativeTypes.Restore((IntPtr)1,cannonBType,ref cannonBType,(IntPtr)20)&&cannonBType=="native APHE for cannon B","shared custom prototype cannot mix native ammo across cannons");
var unknownSourceType="shared custom id";
Check(!nativeTypes.Restore((IntPtr)1,unknownSourceType,ref unknownSourceType,(IntPtr)30)&&nativeTypes.Known((IntPtr)1,unknownSourceType),"unknown-source cached custom shot can be withheld without inventing AP ID");
Console.WriteLine($"SHELL ERA SOURCE CACHE PASS: {checks} checks.");
nativeTypes.Remember((IntPtr)1,"custom prototype native_AP","AP original",(IntPtr)10);
nativeTypes.Remember((IntPtr)1,"custom prototype native_APHE","APHE original",(IntPtr)10);
var previousAp="custom prototype native_AP";var currentAphe="custom prototype native_APHE";
Check(nativeTypes.Restore((IntPtr)1,previousAp,ref previousAp,(IntPtr)10)&&previousAp=="AP original","same source cached former AP type remains AP after switching to APHE");
Check(nativeTypes.Restore((IntPtr)1,currentAphe,ref currentAphe,(IntPtr)10)&&currentAphe=="APHE original","same source APHE type has independent actual native binding");
Console.WriteLine($"SHELL ERA AMMO CACHE PASS: {checks} checks.");

var stockHeatBudget=ShellProfiles.Parse(ReleaseProfiles.Defaults()).Single(p=>p.Id=="heat");
foreach(var period in new[]{1.2,1.2,1.2})
    Check(Math.Abs(ShellChemicalBudget.Resolve(stockHeatBudget,85,period)-102)<1e-9,"stock WWII85mm HEAT budget102: "+period);
Check(ShellChemicalBudget.Resolve(stockHeatBudget,85,4d)==340,"stock ColdWar85mm retains340");
foreach(var period in new double?[]{null,double.NaN,0,21})
    Check(ShellChemicalBudget.Resolve(stockHeatBudget,85,period)==0,"unavailable era fails closed");
Check(ShellChemicalBudget.Resolve(stockHeatBudget with{ChemicalPenetrationMm=450,ChemicalSource="profile"},85,1.2)==382.5,"custom450 budget retained with explicit profile source"); // 0.21.1 explicit chemicalSource.
Check(ShellChemicalBudget.Resolve(stockHeatBudget with{Id="custom_heat",ChemicalSource="profile"},85,1.2)==340,"alias with profile source uses configured numbers");
var changedStockFields=new[]{
stockHeatBudget with{Label="Personal HEAT"},stockHeatBudget with{Behavior="hesh"},
stockHeatBudget with{NativeExplosivePower=1},stockHeatBudget with{SpallMultiplier=2},
stockHeatBudget with{ConeHalfAngleDegrees=10},stockHeatBudget with{ExplosionScale=.8},
stockHeatBudget with{SecondPlatePenetrationFactor=.2},stockHeatBudget with{AirGapLossPerCalibre=.4},
stockHeatBudget with{ReferenceCalibreMm=105},stockHeatBudget with{MinimumDate=new ShellNativeDate(1942,1,1)},
stockHeatBudget with{Settings=stockHeatBudget.Settings with{DiameterRatio=.8}},
stockHeatBudget with{Settings=stockHeatBudget.Settings with{LengthInCalibres=3}},
stockHeatBudget with{Settings=stockHeatBudget.Settings with{Density=1900}},
stockHeatBudget with{Settings=stockHeatBudget.Settings with{VelocityEfficiency=.2}},
stockHeatBudget with{Settings=stockHeatBudget.Settings with{VelocityMultiplier=1.1}},
stockHeatBudget with{Settings=stockHeatBudget.Settings with{MaxVelocityFactor=1.1}},
stockHeatBudget with{Settings=stockHeatBudget.Settings with{MaxVelocity=650}},
stockHeatBudget with{Settings=stockHeatBudget.Settings with{PenetrationQuality=.5}},
stockHeatBudget with{Settings=stockHeatBudget.Settings with{DamageMultiplier=.9}}
};
foreach(var modified in changedStockFields) Check(ShellChemicalBudget.IsStock(modified),"explicit chemicalSource ignores unrelated edits"); // 0.21.1 replaces record equality.
Check(!ShellChemicalBudget.IsStock(stockHeatBudget with{ChemicalSource="profile"}),"chemicalSource profile opts out of native HEAT technology");
Check(ShellChemicalBudget.Resolve(stockHeatBudget,105,1.2)==126,"generic105mm gameplaybudget126 not Gr39 historical95");
Check(ShellChemicalBudget.Resolve(stockHeatBudget,1000,4d)==2000,"chemical cap2000 retained");
var launchedColdwarBudget=ShellChemicalBudget.Resolve(stockHeatBudget,85,4d);
var laterEditorBudget=ShellChemicalBudget.Resolve(stockHeatBudget,85,1.2);
Check(launchedColdwarBudget==340&&laterEditorBudget==102,"captured budget remains independent of subsequent era resolve");
foreach(var unchangedChemical in ShellProfiles.Parse(ReleaseProfiles.Defaults()).Where(p=>p.Behavior is "hesh" or "atgm" or "atgm_gun"))
    Check(ShellChemicalBudget.Resolve(unchangedChemical,85,4d)==ShellBalance.ChemicalPenetration(unchangedChemical,85),"other chemical profiles unchanged "+unchangedChemical.Id);
Check(ShellChemicalBudget.Description(stockHeatBudget,1.2).Contains("1.20"),"stock UI names gameplayperiod");
Check(ShellChemicalBudget.Description(stockHeatBudget with{Id="custom_heat",ChemicalSource="profile"},1.2).Contains("exempt"),"custom UI exemption explained");
Console.WriteLine($"HEAT PERIOD REVIEW PASS: {checks} checks.");

Check(ShellChemicalBudget.IsStock(stockHeatBudget with{Atgm=new AtgmSettings()}),"explicit source ignores nested Atgm presence"); // 0.21.1 explicit chemicalSource.
Console.WriteLine($"FINAL HEAT REVIEW PASS: {checks} checks.");

using(var heavyStream=typeof(ShellProfiles).Assembly.GetManifestResourceStream("ArmourResponse.Fixture")!)
using(var heavyReader=new StreamReader(heavyStream))
{
 var heavyRoot=System.Text.Json.Nodes.JsonNode.Parse(heavyReader.ReadToEnd())!;
 var entries=heavyRoot["responses"]!.AsArray();
 var heavyEntry=System.Text.Json.Nodes.JsonNode.Parse(entries.First(n=>n!["kind"]!.GetValue<string>()=="lightEra")!.ToJsonString())!;
 heavyEntry["responseId"]="heavyEraCassette";heavyEntry["kind"]="heavyEra";
 heavyEntry["compatibleMaterialIds"]=new System.Text.Json.Nodes.JsonArray("cwepHeavyEraCassette");
 heavyEntry["passiveMaterial"]!["density"]=4000;heavyEntry["passiveMaterial"]!["rhaFactor"]=.55;
 heavyEntry["passiveMaterial"]!["spallFactor"]=.25;heavyEntry["passiveMaterial"]!["requestedCostMultiplier"]=10.5;
 heavyEntry["calibration"]!["heatRetention"]=.5;heavyEntry["calibration"]!["intactRodRetention"]=.85;
 heavyEntry["calibration"]!["disturbedRodRetention"]=.85;
 heavyEntry["geometry"]!["minNormalThicknessMm"]=60;heavyEntry["geometry"]!["maxNormalThicknessMm"]=80;
 heavyEntry["geometry"]!["angularCurve"]=System.Text.Json.Nodes.JsonNode.Parse("[{\"angleDegrees\":0,\"weight\":1},{\"angleDegrees\":60,\"weight\":1},{\"angleDegrees\":80,\"weight\":0}]");
 heavyEntry["geometry"]!["kineticAngularCurve"]=System.Text.Json.Nodes.JsonNode.Parse("[{\"angleDegrees\":0,\"weight\":0},{\"angleDegrees\":30,\"weight\":1},{\"angleDegrees\":60,\"weight\":1},{\"angleDegrees\":80,\"weight\":0}]");
 entries.Add(heavyEntry);
 var heavyCfg=ArmourResponses.Parse(heavyRoot.ToJsonString()) with{Enabled=true};
 var heavy=heavyCfg.Responses.Single(r=>r.Kind=="heavyEra");
 double Heavy(string threat,double angle,bool intact=true,double thickness=70,bool original=true,bool cold=true)=>ArmourResponseModel.Apply(heavyCfg,heavy,new(),"heavy",threat,original,cold,thickness,angle,0,intact);
 Check(Heavy("heat",0)==.5&&Heavy("heat",60)==.5,"heavy ERA full HEAT response normal through60");
 Check(Math.Abs(Heavy("heat",70)-.75)<1e-9&&Heavy("heat",80)==1,"heavy HEAT angular fade");
 Check(Heavy("apfsds",0)==1&&Heavy("apfsds",30)==.85&&Heavy("apfsds",60)==.85,"heavy KE distinct angular response");
 Check(Math.Abs(Heavy("apfsds",15)-.925)<1e-9&&Math.Abs(Heavy("apfsds",70)-.925)<1e-9,"heavy KE ramps on both sides");
 foreach(var threat in new[]{"heat","apfsds"})
 {
  Check(Heavy(threat,45,false)==1,"shared spentcell passive for "+threat);
  Check(Heavy(threat,45,thickness:59.9)==1&&Heavy(threat,45,thickness:80.1)==1,"thickness band enforced");
  Check(Heavy(threat,45,thickness:60)<1&&Heavy(threat,45,thickness:80)<1,"inclusive thickness endpoints");
  Check(Heavy(threat,45,original:false)==1&&Heavy(threat,45,cold:false)==1,"secondary and earlier target passive");
  Check(Heavy(threat,90)==1,"outside angle passive");
 }
 // 0.22.12: every original projectile detonates an intact heavy cell. Full-bore
 // AP/APHE have no extra effect by default (kineticFullBore 0: cell consumed,
 // retention 1); an explicit 0.25 still pays a quarter of the rod recipe.
 var heavyQuarter=heavy with{ThreatScale=EraThreatScale.DefaultFor("heavyEra") with{KineticFullBore=.25}};
 foreach(var threat in new[]{"ap","aphe"})
 {
  var consumedShot=new ArmourShotState();
  Check(ArmourResponseModel.Apply(heavyCfg,heavy,consumedShot,"heavy",threat,true,true,70,45,0,true)==1&&consumedShot.Applied.Contains("heavy"),
   "heavy full-bore detonates by default with retention 1 and the cell consumed "+threat);
  var quarterRetention=ArmourResponseModel.Apply(heavyCfg,heavyQuarter,new(),"heavy",threat,true,true,70,45,0,true);
  Check(quarterRetention<1&&quarterRetention>.85,"heavy full-bore with an explicit 0.25 scale pays a quarter of the rod recipe "+threat);
 }
 Check(Heavy("hesh",45)==1&&Heavy("he",45)==1,"heavy HESH/HE detonate without an extra penetration effect");
 Check(!ArmourResponseModel.ReactiveThreat(heavy,"ap")&&!ArmourResponseModel.ReactiveThreat(heavy,"hesh"),
  "legacy ReactiveThreat predicate keeps its old meaning");
 var heavyCells=new EraCells();var sharedCell=new EraCell(88,2,1,0,0);
 Check(heavyCells.Consume(sharedCell)&&!heavyCells.Consume(sharedCell),"HEAT/KE share exactly one finitecell");
 Check(heavyCells.Consume(sharedCell with{Element=3})&&heavyCells.Consume(sharedCell with{Spawn=89}),"separate plate/spawn isolated");
 Check(ArmourResponseModel.AngularWeight(heavy,"apfsds",0)==0&&ArmourResponseModel.AngularWeight(heavy,"heat",0)>0,"zero-weight rod doesnot trigger but normalHEAT does");
 var heavyCumulative=new ArmourShotState();
 for(var i=0;i<20;i++)ArmourResponseModel.Apply(heavyCfg,heavy,heavyCumulative,"h"+i,"heat",true,true,70,45,0,true);
 Check(Math.Abs(heavyCumulative.AdditionalRetention-.4)<1e-9,"heavy HEAT cumulative60percent cap unchanged");
 var heavyKeCumulative=new ArmourShotState();
 for(var i=0;i<20;i++)ArmourResponseModel.Apply(heavyCfg,heavy,heavyKeCumulative,"k"+i,"apfsds",true,true,70,45,0,true);
 Check(Math.Abs(heavyKeCumulative.AdditionalRetention-.65)<1e-9,"heavy KE cumulative35percent cap unchanged");
 Check(ArmourResponses.PassiveMatches(heavy,4000,.55f,.25f)&&!ArmourResponses.PassiveMatches(heavy,4300,.35f,.2f),"heavy exact passive fingerprint");
 var withoutKe=System.Text.Json.Nodes.JsonNode.Parse(heavyRoot.ToJsonString())!;
 withoutKe["responses"]!.AsArray().Last()!["geometry"]!.AsObject().Remove("kineticAngularCurve");
 bool missingRejected=false;try{ArmourResponses.Parse(withoutKe.ToJsonString());}catch(FormatException){missingRejected=true;}
 Check(missingRejected,"heavy missing KE curve rejected");
 var oldWithKe=System.Text.Json.Nodes.JsonNode.Parse(heavyRoot.ToJsonString())!;
 var oldEntry=oldWithKe["responses"]!.AsArray().First(n=>n!["kind"]!.GetValue<string>()=="lightEra")!;
 oldEntry["geometry"]!["kineticAngularCurve"]=System.Text.Json.Nodes.JsonNode.Parse(heavyEntry["geometry"]!["kineticAngularCurve"]!.ToJsonString());
 bool oldRejected=false;try{ArmourResponses.Parse(oldWithKe.ToJsonString());}catch(FormatException){oldRejected=true;}
 Check(oldRejected,"old kinds forbid new optional curve");
}
Console.WriteLine($"HEAVY ERA REVIEW PASS: {checks} checks.");

if(Environment.GetEnvironmentVariable("SHELL_REVIEW_ARMOUR_CATALOGUE") is {} producerPath)
{
 var producerCatalogue=ArmourResponses.Parse(File.ReadAllText(producerPath));
 // Production now contains the full physical ERA catalogue; identify this
 // legacy producer by its material binding, not a unique kind assumption.
 var producerHeavy=producerCatalogue.Responses.Single(r=>r.CompatibleMaterialIds.Contains("cwepHeavyEraCassette"));
 Check(producerHeavy.CompatibleMaterialIds.SequenceEqual(new[]{"cwepHeavyEraCassette"}),"actualproducer exact heavy materialID");
 Check(producerHeavy.Calibration.HeatRetention==.5&&producerHeavy.Calibration.IntactRodRetention==.85&&producerHeavy.Calibration.DisturbedRodRetention==.85,"actualproducer calibration");
 Check(producerHeavy.Geometry.MinNormalThicknessMm==60&&producerHeavy.Geometry.MaxNormalThicknessMm==80&&producerHeavy.CellPitchM==.25,"actualproducer geometry/cells");
 Check(ArmourResponseModel.AngularWeight(producerHeavy,"heat",0)==1&&ArmourResponseModel.AngularWeight(producerHeavy,"apfsds",0)==0,"actualproducer distinctanglecurves");
 Check(producerCatalogue.Enabled&&producerCatalogue.MaximumAdditionalHeatLoss==.85&&producerCatalogue.MaximumAdditionalKineticLoss==.65,"actualproducer live production enabled/caps synchronized");
 Check(producerCatalogue.Responses.Single(r=>r.CompatibleMaterialIds.Contains("cwepLightEraCassette")).Calibration.HeatRetention==.62,"actualproducer legacy light ERA unchanged");
 Console.WriteLine($"ACTUAL PRODUCER CONTRACT PASS: {checks} checks.");
}

if(Environment.GetEnvironmentVariable("SHELL_REVIEW_CANDIDATE_DLL") is {} candidateDll && Environment.GetEnvironmentVariable("SHELL_REVIEW_ARMOUR_CATALOGUE") is {} candidateCatalogue)
{
 var candidateAssembly=System.Reflection.Assembly.LoadFrom(candidateDll);
 var candidateParser=candidateAssembly.GetType("SprocketExpandedBallistics.ArmourResponses",true)!;
 var parsedCandidate=candidateParser.GetMethod("Parse",System.Reflection.BindingFlags.Static|System.Reflection.BindingFlags.NonPublic)!.Invoke(null,new object[]{File.ReadAllText(candidateCatalogue)})!;
 var candidateResponses=(System.Collections.IEnumerable)parsedCandidate.GetType().GetProperty("Responses")!.GetValue(parsedCandidate)!;
 var candidateKinds=candidateResponses.Cast<object>().Select(r=>(string)r.GetType().GetProperty("Kind")!.GetValue(r)!).ToArray();
 Check(candidateKinds.Count(k=>k=="heavyEra")==1,"actual frozen candidate DLL parses producer heavyEra catalogue");
 Console.WriteLine($"ACTUAL DLL CONTRACT PASS: {checks} checks.");
}
var futureNames=new[]{"renamed early","Custom Era 2100","Space"};
var futureStarts=new[]{new ShellNativeDate(1800,1,1),new ShellNativeDate(2100,1,1),new ShellNativeDate(3000,1,1)};
var future=new ShellEraContext(new ShellNativeDate(2101,1,1),futureNames,futureStarts,1,new HashSet<string>(allBehaviors),7);
foreach(var b in allBehaviors)Check(ShellEraPolicy.Allowed(storedAtgm with{Behavior=b},future),"renamed future era uses native available technology");
Check(ShellEraPolicy.Allowed(storedAtgm with{MinimumDate=new ShellNativeDate(2100,1,1)},future),"minimumDate 2100 is allowed on a 2101 vehicle");
Check(!ShellEraPolicy.Allowed(storedAtgm with{MinimumDate=new ShellNativeDate(3000,1,1)},future),"later minimumDate 3000 stays unavailable");
var moved=future with{Names=new[]{"renamed early","Custom Era 2100","Space"},Starts=new[]{new ShellNativeDate(1800,1,1),new ShellNativeDate(1850,1,1),new ShellNativeDate(1900,1,1)},Date=new ShellNativeDate(1851,1,1)};
Check(ShellEraPolicy.Allowed(storedAtgm with{MinimumDate=new ShellNativeDate(1850,1,1)},moved),"minimumDate 1850 works on a moved 1851 vehicle");
Check(ShellEraPolicy.Valid(future with{Date=ShellNativeDate.Maximum,Index=2}),"native classified last-era sentinel valid");
Check(!ShellEraPolicy.Valid(future with{Date=ShellNativeDate.Maximum,Index=1}),"sentinel never allowed in nonlast era");
Check(!ShellEraPolicy.Allowed(storedAtgm,future with{Date=ShellNativeDate.Maximum,Index=2,AvailableBehaviors=new HashSet<string>()}),"sentinel does not invent technology availability");
Check(!ShellEraPolicy.Valid(future with{Starts=new[]{futureStarts[1],futureStarts[0],futureStarts[2]}}),"unordered timeline rejected");
Check(!ShellEraPolicy.Valid(future with{Names=new[]{"same","same","Space"}}),"ambiguous era identity rejected");
Check(!ShellEraPolicy.Valid(future with{Index=-1}),"native unknown classification denied");
Check(!ShellEraPolicy.Valid(future with{Date=new ShellNativeDate(2099,1,1)}),"date before native era start denied");
Check(!ShellDatePolicy.ValidTimeline(new[]{ShellNativeDate.Maximum}),"sentinel cannot be an era start");
Check(ShellChemicalBudget.Resolve(stockHeatBudget,85,7)==595,"custom dated native factor directly controls stock HEAT");
Check(ShellChemicalBudget.Resolve(stockHeatBudget with{ChemicalPenetrationMm=450,ChemicalSource="profile"},85,7)==382.5,"native factor never overwrites a profile-source shell"); // 0.21.1 explicit chemicalSource.
Check(ShellChemicalBudget.Resolve(stockHeatBudget with{ChemicalPenetrationMm=450,ChemicalSource="profile"},85,7,false)==0,"native unavailable behavior gate also denies custom chemical shell");
Check(ShellProfiles.Parse(ReleaseProfiles.Defaults()).All(p=>p.MinimumDate==null),"stock defaults have no duplicate date floor requiring migration");
Console.WriteLine($"FR011 NATIVE DATE POLICY PASS: {checks} checks.");

var yearZero=new ShellEraContext(new ShellNativeDate(0,1,1),new[]{"Year zero","Custom"},new[]{new ShellNativeDate(0,1,1),new ShellNativeDate(1,1,1)},0,new HashSet<string>(allBehaviors));
Check(ShellEraPolicy.Allowed(storedAtgm,yearZero),"native year0 design and era accepted with available tech");
Check(ShellEraPolicy.Valid(yearZero with{Date=new ShellNativeDate(1,1,1),Index=1}),"year0 and year1 remain distinct");
Check(!ShellEraPolicy.Valid(yearZero with{Date=new ShellNativeDate(1,1,1)}),"mismatched native index rejected");
Check(ShellDatePolicy.ValidTimeline(new[]{new ShellNativeDate(0,1,1),new ShellNativeDate(1,1,1)}),"native year0 timeline supported");
Check(!ShellDatePolicy.ValidTimeline(new[]{new ShellNativeDate(0,13,1)}),"invalid native date denied");
Check(!ShellDatePolicy.ValidTimeline(new[]{new ShellNativeDate(1900,2,29)}),"invalid nonleap date denied");
Console.WriteLine($"FR011 YEAR ZERO PASS: {checks} checks.");

foreach(var b in allBehaviors)
{
 using var stream=typeof(ShellProfiles).Assembly.GetManifestResourceStream("NativeShellTech."+b+".json")!;
 using var reader=new StreamReader(stream);using var doc=System.Text.Json.JsonDocument.Parse(reader.ReadToEnd());var data=doc.RootElement;
 var expected=b switch{"ap" or "he"=>"1914.07.28","aphe"=>"1939.09.02","heat"=>"1940.01.01","hesh"=>"1955.01.01","apfsds"=>"1961.01.01","atgm"=>"1955.01.01","atgm_gun"=>"1976.01.01",_=>"1945.09.03"}; // 0.22.9 dated tech ladder.
 Check(data.GetProperty("date").GetString()==expected,"native example boundary preserves previous stock availability "+b);
 Check(data.GetProperty("type").GetString()=="expandedBallistics_"+b&&data.GetProperty("properties").GetProperty("enabled").GetBoolean(),"native technology type/enabled property "+b);
}
using(var stream=typeof(ShellProfiles).Assembly.GetManifestResourceStream("NativeShellTech.heat-modern.json")!)
using(var reader=new StreamReader(stream))
using(var doc=System.Text.Json.JsonDocument.Parse(reader.ReadToEnd()))
{
 Check(doc.RootElement.GetProperty("date").GetString()=="1945.09.03","later stock HEAT record preserves boundary");
 Check(doc.RootElement.GetProperty("properties").GetProperty("penetrationPerCalibre").GetDouble()==2,"1945 native factor now 2.0 calibre"); // 0.21.1 smooth HEAT ladder.
}
using(var stream=typeof(ShellProfiles).Assembly.GetManifestResourceStream("NativeShellTech.heat.json")!)
using(var reader=new StreamReader(stream))
using(var doc=System.Text.Json.JsonDocument.Parse(reader.ReadToEnd()))
 Check(doc.RootElement.GetProperty("properties").GetProperty("penetrationPerCalibre").GetDouble()==1.2,"initial native HEAT factor preserved");
Check(ShellEraPolicy.Allowed(storedAtgm with{Behavior="heat"},NativeContext(3,"heat")),"1940 HEAT boundary is available by Midwar");
Check(ShellChemicalBudget.Resolve(stockHeatBudget,85,1.2)==102,"1940 native heat factor baseline");
Check(ShellChemicalBudget.Resolve(stockHeatBudget,85,4)==340,"postwar native heat factor baseline");
Console.WriteLine($"FR011 NATIVE DATA PASS: {checks} checks.");

foreach(var p in ShellProfiles.Parse(ReleaseProfiles.Defaults()))Check(AtgmLauncherModel.Allows(p)==(p.Behavior=="atgm"),"dedicated launcher admits only launcher ATGM profiles");
Check(!AtgmLauncherModel.Allows(null),"dedicated launcher cannot fall back to native shell");
Check(Guid.TryParse(AtgmLauncherModel.Guid,out var launcherGuid)&&launcherGuid!=Guid.Empty,"own stable part GUID");
Check(AtgmLauncherModel.Boxes.Length==5,"four walls and mount, no closed muzzle plate");
foreach(var box in AtgmLauncherModel.Boxes)
{
 Check(box.Width>0&&box.Height>0&&box.Length>0,"valid own model box dimensions");
 Check(box.Z+box.Length/2<=.00001,"all cosmetic geometry behind native muzzle");
}
Check(AtgmLauncherModel.Boxes.Where(b=>b.Name!="mount").All(b=>Math.Abs(b.Z+.65)<.0001&&Math.Abs(b.Length-1.3)<.0001),"generic1300mm tube envelope");
Check(AtgmLauncherModel.Boxes.Where(b=>b.Name!="mount").All(b=>Math.Abs(b.X)>0.1||Math.Abs(b.Y)>0.1),"muzzle centre remains open");
Console.WriteLine($"FR001 LAUNCHER MODEL/GATE PASS: {checks} checks.");
var rearTube=AtgmLauncherModel.SegmentBoxes(.8f,-1f,true);
var frontTube=AtgmLauncherModel.SegmentBoxes(1f,0,false);
Check(rearTube.Length==5&&frontTube.Length==4,"only native rear group includes mount");
Check(rearTube.All(b=>b.Z+b.Length/2<=-1+.00001&&b.Z-b.Length/2>=-1.8-.00001),"rear box follows native group bounds");
Check(frontTube.All(b=>Math.Abs(b.Z+b.Length/2)<.00001&&Math.Abs(b.Z-b.Length/2+1)<.00001),"front box follows native muzzle and group length");
Check(rearTube.Concat(frontTube).Where(b=>b.Name!="mount").All(b=>Math.Abs(b.X)>.1||Math.Abs(b.Y)>.1),"all native group muzzle centres remain open");
Check(AtgmLauncherModel.SegmentBoxes(.05f,0,true).All(b=>b.Z-b.Length/2>=-.05001&&b.Z+b.Length/2<=.00001),"short native segments keep mount within segment");
foreach(var length in new[]{0f,-1f,float.NaN,float.PositiveInfinity})
{
 bool invalid=false;try{AtgmLauncherModel.SegmentBoxes(length,0,false);}catch(ArgumentOutOfRangeException){invalid=true;}
 Check(invalid,"invalid native segment length rejected");
}
Console.WriteLine($"FR001 NATIVE SEGMENT GEOMETRY PASS: {checks} checks.");
Check(AtgmAmmoBoxRules.Matches(true,"saclos","saclos",120,120,440,440,2),"box matches at exact reach");
Check(!AtgmAmmoBoxRules.Matches(false,"saclos","saclos",120,120,440,440,1),"cross vehicle box rejected even identical cannon sizes");
Check(!AtgmAmmoBoxRules.Matches(true,"saclos","mclos",120,120,440,440,1),"profile identity separates otherwise same native ammo");
Check(!AtgmAmmoBoxRules.Matches(true,"saclos","saclos",135,120,440,440,1),"calibre mismatch rejected");
Check(!AtgmAmmoBoxRules.Matches(true,"saclos","saclos",120,120,100,440,1),"propellant mismatch rejected");
foreach(var d in new[]{2.001,-1,double.NaN,double.PositiveInfinity})Check(!AtgmAmmoBoxRules.Matches(true,"saclos","saclos",120,120,440,440,d),"invalid/out of reach box rejected");
Check(Math.Abs(AtgmAmmoBoxRules.Seconds(135,100)-15)<.00001,"reference missile reload calibrated at15s");
Check(AtgmAmmoBoxRules.Seconds(180,440)>AtgmAmmoBoxRules.Seconds(120,440),"larger missile cycle slower");
Check(AtgmAmmoBoxRules.MechanismMass(8)==26&&AtgmAmmoBoxRules.MechanismCost(8)==570,"eight round mechanism mass/cost premium");
Check(Guid.TryParse(AtgmAmmoBoxRules.Guid,out var boxGuid)&&boxGuid!=launcherGuid,"box unique stable GUID");
Console.WriteLine($"FR001 BOX RULES PASS: {checks} checks.");
Check(!AtgmAmmoBoxRules.WaivesLoader(false,true,19,19,true),"vehicle local VUID collision never waives foreign loader");
Check(!AtgmAmmoBoxRules.WaivesLoader(true,false,19,19,true),"ordinary cannon loader never waived");
Check(!AtgmAmmoBoxRules.WaivesLoader(true,true,18,19,true),"other cannon on same vehicle loader never waived");
Check(!AtgmAmmoBoxRules.WaivesLoader(true,true,19,19,false),"incompatible box never waives loader");
Check(AtgmAmmoBoxRules.WaivesLoader(true,true,19,19,true),"matching own launcher loader waived");
Console.WriteLine($"FR001 LOADER SCOPE PASS: {checks} checks.");
var saclosReference=AtgmAmmoBoxRules.ReferenceLabel("Unnamed Cannon",120,421,"Konkurs","sight");
var mclosReference=AtgmAmmoBoxRules.ReferenceLabel("Unnamed Cannon",120,422,"MCLOS custom","keyboard");
Check(saclosReference.Contains("SACLOS")&&saclosReference.Contains("421"),"reference names SACLOS and native ID");
Check(mclosReference.Contains("MCLOS")&&mclosReference.Contains("MCLOS custom")&&mclosReference.Contains("422"),"reference names MCLOS profile and native ID");
Check(saclosReference!=mclosReference,"same name/calibre reference entries distinguish profiles");
Check(AtgmAmmoBoxRules.ReferenceLabel("Unnamed Cannon",120,423,"Top attack custom","topattack").Contains("TOP ATTACK"),"top-attack reference labelled TOP ATTACK");
Check(AtgmAmmoBoxRules.ReferenceLabel("Unnamed Cannon",120,422,null,null).Contains("profile unavailable"),"unavailable reference clearly labelled");
Console.WriteLine($"FR001 REFERENCE UI PASS: {checks} checks.");

Check(AtgmOpticalScope.Allows(true,true,1,false,true,true),"unique player native own weapon sight accepted");
Check(!AtgmOpticalScope.Allows(false,true,1,false,true,true),"foreign vehicle optical sight rejected");
Check(!AtgmOpticalScope.Allows(true,false,1,false,true,true),"inactive optical sight rejected");
foreach(var n in new[]{0,2})Check(!AtgmOpticalScope.Allows(true,true,n,false,true,true),"missing/ambiguous own sight association rejected");
Check(!AtgmOpticalScope.Allows(true,true,1,true,true,true),"ordinary shared sight rejected");
Check(!AtgmOpticalScope.Allows(true,true,1,false,false,true),"native weapon sight mismatch rejected");
Check(!AtgmOpticalScope.Allows(true,true,1,false,true,false),"non ATGM profile rejected");
Console.WriteLine($"FR001 OPTICAL SCOPE PASS: {checks} checks.");

var initialLedger=new AtgmInitialRoundLedger();
Check(!initialLedger.Claim(0,421,true,true,true),"no native instance no initial round");
Check(!initialLedger.Claim(1,421,false,true,true),"ordinary weapon no initial round");
Check(!initialLedger.Claim(1,421,true,false,true),"invalid profile no initial round");
Check(!initialLedger.Claim(1,421,true,true,false),"used task no initial round");
Check(initialLedger.Claim(1,421,true,true,true),"first own launcher eligible once");
foreach(var eventName in new[]{"rebuild","enable","profile switch","firegroup","scope","same instance new task"})Check(!initialLedger.Claim(1,421,true,true,true),"no refill on "+eventName);
Check(initialLedger.Claim(1,422,true,true,true),"second launcher independent");
Check(initialLedger.Claim(2,421,true,true,true),"same VUID other native vehicle independent");
initialLedger.End(1);
Check(!initialLedger.Claim(2,421,true,true,true),"ending one vehicle keeps other ledger");
Check(initialLedger.Claim(3,421,true,true,true),"fresh native Play has standard round");
Console.WriteLine($"FR001 INITIAL ROUND LIFETIME PASS: {checks} checks.");

var nativeRoundCost=AtgmInitialRoundResources.Cost(135,100,20);
Check(Math.Abs(nativeRoundCost.Assembly-21.565)<.01,"native one-round assembly cost reference");
Check(nativeRoundCost.Material>20*.48599f,"native material includes propellant volume and round mass");
Check(AtgmInitialRoundResources.Cost(135,200,20).Material>nativeRoundCost.Material,"native propellant cost scales with length");
Check(AtgmInitialRoundResources.Cost(160,100,20).Assembly>nativeRoundCost.Assembly,"native larger calibre assembly cost increases");
Console.WriteLine($"FR001 INITIAL ROUND RESOURCES PASS: {checks} checks.");

Check(AtgmInitialAssemblyRules.EmptyFirstAssembly(true,3,-1,0),"first NeverLoaded supported");
Check(AtgmInitialAssemblyRules.EmptyFirstAssembly(true,2,-1,0),"first native Unloaded supported without resetting state");
Check(!AtgmInitialAssemblyRules.EmptyFirstAssembly(false,2,-1,0),"repeat assembly not initial grant");
Check(!AtgmInitialAssemblyRules.EmptyFirstAssembly(true,2,-1,10),"native previously fired weapon rejected");
Check(!AtgmInitialAssemblyRules.EmptyFirstAssembly(true,0,19,0),"already loaded native round not overwritten");
Check(!AtgmInitialAssemblyRules.EmptyFirstAssembly(true,1,-1,0),"native in progress loading not overridden");
Check(!AtgmInitialAssemblyRules.EmptyFirstAssembly(true,2,19,0),"valid loaded type blocks ambiguous initial state");
Check(AtgmInitialAssemblyRules.NeedsLauncherDefault(true,null)&&AtgmInitialAssemblyRules.NeedsLauncherDefault(true,ShellProfiles.Vanilla),"own unselected/Vanilla default migration");
foreach(var id in new[]{"saclos","mclos","custom_atgm","unavailable_custom"})Check(!AtgmInitialAssemblyRules.NeedsLauncherDefault(true,id),"existing explicit profile retained "+id);
Check(!AtgmInitialAssemblyRules.NeedsLauncherDefault(false,ShellProfiles.Vanilla),"ordinary Vanilla unchanged");
Console.WriteLine($"FR001 INITIAL ASSEMBLY/DEFAULT PASS: {checks} checks.");
Check(AtgmOpticalScope.ValidProjection(.5f,.5f,100),"front optical target projects");
Check(!AtgmOpticalScope.ValidProjection(.5f,.5f,0),"camera plane cannot define mask");
Check(!AtgmOpticalScope.ValidProjection(.5f,.5f,-100),"behind-camera optical target rejected");
Check(!AtgmOpticalScope.ValidProjection(float.NaN,.5f,100),"nonfinite optical coordinate rejected");
Check(!AtgmOpticalScope.ValidProjection(.5f,float.PositiveInfinity,100),"nonfinite vertical coordinate rejected");
Check(!AtgmOpticalScope.ValidProjection(.5f,.5f,float.NaN),"nonfinite depth rejected");
Console.WriteLine($"FR001 OPTICAL MASK PROJECTION PASS: {checks} checks.");

Check(PluginIdentity.PluginId=="sprocket.expandedballistics","plugin identity is the Expanded Ballistics GUID");
Console.WriteLine($"PLUGIN IDENTITY PASS: {checks} checks.");

var editorDir=Path.Combine(SprocketExpandedBallistics.Fix02228Tests.FixtureRoot(),"SprocketShellEditor-"+Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(editorDir);
try
{
    var json=ReleaseProfiles.Defaults();
    var edit=new ShellEditorDraft(json);
    Check(System.Text.Json.Nodes.JsonNode.DeepEquals(edit.Root,System.Text.Json.Nodes.JsonNode.Parse(edit.Serialize())),"editor round-trip preserves all preset settings");
    var originalIds=ShellProfiles.Parse(json).Select(p=>p.Id).ToArray();
    edit.Set(0,"label","Editor custom name");
    edit.Set(0,"maximumVelocity","2100,5");
    Check(ShellProfiles.Parse(edit.Serialize())[0].Settings.MaxVelocity==2100.5,"editor accepts decimal comma as numeric input");
    Check(ShellProfiles.Parse(edit.Serialize()).Select(p=>p.Id).SequenceEqual(originalIds),"editor preserves saved IDs");
    bool rejected=false;try{edit.Set(0,"id","changed");}catch(InvalidOperationException){rejected=true;}Check(rejected,"editor rejects ID changes");
    rejected=false;try{edit.Set(0,"maximumVelocity","NaN");}catch(FormatException){rejected=true;}Check(rejected,"editor rejects non-finite values");
    var duplicate=edit.Duplicate(edit.Profiles[0]!.AsObject());
    Check(ShellProfiles.Parse(edit.Serialize())[duplicate].Id!=originalIds[0],"duplicate gets unique identity");
    var path=Path.Combine(editorDir,"shells.json");File.WriteAllText(path,json);
    var backup=edit.Save(path);
    Check(File.ReadAllText(backup)==json,"save backs up original bytes");
    Check(File.ReadAllText(path)==edit.Serialize(),"save atomically commits validated draft");
    var changed=new ShellEditorDraft(File.ReadAllText(path));File.AppendAllText(path,"\n");var external=File.ReadAllText(path);
    rejected=false;try{changed.Save(path);}catch(IOException){rejected=true;}Check(rejected&&File.ReadAllText(path)==external,"external edits are never overwritten");
    var invalid=new ShellEditorDraft(json);invalid.Set(0,"maximumVelocity","-1");File.WriteAllText(path,json);
    rejected=false;try{invalid.Save(path);}catch(Exception){rejected=true;}Check(rejected&&File.ReadAllText(path)==json,"invalid draft cannot overwrite file");
    var limited=new ShellEditorDraft(json);while(limited.Profiles.Count<ShellProfiles.MaximumProfiles)limited.Duplicate(limited.Profiles[0]!.AsObject());
    rejected=false;try{limited.Duplicate(limited.Profiles[0]!.AsObject());}catch(InvalidOperationException){rejected=true;}Check(rejected&&limited.Profiles.Count==ShellProfiles.MaximumProfiles,"editor enforces profile capacity");
    var cancelled=new ShellEditorDraft(json);cancelled.Set(0,"label","Cancelled draft");Check(File.ReadAllText(path)==json,"draft changes never touch file before save");
}
finally{Directory.Delete(editorDir,true);}
Console.WriteLine($"SHELL EDITOR PASS: {checks} checks.");

foreach(var behavior in new[]{"ap","apfsds","aphe","he","heat","hesh","atgm","atgm_gun"})
{
 var editorConversion=new ShellEditorDraft(ReleaseProfiles.Defaults());
 var oldId=editorConversion.Profiles[0]!["id"]!.GetValue<string>();
 editorConversion.ChangeBehavior(0,behavior);
 var parsedConversion=ShellProfiles.Parse(editorConversion.Serialize())[0];
 Check(parsedConversion.Behavior==behavior&&parsedConversion.Id==oldId,"behavior dropdown conversion validates and preserves identity: "+behavior);
}
Console.WriteLine($"SHELL EDITOR CHOICES PASS: {checks} checks.");
ModularChecks.Run(Check);
Console.WriteLine($"MODULAR SHELL COMPILER PASS: {checks} checks.");
FunctionalChecks.Run(Check);
LongRodTests.Run(Check);
Console.WriteLine($"FUNCTIONAL MODULAR SHELL PASS: {checks} checks.");

WireChecks.Run(Check);
WireSettlingChecks.Run(Check);
Console.WriteLine($"WIRE INTEGRATION PURE PASS: {checks} checks.");

using var towStream=typeof(ShellExampleProfiles).Assembly.GetManifestResourceStream("ExpandedBallistics.TowExample")!;
using var towReader=new StreamReader(towStream);
var towRow=System.Text.Json.Nodes.JsonNode.Parse(towReader.ReadToEnd())!.AsObject();
var towEnvelope=new System.Text.Json.Nodes.JsonObject {["schemaVersion"]=1,["profiles"]=new System.Text.Json.Nodes.JsonArray(towRow.DeepClone())};
var towProfile=ShellProfiles.Parse(towEnvelope.ToJsonString()).Single();
Check(towProfile.Flight==new ShellFlight("rocket","sight","launcher")&&towProfile.Behavior=="heat"&&towProfile.Wire?.Transport=="wire","TOW template compiles independent rocket/HEAT/SACLOS/wire modules");
Check(Math.Abs(ShellBalance.ChemicalPenetration(towProfile,152.4)-430)<1e-6&&towProfile.Wire?.MaximumLength==3000,"TOW reference calibre chemical calibration and basic wire budget");
Check(ShellExampleProfiles.Templates().Count==System.Text.Json.Nodes.JsonNode.Parse(ReleaseProfiles.Defaults())!["profiles"]!.AsArray().Count,"editor templates contain production defaults only");
var towDraft=new ModularShellDraft(ReleaseProfiles.Defaults());
var towIndex=towDraft.Duplicate(ModularShellDraft.FromLegacy(towRow));
var towCompiled=ShellProfiles.Parse(towDraft.Compile().RuntimeJson!)[towIndex];
Check(towCompiled.Flight==towProfile.Flight&&towCompiled.Wire==towProfile.Wire&&towCompiled.Atgm==towProfile.Atgm,"TOW authoring and compilation preserves wire/motor/guidance calibration");
Console.WriteLine($"CHEMICAL / TOW PASS: {checks} checks.");



var reliktShared=new EraCells();var reliktOwned=new PlacedEraState();
var oldPlateCell=new EraCell(1,10,2,3,4);reliktShared.Consume(oldPlateCell);
var brickKey=PlacedEraState.Key(1,11);reliktShared.Consume(brickKey);reliktOwned.Track(brickKey);
Check(!reliktShared.Consume(PlacedEraState.Key(1,11)),"one whole brick canonical key cannot rearm by hit position or surface");
Check(reliktOwned.Spent(reliktShared,1,11)&&!reliktOwned.Spent(reliktShared,2,11),"brick consumption isolated by spawn generation");
Check(reliktShared.Consume(PlacedEraState.Key(1,12)),"distinct brick independent canonical VUID");reliktOwned.Track(PlacedEraState.Key(1,12));
reliktOwned.Reset(reliktShared);Check(reliktShared.Contains(oldPlateCell)&&!reliktOwned.Spent(reliktShared,1,11)&&!reliktOwned.Spent(reliktShared,1,12),"Edit reset clears newbrick cells only, preserves old plate ERA");
Check(reliktShared.Consume(brickKey),"new combat may consume reset brick again");
Console.WriteLine($"PLACED ERA PURE PASS: {checks} checks.");

Check(DamageFeedTruth.Outcome(false,true,false,1)==PlateOutcome.Unknown,"embedded continuation is not final armour stop");
Check(DamageFeedTruth.Outcome(false,false,false,0)==PlateOutcome.Unknown,"killed or ended chain alone does not prove armour stop");
Check(DamageFeedTruth.Outcome(true,false,false,1)==PlateOutcome.Penetrated,"native penetration flag reports penetration");
Check(DamageFeedTruth.Outcome(false,true,false,0)==PlateOutcome.Stopped,"native embedded final original reports stop");
Check(DamageFeedTruth.Outcome(false,false,true,1)==PlateOutcome.Deflected,"native deflection reports deflection");
Check(DamageFeedTruth.Outcome(true,false,false,1,true)==PlateOutcome.Unknown,"native exception cannot report success");
Check(DamageFeedTruth.Remaining(Array.Empty<double>())==null,"no original continuation omits residual");
Check(DamageFeedTruth.Remaining(new[]{300d,280d})==null,"ambiguous original continuation omits residual");
Check(DamageFeedTruth.Remaining(new[]{42d})==42,"one actual original continuation reports residual");
Check(DamageFeedTruth.Remaining(new[]{double.NaN})==null,"nonfinite residual omitted");
Check(!DamageFeedTruth.Died(null,false,-1),"unknown alive baseline cannot claim kill");
Check(!DamageFeedTruth.Died(false,false,-1),"already dead pool cannot claim another kill");
Check(DamageFeedTruth.Died(true,false,-1),"observed native alive to dead negative health change proves kill");
Check(!DamageFeedTruth.Died(true,false,1),"positive health event cannot claim kill");
Check(new ComponentDamage("destroyed",true).Merge(new("damaged",false)).Text=="destroyed","later spall cannot downgrade destruction");
Check(!new ComponentDamage("damaged",false).Merge(new("wounded",false)).Fatal,"damage cannot become inferred death");
Check(DamageFeedTruth.Label("<Custom>\nplate","fallback")=="Customplate","custom labels plain sanitized text");
Check(DamageFeedTruth.Label("  ","Plate #4")=="Plate #4","empty name uses truthful ID fallback");
Check(DamageFeedTruth.Label(new string('x',100),"fallback").Length==48,"native labels bounded (0.22.81: 48 characters)");
Check(DamageFeedTruth.StructureLabel("Upper glacis","Unnamed Structure","Plate #4")=="Upper glacis","saved instance name overrides unnamed template");
Check(DamageFeedTruth.StructureLabel("Driver bulkhead","Turret","Plate #4")=="Driver bulkhead","saved instance label wins over differently named template");
Check(DamageFeedTruth.StructureLabel(null,"Front plate","Plate #4")=="Front plate","missing instance mesh uses named template");
Check(DamageFeedTruth.StructureLabel("<>\r\n", "Front plate","Plate #4")=="Front plate","sanitized-empty instance name uses template");
Check(DamageFeedTruth.StructureLabel(null,null,"Plate #196")=="Plate #196","missing names retain exact component ID fallback");
Check(DamageFeedTruth.StructureLabel("  ","<>\n","Plate #196")=="Plate #196","empty sanitized names retain truthful ID fallback");
Check(DamageFeedTruth.StructureLabel("<Upper>\nglacis","Wrong template","Plate #4")=="Upperglacis","instance labels sanitized consistently");
Check(DamageFeedTruth.StructureLabel(new string('x',100),"template","Plate #4").Length==48,"saved instance labels bounded for HUD (0.22.81: 48 characters)");
var feed=new DamageFeedBuffer();feed.Add(1,new[]{"ERA 500 -> 346 mm","Penetrated plate 40 mm left"});
Check(feed.Visible(8.99).Count==2,"ERA and layer outcomes coexist until expiration");
Check(feed.Visible(9).Count==0,"feed rows expire at eight seconds");
feed.Add(10,Enumerable.Range(0,20).Select(i=>i.ToString()));feed.Add(11,new[]{"latest"});
Check(feed.Visible(11).Count==12&&feed.Visible(11).Last().Text=="latest","bounded feed retains newest rows");
Check(feed.Visible(0).Count==0,"new combat clock does not retain future rows");
feed.Add(double.NaN,new[]{"invalid"});Check(feed.Visible(0).Count==0,"nonfinite timestamp cannot enqueue");
Console.WriteLine($"DAMAGE FEED PURE PASS: {checks} checks.");

var boxVertices=PlacedBrickGeometry.Triangles.Select(i=>PlacedBrickGeometry.Corners[i]).ToArray();
Check(PlacedBrickGeometry.MeshMatches(boxVertices,Enumerable.Range(0,36).ToArray()),"exact current native collision triangle soup validated");
var changedBox=boxVertices.ToArray();changedBox[0]+=new System.Numerics.Vector3(.001f,0,0);
Check(!PlacedBrickGeometry.MeshMatches(changedBox,Enumerable.Range(0,36).ToArray()),"unknown altered collision cannot get declared depth");
Check(!PlacedBrickGeometry.UnitScale(new(1,2,1)),"scaled ownpart not implicitly granted fixed cassette gate");
Check(PlacedBrickGeometry.UnitScale(new(-1,1,1)),"mirrored unit geometry admissible");
Check(PlacedBrickGeometry.Chord(new(0,.096f,0),new(0,.026f,0),new(0,-1,0),out var boxDepth)&&Math.Abs(boxDepth-70)<.001,"full native chord proves physical depth");
var edgeDir=System.Numerics.Vector3.Normalize(new System.Numerics.Vector3(1,-.4f,0));var edgeEntry=new System.Numerics.Vector3(.20f,.096f,0);var edgeExit=edgeEntry+edgeDir*(.012f/edgeDir.X);
Check(PlacedBrickGeometry.Chord(edgeEntry,edgeExit,edgeDir,out var edgeDepth)&&Math.Abs(edgeDepth-70)<.001,"short oblique edge chord still belongs validated physical70mm brick");
Check(!PlacedBrickGeometry.Chord(new(0,.05f,0),new(0,.026f,0),new(0,-1,0),out _),"inside start cannot masquerade as first surface impact");
Check(!PlacedBrickGeometry.Chord(new(0,.096f,0),new(0,.026f,0),new(0,1,0),out _),"outward local surface trajectory rejected");
Check(!PlacedBrickGeometry.Chord(new(0,.096f,0),new(0,-.2f,0),new(0,-1,0),out _),"merged foreign exit cannot prove own collision chord");
Check(!PlacedBrickGeometry.Chord(new(.4f,.096f,0),new(.4f,.026f,0),new(0,-1,0),out _),"surface outside native box rejected");
var ownHits=new[]{new PlacedBrickGeometry.Boundary(2,3,3,false,true,false),new PlacedBrickGeometry.Boundary(1,3,1,true,false,false),new PlacedBrickGeometry.Boundary(1,3,2,false,true,false),new PlacedBrickGeometry.Boundary(2,3,2.1f,true,false,false)};
Check(PlacedBrickGeometry.OwnExit(ownHits,1,3)==2,"unsorted merged native span selects actually reached first own exit");
Check(PlacedBrickGeometry.OwnExit(ownHits,3,2.5f)==-1,"unreached later own exit outside nativeblock not invented");
Check(PlacedBrickGeometry.OwnExit(ownHits,1,1.5f)==-1,"future exit beyond reached block rejected");
Check(PlacedBrickGeometry.OwnExit(ownHits.Concat(new[]{ownHits[2]}).ToArray(),1,3)==-1,"ambiguous coincident own exit rejected");
var insideHits=ownHits.ToArray();insideHits[1]=insideHits[1] with {Inside=true};
Check(PlacedBrickGeometry.OwnExit(insideHits,1,3)==2,"mutable native InsideBlock marker is not projectile-inside geometry");
var structureHits=ownHits.ToArray();structureHits[2]=structureHits[2] with {Structure=4};
Check(PlacedBrickGeometry.OwnExit(structureHits,1,3)==-1,"different native structure cannot supply own closed exit");
Console.WriteLine($"PLACED BRICK GEOMETRY PASS: {checks} checks.");

Check(PlacedBrickTrigger.Reached(true,true,true,false,1,70),"actual native entry sufficient without own exit chord");
Check(PlacedBrickTrigger.Reached(true,true,true,false,0,70),"reached partial/merged native entry permits fixedpart calibration");
Check(!PlacedBrickTrigger.Reached(false,true,true,false,1,70),"mount/othercomponent/missedcassette cannot trigger");
Check(!PlacedBrickTrigger.Reached(true,true,false,true,1,70),"native exit alone cannot trigger");
Check(!PlacedBrickTrigger.Reached(true,false,true,false,1,70),"spall fragment cannot activate future cassette");
Check(!PlacedBrickTrigger.Reached(true,true,true,false,double.NaN,70),"invalid native distance rejected");
Check(!PlacedBrickTrigger.Reached(true,true,true,false,-1,70),"entry behind actual native original rejected");
Check(!PlacedBrickTrigger.Reached(true,true,true,false,1,0),"absent physical collisiondepth cannot establish gate");
Check(PlacedBrickTrigger.CanConsume(true,true,false),"same physicaldepth eligibility activates regardlessshortnativechord");
Check(!PlacedBrickTrigger.CanConsume(true,false,false),"wrongfixedpart physicaldepth cannotconsume");
Check(PlacedBrickTrigger.CanConsume(true,true,false),"angular weight 0 lets the placed block detonate (no weight parameter; the effect stays in ArmourResponseModel.Apply)");
Check(!PlacedBrickTrigger.CanConsume(false,true,false),"nonthreat cannotconsume");
Check(!PlacedBrickTrigger.CanConsume(true,true,true),"same native block cannotconsume twice");
var reachedCells=new EraCells();var reachedCell=PlacedEraState.Key(50,836);
Check(reachedCells.Consume(reachedCell)&&!reachedCells.Consume(reachedCell),"partial/merged/centre native entries share one wholebrick consumedstate");
Check(reachedCells.Consume(PlacedEraState.Key(50,837)),"another actuallyreached brick retains independentcell");
Console.WriteLine($"PLACED NATIVE HIT TRIGGER PASS: {checks} checks.");

string EraFixture(string name){using var stream=typeof(ShellProfiles).Assembly.GetManifestResourceStream("EraPreset."+name)!;return new StreamReader(stream).ReadToEnd();}
var presetBindings=SprocketExpandedBallistics.Era.EraPartBindings.Parse(EraFixture("sprocket.expandedballistics.era.bindings.json"));
Check(presetBindings.Bindings.Count==4,"strict explicit legacyplus3 cassette routes");
Check(presetBindings.TryGetCassette("kontakt1Cassette",out var k1Binding)&&k1Binding.Kind=="lightEra"&&k1Binding.ResponseId=="kontakt1Cassette","K1 ownexactlight route");
Check(presetBindings.TryGetCassette("kontakt5Cassette",out var k5Binding)&&k5Binding.Kind=="heavyEra"&&k5Binding.ResponseId=="kontakt5Cassette","K5 ownexactheavy route");
Check(presetBindings.TryGetCassette("reliktTurretCassette",out var turretBinding)&&turretBinding.MaterialId=="reliktCassette"&&turretBinding.ResponseId=="reliktCassette","turretshape sharesacceptedReliktrecipe");
Check(!presetBindings.TryGetCassette("reliktTurretMount",out _)&&presetBindings.TryGetMount("reliktTurretMount",out _),"turretmount remainsstrictlypassive");
Check(!presetBindings.TryGetCassette("reliktCassette_custom",out _)&&!presetBindings.TryGetCassette("ReliktCassette",out _),"no prefixorcasefoldfallback");
Check(!k1Binding!.MatchesRecipe("kontakt5Cassette","heavyEra",new[]{"kontakt1Cassette"}),"recipeID/kind cannotroute tofirstheavyrecord");
Check(!k5Binding!.MatchesRecipe("kontakt5Cassette","heavyEra",new[]{"reliktCassette"}),"exactownmaterial route required");
try{SprocketExpandedBallistics.Era.EraPartBindings.Parse(EraFixture("sprocket.expandedballistics.era.bindings.json").Replace("kontakt1Mount","reliktMount"));Check(false,"conflictingroles rejected");}catch(FormatException){Check(true,"conflictingroles rejected");}
(System.Numerics.Vector3[] Vertices,int[] Triangles) ReadBox(string name)
{
 var vertices=new List<System.Numerics.Vector3>();var tris=new List<int>();
 foreach(var line in EraFixture(name).Split('\n')){var fields=line.Split(' ',StringSplitOptions.RemoveEmptyEntries);if(fields.Length==0)continue;if(fields[0]=="v")vertices.Add(new(float.Parse(fields[1],System.Globalization.CultureInfo.InvariantCulture),float.Parse(fields[2],System.Globalization.CultureInfo.InvariantCulture),float.Parse(fields[3],System.Globalization.CultureInfo.InvariantCulture)));if(fields[0]=="f")foreach(var index in fields.Skip(1))tris.Add(int.Parse(index)-1);}
 return(vertices.ToArray(),tris.ToArray());
}
foreach(var fixturePair in new[]{("kontakt1-flat-collision.obj",30d),("kontakt5-flat-collision.obj",70d),("relikt-turret-30-collision.obj",70d)})
{
 var box=ReadBox(fixturePair.Item1);
 Check(Math.Abs(PlacedPartDepth.Measure(box.Vertices,box.Triangles,System.Numerics.Vector3.One)-fixturePair.Item2)<.001,"actualnativepreset intrinsicdepth "+fixturePair.Item1);
 Check(Math.Abs(PlacedPartDepth.Measure(box.Vertices,box.Triangles,new(-1,1,1))-fixturePair.Item2)<.001,"mirroring preservesintrinsicdepth "+fixturePair.Item1);
 var rotated=box.Vertices.Select(v=>System.Numerics.Vector3.Transform(v,System.Numerics.Quaternion.CreateFromYawPitchRoll(.4f,.2f,.1f))).ToArray();
 Check(Math.Abs(PlacedPartDepth.Measure(rotated,box.Triangles,System.Numerics.Vector3.One)-fixturePair.Item2)<.001,"rotationnot doublecounted inphysicaldepth "+fixturePair.Item1);
}
var turretBox=ReadBox("relikt-turret-30-collision.obj");
Check(turretBox.Vertices.Max(v=>v.Y)-turretBox.Vertices.Min(v=>v.Y)>.19,"rotatedAABBheight190mm isnot70mmcalibration");
Check(Math.Abs(PlacedPartDepth.Measure(turretBox.Vertices,turretBox.Triangles,new(2,2,2))-140)<.001,"physicalmodelscale affectsactualdepth notnominalpath");
Check(double.IsNaN(PlacedPartDepth.Measure(turretBox.Vertices,new[]{0,0,0},System.Numerics.Vector3.One)),"degeneratecollision cannotestablishcalibration");
Check(double.IsNaN(PlacedPartDepth.Measure(turretBox.Vertices,turretBox.Triangles,new(1,0,1))),"collapsedgeometry cannotestablishcalibration");
using(var responseFixture=typeof(ShellProfiles).Assembly.GetManifestResourceStream("ArmourResponse.Fixture")!)
{
 var root=System.Text.Json.Nodes.JsonNode.Parse(new StreamReader(responseFixture).ReadToEnd())!;var oldCount=root["responses"]!.AsArray().Count;
 root["responses"]!.AsArray().Add(System.Text.Json.Nodes.JsonNode.Parse(EraFixture("kontakt1Cassette-response.json")));root["responses"]!.AsArray().Add(System.Text.Json.Nodes.JsonNode.Parse(EraFixture("kontakt5Cassette-response.json")));
 var cfg=ArmourResponses.Parse(root.ToJsonString()) with {Enabled=true};var k1=cfg.Responses.Single(r=>r.ResponseId=="kontakt1Cassette");var k5=cfg.Responses.Single(r=>r.ResponseId=="kontakt5Cassette");
 Check(cfg.Responses.Count==oldCount+2,"new recipes addwithoutreplacing oldplate recipes");
 Check(k1Binding.MatchesRecipe(k1.ResponseId,k1.Kind,k1.CompatibleMaterialIds)&&k5Binding.MatchesRecipe(k5.ResponseId,k5.Kind,k5.CompatibleMaterialIds),"binding+strictresponse parser exactbothnewroutes");
 Check(ArmourResponseModel.ReactiveThreat(k1,"heat")&&!ArmourResponseModel.ReactiveThreat(k1,"apfsds"),"K1reactsHEAT only noKE benefit");
 Check(Math.Abs(ArmourResponseModel.Apply(cfg,k1,new ArmourShotState(),"K1","heat",true,true,30,0,double.NaN,true)-.62)<1e-9,"K1starterHEAT.62");
 Check(Math.Abs(ArmourResponseModel.Apply(cfg,k5,new ArmourShotState(),"K5","heat",true,true,70,30,double.NaN,true)-.55)<1e-9,"K5starterHEAT.55 distinctRelikt.50");
 Check(Math.Abs(ArmourResponseModel.Apply(cfg,k5,new ArmourShotState(),"K5","apfsds",true,true,70,30,double.NaN,true)-.90)<1e-9,"K5starterrod.90 distinctRelikt.85");
 Check(ArmourResponseModel.Apply(cfg,k5,new ArmourShotState(),"K5","heat",true,true,70,30,double.NaN,false)==1,"spentK5cannotreactivate");
}
var mixedCells=new EraCells();foreach(int vuid in new[]{100,101,102,103})Check(mixedCells.Consume(PlacedEraState.Key(10,vuid)),"same/sharedrecipe distinctVUID independentwholebrick");
Check(!mixedCells.Consume(PlacedEraState.Key(10,103)),"turretsharedrecipe retainsoneshotledger");
Console.WriteLine($"STRICT ERA PRESET PASS: {checks} checks.");

var eraHud=new DamageFeedActivations();
Check(!eraHud.Record("preview",false,true,"heavyEra","ERA plate"),"preview never emits realERAactivation");
Check(!eraHud.Record("spent",true,false,"heavyEra","ERA plate"),"spent repeat never emits newERAactivation");
Check(!eraHud.Record("passive",true,true,"glassTextolite","Composite plate"),"passive firsthit notERAactivation");
Check(eraHud.Record("plate:spawn1:vuid10:surface2:x0:y0",true,true,"lightEra","My ERA plate"),"actual firstlightERA platecell consumption emitsactivation");
Check(eraHud.Record("plate:spawn1:vuid10:surface2:x1:y0",true,true,"heavyEra","My ERA plate"),"distinctcell sameplate independentlyreportsfirstconsumption");
Check(!eraHud.Record("plate:spawn1:vuid10:surface2:x0:y0",true,true,"lightEra","duplicate"),"duplicate exactcell reportdeduplicated");
eraHud.Budget("absent",500,250);Check(eraHud.Rows.Count()==2,"budgetcalculation cannotinventconsumption");
eraHud.Budget("plate:spawn1:vuid10:surface2:x0:y0",500,310);Check(eraHud.Rows.First().Contains("500 -> 310 mm"),"knownactualreaction includesobservedbudget");
eraHud.Budget("plate:spawn1:vuid10:surface2:x1:y0",double.NaN,250);Check(eraHud.Rows.Last()=="ERA activated: My ERA plate","invalidbudgetcannothideactualfirstconsumption");
var eraPriorityRows=DamageFeedActivations.Compose(eraHud.Rows,Enumerable.Range(0,8).Select(i=>"health"+i),Enumerable.Range(0,12).Select(i=>"plate"+i)).ToArray();
Check(eraPriorityRows.Length==12&&eraPriorityRows.Take(2).All(r=>r.StartsWith("ERA activated"))&&eraPriorityRows.Count(r=>r.StartsWith("health"))==8,"manylayersandhealthcannottruncate actualERArows");
Check(eraHud.Record("brick1",true,true,"heavyEra","Relikt brick")&&eraHud.Record("brick2",true,true,"heavyEra","Duplet brick")&&!eraHud.Record("brick3",true,true,"heavyEra","Nizh brick"),"activationrecords bounded4 perimpactincludingplacedandplate");
Console.WriteLine($"PLATE ERA HUD PASS: {checks} checks.");

// Native energy/barrel-length recoil parity; terminal ballistic records remain untouched.
foreach(var recoilCalibre in new[]{20d,75d,120d,200d})
{
    var fullMass=1.59e-5*Math.Pow(recoilCalibre,3);
    var baseline=.5*fullMass*800*800/4;
    Check(ShellRecoil.Restore(baseline*.1,fullMass,800,4,false)==baseline,"light terminal proxy retains native full-bore charge recoil");
    Check(ShellRecoil.Restore(baseline*2,fullMass,800,4,false)==baseline*2,"stronger existing native impulse retained");
    Check(ShellRecoil.Restore(baseline*.1,fullMass,800,4,true)==baseline*.1,"missile/sabot carrier output retained");
    Check(ShellRecoil.Restore(7,fullMass,800,0,false)==7,"invalid native barrel retains output");
}
Check(ShellRecoil.Restore(7,1,400,4,false)*4==ShellRecoil.Restore(7,1,800,4,false),"native charge velocity squared scaling");
Check(ShellRecoil.Restore(7,1,800,8,false)*2==ShellRecoil.Restore(7,1,800,4,false),"native inverse barrel length scaling");
Check(ShellRecoil.Restore(7,double.NaN,800,4,false)==7,"invalid native mass retains output");
Check(ShellRecoil.Restore(7,1,double.PositiveInfinity,4,false)==7,"invalid native velocity retains output");
Console.WriteLine($"NATIVE RECOIL PASS: {checks} checks.");

Check(DamageFeedTruth.DirectDamage(true,true,-10),"direct projectile/spall damage emitted in exact native call");
Check(!DamageFeedTruth.DirectDamage(false,false,-10),"ongoing fire outside impact omitted");
Check(!DamageFeedTruth.DirectDamage(true,false,-10),"ambient health notification during shot omitted");
Check(!DamageFeedTruth.DirectDamage(false,true,-10),"native damage call without projectile impact omitted");
Check(!DamageFeedTruth.DirectDamage(true,true,0)&&!DamageFeedTruth.DirectDamage(true,true,10),"no damage or healing omitted");
Check(!DamageFeedTruth.DirectDamage(true,true,double.NaN),"invalid health delta omitted");
var directSameShot=new Dictionary<int,ComponentDamage>();
foreach(var d in new[]{new ComponentDamage("Transmission damaged",false),new ComponentDamage("Transmission damaged",false),new ComponentDamage("Transmission destroyed",true),new ComponentDamage("Transmission damaged",false)})
    directSameShot[1]=directSameShot.TryGetValue(1,out var priorDirect)?priorDirect.Merge(d):d;
Check(directSameShot.Count==1&&directSameShot[1].Fatal,"same-shot component fragments dedup with fatal priority");
Console.WriteLine($"DIRECT DAMAGE FEED PASS: {checks} checks.");

Check(!DamageFeedTruth.DirectDamage(true,true,-10,false),"unrelated register callback cannotbecreditedtonested directshot");
Console.WriteLine($"DIRECT REGISTER MATCH PASS: {checks} checks.");

DynamicEraTests.Run(Check);
Console.WriteLine($"Dynamic ERA included: {checks} checks passed.");

var capacitySeed=System.Text.Json.Nodes.JsonNode.Parse(ReleaseProfiles.Defaults())!["profiles"]![0]!.ToJsonString();
var capacityRoot=new System.Text.Json.Nodes.JsonObject{["schemaVersion"]=1,["profiles"]=new System.Text.Json.Nodes.JsonArray()};
for(var n=1;n<=32;n++)
{
 var row=System.Text.Json.Nodes.JsonNode.Parse(capacitySeed)!;row["id"]="capacity_"+n;row["label"]="Capacity "+n;capacityRoot["profiles"]!.AsArray().Add(row);
}
var capacity32=capacityRoot.ToJsonString();
Check(ShellProfiles.Parse(capacity32).Count==32,"runtime accepts32 unique valid profiles");
Check(ShellProfiles.Parse(new ModularShellDraft(capacity32).Compile().RuntimeJson!).Count==32,"modular compile accepts32 profiles");
var capacityExtra=System.Text.Json.Nodes.JsonNode.Parse(capacitySeed)!;capacityExtra["id"]="capacity_33";capacityExtra["label"]="Capacity33";capacityRoot["profiles"]!.AsArray().Add(capacityExtra);
var capacityRejected=false;try{ShellProfiles.Parse(capacityRoot.ToJsonString());}catch(FormatException){capacityRejected=true;}
Check(capacityRejected,"runtime rejects33 profiles");
capacityRejected=false;try{new ModularShellDraft(capacityRoot.ToJsonString());}catch(FormatException){capacityRejected=true;}
Check(capacityRejected,"modular import rejects33 profiles");
var capacityDraft=new ModularShellDraft(capacity32);
capacityRejected=false;try{capacityDraft.Duplicate(capacityDraft.Profiles[0]!.AsObject());}catch(InvalidOperationException){capacityRejected=true;}
Check(capacityRejected&&capacityDraft.Profiles.Count==32,"modular duplicate preserves32 limit");
Console.WriteLine($"Profile capacity PASS: {checks} checks.");

if(args.Length==2)
{
 var stagedRuntime=File.ReadAllText(args[0]);var stagedModules=File.ReadAllText(args[1]);
 var stagedProfiles=ShellProfiles.Parse(stagedRuntime);Check(stagedProfiles.Count==18,"staged15existing+3tests accepted");
 var stagedCompiled=new ModularShellDraft(stagedRuntime,stagedModules).Compile();
 Check(stagedCompiled.Ready&&System.Text.Json.Nodes.JsonNode.DeepEquals(System.Text.Json.Nodes.JsonNode.Parse(stagedRuntime),System.Text.Json.Nodes.JsonNode.Parse(stagedCompiled.RuntimeJson!)),"staged modular compiler returns identical runtime config");
 for(var n=1;n<=3;n++)
 {
  var profile=stagedProfiles.Single(p=>p.Id=="era_apfsds_test_"+n);
  var result=ShellBalance.Calculate(120,800,1800,profile);
  Check(profile.Behavior=="apfsds"&&Math.Abs(result.Length/result.Diameter-new[]{20d,40d,80d}[n-1])<1e-4,"staged real launch geometry LD "+n);
 }
 var extra=System.Text.Json.Nodes.JsonNode.Parse(stagedRuntime)!;
 var optional=System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory,"lm-grenade-test.profile-fragment.json")))!;
 extra["profiles"]!.AsArray().Add(optional);
 Check(ShellProfiles.Parse(extra.ToJsonString()).Count==19,"optional LM19th profile fits without changing18 existing");
 Check(new ModularShellDraft(extra.ToJsonString()).Compile().Ready,"optional LM19th profile modular import compiles");
 Console.WriteLine($"Staged exact configurations PASS: {checks} checks.");
}

NizhInsertTests.Run(Check);
Console.WriteLine($"Nizh additive PASS: {checks} checks.");

ThreatClassTests.Run(Check);
Console.WriteLine($"Threat class PASS: {checks} checks.");

PhysicalEraTests.Run(Check);
Console.WriteLine($"Physical ERA PASS: {checks} checks.");

TandemTests.Run(Check);
Console.WriteLine($"Tandem probe pure PASS: {checks} checks.");

Check(DamageFeedTruth.Outcome(true,true,false,0)==PlateOutcome.Stopped,"embedded terminal overrides stale penetration flag");
Check(DamageFeedTruth.Outcome(true,true,false,1)==PlateOutcome.Unknown,"embedded continuation cannot claim pass");
Check(DamageFeedTruth.Outcome(true,false,false,0)==PlateOutcome.Unknown,"pass requires observed original continuation");
var timeline=new DamageFeedTimeline();
timeline.Add("plate1","Precursor","Passed cover - 23 mm after plate");
timeline.Era("cell","Precursor",true,true,"heavyEra","brick");
timeline.EraBudget("cell","Precursor",23,12);
timeline.Add("stop","Precursor","Stopped in backing");
timeline.Add("plate1","Main","Passed cover - 700 mm after plate");
timeline.Add("health","Main","Engine damaged");
Check(timeline.All[0].StartsWith("Precursor: Passed")&&timeline.All[1].Contains("ERA only 23 -> 12 mm (before plate)")&&timeline.All[2].StartsWith("Precursor: Stopped")&&timeline.All[3].StartsWith("Main: Passed")&&timeline.All[4].Contains("Engine damaged"),"native event order and stage capture survive final composition");
timeline.Add("plate1","Main","duplicate");
Check(timeline.All.Count==5,"same stage event deduped without suppressing separate charge");
for(int i=0;i<20;i++)timeline.Add("extra"+i,"Main","step"+i);
timeline.Add("final","Main","Stopped in final backing");
Check(timeline.Visible.Count==12&&timeline.Visible[0].Contains("omitted")&&timeline.Visible[^1].Contains("Stopped in final backing"),"bounded HUD keeps native final stop and discloses omitted earlier events");
Console.WriteLine($"Ordered feed PASS: {checks} checks.");

for(int i=0;i<300;i++)timeline.Add("overflow"+i,"Main","step"+i);
timeline.Add("terminal-overflow","Main","Stopped in backing");
Check(timeline.Visible.Count==12&&timeline.Visible[^1]=="Main: Stopped in backing","terminal survives full bounded internal timeline");
Check(DamageFeedTruth.Outcome(true,true,true,0)==PlateOutcome.Stopped,"native embedded terminal never reported as pass or deflection");
Console.WriteLine($"Ordered feed bounded PASS: {checks} checks.");

var terminals=new DamageFeedTimeline();
terminals.Add("p-stop","Precursor","Stopped in first backing",true);
for(int i=0;i<16;i++)terminals.Add("p-health"+i,"Precursor","health "+i);
terminals.Add("m-pass","Main","Passed cover");
terminals.Add("m-stop","Main","Stopped in final backing",true);
for(int i=0;i<20;i++)terminals.Add("m-health"+i,"Main","health "+i);
Check(terminals.Visible.Any(x=>x=="Precursor: Stopped in first backing")&&terminals.Visible.Any(x=>x=="Main: Stopped in final backing"),"both native terminals survive later precursor and main health callbacks");
Check(terminals.Visible.ToList().FindIndex(x=>x.StartsWith("Precursor: Stopped"))<terminals.Visible.ToList().FindIndex(x=>x.StartsWith("Main: Stopped")),"pinned terminal charges remain in original event order");
Check(terminals.Visible[0]=="... 28 events omitted"&&terminals.Visible.Count==12,"omission count includes excluded middle events exactly");
for(int i=0;i<300;i++)terminals.Add("overflow-health"+i,"Main","later health "+i);
Check(terminals.Visible.Any(x=>x.StartsWith("Precursor: Stopped"))&&terminals.Visible.Any(x=>x.StartsWith("Main: Stopped")),"both terminals survive internal ring overflow followed by health");
Console.WriteLine($"Ordered feed terminal retention PASS: {checks} checks.");

var syncBase=ReleaseProfiles.Defaults();
var syncDraft=new ModularShellDraft(syncBase);
syncDraft.Root["note"]="preserve authoring metadata";
var syncAuthoring=syncDraft.SerializeAuthoring();
var syncEdited=System.Text.Json.Nodes.JsonNode.Parse(syncBase)!.AsObject();
syncEdited["profiles"]![0]!["penetrationQuality"]=.6;
var syncRuntime=syncEdited.ToJsonString();
var syncImported=ModularShellDraft.ImportCurrent(syncRuntime,syncAuthoring);
var syncRecovered=new ModularShellDraft(syncRuntime,syncAuthoring,syncImported);
Check(ModularShellDraft.SameJson(syncRuntime,syncRecovered.Compile().RuntimeJson!),"external edited runtime becomes exact compiled authority after explicit sync");
Check(ModularShellDraft.SameJson(syncDraft.Profiles[1]!.ToJsonString(),syncRecovered.Profiles[1]!.ToJsonString()),"unchanged custom modular profile preserved");
Check(syncRecovered.Root["note"]!.GetValue<string>()=="preserve authoring metadata","authoring top level metadata retained");
Check(ModularShellDraft.SameJson(syncBase,new ModularShellDraft(syncBase,null,ModularShellDraft.ImportCurrent(syncBase,null)).Compile().RuntimeJson!),"missing modular file imports current runtime");
var invalidSync=false;try{ModularShellDraft.ImportCurrent("{invalid",syncAuthoring);}catch{invalidSync=true;}
Check(invalidSync,"invalid runtime cannot silently replace draft");
Console.WriteLine($"Editor sync PASS: {checks} checks.");

syncDraft.Profiles[1]!["modules"]!["body"]!["kind"]="unknown_pending_body";
var syncPendingAuthoring=syncDraft.SerializeAuthoring();
var pendingSync=new ModularShellDraft(syncRuntime,syncPendingAuthoring,ModularShellDraft.ImportCurrent(syncRuntime,syncPendingAuthoring));
Check(!pendingSync.Compile().Ready&&pendingSync.Profiles[1]!["modules"]!["body"]!["kind"]!.GetValue<string>()=="unknown_pending_body","unchanged pending custom design survives explicit sync");
var syncTemp=Path.Combine(SprocketExpandedBallistics.Fix02228Tests.FixtureRoot(),"shell-sync-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(syncTemp);
try{
 var runtimeFile=Path.Combine(syncTemp,"shells.json");var draftFile=Path.Combine(syncTemp,"modules.json");
 File.WriteAllText(runtimeFile,syncRuntime);File.WriteAllText(draftFile,syncAuthoring);
 Check(File.ReadAllText(runtimeFile)==syncRuntime&&File.ReadAllText(draftFile)==syncAuthoring,"import remains in memory before Save");
 syncRecovered.Save(runtimeFile,draftFile);
 Check(Directory.GetFiles(syncTemp,"modules.json.backup-*").Length==1,"save keeps original modular draft backup");
 Check(new ModularShellDraft(File.ReadAllText(runtimeFile),File.ReadAllText(draftFile)).Compile().Ready,"saved recovery reopens with synchronized envelope");
 var reopened=new ModularShellDraft(File.ReadAllText(runtimeFile),File.ReadAllText(draftFile));
 File.AppendAllText(runtimeFile," ");
 bool conflict=false;try{reopened.Save(runtimeFile,draftFile);}catch(IOException){conflict=true;}
 Check(conflict,"external edits during recovery retain optimistic concurrency protection");
}finally{Directory.Delete(syncTemp,true);}
Console.WriteLine($"Editor sync recovery PASS: {checks} checks.");

GuardrailTests.Run(Check);
Console.WriteLine($"Versioned ERA guardrails PASS: {checks} checks.");

UnifiedEraTests.Run(Check);
Console.WriteLine($"Unified ERA snapshot/alias PASS: {checks} checks.");

// Early impact/release before MCLOS guidance must return native driving next input pass.
Check(AtgmGuidance.LiveFlight(true,7,7,1,1,"missile","missile",1),"active ejection before guidance owns input");
Check(!AtgmGuidance.LiveFlight(true,7,7,1,1,"missile","missile",3),"release before guidance relinquishes input without callback");
Check(!AtgmGuidance.LiveFlight(true,7,7,1,1,"missile","missile",0),"inactive early explosion relinquishes input");
Check(!AtgmGuidance.LiveFlight(false,7,7,1,1,"missile","missile",1),"removed map relinquishes input without FixedUpdate");
Check(!AtgmGuidance.LiveFlight(true,7,8,1,1,"missile","missile",1),"different pooled projectile cannot retain input");
Check(!AtgmGuidance.LiveFlight(true,7,7,1,2,"missile","missile",1),"new Play reused ID cannot retain input");
Check(!AtgmGuidance.LiveFlight(true,7,7,1,1,"missile","other",1),"different pooled definition cannot retain input");
foreach(var terminalFlag in new[]{2,4,8})Check(!AtgmGuidance.LiveFlight(true,7,7,1,1,"missile","missile",1|terminalFlag),"terminal flag relinquishes input "+terminalFlag);
Console.WriteLine($"ATGM live input ownership PASS: {checks} checks.");

Check(AtgmGuidance.KeyboardWindow(1.1,1,25),"MCLOS input window includes delayed guidance period");
Check(!AtgmGuidance.KeyboardWindow(26,1,25),"flight expiry releases input even without FixedUpdate");
Check(!AtgmGuidance.KeyboardWindow(.5,1,25),"new session clock excludes old flight");
Check(!AtgmGuidance.KeyboardWindow(double.NaN,1,25),"invalid clock fails open to native driving");
Console.WriteLine($"ATGM input expiry PASS: {checks} checks.");

var taggedRoot=System.Text.Json.Nodes.JsonNode.Parse(syncBase)!;
taggedRoot["profiles"]![0]!["armourThreatTags"]=new System.Text.Json.Nodes.JsonArray("futureThreat","kineticFullBore");
var taggedProfiles=ShellProfiles.Parse(taggedRoot.ToJsonString());
Check(taggedProfiles[0].ArmourThreatTags!.SequenceEqual(new[]{"futureThreat","kineticFullBore"}),"future shell armour traits survive strict parser");
var taggedImported=ModularShellDraft.ImportCurrent(taggedRoot.ToJsonString(),null);
var taggedDraft=new ModularShellDraft(taggedRoot.ToJsonString(),null,taggedImported);
Check(ShellProfiles.Parse(taggedDraft.Compile().RuntimeJson!)[0].ArmourThreatTags!.Contains("futureThreat"),"future armour traits survive modular import and compile");
foreach(var invalidTagSet in new[]{new[]{"fragment"},new[]{"duplicate","duplicate"},new[]{"bad tag"},new[]{""}})
{
 taggedRoot["profiles"]![0]!["armourThreatTags"]=new System.Text.Json.Nodes.JsonArray(invalidTagSet.Select(t=>(System.Text.Json.Nodes.JsonNode?)System.Text.Json.Nodes.JsonValue.Create(t)).ToArray());
 bool tagRejected=false;try{ShellProfiles.Parse(taggedRoot.ToJsonString());}catch(FormatException){tagRejected=true;}
 Check(tagRejected,"invalid/reserved shell armour traits rejected");
}
Check(ShellProfiles.Parse(syncBase)[0].ArmourThreatTags==null,"existing shells require no new armour tag fields");
Console.WriteLine($"Extensible armour threat tags PASS: {checks} checks.");

MaterialCatalogueTests.Run(Check);Wave1Tests.Run(Check);Wave2Tests.Run(Check);Fix0226Tests.Run(Check);Fix0226BComposerTests.Run(Check);Fix0226BCombatTests.Run(Check);
Fix0228Tests.Run(Check);Fix0228AddendumTests.Run(Check);Wave2bUiTests.Run(Check);Fix0227Tests.Run(Check);Fix0227AmmoTests.Run(Check);
Release0223Tests.Run(Check);
Console.WriteLine($"Material reactions PASS: {checks} checks.");


var productionDir=Path.Combine(SprocketExpandedBallistics.Fix02228Tests.FixtureRoot(),"Sprocket-rod-defaults-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(productionDir);
try{
 var builtInRod=SprocketExpandedBallistics.ExperimentalArmour.ExperimentalArmour.Parse(RodResponseDefaults.Read(productionDir));
 Check(builtInRod.Enabled&&builtInRod.Recipes.Count>0,"fresh install has production rod response without sidecar opt-in");
 var customRod=RodResponseDefaults.Read(productionDir).Replace("\"enabled\": true","\"enabled\": false");File.WriteAllText(Path.Combine(productionDir,"sprocket.expandedballistics.experimental.json"),customRod);
 Check(RodResponseDefaults.Read(productionDir)==customRod,"custom rod settings retained byte-for-byte");
 Check(SprocketExpandedBallistics.ExperimentalArmour.ExperimentalArmour.Parse(customRod).Enabled,"legacy opt-in false cannot disable standard rod response");
 File.WriteAllText(Path.Combine(productionDir,"sprocket.expandedballistics.experimental.json"),"{bad");
 bool badRodRejected=false;try{SprocketExpandedBallistics.ExperimentalArmour.ExperimentalArmour.Parse(RodResponseDefaults.Read(productionDir));}catch{badRodRejected=true;}
 Check(badRodRejected,"invalid custom settings are not silently replaced by production defaults");
}finally{Directory.Delete(productionDir,true);}
Console.WriteLine($"Production rod defaults PASS: {checks} checks.");

// Carried ammunition mix (FR-002): pure configuration, rack plan and switching rules.
var mix=AmmoLoadoutRules.Parse("vanilla=12;heat=6;apfsds=18");
Check(mix.Count==3&&mix[0].ProfileId=="vanilla"&&mix[0].Count==12&&mix[2].ProfileId=="apfsds","carried mix parses entries in order");
Check(AmmoLoadoutRules.Serialize(mix)=="vanilla=12;heat=6;apfsds=18","carried mix serializes round trip");
Check(AmmoLoadoutRules.Parse(" heat = 6 ; apfsds = 3 ").Count==2,"whitespace tolerant parsing");
Check(AmmoLoadoutRules.Parse("heat=6;heat=9").Count==1,"duplicate carried types are dropped");
Check(AmmoLoadoutRules.Parse("bad id=4").Count==0,"invalid carried type identifiers are rejected");
Check(AmmoLoadoutRules.Parse("heat=0;heat=-3;heat=x").Count==0,"non positive carried counts are rejected");
Check(AmmoLoadoutRules.Parse("heat=99999999999999999999").Count==0,"overflowing carried counts are rejected");
Check(AmmoLoadoutRules.Parse(null).Count==0&&AmmoLoadoutRules.Parse("").Count==0,"empty text gives an empty mix");
Check(AmmoLoadoutRules.Parse("a=1;b=1;c=1;d=1;e=1;f=1;g=1;h=1;i=1").Count==AmmoLoadoutRules.MaximumTypes,"carried mix is capped at eight types");
Check(AmmoLoadoutRules.Parse($"heat={AmmoLoadoutRules.MaximumCount+500}")[0].Count==AmmoLoadoutRules.MaximumCount,"carried counts are capped");
Check(AmmoLoadoutRules.Serialize(new[]{new AmmoLoadoutEntry("heat",0)})=="","empty entries do not serialize");
Check(AmmoLoadoutRules.Total(mix)==36,"total sums the carried rounds");
var fitted=AmmoLoadoutRules.Fit(AmmoLoadoutRules.Parse("apfsds=30;heat=20"),40);
Check(fitted.Count==2&&fitted[0].Count==30&&fitted[1].Count==10,"fit trims the last carried types first");
Check(AmmoLoadoutRules.Fit(mix,0).Count==0,"zero rack capacity carries nothing");
var plan=AmmoLoadoutRules.Distribute(new[]{20,20,10},AmmoLoadoutRules.Parse("apfsds=25;heat=10"));
Check(plan.Count==3,"one rack assignment per rack");
Check(plan[0].ProfileId=="apfsds"&&plan[0].Amount==20,"first rack fills with the first type");
Check(plan[1].ProfileId=="apfsds"&&plan[1].Amount==5,"second rack continues the same type");
Check(plan[2].ProfileId=="heat"&&plan[2].Amount==10,"next type starts in the following rack");
var tight=AmmoLoadoutRules.Distribute(new[]{20,20,10},AmmoLoadoutRules.Parse("apfsds=5;heat=5"));
Check(tight[0].Amount==5&&tight[1].Amount==5&&tight[2].ProfileId==null&&tight[2].Amount==0,"racks outside the mix stay empty");
Check(AmmoLoadoutRules.Distribute(new[]{20},AmmoLoadoutRules.Parse("apfsds=60"))[0].Amount==20,"distribution never exceeds rack capacity");
var zeroRow=new List<AmmoLoadoutEntry>{new("apfsds",18),new("heat",0),new("he",5)};
var zeroPlan=AmmoLoadoutRules.Distribute(new[]{10,10,10,10},zeroRow);
Check(zeroPlan[0].ProfileId=="apfsds"&&zeroPlan[0].Amount==10&&zeroPlan[1].ProfileId=="apfsds"&&zeroPlan[1].Amount==8,"distribution fills the first type before an empty row");
Check(zeroPlan[2].ProfileId=="he"&&zeroPlan[2].Amount==5&&zeroPlan[3].ProfileId==null,"empty carried types never block the types behind them");
Check(AmmoLoadoutRules.Serialize(zeroRow)=="apfsds=18;he=5","serializing drops empty carried types");
Check(AmmoLoadoutRules.IndexOf(mix,"heat")==1&&AmmoLoadoutRules.IndexOf(mix,"missing")==-1,"carried type lookup");
Check(AmmoLoadoutRules.Slot(mix,2)=="apfsds"&&AmmoLoadoutRules.Slot(mix,7)==null,"slot keys map onto carried types");
var left=new Dictionary<string,int>{["vanilla"]=0,["heat"]=4,["apfsds"]=9};
Check(AmmoLoadoutRules.Step(mix,id=>left[id],"vanilla",1)=="heat","cycling skips types without rounds");
Check(AmmoLoadoutRules.Step(mix,id=>left[id],"vanilla",-1)=="apfsds","cycling wraps around");
Check(AmmoLoadoutRules.Step(mix,id=>left[id],"missing",1)=="heat","cycling from an unlisted type starts at the first carried type");
Check(AmmoLoadoutRules.Step(mix,id=>0,"heat",1)==null,"cycling with only empty types returns nothing");
Check(AmmoLoadoutRules.Step(mix,id=>left[id],"heat",0)==null,"zero step direction returns nothing");
Console.WriteLine($"Carried ammunition mix PASS: {checks} checks.");

SprocketExpandedBallistics.Tests.SightIntegrationTests.Run((condition,message)=>{checks++;if(!condition)throw new Exception(message);});
Console.WriteLine($"Sight integration PASS: {checks} checks.");
SprocketExpandedBallistics.Tests.AmmoInspectorPolicyTests.RunAll();
var eraFailures=SprocketExpandedBallistics.Tests.VehicleEraAvailabilityTests.Run(); foreach(var f in eraFailures) Console.WriteLine(f); if(eraFailures.Count>0) Environment.Exit(1); Console.WriteLine("DeepSeek UI and ERA production policy fixtures PASS");


SprocketExpandedBallistics.Tests.ModularDupletTests.Run((condition,message)=>{checks++;if(!condition)throw new Exception(message);});
SprocketExpandedBallistics.Tests.ModularDupletTests.RunSlim((condition,message)=>{checks++;if(!condition)throw new Exception(message);});
SprocketExpandedBallistics.Tests.ReliktModularTests.Run((condition,message)=>{checks++;if(!condition)throw new Exception(message);});
SprocketExpandedBallistics.Tests.DupletModularTests.Run((condition,message)=>{checks++;if(!condition)throw new Exception(message);});
SprocketExpandedBallistics.Tests.ModularLifecycleTests.Run((condition,message)=>{checks++;if(!condition)throw new Exception(message);});
Console.WriteLine($"Recovered standard/slim modular ERA lifecycle PASS: {checks} checks.");

BratPresetChecks.Run(Check);
EraReworkChecks.Run(Check);
EraDisplayNameTests.Run(Check);
AtgmFlightChecks.Run(Check);
AtgmLaunchCaptureChecks.Run(Check);
AtgmLaunchCaptureIntegrationChecks.Run(Check);
AtgmSightReferenceChecks.Run(Check);
AtgmSightHeadChecks.Run(Check);
AtgmGuidanceAxisChecks.Run(Check);
KobraCloseRangeChecks.Run(Check);
AtgmVfxGeometryChecks.Run(Check);
AtgmMotorCurveChecks.Run(Check);
checks += ShellRebalanceTests.Run();
checks += ShellEraListTests.Run();
EraDateGateTests.Run(Check);
Fix02211Tests.Run(Check);Fix02212Tests.Run(Check);Fix02212AddendaTests.Run(Check);HitResultRuleTests.Run(Check);Fix02214Tests.Run(Check);Fix02215Tests.Run(Check);Fix02216Tests.Run(Check);ForkBackplaneTests.Run(Check);Fix02217Tests.Run(Check);Fix02218Tests.Run(Check);Fix02219Tests.Run(Check);Fix02220Tests.Run(Check);Fix02221Tests.Run(Check);Fix02222Tests.Run(Check);Fix02223Tests.Run(Check);Fix02224Tests.Run(Check);Fix02227Tests.Run(Check);Fix02228Tests.Run(Check);Fix02229Tests.Run(Check);Fix02230Tests.Run(Check);Fix02231Tests.Run(Check);Fix02232Tests.Run(Check);Fix02233Tests.Run(Check);
Fix02234Tests.Run(Check);Fix02235Tests.Run(Check);Fix02236Tests.Run(Check);Feat02237CompositeModuleTests.Run(Check);Fix02238Tests.Run(Check);Kontakt5AssetTests.Run(Check);Fix02239Tests.Run(Check);Feat02240ForensicTests.Run(Check);Fix02241Tests.Run(Check);Fix02242Tests.Run(Check);Fix02243Tests.Run(Check);Feat02244MaterialsTests.Run(Check);Feat02245MaterialLookTests.Run(Check);Feat02246CustomTextureTests.Run(Check);Fix02247IntervalTests.Run(Check);Fix02248ReadableIntervalTests.Run(Check);Fix02249EraFingerprintTests.Run(Check);Feat02250ApplyScaleTests.Run(Check);Fix02251DuplicateRowTests.Run(Check);Fix02251EraPlayStabilityTests.Run(Check);
Feat02252SpentRenderTests.Run(Check);Fix02251ChainReviewTests.Run(Check);Feat02253ComposerFillTests.Run(Check);Feat02254ComposerFixTests.Run(Check);ComposerFillBlueprintEvidence.RunCoverage(Check);ComposerFillBlueprintEvidence.RunConform(Check);ComposerFillBlueprintEvidence.RunShell(Check);ComposerFillBlueprintEvidence.RunShellDepth(Check);ComposerFillBlueprintEvidence.RunLayerFaces(Check);ComposerFillBlueprintEvidence.RunLayerBox(Check);ComposerFillBlueprintEvidence.RunLayerShape(Check);ComposerFillBlueprintEvidence.RunLayerAccept(Check);ComposerFillBlueprintEvidence.RunLayerDirection(Check);ComposerFillBlueprintEvidence.RunNegativeTm(Check);ComposerFillBlueprintEvidence.RunOpenBox(Check);ComposerFillBlueprintEvidence.RunPainted(Check);ComposerFillBlueprintEvidence.RunRobust(Check);ComposerFillBlueprintEvidence.RunUniform(Check);ComposerFillBlueprintEvidence.RunRelayer(Check);Feat02270HousingLookTests.Run(Check);Feat02272ModuleIconTests.Run(Check);Feat02273WtVariantTests.Run(Check);Feat02274OverlayApplyTests.Run(Check);Feat02275FollowWallTests.Run(Check);Feat02275ReviewFixTests.Run(Check);
Feat02276RobustTests.Run(Check);
Feat02277SolidBlockTests.Run(Check);
Feat02278HeshTests.Run(Check);
Feat02279SolidMarkTests.Run(Check);
Feat02280FollowFrontTests.Run(Check);
Feat02281SolidForensicTests.Run(Check);
Feat02282SolidBlockFullTests.Run(Check);
Feat02283SolidOverlayTests.Run(Check);
Feat02284SolidBandsTests.Run(Check);
Feat02286HeTests.Run(Check);
Feat02287AtgmSightTests.Run(Check);
Feat02288DerivedThicknessTests.Run(Check);
Feat02289AtgmBoxHeSkirtTests.Run(Check);
Feat02290NativeDivisorTests.Run(Check);
Feat02291ModuleLayersTests.Run(Check);
Feat02292AtgmSightHudTests.Run(Check);
Feat02299HeatAndAmmoTests.Run(Check);
if(collected.Count>0){foreach(var f in collected)Console.Error.WriteLine("COLLECTED FAILURE: "+f);throw new Exception(collected.Count+" collected failures");}
Console.WriteLine($"ATGM flight and tracer checks PASS: {checks} total checks.");

} catch(Exception failure) { Console.Error.WriteLine(failure.ToString()); Environment.ExitCode=1; }
