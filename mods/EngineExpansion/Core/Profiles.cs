namespace SprocketEngineExpansion.Core;

public enum EngineKind { Petrol, Diesel, GasTurbine }
public sealed record EngineSelection(EngineKind Kind=EngineKind.Petrol, bool Turbo=false, double BoostPercent=25, bool Powerpack=false)
{
    public bool IsNative => Kind==EngineKind.Petrol&&!Turbo&&!Powerpack;
    public void Validate()
    {
        if(!Enum.IsDefined(typeof(EngineKind),Kind)||!double.IsFinite(BoostPercent)||BoostPercent<0||BoostPercent>500)
            throw new ArgumentException("Engine selection is outside supported numerical bounds.");
        if(Kind==EngineKind.GasTurbine&&Turbo) throw new ArgumentException("External turbo is unavailable on the turbine proxy.");
    }
}
public sealed record Profile(double Power,double RPM,double Fuel,double Mass,double Cost,Curve? Shape);
public static class Catalog
{
    public static readonly Profile Petrol=new(1,1,1,1,1,null);
    public static readonly Profile Diesel=new(.95,.8,.85,1.15,1.15,new(new[]{new Point(.2,.8),new(.35,1),new(.5,1),new(.65,.92),new(.8,.78),new(1,.55)}));
    public static readonly Profile Turbine=new(1.50,1.20,2.10,1,1.80,new(new[]{new Point(.2,1.55),new(.35,1.42),new(.5,1.28),new(.65,1.14),new(.8,1),new(.9,.88),new(1,.74)}));
    public static Profile Get(EngineKind kind)=>kind switch {EngineKind.Petrol=>Petrol,EngineKind.Diesel=>Diesel,EngineKind.GasTurbine=>Turbine,_=>throw new ArgumentOutOfRangeException(nameof(kind))};
}
public sealed class EnginePlan
{
    public const double NmRpmPerKW=9549.29658551372;
    public EngineSelection Selection {get;}
    public Curve Native {get;}
    public Profile Profile {get;}
    public double NativeMaxRPM {get;}
    public double MaxRPM {get;}
    public double IdleRPM {get;}
    public double Amplitude {get;}
    public EnginePlan(EngineSelection selection,Curve nativeTorque,double maxRPM,double idleRPM)
    {
        selection.Validate();if(!double.IsFinite(maxRPM)||maxRPM<=0||!double.IsFinite(idleRPM)||idleRPM<0||idleRPM>=maxRPM)throw new ArgumentException("Invalid native RPM range.");
        Selection=selection;Native=nativeTorque;Profile=Catalog.Get(selection.Kind);NativeMaxRPM=maxRPM;MaxRPM=Math.Round(maxRPM*Profile.RPM);IdleRPM=idleRPM;
        if(IdleRPM>=MaxRPM)throw new ArgumentException("The native idle RPM is outside the selected profile range.");
        double peak=nativeTorque.MaxProduct(idleRPM/maxRPM,1)*maxRPM/NmRpmPerKW;
        Amplitude=Profile.Shape==null?1:peak*Profile.Power*NmRpmPerKW/(MaxRPM*Profile.Shape.MaxProduct(IdleRPM/MaxRPM,1));
        if(!double.IsFinite(Amplitude)||Amplitude<=0)throw new ArgumentException("Invalid native torque baseline.");
    }
    public double BaseTorque(double rpm)
    {
        if(!double.IsFinite(rpm)||rpm<0)throw new ArgumentOutOfRangeException(nameof(rpm));
        // Preserve native startup/idle samples, without boosting below idle.
        if(rpm<IdleRPM)return Native.At(rpm/NativeMaxRPM);
        if(Selection.Kind==EngineKind.GasTurbine)
        {
            double end=Math.Min(IdleRPM+MaxRPM*.05,MaxRPM);
            if(rpm<end)
            {
                double t=(rpm-IdleRPM)/(end-IdleRPM);t=t*t*(3-2*t);
                return Native.At(IdleRPM/NativeMaxRPM)*(1-t)+Amplitude*Profile.Shape!.At(end/MaxRPM)*t;
            }
        }
        return Profile.Shape==null?Native.At(rpm/NativeMaxRPM):Amplitude*Profile.Shape.At(rpm/MaxRPM);
    }
    // Turbo metadata remains serializable, but this build has no turbo gain or fuel penalty.
    public double Gain(double rpm,bool assemblyValid)=>0;
    public double Torque(double rpm,bool assemblyValid)=>BaseTorque(rpm);
    public double Power(double rpm,bool assemblyValid)=>BaseTorque(rpm)*rpm/NmRpmPerKW;
    public double FuelCoefficient(double rpm,bool assemblyValid)=>Profile.Fuel;
    public Curve Sample(bool assemblyValid,int subdivisions=512)
    {
        if(subdivisions<32||subdivisions>4096)throw new ArgumentOutOfRangeException(nameof(subdivisions));
        var xs=Enumerable.Range(0,subdivisions+1).Select(i=>i/(double)subdivisions)
            .Concat(Native.Points.Select(p=>p.X*NativeMaxRPM/MaxRPM)).Concat(Profile.Shape?.Points.Select(p=>p.X)??Array.Empty<double>())
            .Append(Math.Min(1,IdleRPM/MaxRPM+.05)).Append(IdleRPM/MaxRPM).Where(x=>x>=0&&x<=1).Distinct().OrderBy(x=>x);
        return new(xs.Select(x=>new Point(x,Torque(x*MaxRPM,assemblyValid))));
    }
}

