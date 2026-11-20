using System.Globalization;
using System.Numerics;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
namespace SprocketExpandedBallistics;

/// <summary>
/// 0.22.92 (brief FEAT-0.22.92-ATGM-SIGHT-HUD; user 17-11: "Keuze in de cfg" and "Iets realistisch wat ze bij een TOW2B of swingfire zouden hebben. maak het emmersive").
/// Targeted: the cfg keys and defaults, the style by vehicle date, the Follow pose of EB's aim object per frame, the Hidden ledger on every end path and its
/// safety net (fake mask handles; the game is not available here), the HUD off at Off and without a session, the range text, and the file ledger against the
/// 0.22.91 source (EB_BASE_SRC). Writes FEAT92-RESULT.json and the two preview layouts (atgm-hud-digital.json, atgm-hud-analog.json) to test-output/.
/// The native mask, the overlay plugin and Unity's LookRotation are game code: their wiring is checked on the source, their effect only in the game.
/// </summary>
internal static class Feat02292AtgmSightHudTests
{
    internal static bool IsPlanned(string rel)=>rel is "RuntimeAtgmGuidanceSight.cs" or "AtgmSightHudModel.cs" or "RuntimeAtgmSightHud.cs" or "DamageFeedHud.cs"
        or "Plugin.cs" or "SprocketExpandedBallistics.csproj" or "RELEASE-NOTES.md";
    private sealed class FakeMask:IAtgmMaskHandle
    {
        internal FakeMask(long key,bool active=true){Key=(IntPtr)key;Value=active;}
        public IntPtr Key{get;}
        public bool Alive{get;set;}=true;
        internal bool Value;internal int Writes;internal bool Throw;
        public bool Active{get=>Value;set{if(Throw)throw new InvalidOperationException("native gone");Value=value;Writes++;}}
    }
    internal static void Run(Action<bool,string> sink)
    {
        int checks=0,failures=0;void Check(bool ok,string message){checks++;if(!ok)failures++;sink(ok,"FEAT92: "+message);}
        string root=Fix02228Tests.SourceRoot(),output=Fix02228Tests.OutputRoot();
        string Read(string f)=>File.ReadAllText(Path.Combine(root,f)).Replace("\r\n","\n");
        var sight=Read("RuntimeAtgmGuidanceSight.cs");var hud=Read("RuntimeAtgmSightHud.cs");var model=Read("AtgmSightHudModel.cs");var feed=Read("DamageFeedHud.cs");
        string Method(string text,string signature){var i=text.IndexOf(signature,StringComparison.Ordinal);if(i<0)return "";var j=text.IndexOf("\n    }\n",i,StringComparison.Ordinal);var k=text.IndexOf("\n",i,StringComparison.Ordinal);
            var oneLine=text[i..k];return oneLine.TrimEnd().EndsWith("}",StringComparison.Ordinal)&&!oneLine.TrimEnd().EndsWith("{",StringComparison.Ordinal)?oneLine:j<0?text[i..]:text[i..j];}

        // ================= 1. cfg: keys, defaults, parsing =================
        Check(AtgmSightHudConfig.Section=="ATGM guidance sight"&&AtgmSightHudConfig.MaskKey=="Scope mask"&&AtgmSightHudConfig.StyleKey=="HUD style"&&AtgmSightHudConfig.YearKey=="Digital from year"&&
              AtgmSightHudConfig.RangeKey=="Range readout"&&AtgmSightHudConfig.TimerKey=="Flight timer"&&AtgmSightHudConfig.LampsKey=="Status lamps"&&AtgmSightHudConfig.ReticleKey=="Reticle","1 cfg: the seven keys of the brief, in [ATGM guidance sight]");
        Check(AtgmSightHudConfig.DefaultMask=="Hidden"&&AtgmSightHudConfig.DefaultStyle=="Auto"&&AtgmSightHudConfig.DefaultDigitalYear==1990,"1 cfg: defaults Scope mask = Hidden, HUD style = Auto, Digital from year = 1990");
        var defaults=new AtgmSightHudOptions();
        Check(defaults.Mask==AtgmScopeMaskMode.Hidden&&defaults.Style==AtgmHudStyle.Auto&&defaults.DigitalFromYear==1990&&defaults.RangeReadout&&defaults.FlightTimer&&defaults.StatusLamps&&defaults.Reticle,"1 cfg: default options (all four elements on)");
        Check(AtgmSightHudConfig.MaskValues.SequenceEqual(new[]{"Hidden","Follow"})&&AtgmSightHudConfig.StyleValues.SequenceEqual(new[]{"Auto","Digital","Analog","Off"}),"1 cfg: accepted values listed in the cfg (default first)");
        var maskCases=new (string? Text,AtgmScopeMaskMode Expected)[]{("Hidden",AtgmScopeMaskMode.Hidden),("Follow",AtgmScopeMaskMode.Follow),(" follow ",AtgmScopeMaskMode.Follow),("FOLLOW",AtgmScopeMaskMode.Follow),("hidden",AtgmScopeMaskMode.Hidden),("",AtgmScopeMaskMode.Hidden),(null,AtgmScopeMaskMode.Hidden),("Visible",AtgmScopeMaskMode.Hidden)};
        Check(maskCases.All(c=>AtgmSightHudConfig.Mask(c.Text)==c.Expected),"1 cfg: Scope mask parsing (case and spaces ignored; anything else is the default Hidden), "+maskCases.Length+" cases");
        var styleCases=new (string? Text,AtgmHudStyle Expected)[]{("Auto",AtgmHudStyle.Auto),("Digital",AtgmHudStyle.Digital),("analog",AtgmHudStyle.Analog),(" OFF ",AtgmHudStyle.Off),("Modern",AtgmHudStyle.Auto),(null,AtgmHudStyle.Auto)};
        Check(styleCases.All(c=>AtgmSightHudConfig.Style(c.Text)==c.Expected),"1 cfg: HUD style parsing, "+styleCases.Length+" cases");
        Check(AtgmSightHudConfig.Year(1975)==1975&&AtgmSightHudConfig.Year(1899)==1990&&AtgmSightHudConfig.Year(2101)==1990&&AtgmSightHudConfig.Year(1900)==1900&&AtgmSightHudConfig.Year(2100)==2100,"1 cfg: Digital from year 1900..2100, else 1990");
        var configure=Method(hud,"internal static void Configure(ConfigFile config)");
        Check(configure.Contains("config.Bind(s,AtgmSightHudConfig.MaskKey,AtgmSightHudConfig.DefaultMask,",StringComparison.Ordinal)&&configure.Contains("config.Bind(s,AtgmSightHudConfig.StyleKey,AtgmSightHudConfig.DefaultStyle,",StringComparison.Ordinal)&&
              configure.Contains("config.Bind(s,AtgmSightHudConfig.YearKey,AtgmSightHudConfig.DefaultDigitalYear,",StringComparison.Ordinal)&&
              new[]{"RangeKey","TimerKey","LampsKey","ReticleKey"}.All(k=>configure.Contains($"config.Bind(s,AtgmSightHudConfig.{k},true,",StringComparison.Ordinal))&&configure.Contains("const string s=AtgmSightHudConfig.Section;",StringComparison.Ordinal),
              "1 cfg: the runtime binds exactly these keys and defaults in that section");
        Check(Regex.IsMatch(hud,@"Options=>new\(AtgmSightHudConfig\.Mask\(mask\?\.Value\),AtgmSightHudConfig\.Style\(style\?\.Value\),AtgmSightHudConfig\.Year\("),"1 cfg: the runtime reads the values through the tested parsers (live, no restart)");
        Check(Method(sight,"internal static void Configure(ConfigFile config)").Contains("RuntimeAtgmSightHud.Configure(config);",StringComparison.Ordinal),"1 cfg: bound from the existing [ATGM guidance sight] Configure");
        Check(AtgmSightHudConfig.MaskDescription.Contains("Hidden:",StringComparison.Ordinal)&&AtgmSightHudConfig.MaskDescription.Contains("Follow:",StringComparison.Ordinal)&&
              AtgmSightHudConfig.StyleDescription.Contains("Auto:",StringComparison.Ordinal)&&AtgmSightHudConfig.StyleDescription.Contains("Off:",StringComparison.Ordinal),"1 cfg: the descriptions say what each choice does");

        // ================= 2. style by vehicle date =================
        var styleTable=new StringBuilder("style\tvehicle year\tdigital from\tresolved\n");
        void Style(AtgmHudStyle s,int? year,int from,AtgmHudStyle expected,string name)
        {var r=AtgmSightHudRules.Resolve(s,year,from);styleTable.Append($"{s}\t{year?.ToString()??"unknown"}\t{from}\t{r}\n");Check(r==expected,$"2 style: {name} -> {expected} (got {r})");}
        Style(AtgmHudStyle.Auto,1989,1990,AtgmHudStyle.Analog,"Auto, 1989 against 1990");
        Style(AtgmHudStyle.Auto,1990,1990,AtgmHudStyle.Digital,"Auto, 1990 against 1990 (the boundary year is Digital)");
        Style(AtgmHudStyle.Auto,2005,1990,AtgmHudStyle.Digital,"Auto, 2005");
        Style(AtgmHudStyle.Auto,1970,1990,AtgmHudStyle.Analog,"Auto, 1970 (Swingfire era date)");
        Style(AtgmHudStyle.Auto,1985,1980,AtgmHudStyle.Digital,"Auto, 1985 with the boundary moved to 1980");
        Style(AtgmHudStyle.Auto,1985,0,AtgmHudStyle.Analog,"Auto, an invalid cfg year falls back to 1990");
        Style(AtgmHudStyle.Auto,null,1990,AtgmHudStyle.Digital,"Auto, unreadable date (the modern sight model)");
        foreach(var forced in new[]{AtgmHudStyle.Digital,AtgmHudStyle.Analog,AtgmHudStyle.Off})foreach(var y in new int?[]{1960,2010,null})
            Style(forced,y,1990,forced,$"{forced} is kept whatever the date ({y?.ToString()??"unknown"})");
        Check(!Regex.IsMatch(hud+model,@"ShellEra|EraName|CurrentEra|\.Era\b|EraCatalogue|\bera\s*==",RegexOptions.IgnoreCase),"2 style: no era name is read anywhere in the HUD (only the vehicle date)");
        Check(Method(hud,"private static int? Year(GunnerSight sight)").Contains("sight.Vehicle.Tech.Date.Year",StringComparison.Ordinal),"2 style: the year is the vehicle's own date (Tech.Date, DesignInfo as fallback)");

        // ================= 3. Follow: the aim object per frame =================
        var follow=new StringBuilder("frame\tcommand pitch\tcommand yaw\tmask point elevation\tmask point bearing\toff-ray m\tforward error\n");
        var origin=new Vector3(12.5f,2.31f,-4.2f);var partUp=Vector3.Normalize(new Vector3(.05f,1,.02f));const float convergence=1200;
        bool allOnRay=true,allMoved=true,allUp=true;double worstRay=0,worstElevation=0;Vector3? previous=null;
        for(int frame=0;frame<=60;frame++)
        {
            var pitch=-10+30.0*frame/60;var yaw=25*Math.Sin(frame/9.0);   // the launcher laying from -10 to +20 degrees while traversing
            var p=pitch*Math.PI/180;var y=yaw*Math.PI/180;
            var dir=new Vector3((float)(Math.Cos(p)*Math.Sin(y)),(float)Math.Sin(p),(float)(Math.Cos(p)*Math.Cos(y)))*3.7f;
            var pose=AtgmMaskAim.Pose(origin,dir,partUp);
            if(pose==null){allOnRay=false;continue;}
            var point=AtgmMaskAim.MaskPoint(pose.Value,convergence);var rel=point-origin;
            var along=Vector3.Dot(rel,Vector3.Normalize(dir));var off=(rel-Vector3.Normalize(dir)*along).Length();
            var elevation=Math.Atan2(rel.Y,Math.Sqrt(rel.X*rel.X+rel.Z*rel.Z))*180/Math.PI;var bearing=Math.Atan2(rel.X,rel.Z)*180/Math.PI;
            var fwdError=(pose.Value.Forward-Vector3.Normalize(dir)).Length();
            worstRay=Math.Max(worstRay,off);worstElevation=Math.Max(worstElevation,Math.Abs(elevation-pitch));
            allOnRay&=off<1e-2&&Math.Abs(along-convergence)<1e-2&&fwdError<1e-5&&Math.Abs(elevation-pitch)<1e-3&&Math.Abs(bearing-yaw)<1e-3;
            allUp&=MathF.Abs(Vector3.Dot(pose.Value.Up,pose.Value.Forward))<.999f&&Math.Abs(pose.Value.Up.Length()-1)<1e-5;
            if(previous is {} prev)allMoved&=(point-prev).Length()>1e-3;
            previous=point;
            follow.Append(FormattableString.Invariant($"{frame}\t{pitch:0.000}\t{yaw:0.000}\t{elevation:0.000}\t{bearing:0.000}\t{off:0.000000}\t{fwdError:0.0000000}\n"));
        }
        Check(allOnRay,$"3 follow: 61 frames of a -10..+20 deg lay: the native mask point (position + forward x convergence) lies on the guidance line at the convergence, elevation and bearing as commanded (worst off-ray {worstRay:0.000000} m, worst elevation error {worstElevation:0.000000} deg)");
        Check(allMoved,"3 follow: the mask point moves every frame the guidance line moves (not only at a switch)");
        Check(allUp,"3 follow: the up vector is unit length and never parallel to the line");
        var upPose=AtgmMaskAim.Pose(origin,Vector3.UnitY,Vector3.UnitY);
        Check(upPose is {} u1&&u1.Forward==Vector3.UnitY&&MathF.Abs(Vector3.Dot(u1.Up,u1.Forward))<.999f,"3 follow: straight up along the part's up: another up is used (no degenerate look rotation)");
        Check(AtgmMaskAim.Pose(origin,new Vector3(float.NaN,0,1),partUp)==null&&AtgmMaskAim.Pose(origin,Vector3.Zero,partUp)==null&&AtgmMaskAim.Pose(new Vector3(float.PositiveInfinity,0,0),Vector3.UnitZ,partUp)==null,"3 follow: a non-finite or zero line leaves the object where it was (no pose)");
        Check(AtgmMaskAim.MaskPoint(new AtgmMaskPose(Vector3.Zero,Vector3.UnitZ,Vector3.UnitY),0)==new Vector3(0,0,300)&&AtgmMaskAim.MaskPoint(new AtgmMaskPose(Vector3.Zero,Vector3.UnitZ,Vector3.UnitY),1)==new Vector3(0,0,300),"3 follow: convergence 0 or 1 gives 300 m, as SightAimPoint");
        var poseMask=Method(sight,"private static bool PoseMask(Session s,Transform aim)");var followMask=Method(sight,"private static void FollowMask(Session s)");
        Check(poseMask.Contains("TryAim(s.Cannon,out var origin,out var direction,out _)",StringComparison.Ordinal)&&poseMask.Contains("AtgmMaskAim.Pose(",StringComparison.Ordinal)&&poseMask.Contains("aim.SetPositionAndRotation(",StringComparison.Ordinal)&&
              !poseMask.Contains("LinkedWeapon",StringComparison.Ordinal)&&!followMask.Contains("LinkedWeapon=",StringComparison.Ordinal)&&followMask.Contains("s.Sight.LinkedWeapon?.Pointer==aim.Pointer",StringComparison.Ordinal),
              "3 follow: per frame EB writes only its own aim object, from TryAim, and only while that object is the sight's link");
        var cameraInput=Method(sight,"private static void CameraInput(ScopeCameraController __instance,CameraInputProccessingFlags __1)");var cameraHead=Method(sight,"private static void CameraHead(ScopeCameraController __instance,float __0)");
        Check(cameraInput.Contains("SightAimPoint(s);FollowMask(s);",StringComparison.Ordinal)&&cameraHead.Contains("if(s.SightAxes.Count!=0){FollowMask(s);return;}",StringComparison.Ordinal)&&cameraHead.Contains("SightAimPoint(s);FollowMask(s);return;",StringComparison.Ordinal),
              "3 follow: run from the existing ProcessInputs prefix and postfix (head and own-ring sights)");
        var redirect=Method(sight,"private static void Redirect(Session s)");
        Check(redirect.Contains("if(s.SightAxes.Count==0&&TryHead(s.Cannon,out var head)&&head!=null)target=HeadAim(head);",StringComparison.Ordinal)&&redirect.Contains("else if(s.SightAxes.Count!=0)target=SightAim(s);",StringComparison.Ordinal)&&
              redirect.Contains("if(s.Reference.Redirecting(current?.Pointer??IntPtr.Zero,target.Pointer))s.Previous=current;",StringComparison.Ordinal),
              "3 follow: a sight on its own rings links EB's aim object too (was its part); the restore ledger is unchanged");
        Check(Method(sight,"private static Transform SightAim(Session s)").Contains("aim.SetPositionAndRotation(parent.TransformPoint(s.Sight.ObjectiveLensLocalPosition),parent.rotation)",StringComparison.Ordinal),
              "3 follow: before the first scoped frame the object sits on the part's own line (the mask's old place)");

        // ================= 4. Hidden: every end path and the safety net (fake masks) =================
        var hidden=new StringBuilder("case\tactive after hide\tactive after end\twrites\tok\n");
        IntPtr A=(IntPtr)0xA1,B=(IntPtr)0xB2;
        void EndPath(string name,Action<AtgmScopeMaskLedger> end)
        {
            var ledger=new AtgmScopeMaskLedger();var mask=new FakeMask(0x5C0);ledger.Apply(A,mask,true);var after=mask.Value;end(ledger);
            var ok=!after&&mask.Value&&ledger.Count==0&&mask.Writes==2;
            hidden.Append($"{name}\t{after}\t{mask.Value}\t{mask.Writes}\t{ok}\n");Check(ok,$"4 hidden: {name}: hidden while the ATGM sight is active, then exactly restored (2 writes)");
        }
        EndPath("switch to another sight (Ensure -> End)",l=>l.End(A));
        EndPath("ScopeController.Dispose (End)",l=>l.End(A));
        EndPath("PlayerChanged, another vehicle (End)",l=>l.End(A));
        EndPath("Cannon.Release of a group tube (End)",l=>l.End(A));
        EndPath("Clear (battle end) (End, then RestoreAll)",l=>{l.End(A);l.RestoreAll();});
        EndPath("plugin shutdown (Clear -> RestoreAll)",l=>l.RestoreAll());
        EndPath("zoom out: no active ATGM sight next frame (safety net)",l=>l.SafetyNet(IntPtr.Zero,false));
        EndPath("cfg switched to Follow while hidden (safety net)",l=>l.SafetyNet(A,false));
        EndPath("another scope controller is active (safety net)",l=>l.SafetyNet(B,true));
        {
            var l=new AtgmScopeMaskLedger();var m=new FakeMask(0x5C1);l.Apply(A,m,true);l.Apply(A,m,true);l.Apply(A,m,true);var r=l.SafetyNet(A,true);
            Check(!m.Value&&m.Writes==1&&r==0&&l.Hides(m.Key),"4 hidden: per frame while active: one write only, the safety net leaves the active owner's mask hidden");
            l.End(A);Check(m.Value&&m.Writes==2,"4 hidden: then End restores it");
        }
        {
            var l=new AtgmScopeMaskLedger();var m=new FakeMask(0x5C2,active:false);l.Apply(A,m,true);l.End(A);l.RestoreAll();
            Check(!m.Value&&m.Writes==0&&l.Count==0,"4 hidden: a mask that was already off (not by EB) is never touched; its original value stays");
        }
        {
            var l=new AtgmScopeMaskLedger();var m=new FakeMask(0x5C3);l.Apply(A,m,true);m.Value=true;var restored=l.End(A);
            Check(m.Value&&m.Writes==1&&restored==0&&l.Count==0,"4 hidden: something else switched it back on meanwhile: EB does not write over it and forgets it");
        }
        {
            var l=new AtgmScopeMaskLedger();var m=new FakeMask(0x5C4);l.Apply(A,m,true);m.Alive=false;var restored=l.SafetyNet(IntPtr.Zero,false);
            Check(restored==0&&l.Count==0&&m.Writes==1,"4 hidden: a destroyed mask (scene unloaded) is dropped without a write");
        }
        {
            var l=new AtgmScopeMaskLedger();var m=new FakeMask(0x5C5);l.Apply(A,m,true);m.Throw=true;var threw=false;try{l.RestoreAll();}catch{threw=true;}
            Check(!threw&&l.Count==0,"4 hidden: a failing native write during restore does not throw out of the end path");
        }
        {
            var l=new AtgmScopeMaskLedger();var m=new FakeMask(0x5C6);l.Apply(A,m,false);l.Apply(A,null,true);
            Check(m.Value&&m.Writes==0&&l.Count==0,"4 hidden: Follow (hide=false) or no mask found: nothing is written");
        }
        {
            var l=new AtgmScopeMaskLedger();var m1=new FakeMask(0x5C7);var m2=new FakeMask(0x5C8);l.Apply(A,m1,true);l.Apply(B,m2,true);l.End(A);
            Check(m1.Value&&!m2.Value&&l.Count==1,"4 hidden: End of one controller restores only its own mask");
        }
        var end=Method(sight,"private static void End(IntPtr controller)");var clear=Method(sight,"internal static void Clear()");
        Check(end.Contains("try{RuntimeAtgmSightHud.SessionEnded(controller);}catch(Exception ex){Warn(\"Restore scope mask\",ex);}",StringComparison.Ordinal)&&clear.Contains("RuntimeAtgmSightHud.Clear();",StringComparison.Ordinal),
              "4 hidden: every session end runs SessionEnded; Clear (battle end and plugin shutdown, ShellFeature.Unload) runs the HUD Clear");
        Check(Method(sight,"private static void Dispose(ScopeController __instance)").Contains("End(__instance.Pointer)",StringComparison.Ordinal)&&Method(sight,"private static void PlayerChanged(").Contains("End(pair.Key)",StringComparison.Ordinal)&&
              Method(sight,"private static void Released(Cannon __instance)").Contains("End(pair.Key)",StringComparison.Ordinal)&&Method(sight,"private static Session? Ensure(ScopeController scope,IGunnerSight? sight)").Contains("End(pair.Key)",StringComparison.Ordinal),
              "4 hidden: switch (Ensure), Dispose, PlayerChanged and Release all go through End");
        var sessionEnded=Method(hud,"internal static void SessionEnded(IntPtr controller)");var hudClear=Method(hud,"internal static void Clear()");var update=Method(hud,"internal static void Update()");
        Check(sessionEnded.Contains("ledger.End(controller)",StringComparison.Ordinal)&&hudClear.Contains("ledger.RestoreAll()",StringComparison.Ordinal)&&update.Contains("ledger.SafetyNet(IntPtr.Zero,false);",StringComparison.Ordinal)&&
              update.Contains("ledger.SafetyNet(scope.Pointer,hide);if(hide)ledger.Apply(scope.Pointer,MaskOf(scope),true);",StringComparison.Ordinal),"4 hidden: the runtime maps End, Clear and the per-frame safety net onto the tested ledger");
        Check(feed.Contains("try{RuntimeAtgmSightHud.Update();}catch(Exception ex){try{RuntimeAtgmSightHud.Failed(ex);}catch{}}",StringComparison.Ordinal),"4 hidden: the safety net runs every frame from the top-attack HUD driver (DamageFeedBehaviour.Update)");
        Check(hud.Contains("public bool Active{get=>Scope.active;set=>Scope.active=value;}",StringComparison.Ordinal)&&!Regex.IsMatch(hud,@"postProcessVolume|\.radius|\.centre|\.enabled\s*=|ControlLateUpdate\s*\("),
              "4 hidden: only the Scope post-process component's own active flag is written (not the volume the overlay switches each frame, not radius or centre)");
        Check(Regex.Matches(hud,@"\[Harmony").Count==0&&!hud.Contains("ScopeOverlayVehicleControllerPlugin),",StringComparison.Ordinal),"4 hidden: no hook on the overlay plugin (found once by FindObjectsOfType, read only)");

        // ================= 5. HUD visibility =================
        Check(!AtgmSightHudRules.Visible(AtgmHudStyle.Off,true,false,false)&&!AtgmSightHudRules.Visible(AtgmHudStyle.Auto,true,false,false),"5 hud: Off (and an unresolved Auto) draws nothing");
        Check(!AtgmSightHudRules.Visible(AtgmHudStyle.Digital,false,false,false)&&!AtgmSightHudRules.Visible(AtgmHudStyle.Analog,false,false,false),"5 hud: no active ATGM sight session: hidden");
        Check(!AtgmSightHudRules.Visible(AtgmHudStyle.Digital,true,true,false)&&!AtgmSightHudRules.Visible(AtgmHudStyle.Digital,true,false,true),"5 hud: cursor shown (menu) or editor: hidden");
        Check(AtgmSightHudRules.Visible(AtgmHudStyle.Digital,true,false,false)&&AtgmSightHudRules.Visible(AtgmHudStyle.Analog,true,false,false),"5 hud: Digital and Analog with the active sight: shown");
        Check(AtgmSightHudLayout.Build(AtgmHudStyle.Off,defaults,8).Items.Count==0&&AtgmSightHudLayout.Build(AtgmHudStyle.Auto,defaults,8).Items.Count==0,"5 hud: the Off layout has no items");
        Check(update.Contains("if(!active||scope==null||sight==null||group.Length==0)",StringComparison.Ordinal)&&update.Contains("Hide();return;",StringComparison.Ordinal)&&sessionEnded.Contains("Hide();",StringComparison.Ordinal),
              "5 hud: the runtime hides the canvas without a session and on every end path");
        Check(Method(sight,"internal static bool TryActiveView(").Contains("!s.Initialized||!s.Scope.Scoped||!s.Scope.HasActive||s.Scope.ControlledScope?.Pointer!=s.Sight.Pointer",StringComparison.Ordinal),"5 hud: 'active' is the session's sight being the controlled, scoped sight");
        Check(hud.Contains("scaler.uiScaleMode=CanvasScaler.ScaleMode.ScaleWithScreenSize;",StringComparison.Ordinal)&&hud.Contains("scaler.referenceResolution=new Vector2(1920f,1080f);",StringComparison.Ordinal),"5 hud: own canvas, ScaleWithScreenSize at 1920 x 1080");

        // ================= 6. range and other texts =================
        var rangeCases=new (double? M,string Text)[]{(null,"----"),(double.NaN,"----"),(double.PositiveInfinity,"----"),(-5,"----"),(.2,"----"),(.5,"0001"),(7,"0007"),(850,"0850"),(2350.4,"2350"),(2350.5,"2351"),(3999.6,"4000"),(9999.4,"9999"),(9999.5,"----")};
        Check(rangeCases.All(c=>AtgmSightHudRules.RangeText(c.M)==c.Text&&AtgmSightHudRules.RangeText(c.M).Length==4),"6 range: 4 digits in metres or ----, "+rangeCases.Length+" cases ("+string.Join(", ",rangeCases.Select(c=>(c.M?.ToString(CultureInfo.InvariantCulture)??"none")+"="+AtgmSightHudRules.RangeText(c.M)))+")");
        Check(AtgmSightHudRules.FlightText(null)=="FLT --.-"&&AtgmSightHudRules.FlightText(7.25)=="FLT 07.3"&&AtgmSightHudRules.FlightText(123)=="FLT 99.9"&&AtgmSightHudRules.ReadyText(2)=="RDY 2"&&AtgmSightHudRules.LauncherText(false)=="LCHR DN","6 texts: flight timer, rounds ready and launcher lines");
        Check(Math.Abs(AtgmSightHudRules.GuidanceSeconds(200,25,4000)-20)<1e-9&&AtgmSightHudRules.GuidanceSeconds(200,25,null)==25&&AtgmSightHudRules.MaxRangeMeters(200,25,4000)==4000&&AtgmSightHudRules.MaxRangeMeters(200,25,null)==5000&&AtgmSightHudRules.MaxRangeMeters(0,0,null)==null,
              "6 range: guided time and MAX RNG from the profile (speed x time, cut by the wire)");
        Check(AtgmSightHudRules.NearLimit(18.6,20)&&!AtgmSightHudRules.NearLimit(17,20)&&!AtgmSightHudRules.NearLimit(20,20)&&AtgmSightHudRules.OutOfRange(4200,4000)&&!AtgmSightHudRules.OutOfRange(null,4000),"6 range: MAX RNG warns in the last tenth (at least 1.5 s) and before launch beyond range");
        {
            var clock=new AtgmLaunchClock();clock.Observe((IntPtr)1,40f,100);var none=clock.Seconds(101);clock.Observe((IntPtr)1,40f,102);var still=clock.Seconds(103);clock.Observe((IntPtr)1,103.5f,103.5);var t=clock.Seconds(110);
            Check(none==null&&still==null&&t is {} s&&Math.Abs(s-6.5)<1e-9,"6 timer: the first sample is only a baseline; a new fire time starts the clock");
        }
        Check(Math.Abs(AtgmSightHudRules.PixelsPerMil(8)-7.582)<.01&&AtgmSightHudRules.PixelsPerMil(double.NaN)==AtgmSightHudRules.PixelsPerMil(10)&&AtgmSightHudRules.PixelsPerMil(170.5)==AtgmSightHudRules.PixelsPerMil(10)&&AtgmSightHudRules.PixelsPerMil(.5)==30,
              "6 reticle: NATO mils in reference pixels from the vertical field of view (8 deg: 7.58 px/mil), clamped 2..30");

        // ================= 7. layout: realistic symbols, element switches =================
        var digital=AtgmSightHudLayout.Build(AtgmHudStyle.Digital,defaults,AtgmSightHudRules.PixelsPerMil(8));var analog=AtgmSightHudLayout.Build(AtgmHudStyle.Analog,defaults,AtgmSightHudRules.PixelsPerMil(10));
        bool OpenCentre(AtgmHudLayout l,float gap)=>l.Items.Where(i=>i.Kind==AtgmHudKind.Line&&i.Element=="reticle").All(i=>Math.Abs(i.X)-i.W/2>=gap-1e-3||Math.Abs(i.Y)-i.H/2>=gap-1e-3);
        Check(OpenCentre(digital,16)&&OpenCentre(analog,6),"7 layout: both reticles have an open centre");
        var ticks=digital.Items.Where(i=>i.Id.StartsWith("tick-h+",StringComparison.Ordinal)).Select(i=>(double)i.X).OrderBy(x=>x).ToArray();
        Check(ticks.Length>5&&ticks.Zip(ticks.Skip(1),(a,b)=>b-a).All(d=>Math.Abs(d-2*digital.PixelsPerMil)<1e-3),"7 layout: Digital mil marks every 2 mils ("+ticks.Length+" on the right arm)");
        Check(digital.Items.All(i=>i.Kind==AtgmHudKind.Panel||i.Color.G>=i.Color.R&&i.Color.G>=i.Color.B),"7 layout: Digital is green monochrome (no coloured bars)");
        var stadia=analog.Items.Where(i=>Regex.IsMatch(i.Id,@"^stadia-\d+$")).ToArray();
        Check(stadia.Length==5&&stadia.Zip(stadia.Skip(1),(a,b)=>a.H>b.H).All(x=>x)&&Math.Abs(stadia[1].H-AtgmSightHudRules.StadiaMils(1000)*analog.PixelsPerMil)<1e-3,"7 layout: Analog stadia for a 2.4 m target at 500..3000 m, nearest tallest");
        Check(analog.Items.Where(i=>i.Kind==AtgmHudKind.Text).All(i=>!i.Text.Any(char.IsDigit))&&AtgmSightHudLayout.Frame(analog,new(2,2,true,3,20,1500,4000,1)).Texts.Count==0,"7 layout: Analog shows no digits");
        Check(analog.Items.Count(i=>i.Kind==AtgmHudKind.Lamp)==2&&digital.Items.Count(i=>i.Kind==AtgmHudKind.Lamp)==5,"7 layout: Analog has two lamps (READY, GUIDANCE), Digital five");
        Check(!digital.Items.Concat(analog.Items).Any(i=>Regex.IsMatch(i.Id+i.Text,@"arrow|box|bracket|target",RegexOptions.IgnoreCase)),"7 layout: no game markers (no box around the missile, no arrows)");
        var off4=AtgmSightHudLayout.Build(AtgmHudStyle.Digital,defaults with{RangeReadout=false,FlightTimer=false,StatusLamps=false,Reticle=false},8);
        var onlyRange=AtgmSightHudLayout.Build(AtgmHudStyle.Digital,defaults with{FlightTimer=false,StatusLamps=false,Reticle=false},8);
        Check(off4.Items.Count==0&&onlyRange.Items.All(i=>i.Element is "range" or "panel")&&onlyRange.Items.Any(i=>i.Id=="range"),"7 layout: each element switch removes exactly its items");
        var inFlight=AtgmSightHudLayout.Frame(digital,new(1,2,true,7.25,20,2350,4000,0));var nearEnd=AtgmSightHudLayout.Frame(digital,new(1,2,true,19,20,2350,4000,0));var idle=AtgmSightHudLayout.Frame(digital,new(2,2,false,null,20,null,4000,0));
        Check(inFlight.Lamps["track"]&&inFlight.Texts["flt"]=="FLT 07.3"&&!inFlight.Lamps["maxrng"]&&nearEnd.Lamps["maxrng"]&&!idle.Lamps["track"]&&idle.Texts["range"]=="----"&&idle.Texts["lchr"]=="LCHR DN"&&idle.Texts["rdy"]=="RDY 2",
              "7 frame: TRACK and timer in flight, MAX RNG near the end, ---- without a return");

        // ================= 8. file ledger and hooks against 0.22.91 =================
        var baseDir=Environment.GetEnvironmentVariable("EB_BASE_SRC");
        var ledgerText=new StringBuilder("file\tstate\n");
        static int Hooks(string s)=>s.Split('\n').Count(l=>l.Contains("[HarmonyPatch",StringComparison.Ordinal));
        if(!string.IsNullOrEmpty(baseDir)&&Directory.Exists(baseDir))
        {
            bool Skip(string rel)=>rel.Split('/').Any(p=>p is "bin" or "obj" or "tests" or ".git");
            Dictionary<string,string> Inventory(string dir)=>Directory.EnumerateFiles(dir,"*",SearchOption.AllDirectories).Select(f=>(Rel:Path.GetRelativePath(dir,f).Replace('\\','/'),Full:f)).Where(x=>!Skip(x.Rel)).ToDictionary(x=>x.Rel,x=>x.Full);
            var a=Inventory(baseDir);var b=Inventory(root);
            var diff=a.Keys.Union(b.Keys).Where(k=>!a.ContainsKey(k)||!b.ContainsKey(k)||!File.ReadAllBytes(a[k]).AsSpan().SequenceEqual(File.ReadAllBytes(b[k]))).OrderBy(k=>k,StringComparer.Ordinal).ToArray();
            foreach(var k in diff)ledgerText.Append(k).Append('\t').Append(!a.ContainsKey(k)?"added":!b.ContainsKey(k)?"removed":"changed").Append('\n');
            Check(diff.All(IsPlanned)&&diff.Length==7&&!diff.Any(k=>!b.ContainsKey(k)),$"8 ledger: only the planned files differ from 0.22.91 ({string.Join(", ",diff)}); {b.Count-diff.Length} files byte for byte");
            var protectedFiles=b.Keys.Where(k=>k.StartsWith("Material/",StringComparison.Ordinal)||Regex.IsMatch(k,@"^(CombatForensic|CombatReport|LayeredPlateReport|PenetrationWaterfall|NativeMaterial|RuntimeArmour|MaterialInterval|RuntimeOriginalTrajectory)[^/]*\.cs$")||
                (Regex.IsMatch(k,@"^RuntimeAtgm[^/]*\.cs$")&&k!="RuntimeAtgmGuidanceSight.cs"&&k!="RuntimeAtgmSightHud.cs")||Regex.IsMatch(k,@"^Atgm[^/]*\.cs$")&&k!="AtgmSightHudModel.cs").OrderBy(k=>k,StringComparer.Ordinal).ToArray();
            Check(protectedFiles.Length>500&&protectedFiles.All(k=>a.ContainsKey(k)&&File.ReadAllBytes(a[k]).AsSpan().SequenceEqual(File.ReadAllBytes(b[k]))),
                  $"8 ledger: the byte-equal set of the brief is 0.22.91 byte for byte ({protectedFiles.Length} files: hit path, Material/*, Forensic and report, ATGM guidance and firing RuntimeAtgm*/Atgm* outside the sight)");
            var before=a.Where(p=>p.Key.EndsWith(".cs",StringComparison.Ordinal)).Sum(p=>Hooks(File.ReadAllText(p.Value)));var after=b.Where(p=>p.Key.EndsWith(".cs",StringComparison.Ordinal)).Sum(p=>Hooks(File.ReadAllText(p.Value)));
            Check(before==47&&after==47,$"8 hooks: [HarmonyPatch annotations {before} -> {after}");
            string[] PatchLines(Dictionary<string,string> d)=>d.Where(p=>p.Key.EndsWith(".cs",StringComparison.Ordinal)).SelectMany(p=>File.ReadAllText(p.Value).Replace("\r\n","\n").Split('\n').Where(l=>Regex.IsMatch(l,@"^\s*\[Harmony(Prefix|Postfix|Finalizer|Transpiler)")).Select(l=>p.Key+"|"+l.Trim())).OrderBy(x=>x,StringComparer.Ordinal).ToArray();
            Check(PatchLines(a).SequenceEqual(PatchLines(b)),"8 hooks: no patch added or retargeted (the dead SetAimPoint postfix stays)");
        }
        else
        {
            var hooks=Directory.EnumerateFiles(root,"*.cs",SearchOption.AllDirectories).Where(f=>!Regex.IsMatch(f.Replace('\\','/'),"/(tests|bin|obj)/")).Sum(f=>Hooks(File.ReadAllText(f)));
            Check(hooks==47,"8 hooks: [HarmonyPatch annotations in this tree: "+hooks+" (47); EB_BASE_SRC not set, file ledger skipped");
        }
        Check(!Regex.IsMatch(hud+model,@"ControlLateUpdate\s*\)|HarmonyPatch\(typeof\(ScopeOverlay"),"8 hooks: no hook on the overlay plugin or ControlLateUpdate");

        // ================= preview layouts (same numbers as the code) =================
        object Json(AtgmHudLayout l,AtgmHudFrame f)=>new{style=l.Style.ToString(),pixelsPerMil=l.PixelsPerMil,width=1920,height=1080,
            lit=l.Lit,dim=l.Dim,guidanceLit=l.GuidanceLit,
            items=l.Items.Select(i=>new{i.Id,kind=i.Kind.ToString(),i.X,i.Y,i.W,i.H,color=i.Color,text=i.Kind==AtgmHudKind.Text&&f.Texts.TryGetValue(i.Id,out var t)?t:i.Text,i.Size,i.Align,i.Round,i.Element,
                lit=i.Kind==AtgmHudKind.Lamp?f.Lamps.TryGetValue(i.Id[..^5],out var on)&&on:i.Kind==AtgmHudKind.Text&&l.Style==AtgmHudStyle.Digital&&f.Lamps.TryGetValue(i.Id,out var tl)?tl:(bool?)null})};
        var options=new JsonSerializerOptions{WriteIndented=true,IncludeFields=true};
        var digitalFrame=AtgmSightHudLayout.Frame(digital,new(1,2,true,7.25,20,2350,4000,0));
        var analogFrame=AtgmSightHudLayout.Frame(analog,new(1,2,true,7.25,20,null,3750,0));
        File.WriteAllText(Path.Combine(output,"atgm-hud-digital.json"),JsonSerializer.Serialize(Json(digital,digitalFrame),options));
        File.WriteAllText(Path.Combine(output,"atgm-hud-analog.json"),JsonSerializer.Serialize(Json(analog,analogFrame),options));
        File.WriteAllText(Path.Combine(output,"FEAT92-STYLE.tsv"),styleTable.ToString());
        File.WriteAllText(Path.Combine(output,"FEAT92-FOLLOW.tsv"),follow.ToString());
        File.WriteAllText(Path.Combine(output,"FEAT92-HIDDEN.tsv"),hidden.ToString());
        File.WriteAllText(Path.Combine(output,"FEAT92-LEDGER.tsv"),ledgerText.ToString());
        File.WriteAllText(Path.Combine(output,"FEAT92-RESULT.json"),JsonSerializer.Serialize(new{passed=failures==0,checks,failures},new JsonSerializerOptions{WriteIndented=true}));
        Console.WriteLine($"FEAT92 PASS: {checks} checks, {failures} failures");
    }
}
