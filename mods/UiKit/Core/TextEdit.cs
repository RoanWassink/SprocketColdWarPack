namespace SprocketUiKit;

/// <summary>
/// Single-line text editing state, driven by Input System key polling (IMGUI key events do not
/// arrive in Sprocket 0.2.56.2). Caret is an index into <see cref="Text"/>; Ctrl+A selects all and
/// the next edit replaces the selection.
/// </summary>
internal sealed class TextEdit
{
    internal string Text { get; private set; } = "";
    internal string Original { get; private set; } = "";
    internal int Caret { get; private set; }
    internal bool AllSelected { get; private set; }
    internal int MaxLength { get; set; } = 64;
    internal Func<char, bool>? Filter { get; set; }
    internal bool Changed => Text != Original;
    /// <summary>
    /// Called once per edit that changed the text (each typed character, Backspace, Delete), with the
    /// new text (0.1.4, #16: per keystroke instead of once per frame). Not called by <see cref="Revert"/>.
    /// </summary>
    internal Action<string>? OnEdit { get; set; }

    internal void Begin(string text)
    {
        Original = text ?? "";
        Text = Original.Length > MaxLength ? Original[..MaxLength] : Original;
        Caret = Text.Length; AllSelected = false;
    }
    internal void SelectAll() { AllSelected = Text.Length > 0; Caret = Text.Length; }
    private bool ReplaceSelection()
    {
        if (!AllSelected) return false;
        Text = ""; Caret = 0; AllSelected = false; return true;
    }
    /// <summary>Insert typed characters; rejected characters and overflow are ignored.</summary>
    internal bool Insert(string chars)
    {
        bool changed = ReplaceSelection();
        foreach (var c in chars)
        {
            if (char.IsControl(c) || Filter != null && !Filter(c) || Text.Length >= MaxLength) continue;
            Text = Text.Insert(Caret, c.ToString()); Caret++; changed = true;
        }
        return Edited(changed);
    }
    internal bool Insert(char c) => Insert(c.ToString());
    internal bool Backspace()
    {
        if (ReplaceSelection()) return Edited(true);
        if (Caret == 0) return false;
        Text = Text.Remove(Caret - 1, 1); Caret--; return Edited(true);
    }
    internal bool Delete()
    {
        if (ReplaceSelection()) return Edited(true);
        if (Caret >= Text.Length) return false;
        Text = Text.Remove(Caret, 1); return Edited(true);
    }
    private bool Edited(bool changed) { if (changed) OnEdit?.Invoke(Text); return changed; }
    internal void Left() { if (AllSelected) { AllSelected = false; Caret = 0; return; } Caret = Math.Max(0, Caret - 1); }
    internal void Right() { AllSelected = false; Caret = Math.Min(Text.Length, Caret + 1); }
    internal void Home() { AllSelected = false; Caret = 0; }
    internal void End() { AllSelected = false; Caret = Text.Length; }
    /// <summary>Esc: back to the text at Begin.</summary>
    internal void Revert() { Text = Original; Caret = Text.Length; AllSelected = false; }
}

/// <summary>
/// Physical key (Input System <c>Key</c> enum name) to typed character on a US layout, the layout
/// the Input System's key enum is defined against. Pure table so it can be tested outside Unity.
/// </summary>
internal static class KeyChars
{
    private static readonly Dictionary<string, (char Plain, char Shift)> Map = Build();
    private static Dictionary<string, (char, char)> Build()
    {
        var m = new Dictionary<string, (char, char)>(StringComparer.OrdinalIgnoreCase);
        for (char c = 'a'; c <= 'z'; c++) m[c.ToString()] = (c, char.ToUpperInvariant(c));
        const string shifted = ")!@#$%^&*(";
        for (int d = 0; d <= 9; d++) { m["Digit" + d] = ((char)('0' + d), shifted[d]); m["Numpad" + d] = ((char)('0' + d), (char)('0' + d)); }
        m["Space"] = (' ', ' ');
        m["Minus"] = ('-', '_'); m["Equals"] = ('=', '+');
        m["LeftBracket"] = ('[', '{'); m["RightBracket"] = (']', '}'); m["Backslash"] = ('\\', '|');
        m["Semicolon"] = (';', ':'); m["Quote"] = ('\'', '"'); m["Backquote"] = ('`', '~');
        m["Comma"] = (',', '<'); m["Period"] = ('.', '>'); m["Slash"] = ('/', '?');
        m["NumpadPeriod"] = ('.', '.'); m["NumpadPlus"] = ('+', '+'); m["NumpadMinus"] = ('-', '-');
        m["NumpadMultiply"] = ('*', '*'); m["NumpadDivide"] = ('/', '/');
        return m;
    }
    internal static IEnumerable<string> KeyNames => Map.Keys;
    internal static char? Char(string keyName, bool shift) =>
        Map.TryGetValue(keyName, out var v) ? shift ? v.Shift : v.Plain : null;
}
