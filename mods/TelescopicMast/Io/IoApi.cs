using UnityEngine;

namespace SprocketTelescopicMast.Io;

/// <summary>
/// Stable public facade for other mods (IO-API.md contract v1). Only BCL and
/// UnityEngine types appear in the signatures. Registrations are validated once at
/// Load(); all callbacks run on the main thread. A throwing callback is caught, logged
/// once and disabled for that vehicle until its next spawn.
/// </summary>
public static class IoApi
{
    public const int ApiVersion = 1;
    /// <summary>v1.2 settings (2); v1.1 data/outputs/icons (1); older masts have 0.</summary>
    public const int ApiRevision = 2;

    public static bool RegisterLevelInput(string id, string displayName, string group, Func<GameObject, bool> read)
    {
        if (!CanAdd(id, out string error)) { Warn(error); return false; }
        if (read == null) { Warn($"[IO API] Input '{id}' has no read callback."); return false; }
        if (!IoApiCore.TryRegisterLevelInput(id, displayName, group, value => read((GameObject)value),
                out var registration, out error)) { Warn(error); return false; }
        if (!RegisterType(new ExternalInput(registration), registration.Id)) { IoApiCore.Unregister(id); return false; }
        registration.Provider = read.Method.Module.Assembly.GetName().Name ?? displayName;
        Describe(registration);
        return true;
    }

    public static bool RegisterEventInput(string id, string displayName, string group, Func<GameObject, int> readEvents)
    {
        if (!CanAdd(id, out string error)) { Warn(error); return false; }
        if (readEvents == null) { Warn($"[IO API] Input '{id}' has no event callback."); return false; }
        if (!IoApiCore.TryRegisterEventInput(id, displayName, group, value => readEvents((GameObject)value),
                out var registration, out error)) { Warn(error); return false; }
        if (!RegisterType(new ExternalInput(registration), registration.Id)) { IoApiCore.Unregister(id); return false; }
        registration.Provider = readEvents.Method.Module.Assembly.GetName().Name ?? displayName;
        Describe(registration);
        return true;
    }

    public static bool RegisterOutput(string id, string displayName, string componentId,
        Action<GameObject, bool> apply,
        Func<GameObject, string?> loadRules,
        Action<GameObject, string> saveRules,
        string onLabel = "On", string offLabel = "Off")
    {
        if (!CanAdd(id, out string error)) { Warn(error); return false; }
        if (apply == null) { Warn($"[IO API] Output '{id}' has no apply callback."); return false; }
        if (!IoApiCore.TryRegisterOutput(id, displayName, componentId,
                (part, on) => apply((GameObject)part, on),
                loadRules == null ? null : part => loadRules((GameObject)part),
                saveRules == null ? null : (part, json) => saveRules((GameObject)part, json),
                onLabel, offLabel, out var registration, out error)) { Warn(error); return false; }
        registration.Provider = apply.Method.Module.Assembly.GetName().Name ?? displayName;
        if (!RegisterType(new ExternalOutput(registration), registration.Id)) { IoApiCore.Unregister(id); return false; }
        return true;
    }

    /// <summary>
    /// v1.1 (M1): short per-part data, saved with that part's own blueprint state. null removes
    /// the key. Only parts whose ComponentID is registered through RegisterOutput or
    /// RegisterManagedOutput are accepted. Returns false with one warning per part on a limit.
    /// </summary>
    public static bool SetPartData(GameObject part, string key, string? value)
    {
        if (part == null) { Warn("[IO API] SetPartData: part is null."); return false; }
        try { return IoApiCore.SetPartData(part, key, value); }
        catch (Exception ex) { Warn("[IO API] SetPartData failed: " + ex.Message); return false; }
    }

    /// <summary>v1.1 (M1): the value set for this part, or null when unset or not loaded yet.</summary>
    public static string? GetPartData(GameObject part, string key)
    {
        if (part == null) { Warn("[IO API] GetPartData: part is null."); return null; }
        try { return IoApiCore.GetPartData(part, key); }
        catch (Exception ex) { Warn("[IO API] GetPartData failed: " + ex.Message); return null; }
    }

    /// <summary>
    /// v1.1 (M2): the mast keeps the flow copy and ioPartId on the part itself. One registration
    /// per ComponentID; three part definitions may share that ComponentID.
    /// </summary>
    public static bool RegisterManagedOutput(string id, string displayName, string componentId,
        Action<GameObject, bool> apply, string onLabel = "On", string offLabel = "Off")
    {
        if (!CanAdd(id, out string error)) { Warn(error); return false; }
        if (apply == null) { Warn($"[IO API] Output '{id}' has no apply callback."); return false; }
        if (!IoApiCore.TryRegisterOutput(id, displayName, componentId,
                (part, on) => apply((GameObject)part, on), null, null, onLabel, offLabel, true,
                out var registration, out error)) { Warn(error); return false; }
        registration.Provider = apply.Method.Module.Assembly.GetName().Name ?? displayName;
        if (!RegisterType(new ExternalOutput(registration), registration.Id)) { IoApiCore.Unregister(id); return false; }
        return true;
    }

    /// <summary>v1.1 (M3): an absolute PNG path under BepInEx\plugins; loaded on the first card. First GUID wins.</summary>
    public static bool RegisterPartIcon(string partGuid, string pngPath)
    {
        try { return IoApiCore.RegisterPartIcon(partGuid, pngPath); }
        catch (Exception ex) { Warn("[IO API] RegisterPartIcon failed: " + ex.Message); return false; }
    }

    /// <summary>v1.2: per-part Kit settings, persisted under dataKey in ioPartDataV1.</summary>
    public static bool RegisterPartSettings(string componentId, string dataKey, string schemaJson, Action<GameObject>? onChanged = null) =>
        PartSettings.Register(componentId, dataKey, schemaJson, onChanged == null ? null : part => onChanged((GameObject)part));

    private static bool CanAdd(string id, out string error)
    {
        error = "";
        if (!IoApiCore.IsValidId(id)) { error = $"[IO API] Invalid id '{id}': use letters, digits, '.', '_' or '-'."; return false; }
        if (IoRegistry.FindInput(id) != null || IoRegistry.FindOutput(id) != null)
        { error = $"[IO API] Duplicate id '{id}': an input or output type with that id already exists."; return false; }
        if (IoApiCore.FindInput(id) != null || IoApiCore.FindOutput(id) != null)
        { error = $"[IO API] Duplicate id '{id}'."; return false; }
        return true;
    }

    private static bool RegisterType(IIoType type, string id)
    {
        try
        {
            if (type is IInputType input) IoRegistry.RegisterInput(input);
            else IoRegistry.RegisterOutput((IOutputType)type);
            return true;
        }
        catch (Exception ex)
        {
            Warn($"[IO API] Registration of '{id}' failed: {ex.Message}");
            return false;
        }
    }

    private static void Describe(ExternalInputRegistration registration)
    {
        try
        {
            var none = new Dictionary<string, string>();
            var events = registration.Kind == SignalKind.Level
                ? new[] { new RuleEventDef("turns on", none, false, true), new RuleEventDef("is on (while)", none, true) }
                : new[] { new RuleEventDef("triggers", none, false, true) };
            IoRegistry.DescribeInput(registration.Id, new RuleDescription(registration.Group, null,
                () => new[] { new RuleSourceOption(registration.DisplayName, none) }, events));
        }
        catch (Exception ex) { Warn($"[IO API] Description of '{registration.Id}' failed: {ex.Message}"); }
    }

    private static void Warn(string message) => IoApiCore.Log?.Invoke(message);

    private sealed class ExternalInput : IInputType
    {
        private readonly ExternalInputRegistration registration;
        internal ExternalInput(ExternalInputRegistration registration) { this.registration = registration; }
        public string Id => registration.Id;
        public string DisplayName => registration.DisplayName;
        public SignalKind SignalKind => registration.Kind;
        public ParamSchema Schema => ParamSchema.Empty;
        public string Describe(IReadOnlyDictionary<string, string> values) => registration.DisplayName;
        public InputSample Read(InputInstance instance, object? context)
        {
            if (context is not IExternalVehicleContext vehicle || vehicle.VehicleToken == null) return InputSample.Off;
            if (registration.Kind == SignalKind.Event)
                return new InputSample(false, IoApiCore.TryReadEvents(registration.Id, vehicle.VehicleToken));
            return new InputSample(IoApiCore.TryReadLevel(registration.Id, vehicle.VehicleToken, out bool on) && on, 0);
        }
    }

    private sealed class ExternalOutput : IOutputType
    {
        private readonly ExternalOutputRegistration registration;
        internal ExternalOutput(ExternalOutputRegistration registration) { this.registration = registration; }
        public string Id => registration.Id;
        public string DisplayName => registration.DisplayName;
        public SignalKind SignalKind => SignalKind.Level;
        public ParamSchema Schema => ParamSchema.Empty;
        public IReadOnlyList<IoCommand> Commands { get; } = Enum.GetValues<IoCommand>();
        public string Describe(IReadOnlyDictionary<string, string> values) => registration.DisplayName;
    }
}
