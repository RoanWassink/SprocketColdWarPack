using TMPro;
using UnityEngine;
using UnityEngine.UI;
namespace SprocketUiKit;

// Removed nodes return these native objects to the graph's own pool. Port rows also grow only on demand.
internal sealed class GraphNodeView
{
    internal readonly BoxView Box, Head, Chip, Button;
    internal readonly TextMeshProUGUI Title, Tag, Footer, ButtonText;
    internal readonly Image Lamp;
    internal readonly CanvasGroup Group;
    private readonly List<(Image Dot,TextMeshProUGUI Label)> inputs=new(), outputs=new();
    internal GraphNodeView(RectTransform world)
    {
        Box=new(world,"GraphNode",5); Head=new(Box.Rt,"Header",5); Chip=new(Head.Rt,"TagChip",4);
        Title=Draw.Text(Draw.Node("Title",Head.Rt),TextStyle.Label);
        Tag=Draw.Text(Draw.Node("Tag",Chip.Rt),TextStyle.Small); Tag.alignment=TextAlignmentOptions.Midline;
        Footer=Draw.Text(Draw.Node("Footer",Box.Rt),TextStyle.Small);
        Lamp=Draw.Child(Box.Rt,"Lamp",Assets.Circle(),Tok.Faint);
        Button=new(Box.Rt,"FooterButton",4); ButtonText=Draw.Text(Draw.Node("Text",Button.Rt),TextStyle.Small); Draw.Stretch(ButtonText.rectTransform,3,0,3,0); ButtonText.alignment=TextAlignmentOptions.Midline;
        Group=Box.Rt.gameObject.AddComponent<CanvasGroup>(); Group.blocksRaycasts=false; Group.interactable=false;
    }
    internal void Apply(GraphNode node,GraphDirty dirty)
    {
        if((dirty&GraphDirty.Layout)!=0)
        {
            float w=node.Width,h=node.Bounds.Body.H;
            var header=GraphHeader.Layout(w,Draw.Width(Tag,node.Tag));
            Box.Place(node.Position.x,node.Position.y,w,h); Head.Place(header.Head,default); Chip.Place(header.Chip,default);
            Draw.Place(Tag.rectTransform,header.Tag,default);
            Draw.Place(Title.rectTransform,header.Title,default); Draw.SetText(Title,node.Title); Draw.SetText(Tag,node.Tag);
            Draw.Place(Lamp.rectTransform,10,h-20,6,6); Draw.Place(Footer.rectTransform,23,h-28,w-(node.FooterButton==null?31:89),26); Draw.SetText(Footer,node.FooterText);
            Button.Place(w-60,h-26,54,22); Draw.SetText(ButtonText,node.FooterButton); Draw.SetActive(Button.Rt,node.FooterButton!=null);
            LayoutPorts(node.Inputs,inputs,false,w); LayoutPorts(node.Outputs,outputs,true,w);
            Box.Ring?.rectTransform.SetAsLastSibling();
        }
        if((dirty&(GraphDirty.Colour|GraphDirty.Layout))!=0)
        {
            Box.Set(Tok.HeadFoot,node.Selected?Tok.Accent:Tok.Border); Head.Set(Tok.RowBg,Tok.Divider);
            var c=Draw.R(node.TagColour); Chip.Set(Tok.Btn,c); Draw.SetColor(Tag,c); Button.Set(Tok.Btn,Tok.Border);
            Group.alpha=node.Dimmed?.38f:1; Draw.SetColor(Lamp,node.Lamp?Tok.On:Tok.Faint);
            ColourPorts(node.Inputs,inputs); ColourPorts(node.Outputs,outputs);
        }
    }
    private void LayoutPorts(List<GraphPort> ports,List<(Image Dot,TextMeshProUGUI Label)> pool,bool output,float width)
    {
        while(pool.Count<ports.Count)
        { var dot=Draw.Child(Box.Rt,"Port",Assets.Circle(),Tok.Faint); var label=Draw.Text(Draw.Node("PortLabel",Box.Rt),TextStyle.Small); if(output) label.alignment=TextAlignmentOptions.MidlineRight; pool.Add((dot,label)); }
        for(int i=0;i<pool.Count;i++)
        {
            var v=pool[i]; bool active=i<ports.Count; Draw.SetActive(v.Dot,active); Draw.SetActive(v.Label,active); if(!active)continue;
            float y=36+24*i; Draw.Place(v.Dot.rectTransform,output?width-4:-4,y+8,8,8);
            Draw.Place(v.Label.rectTransform,output?width*.5f:12,y,width*.5f-24,24); Draw.SetText(v.Label,ports[i].Label);
        }
    }
    private static void ColourPorts(List<GraphPort> ports,List<(Image Dot,TextMeshProUGUI Label)> pool)
    { for(int i=0;i<ports.Count;i++) Draw.SetColor(pool[i].Dot,ports[i].Chosen?Tok.AccentText:ports[i].On?Tok.Accent:Tok.GraphWireOff); }
}
