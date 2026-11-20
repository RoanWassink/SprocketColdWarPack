using BepInEx.Configuration;
using Il2CppInterop.Runtime;
using Sprocket.Vehicles;
using Sprocket.Vehicles.AttachedBehaviours;
using Sprocket.Vehicles.Cannons;
using Sprocket.Vehicles.UI;
using Sprocket.WeaponFramework;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using NativeScopeMask=Sprocket.Vehicles.Weapons.Scope;
using NVector=System.Numerics.Vector3;
using Object=UnityEngine.Object;

namespace SprocketExpandedBallistics;

/// <summary>
/// 0.22.92: the ATGM guidance sight's scope mask choice and its own HUD.
/// No hook of its own: it runs from the HUD driver of the top-attack HUD
/// (DamageFeedBehaviour.Update) and from RuntimeAtgmGuidanceSight's end paths.
/// The native mask is the HDRP post-process Sprocket.Vehicles.Weapons.Scope of the
/// player's ScopeOverlayVehicleControllerPlugin (field postProcess). Its overlay
/// switches the whole volume each LateUpdate and writes only the circle centre,
/// so Hidden switches off just that one component (VolumeComponent.active) and
/// puts back exactly the old value. The HUD draws on its own isolated canvas.
/// </summary>
internal static class RuntimeAtgmSightHud
{
    private static ConfigEntry<string>? mask,style;
    private static ConfigEntry<int>? digitalYear;
    private static ConfigEntry<bool>? rangeReadout,flightTimer,statusLamps,reticle;
    internal static void Configure(ConfigFile config)
    {
        const string s=AtgmSightHudConfig.Section;
        mask=config.Bind(s,AtgmSightHudConfig.MaskKey,AtgmSightHudConfig.DefaultMask,new ConfigDescription(AtgmSightHudConfig.MaskDescription,new AcceptableValueList<string>(AtgmSightHudConfig.MaskValues)));
        style=config.Bind(s,AtgmSightHudConfig.StyleKey,AtgmSightHudConfig.DefaultStyle,new ConfigDescription(AtgmSightHudConfig.StyleDescription,new AcceptableValueList<string>(AtgmSightHudConfig.StyleValues)));
        digitalYear=config.Bind(s,AtgmSightHudConfig.YearKey,AtgmSightHudConfig.DefaultDigitalYear,new ConfigDescription(AtgmSightHudConfig.YearDescription,new AcceptableValueRange<int>(AtgmSightHudConfig.MinimumYear,AtgmSightHudConfig.MaximumYear)));
        rangeReadout=config.Bind(s,AtgmSightHudConfig.RangeKey,true,"Digital HUD: the 4-digit laser range in metres along the guidance line ('----' without a return).");
        flightTimer=config.Bind(s,AtgmSightHudConfig.TimerKey,true,"Digital HUD: seconds since the last launch of this sight's launchers, while the missile can still be guided.");
        statusLamps=config.Bind(s,AtgmSightHudConfig.LampsKey,true,"HUD status lamps: rounds ready, launcher, TRACK and MAX RNG (Digital); READY and GUIDANCE (Analog).");
        reticle=config.Bind(s,AtgmSightHudConfig.ReticleKey,true,"HUD reticle: the thin cross with mil marks (and the range stadia in the Analog style).");
    }
    internal static AtgmSightHudOptions Options=>new(AtgmSightHudConfig.Mask(mask?.Value),AtgmSightHudConfig.Style(style?.Value),AtgmSightHudConfig.Year(digitalYear?.Value??AtgmSightHudConfig.DefaultDigitalYear),
        rangeReadout?.Value??true,flightTimer?.Value??true,statusLamps?.Value??true,reticle?.Value??true);

    // ---------------- mask ----------------
    private sealed class MaskHandle:IAtgmMaskHandle
    {
        internal readonly NativeScopeMask Scope;
        internal MaskHandle(NativeScopeMask scope){Scope=scope;}
        public IntPtr Key=>Scope.Pointer;
        public bool Alive{get{try{return !Scope.WasCollected&&Scope!=null;}catch{return false;}}}
        public bool Active{get=>Scope.active;set=>Scope.active=value;}
    }
    private static readonly AtgmScopeMaskLedger ledger=new();
    private static readonly Dictionary<IntPtr,MaskHandle> masks=new();
    private static readonly Dictionary<IntPtr,float> maskRetry=new();
    private static readonly HashSet<string> warnings=new();
    private static void Warn(string area,Exception? ex=null){if(warnings.Add(area))Plugin.ModLog.LogWarning("[ATGM sight HUD] "+area+(ex==null?"":": "+ex.Message));}
    // The overlay plugin of this scope controller, found once (no hook on the plugin).
    private static MaskHandle? MaskOf(ScopeController scope)
    {
        var key=scope.Pointer;
        if(masks.TryGetValue(key,out var known)&&known.Alive)return known;
        masks.Remove(key);
        if(maskRetry.TryGetValue(key,out var retry)&&Time.unscaledTime<retry)return null;
        maskRetry[key]=Time.unscaledTime+2;
        foreach(var plugin in Object.FindObjectsOfType<ScopeOverlayVehicleControllerPlugin>())
        {
            if(plugin==null||plugin.controller?.Pointer!=key||plugin.postProcess==null)continue;
            var handle=new MaskHandle(plugin.postProcess);masks[key]=handle;maskRetry.Remove(key);
            if(ReleaseDiagnostics.Enabled)Plugin.ModLog.LogInfo($"[ATGM sight HUD] MASK scope={key.ToInt64():X} volume component={handle.Key.ToInt64():X} active={handle.Active}");
            return handle;
        }
        Warn("Native scope mask not found; it stays as it is.");
        return null;
    }
    // Every end path of a sight session (switch, Dispose, PlayerChanged, Release, Clear).
    internal static void SessionEnded(IntPtr controller)
    {
        try{ledger.End(controller);}catch(Exception ex){Warn("Mask restore",ex);}
        if(hudOwner==controller){Hide();clock.Reset();lastRange=null;nextRange=0;hudOwner=IntPtr.Zero;}
    }
    internal static void Clear()
    {
        try{ledger.RestoreAll();}catch(Exception ex){Warn("Mask restore",ex);}
        masks.Clear();maskRetry.Clear();years.Clear();clock.Reset();lastRange=null;nextRange=0;hudOwner=IntPtr.Zero;
        Destroy();failures=0;retryAt=0;warnings.Clear();
    }

    // ---------------- HUD ----------------
    private static GameObject? root;
    private static AtgmHudLayout? layout;
    private static readonly Dictionary<string,TextMeshProUGUI> texts=new();
    private static readonly Dictionary<string,Image> lampImages=new();
    private static Sprite? round;
    private static bool fontReported;
    private static int failures;
    private static float retryAt;
    private static IntPtr hudOwner;
    private static readonly AtgmLaunchClock clock=new();
    private static double? lastRange;
    private static float nextRange;
    private static readonly Dictionary<IntPtr,int?> years=new();
    internal static void Failed(Exception ex)
    {
        if(failures++<4)Plugin.ModLog.LogWarning("[ATGM sight HUD] "+ex);
        Destroy();retryAt=Time.unscaledTime+5;
    }
    private static void Hide(){if(root!=null)root.SetActive(false);}
    private static void Destroy()
    {
        if(root!=null)Object.Destroy(root);
        root=null;layout=null;texts.Clear();lampImages.Clear();
    }
    // Driven every frame by DamageFeedBehaviour.Update, like the top-attack HUD.
    internal static void Update()
    {
        var options=Options;
        bool active;ScopeController? scope=null;GunnerSight? sight=null;Cannon[] group=Array.Empty<Cannon>();
        try{active=RuntimeAtgmGuidanceSight.TryActiveView(out scope,out sight,out group);}
        catch(Exception ex){active=false;Warn("Sight session",ex);}
        // Safety net: no active ATGM sight (switched, zoomed out, left), so nothing stays hidden.
        if(!active||scope==null||sight==null||group.Length==0)
        {
            ledger.SafetyNet(IntPtr.Zero,false);
            if(hudOwner!=IntPtr.Zero){clock.Reset();lastRange=null;hudOwner=IntPtr.Zero;}
            Hide();return;
        }
        var hide=options.Mask==AtgmScopeMaskMode.Hidden;
        try{ledger.SafetyNet(scope.Pointer,hide);if(hide)ledger.Apply(scope.Pointer,MaskOf(scope),true);}
        catch(Exception ex){Warn("Scope mask",ex);try{ledger.RestoreAll();}catch{}}
        if(hudOwner!=scope.Pointer){clock.Reset();lastRange=null;nextRange=0;hudOwner=scope.Pointer;}
        foreach(var c in group)try{if(c.Behaviour is {} b)clock.Observe(c.Pointer,b.LastFireTime,Time.timeAsDouble);}catch{}
        var resolved=AtgmSightHudRules.Resolve(options.Style,Year(sight),options.DigitalFromYear);
        if(!AtgmSightHudRules.Visible(resolved,true,Cursor.visible,RuntimeDamageFeed.IsEditor)||Time.unscaledTime<retryAt){Hide();return;}
        var ppm=AtgmSightHudRules.PixelsPerMil(FieldOfView());
        var wanted=AtgmSightHudLayout.Build(resolved,options,ppm);
        if(root==null||layout==null||layout.Signature!=wanted.Signature){Destroy();if(!Build(wanted))return;}
        root!.SetActive(true);
        Apply(AtgmSightHudLayout.Frame(layout!,State(group,options)));
    }
    private static double FieldOfView(){try{var camera=Camera.main;return camera!=null?camera.fieldOfView:double.NaN;}catch{return double.NaN;}}
    private static int? Year(GunnerSight sight)
    {
        if(years.TryGetValue(sight.Pointer,out var known))return known;
        int? year=null;
        try{year=sight.Vehicle.Tech.Date.Year;}catch{}
        if(year==null)try{year=sight.Vehicle.DesignInfo.Date.Year;}catch{}
        return years[sight.Pointer]=year;
    }
    private static AtgmHudState State(Cannon[] group,AtgmSightHudOptions options)
    {
        int ready=0,serving=0;
        foreach(var c in group)
        {
            try
            {
                var b=c.Behaviour;if(b==null||c.HealthFraction<=0||!b.Functioning)continue;
                serving++;if(b.LoadState==LoadState.Loaded)ready++;
            }
            catch{}
        }
        var primary=group[0];
        double guidance=0;double? maxRange=null;
        try
        {
            if(RuntimeShellSelection.CannonProfile(primary) is {Atgm:{} atgm} profile)
            {
                var wire=profile.Wire is {Transport:"wire"} w?w.MaximumLength:(double?)null;
                guidance=AtgmSightHudRules.GuidanceSeconds(atgm.FlightSpeed,atgm.MaximumFlightTime,wire);
                maxRange=AtgmSightHudRules.MaxRangeMeters(atgm.FlightSpeed,atgm.MaximumFlightTime,wire);
            }
        }
        catch{}
        // The laser line is the guidance line (TryAim), sampled five times a second.
        if(options.RangeReadout&&Time.unscaledTime>=nextRange)
        {
            nextRange=Time.unscaledTime+.2f;lastRange=null;
            try
            {
                if(RuntimeAtgmGuidanceSight.TryAim(primary,out var origin,out var direction,out _)&&primary.Vehicle?.Behaviour is {} vehicle&&
                   RuntimeTopAttackLock.TryAcquire(vehicle,new Ray(origin,direction),out var point,out _))lastRange=Vector3.Distance(origin,point);
            }
            catch{}
        }
        return new(ready,group.Length,serving>0,clock.Seconds(Time.timeAsDouble),guidance,lastRange,maxRange,Time.unscaledTimeAsDouble);
    }
    private static GameObject RectObject(string name)=>new(name,new Il2CppSystem.Type[]{Il2CppType.Of<RectTransform>()});
    private static Sprite? Round()
    {
        if(round!=null)return round;
        try
        {
            const int n=32;var texture=new Texture2D(n,n,TextureFormat.RGBA32,false){hideFlags=HideFlags.HideAndDontSave,filterMode=FilterMode.Bilinear};
            for(int y=0;y<n;y++)for(int x=0;x<n;x++)
            {var d=Mathf.Sqrt((x+.5f-n/2f)*(x+.5f-n/2f)+(y+.5f-n/2f)*(y+.5f-n/2f));texture.SetPixel(x,y,new Color(1,1,1,Mathf.Clamp01(n/2f-d)));}
            texture.Apply();
            round=Sprite.Create(texture,new Rect(0,0,n,n),new Vector2(.5f,.5f));round.hideFlags=HideFlags.HideAndDontSave;
        }
        catch(Exception ex){Warn("Round lamp sprite unavailable; square lamps",ex);}
        return round;
    }
    private static Color C(AtgmHudColor c)=>new(c.R,c.G,c.B,c.A);
    private static bool Build(AtgmHudLayout wanted)
    {
        var font=TMP_Settings.defaultFontAsset;
        if(font==null)
        {
            if(!fontReported){fontReported=true;Plugin.ModLog.LogInfo("[ATGM sight HUD] TMP default font unavailable; waiting.");}
            return false;
        }
        root=RectObject("ATGM guidance sight HUD");
        Object.DontDestroyOnLoad(root);
        var canvas=root.AddComponent<Canvas>();
        canvas.renderMode=RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder=80;
        var scaler=root.AddComponent<CanvasScaler>();
        scaler.uiScaleMode=CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution=new Vector2(1920f,1080f);
        // Match the height, so a mil is the same number of reference pixels on any aspect ratio.
        scaler.matchWidthOrHeight=1f;
        foreach(var item in wanted.Items)
        {
            var child=RectObject(item.Id);child.transform.SetParent(root.transform,false);
            var rect=child.GetComponent<RectTransform>();
            rect.anchorMin=rect.anchorMax=new Vector2(.5f,.5f);
            if(item.Kind==AtgmHudKind.Text)
            {
                rect.pivot=new Vector2(item.Align<0?0:item.Align>0?1:.5f,.5f);
                rect.anchoredPosition=new Vector2(item.X,item.Y);rect.sizeDelta=new Vector2(420,item.Size*1.4f);
                var text=child.AddComponent<TextMeshProUGUI>();
                text.font=font;text.fontSize=item.Size;text.color=C(item.Color);text.richText=false;text.raycastTarget=false;
                text.enableWordWrapping=false;text.overflowMode=TextOverflowModes.Overflow;
                text.alignment=item.Align<0?TextAlignmentOptions.Left:item.Align>0?TextAlignmentOptions.Right:TextAlignmentOptions.Center;
                text.text=item.Text;texts[item.Id]=text;
                continue;
            }
            rect.pivot=new Vector2(.5f,.5f);
            rect.anchoredPosition=new Vector2(item.X,item.Y);rect.sizeDelta=new Vector2(item.W,item.H);
            var image=child.AddComponent<Image>();image.raycastTarget=false;image.color=C(item.Color);
            if(item.Round&&Round() is {} sprite)image.sprite=sprite;
            if(item.Kind==AtgmHudKind.Lamp)lampImages[item.Id.EndsWith("-lamp",StringComparison.Ordinal)?item.Id[..^5]:item.Id]=image;
        }
        layout=wanted;
        return true;
    }
    private static void Apply(AtgmHudFrame frame)
    {
        var l=layout!;
        foreach(var pair in frame.Texts)if(texts.TryGetValue(pair.Key,out var text)&&text.text!=pair.Value)text.text=pair.Value;
        foreach(var pair in frame.Lamps)
        {
            var lit=pair.Value?pair.Key=="guidance"?l.GuidanceLit:l.Lit:l.Dim;
            if(lampImages.TryGetValue(pair.Key,out var image))image.color=C(lit);
            // Digital lamps carry their legend; it lights with the lamp.
            if(l.Style==AtgmHudStyle.Digital&&texts.TryGetValue(pair.Key,out var legend))legend.color=C(pair.Value?l.Lit:l.Dim);
        }
    }
    internal static NVector N(Vector3 v)=>new(v.x,v.y,v.z);
    internal static Vector3 U(NVector v)=>new(v.X,v.Y,v.Z);
}
