using SprocketEmitter;
using System.Text.Json;

internal static class FollowCaliberTests
{
    internal static void Run(Action<bool,string> check) {
        bool Near(float a,float b)=>MathF.Abs(a-b)<1e-5f;
        GunCandidate? Pick(params GunCandidate[] guns) {
            GunCandidate? best=null;
            foreach(var gun in guns)if(gun.Beats(best))best=gun;
            return best;
        }
        var small=new GunCandidate(30,1,true,false);
        var large=new GunCandidate(125,9,true,false);
        var tied=large with {Vuid=2};
        foreach(var guns in new[]{new[]{small,large,tied},new[]{tied,small,large},new[]{large,tied,small}})
            check(Pick(guns)==tied,"largest caliber and lowest VUID independent of iteration order");
        check(Pick(small,large with {Installed=false})==small,"uninstalled cannon ignored");
        check(Pick(small,large with {Released=true})==small,"released cannon ignored");
        check(Pick(small,large with {CaliberMm=0})==small,"non-positive blueprint caliber ignored");
        check(Pick() is null,"empty candidate list has no winner");
        var good=GunSize.Validate(large,true,125,900);
        check(good.Valid&&good.Vuid==9,"consistent shell source accepted");
        check(!GunSize.Validate(large,true,105,900).Valid,"Diameter unequal to Caliber rejected");
        check(!GunSize.Validate(large,false,0,0).Valid,"empty ShellBlueprint rejected");
        check(!GunSize.Validate(large,true,125,0).Valid,"zero propellant rejected");
        check(!GunSize.Validate(large,true,0,900).Valid,"zero diameter rejected");
        var winner=Pick(small,large)!.Value;
        var emptyLargest=GunSize.Validate(winner,false,0,0);
        check(!emptyLargest.Valid&&winner==large,"empty largest source never promotes smaller cannon");
        var settings=Settings.Fixed with {Caliber=76};
        foreach(var invalid in new[]{emptyLargest,GunSize.Invalid("no cannon"),GunSize.Validate(large,true,105,900)}) {
            var fallback=EjectionSize.Resolve(settings,invalid,6.08f);
            check(fallback.Source=="fallback"&&Near(fallback.Diameter,.076f)&&Near(fallback.Length,.076f*6.08f)&&fallback.PropellantMm==0,
                "invalid source uses preserved manual caliber and casing factor");
        }
        var followed=EjectionSize.Resolve(settings,good,19);
        check(followed.Source=="cannon:9"&&Near(followed.Diameter,.125f)&&Near(followed.Length,.9f)&&followed.PropellantMm==900,
            "automatic casing ignores manual factor and manual caliber");
        check(settings.Caliber==76,"following leaves saved manual caliber unchanged");
        check(EjectionSize.Resolve(settings,default,6.08f).Source=="fallback","subsequent empty read cannot retain last good value");
        var manual=EjectionSize.Resolve(settings with {FollowCaliber=false},good,4);
        check(manual.Source=="manual"&&Near(manual.Diameter,.076f)&&Near(manual.Length,.304f),"follow disabled uses manual caliber and factor");
        var projectile=EjectionSize.Resolve(settings with {Item="projectile"},good,19);
        check(Near(projectile.Length,.375f),"automatic projectile length is exactly three diameters");
        check(Near(EjectionSize.Resolve(settings with {Item="projectile"},default,4).Length,.304f),"fallback projectile keeps its configured length factor");
        foreach(int diameter in new[]{125,105,88,76,30})foreach(int propellant in new[]{100,700,900,1500}) {
            var source=GunSize.Validate(new(diameter,3,true,false),true,diameter,propellant);
            var size=EjectionSize.Resolve(settings,source,20);
            var scale=size.Scale(.1f,.1f,.7f,2);
            check(Near(scale.X,diameter/100f)&&Near(scale.Y,diameter/100f)&&Near(scale.Z,propellant/700f),
                $"stock casing scale D/100, P/700: D={diameter} P={propellant}");
            var p=EjectionSize.Resolve(settings with {Item="projectile"},source,.1f).Scale(.1f,.1f,.3f,2);
            check(Near(p.X,diameter/100f)&&Near(p.Y,diameter/100f)&&Near(p.Z,diameter/100f),
                $"stock projectile scale D/100: D={diameter} P={propellant}");
        }
        check(Near(followed.Scale(.7f,.1f,.1f,0).X,.9f/.7f)&&Near(followed.Scale(.1f,.7f,.1f,1).Y,.9f/.7f),
            "bounds route preserves alternate longest axes");
        foreach(var (diameter,propellant,valid) in new[]{(5,10,true),(200,2000,true),(4,10,false),(201,700,false),(125,9,false),(125,2001,false)}) {
            var source=GunSize.Validate(new(diameter,1,true,false),true,diameter,propellant);
            check(source.Valid==valid,$"automatic safe range D={diameter} P={propellant}");
            if(valid)check(Near(EjectionSize.Resolve(settings,source,6.08f).Diameter,diameter*.001f),"automatic caliber bypasses manual 155 mm clamp");
        }
        check(Settings.Fixed.FollowCaliber&&new Settings().FollowCaliber,"fixed and new settings default to follow=true");
        var old=Settings.Parse("{\"caliber\":88}",Settings.Fixed with {FollowCaliber=false});
        check(old.FollowCaliber&&old.Caliber==88,"old stored settings without follow key default true and retain manual caliber");
        check(Settings.Parse(null,Settings.Fixed with {FollowCaliber=false}).FollowCaliber,"absent part data defaults follow=true");
        check(!Settings.Parse("{\"follow_caliber\":false,\"caliber\":105}",Settings.Fixed).FollowCaliber,"explicit false preserved");
        check(Settings.Parse("{\"follow_caliber\":\"false\"}",Settings.Fixed).FollowCaliber,"invalid bool falls back to fixed true");
        foreach(bool effects in new[]{false,true}) {
            var schema=Settings.Schema(effects);using var doc=JsonDocument.Parse(schema);
            var fields=doc.RootElement.EnumerateArray().ToArray();
            int follow=Array.FindIndex(fields,f=>f.GetProperty("key").GetString()=="follow_caliber");
            int caliber=Array.FindIndex(fields,f=>f.GetProperty("key").GetString()=="caliber");
            var f=fields[follow];var c=fields[caliber];
            check(fields.Length==(effects?13:11)&&System.Text.Encoding.UTF8.GetByteCount(schema)<=4096,"schema exact 11/13 fields and at most 4096 UTF-8 bytes");
            check(follow+1==caliber&&f.GetProperty("default").GetBoolean()&&f.GetProperty("label").GetString()=="Follow gun caliber", "follow schema fixed true before manual caliber");
            check(c.GetProperty("label").GetString()=="Manual / fallback caliber"&&!c.GetProperty("showIf").GetProperty("value").GetBoolean()&&
                c.GetProperty("showIf").GetProperty("key").GetString()=="follow_caliber"&&c.GetProperty("default").GetSingle()==30&&
                c.GetProperty("min").GetSingle()==5&&c.GetProperty("max").GetSingle()==155&&c.GetProperty("step").GetSingle()==1,"manual schema visibility, default and bounds preserved");
        }
    }
}
