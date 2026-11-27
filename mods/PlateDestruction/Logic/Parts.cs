namespace PlateDestructionP0;

// ================================================================================================
// 0.9.0: Destructible loose exterior parts (smoke launchers, lamps, stowage, tools, antennas)
// and comprehensive core-part / cannon / mantlet hierarchy guards.
// Pure managed code (no Unity / Il2Cpp types) so all managed tests can run without the game.
// ================================================================================================

/// <summary>Classifies VehicleComponents into protected core parts (cannons, mantlets, drivetrain, wheels, hull/turret), optics/hatches, ERA, and destructible loose exterior parts.</summary>
public static class PartFilter
{
    private static readonly string[] CoreTokens=
    {
        "cannon","mantlet","barrel","breech","muzzle","recoil","weapon",
        "turretring","turretbasket","wheel","axle","track","suspension","torsion","spring",
        "engine","transmission","powertrain","drivesprocket","idler",
        "crew","ammo","fuel","layingdrive","traversemotor","decoupler",
        "platestructure","armature","posture","hinge","mast","rotator","motor"
    };

    private static readonly string[] OpticsTokens=
    {
        "sight","viewport","hatch","cupola","rangefinder","periscope","optic","thermal"
    };

    private static readonly string[] EraTokens=
    {
        "kontakt","relikt","duplet","brat","arat","nizh","nozh","varma","malachit","kaktus",
        "fy1","fy2","fy4","fy5","stage1cell","stage2cell","reactive","erablock","erahousing","eracassette"
    };

    private static bool ContainsAny(string? s,string[] tokens)
    {
        if(string.IsNullOrEmpty(s))return false;
        string lo=s!.ToLowerInvariant();
        for(int i=0;i<tokens.Length;i++)
            if(lo.Contains(tokens[i],StringComparison.Ordinal))return true;
        return false;
    }

    private static bool HasEraWord(string? s)
    {
        if(string.IsNullOrEmpty(s))return false;
        if(ContainsAny(s,EraTokens))return true;
        string lo=s!.ToLowerInvariant();
        int idx=0;
        while((idx=lo.IndexOf("era",idx,StringComparison.Ordinal))>=0)
        {
            bool leftOk=idx==0||!char.IsLetter(lo[idx-1]);
            bool rightOk=idx+3>=lo.Length||!char.IsLetter(lo[idx+3]);
            if(leftOk&&rightOk)return true;
            idx+=3;
        }
        return false;
    }

    /// <summary>True for any structural/functional vehicle core component (cannons, mantlets, barrels, wheels, tracks, engine, crew, ammo, plates, hinges, masts) that must never be blown off as an attached/loose part.</summary>
    public static bool IsCoreComponent(string? typeName,string? componentId=null)
    {
        if(AttachedFilter.IsCoreStructuralPart(typeName))
        {
            // GunnerSight/Hatch/Viewport are handled separately as optics/hatches unless explicitly excluded as core.
            if(typeName is not ("GunnerSight" or "Hatch" or "Viewport"))return true;
        }
        return ContainsAny(typeName,CoreTokens)||ContainsAny(componentId,CoreTokens);
    }

    /// <summary>True for optics, sights, rangefinders, viewports, cupolas, and hatches.</summary>
    public static bool IsOpticsOrHatch(string? typeName,string? componentId=null,string? goName=null)
        =>ContainsAny(typeName,OpticsTokens)||ContainsAny(componentId,OpticsTokens)||ContainsAny(goName,OpticsTokens);

    /// <summary>True for ERA housings/cells managed separately by Expanded Ballistics.</summary>
    public static bool IsEraComponent(string? typeName,string? componentId=null,string? goName=null)
        =>HasEraWord(typeName)||HasEraWord(componentId)||HasEraWord(goName);

    /// <summary>1.0.2: True for an internal ERA active cell sub-part (not the outer housing brick).</summary>
    public static bool IsEraActiveCell(string? componentId,string? goName=null)
    {
        bool chk(string? s)=>!string.IsNullOrEmpty(s)&&(s!.Contains("cell",StringComparison.OrdinalIgnoreCase)||s.Contains("active",StringComparison.OrdinalIgnoreCase));
        return chk(componentId)||chk(goName);
    }

    /// <summary>True when a component is eligible as a standalone destructible loose exterior part (e.g. smoke launchers, lamps, stowage, tools, antennas).</summary>
    public static bool IsLoosePartCandidate(string? typeName,string? componentId,string? goName,bool includeOptics,bool includeEra)
    {
        if(string.IsNullOrEmpty(typeName))return false;
        bool isModel=string.Equals(typeName,"VehicleObjectModel",StringComparison.Ordinal)
            ||typeName!.Contains("Smoke",StringComparison.OrdinalIgnoreCase);
        if(!isModel)return false;
        if(IsCoreComponent(typeName,componentId))return false;
        if(!includeOptics&&IsOpticsOrHatch(typeName,componentId,goName))return false;
        if(!includeEra&&IsEraComponent(typeName,componentId,goName))return false;
        return true;
    }
}

/// <summary>Strength, kinetic hit damage, and segment-distance helpers for loose exterior parts.</summary>
public static class LoosePartMath
{
    public const int MaxPartsPerBlast=8;
    public const double MaxPartSizeM=2.2;

    /// <summary>Estimated part durability from its bounding-box diagonal (m): small lamps/smoke launchers ~20-35, large stowage boxes ~60-90.</summary>
    public static double Strength(double baseStrength,double sizeM)
    {
        double b=baseStrength>0&&!double.IsNaN(baseStrength)&&!double.IsInfinity(baseStrength)?baseStrength:35.0;
        double s=sizeM>0&&!double.IsNaN(sizeM)&&!double.IsInfinity(sizeM)?Math.Clamp(sizeM/0.35,0.5,2.5):1.0;
        return Math.Clamp(b*s,10.0,250.0);
    }

    /// <summary>Damage dealt to a loose exterior part by a direct kinetic projectile hit.</summary>
    public static double KineticDamage(double energyMJ,int calibreMm)
    {
        double e=energyMJ>0&&!double.IsNaN(energyMJ)&&!double.IsInfinity(energyMJ)?energyMJ:0.0;
        double c=calibreMm>0?calibreMm:0.0;
        return Math.Max(e*40.0,c*1.5);
    }

    /// <summary>Squared distance from point p to finite segment [a, b].</summary>
    public static double SegmentDistSq(V3 p,V3 a,V3 b)
    {
        var ab=VM.Sub(b,a);
        double lenSq=VM.Dot(ab,ab);
        if(lenSq<1e-12){var d=VM.Sub(p,a);return VM.Dot(d,d);}
        double t=Math.Clamp(VM.Dot(VM.Sub(p,a),ab)/lenSq,0.0,1.0);
        var q=VM.Add(a,VM.Mul(ab,t));
        var diff=VM.Sub(p,q);
        return VM.Dot(diff,diff);
    }
}
