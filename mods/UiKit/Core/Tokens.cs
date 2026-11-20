using System.Globalization;
namespace SprocketUiKit;

/// <summary>8-bit RGBA colour without Unity dependency (converted at the uGUI boundary).</summary>
internal readonly struct Rgba : IEquatable<Rgba>
{
    internal readonly byte R, G, B, A;
    internal Rgba(byte r, byte g, byte b, byte a = 255) { R = r; G = g; B = b; A = a; }
    /// <summary>"#rrggbb" or "#rrggbbaa".</summary>
    internal static Rgba Hex(string hex)
    {
        var s = hex.TrimStart('#');
        if (s.Length != 6 && s.Length != 8) throw new FormatException("Colour must be #rrggbb or #rrggbbaa: " + hex);
        byte P(int i) => byte.Parse(s.Substring(i, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
        return new Rgba(P(0), P(2), P(4), s.Length == 8 ? P(6) : (byte)255);
    }
    internal Rgba WithAlpha(float a) => new(R, G, B, (byte)Math.Clamp((int)MathF.Round(a * 255f), 0, 255));
    internal static readonly Rgba Clear = new(0, 0, 0, 0);
    public bool Equals(Rgba o) => R == o.R && G == o.G && B == o.B && A == o.A;
    public override bool Equals(object? obj) => obj is Rgba o && Equals(o);
    public override int GetHashCode() => (R << 24) | (G << 16) | (B << 8) | A;
    public override string ToString() => $"#{R:x2}{G:x2}{B:x2}{A:x2}";
}

/// <summary>
/// Design tokens taken literally from the 2026-10-31 canvas (Main.dc.html / Components.dc.html).
/// Sizes are reference pixels at 1080p; the kit multiplies by the UI scale.
/// </summary>
internal static class Tok
{
    internal static readonly Rgba GraphWireOff = Rgba.Hex("#6b6250");
    internal static readonly Rgba FocusHeader = Rgba.Hex("#242219");
    internal static readonly Rgba FocusTitle = Rgba.Hex("#fff0cc");
    internal static readonly Rgba UnfocusedTitle = Rgba.Hex("#b2a78d");
    // Surfaces
    internal static readonly Rgba Panel = Rgba.Hex("#14150f");
    internal static readonly Rgba PanelGlass = new(20, 21, 15, 245);      // rgba(20,21,15,.96)
    internal static readonly Rgba HeadFoot = Rgba.Hex("#1b1c15");
    internal static readonly Rgba RowBg = Rgba.Hex("#24231c");
    internal static readonly Rgba Btn = Rgba.Hex("#2b2924");
    internal static readonly Rgba BtnHover = Rgba.Hex("#3a372e");
    internal static readonly Rgba BtnPressed = Rgba.Hex("#24231c");
    internal static readonly Rgba Selected = Rgba.Hex("#3a3220");
    internal static readonly Rgba OffTrack = Rgba.Hex("#3d3c35");
    internal static readonly Rgba Shadow = new(0, 0, 0, 140);              // rgba(0,0,0,.55)
    // Lines
    internal static readonly Rgba Frame = Rgba.Hex("#4a4536");
    internal static readonly Rgba Divider = Rgba.Hex("#3a372e");
    internal static readonly Rgba Border = Rgba.Hex("#5c5442");
    internal static readonly Rgba BorderHover = Rgba.Hex("#8a7a52");
    internal static readonly Rgba AccentLine = Rgba.Hex("#a57a2c");
    // Text
    internal static readonly Rgba Text = Rgba.Hex("#fff0cc");
    internal static readonly Rgba Text2 = Rgba.Hex("#b8ad94");
    internal static readonly Rgba Value = Rgba.Hex("#c9bc9c");
    internal static readonly Rgba Faint = Rgba.Hex("#8f8670");
    internal static readonly Rgba Heading = Rgba.Hex("#e9c27a");
    internal static readonly Rgba AccentText = Rgba.Hex("#f3cf86");
    internal static readonly Rgba Ink = Rgba.Hex("#1b1406");             // text on accent / tooltip
    // Accent and states
    internal static readonly Rgba Accent = Rgba.Hex("#dc9e33");
    internal static readonly Rgba AccentBorder = Rgba.Hex("#f0b44a");
    internal static readonly Rgba AccentHover = Rgba.Hex("#e8ab42");
    internal static readonly Rgba AccentPressed = Rgba.Hex("#c48a28");
    internal static readonly Rgba On = Rgba.Hex("#5fe07a");
    internal static readonly Rgba Success = Rgba.Hex("#8fd99c");
    internal static readonly Rgba Error = Rgba.Hex("#e07a4f");
    internal static readonly Rgba ErrorText = Rgba.Hex("#ffcbb3");
    internal static readonly Rgba DangerBg = Rgba.Hex("#3a1f17");
    internal static readonly Rgba DangerHover = Rgba.Hex("#4a2619");
    internal static readonly Rgba WarnRowBg = Rgba.Hex("#2a1c15");
    internal static readonly Rgba WarnRowBorder = Rgba.Hex("#8a4a30");
    internal static readonly Rgba DisabledBg = Rgba.Hex("#1f1e18");
    internal static readonly Rgba DisabledBorder = Rgba.Hex("#34322a");
    internal static readonly Rgba DisabledText = Rgba.Hex("#6f6857");
    internal static readonly Rgba Tooltip = Rgba.Hex("#fff0cc");
    // 0.1.2: warning severity, lamp kinds, modal, HUD, canvas, slider
    internal static readonly Rgba WarnBg = Rgba.Hex("#2b2410");
    internal static readonly Rgba Backdrop = new(0, 0, 0, 140);            // modal dim rgba(0,0,0,.55)
    internal static readonly Rgba HudBg = new(20, 21, 15, 209);            // #14150f at .82
    internal static readonly Rgba CanvasBg = Rgba.Hex("#1b1c15");
    internal static readonly Rgba SliderTrack = Rgba.Hex("#3d3c35");
    internal static readonly Rgba SwatchOutline = Rgba.Hex("#8f8670");
    // 0.1.5: scrollbar (rest/hover/drag colours reuse Border/Text2/Accent; track is a subtle RowBg)
    internal static readonly Rgba ScrollThumb = Border;
    internal static readonly Rgba ScrollThumbHover = Text2;
    internal static readonly Rgba ScrollThumbDrag = Accent;
    internal static readonly Rgba ScrollTrack = new(36, 35, 28, 140);
    // 0.1.6: table (header and zebra sit a step above the panel; selection/hover reuse the list look)
    internal static readonly Rgba TableRowBg = Panel;
    internal static readonly Rgba TableZebra = HeadFoot;
    internal static readonly Rgba TableHeaderBg = HeadFoot;
    internal static readonly Rgba TableHeaderLine = Border;
    // 0.1.7: charts (grid a step under the axes; refline neutral; bars muted until hovered)
    internal static readonly Rgba ChartGrid = Divider;
    internal static readonly Rgba ChartAxis = Border;
    internal static readonly Rgba ChartTick = Faint;
    internal static readonly Rgba ChartRef = Text2;
    internal static readonly Rgba ChartBar = new(92, 84, 66, 200);         // Border at .78
    internal static readonly Rgba ChartBarBorder = Border;
    internal static readonly Rgba ChartBarHover = new(184, 173, 148, 70);  // Text2 at .27
    internal static readonly Rgba ChartStopped = Error;
    internal static readonly Rgba ChartChip = new(43, 41, 36, 235);        // Btn at .92
    // 0.1.8: radial progress (track is the muted neutral of the switch track)
    internal static readonly Rgba RadialTrack = Rgba.Hex("#3d3c35");
    /// <summary>Fixed series order without an explicit colour: Accent, Success, Error, Text2.</summary>
    internal static Rgba ChartSeries(int index) => index switch { 1 => Success, 2 => Error, 3 => Text2, _ => Accent };


    // Metrics (reference px)
    internal const float RadiusPanel = 6, Radius = 4, RadiusSmall = 3, RadiusChip = 10;
    internal const float HeaderH = 44, FooterH = 32, ButtonH = 36, ButtonCompactH = 28, ButtonPadX = 16, ButtonCompactPadX = 10;
    internal const float FieldH = 34, TextFieldH = 32, StepperBtn = 32, StepperValueW = 72, StepperCompactBtn = 30, StepperCompactValueW = 56;
    internal const float ToggleW = 44, ToggleH = 24, Knob = 18, TabH = 36, TabPadX = 16, RowH = 36, RowTallH = 40;
    internal const float ColumnPadX = 14, ColumnPadY = 14, ColumnGap = 10, SectionGap = 4, BlockPad = 12, BlockGap = 8;
    internal const float IconButton = 32, Lamp = 10, Progress = 4, TooltipMaxW = 280, PopupItemH = 31, PopupMaxItems = 8;
    // 0.1.2
    internal const float SliderH = 32, SliderTrackH = 4, SliderKnob = 14, SliderValueW = 64;
    internal const float ErrorLineH = 16, ModalW = 440, ModalPad = 16, ModalGap = 12, HudPad = 10;
    internal const float StripeW = 3, SwatchW = 10, SwatchH = 22;
    internal const int HudSortingOrder = 80;
    // 0.1.4: maximize margin, title bar button gap, resize grip
    internal const float MaximizeMargin = 8, TitleButtonGap = 4, GripIcon = 12, GripInset = 3;
    // 0.1.5: scrollbar width at rest and on hover/drag, click strip, minimum thumb length
    internal const float ScrollW = 3, ScrollWideW = 6, ScrollHitW = 10, ScrollMinH = 20;
    // 0.1.6: table row/header metrics, cell padding, column gap, fill minimum
    internal const float TableRowH = 32, TableCellPadX = 10, TableCellPadY = 6, TableColumnGap = 12;
    internal const float TableHeaderPadY = 8, TableHeaderLineH = 16, TableMinCol = 40, TableRadius = 2;
    // Type sizes (reference px)
    internal const float FontTitle = 20, FontSection = 15, FontLabel = 14, FontValue = 13, FontSmall = 12, FontTag = 11;
}

/// <summary>Button appearance variants.</summary>
public enum ButtonStyle { Normal, Primary, Quiet, Danger }
/// <summary>Status message severity.</summary>
public enum StatusKind { Info, Success, Warning, Error }
/// <summary>Chip appearance variants.</summary>
public enum ChipStyle { Context, Tag, Accent, Neutral }
/// <summary>Text typography roles.</summary>
public enum TextStyle { Title, Section, Label, Value, Small, Faint, BlockTitle, Heading, Keyword, Body }
/// <summary>Built-in UI glyphs.</summary>
public enum Icon { None, Close, Trash, Plus, Minus, ChevronDown, ChevronUp, ChevronRight, ArrowRight, Check, Warning, Help, Hinge, Dot, ChevronLeft, Maximize, Restore, ResizeGrip }

/// <summary>One dropdown option; consecutive options with the same <see cref="Group"/> get one group heading.</summary>
public readonly record struct Choice(string Label, string? Group = null);

/// <summary>Colour states (pure, tested): fill, border, text.</summary>
internal static class Look
{
    internal static (Rgba Fill, Rgba Border, Rgba Text) Button(ButtonStyle style, bool enabled, bool hover, bool pressed)
    {
        if (!enabled) return (Tok.DisabledBg, Tok.DisabledBorder, Tok.DisabledText);
        return style switch
        {
            ButtonStyle.Primary => (pressed ? Tok.AccentPressed : hover ? Tok.AccentHover : Tok.Accent, Tok.AccentBorder, Tok.Ink),
            ButtonStyle.Quiet => (pressed ? Tok.BtnPressed : hover ? Tok.Btn : Rgba.Clear, hover || pressed ? Tok.BorderHover : Tok.Border, Tok.Text),
            ButtonStyle.Danger => (pressed ? Tok.DangerBg : hover ? Tok.DangerHover : Tok.DangerBg, Tok.Error, Tok.ErrorText),
            _ => (pressed ? Tok.BtnPressed : hover ? Tok.BtnHover : Tok.Btn, hover || pressed ? Tok.BorderHover : Tok.Border, Tok.Text)
        };
    }
    internal static Rgba Status(StatusKind kind) => kind switch
    {
        StatusKind.Success => Tok.Success,
        StatusKind.Warning => Tok.AccentText,
        StatusKind.Error => Tok.ErrorText,
        _ => Tok.Text2
    };
    /// <summary>Warning row by severity (0.1.2): background, border, text, icon colour.</summary>
    internal static (Rgba Bg, Rgba Border, Rgba Text, Rgba Icon) Warning(StatusKind kind) => kind switch
    {
        StatusKind.Warning => (Tok.WarnBg, Tok.AccentLine, Tok.AccentText, Tok.Accent),
        StatusKind.Success => (Tok.RowBg, Tok.Divider, Tok.Text, Tok.Success),
        StatusKind.Info => (Tok.RowBg, Tok.Divider, Tok.Text, Tok.Text2),
        _ => (Tok.WarnRowBg, Tok.WarnRowBorder, Tok.ErrorText, Tok.Error)
    };
    /// <summary>Lit lamp colour per kind (0.1.2).</summary>
    internal static Rgba Lamp(LampKind kind) => kind switch { LampKind.Warning => Tok.Accent, LampKind.Error => Tok.Error, _ => Tok.On };
    internal static Icon StatusIcon(StatusKind kind) => kind switch
    {
        StatusKind.Success => Icon.Check,
        StatusKind.Warning or StatusKind.Error => Icon.Warning,
        _ => Icon.None
    };
    /// <summary>Chip look: radius, fill, border (Clear = none), text colour, font size, bold, padX, height, letter spacing.</summary>
    internal static (float Radius, Rgba Fill, Rgba Border, Rgba Text, float Size, bool Bold, float PadX, float H, float Spacing) Chip(ChipStyle s) => s switch
    {
        ChipStyle.Context => (Tok.RadiusChip, Tok.Btn, Tok.Border, Tok.Heading, Tok.FontSmall, true, 10, 20, 4),
        ChipStyle.Tag => (Tok.RadiusSmall, Tok.Btn, Rgba.Clear, Tok.Value, Tok.FontTag, false, 7, 17, 6),
        ChipStyle.Accent => (Tok.RadiusSmall, Tok.Selected, Tok.AccentLine, Tok.AccentText, Tok.FontValue, true, 10, 23, 0),
        _ => (Tok.RadiusSmall, Tok.Btn, Tok.Border, Tok.Text, Tok.FontValue, true, 10, 23, 0)
    };
    /// <summary>Dropdown popup rows: a heading for each new group, then the options (Index = choice index, -1 for headings).</summary>
    internal static List<(bool Heading, string Label, int Index)> PopupRows(IReadOnlyList<Choice> choices)
    {
        var rows = new List<(bool, string, int)>();
        string? group = null;
        for (int i = 0; i < choices.Count; i++)
        {
            var g = choices[i].Group;
            if (!string.IsNullOrEmpty(g) && g != group) rows.Add((true, g!, -1));
            group = g;
            rows.Add((false, choices[i].Label, i));
        }
        return rows;
    }
}
/// <summary>Contexts in which a panel or HUD may appear.</summary>
[Flags] public enum UiContexts { None = 0, Designer = 1, Game = 2, Other = 4, DesignerAndGame = Designer | Game, All = Designer | Game | Other }
