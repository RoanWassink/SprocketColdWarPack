namespace SprocketExpandedBallistics;
internal static class AtgmAmmoBoxRules
{
    internal const string Guid="c04c9eec-84d9-543b-8449-4c065f5a0b52";
    internal const double Reach=2;
    internal static string ReferenceLabel(string name,int calibre,int id,string? profile,string? guidance)=>
        $"{name} | {profile??"profile unavailable"} ({(guidance=="keyboard"?"MCLOS":guidance=="sight"?"SACLOS":guidance=="topattack"?"TOP ATTACK":"unavailable")}) | {calibre} mm | ID {id}";
    internal static bool WaivesLoader(bool sameVehicle,bool dedicatedLauncher,int cannonId,int breechCannonId,bool compatibleBox)=>
        sameVehicle&&dedicatedLauncher&&cannonId==breechCannonId&&compatibleBox;
    internal static bool Matches(bool sameVehicle,string boxProfile,string? launcherProfile,int boxCalibre,int launcherCalibre,int boxPropellant,int launcherPropellant,double distance)=>
        sameVehicle&&!string.IsNullOrEmpty(boxProfile)&&boxProfile==launcherProfile&&boxCalibre>0&&boxCalibre==launcherCalibre&&
        boxPropellant==launcherPropellant&&double.IsFinite(distance)&&distance>=0&&distance<=Reach;
    internal static double Seconds(double calibre,double propellant)=>15*Math.Pow(Math.Clamp(calibre/135,.25,3),.65)*Math.Pow(Math.Clamp((calibre*3+propellant)/505,.25,5),.2);
    internal static double MechanismMass(int capacity)=>10+2*Math.Max(0,capacity);
    internal static double MechanismCost(int capacity)=>250+40*Math.Max(0,capacity);
    // 0.22.89 begin
    // User 16-11: the automatic feed is on by default under [ATGM ammo box] AutomaticFeed. The old switch [ATGM Launcher Tests] AutomaticAmmoBox is no longer
    // read; BepInEx keeps an existing line in the user's file, so one log line names it.
    internal const string Section="ATGM ammo box",Key="AutomaticFeed",LegacySection="ATGM Launcher Tests",LegacyKey="AutomaticAmmoBox";
    internal const bool DefaultOn=true;
    /// <summary>The value of the old switch in the text of a BepInEx .cfg file (section [ATGM Launcher Tests], key AutomaticAmmoBox), or null when it has none.</summary>
    internal static string? LegacySwitch(string? cfg)
    {
        if(string.IsNullOrEmpty(cfg))return null;
        string? section=null;
        foreach(var raw in cfg.Split('\n'))
        {
            var line=raw.Trim();
            if(line.Length==0||line[0]=='#'||line[0]==';')continue;
            if(line[0]=='['){section=line.EndsWith(']')?line[1..^1].Trim():null;continue;}
            var eq=line.IndexOf('=');
            if(eq>0&&section==LegacySection&&line[..eq].Trim()==LegacyKey)return line[(eq+1)..].Trim();
        }
        return null;
    }
    internal static string LegacyNote(string value)=>$"[ATGM ammo box] The old setting [{LegacySection}] {LegacyKey} = {value} is no longer read; the automatic feed follows [{Section}] {Key} (default true). The old line may be removed from the file.";
    // 0.22.89 end
}
