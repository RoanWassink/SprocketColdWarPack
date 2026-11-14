using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
namespace SprocketTelescopicMast.Io;

internal sealed class SettingField
{
    internal string Key = "", Label = "", Type = "", Unit = "";
    internal double Min, Max, Step;
    internal JsonNode Default = null!;
    internal (string Value, string Label)[] Options = Array.Empty<(string, string)>();
    internal string? ShowKey;
    internal JsonNode? ShowValue;
    internal JsonNode Normalize(JsonNode? value)
    {
        if (Type is "float" or "int")
        {
            double n = value is JsonValue v && v.TryGetValue<double>(out var x) && double.IsFinite(x) ? x : Default.GetValue<double>();
            n = Math.Clamp(n, Min, Max);
            if (Type == "int") n = Math.Clamp(Math.Round(n, MidpointRounding.AwayFromZero), Math.Ceiling(Min), Math.Floor(Max));
            return JsonValue.Create(n)!;
        }
        if (Type == "bool" && value is JsonValue b && b.TryGetValue<bool>(out var flag)) return JsonValue.Create(flag)!;
        if (Type == "enum" && value is JsonValue e && e.TryGetValue<string>(out var s) && Options.Any(o => o.Value == s)) return JsonValue.Create(s)!;
        return JsonNode.Parse(Default.ToJsonString())!;
    }
}
internal sealed class SettingsRegistration
{
    internal string Component = "", DataKey = "";
    internal SettingField[] Fields = Array.Empty<SettingField>();
    internal Action<object>? OnChanged;
    internal JsonObject Values(object part)
    {
        JsonObject values;
        try { values = JsonNode.Parse(PartData.Get(part, DataKey) ?? "{}") as JsonObject ?? new(); }
        catch (JsonException) { values = new(); }
        foreach (var f in Fields) values[f.Key] = f.Normalize(values[f.Key]);
        return values;
    }
    internal bool Visible(SettingField f, JsonObject values) => f.ShowKey == null || values[f.ShowKey]?.ToJsonString() == f.ShowValue?.ToJsonString();
}
/// <summary>Managed v1.2 schema and edits. Called by the host/panel, never by hooks.</summary>
internal static class PartSettings
{
    private static readonly Dictionary<string, SettingsRegistration> Registrations = new(StringComparer.Ordinal);
    internal static SettingsRegistration? Find(string? component) => component != null && Registrations.TryGetValue(component, out var r) ? r : null;
    internal static void ClearForTests() => Registrations.Clear();
    internal static bool Register(string componentId, string dataKey, string schemaJson, Action<object>? onChanged = null)
    {
        try
        {
            void Require(bool ok, string reason) { if (!ok) throw new ArgumentException(reason); }
            Require(IoApiCore.IsRegisteredComponent(componentId), "ComponentID is not registered");
            Require(!Registrations.ContainsKey(componentId), "duplicate settings registration");
            Require(PartData.ValidKey(dataKey, out var error), "invalid dataKey: " + error);
            Require(schemaJson != null && Encoding.UTF8.GetByteCount(schemaJson) <= 4096, "schema exceeds 4 KB or is null");
            var array = JsonNode.Parse(schemaJson!) as JsonArray;
            Require(array != null && array.Count <= 16, "schema must be an array of at most 16 fields");
            string Text(JsonObject o, string key, int max, bool optional = false)
            {
                if (optional && o[key] == null) return "";
                Require(o[key] is JsonValue v && v.TryGetValue<string>(out _), key + " must be a string");
                var s = o[key]!.GetValue<string>();
                Require(s.Length <= max && (optional || !string.IsNullOrWhiteSpace(s)), key + " is empty or too long");
                return s;
            }
            double Number(JsonObject o, string key)
            {
                Require(o[key] is JsonValue v && v.TryGetValue<double>(out var n) && double.IsFinite(n), key + " must be finite numeric");
                return o[key]!.GetValue<double>();
            }
            var fields = new List<SettingField>();
            foreach (var node in array!)
            {
                Require(node is JsonObject, "field must be an object"); var o = (JsonObject)node!;
                var f = new SettingField { Key = Text(o,"key",32), Label = Text(o,"label",40), Type = Text(o,"type",16), Unit = Text(o,"unit",8,true) };
                Require(f.Key.All(c => c is >= 'a' and <= 'z' or >= '0' and <= '9' or '_'), "invalid field key");
                Require(fields.All(x => x.Key != f.Key), "duplicate field key");
                Require(o["default"] != null, "missing default");
                f.Default = JsonNode.Parse(o["default"]!.ToJsonString())!;
                if (f.Type is "float" or "int")
                {
                    f.Min = Number(o,"min"); f.Max = Number(o,"max"); var d = Number(o,"default");
                    Require(f.Min <= f.Max && d >= f.Min && d <= f.Max, "invalid numeric range/default");
                    Require(f.Type != "int" || Math.Ceiling(f.Min) <= Math.Floor(f.Max), "int range contains no integer");
                    f.Step = o["step"] == null ? (f.Type == "int" ? 1 : .1) : Number(o,"step");
                    Require(f.Step > 0, "invalid step");
                }
                else if (f.Type == "bool") Require(o["default"] is JsonValue b && b.TryGetValue<bool>(out _), "bool default required");
                else if (f.Type == "enum")
                {
                    Require(o["options"] is JsonArray a && a.Count > 0 && a.Count <= 16, "enum needs 1..16 options");
                    var options = new List<(string Value,string Label)>();
                    foreach (var item in (JsonArray)o["options"]!)
                    {
                        Require(item is JsonObject, "option must be an object"); var option = (JsonObject)item!;
                        // Option strings have no separate contract length limit; total schema is bounded.
                        var value = Text(option,"value",4096); var label = Text(option,"label",4096);
                        Require(options.All(x => x.Value != value), "duplicate enum value"); options.Add((value,label));
                    }
                    f.Options = options.ToArray();
                    Require(o["default"] is JsonValue e && e.TryGetValue<string>(out var s) && options.Any(x => x.Value == s), "enum default must be an option");
                }
                else Require(false, "unknown field type: " + f.Type);
                if (o["showIf"] != null)
                {
                    Require(o["showIf"] is JsonObject, "showIf must be an object"); var show = (JsonObject)o["showIf"]!;
                    f.ShowKey = Text(show,"key",32); f.ShowValue = show["value"] == null ? null : JsonNode.Parse(show["value"]!.ToJsonString());
                }
                fields.Add(f);
            }
            foreach (var f in fields.Where(f => f.ShowKey != null))
            {
                var source = fields.FirstOrDefault(x => x.Key == f.ShowKey);
                Require(source != null && (source.Type is "bool" or "enum"), "showIf must reference a bool/enum field");
                Require(f.ShowValue != null && source!.Normalize(f.ShowValue).ToJsonString() == f.ShowValue.ToJsonString(), "invalid showIf value");
            }
            Registrations.Add(componentId, new SettingsRegistration { Component = componentId, DataKey = dataKey, Fields = fields.ToArray(), OnChanged = onChanged });
            return true;
        }
        catch (Exception ex) when (ex is ArgumentException or JsonException or InvalidOperationException)
        { IoApiCore.Log?.Invoke("[IO SETTINGS] registration rejected: " + ex.Message); return false; }
    }
    internal static void Ensure(object part)
    {
        var r = Find(PartData.ComponentId(part)); if (r == null) return;
        string json = r.Values(part).ToJsonString();
        if (json != PartData.Get(part,r.DataKey)) PartData.Set(part,r.DataKey,json);
    }
    internal static bool Change(SettingsRegistration r, IReadOnlyList<(object Part,string Id)> targets, string field, JsonNode? value, bool editable, Action<string>? trace)
    {
        if (!editable) return false;
        var f = r.Fields.FirstOrDefault(x => x.Key == field); if (f == null) return false;
        var updates = new List<(object Part,string Id,string Json)>();
        foreach (var t in targets)
        {
            if (PartData.ComponentId(t.Part) != r.Component) return false;
            var values = r.Values(t.Part); values[field] = f.Normalize(value); string json = values.ToJsonString();
            if (json != PartData.Get(t.Part,r.DataKey)) updates.Add((t.Part,t.Id,json));
        }
        // Preflight every target so a full twin cannot leave only half of a pair updated.
        foreach (var t in updates) if (!PartData.CanSet(t.Part,r.DataKey,t.Json)) return false;
        foreach (var t in updates) PartData.Set(t.Part,r.DataKey,t.Json);
        foreach (var t in updates)
        {
            trace?.Invoke($"[IO SETTINGS] component={r.Component} part={t.Id} key={r.DataKey} changed={field}");
            try { r.OnChanged?.Invoke(t.Part); }
            catch (Exception ex) { IoApiCore.Log?.Invoke("[IO SETTINGS] onChanged failed: " + ex.Message); }
        }
        return updates.Count > 0;
    }
}

internal sealed class SettingsBinding
{
    internal SettingsRegistration Registration = null!;
    internal IReadOnlyList<(object Part,string Id)> Targets = Array.Empty<(object,string)>();
    internal JsonObject Values => Registration.Values(Targets[0].Part);
}
