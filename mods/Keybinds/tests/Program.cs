using SprocketKeybinds;
using System.Text.Json;
using System.Text.RegularExpressions;

var temp = Path.Combine(AppContext.BaseDirectory, "SprocketKeybinds.Tests." + Guid.NewGuid());
Directory.CreateDirectory(temp);
int passed = 0;
void Check(bool value, string name) { if (!value) throw new Exception(name); Console.WriteLine("PASS " + name); passed++; }
void Reject(Action action, string name) { try { action(); } catch { Check(true, name); return; } throw new Exception("Expected rejection: " + name); }
try
{
    var path = Path.Combine(temp, "keybinds.json");
    var identity = BindingStore.Identity("example.mast", "raise");
    Check(identity == "example.mast/raise", "stable namespaced identity");
    var guid = BindingStore.StableGuid(identity, "binding0");
    Check(Guid.TryParse(guid, out _) && guid == BindingStore.StableGuid(identity, "binding0"), "stable restart GUID");
    Check(guid != BindingStore.StableGuid(identity, "binding1") && guid != BindingStore.StableGuid("example.thermal/raise", "binding0"), "secondary and owner isolation");
    Reject(() => BindingStore.Identity("example/mast", "raise"), "ambiguous IDs rejected");
    Reject(() => BindingStore.Identity("example.mast", ""), "empty IDs rejected");
    var store = new BindingStore(path); store.Load();
    store.Values[identity] = "initial"; store.Values["absent.mod/action"] = "preserved"; store.Commit();
    store.Begin(); store.Values[identity] = "edited"; store.End();
    Check(store.Values[identity] == "initial" && !store.Editing, "Cancel restores initial state");
    store.Begin(); store.Values[identity] = "applied"; store.Commit();
    store.Values[identity] = "after apply"; store.End();
    Check(store.Values[identity] == "applied", "Apply then Cancel retains applied binding");
    var loaded = new BindingStore(path); loaded.Load();
    Check(loaded.Values[identity] == "applied", "restart reloads binding");
    Check(loaded.Values["absent.mod/action"] == "preserved", "settings for absent mods retained");
    var backup = new BindingStore(path + ".bak"); backup.Load();
    Check(backup.Values[identity] == "initial", "atomic save keeps prior file backup");
    loaded.Begin(); Reject(loaded.Begin, "nested sessions rejected"); loaded.Values["late.mod/action"] = "late"; loaded.End();
    Check(!loaded.Values.ContainsKey("late.mod/action"), "late registration cancellation removes new persisted override");
    File.WriteAllText(path, "{bad json");
    Reject(() => new BindingStore(path).Load(), "corrupt configuration rejected");
    Check(File.ReadAllText(path) == "{bad json", "corrupt configuration left intact");
    File.WriteAllText(path, JsonSerializer.Serialize(new { Version = 999, Bindings = new Dictionary<string,string>() }));
    Reject(() => new BindingStore(path).Load(), "unsupported future schema rejected");
    var migration = new BindingStore(Path.Combine(temp, "migration.json"));
    Check(migration.ImportOnce(identity, "custom F9"), "legacy custom binding imported on first use");
    Check(!migration.ImportOnce(identity, "old CFG F8") && migration.Values[identity] == "custom F9", "subsequent import cannot overwrite API choice");
    migration.Values[identity] = "{\"bindings\":[]}"; migration.Commit();
    var reset = new BindingStore(Path.Combine(temp, "migration.json")); reset.Load();
    Check(!reset.ImportOnce(identity, "old CFG F8"), "factory reset survives restart without reimporting CFG");
    Check(reset.ImportOnce("other.mod/action", "unbound"), "independent external mod can import separately");
    reset.Begin(); Reject(() => reset.ImportOnce("late.mod/action", "legacy"), "import cannot bypass open menu transaction"); reset.End();
    var impossible = new BindingStore(temp); // A directory cannot be replaced by a configuration file.
    Reject(() => impossible.ImportOnce(identity, "legacy"), "failed import save reports failure");
    Check(!impossible.Values.ContainsKey(identity), "failed import removes incomplete migration marker");
    var metadata = new ModMetadata();
    const string hps = "sprocket.hydropneumatic", mast = "sprocket.telescopicmast";
    Check(metadata.Resolve(hps, "old", false).Mod == new ModInfo("Nero", "HPS"), "known alias without consumer changes");
    Check(metadata.Resolve("external.mod", "Example", false).Mod == new ModInfo("Unknown", "Example"), "unknown author uses supplied mod name");
    Check(metadata.Resolve(hps, "old", false).GroupKey == metadata.Resolve(mast, "old", false).GroupKey, "two owners share author parent");
    var before = BindingStore.StableGuid(hps + "/raise", "binding0");
    metadata.Register(hps, "External author", "Renamed");
    Check(metadata.Resolve(hps, "old", false).Mod == new ModInfo("External author", "Renamed"), "late explicit metadata overrides alias");
    Check(before == BindingStore.StableGuid(hps + "/raise", "binding0"), "metadata preserves input GUID");
    metadata.Register(hps, "External author", "Renamed");
    Reject(() => metadata.Register(hps, "Other", "Renamed"), "conflicting metadata rejected");
    metadata.Register("external.one", "Author A", "Same"); metadata.Register("external.two", "Author A", "Same");
    var ordered = metadata.Order(new[] { ("external.two", "x"), ("external.one", "x") }, false);
    Check(ordered.Length == 2 && ordered[0].Owner == "external.one", "duplicate display names retain owners and deterministic order");
    metadata.Register("external.three", "Author B", "Same");
    Check(metadata.Resolve("external.three", "x", false).GroupKey != ordered[0].GroupKey, "identical mod names across authors isolated");
    Reject(() => metadata.Register("external.bad", "", "Name"), "empty metadata rejected");
    Reject(() => metadata.Register("external.bad", "A\nB", "Name"), "control characters rejected");
    Reject(() => metadata.Register("external.bad", "Author", "<size=90>Bad</size>"), "rich text metadata rejected");
    metadata.Register("external.long", "Unicode ÃŽÂ© & Author", new string('x', 128));
    Check(metadata.Resolve("external.long", "x", false).Mod.Name.Length == 128, "long Unicode display text accepted for ellipsis rendering");
    Reject(() => metadata.Register("external.bad", "Author", new string('x', 129)), "excessive display length rejected");
    metadata.RegisterPack(hps, "example.pack", "Example Pack");
    Check(metadata.Resolve(hps, "x", true).GroupKey == "pack:example.pack", "explicit primary pack grouping");
    Check(metadata.Resolve(mast, "x", true).GroupKey == "author:Nero", "standalone mod does not inherit pack membership");
    Reject(() => metadata.RegisterPack(hps, "second.pack", "Other"), "ambiguous pack membership rejected");
    Reject(() => metadata.RegisterPack(mast, "example.pack", "Conflicting name"), "conflicting pack label rejected");
    var transaction = new BindingStore(Path.Combine(temp, "grouping.json")); transaction.Values[identity] = "saved"; transaction.Commit();
    transaction.Begin(); transaction.Values[identity] = "pending";
    metadata.Order(new[] { (hps, "HPS"), (mast, "Mast") }, false);
    metadata.Order(new[] { (hps, "HPS"), (mast, "Mast") }, true);
    Check(transaction.Editing && transaction.Values[identity] == "pending", "group switch preserves pending transaction");
    transaction.End(); Check(transaction.Values[identity] == "saved", "Cancel still restores after group switch");
    var unbound = "{\"bindings\":[{\"path\":\"\"}]}";
    transaction.Values[identity] = unbound; transaction.Commit();
    var restarted = new BindingStore(Path.Combine(temp, "grouping.json")); restarted.Load();
    Check(restarted.Values[identity] == unbound && !restarted.ImportOnce(identity, "old key"), "explicit unbound persists and blocks legacy reimport");
    const string oldId = "legacy.sight/toggle", newId = "sprocket.sight/toggle";
    string FullOverrides(string id) => JsonSerializer.Serialize(new { bindings = new[] {
        new { action = "Mod/toggle", id = BindingStore.StableGuid(id, "binding0"), path = "", interactions = "hold(duration=0.5)", processors = "null" },
        new { action = "Mod/toggle", id = BindingStore.StableGuid(id, "binding1"), path = "<Mouse>/middleButton", interactions = "press", processors = "invert" }
    }});
    var migratePath = Path.Combine(temp, "identity-migration.json");
    var identities = new BindingStore(migratePath);
    var sourceJson = FullOverrides(oldId);
    identities.Values[oldId] = sourceJson; identities.Commit();
    Check(identities.MigrateIdentity(oldId, newId), "full identity migration persisted");
    Check(identities.Values[oldId] == sourceJson, "old record preserved byte-for-byte for rollback");
    using (var mapped = JsonDocument.Parse(identities.Values[newId]))
    {
        var slots = mapped.RootElement.GetProperty("bindings");
        Check(slots[0].GetProperty("path").GetString() == "" && slots[1].GetProperty("path").GetString() == "<Mouse>/middleButton", "primary unbound and secondary path preserved");
        Check(slots[0].GetProperty("interactions").GetString() == "hold(duration=0.5)" && slots[1].GetProperty("processors").GetString() == "invert", "interactions and processors preserved");
        Check(slots[0].GetProperty("id").GetString() == BindingStore.StableGuid(newId, "binding0") && slots[1].GetProperty("id").GetString() == BindingStore.StableGuid(newId, "binding1"), "both deterministic binding GUIDs remapped");
    }
    var reloadedMigration = new BindingStore(migratePath); reloadedMigration.Load();
    Check(reloadedMigration.Values[newId] == identities.Values[newId], "migrated slots survive restart");
    Check(!identities.MigrateIdentity(oldId, newId), "migration repeated without overwriting target");
    identities.Values[newId] = "";
    Check(!identities.MigrateIdentity(oldId, newId) && identities.Values[newId] == "", "even empty target record wins");
    identities.Values[newId] = "{\"bindings\":[]}";
    Check(!identities.MigrateIdentity(oldId, newId), "target factory-reset record wins");
    identities.Values[newId] = FullOverrides(newId);
    Check(!identities.MigrateIdentity(oldId, newId) && identities.Values[newId] == FullOverrides(newId), "explicit unbound target slots win unchanged");
    Check(!identities.MigrateIdentity("absent.sight/toggle", "new.sight/toggle"), "missing source is no-op");
    identities.Begin(); Reject(() => identities.MigrateIdentity(oldId, "new.sight/toggle"), "identity migration rejects edit transaction"); identities.End();
    var renamed = BindingIdentityMigration.Remap(sourceJson, oldId, "sprocket.sight/activate");
    Check(JsonDocument.Parse(renamed).RootElement.GetProperty("bindings")[1].GetProperty("action").GetString() == "Mod/activate", "action name also remapped");
    var actionGuidJson = sourceJson.Replace("Mod/toggle", BindingStore.StableGuid(oldId, "action"));
    Check(BindingIdentityMigration.Remap(actionGuidJson, oldId, newId).Contains(BindingStore.StableGuid(newId, "action")), "GUID-form action remapped");
    var resetTarget = "sprocket.sight/reset";
    identities.Values["legacy.sight/reset"] = "{\"bindings\":[]}";
    Check(identities.MigrateIdentity("legacy.sight/reset", resetTarget) && identities.Values[resetTarget] == "{\"bindings\":[]}", "source factory-reset record migrated without redefining defaults");
    foreach (var bad in new[] { "{bad", "{\"bindings\":[],\"Version\":2}", "{\"bindings\":[],\"bindings\":[]}", "{\"bindings\":{}}",
        sourceJson.Replace("<Mouse>/middleButton", "X").Replace("\"processors\":\"invert\"", "\"futureField\":\"invert\""),
        sourceJson.Replace(BindingStore.StableGuid(oldId, "binding1"), BindingStore.StableGuid(oldId, "binding0")),
        sourceJson.Replace(BindingStore.StableGuid(oldId, "binding1"), Guid.NewGuid().ToString()),
        sourceJson.Replace("Mod/toggle", "Mod/other"), sourceJson.Replace("\"path\":\"\"", "\"path\":12") })
    {
        identities.Values[oldId] = bad;
        var fileBefore = File.ReadAllBytes(migratePath);
        Reject(() => identities.MigrateIdentity(oldId, "new.bad/toggle"), "malformed/unknown source rejects atomically");
        Check(!identities.Values.ContainsKey("new.bad/toggle") && File.ReadAllBytes(migratePath).SequenceEqual(fileBefore), "rejected migration leaves memory and file intact");
    }
    var failingMigration = new BindingStore(temp);
    failingMigration.Values[oldId] = sourceJson;
    Reject(() => failingMigration.MigrateIdentity(oldId, newId), "identity migration save failure reported");
    Check(!failingMigration.Values.ContainsKey(newId) && failingMigration.Values[oldId] == sourceJson, "save failure retains source and removes partial target");
    var plain = ButtonBinding.Parse("<Keyboard>/x");
    var shift = ButtonBinding.Parse("Shift+<Keyboard>/x");
    var triple = ButtonBinding.Parse("Ctrl+Shift+<Keyboard>/x");
    Check(plain.Canonical == "<keyboard>/x" && plain.Display == "X", "plain path parse and display");
    Check(shift.Display == "Shift+X" && ButtonBinding.Parse("Ctrl+Alt+<Keyboard>/f").Display == "Ctrl+Alt+F", "combo display");
    Check(ButtonBinding.Parse("LeftShift+<Keyboard>/X") == shift && ButtonBinding.Parse("<Keyboard>/rightShift+<Keyboard>/x") == shift,
        "left/right modifier aliases normalize for conflicts");
    Check(plain != shift && ButtonBinding.Parse("RightCtrl+LeftShift+<Keyboard>/x") == triple, "plain and combo conflicts remain distinct");
    foreach (var value in new[] { "", "<Keyboard>/leftShift", "<Mouse>/backButton", "Shift+<Keyboard>/x", "Ctrl+Alt+Shift+<Keyboard>/f12", "Alt+<Mouse>/middleButton" })
    {
        var parsed = ButtonBinding.Parse(value);
        Check(ButtonBinding.Parse(parsed.Canonical) == parsed, "binding path format round trip: " + value);
    }
    foreach (var bad in new[] { "Shift+X", "Super+<Keyboard>/x", "Shift+RightShift+<Keyboard>/x", "Shift+<Keyboard>/leftShift", "<Keyboard>/*", "<Keyboard>/", "<Mouse>/x/y", "<Keyboard>/x\nY" })
        Reject(() => ButtonBinding.Parse(bad), "invalid combo rejected: " + bad.Replace("\n", "\\n"));
    var allBindings = new[] { plain, shift, triple };
    Check(ButtonBinding.Wins(plain, allBindings, Modifiers.None), "X wins without modifiers");
    Check(!ButtonBinding.Wins(plain, allBindings, Modifiers.Shift) && ButtonBinding.Wins(shift, allBindings, Modifiers.Shift), "Shift+X suppresses X");
    Check(!ButtonBinding.Wins(plain, allBindings, Modifiers.Ctrl | Modifiers.Shift)
        && !ButtonBinding.Wins(shift, allBindings, Modifiers.Ctrl | Modifiers.Shift)
        && ButtonBinding.Wins(triple, allBindings, Modifiers.Ctrl | Modifiers.Shift), "Ctrl+Shift+X wins over both less specific binds");
    Check(ButtonBinding.Wins(plain, new[] { plain }, Modifiers.Shift | Modifiers.Ctrl), "plain without a competing combo works with modifiers");
    Check(ButtonBinding.Wins(shift, allBindings, Modifiers.Shift | Modifiers.Alt), "extra unrelated modifier permits a combo");
    var plainLatch = new BindingLatch(); var shiftLatch = new BindingLatch(); var tripleLatch = new BindingLatch();
    Check(plainLatch.Step(plain, true, true, Modifiers.Shift, allBindings) == (false, false, false), "suppressed plain has no held/pressed edge");
    Check(shiftLatch.Step(shift, true, true, Modifiers.Shift, allBindings) == (true, true, false), "modifier-first combo presses");
    Check(shiftLatch.Step(shift, true, false, Modifiers.Shift, allBindings) == (true, false, false), "combo remains held, press is non-repeating");
    Check(shiftLatch.Step(shift, true, false, Modifiers.None, allBindings) == (false, false, true), "modifier release releases combo");
    Check(shiftLatch.Step(shift, true, false, Modifiers.Shift, allBindings) == (false, false, false), "modifier repress cannot retrigger while X remains held");
    shiftLatch.Step(shift, false, false, Modifiers.None, allBindings);
    Check(shiftLatch.Step(shift, true, true, Modifiers.Shift, allBindings).Pressed, "main-key repress rearms combo");
    Check(shiftLatch.Step(shift, false, false, Modifiers.Shift, allBindings).Released, "main-key release releases combo");
    var reversed = new BindingLatch();
    Check(!reversed.Step(shift, true, true, Modifiers.None, allBindings).Pressed
        && !reversed.Step(shift, true, false, Modifiers.Shift, allBindings).Held, "key-before-modifier never activates combo");
    var sameFrameReverse = new BindingLatch();
    Check(!sameFrameReverse.Step(shift, true, true, Modifiers.Shift, allBindings, true, Modifiers.None).Pressed,
        "ordered key-before-modifier within one frame is rejected");
    Check(tripleLatch.Step(triple, true, true, Modifiers.Ctrl | Modifiers.Shift, allBindings) == (true, true, false), "most-specific latch activates");
    Check(tripleLatch.Step(triple, true, false, Modifiers.Ctrl | Modifiers.Shift, allBindings, false) == (false, false, true), "settings/focus suppression clears active combo");
    Check(!tripleLatch.Step(triple, true, false, Modifiers.Ctrl | Modifiers.Shift, allBindings).Held, "closing settings does not rearm a held combo");
    var tap = new BindingLatch();
    Check(tap.Step(shift, false, true, Modifiers.Shift, allBindings) == (false, true, true), "same-frame combo tap retains press/release edges");
    var modifierTap = new BindingLatch();
    Check(modifierTap.Step(shift, true, true, Modifiers.None, allBindings, true, Modifiers.Shift, Modifiers.None) == (false, true, true),
        "same-frame modifier release retains combo edges while the main key stays down");
    var modifierCycle = new BindingLatch();
    Check(modifierCycle.Step(shift, true, true, Modifiers.Shift, allBindings, true, Modifiers.Shift, Modifiers.None) == (false, true, true)
        && !modifierCycle.Step(shift, true, false, Modifiers.Shift, allBindings, true, Modifiers.Shift, Modifiers.None).Held,
        "same-frame modifier release/repress cannot keep or rearm combo");
    var secondary = ButtonBinding.Parse("<Keyboard>/y");
    var secondaryLatch = new BindingLatch();
    var suppressedPrimary = new BindingLatch().Step(plain, true, true, Modifiers.Shift, allBindings);
    var pressedSecondary = secondaryLatch.Step(secondary, true, true, Modifiers.Shift, allBindings.Append(secondary).ToArray());
    Check(BindingLatch.Combine(false, new[] { suppressedPrimary, pressedSecondary }, false, true) == (true, true, false),
        "suppressed native primary cannot swallow secondary press");
    Check(BindingLatch.Combine(true, new[] { (true, false, false), (true, true, false) }, false, true) == (true, false, false),
        "another held slot prevents duplicate aggregate press");
    Check(BindingLatch.Combine(true, new[] { (true, true, false) }, true, true).Pressed, "native same-frame re-press remains visible");
    Check(!BindingLatch.Combine(true, new[] { (false, false, true), (true, false, false) }, false, true).Released,
        "releasing one slot does not release another held slot");
    Check(BindingLatch.Combine(true, new[] { (false, false, true) }, false, true).Released,
        "aggregate release occurs when the final eligible slot releases");
    Check(BindingLatch.Combine(true, new[] { (true, true, true) }, true, false) == (false, false, false), "configuring suppresses all aggregate reads");

    var captureCombo = new ButtonCapture();
    captureCombo.Event("<Keyboard>/leftShift", true, Modifiers.Shift);
    captureCombo.Event("<Keyboard>/x", true, Modifiers.Shift);
    Check(captureCombo.Result == shift.Canonical, "capture modifier plus main key");
    var captureModifier = new ButtonCapture();
    captureModifier.Event("<Keyboard>/rightShift", true, Modifiers.Shift);
    Check(captureModifier.Result == null, "modifier-only capture waits for release");
    captureModifier.Event("<Keyboard>/rightShift", false, Modifiers.None);
    Check(captureModifier.Result == "<keyboard>/rightshift", "modifier-only capture preserves physical side");
    var escapeCapture = new ButtonCapture(); escapeCapture.Event("<Keyboard>/escape", true, Modifiers.None);
    Check(escapeCapture.Cancelled && escapeCapture.Result == null, "Escape cancels capture");
    var mouseCapture = new ButtonCapture(); mouseCapture.Event("<Mouse>/middleButton", true, Modifiers.None);
    Check(mouseCapture.Result == "<mouse>/middlebutton", "mouse capture retained");
    var multipleCapture = new ButtonCapture();
    multipleCapture.Event("<Keyboard>/rightCtrl", true, Modifiers.Ctrl);
    multipleCapture.Event("<Keyboard>/leftAlt", true, Modifiers.Ctrl | Modifiers.Alt);
    multipleCapture.Event("<Keyboard>/f", true, Modifiers.Ctrl | Modifiers.Alt);
    Check(multipleCapture.Result == ButtonBinding.Parse("Ctrl+Alt+<Keyboard>/f").Canonical, "capture multiple modifiers");

    var comboId = BindingStore.StableGuid(oldId, "binding1");
    var comboPath = ButtonBinding.Parse("LeftShift+<Mouse>/middleButton").Canonical;
    var extension = new Dictionary<string, string> { [comboId] = comboPath };
    var combined = ComboRecord.Write(sourceJson, extension);
    var decoded = ComboRecord.Read(combined);
    Check(decoded.Combos[comboId] == comboPath && JsonDocument.Parse(decoded.Native).RootElement.GetProperty("bindings")[1].GetProperty("path").GetString() == "<Mouse>/middleButton",
        "combo extension round trip leaves native base path usable");
    Check(!decoded.Native.Contains(ComboRecord.Field) && ComboRecord.Read(sourceJson).Combos.Count == 0, "native loader sees no extension; legacy record has no combos");
    var migratedCombo = ComboRecord.Read(BindingIdentityMigration.Remap(combined, oldId, newId));
    Check(migratedCombo.Combos[BindingStore.StableGuid(newId, "binding1")] == comboPath, "identity migration remaps combo metadata too");
    Reject(() => ComboRecord.Read("{\"bindings\":[],\"sprocketCombos\":[]}"), "malformed combo extension rejected");
    Reject(() => ComboRecord.Read(ComboRecord.Write(sourceJson, new Dictionary<string,string> { [comboId] = "<Keyboard>/x" })), "plain combo metadata rejected");
    Reject(() => BindingIdentityMigration.Remap(ComboRecord.Write(sourceJson, new Dictionary<string,string> { [comboId] = "Shift+<Keyboard>/x" }), oldId, newId), "mismatched combo and native path rejected");
    var preservation = new BindingStore(Path.Combine(temp, "released-records.json"));
    var fixtures = new[] { sourceJson, "{ \"bindings\" : [ { \"path\" : \"\" } ] }", "{ \"bindings\" : [] }", "{\"bindings\":[{\"path\":\"<Keyboard>/X\"}]}" };
    for (int i = 0; i < fixtures.Length; i++) preservation.Values["legacy.mod/action" + i] = fixtures[i];
    preservation.Values["absent.owner/action"] = combined; preservation.Commit(); preservation.Load(); preservation.Begin();
    foreach (var pair in preservation.Values.ToArray())
    {
        var parsed = ComboRecord.Read(pair.Value);
        var remembered = new PreservedRecord(); remembered.Remember(pair.Value, parsed.Native, parsed.Combos);
        preservation.Values[pair.Key] = remembered.Save(parsed.Native, parsed.Combos);
    }
    preservation.Commit(); preservation.End(); preservation.Load();
    Check(fixtures.Select((value, i) => preservation.Values["legacy.mod/action" + i] == value).All(v => v)
        && preservation.Values["absent.owner/action"] == combined, "plain/unbound/reset/absent-owner records remain byte-for-byte identical after no-op Apply");
    preservation.Begin(); preservation.Values["legacy.mod/action0"] = combined; preservation.End();
    Check(preservation.Values["legacy.mod/action0"] == sourceJson, "Cancel rolls back combo edit");
    preservation.Begin(); preservation.Values["legacy.mod/action0"] = combined; preservation.Commit();
    preservation.Values["legacy.mod/action0"] = fixtures[2]; preservation.End(); preservation.Load();
    Check(preservation.Values["legacy.mod/action0"] == combined, "combo Apply then reset/Cancel retains applied combo after restart");
    var preserved = new PreservedRecord(); preserved.Remember(combined, decoded.Native, decoded.Combos);
    Check(preserved.Save(decoded.Native, new Dictionary<string,string>()) == decoded.Native, "explicit change to plain removes only combo extension");

    var free = FreeKeys.Calculate(new[] { "<Keyboard>/A", "<Keyboard>/f1" }, new[] { shift, ButtonBinding.Parse("<Keyboard>/b"), ButtonBinding.Parse("<Keyboard>/5") });
    Check(!free.Contains("a") && !free.Contains("b") && !free.Contains("f1") && !free.Contains("5") && free.Contains("x"), "free keys exclude vanilla and plain mods but include combo-only main keys");
    Check(!free.Any(k => new[] { "escape", "enter", "tab", "leftmeta", "rightmeta", "numpadenter" }.Contains(k)), "reserved keys excluded from candidate list");
    Check(free.Contains("numpad0") && free.Contains("f12") && free.Contains("semicolon") && free.Contains("insert") && free.Contains("uparrow"), "candidate groups include numpad/functions/punctuation/navigation/arrows");
    Check(FreeKeys.Calculate(Array.Empty<string>(), new[] { plain }).Contains("x") == false
        && FreeKeys.Calculate(Array.Empty<string>(), new[] { shift }).Contains("x"), "free calculation updates after plain-to-combo rebind");
    Check(FreeKeys.Calculate(Array.Empty<string>(), Array.Empty<ButtonBinding>(), false).Length == 0, "unknown vanilla never advertises a free key");
    var singles = FreeKeys.SinglePaths(new[] {
        new FreeKeys.NativeBinding("ButtonWithOneModifier", true), new FreeKeys.NativeBinding("<Keyboard>/leftShift", false, true), new FreeKeys.NativeBinding("<Keyboard>/x", false, true),
        new FreeKeys.NativeBinding("2DVector", true), new FreeKeys.NativeBinding("<Keyboard>/w", false, true), new FreeKeys.NativeBinding("<Keyboard>/s", false, true), new FreeKeys.NativeBinding("<Keyboard>/f1") }).ToArray();
    Check(singles.SequenceEqual(new[] { "<Keyboard>/w", "<Keyboard>/s", "<Keyboard>/f1" }), "vanilla modifier composites excluded but movement composite keys reserved");
    var vanillaOverrides = FreeKeys.ReadNativeOverrides(sourceJson);
    Check(vanillaOverrides[BindingStore.StableGuid(oldId, "binding0")] == "" && vanillaOverrides[comboId] == "<Mouse>/middleButton",
        "vanilla RebindMap JSON preserves unbound and concrete overrides");
    Check(FreeKeys.ReadNativeOverrides(sourceJson.Replace("\"path\":\"\"", "\"path\":\"null\""))[BindingStore.StableGuid(oldId, "binding0")] == null,
        "Unity null sentinel in vanilla overrides falls back to factory path");
    Reject(() => FreeKeys.ReadNativeOverrides("{\"bindings\":[{\"id\":\"bad\",\"path\":\"<Keyboard>/x\"}]}"), "invalid vanilla override rejects instead of advertising free keys");
    if (args.Length >= 2)
    {
        Check(ApiSurface.ReleaseVersion(args[^1]) == "0.1.10", "built release metadata is 0.1.10 (version-value ABI exception)");
        Check(ApiSurface.PluginGuid(args[^1]) == "sprocket.keybinds", "built plugin GUID is sprocket.keybinds (plugin-GUID ABI exception)");
        var currentApi = ApiSurface.Read(args[^1]);
        var additions = currentApi.Except(ApiSurface.Read(args[0])).OrderBy(s => s, StringComparer.Ordinal).ToArray();
        var reportRoot = new DirectoryInfo(AppContext.BaseDirectory);
        while (reportRoot != null && !File.Exists(Path.Combine(reportRoot.FullName, "SprocketKeybinds.csproj"))) reportRoot = reportRoot.Parent;
        File.WriteAllLines(Path.Combine(reportRoot!.FullName, "logs", "public-api-added.txt"), additions);
        Console.WriteLine($"Public API additions: {additions.Length} (logs/public-api-added.txt)");
        foreach (var baseline in args.SkipLast(1))
        {
            var previousApi = ApiSurface.Read(baseline);
            Check(ApiSurface.ReleaseVersion(baseline) == "0.1.9" && ApiSurface.PluginGuid(baseline) == "sprocket.keybinds", "baseline is 0.1.9 with the same plugin GUID");
            Check(Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(baseline))).StartsWith("1EC9E2D611D0", StringComparison.Ordinal), "baseline SHA256 prefix matches brief");
            Check(previousApi.Count == 44, "baseline contains exactly 44 public ABI members");
            var missing = previousApi.Except(currentApi).ToArray();
            foreach (var member in missing) Console.WriteLine("MISSING ABI: " + member);
            Check(missing.Length == 0, $"all {previousApi.Count} public ABI members and optional defaults preserved: {baseline}");
        }
    }
    // 0.1.7 configs store "" for actions without overrides (seen live for hydro/smoke/dynamicsight).
    var emptyRecord = ComboRecord.Read("");
    Check(emptyRecord.Native == "" && emptyRecord.Combos.Count == 0, "empty legacy record reads as no overrides");
    var emptyState = new PreservedRecord();
    emptyState.Remember("", "", new Dictionary<string, string>());
    Check(emptyState.Save("", new Dictionary<string, string>()) == "", "empty record without combos saves unchanged");
    Check(FreeKeys.ReadNativeOverrides("").Count == 0, "empty vanilla override record means no overrides");
    // Optional: read every record of a real config (copied first, the original is never written).
    var realConfig = Environment.GetEnvironmentVariable("KEYBINDS_CONFIG");
    if (!string.IsNullOrEmpty(realConfig))
    {
        var copy = Path.Combine(temp, "real.keybinds.json");
        File.Copy(realConfig, copy);
        var real = new BindingStore(copy); real.Load();
        var unreadable = real.Values.Where(p => { try { ComboRecord.Read(p.Value); return false; } catch { return true; } }).Select(p => p.Key).ToArray();
        Check(unreadable.Length == 0, $"all {real.Values.Count} real config records readable ({real.Values.Count(p => p.Value == "")} empty){(unreadable.Length > 0 ? ": " + string.Join(", ", unreadable) : "")}");
    }
    // Il2CppInterop rejects delegates with struct parameters at runtime (0.1.8 failed to load on CallbackContext).
    var sourceRoot = new DirectoryInfo(AppContext.BaseDirectory);
    while (sourceRoot != null && !File.Exists(Path.Combine(sourceRoot.FullName, "SprocketKeybinds.csproj"))) sourceRoot = sourceRoot.Parent;
    Check(sourceRoot != null, "plugin source located for native delegate scan");
    var pluginFiles = Directory.GetFiles(sourceRoot!.FullName, "*.cs");
    Check(pluginFiles.Any(f => Path.GetFileName(f) == "PanelStyle.cs"), "native delegate scan includes PanelStyle.cs");
    Check(pluginFiles.Any(f => Path.GetFileName(f) == "ModSettings.cs"), "native delegate and struct scan includes ModSettings.cs");
    var pluginSources = pluginFiles.Select(File.ReadAllText).ToArray();
    // Only parameterless UnityAction conversions are proven on this runtime.
    Check(!pluginSources.Any(s => System.Text.RegularExpressions.Regex.IsMatch(s, @"ConvertDelegate<(?!UnityAction>)|Il2CppSystem\.Action<|add_performed|add_started|add_canceled")),
        "no IL2CPP delegates with struct parameters");
    var load = File.ReadAllText(Path.Combine(sourceRoot.FullName, "Plugin.cs"));
    Check(System.Text.RegularExpressions.Regex.IsMatch(load, @"try\s*\{\s*InputRuntime\.Initialize\(\);\s*\}"), "combo runtime failure cannot fail Load");
    var panelSource = File.ReadAllText(Path.Combine(sourceRoot.FullName, "ModPanel.cs"));
    var styleSource = File.ReadAllText(Path.Combine(sourceRoot.FullName, "PanelStyle.cs"));
    var projectSource = File.ReadAllText(Path.Combine(sourceRoot.FullName, "SprocketKeybinds.csproj"));
    Check(!panelSource.Contains("SprocketUiKit", StringComparison.OrdinalIgnoreCase)
        && !styleSource.Contains("SprocketUiKit", StringComparison.OrdinalIgnoreCase), "panel and helpers contain no Kit name, reflection or dependency");
    Check(!projectSource.Contains("SprocketUiKit", StringComparison.OrdinalIgnoreCase), "project has no Kit reference");
    // tests/STYLE.md is a snapshot of the UI Kit style guide (0.1.0); the panel tokens must match it exactly.
    var styleGuide = File.ReadAllText(Path.Combine(sourceRoot.FullName, "tests", "STYLE.md"));
    var guideTokens = new Dictionary<string, string[]>(StringComparer.Ordinal);
    foreach (var line in styleGuide.Split('\n').Where(l => l.StartsWith('|')))
    {
        var cells = line.Split('|');
        for (int i = 1; i + 1 < cells.Length; i += 2)
        {
            var hexes = Regex.Matches(cells[i + 1], @"#[0-9a-fA-F]{6}").Select(m => m.Value.ToLowerInvariant()).ToArray();
            if (hexes.Length > 0) guideTokens.Add(cells[i].Trim(), hexes);
        }
    }
    var expectedTokens = new Dictionary<string, string>
    {
        ["Panel"] = guideTokens["Panel"][0], ["Block"] = guideTokens["Header/footer, blocks, popup"][0],
        ["Row"] = guideTokens["Row"][0], ["Button"] = guideTokens["Button"][0], ["Hover"] = guideTokens["Button"][1],
        ["Selected"] = guideTokens["Selected"][0], ["Accent"] = guideTokens["Accent"][0],
        ["Text"] = guideTokens["Text"][0], ["Text2"] = guideTokens["Text 2 / labels"][0], ["Value"] = guideTokens["Value"][0],
        ["Faint"] = guideTokens["Faint (counts, keywords)"][0], ["Heading"] = guideTokens["Section heading"][0],
        ["Frame"] = guideTokens["Frame"][0], ["Divider"] = guideTokens["Divider"][0], ["Border"] = guideTokens["Border"][0],
        ["Disabled"] = guideTokens["Disabled"][0], ["Danger"] = guideTokens["Error"][0], ["DangerText"] = guideTokens["Error"][1],
        ["Warning"] = guideTokens["Error"][2], ["WarningBorder"] = guideTokens["Error"][3]
    };
    var actualTokens = Regex.Matches(styleSource, "readonly Color (\\w+) = Hex\\(\"(#[0-9a-fA-F]{6})\"")
        .ToDictionary(m => m.Groups[1].Value, m => m.Groups[2].Value.ToLowerInvariant());
    foreach (var token in expectedTokens)
        Check(actualTokens.TryGetValue(token.Key, out var value) && value == token.Value, $"STYLE.md token {token.Key}: {token.Value}");
    Check(actualTokens.Count == expectedTokens.Count + 1 && actualTokens["DangerHover"] == "#3a1f17", "only extra token is the brief's Clear danger hover");
    Check(Regex.IsMatch(styleSource, "Panel = Hex\\(\"#14150f\", \\.96f\\)"), "window alpha is .96");
    Check(Regex.IsMatch(projectSource, @"<Version>0\.1\.10</Version>"), "project version is 0.1.10");
    SettingsTests.Run(temp, Check);
    NativeSafetyTests.Run(sourceRoot.FullName, Check);
    Console.WriteLine($"{passed} checks passed.");
}
catch (Exception e) { Console.Error.WriteLine("FAIL " + e); Environment.ExitCode = 1; }
finally { Directory.Delete(temp, true); }
