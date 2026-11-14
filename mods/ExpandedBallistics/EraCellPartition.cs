namespace SprocketExpandedBallistics;

// Pure, bounded partition policy for placeable ERA components that may grow.
// Coordinates are metres in the component's scaled local surface plane. This
// code deliberately has no Unity/native dependency so the same thresholds are
// regression-tested on every build.
internal readonly record struct EraCellLayout(
    double MinU,double MinV,double PitchM,int CountU,int CountV,double ToleranceM,double PitchV=0)
{
    // Legacy finite-cell panels use one square pitch. Modular whole-cell panels
    // use distinct width/length pitches; PitchV==0 keeps the legacy meaning.
    internal double PitchUM=>PitchM;
    internal double PitchVM=>PitchV>0?PitchV:PitchM;
    internal double MaxU=>MinU+CountU*PitchUM;
    internal double MaxV=>MinV+CountV*PitchVM;
    internal int CellCount=>CountU*CountV;
}

internal static class EraCellPartition
{
    // A boundary dead band keeps a hit from alternating between two cells when
    // the impact point is numerically on a seam. It is one millimetre, far below
    // any projectile/geometry resolution in Sprocket.
    internal const double BorderToleranceM=.001;
    // Half-pitch hysteresis: a new zone appears when the scaled extent reaches
    // (n+0.5)*pitch, not after every tiny drag. This is applied symmetrically for
    // both growth and shrink, so the count is deterministic.
    internal const double CountHysteresis=.0001;

    internal static int Count(double extentM,double pitchM)
    {
        if(!double.IsFinite(extentM)||!double.IsFinite(pitchM)||extentM<=0||pitchM<=0)return 0;
        var raw=extentM/pitchM;
        if(!double.IsFinite(raw)||raw<=0)return 0;
        if(raw>4096)return 4097;
        // Round-to-nearest with an explicit dead band around .5. At exactly
        // 3.0*pitch the count stays 3; at 3.5*pitch it becomes 4.
        var count=(int)Math.Floor(raw+0.5+CountHysteresis);
        return Math.Max(1,count);
    }

    // Whole-cell modular counts are driven by the native scale, not by a
    // permanently authored lattice. A dead band keeps a value exactly on a
    // half-step deterministic; the result is clamped to the descriptor maximum.
    internal static int CountForScale(double scale, int maximum)
    {
        // Native mirroring reports a NEGATIVE signed X/Z model scale. Count is a
        // magnitude property, so only zero/non-finite/out-of-bound input is
        // invalid; rejecting negative scale here would leave the native default
        // block on the right side while the left side rebuilt normally.
        if(!double.IsFinite(scale)||scale==0||maximum<1||maximum>64)return 0;
        var magnitude=Math.Abs(scale);
        if(magnitude>maximum+0.5)return maximum;
        var count=(int)Math.Floor(magnitude+0.5+CountHysteresis);
        return Math.Clamp(count,1,maximum);
    }

    internal static bool TryModular(double scaleX,double scaleZ,double cellWidthM,double cellLengthM,
        int maxCountU,int maxCountV,out EraCellLayout layout,out string error,int fixedCountV=0)
    {
        layout=default;error="";
        if(!Finite(scaleX)||!Finite(scaleZ)||scaleX==0||scaleZ==0){error="non-finite or zero modular scale";return false;}
        if(!Finite(cellWidthM)||!Finite(cellLengthM)||cellWidthM<=0||cellLengthM<=0){error="invalid modular cell size";return false;}
        if(maxCountU<1||maxCountV<1||maxCountU>64||maxCountV>64){error="modular count bound is invalid";return false;}
        var countU=CountForScale(scaleX,maxCountU);
        var countV=CountForScale(scaleZ,maxCountV);
        if(fixedCountV<0||fixedCountV>maxCountV){error="fixed row count is invalid";return false;}
        if(fixedCountV>0)countV=fixedCountV;
        if(countU<1||countV<1){error="degenerate modular count";return false;}
        if((long)countU*countV>4096){error="modular cell count exceeds limit";return false;}
        var width=countU*cellWidthM;
        var length=countV*cellLengthM;
        layout=new(-width/2,-length/2,cellWidthM,countU,countV,BorderToleranceM,cellLengthM);
        return true;
    }

    internal static bool TryLayout(double minU,double maxU,double minV,double maxV,
        double pitchM,int maxCells,out EraCellLayout layout,out string error)
    {
        layout=default;error="";
        if(!Finite(minU)||!Finite(maxU)||!Finite(minV)||!Finite(maxV)||!Finite(pitchM))
        {error="non-finite geometry or pitch";return false;}
        if(maxU<=minU||maxV<=minV){error="empty or inverted surface bounds";return false;}
        if(pitchM<=0){error="pitch must be positive";return false;}
        if(maxCells<1||maxCells>4096){error="max cell count outside supported bound";return false;}
        var countU=Count(maxU-minU,pitchM);
        var countV=Count(maxV-minV,pitchM);
        if(countU==0||countV==0){error="degenerate cell count";return false;}
        var total=(long)countU*countV;
        if(total>maxCells){error=$"cell count {total} exceeds limit {maxCells}";return false;}
        layout=new(minU,minV,pitchM,countU,countV,BorderToleranceM);
        return true;
    }

    // Stable index in [0,count-1]. Coordinates within one millimetre of a seam
    // snap to that seam before the floor, so a mirrored/rotated hit and its
    // visual triangle use the same deterministic cell.
    internal static bool TryCell(in EraCellLayout layout,double u,double v,out int x,out int y)
    {
        x=0;y=0;
        if(!Finite(u)||!Finite(v))return false;
        var su=Snap(layout,u,layout.MinU,layout.PitchUM);
        var sv=Snap(layout,v,layout.MinV,layout.PitchVM);
        var qx=(su-layout.MinU)/layout.PitchUM;
        var qy=(sv-layout.MinV)/layout.PitchVM;
        if(!double.IsFinite(qx)||!double.IsFinite(qy))return false;
        x=qx<=0?0:qx>=layout.CountU?layout.CountU-1:(int)Math.Floor(qx);
        y=qy<=0?0:qy>=layout.CountV?layout.CountV-1:(int)Math.Floor(qy);
        return true;
    }

    internal static bool TryTriangleCell(in EraCellLayout layout,
        double au,double av,double bu,double bv,double cu,double cv,out int x,out int y)
        =>TryCell(layout,(au+bu+cu)/3.0,(av+bv+cv)/3.0,out x,out y);

    private static double Snap(in EraCellLayout layout,double value,double origin,double pitch)
    {
        var offset=value-origin;
        var nearest=Math.Round(offset/pitch,MidpointRounding.AwayFromZero);
        var seam=nearest*pitch;
        return Math.Abs(offset-seam)<=Math.Min(layout.ToleranceM,pitch*.25)
            ? origin+seam
            : value;
    }

    // Area scaling, never volume scaling. Depth is locked by the part descriptor;
    // if a caller supplies non-finite/zero scales the answer is NaN so the caller
    // can fail closed instead of granting free mass/area.
    internal static double AreaScale(double scaleX,double scaleZ)
    {
        if(!Finite(scaleX)||!Finite(scaleZ)||scaleX==0||scaleZ==0)return double.NaN;
        var area=Math.Abs(scaleX*scaleZ);
        return double.IsFinite(area)&&area>0?area:double.NaN;
    }

    internal static bool DepthLocked(double scaleY)=>Finite(scaleY)&&Math.Abs(Math.Abs(scaleY)-1)<=1e-3;

    internal static double MassScale(double scaleX,double scaleZ,double maximum)
    {
        var area=AreaScale(scaleX,scaleZ);
        if(!double.IsFinite(area))return double.NaN;
        return double.IsFinite(maximum)&&maximum>0?Math.Min(area,maximum):area;
    }

    private static bool Finite(double value)=>double.IsFinite(value);
}
