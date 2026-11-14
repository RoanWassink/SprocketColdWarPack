using SprocketTelescopicMast;
using SprocketTelescopicMast.Io;
using SprocketUiKit;
using UnityEngine;
internal static class Tests098
{
 internal static int Run()
 {
  int checks=0;void Check(bool ok,string why){checks++;if(!ok)throw new Exception("0.9.8 UI: "+why);}
  var flow=new FlowDocument();var input=new FlowNode("on","always");flow.AddNode(input);
  var a=new FlowNode("a","mast");a.Set("partId","primary");flow.AddNode(a);flow.Connect("on","a",0);
  var b=new FlowNode("b","mast");b.Set("partId","secondary");flow.AddNode(b);flow.Connect("on","b",0);
  var missing=new FlowNode("missing","mast");missing.Set("partId","missing-part");flow.AddNode(missing);
  string original=FlowJson.Write(flow);UiKit.Context=UiContexts.Designer;Time.unscaledTime=100;
  HingeRuntime.Current=new(){Flow=flow,Parts=new(){new("primary","mast","Mast 1")},Exists=id=>id=="secondary",TwinHint=n=>n.PartId=="secondary"?"twin of Mast 1, can be removed":null};
  HingePanel.Create(new());UiKit.Latest.Open();
  Time.unscaledTime+=.2f;HingePanel.Update();UiKit.Poll();
  var graph=NodeGraph.Latest;
  Check(graph.Nodes.Count==4,"both saved outputs remain drawn");
  Check(!graph.Nodes["b"].Dimmed,"wired twin is not shown as a missing part");
  Check(graph.Nodes["missing"].Dimmed,"genuinely absent part remains dimmed");
  Check(graph.Nodes["b"].FooterText=="twin of Mast 1, can be removed","removable-twin hint visible on the real graph node");
  Check(FlowJson.Write(flow)==original,"opening and previewing panel never rewrites graph");
  HingePanel.Destroy();HingeRuntime.Current=null;return checks;
 }
}
