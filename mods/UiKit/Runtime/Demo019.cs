using UnityEngine;
namespace SprocketUiKit;

internal static class Demo019
{
    internal static readonly string[] Tabs={ "Graph" };
    private static NodeGraph graph=null!;
    private static readonly List<GraphPort> signals=new();
    private static readonly List<GraphWire> links=new();
    private static string? pendingNode,pendingPort;
    private static string status="Click an output, then an input. Drag blocks or empty canvas; wheel zooms.";
    private static int serial;
    private static bool stress,isolated,mastPattern;
    private static float nextSignal,nextReading,elapsed;
    private static int frames;
    private static string reading="Average frame: waiting...";
    internal static void Build(Panel panel,Func<int> tab)
    {
        var columns=panel.Columns(172,0,200);
        columns[0].Parent!.VisibleWhen(()=>tab()==Demo017.FirstTab+1);
        var tools=columns[0].Section("Graph");
        tools.Button("Mast pattern",()=>MastPattern());
        tools.Button("Example",()=>Example()); tools.Button("Stress",()=>Stress()); tools.Button("Fit",()=>graph.FitAll());
        tools.Button("Zoom +",()=>graph.SetView(graph.Pan,graph.Zoom*1.15f)); tools.Button("Zoom -",()=>graph.SetView(graph.Pan,graph.Zoom/1.15f));
        tools.Button("Compare Canvas",()=> { isolated=!isolated; graph.BatchIsolation(isolated); frames=0;elapsed=0; });
        tools.Label(()=>isolated?"Canvas: isolated world":"Canvas: shared (default)",TextStyle.Small).Wrap();
        tools.Label(()=>reading,TextStyle.Small).Wrap();
        tools.Label("Compare the same stress scene while dragging. Average includes the whole game frame, including native UI work.",TextStyle.Faint).Wrap();
        graph=columns[1].NodeGraph().Fill().Snap(8).Hint(()=>pendingNode==null?null:"Now click an input port...");
        graph.OnClick(Click).OnPortDrag(PortDrag).OnNodeMoved((id,pos,final)=> { if(final)status="Moved "+id+" to "+pos.x.ToString("0")+", "+pos.y.ToString("0"); });
        columns[2].Section("Inspector").Label(()=>status,TextStyle.Small).Wrap();
        // A retained helper ticks only while this tab is visible; it never polls input.
        columns[2].Add(new GraphTicker()).Height(0);
        Example();
    }
    private static void Reset()
    { graph.Clear(); signals.Clear(); links.Clear(); pendingNode=pendingPort=null; serial=0; stress=mastPattern=false; }
    private static GraphNode Add(string id,float x,float y,string title,string tag,Color colour,int inputs,int outputs)
    {
        var n=graph.AddNode(id,x,y,title,tag,colour);
        for(int i=0;i<inputs;i++)signals.Add(n.AddInput("i"+i,inputs==1?"Signal":"Input "+(i+1)));
        for(int i=0;i<outputs;i++)signals.Add(n.AddOutput("o"+i,"Signal"));
        n.FooterText="Ready"; return n;
    }
    private static void Link(string a,string b,int input=0) => links.Add(graph.AddWire("w"+serial++,a,"o0",b,"i"+input));
    private static void Example()
    {
        Reset();
        var brake=Add("brake",24,80,"Brake pedal","IN",GraphColours.In,0,1); brake.FooterText="Released"; brake.FooterButton="Hold";
        Add("and",256,64,"AND","LOGIC",GraphColours.Logic,2,1).FooterText="Both inputs";
        Add("delay",256,252,"Delay","LOGIC",GraphColours.Logic,1,1).FooterText="0.5 s";
        Add("hinge",496,100,"Hinge open","OUT",GraphColours.Out,1,0).FooterText="Closed";
        Link("brake","and"); Link("and","delay"); Link("delay","hinge"); graph.FitAll();
        status="Click an output, then an input. Select a wire by clicking its curve.";
    }
    private static void MastPattern()
    {
        Example(); mastPattern=true;
        graph.Node("hinge")!.Tag="LONG OUTPUT TAG";
        pendingNode="brake"; pendingPort="o0"; graph.BeginPendingWire(pendingNode,pendingPort);
        status="Mast pattern: wires and pending restart at 10 Hz. Move the mouse, pan and drag; check for trails/fans.";
    }
    private static void Stress()
    {
        Reset(); stress=true;
        for(int i=0;i<60;i++)Add("n"+i,(i%10)*220,(i/10)*160,"Block "+(i+1),i%3==0?"IN":i%3==1?"LOGIC":"OUT",i%3==0?GraphColours.In:i%3==1?GraphColours.Logic:GraphColours.Out,2,1);
        for(int i=0;i<100;i++)Link("n"+(i%60),"n"+((i*7+1)%60),i/60);
        graph.FitAll(); status="60 blocks / 100 wires. Signals change at 10 Hz. Drag, pan and zoom to compare Canvas modes.";
    }
    private static void PortDrag(GraphPortDrop drop)
    {
        var hit=drop.Drop;
        if(hit.Kind!=GraphHitKind.Port) { status="Drop on a port to connect."; return; }
        var target=graph.Node(hit.NodeId!)!.FindPort(hit.PortId!)!;
        if(target.IsOutput==drop.FromOutput) { status="Connect an output to an input."; return; }
        var fromNode=drop.FromOutput?drop.FromNode:hit.NodeId!;
        var fromPort=drop.FromOutput?drop.FromPort:hit.PortId!;
        var toNode=drop.FromOutput?hit.NodeId!:drop.FromNode;
        var toPort=drop.FromOutput?hit.PortId!:drop.FromPort;
        links.Add(graph.AddWire("w"+serial++,fromNode,fromPort,toNode,toPort));
        status="Connected by dragging: "+fromNode+" -> "+toNode;
    }
    private static void Click(GraphHit hit)
    {
        if(hit.Button!=0)return;
        foreach(var w in links)w.Selected=w.Id==hit.WireId;
        if(hit.Kind==GraphHitKind.Port)
        {
            var node=graph.Node(hit.NodeId!)!; var port=node.FindPort(hit.PortId!)!;
            if(port.IsOutput) { CancelPending(); pendingNode=node.Id; pendingPort=port.Id; port.Chosen=true; graph.BeginPendingWire(node.Id,port.Id); }
            else if(pendingNode!=null) { links.Add(graph.AddWire("w"+serial++,pendingNode,pendingPort!,node.Id,port.Id)); CancelPending(); status="Connected to "+node.Title+" / "+port.Label; }
        }
        else if(hit.Kind==GraphHitKind.FooterButton)
        { var n=graph.Node(hit.NodeId!)!; n.Lamp=!n.Lamp; n.FooterText=n.Lamp?"Held":"Released"; n.FooterButton=n.Lamp?"Release":"Hold"; foreach(var p in n.Outputs)p.On=n.Lamp; }
        else
        {
            CancelPending(); foreach(var n in graph.NodesInternal)n.Selected=n.Id==hit.NodeId;
            status=hit.Kind==GraphHitKind.Wire?"Selected wire "+hit.WireId:hit.Kind==GraphHitKind.Node?"Selected "+graph.Node(hit.NodeId!)!.Title:"Canvas";
        }
    }
    private static void CancelPending()
    { if(pendingNode!=null) { var p=graph.Node(pendingNode)?.FindPort(pendingPort!); if(p!=null)p.Chosen=false; } pendingNode=pendingPort=null; graph.EndPendingWire(); }
    private sealed class GraphTicker : Element
    {
        protected override void Build() { }
        internal override float MeasureHeight(float width)=>0;
        internal override void Refresh(float now)
        {
            frames++; elapsed+=Time.unscaledDeltaTime;
            if(now>=nextReading) { nextReading=now+.5f; reading="Average frame: "+(1000*elapsed/Math.Max(1,frames)).ToString("0.00")+" ms"; elapsed=0; frames=0; }
            if((!stress&&!mastPattern)||now<nextSignal)return; nextSignal=now+.1f;
            if(mastPattern)
            {
                graph.EndPendingWire();
                foreach(var wire in links)graph.RemoveWire(wire.Id);
                links.Clear(); Link("brake","and"); Link("and","delay"); Link("delay","hinge");
                pendingNode="brake"; pendingPort="o0"; graph.BeginPendingWire(pendingNode,pendingPort);
            }
            int tick=(int)(now*10); for(int i=0;i<signals.Count;i+=7)signals[i].On=((tick+i)&1)==0;
            for(int i=0;i<links.Count;i+=5)links[i].On=((tick+i)&1)==0;
        }
    }
}
