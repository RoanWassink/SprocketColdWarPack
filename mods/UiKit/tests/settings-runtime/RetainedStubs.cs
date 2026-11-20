// Headless retained controls: exercise the production Settings.cs bindings with real Keybinds/ConfigEntry values.
// Unity drawing and input dispatch remain an explicit live-test gate.
namespace SprocketUiKit;
public enum TextStyle { Value, Heading }
public class Element
{
    internal System.Func<bool>? VisibleGetter;
    internal virtual System.Collections.Generic.IEnumerable<Element> Kids => System.Array.Empty<Element>();
}
public abstract partial class Container : Element
{
    internal readonly System.Collections.Generic.List<Element> Children = new();
    internal override System.Collections.Generic.IEnumerable<Element> Kids => Children;
    public T Add<T>(T e) where T : Element { Children.Add(e); return e; }
    public void Remove(Element e) => Children.Remove(e);
    public Row Row() => Add(new Row());
    public Stack Stack() => Add(new Stack());
    public Label Label(string text, TextStyle style = TextStyle.Value) => Add(new Label(text));
    public Label Label(System.Func<string> text) => Add(new Label(text()));
    public Element Spacer() => Add(new Element());
    public Chip Chip(string text) => Add(new Chip(text));
    public Button Button(string text, System.Action action) => Add(new Button(text, action));
    public Toggle Toggle(System.Func<bool> value, System.Action<bool> set) => Add(new Toggle(value, set));
    public Slider Slider(System.Func<double> value, System.Action<double> set, double min, double max, double step) => Add(new Slider(value, set));
    public Dropdown Dropdown(System.Collections.Generic.IReadOnlyList<string> choices, System.Func<int> value, System.Action<int> select) => Add(new Dropdown(select));
    public TextField TextField(System.Func<string> value, System.Action<string> commit) => Add(new TextField(commit));
    public Element Segmented(string[] labels, System.Func<int> get, System.Action<int> set) => Add(new Element());
    public Stack ScrollView() => Add(new Stack());
}
public sealed class Row : Container { internal SettingBinding? SettingBinding; internal float Gap; }
public class Stack : Container { }
public sealed class Column : Stack { }
public sealed class Chip : Element { internal string Text; internal Chip(string text) { Text = text; } }
public sealed class Label : Element { internal string Text; internal Label(string text) { Text = text; } public Label Wrap() => this; }
public sealed class Button : Element { internal string Text; private readonly System.Action onClick; internal Button(string text, System.Action action) { Text = text; onClick = action; } }
public sealed class Toggle : Element { private readonly System.Func<bool> value; private readonly System.Action<bool> set; internal Toggle(System.Func<bool> value, System.Action<bool> set) { this.value = value; this.set = set; } }
public sealed class Slider : Element
{
    private readonly System.Func<double> value; private readonly System.Action<double> set; private System.Action? dragEnd;
    internal Slider(System.Func<double> value, System.Action<double> set) { this.value = value; this.set = set; }
    public Slider WithField() => this;
    public Slider OnDragEnd(System.Action done) { dragEnd = done; return this; }
}
public sealed class Dropdown : Element { private readonly System.Action<int> select; internal Dropdown(System.Action<int> select) { this.select = select; } }
public sealed class TextField : Element
{
    private readonly System.Action<string> commit;
    internal TextField(System.Action<string> commit) { this.commit = commit; }
    internal void Changed(string text) { }
    internal void Commit(string text) => commit(text);
    public TextField MaxChars(int n) => this;
}
public static class ElementExtensions
{
    public static T Tooltip<T>(this T e, string text) where T : Element => e;
    public static T Width<T>(this T e, float width) where T : Element => e;
    public static T Fill<T>(this T e) where T : Element => e;
    public static T VisibleWhen<T>(this T e, System.Func<bool> visible) where T : Element { e.VisibleGetter = visible; return e; }
}
[System.Flags] public enum UiContexts { All = 7 }
public sealed class Panel
{
    public readonly Stack Body = new(); public bool IsOpen;
    public UiContexts AllowedContexts; public bool StayOpenWhilePaused, CloseOnSceneChange;
    public event System.Action? OnOpen, OnClose;
    public void Open() { IsOpen = true; OnOpen?.Invoke(); }
    public void Close() { IsOpen = false; OnClose?.Invoke(); }
    public Column[] Columns(params float[] widths) { var cols = new[] { new Column(), new Column() }; Body.Add(cols[0]); Body.Add(cols[1]); return cols; }
    public void Confirm(string title, string text, string button, System.Action confirm) { }
}
public static class UiKit
{
    public const string PluginGuid = "sprocket.uikit";
    public static Panel CreatePanel(string owner, string id, string title, float width, float height) => new();
    public static void DestroyPanel(Panel p) => p.Close();
}
internal static class Kit { internal static float Now = 0; internal static void LeftSubtree(Panel p) { } }
internal sealed class Plugin { internal static Plugin Instance = new(); internal Logger Log = new(); }
internal sealed class Logger { internal int WarningCount; internal void LogWarning(string text) { WarningCount++; System.Console.WriteLine(text); } }
