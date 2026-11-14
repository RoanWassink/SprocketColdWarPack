using System.Numerics;
using SprocketExpedition.Game;

internal static class SpecialSpawnChecks
{
    internal static void Run()
    {
        static void Check(bool ok,string message){if(!ok)throw new Exception(message);}
        var anchors=new[]{new SpawnArcAnchor(new Vector2(-20,-25),Vector2.UnitY),new SpawnArcAnchor(new Vector2(20,25),Vector2.UnitY)};
        foreach(var pattern in new[]{"Swarm","Line","Split"}) for(var seed=0;seed<32;seed++)
        {
            var count=pattern=="Swarm"?12:pattern=="Line"?3:8;
            Check(CampaignSpecialSpawnPlanner.TryPlan(anchors,count,"special"+seed,pattern,(_,_)=>true,(_,_)=>true,out var points,out var cover,out _),"whole special formation");
            Check(cover,"covered layout preferred");
            Check(CampaignSpecialSpawnPlanner.TryPlan(anchors,count,"special"+seed,pattern,(_,_)=>true,(_,_)=>true,out var retry,out _,out _)&&points.SequenceEqual(retry),"same retry layout");
            foreach(var p in points)foreach(var a in anchors){var delta=p-a.Position;Check(delta.Length() is>=349.99f and<=500.01f&&Vector2.Dot(delta,a.Forward)>=0,"all friendly anchors retain front350-500m");}
            for(var i=0;i<count;i++)for(var j=0;j<i;j++) Check(Vector2.Distance(points[i],points[j])>=23.99f,"safe native marker spacing");
            var diameter=points.Max(p=>points.Max(q=>Vector2.Distance(p,q)));
            if(pattern=="Swarm")Check(diameter<100f,"compact swarm cluster");
            if(pattern=="Line")Check(diameter>=119.99f,"broad line");
            if(pattern=="Split")Check(points.Min(p=>p.X)<-150&&points.Max(p=>p.X)>150,"separate flanks");
        }
        Check(CampaignSpecialSpawnPlanner.TryPlan(anchors,12,"open","Swarm",(_,_)=>true,(_,_)=>false,out _,out var withCover,out _)&&!withCover,"open fallback keeps swarm geometry");
        Check(!CampaignSpecialSpawnPlanner.TryPlan(anchors,12,"blocked","Swarm",(_,_)=>false,(_,_)=>true,out var failed,out _,out _)&&failed.Length==0,"no partial unsafe layout");
        var combinedRoles = new[] { "Center", "Center", "Center", "Flank", "Flank", "Support" };
        for (var seed = 0; seed < 24; seed++)
        {
            Check(CampaignSpecialSpawnPlanner.TryPlan(anchors, 6, "combined-" + seed, "CombinedArms",
                (_, _) => true, (_, _) => false, out var combined, out var combinedCover,
                out var combinedReport, range: new CampaignSpawnRange(350, 500), deploymentRoles: combinedRoles)
                && !combinedCover, "combined-arms roles produce a complete open formation: " + combinedReport);
            for (var i = 0; i < combined.Length; i++)
                for (var j = 0; j < i; j++)
                    Check(Vector2.Distance(combined[i], combined[j]) >= 23.99f,
                        "combined-arms centre/flank/support spacing never overlaps");
            Check(CampaignSpecialSpawnPlanner.TryPlan(anchors, 6, "combined-" + seed, "CombinedArms",
                (_, _) => true, (_, _) => false, out var retry, out _, out _, deploymentRoles: combinedRoles)
                && combined.SequenceEqual(retry), "combined-arms retry is deterministic");
        }
        foreach(var name in new[]{"75mm pak 40","QF 3.7-inch AA gun","37mm Gun M3A1","75mm Pack Howitzer M1","Bofors 8cm Model 1929","Canone d'infantrie de 37 modèle 1916","8.5 - 8.8 cm Flak M39 (r)"})Check(CampaignTankNamePolicy.IsStationaryGun(name),"exclude stationary "+name);
        foreach(var name in new[]{"Churchill Mk.III Gun Carrier","75mm Self-Propelled gun Badger","GST.KW 15.5cm Haubitzen AU12K","Panzerkampfwagen VI Tiger Ausf. H1","Gunfighter","MK18 Man o' War Gun Carriage","105 leFH18B2","105mm LeFH 18 RZ 25 (f)"})Check(!CampaignTankNamePolicy.IsStationaryGun(name),"retain mobile "+name);
        Console.WriteLine("PASS special swarm/line/split geometry, range, cover/fallback, deterministic retry and conservative stationary-gun name filter");
    }
}
