using System.Globalization;
namespace SprocketExpandedBallistics;

/// <summary>
/// One shot on the hit panel (wave 2b, HIT-PANEL.md). Built once per impact; every string is final,
/// so the HUD never formats per frame. Reference identity matters: the HUD repeater rebuilds only
/// when the list of block instances changes.
/// </summary>
internal sealed class HitBlock
{
    internal string Shooter="";
    internal int Number;internal string NumberText="",Direction="",Ammo="",Values="",Result="",Colour=HitBlockBuilder.Grey;
    internal bool Tandem;internal string Detail="",DamageShort="",DamageExpanded="";
    internal string[] Breakdown=Array.Empty<string>();
    /// <summary>0.22.23: the penetration budget of the impact (null when no layer was measured); built once with the block.</summary>
    internal WaterfallModel? Waterfall;
    internal double Time;
}

/// <summary>
/// 0.22.11, the one rule for the result word of a shot, shared by the hit panel, the Kit combat report and the HTML export (all three
/// build their result in <see cref="HitBlockBuilder.Build"/>). A round that stopped in armour but still did damage inside the vehicle
/// (crew killed or wounded, ammunition, engine, gun, fuel, transmission ...) got through: that is PENETRATED, not STOPPED. A stop in
/// armour with no internal damage stays STOPPED, and a ricochet stays a ricochet. A destroyed tank always counts as internal damage;
/// the TANK DESTROYED label itself is the separate chip/heading flag (<see cref="CombatDamageSummary.IsDestroyed"/>) beside the word.
/// </summary>
/// <remarks>0.22.43: a stop after the core reached an internal module with residual penetration (a perforated protective envelope, see
/// <see cref="CombatForensics.Envelope"/>) reads PENETRATED in <see cref="HitBlockBuilder.Build"/>.</remarks>
internal static class HitResultRule
{
    /// <summary>True when the shot's interaction events record damage to a vehicle component (the events the damage summary and the hit panel damage line use).</summary>
    internal static bool InternalDamage(IReadOnlyList<CombatReportEvent> events)
    {
        if(events==null||events.Count==0)return false;
        var summary=CombatDamageSummary.From(events);
        return summary.Any||summary.VehicleDestroyedEvent||HitBlockBuilder.Damage(events).Count>0;
    }
    /// <summary>The result word to show for a shot whose armour-only result is <paramref name="armourResult"/>.</summary>
    /// <summary>
    /// 0.22.22 (decision 2026-11-02): when the original core stopped (a native stop, or no residual penetration left) but the shot still hurt the vehicle
    /// (spall: modules or crew), the word is "STOPPED · spall damage", not PENETRATED. A real perforation (a core that passed with residual penetration)
    /// stays PENETRATED. Supersedes the 0.22.11 rule that turned every stop with damage into PENETRATED.
    /// </summary>
    internal static string Resolve(string armourResult,IReadOnlyList<CombatReportEvent> events,bool destroyed=false,bool zeroResidual=false)
        =>(armourResult==HitBlockBuilder.Stopped||armourResult==HitBlockBuilder.Penetrated&&zeroResidual)&&(destroyed||InternalDamage(events))?HitBlockBuilder.StoppedSpall:armourResult;
    /// <summary>Colour of a result that is not decided by the armour events alone: reads as a penetration (cyan) unless ERA or a material reaction outranks it.</summary>
    internal static string PenetratedColour(IReadOnlyList<CombatReportEvent> events)
    {
        var kept=(events??Array.Empty<CombatReportEvent>()).Where(e=>e!=null&&!(e.Kind=="nativeStop"||e.Kind=="outcome"&&e.Text.StartsWith("Stopped",StringComparison.OrdinalIgnoreCase)))
            .Append(new CombatReportEvent("plateResidual","","Penetrated"));
        return CombatHitSummaryModel.Category(kept).Colour;
    }
}

/// <summary>Pure: combat-report shot → hit block (reuses <see cref="CombatHitProjection"/> and the shared category colours).</summary>
internal static class HitBlockBuilder
{
    internal const string Penetrated="PENETRATED",Stopped="STOPPED",StoppedSpall="STOPPED · spall damage",Ricochet="RICOCHET",NoArmour="NO ARMOUR",Unknown="?";
    internal const string Grey="#A5ADB5";
    internal const int AmmoColumns=30,DetailColumns=120,LineColumns=100,MaxBreakdown=16;
    private static string N(double? v)=>v is {} x&&double.IsFinite(x)?Math.Round(x).ToString("0",CultureInfo.InvariantCulture):"?";
    private static string Clean(string? text,int columns)=>CombatHitSummaryModel.Fit(CombatReportPresentation.Escape(text??""),columns);
    private static double Reduction(CombatReportEvent e)=>e.BeforeMm is {} b&&e.AfterMm is {} a?Math.Max(0,b-a):0;

    internal static HitBlock Build(CombatReportShot shot)
    {
        var views=CombatHitProjection.Build(shot);
        var armour=views.Where(v=>v.Status!="Diagnostic only"&&(v.Layer?.Parts.Any(p=>p.Armour)==true||v.Status is "Stopped" or "Deflected" or "Passed")).ToArray();
        var main=armour.Where(v=>v.Stage!="Precursor").ToArray();
        var last=main.LastOrDefault();
        var tandem=shot.Events.Any(e=>e.Stage=="Precursor");
        string result=last==null
            ?(shot.Events.Any(e=>e.Kind=="componentResidual")?NoArmour:Unknown)
            :main.Any(v=>v.Status.StartsWith("Stopped",StringComparison.Ordinal))?Stopped
            :main.Any(v=>v.Status=="Deflected")?Ricochet
            :last.Status=="Passed"?Penetrated:Unknown;
        // The armour-only result above drives the numbers and the detail line; the word and colour that are shown come from the shared rule.
        double? before=shot.InitialPenetrationMm??main.FirstOrDefault()?.Before;
        double? after=result==Stopped?0:last?.After;
        var shown=HitResultRule.Resolve(result,shot.Events,CombatDamageSummary.IsDestroyed(shot),result==Penetrated&&after is {} residual&&double.IsFinite(residual)&&residual<=.5);
        // 0.22.43: a core that reached an internal module with residual penetration perforated the protective envelope: PENETRATED, also when it
        // came to rest further on (in the far-side armour). "STOPPED · spall damage" stays for an arrest ahead of every internal module.
        if((result==Stopped||shown==StoppedSpall)&&CombatForensics.EnvelopePerforated(shot))shown=Penetrated;
        var values=result==Ricochet||result==NoArmour?$"{N(before)} mm":result==Unknown?$"{N(before)} → ? mm":$"{N(before)} → {N(after)} mm";
        var colour=shown is Unknown or NoArmour?Grey:shown==StoppedSpall?CombatHitSummaryModel.Category(shot.Events).Colour:shown!=result?HitResultRule.PenetratedColour(shot.Events):CombatHitSummaryModel.Category(shot.Events).Colour;
        var tokens=Tokens(shot.Events,main);
        var end=EndToken(result,main.LastOrDefault(v=>!CombatZeroSpan.Is(v))??last); // 0.22.41: never name a hidden 0 mm layer
        if(end!=null)tokens.Add(end);
        if(tokens.Count==0)tokens.Add(result==Unknown?"outcome not measured":"no measured layer loss");
        var damage=Damage(shot.Events);
        var block=new HitBlock
        {
            Number=shot.Number,NumberText="#"+shot.Number.ToString(CultureInfo.InvariantCulture),Direction=Direction(shot.Direction),
            Shooter=shot.Direction=="Incoming"?"From: "+CombatReportTruth.Text(shot.Shooter,"unknown"):"",
            Ammo=Clean(shot.Profile,AmmoColumns),Tandem=tandem,Values=values,Result=shown,Colour=colour,
            Detail=Clean(string.Join(" · ",tokens),DetailColumns),
            DamageShort=damage.Count==0?"":"→ "+Clean(damage[0],40)+(damage.Count>1?$" +{damage.Count-1}":""),
            DamageExpanded=damage.Count==0?"":"→ "+Clean(string.Join(", ",damage.Take(2)),70)+(damage.Count>2?$" +{damage.Count-2}":"")
        };
        block.Breakdown=Breakdown(shot,armour,tandem,before,damage);
        block.Waterfall=PenetrationWaterfall.Build(shot,views);
        return block;
    }
    internal static string Direction(string? direction)=>direction switch{"Outgoing"=>"OUT","Incoming"=>"IN","Own vehicle self-hit"=>"SELF","Other vehicles"=>"OTHER",_=>""};

    /// <summary>ERA · materials (largest first) · steel, as the wave-1 summary computes them.</summary>
    internal static List<string> Tokens(IReadOnlyList<CombatReportEvent> events,IReadOnlyList<CombatHitView> main)
    {
        var tokens=new List<string>();
        double era=0;string eraName="";bool eraMain=false,eraAny=false,spent=false;
        var materials=new Dictionary<string,double>(StringComparer.Ordinal);
        foreach(var e in events)
        {
            if(e.Kind=="eraResidual"){era+=Reduction(e);eraAny=true;if(e.Stage!="Precursor")eraMain=true;}
            if(e.Kind is "lightEra" or "heavyEra"){eraAny=true;if(eraName.Length==0)eraName=EraName(e.Text);if(e.Stage!="Precursor")eraMain=true;}
            if(e.Kind=="eraSpent")spent=true;
            // 0.22.25: the line-of-sight correction is its own token (a note while the core passes, a stop when it ends it).
            if((e.Kind is "materialNote" or "materialStop")&&LegacyReportText.IsObliquityCorrection(e.Text))
            {materials["Obliquity"]=materials.GetValueOrDefault("Obliquity")+Reduction(e);continue;}
            if(e.Kind is "materialReaction" or "materialStop" or "rodOrientation")
            {
                var name=CombatReportLanguage.Scrub(CombatReportLanguage.Material(e.Text.Split(':',';')[0].Trim()));if(name.Length==0)name="material";
                materials[name]=materials.GetValueOrDefault(name)+Reduction(e);
            }
        }
        if(eraAny)tokens.Add($"ERA{(eraName.Length>0?" "+eraName:"")} −{era:0}"+(eraMain?"":" (precursor)"));
        else if(spent)tokens.Add("ERA already spent");
        foreach(var m in materials.OrderByDescending(m=>m.Value))tokens.Add($"{m.Key} −{m.Value:0}");
        double steel=0;var measured=new HashSet<string>(StringComparer.Ordinal);
        for(int i=events.Count-1;i>=0;i--)
        {
            var residual=events[i];
            if(residual.Kind!="plateResidual"||residual.HitId!=null&&!measured.Add(residual.HitId))continue;
            // 0.22.16: the skipped-plate residual already starts after the
            // cassette, so subtracting that cassette reaction would hide the
            // passive resistance from the steel token.
            double extra=residual.HitId==null||residual.Text.Contains("Skipped composite plate",StringComparison.Ordinal)?0:
                events.Where(e=>e.HitId==residual.HitId&&(e.Kind is "eraResidual" or "materialReaction" or "materialStop"||e.Kind=="materialNote"&&LegacyReportText.IsObliquityCorrection(e.Text))).Sum(Reduction);
            steel+=Math.Max(0,Reduction(residual)-extra);
        }
        if(steel>0)tokens.Add($"steel −{steel:0}");
        return tokens;
    }
    private static string EraName(string text)
    {
        var t=text.Trim();
        foreach(var suffix in new[]{" ERA brick"," turret ERA brick"," brick"})if(t.EndsWith(suffix,StringComparison.OrdinalIgnoreCase)){t=t[..^suffix.Length];break;}
        return t.Length>44?t[..44]:t;
    }
    internal static string Part(CombatHitView? view)
    {
        if(view==null)return "armour";
        if(view.Layer?.AirGapMm!=null)return "air gap";
        var parts=view.Layer?.Parts.Where(p=>p.Armour).ToArray()??Array.Empty<CombatLayerPart>();
        // 0.22.41: report names (no "(add-on)" preset tag, readable module ids, "composite array" for a native group of plates).
        if(parts.Length==1)return CombatReportLanguage.Module(parts[0].Name)+(string.IsNullOrWhiteSpace(parts[0].MaterialLabel)?"":" ("+CombatReportLanguage.Material(parts[0].MaterialLabel,parts[0].MaterialId)+")");
        if(parts.Length>1)return $"composite array ({parts.Length} plates)";
        return CombatReportLanguage.Module(view.Name);
    }
    private static string? EndToken(string result,CombatHitView? last)=>result switch
    {
        Stopped=>"stopped in "+Part(last),
        Ricochet=>"ricochet off "+Part(last)+(last?.Layer?.Parts.FirstOrDefault(p=>p.Armour)?.AngleDegrees is {} a?$" ({a:0}°)":""),
        Penetrated=>"through "+Part(last),
        _=>null
    };
    /// <summary>"gunner killed", "gun damaged": crew and component damage from the shot's interaction events.</summary>
    internal static IReadOnlyList<string> Damage(IReadOnlyList<CombatReportEvent> events)
    {
        var list=new List<string>();
        foreach(var e in events)
        {
            if(e.Kind!="interaction")continue;
            var t=e.Text;
            if(!(t.EndsWith(" killed",StringComparison.Ordinal)||t.EndsWith(" wounded",StringComparison.Ordinal)||t.EndsWith(" destroyed",StringComparison.Ordinal)||t.EndsWith(" damaged",StringComparison.Ordinal)))continue;
            if(t.StartsWith("Crew member ",StringComparison.Ordinal))t=t["Crew member ".Length..];
            t=CombatReportLanguage.Damage(t); // 0.22.41: "ammoRack destroyed" → "ammunition stowage destroyed"
            if(!list.Contains(t,StringComparer.Ordinal))list.Add(t);
        }
        // Killed/destroyed first: the most important damage is never hidden behind "+N".
        return list.OrderBy(t=>t.EndsWith(" killed",StringComparison.Ordinal)||t.EndsWith(" destroyed",StringComparison.Ordinal)?0:1).ToArray();
    }
    private static string[] Breakdown(CombatReportShot shot,IReadOnlyList<CombatHitView> armour,bool tandem,double? before,IReadOnlyList<string> damage)
    {
        var lines=new List<string>();
        foreach(var stage in armour.Select(v=>v.Stage).Distinct())
        {
            var stageViews=armour.Where(v=>v.Stage==stage).ToArray();
            if(tandem)lines.Add(stage=="Precursor"?$"PRECURSOR {N(stageViews.FirstOrDefault()?.Before)} mm":$"MAIN CHARGE {N(before)} mm");
            foreach(var v in stageViews)
            {
                if(CombatZeroSpan.Is(v))continue; // 0.22.41: a 0 mm layer that changes nothing is not listed
                var parts=v.Layer?.Parts.Where(p=>p.Armour).ToArray()??Array.Empty<CombatLayerPart>();
                var geometry=parts.Length==1?$" · {N(parts[0].ThicknessMm)} mm · {N(parts[0].AngleDegrees)}°":"";
                var exit=v.Status.StartsWith("Stopped",StringComparison.Ordinal)?"0":v.Status=="Deflected"?"ricochet":N(v.After);
                var delta=v.Before is {} b&&v.After is {} a?$"  −{Math.Max(0,b-a):0}":"";
                lines.Add(Clean($"{Part(v)}{geometry}   {N(v.Before)} → {exit}{delta}",LineColumns));
                var detail=Tokens(v.Details,Array.Empty<CombatHitView>());
                if(v.Reaction.Length>0&&detail.Count==0)detail.Add(CombatReportLanguage.Reaction(v.Reaction));
                if(detail.Count>0)lines.Add(Clean("   "+string.Join(" · ",detail),LineColumns));
            }
        }
        if(damage.Count>0)lines.Add(Clean("Damage: "+string.Join(", ",damage),LineColumns));
        if(lines.Count>MaxBreakdown){var omitted=lines.Count-MaxBreakdown+1;lines=lines.Take(MaxBreakdown-1).ToList();lines.Add($"… {omitted} more lines — see the combat report");}
        if(shot.OmittedEvents>0)lines.Add($"… {shot.OmittedEvents} events omitted — see the combat report");
        return lines.ToArray();
    }
}

/// <summary>
/// The blocks on the hit panel: newest first, at most <see cref="MaxBlocks"/>, expiring after
/// <see cref="Lifetime"/> seconds with a 2 s fade. A pinned block stays on top and never expires.
/// <see cref="Version"/> is the dirty flag: it changes only when the shown list changes.
/// </summary>
internal sealed class HitPanelBuffer
{
    internal const double FadeSeconds=2;internal const float PinnedAlpha=.75f;
    private readonly List<HitBlock> blocks=new();
    private int max=6;private double lifetime=10;
    private HitBlock[] shown=Array.Empty<HitBlock>();private int shownVersion=-1;
    private (int Version,string Count,string Pinned,string Lifetime) texts=(-1,"","","");
    internal int Version{get;private set;}
    internal HitBlock? Pinned{get;private set;}
    internal int Count=>blocks.Count;
    internal int MaxBlocks{get=>max;set{var v=Math.Clamp(value,3,8);if(v==max)return;max=v;Trim();Version++;}}
    internal double Lifetime{get=>lifetime;set{var v=double.IsFinite(value)?Math.Clamp(value,4,30):10;if(v==lifetime)return;lifetime=v;Version++;}}
    /// <summary>Pinned block first, then newest first. The same array instance until <see cref="Version"/> changes.</summary>
    internal IReadOnlyList<HitBlock> Shown
    {
        get
        {
            if(shownVersion==Version)return shown;
            shown=Pinned!=null?new[]{Pinned}.Concat(blocks.Where(b=>!ReferenceEquals(b,Pinned))).ToArray():blocks.ToArray();
            shownVersion=Version;return shown;
        }
    }
    internal HitBlock? Focus=>Pinned??(blocks.Count>0?blocks[0]:null);
    internal bool IsFocus(HitBlock block)=>ReferenceEquals(block,Focus);
    internal void Add(double now,HitBlock block)
    {
        if(!double.IsFinite(now)||block==null)return;
        block.Time=now;blocks.Insert(0,block);Trim();Version++;
    }
    private void Trim()
    {
        while(blocks.Count>max)
        {
            var i=blocks.FindLastIndex(b=>!ReferenceEquals(b,Pinned));
            if(i<0)break;
            blocks.RemoveAt(i);
        }
    }
    /// <summary>Remove expired blocks; true (and a new version) only when something left.</summary>
    internal bool Expire(double now)
    {
        if(!double.IsFinite(now)){var had=blocks.Count>0;Clear();return had;}
        bool changed=false;
        for(int i=blocks.Count-1;i>=0;i--)
        {
            var b=blocks[i];if(ReferenceEquals(b,Pinned))continue;
            if(now-b.Time>=lifetime||now<b.Time){blocks.RemoveAt(i);changed=true;}
        }
        if(changed)Version++;
        return changed;
    }
    /// <summary>1 while fresh, fading to 0 over the last 2 s; a pinned block sits at .75 once older than the lifetime.</summary>
    internal float Alpha(HitBlock block,double now)
    {
        var age=now-block.Time;
        if(!double.IsFinite(age))return 1f;
        if(ReferenceEquals(block,Pinned))return age>lifetime?PinnedAlpha:1f;
        var left=lifetime-age;
        return left>=FadeSeconds?1f:(float)Math.Clamp(left/FadeSeconds,0,1);
    }
    /// <summary>Pin the newest block, or unpin. Returns the new pinned state.</summary>
    internal bool TogglePin()
    {
        if(Pinned!=null){Pinned=null;Version++;return false;}
        if(blocks.Count==0)return false;
        Pinned=blocks[0];Version++;return true;
    }
    internal void Clear(){if(blocks.Count==0&&Pinned==null)return;blocks.Clear();Pinned=null;Version++;}
    private void Texts()
    {
        if(texts.Version==Version)return;
        texts=(Version,blocks.Count.ToString(CultureInfo.InvariantCulture),Pinned==null?"":"PINNED "+Pinned.NumberText,$"last {lifetime:0} s");
    }
    internal string CountText{get{Texts();return texts.Count;}}
    internal string PinnedText{get{Texts();return texts.Pinned;}}
    internal string LifetimeText{get{Texts();return texts.Lifetime;}}
}
