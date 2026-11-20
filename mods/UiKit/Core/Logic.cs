using System.Globalization;
namespace SprocketUiKit;

/// <summary>Screen corner (or edge centre) a HUD is anchored to.</summary>
/// <summary>BottomCentre UI data and behavior.</summary>
public enum HudCorner { TopLeft, TopRight, BottomLeft, BottomRight, TopCentre, BottomCentre }
/// <summary>Lamp colour: on (green), warning (amber), error (red).</summary>
/// <summary>Error UI data and behavior.</summary>
public enum LampKind { On, Warning, Error }

/// <summary>What Esc does, in priority order (pure, tested).</summary>
internal enum EscTarget { None, TextField, Popup, Modal, Page, Panel }
/// <summary>What a key does in a modal (pure, tested).</summary>
internal enum ModalKey { None, Confirm, Cancel }

/// <summary>Pure logic for the 0.1.2 features (slider, pages, Esc order, modal keys, HUD anchors, shortcuts).</summary>
internal static class Logic
{
    // ---------- Slider ----------
    /// <summary>Snap to the step grid (anchored at min when finite) and clamp; step &lt;= 0 only clamps.</summary>
    internal static double Snap(double value, double min, double max, double step)
    {
        if (double.IsNaN(value)) return value;
        if (max < min) (min, max) = (max, min);
        if (step > 0 && double.IsFinite(step))
        {
            double anchor = double.IsFinite(min) ? min : 0;
            value = anchor + Math.Round(Math.Round((value - anchor) / step, 9), MidpointRounding.AwayFromZero) * step;
            value = KitMath.Clean(Math.Clamp(value, min, max), step);
        }
        return Math.Clamp(value, min, max);
    }
    /// <summary>Knob position 0..1 for a value.</summary>
    internal static double SliderFraction(double value, double min, double max) =>
        max > min && double.IsFinite(value) ? Math.Clamp((value - min) / (max - min), 0, 1) : 0;
    /// <summary>Value for a click/drag position on the track (fraction 0..1), snapped.</summary>
    internal static double SliderAt(double fraction, double min, double max, double step) =>
        Snap(min + Math.Clamp(fraction, 0, 1) * (max - min), min, max, step);
    /// <summary>Relative drag: <paramref name="dx"/> units on a track of <paramref name="trackW"/>; fine (Shift) moves 10× slower.</summary>
    internal static double SliderDrag(double start, float dx, float trackW, double min, double max, double step, bool fine) =>
        Snap(start + dx / Math.Max(1f, trackW) * (max - min) * (fine ? .1 : 1), min, max, step);
    /// <summary>Wheel: one step per notch (at least one), coarse (Shift) = 10 steps.</summary>
    internal static double SliderWheel(double value, float notches, double min, double max, double step, bool coarse)
    {
        int n = (int)MathF.Round(notches); if (n == 0) n = notches > 0 ? 1 : -1;
        double s = (step > 0 ? step : (max - min) / 100) * (coarse ? 10 : 1);
        if (double.IsNaN(value)) return value;
        value = Snap(value, min, max, step) + n * s;
        return Snap(value, min, max, step);
    }
    /// <summary>Parse a typed number (invariant; a comma counts as decimal point).</summary>
    internal static bool TryParseNumber(string? text, out double value) =>
        double.TryParse((text ?? "").Trim().Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out value) && double.IsFinite(value);

    // ---------- Rows ----------
    /// <summary>
    /// Row shrink-to-fit: when fixed widths (&gt;= 0) plus gaps exceed <paramref name="available"/>,
    /// shrink the <paramref name="shrinkable"/> ones proportionally (not below <paramref name="min"/>).
    /// Flexible entries (&lt; 0) stay as they are.
    /// </summary>
    internal static float[] ShrinkWidths(float available, float gap, IReadOnlyList<float> intrinsic, IReadOnlyList<bool> shrinkable, float min = 24)
    {
        var w = intrinsic.ToArray();
        float used = gap * Math.Max(0, w.Length - 1), room = 0;
        for (int i = 0; i < w.Length; i++) if (w[i] >= 0) { used += w[i]; if (shrinkable[i]) room += MathF.Max(0, w[i] - min); }
        float over = used - available;
        if (over <= 0 || room <= 0) return w;
        float k = MathF.Min(1, over / room);
        for (int i = 0; i < w.Length; i++) if (w[i] >= 0 && shrinkable[i]) w[i] -= MathF.Max(0, w[i] - min) * k;
        return w;
    }

    // ---------- Keys ----------
    /// <summary>Esc order: text field, dropdown, modal, sub-page (back), panel (close).</summary>
    internal static EscTarget Esc(bool typing, bool popup, bool modal, int pageDepth, bool panelOpen) =>
        typing ? EscTarget.TextField : popup ? EscTarget.Popup : modal ? EscTarget.Modal : pageDepth > 0 ? EscTarget.Page : panelOpen ? EscTarget.Panel : EscTarget.None;
    /// <summary>
    /// Modal keys: Esc always cancels. Enter presses the default button: confirm, except for a
    /// Danger confirmation, where the safe default (cancel) is used.
    /// </summary>
    internal static ModalKey ConfirmKey(bool enter, bool esc, bool danger) =>
        esc ? ModalKey.Cancel : enter ? (danger ? ModalKey.Cancel : ModalKey.Confirm) : ModalKey.None;
    /// <summary>
    /// User-initiated close (Esc, close button, toggle key, <c>Panel.RequestClose()</c>, 0.1.4): never when
    /// not closable; otherwise <paramref name="request"/> decides (null or an exception = close).
    /// </summary>
    internal static bool MayClose(bool closable, Func<bool>? request, string where) =>
        closable && (request == null || Guard.Get(where, request, true));

    // ---------- HUD ----------
    /// <summary>HUD rectangle in view units for a corner anchor; offsets point inwards; kept on screen.</summary>
    internal static RectF HudRect(HudCorner corner, float offX, float offY, float w, float h, float viewW, float viewH)
    {
        float x = corner switch
        {
            HudCorner.TopRight or HudCorner.BottomRight => viewW - w - offX,
            HudCorner.TopCentre or HudCorner.BottomCentre => (viewW - w) / 2 + offX,
            _ => offX
        };
        float y = corner is HudCorner.BottomLeft or HudCorner.BottomRight or HudCorner.BottomCentre ? viewH - h - offY : offY;
        x = Math.Clamp(x, 0, MathF.Max(0, viewW - w)); y = Math.Clamp(y, 0, MathF.Max(0, viewH - h));
        return new RectF(x, y, w, h);
    }
    /// <summary>HUD fade: alpha moves towards the target at 1/<paramref name="seconds"/> per second.</summary>
    internal static float Fade(float alpha, bool show, float seconds, float dt)
    {
        float target = show ? 1 : 0;
        if (seconds <= 0) return target;
        float step = dt / seconds;
        return alpha < target ? MathF.Min(target, alpha + step) : MathF.Max(target, alpha - step);
    }

    // ---------- Shortcuts ----------
    /// <summary>Parsed key chord: Input System key name plus modifiers.</summary>
    internal readonly record struct Chord(string Key, bool Ctrl, bool Shift, bool Alt)
    {
        public override string ToString() => (Ctrl ? "Ctrl+" : "") + (Shift ? "Shift+" : "") + (Alt ? "Alt+" : "") + Display(Key);
        private static string Display(string k) => k.StartsWith("Digit", StringComparison.Ordinal) ? k[5..] : k switch { "UpArrow" => "Up", "DownArrow" => "Down", "LeftArrow" => "Left", "RightArrow" => "Right", "Delete" => "Del", _ => k };
    }
    private static readonly Dictionary<string, string> KeyAliases = new(StringComparer.OrdinalIgnoreCase)
    {
        ["del"] = "Delete", ["delete"] = "Delete", ["ins"] = "Insert", ["insert"] = "Insert", ["enter"] = "Enter", ["return"] = "Enter",
        ["up"] = "UpArrow", ["down"] = "DownArrow", ["left"] = "LeftArrow", ["right"] = "RightArrow",
        ["uparrow"] = "UpArrow", ["downarrow"] = "DownArrow", ["leftarrow"] = "LeftArrow", ["rightarrow"] = "RightArrow",
        ["pgup"] = "PageUp", ["pageup"] = "PageUp", ["pgdn"] = "PageDown", ["pagedown"] = "PageDown", ["home"] = "Home", ["end"] = "End",
        ["space"] = "Space", ["backspace"] = "Backspace", ["tab"] = "Tab",
        ["plus"] = "Equals", ["minus"] = "Minus", ["comma"] = "Comma", ["period"] = "Period", ["slash"] = "Slash",
    };
    /// <summary>
    /// Parse "Ctrl+D", "Delete", "Alt+UpArrow", "Shift+F5", "Ctrl+1". Esc is reserved for the kit
    /// and rejected, as are unknown keys and chords without a key.
    /// </summary>
    internal static bool TryParseChord(string? text, out Chord chord)
    {
        chord = default;
        if (string.IsNullOrWhiteSpace(text)) return false;
        bool ctrl = false, shift = false, alt = false; string? key = null;
        foreach (var raw in text.Split('+', StringSplitOptions.TrimEntries))
        {
            if (raw.Length == 0) return false;
            switch (raw.ToLowerInvariant())
            {
                case "ctrl": case "control": ctrl = true; continue;
                case "shift": shift = true; continue;
                case "alt": alt = true; continue;
            }
            if (key != null) return false;
            if (raw.Equals("esc", StringComparison.OrdinalIgnoreCase) || raw.Equals("escape", StringComparison.OrdinalIgnoreCase)) return false;
            if (raw.Length == 1 && char.IsLetter(raw[0])) key = raw.ToUpperInvariant();
            else if (raw.Length == 1 && char.IsDigit(raw[0])) key = "Digit" + raw;
            else if (KeyAliases.TryGetValue(raw, out var a)) key = a;
            else if (raw.Length >= 2 && (raw[0] == 'F' || raw[0] == 'f') && int.TryParse(raw[1..], out int f) && f >= 1 && f <= 12) key = "F" + f;
            else return false;
        }
        if (key == null) return false;
        chord = new Chord(key, ctrl, shift, alt);
        return true;
    }
    /// <summary>Exact modifier match: Ctrl+D does not fire on Ctrl+Shift+D, D does not fire on Ctrl+D.</summary>
    internal static bool ChordMatches(Chord c, bool ctrl, bool shift, bool alt) => c.Ctrl == ctrl && c.Shift == shift && c.Alt == alt;
}

/// <summary>Panel sub-page history (root = depth 0). Pure, tested.</summary>
internal sealed class PageStack
{
    private readonly List<string> stack = new();
    /// <summary>Current page id, or null for the root page (panel Body).</summary>
    internal string? Current => stack.Count > 0 ? stack[^1] : null;
    internal int Depth => stack.Count;
    internal IReadOnlyList<string> Path => stack;
    /// <summary>Go to a page: no-op when it is the current page; an id already in the trail truncates back to it; null = root.</summary>
    internal bool Navigate(string? id)
    {
        if (id == null) { if (stack.Count == 0) return false; stack.Clear(); return true; }
        if (Current == id) return false;
        int at = stack.IndexOf(id);
        if (at >= 0) { stack.RemoveRange(at + 1, stack.Count - at - 1); return true; }
        stack.Add(id); return true;
    }
    /// <summary>One page back; false at the root.</summary>
    internal bool Back() { if (stack.Count == 0) return false; stack.RemoveAt(stack.Count - 1); return true; }
    internal void Remove(string id) { int at = stack.IndexOf(id); if (at >= 0) stack.RemoveRange(at, stack.Count - at); }
}
