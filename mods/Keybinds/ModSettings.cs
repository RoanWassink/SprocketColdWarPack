using System.Collections;
using System.Globalization;
using BepInEx.Configuration;
using BepInEx.Logging;

namespace SprocketKeybinds;

public enum SettingKind { Toggle, Number, Choice, Text }

public sealed class SettingOptions
{
    public string? Label { get; set; }
    public string? Description { get; set; }
    public string? Group { get; set; }
    public bool RequiresRestart { get; set; }
    public bool Advanced { get; set; }
    public float? Step { get; set; }
    public Func<bool>? VisibleWhen { get; set; }
}

/// <summary>A live view of an entry owned and persisted by the registering mod.</summary>
public sealed class ModSetting
{
    private string ownerId = "", author = "Unknown", modName = "";
    public string OwnerId { get => ownerId; }
    public string Author { get => author; }
    public string ModName { get => modName; }
    public string Group { get; }
    public string Key { get; }
    public string Label { get; }
    public string Description { get; }
    public SettingKind Kind { get; }
    public double Min { get; }
    public double Max { get; }
    public float? Step { get; }
    public IReadOnlyList<string> Choices { get; }
    public bool RequiresRestart { get; }
    public bool Advanced { get; }
    public ConfigEntryBase Entry { get; }
    private readonly Func<bool>? visibleWhen;
    private readonly object[] choiceValues;
    private readonly SettingKind valueKind;
    private readonly string warningKey;

    internal ModSetting(ConfigEntryBase entry, SettingOptions? options, SettingKind kind,
        double min = 0, double max = 0, object[]? choices = null)
    {
        Entry = entry; Kind = valueKind = kind; Min = min; Max = max;
        Group = options?.Group ?? entry.Definition.Section;
        Key = entry.Definition.Key; Label = options?.Label ?? Key;
        Description = options?.Description ?? entry.Description.Description;
        Step = options?.Step ?? (kind == SettingKind.Number && (entry.SettingType == typeof(int) || entry.SettingType == typeof(long)) ? 1f : null);
        RequiresRestart = options?.RequiresRestart ?? false; Advanced = options?.Advanced ?? false;
        visibleWhen = options?.VisibleWhen;
        choiceValues = choices ?? Array.Empty<object>();
        Choices = Array.AsReadOnly(entry.SettingType.IsEnum && entry.Description.AcceptableValues == null
            ? Enum.GetNames(entry.SettingType) : choiceValues.Select(ChoiceName).ToArray());
        warningKey = entry.ConfigFile.ConfigFilePath + "/" + entry.Definition;
    }
    private static string ChoiceName(object value) => Convert.ToString(value, CultureInfo.InvariantCulture) ?? "";
    internal void SetOwner(string id) { ownerId = id; }
    internal void RefreshMetadata(ModInfo info) { author = info.Author; modName = info.Name; }

    public bool IsVisible
    {
        get
        {
            try { return visibleWhen?.Invoke() ?? true; }
            catch (Exception e) { ModSettings.Warn("visible:" + warningKey, "Visibility predicate failed: " + e.Message); return true; }
        }
    }
    public object Value
    {
        get
        {
            try { return Entry.BoxedValue; }
            catch (Exception e) { ModSettings.Warn("read:" + warningKey, "Cannot read setting: " + e.Message); return DefaultValue; }
        }
        set
        {
            try
            {
                object converted = value;
                var type = Entry.SettingType;
                if (valueKind == SettingKind.Number)
                {
                    if (value == null || !IsNumber(value.GetType())) throw new ArgumentException("Expected a number.");
                    // Decimal preserves Int64 precision; floating inputs are rounded for integral entries.
                    if (type == typeof(int) || type == typeof(long))
                    {
                        var rounded = decimal.Round(Convert.ToDecimal(value, CultureInfo.InvariantCulture), 0, MidpointRounding.AwayFromZero);
                        converted = Convert.ChangeType(rounded, type, CultureInfo.InvariantCulture);
                    }
                    else
                    {
                        double number = Convert.ToDouble(value, CultureInfo.InvariantCulture);
                        if (!double.IsFinite(number)) throw new ArgumentException("Expected a finite number.");
                        converted = Convert.ChangeType(value, type, CultureInfo.InvariantCulture);
                        if (converted is float single && !float.IsFinite(single)) throw new OverflowException();
                    }
                }
                else if (valueKind == SettingKind.Choice)
                {
                    var candidate = value;
                    if (value is string text)
                    {
                        candidate = choiceValues.FirstOrDefault(c => ChoiceName(c) == text);
                        if (candidate == null && type.IsEnum && Enum.TryParse(type, text, false, out var parsed)) candidate = parsed;
                    }
                    if (candidate == null || candidate.GetType() != type || !choiceValues.Contains(candidate))
                        throw new ArgumentException("Expected an available choice.");
                    converted = candidate;
                }
                else if (value == null || value.GetType() != type) throw new ArgumentException("Incorrect setting type.");
                Entry.BoxedValue = converted;
            }
            catch (Exception e) { ModSettings.Warn("write:" + warningKey, "Ignored invalid setting value: " + e.Message); }
        }
    }
    internal static bool IsNumber(Type type) => !type.IsEnum && Type.GetTypeCode(type) is (TypeCode.Byte or TypeCode.SByte
        or TypeCode.Int16 or TypeCode.UInt16 or TypeCode.Int32 or TypeCode.UInt32 or TypeCode.Int64 or TypeCode.UInt64
        or TypeCode.Single or TypeCode.Double or TypeCode.Decimal);
    public object DefaultValue
    {
        get
        {
            try { return Entry.DefaultValue; }
            catch (Exception e) { ModSettings.Warn("default:" + warningKey, "Cannot read default: " + e.Message); return null!; }
        }
    }
    public void Reset() { Value = DefaultValue; }
}

public sealed class ModSettingGroup
{
    public string Key { get; }
    public string Name { get; }
    public bool IsPack { get; }
    public IReadOnlyList<ModSettingMod> Mods { get; }
    internal ModSettingGroup(string key, string name, bool isPack, IEnumerable<ModSettingMod> mods)
    { Key = key; Name = name; IsPack = isPack; Mods = Array.AsReadOnly(mods.ToArray()); }
}
public sealed class ModSettingMod
{
    public string OwnerId { get; }
    public string Name { get; }
    public string Author { get; }
    public IReadOnlyList<ModSettingSection> Sections { get; }
    internal ModSettingMod(string ownerId, string name, string author, IEnumerable<ModSettingSection> sections)
    { OwnerId = ownerId; Name = name; Author = author; Sections = Array.AsReadOnly(sections.ToArray()); }
}
public sealed class ModSettingSection
{
    public string Name { get; }
    public IReadOnlyList<ModSetting> Settings { get; }
    internal ModSettingSection(string name, IEnumerable<ModSetting> settings)
    { Name = name; Settings = Array.AsReadOnly(settings.ToArray()); }
}

/// <summary>Managed global settings registry. No storage, Unity calls, or readiness requirement.</summary>
public static class ModSettings
{
    private static readonly Dictionary<(string Owner, ConfigDefinition Definition), ModSetting> settings = new();
    private static readonly List<ModSetting> registrationOrder = new();
    private static readonly Dictionary<string, List<string>> sectionOrder = new(StringComparer.Ordinal);
    private static readonly HashSet<string> warnings = new(StringComparer.Ordinal);
    private static readonly ManualLogSource log = Logger.CreateLogSource("Sprocket Mod Settings");
    private static IReadOnlyList<ModSetting> all = Array.AsReadOnly(Array.Empty<ModSetting>());
    private static IReadOnlyList<ModSettingGroup> authors = Array.AsReadOnly(Array.Empty<ModSettingGroup>());
    private static IReadOnlyList<ModSettingGroup> packs = Array.AsReadOnly(Array.Empty<ModSettingGroup>());
    private static Action? presenter;
    private static Func<bool>? presenterIsOpen;
    private static bool missingPresenterLogged;
    static ModSettings() { ModMetadata.Shared.Changed += Rebuild; }

    internal static void Warn(string cause, string message)
    {
        try { if (warnings.Add(cause)) log.LogWarning(message); } catch { /* Logging must not break a caller. */ }
    }
    private static bool ValidOwner(string ownerId)
    {
        try { BindingStore.Identity(ownerId, "settings"); return true; }
        catch { Warn("owner:" + ownerId, "Invalid settings owner ID."); return false; }
    }
    public static ModSetting? Describe(ConfigEntryBase entry, SettingOptions? options = null)
    {
        try
        {
            if (entry == null) { Warn("entry:null", "Cannot describe a null config entry."); return null; }
            var type = entry.SettingType;
            var acceptable = entry.Description.AcceptableValues;
            var constraintType = acceptable?.GetType();
            bool Generic(Type generic) => constraintType != null && constraintType.IsGenericType
                && constraintType.GetGenericTypeDefinition() == generic;
            if (type.Name == "KeyboardShortcut")
            { Warn("type:KeyboardShortcut", "Keyboard shortcuts belong to the keybind API."); return null; }
            if (type == typeof(bool)) return new(entry, options, SettingKind.Toggle);
            bool number = type == typeof(int) || type == typeof(long) || type == typeof(float) || type == typeof(double);
            if (number && Generic(typeof(AcceptableValueRange<>)))
                return new(entry, options, SettingKind.Number,
                    Convert.ToDouble(constraintType!.GetProperty("MinValue")!.GetValue(acceptable), CultureInfo.InvariantCulture),
                    Convert.ToDouble(constraintType!.GetProperty("MaxValue")!.GetValue(acceptable), CultureInfo.InvariantCulture));
            if (Generic(typeof(AcceptableValueList<>)))
            {
                var values = ((IEnumerable)constraintType!.GetProperty("AcceptableValues")!.GetValue(acceptable)!).Cast<object>().ToArray();
                if (values.Length > 0)
                    return new(entry, options, SettingKind.Choice, choices: values);
            }
            else if (type.IsEnum) return new(entry, options, SettingKind.Choice, choices: Enum.GetValues(type).Cast<object>().ToArray());
            else if (type == typeof(string)) return new(entry, options, SettingKind.Text);
            Warn("type:" + type.FullName + ":" + constraintType?.FullName, "Unsupported settings type or missing numeric range: " + type.Name);
        }
        catch (Exception e) { Warn("describe:" + e.GetType().FullName, "Cannot describe setting: " + e.Message); }
        return null;
    }
    public static ModSetting? Register(string ownerId, ConfigEntryBase entry, SettingOptions? options = null)
    {
        try
        {
            if (!ValidOwner(ownerId)) return null;
            if (entry != null && settings.TryGetValue((ownerId, entry.Definition), out var existing)) return existing;
            var setting = Describe(entry!, options);
            if (setting == null) return null;
            setting.SetOwner(ownerId);
            settings.Add((ownerId, setting.Entry.Definition), setting); registrationOrder.Add(setting);
            if (!sectionOrder.TryGetValue(ownerId, out var sections)) sectionOrder[ownerId] = sections = new();
            if (!sections.Contains(setting.Group)) sections.Add(setting.Group);
            Rebuild(); return setting;
        }
        catch (Exception e) { Warn("register:" + ownerId + ":" + e.GetType().FullName, "Cannot register setting: " + e.Message); return null; }
    }
    public static int RegisterSection(string ownerId, ConfigFile file, string section)
    {
        try
        {
            if (!ValidOwner(ownerId)) return 0;
            int count = 0;
            if (file != null && section != null)
                foreach (var entry in file.Where(p => p.Key.Section == section).Select(p => p.Value).ToArray())
                    if (Register(ownerId, entry) != null) count++;
            if (count == 0) Warn("section:" + ownerId + ":" + section, "No supported entries in settings section: " + section);
            return count;
        }
        catch (Exception e) { Warn("section:" + ownerId + ":" + section, "Cannot register settings section: " + e.Message); return 0; }
    }
    public static bool Unregister(string ownerId, ConfigDefinition definition)
    {
        try
        {
            if (!ValidOwner(ownerId) || definition == null || !settings.Remove((ownerId, definition), out var removed)) return false;
            registrationOrder.Remove(removed);
            if (!registrationOrder.Any(s => s.OwnerId == ownerId && s.Group == removed.Group)) sectionOrder[ownerId].Remove(removed.Group);
            if (sectionOrder[ownerId].Count == 0) sectionOrder.Remove(ownerId);
            Rebuild(); return true;
        }
        catch (Exception e) { Warn("unregister:" + ownerId, "Cannot unregister setting: " + e.Message); return false; }
    }
    public static IReadOnlyList<ModSetting> All => all;
    public static IReadOnlyList<ModSettingGroup> Snapshot(bool byPack = false) => byPack ? packs : authors;
    public static event Action? Changed;

    private static void Rebuild()
    {
        try
        {
            var values = registrationOrder.ToArray();
            var metadata = ModMetadata.Shared;
            var owners = settings.Keys.Select(k => k.Owner).Distinct().Select(o => (Owner: o, Name: o)).ToArray();
            IReadOnlyList<ModSettingGroup> Build(bool byPack)
            {
                return Array.AsReadOnly(metadata.Order(owners, byPack).GroupBy(p => p.GroupKey).Select(group =>
                {
                    var first = group.First();
                    return new ModSettingGroup(first.GroupKey, first.GroupName, first.Pack, group.Select(p =>
                        new ModSettingMod(p.Owner, p.Mod.Name, p.Mod.Author, sectionOrder[p.Owner]
                            .Select(name => new ModSettingSection(name, values.Where(s => s.OwnerId == p.Owner && s.Group == name))))));
                }).ToArray());
            }
            foreach (var item in values)
            {
                var info = metadata.Resolve(item.OwnerId, item.OwnerId, false);
                item.RefreshMetadata(info.Mod);
            }
            var nextAuthors = Build(false); var nextPacks = Build(true);
            all = Array.AsReadOnly(values); authors = nextAuthors; packs = nextPacks;
            var handlers = Changed;
            if (handlers != null)
                foreach (Action handler in handlers.GetInvocationList())
                    try { handler(); } catch (Exception e) { Warn("changed:" + handler.Method, "Settings change handler failed: " + e.Message); }
        }
        catch (Exception e) { Warn("snapshot:" + e.GetType().FullName, "Cannot rebuild settings snapshot: " + e.Message); }
    }
    public static void ResetOwner(string ownerId)
    {
        if (!ValidOwner(ownerId)) return;
        foreach (var setting in all.Where(s => s.OwnerId == ownerId).ToArray()) setting.Reset();
    }
    public static void SetPresenter(Action? open, Func<bool>? isOpen)
    {
        if (presenter != null && open != null) Warn("presenter:replace", "Replacing the mod settings presenter.");
        presenter = open; presenterIsOpen = open == null ? null : isOpen;
    }
    public static bool HasPresenter => presenter != null;
    public static bool IsOpen
    {
        get
        {
            try { return HasPresenter && (presenterIsOpen?.Invoke() ?? false); }
            catch (Exception e) { Warn("presenter:state", "Cannot read presenter state: " + e.Message); return false; }
        }
    }
    public static void Open()
    {
        try
        {
            if (presenter != null) presenter();
            else if (!missingPresenterLogged) { missingPresenterLogged = true; log.LogInfo("Mod settings needs a presenter (Sprocket UI Kit)."); }
        }
        catch (Exception e) { Warn("presenter:open", "Cannot open mod settings: " + e.Message); }
    }
}
