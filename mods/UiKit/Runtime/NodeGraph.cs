using Il2CppInterop.Runtime.InteropTypes.Arrays;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
namespace SprocketUiKit;

/// <summary>Retained node graph. Mutate handles on the Unity thread; changes are flushed once per frame.</summary>
public sealed class NodeGraph : Element
{
    private readonly Dictionary<string,GraphNode> nodes=new(StringComparer.Ordinal);
    private readonly Dictionary<string,GraphWire> wires=new(StringComparer.Ordinal);
    private readonly List<GraphNode> order=new();
    private readonly List<GraphWire> wireOrder=new();
    private readonly List<GraphNodeBounds> hitNodes=new();
    private readonly List<GraphStrip> hitWires=new();
    private readonly Stack<GraphNodeView> pool=new();
    private RectTransform? world;
    private RectMask2D? mask;
    private CanvasRenderer? renderer;
    private Mesh? mesh;
    private Texture2D? texture;
    private TextMeshProUGUI? hintText;
    private BoxView? hintBox;
    private Il2CppStructArray<Vector3>? vertices;
    private Il2CppStructArray<Vector2>? uv;
    private Il2CppStructArray<Color32>? colours;
    private Il2CppStructArray<int>? triangles;
    private int slots=-1;
    private readonly List<RectMask2D> clipMasks=new();
    private bool overflowWarned;
    private float nextMeshLog;
    private int loggedSlots=-1;
    private bool topology=true, shape=true, viewDirty=true;
    private Vector2 pan;
    private float zoom=1,minZoom=.5f,maxZoom=1.5f,grid;
    private float? fitMargin;
    private Func<string?>? hint;
    private Action<GraphHit>? click;
    private Action<string,Vector2,bool>? moved;
    private Action<Vector2,float>? viewChanged;
    private GraphPort? pending, dragPort;
    private readonly GraphPortGesture<GraphHit> portGesture=new();
    private readonly GraphStrip dragStrip=new();
    private Action<GraphPortDrop>? portDrop;
    private float portRadius=7;
    private bool DragWire => dragPort!=null&&portGesture.Dragging;
    private readonly GraphStrip pendingStrip=new();
    private Vec pendingEnd=new(float.NaN,float.NaN);
    internal NodeGraph() { }
    internal IReadOnlyList<GraphNode> NodesInternal=>order;
    private Canvas? isolatedCanvas;
    private bool isolate;
    internal void BatchIsolation(bool enabled)
    { isolate=enabled; if(world==null)return; if(enabled&&isolatedCanvas==null)isolatedCanvas=world.gameObject.AddComponent<Canvas>(); if(isolatedCanvas!=null)isolatedCanvas.enabled=enabled; lastClip=null; }
    public Vector2 ViewSize => new(Rect.W,Rect.H);
    public Rect VisibleRect { get { var r=GraphMath.Visible(new(pan.x,pan.y),zoom,Rect.W,Rect.H); return new(r.X,r.Y,r.W,r.H); } }
    public NodeGraph PortHitRadius(float px) { portRadius=GraphMath.PortRadius(px); return this; }
    public NodeGraph OnPortDrag(Action<GraphPortDrop> handler) { portDrop=handler; return this; }
    public Vector2 Pan => pan;
    public float Zoom => zoom;
    public NodeGraph SetView(Vector2 pan,float zoom)
    {
        if(!float.IsFinite(pan.x)||!float.IsFinite(pan.y)||!float.IsFinite(zoom)) throw new ArgumentOutOfRangeException(nameof(zoom));
        zoom=Math.Clamp(zoom,minZoom,maxZoom); if(this.pan==pan&&this.zoom==zoom)return this;
        this.pan=pan; this.zoom=zoom; viewDirty=true; NotifyView(); return this;
    }
    public NodeGraph ZoomRange(float min=.5f,float max=1.5f)
    { if(!float.IsFinite(min)||!float.IsFinite(max)||min<=0||max<min)throw new ArgumentOutOfRangeException(nameof(min)); minZoom=min; maxZoom=max; return SetView(pan,zoom); }
    public NodeGraph Snap(float grid) { if(!float.IsFinite(grid)||grid<0)throw new ArgumentOutOfRangeException(nameof(grid)); this.grid=grid; return this; }
    public NodeGraph Hint(Func<string?> text) { hint=text; return this; }
    public NodeGraph FitAll(float margin=40) { if(!float.IsFinite(margin)||margin<0)throw new ArgumentOutOfRangeException(nameof(margin)); fitMargin=margin; return this; }
    public NodeGraph OnClick(Action<GraphHit> handler) { click=handler; return this; }
    public NodeGraph OnNodeMoved(Action<string,Vector2,bool> handler) { moved=handler; return this; }
    public NodeGraph OnViewChanged(Action<Vector2,float> handler) { viewChanged=handler; return this; }
    public GraphNode AddNode(string id,float x,float y,string title,string tag,Color tagColour,float width=176)
    {
        if(string.IsNullOrWhiteSpace(id)||nodes.ContainsKey(id))throw new ArgumentException("Node id must be unique and non-empty.",nameof(id));
        if(!float.IsFinite(x)||!float.IsFinite(y)||!float.IsFinite(width)||width<100)throw new ArgumentOutOfRangeException(nameof(width));
        var n=new GraphNode(this,id,x,y,title,tag,tagColour,width); nodes.Add(id,n);
        int firstSelected=order.FindIndex(static item=>item.Selected); if(firstSelected<0)order.Add(n); else order.Insert(firstSelected,n); return n;
    }
    public GraphNode? Node(string id) => nodes.TryGetValue(id,out var n)?n:null;
    public GraphWire? Wire(string id) => wires.TryGetValue(id,out var w)?w:null;
    public bool RemoveNode(string id)
    {
        if(!nodes.Remove(id,out var n))return false;
        for(int i=wireOrder.Count-1;i>=0;i--) if(wireOrder[i].From.Node==n||wireOrder[i].To.Node==n) RemoveWire(wireOrder[i].Id);
        if(pending?.Node==n)EndPendingWire(); if(dragNode==n||dragPort?.Node==n)PointerCancel();
        order.Remove(n); if(n.View!=null) { Draw.SetActive(n.View.Box.Rt,false); pool.Push(n.View); n.View=null; } return true;
    }
    public void Clear() { PointerCancel(); while(order.Count>0)RemoveNode(order[order.Count-1].Id); EndPendingWire(); }
    public GraphWire AddWire(string id,string fromNode,string fromPort,string toNode,string toPort)
    {
        if(string.IsNullOrWhiteSpace(id)||wires.ContainsKey(id))throw new ArgumentException("Wire id must be unique and non-empty.",nameof(id));
        var a=RequirePort(fromNode,fromPort); var b=RequirePort(toNode,toPort);
        if(!a.IsOutput||b.IsOutput)throw new ArgumentException("Wires run from an output to an input.");
        var w=new GraphWire(this,id,a,b); wires.Add(id,w); wireOrder.Add(w); topology=shape=true; return w;
    }
    public bool RemoveWire(string id) { if(!wires.Remove(id,out var w))return false; wireOrder.Remove(w); topology=shape=true; return true; }
    public void BeginPendingWire(string node,string port) { pending=RequirePort(node,port); topology=shape=true; pendingEnd=new(float.NaN,float.NaN); }
    public void EndPendingWire() { if(pending==null)return; pending=null; topology=shape=true; }
    private GraphPort RequirePort(string node,string port) => Node(node)?.FindPort(port)??throw new ArgumentException("Unknown graph port: "+node+"/"+port);
    internal void Changed(GraphDirty dirty) { if((dirty&GraphDirty.Shape)!=0)shape=true; }
    internal void NodeShape(GraphNode node)
    { foreach(var w in wireOrder) if(w.From.Node==node||w.To.Node==node)w.ShapeDirty=true; shape=true; }
    private void Front(GraphNode n) { order.Remove(n); order.Add(n); n.View?.Box.Rt.SetAsLastSibling(); }
    private void NotifyView() { try { viewChanged?.Invoke(pan,zoom); } catch(Exception ex) { Guard.Report(Site(0,".ViewChanged"),ex); } }
    protected override void Build()
    {
        var bg=new BoxView(Rt!,"GraphBackground",4); Draw.Stretch(bg.Rt); bg.Set(Tok.CanvasBg,Tok.Divider);
        var viewport=Draw.Node("Viewport",Rt!); Draw.Stretch(viewport); mask=viewport.gameObject.AddComponent<RectMask2D>();
        world=Draw.Node("World",viewport);
        BatchIsolation(isolate);
        clipMasks.Clear();
        for(Transform? parent=viewport;parent!=null;parent=parent.parent)
        {
            var ancestor=parent.GetComponent<RectMask2D>();
            if(ancestor!=null)clipMasks.Add(ancestor);
        }
        var wireRoot=Draw.Node("Wires",world); renderer=wireRoot.gameObject.AddComponent<CanvasRenderer>(); renderer.materialCount=1;
        mesh=new Mesh { name="UI Kit graph wires", hideFlags=HideFlags.HideAndDontSave, indexFormat=UnityEngine.Rendering.IndexFormat.UInt16 };
        texture=new Texture2D(1,16,TextureFormat.RGBA32,false) { name="UI Kit wire AA",hideFlags=HideFlags.HideAndDontSave,wrapMode=TextureWrapMode.Clamp,filterMode=FilterMode.Bilinear };
        var pixels=new Color[16]; for(int i=0;i<16;i++)pixels[i]=new(1,1,1,MathF.Min(1,MathF.Min(i,15-i)/2f)); texture.SetPixels(pixels); texture.Apply(false,false);
        renderer.SetMaterial(Graphic.defaultGraphicMaterial,texture);
        hintBox=new(viewport,"Hint",4); hintBox.Set(Tok.Selected,Tok.AccentLine);
        hintText=Draw.Text(Draw.Node("HintText",hintBox.Rt),TextStyle.Small); Draw.Stretch(hintText.rectTransform,10,0,10,0); hintText.alignment=TextAlignmentOptions.Midline;
        foreach(var n in order)n.Changes.Mark(GraphDirty.Layout|GraphDirty.Colour);
        shownHint="\0"; lastClip=null; loggedSlots=-1; nextMeshLog=0; topology=shape=viewDirty=true;
    }
    internal override void Unbuild()
    {
        PointerCancel(); if(Kit.GraphCapture==this)Kit.GraphCapture=null;
        foreach(var n in order)n.View=null; pool.Clear();
        if(mesh!=null)UnityEngine.Object.Destroy(mesh); if(texture!=null)UnityEngine.Object.Destroy(texture);
        mesh=null; texture=null; renderer=null; world=null; mask=null; hintBox=null; hintText=null; isolatedCanvas=null;
        clipMasks.Clear(); ReleaseBuffers(); slots=-1; base.Unbuild();
    }
    // Interop arrays have GC handles managed by their wrappers, with no IDisposable API.
    private void ReleaseBuffers() { vertices=null; uv=null; colours=null; triangles=null; }
    internal override float MeasureHeight(float width)=>240;
    internal override bool Interactive=>true;
    internal override void ArrangeCore(RectF r,Vec origin,RectF clip) { base.ArrangeCore(r,origin,clip); viewDirty=true; }
    internal override void Refresh(float now)
    {
        if(world==null||renderer==null)return;
        if(fitMargin is { } margin && Rect.W>0&&Rect.H>0)
        {
            fitMargin=null;
            if(order.Count>0) {
                float x=float.MaxValue,y=x,right=float.MinValue,bottom=right;
                foreach(var n in order) { var b=n.Bounds.Body; x=MathF.Min(x,b.X); y=MathF.Min(y,b.Y); right=MathF.Max(right,b.Right); bottom=MathF.Max(bottom,b.Bottom); }
                var fit=GraphMath.Fit(new(x,y,right-x,bottom-y),Rect.W,Rect.H,margin,minZoom,maxZoom);
                SetView(new(fit.Pan.X,fit.Pan.Y),fit.Zoom);
            }
        }
        bool arrangeHint=viewDirty;
        if(viewDirty) { world.anchoredPosition=new(pan.x,-pan.y); world.localScale=new(zoom,zoom,1); viewDirty=false; }
        // A raw CanvasRenderer does not receive Graphic.SetClipRect. Mirror the native mask explicitly.
        RectF canvasClip=default; bool first=true;
        foreach(var ancestor in clipMasks)
        {
            if(!ancestor.isActiveAndEnabled)continue;
            var rect=ancestor.canvasRect; var padding=ancestor.padding;
            var next=GraphClip.Padded(new(rect.xMin,rect.yMin,rect.width,rect.height),padding.x,padding.y,padding.z,padding.w);
            canvasClip=first?next:GraphClip.Intersect(canvasClip,next); first=false;
        }
        // Shared Canvas is the production route. Isolated Canvas remains a demo-only experiment;
        // convert the root-canvas mask bounds into that Canvas's space for the comparison.
        var clipRect=new Rect(canvasClip.X,canvasClip.Y,canvasClip.W,canvasClip.H);
        if(isolate&&isolatedCanvas!=null)
        {
            var root=mask!.GetComponentInParent<Canvas>().rootCanvas.transform;
            var a=world.InverseTransformPoint(root.TransformPoint(new Vector3(clipRect.xMin,clipRect.yMin,0)));
            var b=world.InverseTransformPoint(root.TransformPoint(new Vector3(clipRect.xMax,clipRect.yMax,0)));
            clipRect=new Rect(a.x,a.y,b.x-a.x,b.y-a.y);
        }
        if(lastClip==null||lastClip.Value!=clipRect) { renderer.EnableRectClipping(clipRect); renderer.cull=canvasClip.W<=0||canvasClip.H<=0; lastClip=clipRect; }
        GraphNode? raise=null; bool siblingOrder=false,selectionOrder=false;
        foreach(var n in order)
        {
            if(n.View==null) { n.View=pool.Count>0?pool.Pop():new GraphNodeView(world); Draw.SetActive(n.View.Box.Rt,true); siblingOrder=true; n.Changes.Mark(GraphDirty.Layout|GraphDirty.Colour); }
            var dirty=n.Changes.Take(); if(dirty!=GraphDirty.None)n.View.Apply(n,dirty);
            if(n.Selected&&(dirty&GraphDirty.Order)!=0)raise=n;
            selectionOrder|=(dirty&GraphDirty.Order)!=0;
        }
        // Keep model hit order and native sibling order identical, including after pooled reuse.
        if(selectionOrder) { order.Sort(static(a,b)=>a.Selected.CompareTo(b.Selected)); siblingOrder=true; }
        if(siblingOrder)foreach(var n in order)n.View!.Box.Rt.SetAsLastSibling();
        if(raise!=null)Front(raise);
        if(pending!=null) { var end=Graph(Local(Panel.LocalPointer)); if(end.X!=pendingEnd.X||end.Y!=pendingEnd.Y) { pendingEnd=end; shape=true; } }
        FlushMesh();
        string? message=hint==null?null:Guard.Get(Site(1,".Hint"),hint,null);
        if(message!=shownHint) { shownHint=message; Draw.SetText(hintText,message); Draw.SetActive(hintBox!.Rt,!string.IsNullOrEmpty(message)); arrangeHint=true; }
        if(hintBox!=null&&arrangeHint)hintBox.Place(12,12,MathF.Max(0,Rect.W-24),30);
    }
    private string? shownHint="\0";
    private Rect? lastClip;
    private void FlushMesh()
    {
        int wireCount=GraphMeshLayout.WireSlots(wireOrder.Count,pending!=null,DragWire);
        int count=wireCount+(pending==null?0:1)+(DragWire?1:0);
        if(wireCount<wireOrder.Count&&!overflowWarned)
        {
            overflowWarned=true;
            Plugin.Instance.Log.LogWarning($"[UI Kit] Graph wire limit: {GraphMeshLayout.MaxSlots} slots (including pending); extra wires are not drawn.");
        }
        bool all=GraphMeshLayout.Rebuild(topology,slots,count);
        if(slots!=count)
        {
            ReleaseBuffers(); slots=count; int v=count*GraphMeshLayout.VerticesPerSlot;
            vertices=new(v); uv=new(v); colours=new(v); triangles=new(count*GraphMeshLayout.IndicesPerSlot);
            for(int i=0;i<triangles.Length;i++)triangles[i]=GraphMeshLayout.IndexAt(i);
            for(int p=0;p<v;p+=4) { uv[p]=uv[p+2]=new(.5f,0); uv[p+1]=uv[p+3]=new(.5f,1); }
        }
        bool geometry=shape||all, colour=all;
        for(int i=0;i<wireCount;i++)
        {
            var w=wireOrder[i]; w.Offset=i*GraphMeshLayout.VerticesPerSlot;
            if(w.ShapeDirty) { w.Strip.Build(w.From.Point,w.To.Point,w.Width); w.ShapeDirty=false; }
            if(geometry)CopyShape(w.Strip,w.Offset);
            if(w.ColourDirty||all) { CopyColour(w.Tint,w.Offset); colour=true; w.ColourDirty=false; }
        }
        if(pending!=null&&geometry) { pendingStrip.Build(pending.Point,pendingEnd,2,true); CopyShape(pendingStrip,wireCount*GraphMeshLayout.VerticesPerSlot); CopyColour(Draw.C(Tok.Text),wireCount*GraphMeshLayout.VerticesPerSlot); colour=true; }
        if(DragWire&&geometry) { dragStrip.Build(dragPort!.Point,Graph(Local(Panel.LocalPointer)),2,true); int offset=(wireCount+(pending==null?0:1))*GraphMeshLayout.VerticesPerSlot; CopyShape(dragStrip,offset); CopyColour(Draw.C(Tok.AccentText),offset); colour=true; }
        if(all) UploadFullMesh();
        else
        {
            if(geometry) { mesh!.vertices=vertices!; mesh.uv=uv!; }
            if(colour)mesh!.colors32=colours!;
            if(geometry||colour)renderer!.SetMesh(mesh!);
        }
        shape=topology=false;
    }
    private void UploadFullMesh()
    {
        // Clear the Mesh's old channels/indices before changing its vertex count. SetMesh replaces
        // the renderer submission, as in uGUI Graphic.DoMeshGeneration (no renderer.Clear needed).
        mesh!.Clear();
        mesh.vertices=vertices!;
        mesh.uv=uv!;
        mesh.colors32=colours!;
        mesh.triangles=triangles!;
        renderer!.SetMesh(mesh);
        if(Plugin.Detailed&&(slots!=loggedSlots||Time.unscaledTime>=nextMeshLog))
        {
            int renderers=world!.GetComponentsInChildren<CanvasRenderer>(true).Length;
            Plugin.Instance.Log.LogInfo($"[UI Kit] Graph mesh rebuild: slots={slots}, vertices={vertices!.Length}, indices={triangles!.Length}, indexFormat={mesh.indexFormat}, CanvasRenderers={renderers} (one wire renderer).");
            loggedSlots=slots; nextMeshLog=Time.unscaledTime+1;
        }
    }
    private void CopyShape(GraphStrip strip,int offset)
    { for(int j=0;j<128;j++) { var p=j<strip.Count*4?strip.Vertices[j]:default; vertices![offset+j]=new(p.X,-p.Y,0); } }
    private void CopyColour(Color colour,int offset) { Color32 c=colour; for(int j=0;j<128;j++)colours![offset+j]=c; }
    private Vec Local(Vec p)=>p-new Vec(Rect.X,Rect.Y);
    private Vec Graph(Vec local)=>GraphMath.Graph(local,new(pan.x,pan.y),zoom);
    private GraphHit Hit(Vec local,int btn)
    {
        var p=Graph(local); float scale=Kit.CurrentScale(Panel)*zoom;
        hitNodes.Clear(); foreach(var n in order)hitNodes.Add(n.Bounds);
        hitWires.Clear(); for(int i=0;i<GraphMeshLayout.WireSlots(wireOrder.Count,pending!=null,DragWire);i++) { var w=wireOrder[i]; if(w.ShapeDirty) { w.Strip.Build(w.From.Point,w.To.Point,w.Width); w.ShapeDirty=false; } hitWires.Add(w.Strip); }
        var pick=GraphPicking.Hit(hitNodes,hitWires,p,scale,portRadius);
        var node=pick.Kind is GraphPickKind.Node or GraphPickKind.Port or GraphPickKind.FooterButton?order[pick.Index]:null;
        return new() { Kind=(GraphHitKind)pick.Kind,NodeId=node?.Id,PortId=pick.Kind==GraphPickKind.Port?(pick.Output?node!.Outputs:node!.Inputs)[pick.Port].Id:null,WireId=pick.Kind==GraphPickKind.Wire?wireOrder[pick.Index].Id:null,GraphPos=new(p.X,p.Y),Button=btn,Shift=Kit.ShiftHeld,Ctrl=Kit.CtrlHeld };
    }
    private bool down,dragged;
    private int button;
    private Vec start;
    private Vector2 startPan,startNode;
    private GraphNode? dragNode;
    private GraphHit pressedHit;
    internal void PointerDown(int btn,Vec p)
    { down=true; dragged=false; button=btn; start=Local(p); startPan=pan; pressedHit=Hit(start,btn); dragPort=btn==0&&pressedHit.Kind==GraphHitKind.Port?RequirePort(pressedHit.NodeId!,pressedHit.PortId!):null; portGesture.Begin(start,pressedHit,dragPort!=null); dragNode=btn==0&&pressedHit.Kind==GraphHitKind.Node?Node(pressedHit.NodeId!):null; if(dragNode!=null) { startNode=dragNode.Position; Front(dragNode); } }
    internal void PointerMove(Vec p)
    {
        if(!down)return; var local=Local(p); bool wasDrag=DragWire; portGesture.Move(local,Kit.CurrentScale(Panel)); if(wasDrag!=DragWire)topology=true; if(DragWire)shape=true; dragged|=GraphMath.Dragged(start,local,Kit.CurrentScale(Panel)); if(!dragged)return;
        var delta=local-start;
        if(dragNode!=null) { var v=new Vector2(GraphMath.Snap(startNode.x+delta.X/zoom,grid),GraphMath.Snap(startNode.y+delta.Y/zoom,grid)); if(v!=dragNode.Position) { dragNode.Position=v; NotifyMove(dragNode,false); } }
        else if(button==2||button==0&&pressedHit.Kind==GraphHitKind.Canvas)SetView(new(startPan.x+delta.X,startPan.y+delta.Y),zoom);
    }
    internal void PointerUp(Vec p)
    {
        if(!down)return; PointerMove(p); down=false;
        if(portGesture.End()&&dragPort!=null)
        {
            var source=dragPort; dragPort=null; topology=shape=true;
            var drop=new GraphPortDrop(source.Node.Id,source.Id,source.IsOutput,Hit(Local(p),button));
            try { portDrop?.Invoke(drop); } catch(Exception ex) { Guard.Report(Site(4,".PortDrag"),ex); }
        }
        else if(dragged&&dragNode!=null)NotifyMove(dragNode,true);
        else if(!dragged&&Rect.Contains(p)&&Clip.Contains(p)) { var h=Hit(Local(p),button); if(h.Kind==pressedHit.Kind&&h.NodeId==pressedHit.NodeId&&h.PortId==pressedHit.PortId&&h.WireId==pressedHit.WireId) { try { click?.Invoke(h); } catch(Exception ex) { Guard.Report(Site(2,".Click"),ex); } } }
        dragNode=null; dragPort=null;
    }
    internal void PointerCancel() { if(DragWire)topology=shape=true; portGesture.Cancel(); down=false; dragNode=null; dragPort=null; }
    private void NotifyMove(GraphNode n,bool final) { try { moved?.Invoke(n.Id,n.Position,final); } catch(Exception ex) { Guard.Report(Site(3,".Moved"),ex); } }
    internal override void OnPress(Vec p)=>Kit.BeginGraphCapture(this,0,p);
    internal override bool OnScroll(float notches,Vec p)
    { if(portGesture.Active)return true; var cursor=Local(p); float z=Math.Clamp(zoom*MathF.Pow(1.15f,notches),minZoom,maxZoom); var v=GraphMath.ZoomAt(cursor,new(pan.x,pan.y),zoom,z); SetView(new(v.X,v.Y),z); return true; }
}
