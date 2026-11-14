using SprocketUiKit;
using UnityEngine;

// Build once from a plugin's Load(). Register/open the returned panel through the Keybinds API.
// Persistence and flow evaluation belong to the consumer; this sample keeps the handles for live updates.
public sealed class GraphEditor
{
    private readonly NodeGraph graph;
    private readonly Dictionary<string,GraphNode> nodes=new();
    private readonly Dictionary<(string Node,string Port),GraphPort> ports=new();
    private readonly List<GraphWire> wires=new();
    private (string Node,string Port)? pending;
    private GraphWire? selectedWire;
    private GraphNode? selectedNode;
    private int serial;
    public Panel Panel { get; }
    public GraphEditor(string modGuid)
    {
        Panel=UiKit.CreatePanel(modGuid,"flow","I/O sandbox","Example flow editor",1120,650);
        var columns=Panel.Columns(170,0,220);
        var library=columns[0].Section("Library");
        library.Button("Add delay",()=>AddDelay());
        library.Button("Fit",()=>graph!.FitAll());
        graph=columns[1].NodeGraph().Fill().Snap(8).ZoomRange(.5f,1.5f)
            .Hint(()=>pending.HasValue?"Now click an input port...":null);
        var inspector=columns[2].Section("Inspector");
        inspector.Label(()=>selectedNode?.Title??(selectedWire!=null?"Wire":"Click a block or wire"));
        inspector.TextField(()=>selectedNode?.Title??"",text=> { if(selectedNode!=null)selectedNode.Title=text; });
        inspector.Button("Delete",()=>DeleteSelection());
        var brake=Add("brake",20,100,"Brake pedal","IN",GraphColours.In); Port(brake,"signal","Signal",true); brake.FooterButton="Hold";
        var and=Add("and",250,70,"AND","LOGIC",GraphColours.Logic); Port(and,"a","A",false);Port(and,"b","B",false);Port(and,"result","Result",true);
        var delay=Add("delay",250,260,"Delay","LOGIC",GraphColours.Logic);Port(delay,"in","Signal",false);Port(delay,"out","Signal",true);delay.FooterText="0.5 s";
        var hinge=Add("hinge",490,100,"Hinge open","OUT",GraphColours.Out);Port(hinge,"open","Open",false);
        Connect("brake","signal","and","a");Connect("and","result","delay","in");Connect("delay","out","hinge","open");
        graph.OnClick(Click).OnPortDrag(PortDrag).OnNodeMoved((id,position,final)=> { if(final)SavePosition(id,position); })
            .OnViewChanged((pan,zoom)=>SaveView(pan,zoom)).FitAll();
    }
    private GraphNode Add(string id,float x,float y,string title,string tag,Color colour)
    { var node=graph.AddNode(id,x,y,title,tag,colour);nodes.Add(id,node);return node; }
    private void Port(GraphNode node,string id,string label,bool output)
        =>ports.Add((node.Id,id),output?node.AddOutput(id,label):node.AddInput(id,label));
    private void AddDelay()
    { var node=Add("delay"+serial++,120,180,"Delay","LOGIC",GraphColours.Logic);Port(node,"in","Signal",false);Port(node,"out","Signal",true); }
    private void Connect(string from,string output,string to,string input)
    {
        // The sample flow allows one driver per input; fan-out is unrestricted.
        for(int i=wires.Count-1;i>=0;i--) if(destinations[wires[i].Id]==(to,input)) { graph.RemoveWire(wires[i].Id);destinations.Remove(wires[i].Id);wires.RemoveAt(i); }
        string id="wire"+serial++;wires.Add(graph.AddWire(id,from,output,to,input));destinations[id]=(to,input);
    }
    private readonly Dictionary<string,(string Node,string Port)> destinations=new();
    private void Cancel()
    { if(pending is { } start&&ports.TryGetValue(start,out var port))port.Chosen=false;pending=null;graph.EndPendingWire(); }
    private void PortDrag(GraphPortDrop drop)
    {
        var hit=drop.Drop;
        if(hit.Kind!=GraphHitKind.Port||hit.NodeId==null||hit.PortId==null)return;
        var target=ports[(hit.NodeId,hit.PortId)];
        if(target.IsOutput==drop.FromOutput)return;
        if(drop.FromOutput)Connect(drop.FromNode,drop.FromPort,hit.NodeId,hit.PortId);
        else Connect(hit.NodeId,hit.PortId,drop.FromNode,drop.FromPort);
    }
    private void Click(GraphHit hit)
    {
        if(hit.Button!=0)return;
        if(hit.Kind==GraphHitKind.Port)
        {
            var key=(hit.NodeId!,hit.PortId!);var port=ports[key];
            if(port.IsOutput) { Cancel();pending=key;port.Chosen=true;graph.BeginPendingWire(key.Item1,key.Item2); }
            else if(pending is { } from) { Connect(from.Node,from.Port,key.Item1,key.Item2);Cancel(); }
            return;
        }
        Cancel();
        foreach(var node in nodes.Values)node.Selected=node.Id==hit.NodeId;
        foreach(var wire in wires)wire.Selected=wire.Id==hit.WireId;
        selectedNode=hit.NodeId==null?null:nodes[hit.NodeId]; selectedWire=hit.WireId==null?null:graph.Wire(hit.WireId);
        if(hit.Kind==GraphHitKind.FooterButton&&selectedNode!=null)
        { selectedNode.Lamp=!selectedNode.Lamp;selectedNode.FooterButton=selectedNode.Lamp?"Release":"Hold";ports[(selectedNode.Id,"signal")].On=selectedNode.Lamp; }
    }
    private void DeleteSelection()
    {
        Cancel();if(selectedWire!=null) { graph.RemoveWire(selectedWire.Id);wires.Remove(selectedWire);destinations.Remove(selectedWire.Id);selectedWire=null; }
        if(selectedNode!=null)
        {
            string id=selectedNode.Id; graph.RemoveNode(id);nodes.Remove(id);selectedNode=null;
            foreach(var key in ports.Keys.Where(key=>key.Node==id).ToArray())ports.Remove(key);
            for(int i=wires.Count-1;i>=0;i--)if(graph.Wire(wires[i].Id)==null) { destinations.Remove(wires[i].Id);wires.RemoveAt(i); }
        }
    }
    // Invoke from the consumer's existing main-thread signal tick (for example 10 Hz).
    public void SetSignal(string node,string port,bool on) => ports[(node,port)].On=on;
    public void SetWireSignal(string id,bool on) { var wire=graph.Wire(id);if(wire!=null)wire.On=on; }
    private void SavePosition(string id,Vector2 position) { /* Store in the consumer's flow model. */ }
    private void SaveView(Vector2 pan,float zoom) { /* Store with the consumer's editor state. */ }
}
