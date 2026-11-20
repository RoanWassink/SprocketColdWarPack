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
    private static string? ValidateCopy(VehiclePowertrain powertrain,PowertrainLayout before,PowertrainLayout after)
    {
        AssertReferences(after);
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
                }
            }
        }
        return LayoutValidation.NewProblem(Issues(before),Issues(after));
    }
}
