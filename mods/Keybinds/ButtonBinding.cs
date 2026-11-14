using System.Text.Json.Nodes;

namespace SprocketKeybinds;

[Flags]
internal enum Modifiers { None = 0, Shift = 1, Ctrl = 2, Alt = 4 }

// Pure format shared by registration, capture, conflicts, persistence and arbitration.
internal sealed record ButtonBinding(string Path, Modifiers Modifiers)
{
    internal static Modifiers Modifier(string name) => name.ToLowerInvariant() switch
    {
        "shift" or "leftshift" or "rightshift" => Modifiers.Shift,
        "ctrl" or "control" or "leftctrl" or "rightctrl" => Modifiers.Ctrl,
        "alt" or "leftalt" or "rightalt" => Modifiers.Alt,
        _ => Modifiers.None
    };
    internal static ButtonBinding Parse(string value)
    {
        if (value == null) throw new ArgumentNullException(nameof(value));
        if (value == "") return new("", Modifiers.None);
        var parts = value.Split('+', StringSplitOptions.TrimEntries);
        Modifiers modifiers = Modifiers.None;
        foreach (var part in parts.SkipLast(1))
        {
            var modifier = Modifier(part.Replace("<Keyboard>/", "", StringComparison.OrdinalIgnoreCase));
            if (modifier == Modifiers.None || (modifiers & modifier) != 0)
                throw new ArgumentException("Use distinct Shift, Ctrl and Alt modifiers before a concrete button path.");
            modifiers |= modifier;
        }
        var path = parts[^1];
        if ((!path.StartsWith("<Keyboard>/", StringComparison.OrdinalIgnoreCase) && !path.StartsWith("<Mouse>/", StringComparison.OrdinalIgnoreCase))
            || path.EndsWith('/') || path.Substring(path.IndexOf('/') + 1).Any(c => char.IsControl(c) || char.IsWhiteSpace(c) || c is '*' or '<' or '>')
            || path.Count(c => c == '/') != 1)
            throw new ArgumentException("Use a concrete <Keyboard>/... or <Mouse>/... button path, optionally prefixed by Shift+Ctrl+Alt+.");
        if (modifiers != Modifiers.None && path.StartsWith("<Keyboard>/", StringComparison.OrdinalIgnoreCase) && Modifier(path.Split('/')[1]) != Modifiers.None)
            throw new ArgumentException("Use a non-modifier main key for a combo; a modifier alone remains supported.");
        return new(path.ToLowerInvariant(), modifiers);
    }
    internal string Prefix => string.Concat(new[] { Modifiers.Shift, Modifiers.Ctrl, Modifiers.Alt }.Where(m => (Modifiers & m) != 0).Select(m => m + "+"));
    internal string Canonical => Path == "" ? "" : Prefix + Path;
    internal int Specificity => System.Numerics.BitOperations.PopCount((uint)Modifiers);
    internal bool Matches(Modifiers held) => (held & Modifiers) == Modifiers;
    internal string Display => Prefix + (Path == "" ? "Unbound" : Path.Split('/')[1] switch
    {
        "leftshift" => "Left Shift", "rightshift" => "Right Shift",
        "leftctrl" => "Left Ctrl", "rightctrl" => "Right Ctrl",
        "leftalt" => "Left Alt", "rightalt" => "Right Alt",
        var key => key.Length == 1 ? key.ToUpperInvariant() : char.ToUpperInvariant(key[0]) + key[1..]
    });
    internal static bool Wins(ButtonBinding binding, IEnumerable<ButtonBinding> registered, Modifiers held)
        => binding.Path != "" && binding.Matches(held) && !registered.Any(other =>
            other.Path.Equals(binding.Path, StringComparison.OrdinalIgnoreCase) && other.Matches(held) && other.Specificity > binding.Specificity);
}

// No upgrades while a main key is held: modifier-first is required, even after release/repress of a modifier.
internal sealed class BindingLatch
{
    private bool armed, held;
    internal (bool Held, bool Pressed, bool Released) Step(ButtonBinding binding, bool down, bool pressed, Modifiers modifiers,
        IReadOnlyCollection<ButtonBinding> all, bool allowed = true, Modifiers? pressModifiers = null, Modifiers? retainedModifiers = null)
    {
        bool previous = held;
        bool fires = allowed && pressed && ButtonBinding.Wins(binding, all, pressModifiers ?? modifiers);
        if (!allowed || !down) armed = false;
        if (fires && down) armed = true;
        if (!binding.Matches(modifiers) || !binding.Matches(retainedModifiers ?? modifiers)) armed = false;
        held = allowed && down && (binding.Modifiers == Modifiers.None || armed) && ButtonBinding.Wins(binding, all, modifiers);
        return (held, fires, previous && !held || fires && !held);
    }
    internal static (bool Held, bool Pressed, bool Released) Combine(bool wasHeld,
        IReadOnlyCollection<(bool Held, bool Pressed, bool Released)> slots, bool nativePressed, bool allowed)
    {
        if (!allowed) return (false, false, false);
        bool held = slots.Any(s => s.Held), pressed = slots.Any(s => s.Pressed), released = slots.Any(s => s.Released);
        return (held, pressed && (!wasHeld || nativePressed), !held && (wasHeld || released));
    }
}

internal sealed class ButtonCapture
{
    private string? pendingModifier;
    internal bool Cancelled { get; private set; }
    internal string? Result { get; private set; }
    internal void Event(string path, bool down, Modifiers modifiers)
    {
        if (Result != null || Cancelled) return;
        var binding = ButtonBinding.Parse(path);
        if (binding.Path == "<keyboard>/escape" && down) { Cancelled = true; return; }
        if (binding.Path.StartsWith("<keyboard>/") && ButtonBinding.Modifier(binding.Path.Split('/')[1]) != Modifiers.None)
        {
            if (down) pendingModifier = binding.Path;
            else if (pendingModifier == binding.Path) Result = binding.Path;
        }
        else if (down) Result = new ButtonBinding(binding.Path, modifiers).Canonical;
    }
}

// Additive extension inside each existing opaque Unity override record. Native paths stay ordinary paths.
internal static class ComboRecord
{
    internal const string Field = "sprocketCombos";
    internal static (string Native, Dictionary<string, string> Combos) Read(string json)
    {
        // 0.1.7 stores "" for actions without overrides; it goes to the native loader unchanged.
        if (string.IsNullOrWhiteSpace(json)) return (json, new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase));
        var root = JsonNode.Parse(json)?.AsObject() ?? throw new InvalidDataException("Empty override record.");
        var combos = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (root.TryGetPropertyValue(Field, out var extension))
        {
            if (extension is not JsonObject values) throw new InvalidDataException("Invalid combo overrides.");
            foreach (var value in values)
            {
                if (!Guid.TryParse(value.Key, out _) || value.Value == null) throw new InvalidDataException("Invalid combo binding ID.");
                var binding = ButtonBinding.Parse(value.Value.GetValue<string>());
                if (binding.Modifiers == Modifiers.None) throw new InvalidDataException("Combo extension needs modifiers.");
                combos.Add(value.Key, binding.Canonical);
            }
            root.Remove(Field);
        }
        return (root.ToJsonString(), combos);
    }
    internal static string Write(string native, IReadOnlyDictionary<string, string> combos)
    {
        if (combos.Count == 0) return native;
        var root = JsonNode.Parse(native)!.AsObject();
        var extension = new JsonObject();
        foreach (var combo in combos.OrderBy(p => p.Key, StringComparer.Ordinal)) extension[combo.Key] = combo.Value;
        root[Field] = extension;
        return root.ToJsonString();
    }
}

internal sealed class PreservedRecord
{
    private string? original, nativeSnapshot, comboSnapshot;
    internal void Remember(string json, string native, IReadOnlyDictionary<string, string> combos)
    { original = json; nativeSnapshot = native; comboSnapshot = Signature(combos); }
    internal string Save(string native, IReadOnlyDictionary<string, string> combos)
        => original != null && native == nativeSnapshot && Signature(combos) == comboSnapshot ? original : ComboRecord.Write(native, combos);
    private static string Signature(IReadOnlyDictionary<string, string> combos) => System.Text.Json.JsonSerializer.Serialize(combos.OrderBy(p => p.Key, StringComparer.Ordinal));
}

internal static class FreeKeys
{
    internal static Dictionary<string, string?> ReadNativeOverrides(string json)
    {
        var result = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrWhiteSpace(json)) return result;
        using var document = System.Text.Json.JsonDocument.Parse(json);
        foreach (var binding in document.RootElement.GetProperty("bindings").EnumerateArray())
        {
            var id = binding.GetProperty("id").GetString();
            if (!Guid.TryParse(id, out _)) throw new InvalidDataException("Invalid vanilla override binding ID.");
            string? path = binding.GetProperty("path").GetString();
            result.Add(id!, path == "null" ? null : path);
        }
        return result;
    }
    internal sealed record NativeBinding(string Path, bool IsComposite = false, bool IsPart = false);
    internal static IEnumerable<string> SinglePaths(IEnumerable<NativeBinding> bindings)
    {
        bool modifierComposite = false;
        foreach (var binding in bindings)
        {
            if (binding.IsComposite)
            {
                string type = binding.Path.Split('(')[0];
                modifierComposite = new[] { "OneModifier", "TwoModifiers", "ButtonWithOneModifier", "ButtonWithTwoModifiers" }
                    .Contains(type, StringComparer.OrdinalIgnoreCase);
                continue;
            }
            if (!binding.IsPart) modifierComposite = false;
            if ((!binding.IsPart || !modifierComposite) && binding.Path != "") yield return binding.Path;
        }
    }
    internal static readonly string[][] Groups = {
        Enumerable.Range('a', 26).Select(c => ((char)c).ToString()).ToArray(),
        Enumerable.Range(0, 10).Select(i => i.ToString()).ToArray(),
        Enumerable.Range(1, 12).Select(i => "f" + i).ToArray(),
        Enumerable.Range(0, 10).Select(i => "numpad" + i).Concat(new[] { "numpaddivide", "numpadmultiply", "numpadminus", "numpadplus", "numpadperiod",
            "backquote", "minus", "equals", "leftbracket", "rightbracket", "backslash", "semicolon", "quote", "comma", "period", "slash",
            "insert", "delete", "home", "end", "pageup", "pagedown", "uparrow", "downarrow", "leftarrow", "rightarrow" }).ToArray()
    };
    internal static string[] Calculate(IEnumerable<string> vanilla, IEnumerable<ButtonBinding> mods) => Calculate(vanilla, mods, true);
    internal static string[] Calculate(IEnumerable<string> vanilla, IEnumerable<ButtonBinding> mods, bool vanillaKnown)
    {
        if (!vanillaKnown) return Array.Empty<string>();
        var used = new HashSet<string>(vanilla.Select(p => p.ToLowerInvariant()), StringComparer.OrdinalIgnoreCase);
        used.UnionWith(mods.Where(b => b.Modifiers == Modifiers.None).Select(b => b.Path));
        return Groups.SelectMany(g => g).Where(key => !used.Contains("<Keyboard>/" + key)).ToArray();
    }
}
