using System.Text.Json;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;
using TMPro;
using Object = UnityEngine.Object;

namespace SprocketKeybinds;

/// <summary>Shared API. Register on Unity's main thread after the required BepInEx dependency has loaded.</summary>
public static class Keybinds
{
    public const string PluginGuid = "sprocket.keybinds";
    private static readonly Dictionary<string, ModKeybind> entries = new(StringComparer.Ordinal);
    internal static readonly ModMetadata Metadata = new();
    /// <summary>Optional display attribution; available from 0.1.5. Identical repeats are allowed; conflicts throw.</summary>
    public static void RegisterModMetadata(string ownerId, string author, string displayName)
    {
        CheckThread(); Metadata.Register(ownerId, author, displayName); Plugin.Safe(ModPanel.Layout);
    }
    /// <summary>Declares one explicit primary pack for an owner. Pack installers may call this without rebuilding consumers.</summary>
    public static void RegisterPackMembership(string ownerId, string packId, string packDisplayName)
    {
        CheckThread(); Metadata.RegisterPack(ownerId, packId, packDisplayName); Plugin.Safe(ModPanel.Layout);
    }
    internal static BindingStore Store = null!;
    private static int mainThread;
    internal static IEnumerable<ModKeybind> Entries => entries.Values;
    public static bool Ready => Store != null;
    public static bool IsConfiguring => MenuOpen;
    internal static bool MenuOpen => Store != null && Store.Editing && VanillaMenu.IsOpen;
    internal static void Initialize(string path)
    {
        mainThread = Environment.CurrentManagedThreadId;
        var store = new BindingStore(path);
        store.Load(); // On invalid storage, initialization fails without replacing the user's data.
        Store = store;
    }
    internal static void CheckThread()
    {
        if (!Ready) throw new InvalidOperationException("Sprocket Keybinds is not ready. Declare a BepInDependency on Keybinds.PluginGuid.");
        if (Environment.CurrentManagedThreadId != mainThread) throw new InvalidOperationException("Keybinds must be accessed from Unity's main thread.");
    }
    /// <summary>Registers one button with primary and optional secondary binding. IDs must remain stable across releases.</summary>
    public static ModKeybind RegisterButton(string modGuid, string modName, string actionId, string label,
        string defaultPath, string? secondaryPath = null)
    {
        CheckThread();
        var identity = BindingStore.Identity(modGuid, actionId);
        if (entries.ContainsKey(identity)) throw new ArgumentException("Duplicate keybind: " + identity);
        if (string.IsNullOrWhiteSpace(modName) || string.IsNullOrWhiteSpace(label)) throw new ArgumentException("A mod name and action label are required.");
        ValidatePath(defaultPath); if (secondaryPath != null) ValidatePath(secondaryPath);
        var handle = new ModKeybind(identity, modName, label, actionId, defaultPath, secondaryPath);
        try
        {
            if (Store.Values.TryGetValue(identity, out var saved)) handle.Load(saved);
            entries.Add(identity, handle);
            InputRuntime.Invalidate();
            Plugin.LogInfo("Registered " + identity);
            Plugin.Safe(VanillaMenu.Refresh);
            return handle;
        }
        catch { handle.Destroy(); throw; }
    }
    private static void ValidatePath(string path)
    {
        var binding = ButtonBinding.Parse(path);
        if (binding.Path.Length > 0)
        {
            var control = InputSystem.FindControl(binding.Path);
            if (control != null && control.TryCast<UnityEngine.InputSystem.Controls.ButtonControl>() == null)
                throw new ArgumentException("The path must identify a button.");
        }
    }
    /// <summary>Imports one legacy primary binding once. Saved API choices always win; Reset keeps the registered factory default.</summary>
    public static bool TryImportLegacyBinding(ModKeybind binding, string path)
    {
        CheckThread();
        if (binding == null || !entries.TryGetValue(binding.Id, out var registered) || !ReferenceEquals(binding, registered))
            throw new ArgumentException("The handle must be registered with this API.", nameof(binding));
        if (Store.Editing) throw new InvalidOperationException("Import legacy settings outside a menu edit session.");
        if (Store.Values.ContainsKey(binding.Id)) return false;
        ValidatePath(path);
        var previous = binding.Save();
        try
        {
            binding.SetPath(0, path);
            return Store.ImportOnce(binding.Id, binding.Save());
        }
        catch { binding.Load(previous); throw; }
    }
    /// <summary>Migrates both saved override slots before registration; existing target choices win. Available since 0.1.6.</summary>
    public static bool TryMigrateBindingIdentity(string oldOwner, string oldAction, string newOwner, string newAction)
    {
        CheckThread();
        var source = BindingStore.Identity(oldOwner, oldAction);
        var target = BindingStore.Identity(newOwner, newAction);
        if (Store.Editing) throw new InvalidOperationException("Migrate identities outside a menu edit session.");
        if (entries.ContainsKey(source) || entries.ContainsKey(target))
            throw new InvalidOperationException("Migrate identities before registering either action.");
        return Store.MigrateIdentity(source, target);
    }
    /// <summary>Reports exact effective-path overlaps among mod bindings. Contexts may make an overlap intentional.</summary>
    public static IReadOnlyList<BindingConflict> GetConflicts()
    {
        CheckThread();
        var result = new List<BindingConflict>();
        var all = entries.Values.ToArray();
        for (int a = 0; a < all.Length; a++)
            for (int b = a + 1; b < all.Length; b++)
                foreach (var path in all[a].Bindings.Select(p => p.Canonical).Intersect(all[b].Bindings.Select(p => p.Canonical), StringComparer.OrdinalIgnoreCase).Where(p => p.Length > 0))
                    result.Add(new(all[a].Id, all[b].Id, path));
        return result;
    }
    internal static bool InputAllowed
    {
        get
        {
            if (MenuOpen || !Application.isFocused || VanillaMenu.Rebinding) return false;
            var selected = EventSystem.current?.currentSelectedGameObject;
            if (selected != null && (selected.GetComponent<InputField>() != null || selected.GetComponent<TMP_InputField>() != null)) return false;
            return true;
        }
    }
    internal static void Begin()
    {
        if (Store.Editing) End();
        Capture(); Store.Begin();
    }
    private static void Capture()
    {
        foreach (var entry in Entries) Store.Values[entry.Id] = entry.Save();
    }
    internal static void Commit() { Capture(); Store.Commit(); }
    internal static void Rollback()
    {
        Store.Rollback();
        foreach (var entry in Entries) entry.Load(Store.Values.GetValueOrDefault(entry.Id, "{\"bindings\":[]}"));
        VanillaMenu.UpdateLabels();
    }
    internal static void End() { if (Store == null || !Store.Editing) return; Rollback(); Store.End(); }
    public static bool Unregister(string modGuid, string actionId)
    {
        CheckThread();
        if (!entries.Remove(BindingStore.Identity(modGuid, actionId), out var entry)) return false;
        InputRuntime.Invalidate();
        Store.Values[entry.Id] = entry.Save();
        VanillaMenu.Remove(entry); entry.Destroy(); return true;
    }
}

public sealed record BindingConflict(string FirstActionId, string SecondActionId, string Path);

public sealed class ModKeybind
{
    public string Id { get; }
    public string ModName { get; }
    public string Label { get; }
    internal InputActionAsset Asset { get; }
    internal InputAction Action { get; }
    internal InputAction[] Slots { get; }
    internal InputActionReference Reference { get; }
    private bool disposed;
    private readonly ButtonBinding[] defaults;
    private ButtonBinding[] bindings;
    private readonly PreservedRecord recordState = new();
    internal IEnumerable<ButtonBinding> Bindings => bindings;
    internal readonly BindingLatch[] Latches;
    internal (bool Held, bool Pressed, bool Released) State;
    internal ModKeybind(string id, string modName, string label, string actionName, string primary, string? secondary)
    {
        Id = id; ModName = modName; Label = label;
        var paths = secondary == null ? new[] { primary } : new[] { primary, secondary };
        defaults = paths.Select(ButtonBinding.Parse).ToArray();
        bindings = defaults.ToArray();
        Latches = paths.Select(_ => new BindingLatch()).ToArray();
        var json = JsonSerializer.Serialize(new { name = id, maps = new[] { new {
            name = "Mod", id = BindingStore.StableGuid(id, "map"),
            actions = new[] { new { name = actionName, type = "Button", id = BindingStore.StableGuid(id, "action"), expectedControlType = "Button" } }
                .Concat(defaults.Select((_, i) => new { name = actionName + ".slot" + i, type = "Button", id = BindingStore.StableGuid(id, "probeAction" + i), expectedControlType = "Button" })).ToArray(),
            bindings = defaults.Select((p, i) => new { name = i == 0 ? "Primary" : "Secondary", id = BindingStore.StableGuid(id, "binding" + i), path = p.Path, action = actionName, groups = "", interactions = "", processors = "", isComposite = false, isPartOfComposite = false })
                .Concat(defaults.Select((p, i) => new { name = "Slot", id = BindingStore.StableGuid(id, "probe" + i), path = p.Path, action = actionName + ".slot" + i, groups = "", interactions = "", processors = "", isComposite = false, isPartOfComposite = false })).ToArray()
        } } });
        Asset = InputActionAsset.FromJson(json);
        // These are ScriptableObjects, not scene-root GameObjects. Managed plugin references
        // alone must not be relied on as roots for Unity's native unused-asset collection.
        Asset.hideFlags = HideFlags.HideAndDontSave;
        Action = Asset.FindAction(actionName, true);
        Slots = Enumerable.Range(0, paths.Length).Select(i => Asset.FindAction(actionName + ".slot" + i, true)).ToArray();
        Reference = InputActionReference.Create(Action);
        Reference.hideFlags = HideFlags.HideAndDontSave;
        Asset.Enable();
    }
    public bool IsHeld { get { Check(); InputRuntime.Sample(); return Keybinds.InputAllowed && State.Held; } }
    public bool WasPressedThisFrame { get { Check(); InputRuntime.Sample(); return Keybinds.InputAllowed && State.Pressed; } }
    public bool WasReleasedThisFrame { get { Check(); InputRuntime.Sample(); return Keybinds.InputAllowed && State.Released; } }
    public string GetBindingDisplayString(int index = 0)
    {
        Check();
        if (index < 0 || index >= Action.bindings.Count) throw new ArgumentOutOfRangeException(nameof(index));
        return bindings[index].Modifiers != Modifiers.None ? bindings[index].Display : InputActionRebindingExtensions.GetBindingDisplayString(Action, index, default);
    }
    /// <summary>Clears one slot. In the settings menu Apply/Accept saves it and Cancel restores it.</summary>
    public void Unbind(int index = 0)
    {
        Check();
        if (index < 0 || index >= Action.bindings.Count) throw new ArgumentOutOfRangeException(nameof(index));
        SetPath(index, "");
        VanillaMenu.UpdateLabels();
        if (!Keybinds.Store.Editing) Keybinds.Commit();
    }
    internal string[] Paths => bindings.Select(b => b.Canonical).ToArray();
    private Dictionary<string, string> ComboOverrides() => Enumerable.Range(0, bindings.Length)
        .Where(i => Action.bindings[i].overridePath != null && bindings[i].Modifiers != Modifiers.None)
        .ToDictionary(i => Action.bindings[i].id.ToString(), i => bindings[i].Canonical);
    internal string Save()
    {
        var native = InputActionRebindingExtensions.SaveBindingOverridesAsJson(Action);
        var combos = ComboOverrides();
        // Opening settings / changing another owner must not normalize an existing released record.
        return recordState.Save(native, combos);
    }
    internal void Load(string json)
    {
        (string Native, Dictionary<string, string> Combos) record;
        try
        {
            record = ComboRecord.Read(json);
            foreach (var combo in record.Combos)
                if (!Enumerable.Range(0, defaults.Length).Any(i => Action.bindings[i].id.ToString().Equals(combo.Key, StringComparison.OrdinalIgnoreCase)))
                    throw new InvalidDataException("Unknown combo slot for " + Id);
        }
        catch (Exception e) when (e is InvalidDataException or System.Text.Json.JsonException or InvalidOperationException or FormatException)
        {
            // A record the combo layer cannot read must not stop a consumer from registering: load it as 0.1.7 did.
            Plugin.Logger.LogWarning($"[Keybinds] Combo data ignored for {Id}; loading record as plain bindings: {e.Message}");
            record = (json, new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase));
        }
        InputActionRebindingExtensions.LoadBindingOverridesFromJson(Action, record.Native, true);
        bindings = Enumerable.Range(0, defaults.Length).Select(i => Action.bindings[i].overridePath == null
            ? defaults[i] : ButtonBinding.Parse(Action.bindings[i].effectivePath ?? "")).ToArray();
        foreach (var combo in record.Combos)
        {
            int slot = Enumerable.Range(0, defaults.Length).First(i => Action.bindings[i].id.ToString().Equals(combo.Key, StringComparison.OrdinalIgnoreCase));
            var parsed = ButtonBinding.Parse(combo.Value);
            if (Action.bindings[slot].overridePath == null || !parsed.Path.Equals(Action.bindings[slot].effectivePath, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Combo metadata does not match native override.");
            bindings[slot] = parsed;
        }
        recordState.Remember(json, InputActionRebindingExtensions.SaveBindingOverridesAsJson(Action), ComboOverrides());
        InputRuntime.Invalidate();
        SyncSlots();
    }
    internal void SetPath(int index, string path)
    {
        var parsed = ButtonBinding.Parse(path);
        InputActionRebindingExtensions.ApplyBindingOverride(Action, index, parsed.Path);
        bindings[index] = parsed;
        SyncSlots(); InputRuntime.Invalidate();
    }
    internal void Reset(int index)
    {
        InputActionRebindingExtensions.RemoveBindingOverride(Action, index);
        bindings[index] = defaults[index];
        SyncSlots(); InputRuntime.Invalidate();
    }
    private void SyncSlots()
    {
        for (int i = 0; i < Slots.Length; i++)
        {
            var binding = Action.bindings[i];
            InputActionRebindingExtensions.ApplyBindingOverride(Slots[i], 0, new InputBinding
            {
                overridePath = binding.effectivePath ?? "", overrideInteractions = binding.effectiveInteractions,
                overrideProcessors = binding.effectiveProcessors
            });
        }
        Asset.Enable();
    }
    internal void EnsureEnabled()
    {
        if (disposed) return;
        if (Asset == null || Reference == null)
            throw new InvalidOperationException("Native input asset lost for " + Id);
        if (!Action.enabled) Action.Enable();
        foreach (var slot in Slots) if (!slot.enabled) slot.Enable();
    }
    internal string Health => $"{Id}: assetAlive={Asset != null}, referenceAlive={Reference != null}, enabled={Action.enabled}, paths={string.Join(",", Paths)}";
    private void Check() { Keybinds.CheckThread(); if (disposed) throw new ObjectDisposedException(nameof(ModKeybind)); }
    internal void Destroy() { disposed = true; Asset.Disable(); Object.Destroy(Reference); Object.Destroy(Asset); }
}


