namespace SprocketEngineExpansion.Core;

public enum PowerpackTurboLayout { Compact, Outboard }
public sealed record PowerpackModelSettings(bool Enabled=true,double SizeFactor=1,double LengthFactor=.9,
    double HeightFactor=1,PowerpackTurboLayout TurboLayout=PowerpackTurboLayout.Compact,double TransmissionVolumeFactor=1)
{
    public PowerpackModelSettings Validate()
    {
        static void Range(double x,double min,double max,string name)
        {if(!double.IsFinite(x)||x<min||x>max)throw new ArgumentOutOfRangeException(name);}
        Range(SizeFactor,.8,1.25,nameof(SizeFactor));Range(LengthFactor,.8,1.25,nameof(LengthFactor));
        Range(HeightFactor,.8,1.25,nameof(HeightFactor));Range(TransmissionVolumeFactor,.5,1.5,nameof(TransmissionVolumeFactor));
        if(!Enum.IsDefined(typeof(PowerpackTurboLayout),TurboLayout))throw new ArgumentOutOfRangeException(nameof(TurboLayout));
        return this;
    }
    public Vec3 Dimensions(int cylinders,double litres)
    {
        double k=Math.Pow(litres,1d/3)*SizeFactor;
        return new(k*(.405+.120*(cylinders/2))*LengthFactor,k*(TurboLayout==PowerpackTurboLayout.Compact?.70:1.25),k*.62*HeightFactor);
    }
    public double Start=>.160*SizeFactor*LengthFactor;
    public double Pitch=>.120*SizeFactor*LengthFactor;
    public double Output=>.245*SizeFactor*LengthFactor;
    public double Crank=>.220*SizeFactor*HeightFactor;
    public double CentreOfMass=>.260*SizeFactor*HeightFactor;
}

/// <summary>Per-model original and clone; production and tests share the switch/invalidation policy.</summary>
public sealed class ModelDefinitionLease<T> where T:class
{
    public T Native {get;}
    public T Current {get;private set;}
    public bool Active {get;private set;}
    private PowerpackModelSettings? settings;
    private T? clone;
    public ModelDefinitionLease(T native){Native=Current=native;}
    public bool Select(bool active,PowerpackModelSettings value,Func<T,PowerpackModelSettings,T> makeClone)
    {
        if(active&&(clone==null||settings!=value)){clone=makeClone(Native,value);settings=value;}
        T next=active?clone!:Native;
        bool changed=!ReferenceEquals(Current,next);Current=next;Active=active;return changed;
    }
}

/// <summary>One owned render child. Retire before replacement, including unchanged rebuilds.</summary>
public sealed class RenderChildSlot<T> where T:class
{
    public T? Value {get;private set;}
    public void Rebuild(Func<T?> create,Action<T> retire)
    {Clear(retire);Value=create();}
    public void Clear(Action<T> retire)
    {var old=Value;Value=null;if(old!=null)retire(old);}
}
