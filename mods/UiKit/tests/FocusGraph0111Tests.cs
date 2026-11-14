using SprocketUiKit;
using UnityEngine;

internal static class FocusGraph0111Tests
{
    private sealed class ActionStub
    {
        internal bool Enabled;
        internal int Enables;
        internal ActionStub(bool enabled) { Enabled=enabled; }
    }
    internal static void Run(Action<bool,string> check)
    {
        var a=new object(); var b=new object(); var hud=new object();
        var order=new List<object>{ a,b,hud };
        var focus=new KeyboardFocusState<object>();
        focus.Open(a,true); check(focus.Current==a,"focus: normal panel opens focused");
        focus.Open(hud,false); check(focus.Current==a,"focus: HUD never acquires focus");
        focus.Open(b,true); check(focus.Current==b,"focus: newest normal panel wins");
        focus.Press(a); check(focus.Current==a,"focus: title/content/border press takes focus");
        focus.Press(null); check(focus.Current==null,"focus: press outside all panels releases focus");
        focus.Press(b); focus.Close(b,order,p=>p!=hud);
        check(focus.Current==a,"focus: closing owner hands off to top open normal panel, skipping HUD");
        focus.Close(b,order,p=>p!=hud); check(focus.Current==a,"focus: closing non-owner keeps focus");
        focus.Close(a,order,_=>false); check(focus.Current==null,"focus: closing last normal panel clears focus");
        foreach(var reason in new[]{"pause","scene change","alt-tab","settings","guard exception","unload"})
        { focus.Open(a,true); focus.Clear(); check(focus.Current==null,"focus: cleared for "+reason); }
        check(InputFocusPolicy.Block(true,false,true,true),"focus policy: panel blocks without typing");
        check(!InputFocusPolicy.Block(true,false,false,true),"focus policy: panel config false releases panel-only block");
        check(InputFocusPolicy.Block(false,true,false,true),"focus policy: typing option still works with panel option off");
        check(!InputFocusPolicy.Block(false,true,true,false),"focus policy: typing suspension option false respected");
        check(InputFocusPolicy.Block(true,true,true,false),"focus policy: panel guard independent of typing option");
        check(!InputFocusPolicy.Block(false,false,true,true),"focus policy: no owner requests self-healing restore");

        var owned=new SuspensionSet<ActionStub>();
        var enabled=new ActionStub(true); var disabled=new ActionStub(false); var other=new ActionStub(true);
        Action<ActionStub> disable=static x=>x.Enabled=false;
        Action<ActionStub> enable=static x=>{x.Enabled=true; x.Enables++;};
        owned.Suspend(1,enabled,enabled.Enabled,disable); owned.Suspend(2,disabled,disabled.Enabled,disable);
        check(owned.Count==1&&!enabled.Enabled&&!disabled.Enabled,"restore set: records enabled actions only");
        enabled.Enabled=true; owned.Suspend(1,enabled,true,disable);
        check(owned.Count==1&&!enabled.Enabled,"restore set: re-enabled game action disabled again without duplicate ownership");
        if(!InputFocusPolicy.Block(false,false,true,true))owned.Restore(enable);
        check(owned.Count==0&&enabled.Enables==1&&disabled.Enables==0&&other.Enables==0,"restore set: self-heal restores only own actions in same tick");
        try { owned.Suspend(3,other,true,static _=>throw new Exception("disable fault")); }
        catch { owned.Restore(enable); }
        check(owned.Count==0&&other.Enables==1,"restore set: Disable exception still restores recorded action");
        owned.Suspend(1,enabled,true,disable); owned.Suspend(3,other,true,disable);
        var error=owned.Restore(x=>{if(x==enabled)throw new Exception("enable fault");enable(x);});
        check(error!=null&&owned.Count==1&&other.Enabled,"restore set: one restore exception does not skip other actions");
        owned.Restore(enable); check(owned.Count==0&&enabled.Enabled,"restore set: failed restore retried until successful");
        var throttle=new RescanThrottle();
        check(throttle.Due(true,0),"rescan: immediate at first focus");
        bool premature=false; for(int frame=1;frame<60;frame++)premature|=throttle.Due(true,frame/60f);
        check(!premature&&throttle.Due(true,1),"rescan: maximum once per second");
        check(!throttle.Due(true,1.5f)&&throttle.Due(true,2),"rescan: panel-to-text or panel handoff does not bypass throttle");
        throttle.Due(false,2.1f); check(throttle.Due(true,2.11f),"rescan: release and new focus starts immediate scan");
        for(int i=0;i<2000;i++){ throttle.Due(true,2.2f); owned.Restore(enable); }
        long before=GC.GetAllocatedBytesForCurrentThread();
        for(int i=0;i<2000;i++){ throttle.Due(true,2.2f); owned.Restore(enable); InputFocusPolicy.Block(true,false,true,true); }
        long bytes=GC.GetAllocatedBytesForCurrentThread()-before;
        Console.WriteLine($"INFO focus guard steady throttle/self-heal: {bytes} bytes / 2000 ticks");
        check(bytes==0,"focus guard: zero steady per-frame allocations");

        var labels=new List<string>{"One","Two"}; var cache=new TabLabelCache();
        check(cache.Poll(labels)&&cache.Labels.Count==2,"tabs: initial label snapshot");
        check(!cache.Poll(new[]{"One","Two"}),"tabs: equal new list keeps cache");
        labels[1]="Renamed"; check(cache.Poll(labels)&&cache.Labels[1]=="Renamed","tabs: same-instance rename updates text");
        labels.Add("Three"); check(cache.Poll(labels)&&cache.Labels.Count==3,"tabs: growing count rebuilds snapshot");
        labels.RemoveAt(0); check(cache.Poll(labels)&&cache.Labels.Count==2&&cache.Labels[0]=="Renamed","tabs: shrinking count updates snapshot");
        for(int i=0;i<2000;i++)cache.Poll(labels);
        before=GC.GetAllocatedBytesForCurrentThread();for(int i=0;i<2000;i++)cache.Poll(labels);
        bytes=GC.GetAllocatedBytesForCurrentThread()-before;
        Console.WriteLine($"INFO tabs label cache: {bytes} bytes / 2000 polls");check(bytes==0,"tabs: label polling zero bytes");
        check(cache.Poll(Array.Empty<string>())&&cache.Labels.Count==0,"tabs: zero labels supported");

        var source=new GraphHit{Kind=GraphHitKind.Port,NodeId="A",PortId="out",GraphPos=new(176,48)};
        var gesture=new GraphPortGesture<GraphHit>();
        gesture.Begin(default,source,true);gesture.Move(new(4,0),1);
        check(!gesture.End(),"port gesture: exactly four physical px remains a click");
        gesture.Begin(default,source,true);gesture.Move(new(3.01f,0),1.333f);
        check(gesture.Dragging,"port gesture: scale-adjusted movement starts drag above four px");
        gesture.Move(default,1.333f); check(gesture.End(),"port gesture: return to start retains drag once threshold crossed");
        var dropHit=new GraphHit{Kind=GraphHitKind.Port,NodeId="B",PortId="in",GraphPos=new(300,48),Button=0,Shift=true,Ctrl=true};
        var drop=new GraphPortDrop(gesture.Source.NodeId!,gesture.Source.PortId!,true,dropHit);
        check(drop.FromNode=="A"&&drop.FromPort=="out"&&drop.FromOutput&&drop.Drop.NodeId=="B"&&drop.Drop.PortId=="in"&&drop.Drop.Shift&&drop.Drop.Ctrl&&drop.Drop.GraphPos.x==300,"GraphPortDrop: exact source, direction and complete release hit");
        foreach(var kind in new[]{GraphHitKind.Canvas,GraphHitKind.Node,GraphHitKind.FooterButton,GraphHitKind.Wire})
        { var d=new GraphPortDrop("B","in",false,new GraphHit{Kind=kind,GraphPos=new(-50,20)});check(!d.FromOutput&&d.Drop.Kind==kind&&d.Drop.GraphPos.x==-50,"GraphPortDrop: input-origin release onto "+kind); }
        gesture.Begin(default,source,false);gesture.Move(new(50,0),1);check(!gesture.End(),"port gesture: non-port press never emits port drop");
        gesture.Begin(default,source,true);gesture.Move(new(50,0),1);gesture.Cancel();check(!gesture.End(),"port gesture: cancellation removes drag");
        check(GraphMeshLayout.WireSlots(600,true,true)==509,"port drag: independent pending and drag wires reserve two mesh slots");

        var nodes=new List<GraphNodeBounds>{new(new(0,0,176,90),1,1,true),new(new(-20,32,176,90),0,0,false)};
        var wires=Array.Empty<GraphStrip>();
        check(GraphPicking.Hit(nodes,wires,new(0,48),1).Kind==GraphPickKind.Node,"z-order: higher body blocks lower input port");
        check(GraphPicking.Hit(nodes,wires,new(140,75),1).Kind==GraphPickKind.Node,"z-order: higher body blocks lower footer");
        check(GraphPicking.Hit(nodes,wires,new(179,48),1).Kind==GraphPickKind.Port,"z-order: uncovered protruding lower port still works");
        nodes.RemoveAt(1);
        check(GraphPicking.Hit(nodes,wires,new(-8,48),1).Kind==GraphPickKind.Canvas&&GraphPicking.Hit(nodes,wires,new(-8,48),1,12).Kind==GraphPickKind.Port,"port radius: default 7 versus configured 12 physical px");
        check(GraphPicking.Hit(nodes,wires,new(-6,48),2,12).Kind==GraphPickKind.Port&&GraphPicking.Hit(nodes,wires,new(-6.01f,48),2,12).Kind==GraphPickKind.Canvas,"port radius: physical size independent of scale and zoom");
        check(GraphMath.PortRadius(0)==0&&GraphMath.PortRadius(12)==12,"port radius: zero and finite positive accepted");
        foreach(var invalid in new[]{-1f,float.NaN,float.PositiveInfinity})
        { bool threw=false;try{GraphMath.PortRadius(invalid);}catch(ArgumentOutOfRangeException){threw=true;}check(threw,"port radius: reject "+invalid); }
        foreach(float zoom in new[]{.5f,1f,1.5f})
        {
            var view=GraphMath.Visible(new(20,-30),zoom,800,600);
            var top=GraphMath.Local(new(view.X,view.Y),new(20,-30),zoom);
            var bottom=GraphMath.Local(new(view.Right,view.Bottom),new(20,-30),zoom);
            check(MathF.Abs(top.X)<.001&&MathF.Abs(top.Y)<.001&&MathF.Abs(bottom.X-800)<.001&&MathF.Abs(bottom.Y-600)<.001,"ViewSize/VisibleRect: layout extent and graph-space inverse at zoom "+zoom);
        }
        Reproduction(check);
    }
    private static void Reproduction(Action<bool,string> check)
    {
        // Exact 0.9.1 placements and port counts from mast tests/KitRepro and Io/FlowModel.cs.
        var nodes=new List<GraphNodeBounds>();
        int Inputs(string type)=>type is "and" or "or" or "counter" or "memory"?2:type is "not" or "delay" or "pulse" or "hinge" or "effects.light"?1:0;
        void Add(string type,float x,float y)
        { int inputs=Inputs(type),outputs=type is "hinge" or "effects.light"?0:1;nodes.Add(new(new(x,y,176,GraphMath.Height(inputs,outputs)),inputs,outputs,false)); }
        void Template(params string[] types){for(int i=0;i<types.Length;i++)Add(types[i],32+i*216,32);}
        Template("cannon.reloading","hinge");Template("cannon.fired","delay","pulse","hinge");Template("vehicle.braking","effects.light");
        foreach(var type in new[]{"and","or","not","delay","pulse","counter","memory","and","or","not","delay","pulse","counter","memory","and","or"})
        {int o=nodes.Count%6;Add(type,32+o*24,32+o*32);}
        int ports=0,overlaps=0,hidden=0,oldHidden=0;
        for(int i=0;i<nodes.Count;i++)for(int j=i+1;j<nodes.Count;j++){var r=nodes[i].Body.Intersect(nodes[j].Body);if(r.W>0&&r.H>0)overlaps++;}
        for(int i=0;i<nodes.Count;i++)for(int side=0;side<2;side++)for(int k=0;k<(side==0?nodes[i].Inputs:nodes[i].Outputs);k++)
        {
            ports++;bool output=side==1;var p=nodes[i].Port(output,k);
            var pick=GraphPicking.Hit(nodes,Array.Empty<GraphStrip>(),p,1.333f);
            bool covered=false;for(int j=i+1;j<nodes.Count;j++)covered|=nodes[j].Body.Contains(p);
            bool self=pick.Kind==GraphPickKind.Port&&pick.Index==i&&pick.Port==k&&pick.Output==output;
            if(covered&&self)hidden++;
            // Historical global-port picking retained only as the fault oracle.
            for(int j=nodes.Count-1;j>=0;j--)
            {
                int port=nodes[j].HitPort(p,7/1.333f,false);bool outPort=false;
                if(port<0){port=nodes[j].HitPort(p,7/1.333f,true);outPort=true;}
                if(port>=0){if(covered&&j==i&&port==k&&outPort==output)oldHidden++;break;}
            }
        }
        Console.WriteLine($"INFO mast exact stacked reproduction: {nodes.Count} blocks, {ports} ports, {overlaps} overlaps; hidden picks old={oldHidden}, new={hidden}");
        check(nodes.Count==24&&ports==52&&overlaps==129&&oldHidden==6,"mast reproduction: matches original 24/52/129/6 evidence");
        check(hidden==0,"mast reproduction: zero hidden ports win under higher bodies");
    }
}
