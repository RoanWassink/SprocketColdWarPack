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
Console.WriteLine($"PASS {checks} final-era boundary checks; no era name, modern floor, horizon or price policy.");
