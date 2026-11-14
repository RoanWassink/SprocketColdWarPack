using Il2CppInterop.Runtime;
using Sprocket.PowertrainLayouts;
using Sprocket.Vehicles;
using Sprocket.Vehicles.Colliders;
using Sprocket.Vehicles.Powertrains.Layouts;
using SprocketEngineExpansion.Core;
namespace SprocketEngineExpansion.Native;

internal static partial class PowerpackLayout
{
    private static Context ReadLayout(VehiclePowertrain powertrain,PowertrainLayout layout,int enginePort)
    {
        var modules=new List<LayoutModule>();var connections=new List<LayoutConnection>();
        for(int i=0;i<layout.powertrainModules.Count;i++)
        {var m=layout.powertrainModules[i];modules.Add(new(m.id,(ModuleKind)(byte)m.moduleType,(short)m.moduleFlags,m.mirror?.id??-1));}
        for(int i=0;i<layout.connections.Count;i++)
        {var c=layout.connections[i];connections.Add(new(c.id,c.provider.module.id,c.receiver.module.id,(byte)c.connectionFlags));}
        int engine=layout.GetPort(enginePort).module.id;
        var plan=PowerpackRules.Find(modules,connections,engine);
        var twin=plan.Status==PowerpackStatus.ViaJunction?PowerpackRules.FindTwin(modules,connections,engine):null;
        return new(powertrain,plan,twin);
    }
    private static void CollapseCopy(PowertrainLayout copy,CollapsePlan plan)
    {
        int maximum=-1;
        for(int i=0;i<copy.powertrainModules.Count;i++)maximum=Math.Max(maximum,copy.powertrainModules[i].id);
        for(int i=0;i<copy.modulePorts.Count;i++)maximum=Math.Max(maximum,copy.modulePorts[i].id);
        for(int i=0;i<copy.connections.Count;i++)maximum=Math.Max(maximum,copy.connections[i].id);
        // Capture surviving port IDs before DestroyModule disconnects the old linked lists.
        var links=plan.Links.Where(l=>l.FirstConnection!=l.LastConnection).Select(l=>(
            Provider:copy.GetConnection(l.FirstConnection).provider.id,Receiver:copy.GetConnection(l.LastConnection).receiver.id)).ToArray();
        foreach(int id in plan.Removed)copy.DestroyModule(copy.GetModule(id));
        copy.DeleteMarked();copy.Reindex();
        copy.nextID=Math.Max(copy.nextID,checked(maximum+1));
        foreach(var link in links)
            copy.CreateConnection(copy.GetPort(link.Provider),copy.GetPort(link.Receiver),null!,0,copy.nextID++);
        if(plan.Splitter>=0)
        {var splitter=copy.GetModule(plan.Splitter);splitter.moduleFlags=(PowertrainModuleFlags)((short)splitter.moduleFlags|0x70);}
        copy.Reindex();copy.RecountSelected();
        AssertReferences(copy);
    }
    private static void AssertReferences(PowertrainLayout layout)
    {
        var modules=new HashSet<int>();var ports=new HashSet<int>();var connections=new HashSet<int>();
        for(int i=0;i<layout.powertrainModules.Count;i++)modules.Add(layout.powertrainModules[i].id);
        for(int i=0;i<layout.modulePorts.Count;i++)ports.Add(layout.modulePorts[i].id);
        for(int i=0;i<layout.connections.Count;i++)connections.Add(layout.connections[i].id);
        for(int i=0;i<layout.powertrainModules.Count;i++)
        {var m=layout.powertrainModules[i];if(m.mirror!=null&&(!modules.Contains(m.mirror.id)||m.mirror.mirror?.id!=m.id))throw new InvalidOperationException("Orphan/asymmetric mirror");}
        for(int i=0;i<layout.modulePorts.Count;i++)
        {var p=layout.modulePorts[i];if(p.module==null||!modules.Contains(p.module.id)||p.next==null||p.previous==null||!ports.Contains(p.next.id)||!ports.Contains(p.previous.id)||p.connection!=null&&!connections.Contains(p.connection.id))throw new InvalidOperationException("Orphan port");}
        for(int i=0;i<layout.connections.Count;i++)
        {var c=layout.connections[i];if(c.provider==null||c.receiver==null||!ports.Contains(c.provider.id)||!ports.Contains(c.receiver.id)||c.provider.connection?.id!=c.id||c.receiver.connection?.id!=c.id)throw new InvalidOperationException("Orphan connection");}
    }
    private sealed record Obstacle(string Key,int Module,OrientedBox Box);
    private static string? ValidateCopy(VehiclePowertrain powertrain,PowertrainLayout before,PowertrainLayout after)
    {
        AssertReferences(after);
        var scale=powertrain.transform.lossyScale;
        if(Math.Abs(scale.x-scale.y)>1e-5||Math.Abs(scale.x-scale.z)>1e-5||scale.x<=0)return "Preflight cannot certify a non-uniform or inverted layout scale";
        var obstacles=new List<Obstacle>();
        var items=powertrain.Vehicle.ObjectReader.Items;
        int count=items.Cast<Il2CppSystem.Collections.Generic.IReadOnlyCollection<VehicleObject>>().Count;
        for(int i=0;i<count;i++)
        {
            var components=items[i].Components;
            int n=components.Cast<Il2CppSystem.Collections.Generic.IReadOnlyCollection<VehicleComponent>>().Count;
            for(int j=0;j<n;j++)
            {
                var component=components[j];int module=-1;
                // Use the part's powertrain node to exclude only a shaft's own endpoint parts.
                for(int k=0;k<n;k++)if(components[k].TryCast<PowertrainComponent>() is {} node&&node.layout?.Pointer==powertrain.Pointer)module=node.powertrainModuleID;
                var colliders=component.Colliders;
                int nc=colliders.Cast<Il2CppSystem.Collections.Generic.IReadOnlyCollection<IVehicleCollider>>().Count;
                for(int k=0;k<nc;k++)
                {
                    var c=colliders[k].TryCast<VehicleCollider>();
                    if(c==null||((int)c.Type&8)==0||c.Transform==null||c.Released)continue;
                    var bounds=c.LocalBounds;
                    if(ToVec(bounds.size).Length<1e-9)continue;
                    var box=FrameOf(powertrain.transform).ToLocal(FrameOf(c.Transform).ToWorld(new(ToVec(bounds.center),ToVec(bounds.size),new(0,0,0,1))));
                    obstacles.Add(new(c.Pointer.ToString(),module,box));
                }
            }
        }
        OrientedBox At(Obstacle o,PowertrainLayout layout)
        {
            if(o.Module<0)return o.Box;
            var original=before.GetModule(o.Module);var updated=layout.GetModule(o.Module);
            if(original==null||updated==null)return o.Box;
            var relative=ToQuat(updated.rotation)*ToQuat(original.rotation).Inverse;
            return new(ToVec(updated.position)+relative.Rotate(o.Box.Centre-ToVec(original.position)),o.Box.Size,relative*o.Box.Rotation);
        }
        IEnumerable<LayoutIssue> Issues(PowertrainLayout layout)
        {
            for(int i=0;i<layout.connections.Count;i++)
            {
                var c=layout.connections[i];var a=c.provider;var b=c.receiver;
                var delta=ToVec(b.position-a.position);double length=delta.Length;
                if(!double.IsFinite(length)){yield return new($"{c.id}:finite","Non-finite connection geometry");continue;}
                if(((byte)c.connectionFlags&1)!=0)
                {
                    if(length>1)yield return new($"{c.id}:long",$"TooLong on abstracted connection {c.id}");
                    if(c.diameter>.4)yield return new($"{c.id}:thick",$"TooThick on abstracted connection {c.id}");
                    continue;
                }
                if(length<.1)yield return new($"{c.id}:short",$"Shaft {c.id} shorter than 0.1 m");
                if(LayoutValidation.Angle(ToVec(a.direction),delta)>DrivetrainRules.Angle(a.minConnectionAngle,DrivetrainAdapter.AngleMultiplier())+.001||
                   LayoutValidation.Angle(ToVec(b.direction),delta*(-1))>DrivetrainRules.Angle(b.minConnectionAngle,DrivetrainAdapter.AngleMultiplier())+.001)
                    yield return new($"{c.id}:angle",$"New shaft angle at connection {c.id}");
                var envelope=LayoutValidation.Envelope(ToVec(a.position),ToVec(b.position),c.diameter*.5);
                foreach(var o in obstacles)
                    if(o.Module!=a.module.id&&o.Module!=b.module.id&&envelope.Intersects(At(o,layout)))
                        yield return new($"{c.id}:obstruction:{o.Key}",$"Possible downstream obstruction at shaft {c.id}; conservative bounds preflight");
            }
            for(int i=0;i<obstacles.Count;i++)for(int j=i+1;j<obstacles.Count;j++)
            {
                var a=obstacles[i];var b=obstacles[j];
                if(a.Module==b.Module||a.Module<0&&b.Module<0)continue;
                if(At(a,layout).Intersects(At(b,layout)))yield return new($"box:{a.Key}:{b.Key}","Possible module obstruction after placement; conservative bounds preflight");
            }
        }
        return LayoutValidation.NewProblem(Issues(before),Issues(after));
    }
}
