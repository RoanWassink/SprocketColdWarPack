namespace SprocketEngineExpansion.Core;

public sealed record CollapseLink(int FirstConnection,int LastConnection);
public sealed record CollapsePlan(PowerpackStatus Status,string Reason,int[] Removed,CollapseLink[] Links,int Splitter=-1,double Distance=0)
{
    public bool Needed=>Removed.Length>0;
}
/// <summary>Read-only graph recognition; never walks beyond the first transmissions.</summary>
public static class LayoutCollapse
{
    public const double DefaultMaxDistance=1.75;
    public static CollapsePlan Find(IReadOnlyList<LayoutModule> modules,IReadOnlyList<LayoutConnection> connections,
        int engineId,Func<int,Vec3> receiverPosition,Vec3 engineOutput,Vec3 engineDirection,double maxDistance=DefaultMaxDistance,Vec3? vehicleForward=null)
    {
        CollapsePlan Fail(string reason,PowerpackStatus status=PowerpackStatus.Conflict,double distance=0)=>new(status,reason,Array.Empty<int>(),Array.Empty<CollapseLink>(),-1,distance);
        // Existing direct/exact Twin layouts retain 0.4.1 eligibility, regardless of distance.
        var legacy=PowerpackRules.Find(modules,connections,engineId);
        var legacyTwin=PowerpackRules.FindTwin(modules,connections,engineId);
        if(legacy.Status==PowerpackStatus.Ready||legacyTwin.Status==PowerpackStatus.Ready)
        {
            var ids=legacy.Status==PowerpackStatus.Ready?new[]{legacy.ConnectionId}:new[]{legacyTwin.LeftConnectionId,legacyTwin.RightConnectionId};
            double legacyDistance=ids.Max(id=>(receiverPosition(id)-engineOutput).Length);
            return new(PowerpackStatus.Ready,"Ready: retained 0.4.1 direct/exact Twin; no distance gate",Array.Empty<int>(),Array.Empty<CollapseLink>(),legacyTwin.Status==PowerpackStatus.Ready?legacyTwin.JunctionId:-1,legacyDistance);
        }
        if(!double.IsFinite(maxDistance)||maxDistance<=0)return Fail("Conflict: invalid maximum collapse distance.");
        var map=modules.ToDictionary(m=>m.Id);var removed=new HashSet<int>();var visited=new HashSet<int>();
        var links=new List<CollapseLink>();var transmissions=new List<LayoutModule>();int splitter=-1;
        var start=connections.Where(c=>c.ProviderModule==engineId).ToArray();
        if(start.Length!=1)return Fail("Branch: the engine must have exactly one connected output.");
        string? error=null;
        void Walk(LayoutConnection first)
        {
            var edge=first;
            while(true)
            {
                if(!map.TryGetValue(edge.ReceiverModule,out var m)||!visited.Add(m.Id)){error="Conflict: dangling reference or cycle.";return;}
                var inputs=connections.Where(c=>c.ReceiverModule==m.Id).ToArray();
                if(inputs.Length!=1){error="Branch: a module has multiple inputs.";return;}
                if(m.Kind==ModuleKind.Transmission){transmissions.Add(m);links.Add(new(first.Id,edge.Id));return;}
                if(m.Kind!=ModuleKind.Junction){error="Branch: the engine route does not end at a gearbox.";return;}
                var outputs=connections.Where(c=>c.ProviderModule==m.Id).ToArray();
                if(outputs.Length==1)
                {
                    if((edge.Flags&1)!=0){error="Conflict: the route already has abstracted connections.";return;}
                    removed.Add(m.Id);edge=outputs[0];continue;
                }
                if(outputs.Length!=2||splitter!=-1){error="Branch: only one two-way splitter is supported.";return;}
                if((m.Flags&~(0x70|6))!=0){error="Conflict: the splitter has incompatible positioning flags.";return;}
                splitter=m.Id;links.Add(new(first.Id,edge.Id));
                foreach(var output in outputs){Walk(output);if(error!=null)return;}
                return;
            }
        }
        Walk(start[0]);if(error!=null)return Fail(error);
        if(transmissions.Count!=1&&transmissions.Count!=2)return Fail("Branch: expected one gearbox or a twin pair.");
        if(transmissions.Count==2&&(splitter<0||transmissions[0].Id==transmissions[1].Id))return Fail("Branch: expected two distinct gearboxes after the splitter.");
        foreach(int id in removed)
            if(map[id].MirrorId>=0&&(!removed.Contains(map[id].MirrorId)||map[map[id].MirrorId].MirrorId!=id))return Fail("Conflict: mirrored junctions must be removed together.");
        double distance=0;bool forward=false;
        foreach(var t in transmissions)
        {
            var c=connections.Single(c=>c.ReceiverModule==t.Id);var delta=receiverPosition(c.Id)-engineOutput;
            distance=Math.Max(distance,delta.Length);forward|=Vec3.Dot(delta,(vehicleForward??new Vec3(0,0,1)).Normalized)>maxDistance;
        }
        // Vehicle forward is converted to layout space by the adapter (identity defaults to +Z).
        // This gate applies ONLY to newly collapsed chains, never legacy direct/exact Twin.
        if(distance>maxDistance)return Fail(forward?"Front drive: gearbox is ahead of the rear engine.":"Too far from engine: gearbox input exceeds the collapse distance.",PowerpackStatus.Conflict,distance);
        // Reuse the 0.4.1 recogniser after projecting the collapsed graph, including its exact flag checks.
        var projected=connections.Where(c=>!removed.Contains(c.ProviderModule)&&!removed.Contains(c.ReceiverModule)).ToList();
        int next=connections.Max(c=>c.Id)+1;
        foreach(var link in links.Where(l=>l.FirstConnection!=l.LastConnection))
        {var a=connections.Single(c=>c.Id==link.FirstConnection);var b=connections.Single(c=>c.Id==link.LastConnection);projected.Add(new(next++,a.ProviderModule,b.ReceiverModule,0));}
        var projectedModules=modules.Where(m=>!removed.Contains(m.Id)).Select(m=>m.Id==splitter?m with {Flags=(short)(m.Flags|0x70)}:m).ToArray();
        var direct=PowerpackRules.Find(projectedModules,projected,engineId);
        var status=splitter<0?direct.Status:PowerpackRules.FindTwin(projectedModules,projected,engineId).Status;
        if(status!=PowerpackStatus.Ready)return Fail("Conflict: existing positioning or abstracted flags cannot be restored exactly.");
        return new(status,"Ready",removed.OrderBy(x=>x).ToArray(),links.ToArray(),splitter,distance);
    }
}

public sealed record LayoutIssue(string Key,string Reason);
public static class LayoutValidation
{
    public static string? NewProblem(IEnumerable<LayoutIssue> before,IEnumerable<LayoutIssue> after)
    {var known=before.Select(x=>x.Key).ToHashSet();return after.FirstOrDefault(x=>!known.Contains(x.Key))?.Reason;}
    public static OrientedBox Envelope(Vec3 a,Vec3 b,double radius)=>new((a+b)*.5,
        new(Math.Abs(a.X-b.X)+radius*2,Math.Abs(a.Y-b.Y)+radius*2,Math.Abs(a.Z-b.Z)+radius*2),new(0,0,0,1));
    public static double Angle(Vec3 a,Vec3 b)=>Math.Acos(Math.Clamp(Vec3.Dot(a.Normalized,b.Normalized),-1,1))*180/Math.PI;
}
