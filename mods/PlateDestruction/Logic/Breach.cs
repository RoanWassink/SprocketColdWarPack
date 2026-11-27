namespace PlateDestructionP0;

// ================================================================================================
// 1.0.0: Whole-tank destruction toggle, EB-integrated realistic blast breach thresholds,
// localized breach radius / accumulation zones, and inward breach overpressure damage math.
// Pure managed code (no Unity / Il2Cpp types) so all managed tests can run without the game.
// ================================================================================================

/// <summary>1.0.0: Whole-tank opt-out token stored on the root StructureBlueprint.Description. Tanks are destructible by default unless [no-destruction] is present.</summary>
public static class TankDestructionFlag
{
    public const string Token="[no-destruction]";

    public static bool IsDisabled(string? description)
        =>!string.IsNullOrEmpty(description)&&description!.IndexOf(Token,StringComparison.OrdinalIgnoreCase)>=0;

    public static string? SetEnabled(string? description,bool enabled)
    {
        if(enabled)
        {
            if(!IsDisabled(description))return description;
            string s=description!;
            int idx;
            while((idx=s.IndexOf(Token,StringComparison.OrdinalIgnoreCase))>=0)
                s=s.Remove(idx,Token.Length);
            while(s.Contains("  ",StringComparison.Ordinal))s=s.Replace("  "," ");
            s=s.Trim();
            return s.Length==0?null:s;
        }
        if(IsDisabled(description))return description;
        string trimmed=(description??"").Trim();
        return trimmed.Length==0?Token:trimmed+" "+Token;
    }
}

/// <summary>1.0.0: Realistic blast breach thresholds, steep 1/(1+(d/r)^3) breach distance falloff, localized hole sizing, and 0.4 m accumulation zones.</summary>
public static class BlastBreachMath
{
    public static double EffectiveCalibreMm(int effectCalibreMm,bool ebHasData,double ebCalibreMm)
        =>ebHasData&&double.IsFinite(ebCalibreMm)&&ebCalibreMm>0?ebCalibreMm:Math.Max(0,effectCalibreMm);

    /// <summary>Plate thickness (mm RHA) this blast can realistically breach on direct contact. Uses EB telemetry directly when present, otherwise matches EB HeRules (20 mm at 100 mm HE, scaling with cal^2) and conservative HESH/HEAT rules.</summary>
    public static double EffectiveBreachMm(string? kind,double calibreMm,bool ebHasData,double ebBreachMm,double heMmAt100=20.0)
    {
        if(ebHasData&&double.IsFinite(ebBreachMm))return Math.Max(0.0,ebBreachMm);
        if(!double.IsFinite(calibreMm)||calibreMm<=0)return 0.0;
        string k=(kind??"").Trim().ToLowerInvariant();
        if(k=="era"||k=="aphe"||k=="heat")return 0.0;
        double r=calibreMm/100.0;
        if(k=="hesh")return 14.0*Math.Pow(r,1.5);
        double baseMm=double.IsFinite(heMmAt100)&&heMmAt100>0?heMmAt100:20.0;
        return baseMm*r*r;
    }

    /// <summary>Steep local overpressure falloff for actual steel breaching: 50% at rCore (~0.28 m at 100 mm, ~0.43 m at 152 mm), ~11% at 2*rCore.</summary>
    public static double BreachCapacityAtDistanceMm(double contactBreachMm,double calibreMm,double distM)
    {
        if(!double.IsFinite(contactBreachMm)||contactBreachMm<=0||!double.IsFinite(distM)||distM<0)return 0.0;
        double cal=double.IsFinite(calibreMm)&&calibreMm>0?calibreMm:100.0;
        double rCore=Math.Max(0.18,0.28*(cal/100.0));
        double u=distM/rCore;
        return contactBreachMm/(1.0+u*u*u);
    }

    /// <summary>True only when the plate is within 2.2x of the shell's contact breach rating (prevents tiny shells from chipping through 200 mm composite armor).</summary>
    public static bool CanAccumulateBreach(double localThMm,double contactBreachMm)
        =>double.IsFinite(localThMm)&&localThMm>0&&double.IsFinite(contactBreachMm)&&contactBreachMm>0&&localThMm<=contactBreachMm*2.2;

    /// <summary>Localized hole radius (m) proportional to calibre and breach margin (typically 0.12-0.38 m; never tears away half a turret).</summary>
    public static double BreachHoleRadiusM(double effectiveCapMm,double localThMm,double calibreMm,double maxRadiusM=0.38)
    {
        double th=Math.Max(localThMm,2.0);
        double ratio=Math.Max(1.0,effectiveCapMm/th);
        double calScale=Math.Clamp((calibreMm>0?calibreMm:100.0)/100.0,0.4,2.2);
        double r=0.08*calScale+0.09*Math.Sqrt(Math.Max(0.0,ratio-1.0))*Math.Sqrt(calScale);
        double cap=double.IsFinite(maxRadiusM)&&maxRadiusM>0.10?Math.Min(maxRadiusM,0.45):0.38;
        return Math.Clamp(r,0.09,cap);
    }

    /// <summary>Quantizes a local impact point into a 0.4 m accumulation bucket so repeated hits on the same spot weaken it without summing hits on opposite ends of the hull.</summary>
    public static (int X,int Y,int Z) ZoneKey(V3 localPt,double bucketM=0.40)
    {
        double inv=1.0/Math.Max(0.10,bucketM);
        return ((int)Math.Floor(localPt.X*inv),(int)Math.Floor(localPt.Y*inv),(int)Math.Floor(localPt.Z*inv));
    }

    /// <summary>1.0.1: Recognizes Expanded Ballistics [Solid block] composite plates whose visual mesh is a thin hollow shell.</summary>
    public static bool IsSolidBlockName(string? meshName)
        =>!string.IsNullOrEmpty(meshName)&&meshName!.IndexOf("[Solid block]",StringComparison.OrdinalIgnoreCase)>=0;

    /// <summary>1.0.1: Realistic visual blast dent depth (mm) for non-breaching hits: thin/medium plates buckle strongly, while thick 80-350+ mm solid armor only takes realistic shallow surface pitting instead of 120 mm cave-ins.</summary>
    public static double RealisticBlastDentDepthMm(
        double rawBlastDmg,
        double localThMm,
        double blastStrengthPerMm,
        double defaultPlateStrength,
        double hullTurretBlastScale,
        double blastMaxDepthMm,
        out double effQ)
    {
        effQ=0;
        if(!double.IsFinite(rawBlastDmg)||rawBlastDmg<=0||!double.IsFinite(blastMaxDepthMm)||blastMaxDepthMm<=0)return 0;
        double th=double.IsFinite(localThMm)&&localThMm>0?localThMm:20.0;
        double boost=1.0;
        if(th>=25.0&&hullTurretBlastScale>1.0)
        {
            double taper=th<=60.0?1.0:Math.Clamp(1.0-(th-60.0)/60.0,0.0,1.0);
            boost=1.0+(hullTurretBlastScale-1.0)*taper;
        }
        double dentStr=BlastMath.Strength(blastStrengthPerMm,Math.Min(th,90.0),defaultPlateStrength)/Math.Max(0.1,boost);
        effQ=DentMath.BlastRatio(rawBlastDmg,dentStr);
        double maxCap=th<=40.0?blastMaxDepthMm:Math.Clamp(blastMaxDepthMm*Math.Pow(40.0/th,0.75),14.0,blastMaxDepthMm);
        return DentMath.BlastDepthMm(effQ,maxCap);
    }

    /// <summary>1.0.2: Blast radius (m) within which HE/HESH strips exterior ERA bricks off intact or dented armor (e.g. ~0.68 m for 152 mm HE).</summary>
    public static double EraStripRadiusM(double calibreMm,double radiusPer100Mm=0.45)
    {
        if(!double.IsFinite(calibreMm)||calibreMm<50.0||!double.IsFinite(radiusPer100Mm)||radiusPer100Mm<=0)return 0.0;
        return Math.Clamp(radiusPer100Mm*(calibreMm/100.0),0.20,1.25);
    }
}

/// <summary>1.0.0/1.0.2: Realistic enclosed-compartment behind-breach overpressure &amp; fragmentation damage to exposed crew and interior modules.</summary>
public static class BreachDamageMath
{
    public static double ExcessMm(double breachCapMm,double localThMm)
    {
        if(!double.IsFinite(breachCapMm)||!double.IsFinite(localThMm))return 0.0;
        return Math.Max(0.0,breachCapMm-Math.Max(0.0,localThMm)*0.65);
    }

    /// <summary>Effective behind-armor overpressure reach (m) inside a breached compartment (~1.7 m for 75 mm HE on thin steel up to ~2.7 m for 152 mm HE).</summary>
    public static double ConeRadiusM(double calibreMm,double breachCapMm,double localThMm)
    {
        double excess=ExcessMm(breachCapMm,localThMm);
        if(excess<=1.0||!(breachCapMm>0))return 0.0;
        double energyRatio=Math.Clamp(excess/breachCapMm,0.0,1.0);
        double cal=Math.Clamp((calibreMm>0?calibreMm:100.0)/100.0,0.3,2.2);
        return Math.Clamp(0.90+1.35*cal*(0.45+0.55*Math.Sqrt(energyRatio)),0.85,3.00);
    }

    /// <summary>True if the module lies in the inward hemisphere behind the breached plate (or immediately touching the hole within nearFreeRadiusM).</summary>
    public static bool IsBehindHole(V3 holeCenter,V3 inwardNormal,V3 modulePos,double nearFreeRadiusM=0.35)
    {
        var d=VM.Sub(modulePos,holeCenter);
        double len=VM.Len(d);
        if(len<=nearFreeRadiusM)return true;
        var n=VM.Norm(inwardNormal,new V3(0,-1,0));
        return VM.Dot(d,n)/len>=-0.05;
    }

    /// <summary>Fraction (0..1) of a module/crew pool's max health removed by enclosed-compartment overpressure &amp; fragments through a fresh breach.</summary>
    public static double ModuleDamageFraction(double distM,double coneRadiusM,double calibreMm,double excessMm,double breachCapMm,bool isCrew)
    {
        if(excessMm<=1.0||!(coneRadiusM>0)||!(breachCapMm>0)||!double.IsFinite(distM)||distM<0||distM>=coneRadiusM)return 0.0;
        double energyRatio=Math.Clamp(excessMm/breachCapMm,0.0,1.0);
        double calFactor=Math.Clamp(Math.Pow(Math.Max(20.0,calibreMm)/100.0,1.35),0.30,2.2);
        double rHalf=Math.Max(0.25,coneRadiusM*0.52);
        double u=distM/rHalf;
        double taper=Math.Clamp(1.0-distM/coneRadiusM,0.0,1.0);
        double falloff=(1.0/(1.0+u*u))*Math.Sqrt(taper);
        double baseFrac=(isCrew?1.35:0.75)*calFactor*(0.45+0.55*energyRatio)*falloff;
        return Math.Clamp(baseFrac,0.0,1.0);
    }
}
