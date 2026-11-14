using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
namespace SprocketVisualEffects;

internal sealed record LampSettings
{
    [JsonPropertyName("color")] public string Color { get; init; } = "#FFFFFF";
    [JsonPropertyName("intensity")] public float Intensity { get; init; } = 300;
    [JsonPropertyName("range")] public float Range { get; init; } = 30;
    [JsonPropertyName("angle")] public float Angle { get; init; } = 60;
    internal bool Infrared => Color == "#000000";
    internal static readonly string[] Forms={"round","rect","marker"};
    internal static readonly string[] Guids={"7b696e26-98bd-4e6d-ad6a-9009814b0c01","7b696e26-98bd-4e6d-ad6a-9009814b0c02","7b696e26-98bd-4e6d-ad6a-9009814b0c03"};
    internal static readonly string[] Presets={"Red","Amber","White","Green","Blue","IR (no visible emission)"};
    internal static readonly string[] Colors={"#FF1808","#FF9A18","#FFFFFF","#28FF38","#3070FF","#000000"};
    // 0.4.4: rect (brake) 8->80 cd and marker 2->20 cd so the lens reads as lit in daylight.
    internal static readonly float[] LegacyIntensity={300,8,2};
    internal static float MigrateIntensity(int form,float configured)=>configured==LegacyIntensity[Math.Clamp(form,0,2)]?Default(form).Intensity:configured;
    internal static LampSettings Default(int form)=>new(){Color=Colors[form==0?2:form==1?0:1],Intensity=form==0?300:form==1?80:20,Range=form==0?30:form==1?6:3};
    internal static bool ValidColor(string? s)=>s?.Length==7 && s[0]=='#' && uint.TryParse(s.AsSpan(1),NumberStyles.HexNumber,CultureInfo.InvariantCulture,out _);
    internal LampSettings Sanitize(LampSettings fallback)=>this with {
        Color=ValidColor(Color)?Color.ToUpperInvariant():fallback.Color,
        Intensity=Safe(Intensity,fallback.Intensity,0,100000),Range=Safe(Range,fallback.Range,.25f,100),Angle=Safe(Angle,60,5,120)};
    internal static float Safe(float x,float fallback,float min,float max)=>float.IsFinite(x)?Math.Clamp(x,min,max):fallback;
    internal static LampSettings Parse(string? json,LampSettings fallback) {
        if(string.IsNullOrEmpty(json)||System.Text.Encoding.UTF8.GetByteCount(json)>4096)return fallback;
        try{return (JsonSerializer.Deserialize<LampSettings>(json)??fallback).Sanitize(fallback);}catch{return fallback;}
    }
    internal string Json()=>JsonSerializer.Serialize(this);
    internal (float R,float G,float B) Rgb() {
        uint n=uint.Parse(Color.AsSpan(1),NumberStyles.HexNumber,CultureInfo.InvariantCulture);
        return (((n>>16)&255)/255f,((n>>8)&255)/255f,(n&255)/255f);
    }
}
internal static class LampPolicy
{
    internal const float FadeSeconds=.05f;
    internal static float Fade(float old,bool on,float dt)=>Math.Clamp(old+(on?1:-1)*LampSettings.Safe(dt,0,0,.1f)/FadeSeconds,0,1);
    internal static float Lod(float distance,float limit) {
        limit=LampSettings.Safe(limit,250,10,2000);
        if(!float.IsFinite(distance)||distance>=limit)return 0;
        return Math.Clamp((limit-distance)/(.2f*limit),0,1);
    }
    internal static bool Allocate(int index,int budget,bool playing,bool visible,float fade,float lod)=>playing && visible && fade>0 && lod>0 && index<Math.Clamp(budget,0,128);
}
