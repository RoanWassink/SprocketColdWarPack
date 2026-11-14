namespace SprocketVisualEffects;

// 0.4.4 lens/halo appearance. Pure math (no Unity types) so LampChecks can test it.
// 0.4.3 wrote physical radiance (cd / lens area, up to 150000) with exposure weight 1:
// at designer exposure the red channel ran far past 1 and the tonemapper pushed the hue to
// yellow; in daylight play the same radiance was too dim. 0.4.4 writes display-referred
// emission (exposure weight 0) and caps the brightest channel, so the hue ratio is kept.
internal static class LampLook
{
    internal const float MaxChannelDefault=1.6f;   // brightest emitted channel, display units
    internal const float ReferenceCd=80f;          // a day-visible brake light reaches full lens brightness
    internal const float MinimumFraction=.15f;     // any non-zero lamp still reads as "on"

    // Lens brightness in 0..maxChannel from the physical intensity setting (log response).
    internal static float Brightness(float candela,float maxChannel) {
        maxChannel=LampSettings.Safe(maxChannel,MaxChannelDefault,.25f,4f);
        if(!float.IsFinite(candela)||candela<=0)return 0;
        float f=MathF.Log10(1+candela)/MathF.Log10(1+ReferenceCd);
        return maxChannel*Math.Clamp(f,MinimumFraction,1f);
    }

    // Emission colour: hue normalised so its brightest channel is 1, then scaled; no channel
    // can exceed maxChannel, and channel ratios equal the configured colour's ratios.
    internal static (float R,float G,float B) Emission(float r,float g,float b,float candela,float maxChannel,float fade) {
        float m=MathF.Max(r,MathF.Max(g,b));
        if(!(m>0)||!float.IsFinite(m))return (0,0,0);
        float k=Brightness(candela,maxChannel)*Math.Clamp(float.IsFinite(fade)?fade:0,0,1)/m;
        return (r*k,g*k,b*k);
    }

    // Halo: soft additive-looking glow quad, alpha bounded, diameter relative to the lens.
    internal static float HaloAlpha(float candela,float strength,float fade) {
        strength=LampSettings.Safe(strength,.45f,0,1);
        if(!float.IsFinite(candela)||candela<=0)return 0;
        float f=Math.Clamp(MathF.Log10(1+candela)/MathF.Log10(1+ReferenceCd),MinimumFraction,1f);
        return strength*f*Math.Clamp(float.IsFinite(fade)?fade:0,0,1);
    }
    // 0.4.5: lens radius of the stock model (LampLens, measured from the installed stock meshes).
    internal static float HaloDiameter(int form,float partScale)=>LampLens.Radius[Math.Clamp(form,0,2)]*2*3.2f*LampSettings.Safe(partScale,1,.25f,4f);
}
