using SprocketColdWarExpansionPack;
var checks = 0;
void Check(bool ok, string label) { checks++; if (!ok) throw new Exception(label); }
var timelines = new[] {
    new[] { new DateTime(1914,7,28) },
    new[] { new DateTime(1914,7,28), new DateTime(1939,9,2) },
    new[] { new DateTime(1914,7,28), new DateTime(1945,9,3) },
    new[] { new DateTime(1914,7,28), new DateTime(2100,1,1), new DateTime(3000,1,1) },
    new[] { new DateTime(1900,1,1), new DateTime(9999,12,30) }
};
foreach (var starts in timelines)
{
    Check(ColdWarRules.ConfirmLastEra(starts) == starts.Length - 1, "arbitrary native timeline");
    for (var index = -1; index <= starts.Length; index++)
    {
        Check(ColdWarRules.RecognizeLastEraSentinel(true, true, index, starts) == (index == starts.Length - 1), "only final index at exact sentinel");
        Check(!ColdWarRules.RecognizeLastEraSentinel(true, false, index, starts), "finite dates unchanged");
        Check(!ColdWarRules.RecognizeLastEraSentinel(false, true, index, starts), "disabled repair unchanged");
    }
}
foreach (var invalid in new[] { Array.Empty<DateTime>(), new[] { DateTime.MaxValue },
    new[] { new DateTime(1945,9,3), new DateTime(1945,9,3) },
    new[] { new DateTime(1945,9,3), new DateTime(1900,1,1) } })
{
    Check(ColdWarRules.ConfirmLastEra(invalid) == -1, "invalid timeline rejected");
    Check(!ColdWarRules.RecognizeLastEraSentinel(true, true, invalid.Length - 1, invalid), "invalid sentinel unchanged");
}
var zeroTimeline = new[] { ColdWarRules.CalendarKey(0,1,1), ColdWarRules.CalendarKey(1914,7,28) };
Check(ColdWarRules.CalendarKey(0,1,1) == 101, "native year-zero date");
Check(ColdWarRules.CalendarKey(0,2,29) == 229, "native year-zero leap date");
Check(ColdWarRules.CalendarKey(0,2,30) == -1, "invalid year-zero day");
Check(ColdWarRules.RecognizeLastEraSentinel(true,true,1,zeroTimeline), "native timeline beginning at year zero");
Check(!ColdWarRules.RecognizeLastEraSentinel(true,true,0,zeroTimeline), "year-zero timeline earlier index unaffected");
Check(ColdWarRules.ConfirmLastEra(new[] { 101, 99991231 }) == -1, "native sentinel start rejected");
foreach(var starts in timelines)
    Check(ColdWarRules.ConfirmLastEra(starts.Select(d=>ColdWarRules.CalendarKey(d.Year,d.Month,d.Day)).ToArray()) == starts.Length-1, "native calendar keys match calendar timeline");
// Era files shipped by the pack (variant B): exactly Coldwar1/2/3, not the game's own placeholder Coldwar.json (1945.09.03, not playable).
// The game's real timeline is the stock eras plus these files; the final-era repair needs strictly rising starts.
string eraDir = Environment.GetEnvironmentVariable("CWEP_ERA_DIR") ?? ""; // optional override; default: the data/Eras folder above the build output
for (var d = new DirectoryInfo(AppContext.BaseDirectory); d != null && eraDir.Length == 0; d = d.Parent)
    if (Directory.Exists(Path.Combine(d.FullName, "data", "Eras"))) eraDir = Path.Combine(d.FullName, "data", "Eras");
Check(eraDir.Length > 0, "data/Eras folder found");
var shipped = Directory.GetFiles(eraDir, "*.json").OrderBy(f => f, StringComparer.Ordinal).ToArray();
Check(shipped.Select(Path.GetFileName).SequenceEqual(new[] { "Coldwar1.json", "Coldwar2.json", "Coldwar3.json" }), "pack ships exactly Coldwar1/2/3 and not the game's own Coldwar.json");
var packStarts = new List<DateTime>();
foreach (var file in shipped)
{
    using var doc = System.Text.Json.JsonDocument.Parse(File.ReadAllText(file), new System.Text.Json.JsonDocumentOptions { AllowTrailingCommas = true });
    var root = doc.RootElement;
    Check(root.GetProperty("name").GetString() == Path.GetFileNameWithoutExtension(file), "era name equals file name");
    Check(root.GetProperty("playable").GetBoolean(), "shipped era is playable");
    Check(root.GetProperty("mediumVehicleMass").GetInt32() > 0 && root.GetProperty("heavyVehicleMass").GetInt32() > root.GetProperty("mediumVehicleMass").GetInt32(), "mass classes rise");
    packStarts.Add(DateTime.ParseExact(root.GetProperty("start").GetString() ?? "", "yyyy.MM.dd", System.Globalization.CultureInfo.InvariantCulture));
}
Check(packStarts[0] == new DateTime(1945, 9, 4) && packStarts[1] == new DateTime(1960, 1, 1) && packStarts[2] == new DateTime(1975, 1, 1), "pack era starts 1945.09.04, 1960.01.01, 1975.01.01");
var gameStockStarts = new[] { new DateTime(1914,7,28), new DateTime(1918,11,12), new DateTime(1939,9,2), new DateTime(1942,1,1), new DateTime(1944,1,1), new DateTime(1945,9,3) };
var fullTimeline = gameStockStarts.Concat(packStarts).ToArray();
Check(ColdWarRules.ConfirmLastEra(fullTimeline) == fullTimeline.Length - 1, "stock eras (incl. the placeholder 1945.09.03) plus Coldwar1/2/3 form a strictly rising timeline");
Check(packStarts[0] > gameStockStarts[^1], "Coldwar1 starts after the game's own Coldwar placeholder");
Check(ColdWarRules.RecognizeLastEraSentinel(true, true, fullTimeline.Length - 1, fullTimeline) && !ColdWarRules.RecognizeLastEraSentinel(true, true, fullTimeline.Length - 2, fullTimeline), "final-era repair targets Coldwar3 only");
Check(ColdWarRules.ConfirmLastEra(gameStockStarts.Concat(new[] { new DateTime(1945, 9, 3) }).ToArray()) == -1, "a second era on the same start 1945.09.03 would be rejected (why Coldwar1 starts one day later)");
Console.WriteLine($"PASS {checks} final-era boundary checks; no era name, modern floor, horizon or price policy.");
