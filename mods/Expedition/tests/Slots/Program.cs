using System.Security.Cryptography;
using SprocketExpedition.Core.Persistence;
using SprocketExpedition.Core.Run;
using SprocketExpedition.Game;

var project = new DirectoryInfo(AppContext.BaseDirectory);
while (project != null && !File.Exists(Path.Combine(project.FullName, "src", "SprocketExpedition.Game", "Plugin.cs"))) project = project.Parent;
if (project == null) throw new Exception("Project root missing");
var root = project.FullName;
var temp = Path.Combine(Environment.GetEnvironmentVariable("EXPEDITION_TEST_TEMP") ?? Path.GetTempPath(),
    "slot-tests-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(temp);
BepInEx.Paths.ConfigPath = temp;
var count = 0;
void Check(bool ok, string name) { if (!ok) throw new Exception(name); count++; Console.WriteLine("PASS " + name); }
try
{
    Check(CampaignSlotManager.SlotCount == 8, "default eight slots");
    foreach (var size in new[] { 4, 8, 12, -1, 0, 3, 13, int.MaxValue, int.MinValue })
    {
        CampaignSlotManager.Configure(size);
        Check(CampaignSlotManager.SlotCount == Math.Clamp(size, 4, 12), "slot range " + size);
    }
    CampaignSlotManager.Configure(8);
    Check(CampaignSlotManager.Root(1).EndsWith(Path.Combine("slots", "slot-1"))
        && CampaignSlotManager.Root(4).EndsWith(Path.Combine("slots", "slot-4")), "existing paths unchanged");
    foreach (var invalid in new[] { 0, 9 })
    {
        bool rejected = false;
        try { CampaignSlotManager.Root(invalid); } catch (ArgumentOutOfRangeException) { rejected = true; }
        Check(rejected, "slot index rejected " + invalid);
    }
    Check(CampaignSlotManager.IsEmpty(8), "missing slot is empty without creating run");
    var store = CampaignSlotManager.Store(8);
    CampaignSlotManager.Write(8, store);
    Check(CampaignSlotManager.IsEmpty(8), "summary-only slot stays empty");
    Check(store.ListRuns().Runs.Count == 0, "empty inspection creates no run");
    foreach (var allow in new[] { false, true })
    {
        Check(CampaignSlotPolicy.UseFixture(null, false, allow) == allow, "new fixture requires dev option " + allow);
        Check(!CampaignSlotPolicy.UseFixture(null, true, allow), "native new game never falls back " + allow);
        Check(CampaignSlotPolicy.UseFixture("fixture.firstplay.1", false, allow), "existing fixture save still loads " + allow);
        Check(!CampaignSlotPolicy.UseFixture("native.cost.1.saved", false, allow), "native save stays native " + allow);
    }
    var payload = new byte[] { 1, 7, 9, 42 };
    File.WriteAllBytes(Path.Combine(CampaignSlotManager.Root(8), "archive-proof.bin"), payload);
    bool started = false;
    Check(CampaignSlotPolicy.ArchiveAndStart(8, CampaignSlotManager.ArchiveSlot, slot =>
    {
        started = slot == 8 && !Directory.Exists(CampaignSlotManager.Root(slot));
    }) && started, "archive completes before new game in the same slot");
    var archived = Directory.GetFiles(Path.Combine(temp, "SprocketExpedition", "slots", "archive"), "archive-proof.bin", SearchOption.AllDirectories);
    Check(archived.Length == 1 && File.ReadAllBytes(archived[0]).SequenceEqual(payload), "archived bytes preserved");
    started = false;
    Check(!CampaignSlotPolicy.ArchiveAndStart(3, _ => false, _ => started = true) && !started, "archive failure never starts new game");
    var game = Path.Combine(root, "src", "SprocketExpedition.Game");
    var host = File.ReadAllText(Path.Combine(game, "CampaignControllerHost.cs"));
    var menu = File.ReadAllText(Path.Combine(game, "CampaignNativeMenu.cs"));
    var kit = File.ReadAllText(Path.Combine(game, "CampaignKitMenu.cs"));
    var flow = File.ReadAllText(Path.Combine(game, "CampaignNativeFlow.cs"));
    var plugin = File.ReadAllText(Path.Combine(game, "Plugin.cs"));
    Check(menu.Contains("else if (slotsFull)\n".Replace("\n", "\r\n")) || menu.Contains("else if (slotsFull)"), "full screen takes priority over controller view");
    Check(menu.Contains("slotsFull ? \"Delete & start new\"") && menu.Contains("if (!slotsFull)"), "full list exposes delete and start instead of open");
    Check(menu.Contains("new MenuButton(\"BACK\", Close)") && menu.Contains("next.CloseAction = Close;"), "full list has Back and Esc");
    Check(menu.Contains("CampaignControllerHost.DeleteAndStartNew(selected)") && host.Contains("CampaignSlotPolicy.ArchiveAndStart"), "single confirmation uses tested archive-first flow");
    Check(host.Contains("CampaignNativeMenu.ShowNewGameOptions(slot)") && menu.Contains("BeginNewGame(startingEraSelection, ironmanSelection, newGameSlot)"), "empty slot opens native era and Ironman options");
    Check(flow.Contains("NewGameSlot = slot;") && host.Contains("CampaignSlotManager.IsEmpty(CampaignNativeFlow.NewGameSlot)"), "requested slot survives editor handoff and is rechecked");
    Check(host.Contains("No run was created.") && host.Contains("Plugin.AllowFixtureCatalogue?.Value == true"), "native unavailable cannot create implicit fixture");
    Check(plugin.Contains("\"AllowFixtureCatalogue\", false") && plugin.Contains("\"SaveSlots\", 8"), "config defaults fixture off and eight slots");
    Check(menu.Contains("Plugin.AllowFixtureCatalogue?.Value == true && !CampaignControllerHost.UsesNativeContent")
        && menu.Contains("CampaignControllerHost.StartNew();"), "developer fixture start is reachable only behind config");
    Check(menu.Contains("if (pendingArchive != selected) return;"), "stale confirmation cannot delete twice");
    Check(kit.Contains("column.ScrollView().Fill()") && kit.Contains("scroll.Repeat(() => page.CardRows"), "all slot rows are inside scrolling body");
    // The two saved campaign slots are not part of the source tree (they hold saved tank blueprints that other
    // mods wrote their settings into). Point EXPEDITION_FIXTURES at a folder with slot-1 and slot-2.
    var fixtures = Environment.GetEnvironmentVariable("EXPEDITION_FIXTURES");
    var haveFixtures = !string.IsNullOrWhiteSpace(fixtures) && Directory.Exists(fixtures);
    if (!haveFixtures) Console.WriteLine("SKIP supplied slots: set EXPEDITION_FIXTURES to a folder with slot-1 and slot-2");
    foreach (var slot in haveFixtures ? new[] { 1, 2 } : Array.Empty<int>())
    {
        var fixture = Path.Combine(fixtures!, "slot-" + slot);
        var before = Directory.GetFiles(fixture, "*", SearchOption.AllDirectories).ToDictionary(p => p, p => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(p))));
        var savedStore = new RunStore(fixture);
        var load = savedStore.LoadActiveRun(ModAvailability.None);
        Check(load.CanResume && load.Snapshot != null, "supplied slot " + slot + " loads");
        _ = RunDirector.Restore(load.Snapshot!);
        var prepared = CampaignResumePreparation.Read(savedStore, ModAvailability.None);
        Check(prepared.Error == null && prepared.Blueprint?.Bytes.Length > 0, "supplied slot " + slot + " restores and verifies design");
        Check(before.All(pair => pair.Value == Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(pair.Key))))
            && before.Count == Directory.GetFiles(fixture, "*", SearchOption.AllDirectories).Length, "supplied slot " + slot + " remains byte-identical");
    }
    Console.WriteLine($"PASS Slots: {count} checks; managed behavior and source wiring (no live UI)");
    return 0;
}
catch (Exception ex) { Console.Error.WriteLine("FAIL " + ex); return 1; }
finally { Directory.Delete(temp, true); }
