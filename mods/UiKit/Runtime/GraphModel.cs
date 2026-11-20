using UnityEngine;
namespace SprocketUiKit;

/// <summary>GraphColours UI data and behavior.</summary>
public static class GraphColours
{
    private static readonly Color inColour = new Color32(0x79,0xd8,0xe8,255);
    public static Color In => inColour;
    public static Color Logic => Draw.C(Tok.Value);
    public static Color Out => Draw.C(Tok.AccentText);
    public static Color WireOn => Draw.C(Tok.Accent);
    public static Color WireOff => Draw.C(Tok.GraphWireOff);
}
/// <summary>GraphNode UI data and behavior.</summary>
public sealed class GraphNode
{
    internal readonly NodeGraph Owner;
    internal readonly GraphChanges Changes = new();
    internal readonly List<GraphPort> Inputs = new(), Outputs = new();
    internal GraphNodeView? View;
    internal readonly float Width;
    public string Id { get; }
    internal GraphNode(NodeGraph owner,string id,float x,float y,string title,string tag,Color colour,float width)
    { Owner=owner; Id=id; position=new(x,y); this.title=title; this.tag=tag; tagColour=colour; Width=width; Changes.Mark(GraphDirty.Layout|GraphDirty.Colour); }
    private string title,tag,footerText="";
    private string? footerButton;
    private Color tagColour;
    private Vector2 position;
    private bool selected,dimmed,lamp;
    private void Set<T>(ref T field,T value,GraphDirty dirty) { if(Changes.Set(ref field,value,dirty) && (dirty&GraphDirty.Shape)!=0) Owner.NodeShape(this); }
    public string Title { get=>title; set=>Set(ref title,value??"",GraphDirty.Layout); }
    public string Tag { get=>tag; set=>Set(ref tag,value??"",GraphDirty.Layout); }
    public Color TagColour { get=>tagColour; set { if(tagColour==value)return; tagColour=value; Changes.Mark(GraphDirty.Colour); } }
    public Vector2 Position { get=>position; set { if(!float.IsFinite(value.x)||!float.IsFinite(value.y)) throw new ArgumentOutOfRangeException(nameof(value)); if(position==value)return; position=value; Changes.Mark(GraphDirty.Layout|GraphDirty.Shape); Owner.NodeShape(this); } }
    public bool Selected { get=>selected; set=>Set(ref selected,value,GraphDirty.Colour|GraphDirty.Order); }
    public bool Dimmed { get=>dimmed; set=>Set(ref dimmed,value,GraphDirty.Colour); }
    public bool Lamp { get=>lamp; set=>Set(ref lamp,value,GraphDirty.Colour); }
    public string FooterText { get=>footerText; set=>Set(ref footerText,value??"",GraphDirty.Layout); }
    public string? FooterButton { get=>footerButton; set=>Set(ref footerButton,value,GraphDirty.Layout); }
    internal GraphNodeBounds Bounds => new(new(position.x,position.y,Width,GraphMath.Height(Inputs.Count,Outputs.Count)),Inputs.Count,Outputs.Count,FooterButton!=null);
    public GraphPort AddInput(string id,string label) => AddPort(id,label,false);
    public GraphPort AddOutput(string id,string label) => AddPort(id,label,true);
    private GraphPort AddPort(string id,string label,bool output)
    {
        if(string.IsNullOrWhiteSpace(id)) throw new ArgumentException("Port id is required.",nameof(id));
        foreach(var p in Inputs) if(p.Id==id) throw new ArgumentException("Duplicate port id.",nameof(id));
        foreach(var p in Outputs) if(p.Id==id) throw new ArgumentException("Duplicate port id.",nameof(id));
        var list=output?Outputs:Inputs; var port=new GraphPort(this,id,label,output,list.Count); list.Add(port);
        Changes.Mark(GraphDirty.Layout|GraphDirty.Colour); Owner.NodeShape(this); return port;
    }
    internal GraphPort? FindPort(string id)
    { foreach(var p in Inputs) if(p.Id==id) return p; foreach(var p in Outputs) if(p.Id==id) return p; return null; }
}
/// <summary>GraphPort UI data and behavior.</summary>
public sealed class GraphPort
{
    internal readonly GraphNode Node;
    internal readonly int Index;
    public string Id { get; }
    public bool IsOutput { get; }
    private bool on,chosen;
    private string label;
    internal GraphPort(GraphNode node,string id,string label,bool output,int index) { Node=node; Id=id; this.label=label; IsOutput=output; Index=index; }
    public bool On { get=>on; set { if(on==value)return; on=value; Node.Changes.Mark(GraphDirty.Colour); } }
    public bool Chosen { get=>chosen; set { if(chosen==value)return; chosen=value; Node.Changes.Mark(GraphDirty.Colour); } }
    public string Label { get=>label; set { value??=""; if(label==value)return; label=value; Node.Changes.Mark(GraphDirty.Layout); } }
    internal Vec Point => Node.Bounds.Port(IsOutput,Index);
}
/// <summary>GraphWire UI data and behavior.</summary>
public sealed class GraphWire
{
    internal readonly NodeGraph Owner;
    internal readonly GraphPort From,To;
    internal readonly GraphStrip Strip = new();
    internal bool ShapeDirty=true, ColourDirty=true;
    internal int Offset;
    public string Id { get; }
    private bool on,selected;
    private Color? colour;
    private float width=2;
    internal GraphWire(NodeGraph owner,string id,GraphPort from,GraphPort to) { Owner=owner; Id=id; From=from; To=to; }
    public bool On { get=>on; set { if(on==value)return; on=value; ColourDirty=true; } }
    public bool Selected { get=>selected; set { if(selected==value)return; selected=value; ColourDirty=true; } }
    public Color? Colour { get=>colour; set { if(colour==value)return; colour=value; ColourDirty=true; } }
    public float Width { get=>width; set { if(!float.IsFinite(value)||value<=0) throw new ArgumentOutOfRangeException(nameof(value)); if(width==value)return; width=value; ShapeDirty=true; Owner.Changed(GraphDirty.Shape); } }
    internal Color Tint => Selected ? Draw.C(Tok.AccentText) : Colour ?? (On ? GraphColours.WireOn : GraphColours.WireOff);
}
