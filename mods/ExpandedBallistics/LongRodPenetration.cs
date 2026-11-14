namespace SprocketExpandedBallistics;

// Finite eroding-phase Alekseevskii–Tate reference envelope, not a full terminal solver.
// ARL-TR-2855 equations 1–3; fixed reference steel and tungsten strength calibration.
internal static class LongRodPenetration
{
    private static readonly Dictionary<(DartBallistics Dart,double Density,double Velocity),DartBallistics> Cache = new();
    internal const double TargetDensity = 7850, RodStrength = 1e9, TargetResistance = 5.5e9;
    internal static double EnvelopeMm(double lengthM, double density, double velocity, int steps = 2048)
    {
        if (!double.IsFinite(lengthM) || lengthM <= 0 || !double.IsFinite(density) || density <= 0 ||
            !double.IsFinite(velocity) || velocity <= 0 || steps < 128 || steps > 32768)
            throw new ArgumentOutOfRangeException(nameof(lengthM));
        var delta = TargetResistance - RodStrength;
        double InterfaceSpeed(double v)
        {
            var numerator = density*v*v - 2*delta;
            if (v <= 0 || numerator <= 0) return 0;
            return Math.Clamp(numerator/(density*v + Math.Sqrt(density*TargetDensity*v*v + 2*(density-TargetDensity)*delta)), 0, v);
        }
        // x = -log(remaining length / initial length); avoids the 1/length singularity.
        (double V, double P) Derivative(double x, double v)
        {
            var u = InterfaceSpeed(v);
            if (u <= 0) return (0,0);
            var erosion = v-u;
            return (-RodStrength/(density*erosion), Math.Exp(-x)*u/erosion);
        }
        if (InterfaceSpeed(velocity) <= 0)
            throw new InvalidOperationException("APFSDS speed/density is outside the supported eroding long-rod regime; use a full-bore AP profile for slow projectiles.");
        var h = 16d/steps;
        double penetration = 0, vNow = velocity;
        for (var i=0; i<steps && InterfaceSpeed(vNow)>0; i++)
        {
            var x = i*h;
            var first = Derivative(x,vNow);
            var midpoint = Derivative(x+h*.5,vNow+first.V*h*.5);
            penetration += h*midpoint.P;
            vNow += h*midpoint.V;
        }
        return lengthM*1000*penetration;
    }

    // Current 0.2.56.1 native law, including its centimetre conversion and mm output.
    internal static double NativeBudgetMm(DartBallistics dart, double velocity) =>
        (float)Math.Pow((float)Math.Sqrt(dart.Mass)*(float)velocity /
            ((float)Math.Pow(dart.Diameter*1000f*.01f,.75f)*dart.PenetratorConstant),1.43f)*100f;

    internal static DartBallistics Bound(DartBallistics dart, double density, double referenceVelocity)
    {
        var key=(dart,density,referenceVelocity);
        lock(Cache){if(Cache.TryGetValue(key,out var cached))return cached;}
        var envelope = EnvelopeMm(dart.Length,density,referenceVelocity);
        var budget = NativeBudgetMm(dart,referenceVelocity);
        if (budget <= envelope) return Remember(key,dart);
        var required = Math.Ceiling(dart.PenetratorConstant*Math.Pow(budget/envelope,1d/1.43));
        if (!double.IsFinite(required) || required > ushort.MaxValue)
            throw new InvalidOperationException("APFSDS long-rod bound cannot be represented by the native penetrator constant; lower penetration quality or revise shell geometry.");
        var bounded = dart with {PenetratorConstant=(ushort)required};
        // Native float rounding can cross the bound by one integer K step.
        while (NativeBudgetMm(bounded,referenceVelocity)>envelope)
        {
            if (bounded.PenetratorConstant==ushort.MaxValue)
                throw new InvalidOperationException("APFSDS long-rod bound exceeds the native penetrator constant range.");
            bounded = bounded with {PenetratorConstant=(ushort)(bounded.PenetratorConstant+1)};
        }
        return Remember(key,bounded);
    }
    private static DartBallistics Remember((DartBallistics Dart,double Density,double Velocity) key,DartBallistics result)
    {
        lock(Cache){if(Cache.Count>=256)Cache.Clear();Cache[key]=result;}
        return result;
    }
}
