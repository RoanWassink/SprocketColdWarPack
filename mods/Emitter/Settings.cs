using System.Text.Json;
namespace SprocketEmitter;

internal sealed record Settings
{
    public string Item {get;init;}="casing";
    public bool FollowCaliber {get;init;}=true;
    public float Caliber {get;init;}=30;
    public float Speed {get;init;}=3;
    public float Spread {get;init;}=8;
    public float Spin {get;init;}=180;
    public int Count {get;init;}=1;
    public bool Continuous {get;init;}
    public float Interval {get;init;}=.5f;
    public float Lifetime {get;init;}=10;
    public bool Inherit {get;init;}=true;
    public bool Flash {get;init;}=true;
    public bool Smoke {get;init;}=true;
    // IO-API v1.2 §7.4: Mast freezes the normalized schema defaults into each part's data.
    // The schema and the revision-2 reader therefore use these fixed constants, never the cfg.
    internal static readonly Settings Fixed=new(){FollowCaliber=true};
    internal static float Finite(float x,float fallback,float min,float max)=>float.IsFinite(x)?Math.Clamp(x,min,max):fallback;
    internal Settings Sanitize()=>this with {Item=Item is "casing" or "projectile"?Item:"casing",Caliber=Finite(Caliber,30,5,155),Speed=Finite(Speed,3,.5f,15),Spread=Finite(Spread,8,0,30),Spin=Finite(Spin,180,0,720),Count=Math.Clamp(Count,1,8),Interval=Finite(Interval,.5f,.05f,5),Lifetime=Finite(Lifetime,10,2,60)};
    // Missing fields take the given defaults; invalid values take the default with one callback.
    // Unknown keys are ignored here and stay stored by Mast (the mod never writes settings).
    internal static Settings Parse(string? json,Settings defaults,Action? invalid=null) {
        var d=defaults.Sanitize() with {FollowCaliber=true};if(string.IsNullOrWhiteSpace(json))return d;
        try {
            using var doc=JsonDocument.Parse(json);
            if(doc.RootElement.ValueKind!=JsonValueKind.Object){invalid?.Invoke();return d;}
            var root=doc.RootElement;
            float F(string k,float fallback,float min,float max) {
                if(!root.TryGetProperty(k,out var p))return fallback;
                if(p.ValueKind!=JsonValueKind.Number||!p.TryGetSingle(out float x)||!float.IsFinite(x)){invalid?.Invoke();return fallback;}
                return Math.Clamp(x,min,max);
            }
            bool B(string k,bool fallback){if(!root.TryGetProperty(k,out var p))return fallback;if(p.ValueKind is JsonValueKind.True or JsonValueKind.False)return p.GetBoolean();invalid?.Invoke();return fallback;}
            string item=d.Item;
            if(root.TryGetProperty("item",out var ip)){if(ip.ValueKind==JsonValueKind.String&&ip.GetString() is "casing" or "projectile")item=ip.GetString()!;else invalid?.Invoke();}
            return new Settings {Item=item,FollowCaliber=B("follow_caliber",true),Caliber=F("caliber",d.Caliber,5,155),Speed=F("speed",d.Speed,.5f,15),Spread=F("spread",d.Spread,0,30),Spin=F("spin",d.Spin,0,720),Count=(int)MathF.Round(F("count",d.Count,1,8),MidpointRounding.AwayFromZero),Continuous=B("continuous",d.Continuous),Interval=F("interval",d.Interval,.05f,5),Lifetime=F("lifetime",d.Lifetime,2,60),Inherit=B("inherit",d.Inherit),Flash=B("flash",d.Flash),Smoke=B("smoke",d.Smoke)};
        }catch(JsonException){invalid?.Invoke();return d;}
    }
    // IO-API §7 schema. 0.1.0 ships effects=false; flash/smoke join the schema in 0.2.0.
    internal static string Schema(bool effects) {
        var d=Fixed;var fields=new List<object>();
        void N(string key,string label,string type,string? unit,float min,float max,float step,object value,object? showIf=null)=>fields.Add(new {key,label,type,unit,min,max,step,@default=value,showIf});
        void B(string key,string label,bool value)=>fields.Add(new {key,label,type="bool",@default=value});
        fields.Add(new {key="item",label="Item",type="enum",options=new[]{new{value="casing",label="Casing"},new{value="projectile",label="Projectile"}},@default=d.Item});
        B("follow_caliber","Follow gun caliber",true);
        N("caliber","Manual / fallback caliber","float","mm",5,155,1,d.Caliber,new{key="follow_caliber",value=false});
        N("speed","Ejection speed","float","m/s",.5f,15,.1f,d.Speed);
        N("spread","Spread","float","deg",0,30,1,d.Spread);
        N("spin","Spin","float","deg/s",0,720,10,d.Spin);
        N("count","Count","int",null,1,8,1,d.Count);
        B("continuous","Repeat while on",d.Continuous);
        N("interval","Interval","float","s",.05f,5,.05f,d.Interval,new{key="continuous",value=true});
        N("lifetime","Lifetime","float","s",2,60,1,d.Lifetime);
        B("inherit","Inherit vehicle velocity",d.Inherit);
        if(effects){B("flash","Flash",d.Flash);B("smoke","Smoke",d.Smoke);}
        return JsonSerializer.Serialize(fields,new JsonSerializerOptions{DefaultIgnoreCondition=System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull});
    }
}
