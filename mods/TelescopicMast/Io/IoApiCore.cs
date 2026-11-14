namespace SprocketTelescopicMast.Io;

// Unity-free core of the public facade. The facade (IoApi.cs) wraps GameObject delegates
// into these object callbacks; the runtime passes the vehicle/part object. Tests exercise
// this file directly, including duplicate/invalid ids, callback failures and the per-vehicle
// disabled state.

internal sealed class ExternalInputRegistration
{
    internal string Provider = "";
    internal string Id = "";
    internal string DisplayName = "";
    internal string Group = "";
    internal SignalKind Kind;
    internal Func<object, bool>? ReadLevel;
    internal Func<object, int>? ReadEvents;
}

internal sealed class ExternalOutputRegistration
{
    internal string Provider = "";
    internal string Id = "";
    internal string DisplayName = "";
    internal string ComponentId = "";
    internal Action<object, bool> Apply = (_, _) => { };
    internal Func<object, string?>? LoadRules;
    internal Action<object, string>? SaveRules;
    internal string OnLabel = "On";
    internal string OffLabel = "Off";
    /// <summary>v1.1: the mast owns this part's flow copy and ioPartId; no loadRules/saveRules.</summary>
    internal bool Managed;
}

internal static class IoApiCore
{
    /// <summary>
    /// Unity's GameObject/Component Equals compares the native instance, so a fresh wrapper
    /// for the same vehicle still addresses the same disabled-state entry.
    /// </summary>
    private sealed class VehicleComparer : IEqualityComparer<object>
    {
        internal static readonly VehicleComparer Instance = new();
        public new bool Equals(object? x, object? y) => ReferenceEquals(x, y) || (x != null && x.Equals(y));
        public int GetHashCode(object obj) => obj.GetHashCode();
    }

    private static readonly object Gate = new();
    private static readonly object NullVehicle = new();
    private static readonly Dictionary<string, ExternalInputRegistration> Inputs = new(StringComparer.Ordinal);
    private static readonly List<string> InputOrder = new();
    private static readonly Dictionary<string, ExternalOutputRegistration> Outputs = new(StringComparer.Ordinal);
    private static readonly List<string> OutputOrder = new();
    private static readonly Dictionary<object, HashSet<string>> Disabled = new(VehicleComparer.Instance);

    internal static Action<string>? Log;

    internal static IReadOnlyList<ExternalOutputRegistration> OutputRegistrations
    {
        get { lock (Gate) return OutputOrder.Select(id => Outputs[id]).ToArray(); }
    }
    internal static IReadOnlyList<ExternalInputRegistration> InputRegistrations
    {
        get { lock (Gate) return InputOrder.Select(id => Inputs[id]).ToArray(); }
    }

    internal static bool IsValidId(string? id) =>
        !string.IsNullOrWhiteSpace(id) && id.All(c => char.IsLetterOrDigit(c) || c is '.' or '_' or '-');

    internal static bool TryRegisterLevelInput(string id, string displayName, string group, Func<object, bool> read,
        out ExternalInputRegistration registration, out string error) =>
        TryRegisterInput(id, displayName, group, SignalKind.Level, read, null, out registration, out error);

    internal static bool TryRegisterEventInput(string id, string displayName, string group, Func<object, int> read,
        out ExternalInputRegistration registration, out string error) =>
        TryRegisterInput(id, displayName, group, SignalKind.Event, null, read, out registration, out error);

    private static bool TryRegisterInput(string id, string displayName, string group, SignalKind kind,
        Func<object, bool>? readLevel, Func<object, int>? readEvents,
        out ExternalInputRegistration registration, out string error)
    {
        registration = null!;
        error = "";
        if (!IsValidId(id)) { error = $"Invalid input id '{id}': use letters, digits, '.', '_' or '-'."; return false; }
        if (readLevel == null && readEvents == null) { error = $"Input '{id}' has no read callback."; return false; }
        lock (Gate)
        {
            if (Inputs.ContainsKey(id) || Outputs.ContainsKey(id)) { error = $"Duplicate I/O API id '{id}'."; return false; }
            registration = new ExternalInputRegistration
            {
                Id = id, DisplayName = string.IsNullOrWhiteSpace(displayName) ? id : displayName,
                Group = string.IsNullOrWhiteSpace(group) ? "External" : group, Kind = kind,
                ReadLevel = readLevel, ReadEvents = readEvents
            };
            Inputs.Add(id, registration); InputOrder.Add(id);
        }
        return true;
    }

    internal static bool TryRegisterOutput(string id, string displayName, string componentId,
        Action<object, bool> apply, Func<object, string?>? loadRules, Action<object, string>? saveRules,
        string onLabel, string offLabel, out ExternalOutputRegistration registration, out string error) =>
        TryRegisterOutput(id, displayName, componentId, apply, loadRules, saveRules, onLabel, offLabel, false,
            out registration, out error);

    internal static bool TryRegisterOutput(string id, string displayName, string componentId,
        Action<object, bool> apply, Func<object, string?>? loadRules, Action<object, string>? saveRules,
        string onLabel, string offLabel, bool managed, out ExternalOutputRegistration registration, out string error)
    {
        registration = null!;
        error = "";
        if (!IsValidId(id)) { error = $"Invalid output id '{id}': use letters, digits, '.', '_' or '-'."; return false; }
        if (string.IsNullOrWhiteSpace(componentId)) { error = $"Output '{id}' has no componentId."; return false; }
        if (apply == null) { error = $"Output '{id}' has no apply callback."; return false; }
        lock (Gate)
        {
            if (Inputs.ContainsKey(id) || Outputs.ContainsKey(id)) { error = $"Duplicate I/O API id '{id}'."; return false; }
            if (managed && Outputs.Values.Any(o => o.ComponentId == componentId))
            { error = $"Output '{id}': ComponentID '{componentId}' already has an external output; managed outputs allow one registration per ComponentID."; return false; }
            registration = new ExternalOutputRegistration
            {
                Id = id, DisplayName = string.IsNullOrWhiteSpace(displayName) ? id : displayName,
                ComponentId = componentId, Apply = apply, LoadRules = loadRules, SaveRules = saveRules,
                OnLabel = string.IsNullOrWhiteSpace(onLabel) ? "On" : onLabel,
                OffLabel = string.IsNullOrWhiteSpace(offLabel) ? "Off" : offLabel,
                Managed = managed
            };
            Outputs.Add(id, registration); OutputOrder.Add(id);
        }
        return true;
    }

    internal static bool IsManagedComponent(string? componentId) =>
        !string.IsNullOrWhiteSpace(componentId) && OutputRegistrations.Any(r => r.Managed && r.ComponentId == componentId);

    internal static bool IsRegisteredComponent(string? componentId) =>
        !string.IsNullOrWhiteSpace(componentId) && OutputRegistrations.Any(r => r.ComponentId == componentId);

    internal static bool HasRegistrationFor(string? componentId) => IsRegisteredComponent(componentId);

    internal static ExternalOutputRegistration? FindByComponent(string? componentId) =>
        OutputRegistrations.FirstOrDefault(r => r.ComponentId == componentId);

    /// <summary>M2: "&lt;part name&gt; &lt;n&gt;"; the registration displayName when no part name is readable.</summary>
    internal static string ExternalLabel(string? partName, string displayName, int number) =>
        (string.IsNullOrWhiteSpace(partName) ? displayName : partName!.Trim()) + " " + number.ToString();

    // ---------- v1.1 per-part data (IO-API.md §6 M1) ----------
    internal static bool SetPartData(object part, string key, string? value) => PartData.Set(part, key, value);
    internal static string? GetPartData(object part, string key) => PartData.Get(part, key);
    internal static void AttachPart(object part, string componentId) => PartData.Attach(part, componentId);
    internal static void ForgetPart(object part) => PartData.Remove(part);
    internal static string? PartDataJson(object part) => PartData.StateJson(part);
    internal static void LoadPartData(object part, string componentId, string? json)
    { PartData.Attach(part, componentId); PartData.LoadState(part, json); }

    // ---------- v1.1 part icons (IO-API.md §6 M3) ----------
    internal static void ConfigurePluginRoot(string? root) => PartIcons.ConfigureRoot(root);
    internal static bool RegisterPartIcon(string partGuid, string pngPath) => PartIcons.Register(partGuid, pngPath);
    internal static bool TryGetPartIcon(string partGuid, out string path) => PartIcons.TryGet(partGuid, out path);

    internal static void Unregister(string id)
    {
        lock (Gate) { Inputs.Remove(id); InputOrder.Remove(id); Outputs.Remove(id); OutputOrder.Remove(id); }
    }

    internal static ExternalInputRegistration? FindInput(string id)
    { lock (Gate) return Inputs.TryGetValue(id, out var r) ? r : null; }
    internal static ExternalOutputRegistration? FindOutput(string id)
    { lock (Gate) return Outputs.TryGetValue(id, out var r) ? r : null; }

    /// <summary>Read a level; false when the registration is missing, disabled or its callback throws.</summary>
    internal static bool TryReadLevel(string id, object vehicle, out bool value)
    {
        value = false;
        var reg = FindInput(id);
        if (reg?.ReadLevel == null || IsDisabled(id, vehicle)) return false;
        try { value = reg.ReadLevel(vehicle); return true; }
        catch (Exception ex) { Fail(id, vehicle, ex); return false; }
    }

    /// <summary>Read pending events; 0 when the registration is missing, disabled or its callback throws.</summary>
    internal static int TryReadEvents(string id, object vehicle)
    {
        var reg = FindInput(id);
        if (reg?.ReadEvents == null || IsDisabled(id, vehicle)) return 0;
        try { return Math.Max(0, reg.ReadEvents(vehicle)); }
        catch (Exception ex) { Fail(id, vehicle, ex); return 0; }
    }

    internal static bool TryLoadRules(string id, object vehicle, object part, out string? json)
    {
        json = null;
        var reg = FindOutput(id);
        if (reg == null || IsDisabled(id, vehicle)) return false;
        if (reg.LoadRules == null) return true;
        try { json = reg.LoadRules(part); return true; }
        catch (Exception ex) { Fail(id, vehicle, ex); return false; }
    }

    internal static bool TrySaveRules(string id, object vehicle, object part, string json)
    {
        var reg = FindOutput(id);
        if (reg == null || IsDisabled(id, vehicle)) return false;
        if (reg.SaveRules == null) return true;
        try { reg.SaveRules(part, json); return true; }
        catch (Exception ex) { Fail(id, vehicle, ex); return false; }
    }

    internal static bool TryApply(string id, object vehicle, object part, bool on)
    {
        var reg = FindOutput(id);
        if (reg == null || IsDisabled(id, vehicle)) return false;
        try { reg.Apply(part, on); return true; }
        catch (Exception ex) { Fail(id, vehicle, ex); return false; }
    }

    internal static bool IsDisabled(string id, object vehicle)
    {
        vehicle ??= NullVehicle;
        lock (Gate) return Disabled.TryGetValue(vehicle, out var set) && set.Contains(id);
    }

    internal static void ResetVehicle(object? vehicle)
    {
        if (vehicle == null) return;
        lock (Gate) Disabled.Remove(vehicle);
    }

    /// <summary>The registration is disabled for this vehicle until its next spawn.</summary>
    private static void Fail(string id, object vehicle, Exception ex)
    {
        vehicle ??= NullVehicle;
        bool first;
        lock (Gate)
        {
            if (!Disabled.TryGetValue(vehicle, out var set)) Disabled[vehicle] = set = new HashSet<string>(StringComparer.Ordinal);
            first = set.Add(id);
        }
        if (first) Log?.Invoke($"[IO API] '{id}' callback failed on this vehicle; disabled until its next spawn: {ex.Message}");
    }

    internal static void ClearForTests()
    {
        lock (Gate)
        {
            Inputs.Clear(); InputOrder.Clear(); Outputs.Clear(); OutputOrder.Clear(); Disabled.Clear();
        }
        PartData.ClearForTests(); PartIcons.ClearForTests(); PartSettings.ClearForTests();
        Log = null;
    }
}
