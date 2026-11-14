using System.Text.Json.Nodes;
namespace SprocketExpandedBallistics;
internal static class TopAttackStockMigration
{
    // Only the recognized old guidance tuple. Payload, precursor and every other
    // setting survive. Custom guidance is deliberately not upgraded.
    internal static bool Apply(JsonObject entry)
    {
        bool Is(string key,string value)=>entry[key]?.ToString()==value;
        bool IsNumber(string key,double value)=>entry[key] is JsonValue n&&!n.TryGetValue<string>(out _)&&double.TryParse(n.ToJsonString(),System.Globalization.NumberStyles.Float,System.Globalization.CultureInfo.InvariantCulture,out var actual)&&actual==value;
        if(entry["flight"]!=null)return false; // A customized modular flight owns its own guidance.
        if(!Is("id","atgm_topattack_test")||!Is("guidanceMode","topattack"))return false;
        foreach(var pair in new Dictionary<string,double>{{"flightSpeed",200},{"maxTurnRate",90},{"guidanceDelay",.25},{"maximumFlightTime",25},{"launchSpeedMultiplier",1},{"launchSpeed",50},{"acceleration",150},{"motorDelay",.12},{"motorBurnTime",4},{"coastDeceleration",1},{"topAttackAltitude",60},{"topAttackClimbAngle",25},{"topAttackDiveAngle",55}})
            if(!IsNumber(pair.Key,pair.Value))return false;
        if(!Is("launchSpeedMode","fixed"))return false;
        entry["topAttackAltitude"]=90;entry["topAttackClimbAngle"]=45;entry["topAttackDiveAngle"]=70;
        return true;
    }
}
