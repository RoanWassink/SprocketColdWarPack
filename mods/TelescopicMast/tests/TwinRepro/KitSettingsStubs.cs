namespace SprocketUiKit;
public enum TextStyle { Section,Faint }
public class Element
{
 internal System.Func<bool> Enabled=()=>true, Visible=()=>true;
 public Element EnabledWhen(System.Func<bool> f){Enabled=f;return this;}
 public Element VisibleWhen(System.Func<bool> f){Visible=f;return this;}
 public Element Wrap()=>this;
}
public class Container : Element
{
 internal string Name="";
 internal static readonly System.Collections.Generic.Dictionary<string,(Element Widget,System.Delegate Set)> Controls=new();
 internal static readonly System.Collections.Generic.List<(string Text,Element Widget)> Labels=new();
 public Container Stack()=>new();
 public Element Label(string text,TextStyle style){var e=new Element();Labels.Add((text,e));return e;}
 public Container Labeled(string name)=>new(){Name=name};
 public Element Stepper(System.Func<double> get,System.Action<double> set,double min,double max,double step,string unit){get();var e=new Element();Controls[Name]=(e,set);return e;}
 public Element Toggle(System.Func<bool> get,System.Action<bool> set){get();var e=new Element();Controls[Name]=(e,set);return e;}
 public Element Dropdown(System.Func<System.Collections.Generic.IReadOnlyList<string>> choices,System.Func<int> get,System.Action<int> set){get();choices();var e=new Element();Controls[Name]=(e,set);return e;}
}
