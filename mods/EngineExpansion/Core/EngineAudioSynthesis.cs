namespace SprocketEngineExpansion.Core;

/// <summary>
/// Seamless procedural loop synthesizer (48 kHz mono float samples) for Gas Turbine
/// jet exhaust roar + soft compressor whine and Diesel deep V12 growl + warm turbo air rush.
/// Pure .NET, no Unity APIs.
/// </summary>
public static class EngineAudioSynthesis
{
    public const int SampleRate=48000;

    public static float[] CreateTurbineLoop(double seconds=2.0)=>
        SynthesizeLayeredLoop(
            seconds,
            seed:0x54555242,
            peakCap:0.88,
            noiseBands:new[]
            {
                // Deep jet exhaust column rumble (~60-220 Hz) + broadband jet whoosh (~250-1500 Hz) + gentle air sheen.
                (80,380,1.80),
                (12,72,1.15),
                (3,14,0.22)
            },
            chugHz:0.0,
            chugDepth:0.0,
            tones:new[]{(280.0,0.09,0.0),(560.0,0.045,0.8),(840.0,0.02,1.7)});

    public static float[] CreateTurboWhistleLoop(double seconds=2.0)=>
        SynthesizeMuscleDieselLoop(seconds,seed:0x44494553,peakCap:0.90);

    private static float[] SynthesizeMuscleDieselLoop(double seconds,int seed,double peakCap)
    {
        double clamped=double.IsFinite(seconds)?Math.Clamp(seconds,0.25,10.0):2.0;
        int n=(int)Math.Round(SampleRate*clamped);
        var rng=new Random(seed);
        var raw=new double[n];
        for(int i=0;i<n;i++)raw[i]=rng.NextDouble()*2.0-1.0;

        // Aggressive exhaust bark turbulence (120-700 Hz bite) + subtle warm turbo air rush.
        var barkLp=CircularBoxSmooth(CircularBoxSmooth(raw,14),14);
        var barkTrend=CircularBoxSmooth(barkLp,110);
        var airLp=CircularBoxSmooth(CircularBoxSmooth(raw,6),6);
        var airTrend=CircularBoxSmooth(airLp,26);

        double twoPiOverN=2.0*Math.PI/n;
        double f0=Math.Max(1.0,Math.Round(38.0*clamped));
        double halfOrder=Math.Max(1.0,Math.Round(19.0*clamped));
        double bankLope=Math.Max(1.0,Math.Round(57.0*clamped));

        var buf=new double[n];
        double peak=0.0;
        for(int i=0;i<n;i++)
        {
            // Asymmetric multi-harmonic combustion pulse passed through soft-clipper for muscle-car / T-72 V12 bite.
            double pulse=
                0.55*Math.Sin(twoPiOverN*f0*i)
                +0.42*Math.Sin(twoPiOverN*(f0*2.0)*i+0.45)
                +0.30*Math.Sin(twoPiOverN*(f0*3.0)*i+0.95)
                +0.22*Math.Sin(twoPiOverN*(f0*4.0)*i+1.40)
                +0.15*Math.Sin(twoPiOverN*(f0*5.0)*i+0.20)
                +0.10*Math.Sin(twoPiOverN*(f0*6.0)*i+0.75)
                +0.16*Math.Sin(twoPiOverN*bankLope*i+1.10);
            double lopeMod=1.0+0.22*Math.Sin(twoPiOverN*halfOrder*i);
            double bark=Math.Tanh(2.3*pulse)*lopeMod;
            double turb=(barkLp[i]-barkTrend[i])*1.65+(airLp[i]-airTrend[i])*0.28;
            double v=bark*0.78+turb;
            buf[i]=v;
            double a=Math.Abs(v);
            if(a>peak)peak=a;
        }

        double scale=peak>peakCap&&peak>1e-9?peakCap/peak:1.0;
        var result=new float[n];
        for(int i=0;i<n;i++)
            result[i]=(float)Math.Clamp(buf[i]*scale,-1.0,1.0);
        return result;
    }

    private static float[] SynthesizeLayeredLoop(
        double seconds,
        int seed,
        double peakCap,
        (int LowpassRadius,int HighpassRadius,double Gain)[] noiseBands,
        double chugHz,
        double chugDepth,
        (double FreqHz,double Gain,double Phase)[] tones)
    {
        double clamped=double.IsFinite(seconds)?Math.Clamp(seconds,0.25,10.0):2.0;
        int n=(int)Math.Round(SampleRate*clamped);
        var rng=new Random(seed);
        var raw=new double[n];
        for(int i=0;i<n;i++)raw[i]=rng.NextDouble()*2.0-1.0;

        var noise=new double[n];
        foreach(var band in noiseBands)
        {
            // Circular two-pass box smoothing forms a wrap-around bandpass noise floor (no seam click).
            var lp=CircularBoxSmooth(CircularBoxSmooth(raw,band.LowpassRadius),band.LowpassRadius);
            var trend=CircularBoxSmooth(lp,band.HighpassRadius);
            for(int i=0;i<n;i++)noise[i]+=(lp[i]-trend[i])*band.Gain;
        }

        var buf=new double[n];
        double twoPiOverN=2.0*Math.PI/n;
        double[] cycles=new double[tones.Length];
        for(int t=0;t<tones.Length;t++)cycles[t]=Math.Max(1.0,Math.Round(tones[t].FreqHz*clamped));
        double chugCycles=chugHz>0?Math.Max(1.0,Math.Round(chugHz*clamped)):0.0;

        double peak=0.0;
        for(int i=0;i<n;i++)
        {
            double tone=0.0;
            for(int t=0;t<tones.Length;t++)
                tone+=tones[t].Gain*Math.Sin(twoPiOverN*cycles[t]*i+tones[t].Phase);
            if(chugCycles>0)tone*=1.0+chugDepth*Math.Sin(twoPiOverN*chugCycles*i);
            double v=noise[i]+tone;
            buf[i]=v;
            double a=Math.Abs(v);
            if(a>peak)peak=a;
        }

        double scale=peak>peakCap&&peak>1e-9?peakCap/peak:1.0;
        var result=new float[n];
        for(int i=0;i<n;i++)
            result[i]=(float)Math.Clamp(buf[i]*scale,-1.0,1.0);
        return result;
    }

    private static double[] CircularBoxSmooth(double[] input,int radius)
    {
        int n=input.Length;
        var output=new double[n];
        if(radius<=0||n==0){Array.Copy(input,output,n);return output;}
        int r=Math.Min(radius,(n-1)/2);
        int width=r*2+1;
        double sum=0.0;
        for(int k=-r;k<=r;k++)sum+=input[(k%n+n)%n];
        for(int i=0;i<n;i++)
        {
            output[i]=sum/width;
            int outIdx=((i-r)%n+n)%n;
            int inIdx=(i+r+1)%n;
            sum+=input[inIdx]-input[outIdx];
        }
        return output;
    }
}
