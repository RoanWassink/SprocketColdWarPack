using SprocketThermalSight;
using System.Text.Json;
using SprocketThermalSight.Tests;

int checks = 0;
void Check(bool value, string name) { checks++; if (!value) throw new Exception(name); }
void Reject(Action action, string name) { bool rejected = false; try { action(); } catch { rejected = true; } Check(rejected, name); }
var catalog = ThermalCatalog.Read(Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../package/BepInEx/plugins/SprocketThermalSight/thermal-models.json")));
Check(catalog.Models.Count == 9, "0.5.4: the nine standardised profiles, legacy parts removed");
Check(catalog.Aliases.Count == 5, "0.5.4: five legacy part aliases");
Check(catalog.Models[0].ComponentId != catalog.Models[1].ComponentId, "per-part identity");
var p = new ThermalProfile { PartGuid = Guid.NewGuid().ToString(), ComponentId = "test", DisplayName = "test", Width = 80, Height = 60, Noise = 0, Blur = 0, Contrast = 1, Gamma = 1, GrayLevels = 256 };
p.Validate();
int count = p.Width * p.Height;
var scene = new byte[count * 4]; var mask = new byte[count * 4];
for (int y = 0; y < p.Height; y++) for (int x = 0; x < p.Width; x++)
{ int k = (y * p.Width + x) * 4; scene[k + 3] = 255; if (x >= 40) mask[k] = mask[k + 1] = mask[k + 2] = 255; }
var white = SensorProcessor.Process(scene, mask, p, 1);
Check(white[4 * 50] > white[0], "vehicle brighter than background");
for (int i = 0; i < count; i++) Check(white[i * 4 + 3] == 255, "opaque image");
p.BlackHot = true; var black = SensorProcessor.Process(scene, mask, p, 1);
for (int i = 0; i < count; i++) Check(Math.Abs(white[i * 4] + black[i * 4] - 255) <= 1, "polarity inversion");
p.BlackHot = false; p.GrayLevels = 2; var binary = SensorProcessor.Process(scene, mask, p, 1);
Check(binary.Where((_, i) => i % 4 == 0).All(v => v == 0 || v == 255), "two gray levels");
p.GrayLevels = 256; p.Noise = .1f;
Check(SensorProcessor.Process(scene, mask, p, 7).SequenceEqual(SensorProcessor.Process(scene, mask, p, 7)), "repeatable noise seed");
Check(!SensorProcessor.Process(scene, mask, p, 7).SequenceEqual(SensorProcessor.Process(scene, mask, p, 8)), "changing sensor noise");
p.Noise = 0; p.Blur = 1; var blurred = SensorProcessor.Process(scene, mask, p, 1);
Check(blurred[39 * 4] > white[39 * 4] && blurred[40 * 4] < white[40 * 4], "edge softness");
p.Blur = 0; p.Brightness = 1; Check(SensorProcessor.Process(scene, mask, p, 1)[0] == 255, "brightness clamp");
p.Brightness = -1; Check(SensorProcessor.Process(scene, mask, p, 1)[50 * 4] == 0, "black clamp");
p.Brightness = 0; p.Gamma = float.NaN; Reject(p.Validate, "reject NaN");
p.Gamma = 1; p.Width = 2000; Reject(p.Validate, "reject huge allocation");
p.Width = 80; Reject(() => SensorProcessor.Process(new byte[4], mask, p, 1), "reject buffer mismatch");
var duplicate = new ThermalCatalog { Models = new() { p, p } }; Reject(duplicate.Validate, "reject duplicate ID");
p.Brightness = 1; p.Palette = "custom"; p.CustomColors = new[] { "#112233", "#AABBCC" };
var colored = SensorProcessor.Process(scene, mask, p, 1);
Check(colored[0] == 0xAA && colored[1] == 0xBB && colored[2] == 0xCC, "custom hot endpoint");
p.Brightness = -1; colored = SensorProcessor.Process(scene, mask, p, 1);
Check(colored[0] == 0x11 && colored[1] == 0x22 && colored[2] == 0x33, "custom cold endpoint");
p.Palette = "greenHot"; p.Brightness = 1; colored = SensorProcessor.Process(scene, mask, p, 1);
Check(colored[1] > colored[0] && colored[1] > colored[2], "green palette");
p.Palette = "blackHot"; Check(SensorProcessor.Process(scene, mask, p, 1)[0] == 0, "named blackhot inversion");
p.Palette = "invalid"; Reject(p.Validate, "unknown palette");
p.Palette = "custom"; p.CustomColors = new[] { "#XYZXYZ", "#FFFFFF" }; Reject(p.Validate, "invalid color");
p.Palette = "whiteHot"; p.CustomColors = new[] { "#000000", "#FFFFFF" }; p.Brightness = 0;
p.Contrast = 1.7f; p.BackgroundLevel = .3f; p.BackgroundDetail = .3f; p.BackgroundGamma = .6f;
var darkScene = new byte[count * 4]; var coldMask = new byte[count * 4];
for (int i = 0; i < count; i++) { int k = i * 4; byte value = (byte)(i % p.Width < 40 ? 5 : 35); darkScene[k] = darkScene[k + 1] = darkScene[k + 2] = value; }
var terrain = SensorProcessor.Process(darkScene, coldMask, p, 1);
Check(terrain[0] > 30, "dark terrain remains visible at Mk3 contrast");
Check(terrain[50 * 4] - terrain[0] > 15, "faint terrain differences survive contrast");
var terrainHot = SensorProcessor.Process(darkScene, mask, p, 1);
Check(terrainHot[50 * 4] > terrain[50 * 4] + 50, "vehicle remains distinct from lifted terrain");
Check(catalog.Resolve(null).ComponentId == catalog.DefaultProfileId, "JSON default");
Check(catalog.Map("thermalSightMk3BlackHot") == "tiGen2" && catalog.Resolve(catalog.Map("thermalSightMk3BlackHot")!).Palette == "whiteHot", "0.5.4: legacy black-hot part maps to Gen 2, polarity is a switch");
Check(catalog.Resolve("missing").ComponentId == catalog.DefaultProfileId, "missing profile fallback");
Console.WriteLine($"PASS: {checks} sensor/profile checks; native rendering, part loading and mass/cost require gameplay validation.");


// Before post processing, software DRS occupies only part of an oversized buffer.
var low = BufferLayout.Resolve(1920, 1080, 960, 540, true);
Check(low.ScaleX == .5f && low.ScaleY == .5f, "low resolution capture reads valid region");
// After upscaling, scope source is full resolution even while camera.actualWidth is 960.
var final = BufferLayout.Resolve(1920, 1080, 960, 540, false);
Check(final.ViewportWidth == 1920 && final.ViewportHeight == 1080, "fixed post-upscale scope fills entire source");
var upscaled = BufferLayout.Resolve(1920, 1080, 1920, 1080, true);
Check(upscaled.ScaleX == 1 && upscaled.ScaleY == 1, "custom upscaler handle uses its own full viewport");
var hardware = BufferLayout.Resolve(960, 540, 960, 540, true);
Check(hardware.ScaleX == 1 && hardware.ScaleY == 1, "hardware-scaled physical surface samples full UVs");
foreach (int vw in new[] { 1920, 1344, 960, 1152, 1920 })
{
    var frame = BufferLayout.Resolve(2560, 1440, vw, vw * 9 / 16, true);
    Check(frame.ViewportWidth == vw && Math.Abs(frame.ScaleX * 2560 - vw) < .001f, "viewport follows changing render scale without allocation resize");
}
var odd = BufferLayout.Resolve(1919, 1079, 959, 539, true);
Check(Math.Abs(odd.ScaleX * 1919 - 959) < .001f && Math.Abs(odd.ScaleY * 1079 - 539) < .001f, "odd/asymmetric dimensions preserve sample footprint");
Reject(() => BufferLayout.Resolve(0, 1080, 960, 540, true), "reject uninitialized render surface");
Console.WriteLine($"PASS: {checks} checks including DRS layouts; native DRS visuals remain unvalidated.");
var vanilla = new[] { new DateTime(1900,1,1), new DateTime(1918,1,1), new DateTime(1940,1,1), new DateTime(1945,9,3) };
Check(!ThermalEraPolicy.Allows(new DateTime(1945,9,2), vanilla, new DateTime(1945,9,3)), "WWII final day excluded");
Check(ThermalEraPolicy.Allows(new DateTime(1945,9,3), vanilla, new DateTime(1945,9,3)), "first postwar day inclusive");
Check(ThermalEraPolicy.Allows(new DateTime(1960,1,1), vanilla, new DateTime(1945,9,3)), "finite vanilla postwar");
Check(ThermalEraPolicy.Allows(DateTime.MaxValue, vanilla, new DateTime(1945,9,3)), "vanilla final-era sentinel");
var custom = new[] { new DateTime(1900,1,1), new DateTime(1945,9,3), new DateTime(1992,1,1), new DateTime(2050,1,1) };
foreach (var date in new[] { new DateTime(1945,9,3), new DateTime(1992,1,1), new DateTime(2050,1,1), new DateTime(2400,1,1), new DateTime(9999,12,30) })
    Check(ThermalEraPolicy.Allows(date, custom, new DateTime(1945,9,3)), "custom postwar/modern/future has no arbitrary upper horizon");
Check(ThermalEraPolicy.Allows(DateTime.MaxValue, custom, new DateTime(1945,9,3)), "custom future final-era sentinel, no name lookup");
var earlyOnly = new[] { new DateTime(1900,1,1), new DateTime(1939,9,1) };
Check(ThermalEraPolicy.Allows(DateTime.MaxValue, earlyOnly, new DateTime(1945,9,3)), "native final era is open-ended regardless its start");
Check(!ThermalEraPolicy.Allows(new DateTime(1944,6,1), custom, new DateTime(1945,9,3)), "date excludes earlier custom era regardless label");
Check(!ThermalEraPolicy.Allows(null, custom, new DateTime(1945,9,3)), "missing owner date fails closed");
Check(!ThermalEraPolicy.Allows(new DateTime(1950,1,1), Array.Empty<DateTime>(), new DateTime(1945,9,3)), "missing era metadata fails closed");
Check(!ThermalEraPolicy.Allows(new DateTime(1950,1,1), new[] {new DateTime(1945,9,3),new DateTime(1900,1,1)}, new DateTime(1945,9,3)), "unordered eras fail closed");
Check(!ThermalEraPolicy.Allows(DateTime.MaxValue, new[] {DateTime.MaxValue.Date}, new DateTime(1945,9,3)), "sentinel is not valid era start");
Check(!ThermalEraPolicy.Allows(new DateTime(1945,9,3), new[] {new DateTime(2000,1,1)}, new DateTime(1945,9,3)), "date before first registered era fails closed");
Console.WriteLine($"PASS: {checks} checks including postwar windows; native imports/save-load and custom-era visuals need live validation.");
Check(RangefinderPolicy.Nearest(new double[]{300,100,200},500)==100,"unsorted hits choose nearest surface");
Check(RangefinderPolicy.Nearest(new double[]{200,300,100},500)==100,"hit order does not alter distance");
Check(RangefinderPolicy.Nearest(new double[]{double.NaN,double.PositiveInfinity,-1,0,600},500)==null,"invalid and out-of-range returns rejected");
Check(RangefinderPolicy.Nearest(new double[]{500},500)==500,"maximum range inclusive");
Check(RangefinderPolicy.Nearest(Array.Empty<double>(),500)==null,"empty ray has no return");
Check(RangefinderPolicy.Nearest(new double[]{100},double.NaN)==null,"invalid configured limit rejected");
Check(RangefinderPolicy.CanMeasure(true,false,true,true,true,0),"active sight with fitted device can measure");
Check(RangefinderPolicy.CanMeasure(true,false,true,true,true,1),"one-frame callback ordering accepted");
Check(!RangefinderPolicy.CanMeasure(true,false,true,true,true,2),"stale controller rejected");
Check(!RangefinderPolicy.CanMeasure(true,false,true,true,true,-1),"future frame rejected");
Check(!RangefinderPolicy.CanMeasure(false,false,true,true,true,0),"unfocused cannot measure");
Check(!RangefinderPolicy.CanMeasure(true,true,true,true,true,0),"paused cannot measure");
Check(!RangefinderPolicy.CanMeasure(true,false,false,true,true,0),"disabled cannot measure");
Check(!RangefinderPolicy.CanMeasure(true,false,true,false,true,0),"unscoped cannot measure");
Check(!RangefinderPolicy.CanMeasure(true,false,true,true,false,0),"missing fitted device cannot measure");
Console.WriteLine($"PASS: {checks} checks including ranging policy; native ray/HUD/save-load require live testing.");

Check(RangefinderPolicy.Readout(RangefinderPolicy.Nearest(new double[]{20,200},4000),50)=="Too close","near obstruction cannot be filtered to range behind it");
Check(RangefinderPolicy.Readout(50,50)=="50 m","minimum inclusive");
Check(RangefinderPolicy.Readout(54,50)=="50 m","ten metre readout rounded down");
Check(RangefinderPolicy.Readout(55,50)=="60 m","ten metre midpoint rounded away from zero");
Check(RangefinderPolicy.Readout(null,50)=="No return","no synthetic maximum return");
Console.WriteLine($"PASS: {checks} checks including device readout and near obstruction.");
Check(RangefinderDevicePolicy.Kind("laserRangefinderSight")==RangefinderKind.ManualLaser,"legacy marker remains manual");
Check(RangefinderDevicePolicy.Kind("automaticLaserRangefinder")>RangefinderDevicePolicy.Kind("laserRangefinderSight"),"automatic priority");
Check(RangefinderDevicePolicy.Kind("sight")==RangefinderKind.None,"unrelated sight does not grant capability");
Check(RangefinderDevicePolicy.OpticalDelay(50)==5.05,"near target delay");
Check(RangefinderDevicePolicy.OpticalDelay(3000)==8,"far target delay limit");
Check(RangefinderDevicePolicy.Mass(2)==22 && RangefinderDevicePolicy.Cost(2)==200,"default optical resource calibration");
Check(RangefinderDevicePolicy.Cost(3)<400,"largest allowed baseline still cheaper than manual laser");
Reject(()=>RangefinderDevicePolicy.OpticalReading(1000,0),"zero baseline rejected");
Check(ThermalEraPolicy.Allows(new DateTime(1944,1,1), vanilla,new DateTime(1943,10,19)),"optical allowed in late WWII");
Check(!ThermalEraPolicy.Allows(new DateTime(1944,1,1),vanilla,new DateTime(1945,9,3)),"thermal postwar floor preserved");
Check(ThermalEraPolicy.Allows(DateTime.MaxValue,new[]{new DateTime(1900,1,1),new DateTime(1943,10,19)},new DateTime(1943,10,19)),"last-era sentinel also supports optical WWII floor");
Console.WriteLine($"PASS: {checks} including extended ranging math and independent optical era gate.");

Check(RangefinderDevicePolicy.OpticalReading(150,1)==200,"one metre user's midpoint example");
Check(RangefinderDevicePolicy.OpticalReading(149,1)==100,"one metre rounds down below midpoint");
Check(RangefinderDevicePolicy.OpticalReading(150,2)==150,"two metre resolves fifty metre steps");
Check(RangefinderDevicePolicy.OpticalReading(175,2)==200,"fifty metre midpoint rounds up");
Check(RangefinderDevicePolicy.OpticalReading(163,3)==175,"three metre resolves twenty five metre steps");
Check(RangefinderDevicePolicy.OpticalReading(50,1)==100,"near reading follows coarse step");
Check(RangefinderDevicePolicy.OpticalReading(3000,1)==3000,"upper limit maintained");
Check(RangefinderDevicePolicy.Mass(1)==15 && RangefinderDevicePolicy.Cost(1)==150,"one metre default mass and cost");
Reject(()=>RangefinderDevicePolicy.OpticalReading(double.NaN,1),"invalid distance rejected");
Reject(()=>RangefinderDevicePolicy.OpticalReading(40,1),"near obstruction never quantized as valid range");
Reject(()=>RangefinderDevicePolicy.OpticalReading(3001,1),"outside optical envelope rejected");
for(int range=50;range<=3000;range++) foreach(var baseline in new[]{1d,2d,3d}){
 var reading=RangefinderDevicePolicy.OpticalReading(range,baseline);
 var step=RangefinderDevicePolicy.OpticalStep(baseline);
 Check(Math.Abs(reading-range)<=step/2+.0001,"bounded quantization error");
 Check(reading%step==0,"readout is a real step multiple");
}
Console.WriteLine($"PASS: {checks} including optical quantization boundaries and user's 150-to-200 example.");

// Native Part dates are mutable data, not a permanent postwar gate.
foreach(var invention in new[]{new DateTime(1910,6,1),new DateTime(1943,10,19),new DateTime(1965,7,1),new DateTime(2100,1,1)})
{
 Check(!ThermalEraPolicy.Allows(invention.AddDays(-1),vanilla,invention),"before actual Part date");
 Check(ThermalEraPolicy.Allows(invention,vanilla,invention),"on actual Part date");
 Check(ThermalEraPolicy.Allows(invention.AddDays(1),vanilla,invention),"after actual Part date");
 Check(ThermalEraPolicy.Allows(DateTime.MaxValue,vanilla,invention),"final-era allows invention later than final start");
}
Console.WriteLine($"PASS: {checks} including native mutable Part dates and open final era.");

// Saved profile key: only the current key is read; profile ID strings are preserved.
Check(SavedProfileKeys.Resolve(Array.Empty<string>()) == null, "no profile key");
Check(SavedProfileKeys.Resolve(new[]{SavedProfileKeys.Canonical}) == SavedProfileKeys.Canonical, "profile key read");
Check(SavedProfileKeys.Resolve(new[]{"unrelatedKey"}) == null, "unrelated key ignored");
foreach(var profileId in new[]{"tiGen2","custom-user-profile"})
{
 var save = new Dictionary<string,string>{{SavedProfileKeys.Canonical,profileId}};
 Check(save[SavedProfileKeys.Resolve(save.Keys)!]==profileId, "profile ID survives save and reload");
}
Console.WriteLine($"PASS: {checks} including profile key read/write roundtrips.");
Check(RangefinderDevicePolicy.Kind("t72StyleSight")==RangefinderKind.ManualLaser,"TPD-K1 provides laser ranging");
Check(!RangefinderDevicePolicy.IsStandalone("t72StyleSight"),"Integrated sight retains its own native model and mass path");
Check(RangefinderDevicePolicy.IsStandalone("laserRangefinderSight"),"Standalone laser retains device renderer");
Check(!RangefinderDevicePolicy.IsStandalone(null),"Missing part cannot enable a device renderer");
Console.WriteLine($"PASS: {checks} including integrated TPD-K1 classification.");

// v2 schema and in-memory v1 migration use the actual file reader.
void ReadText(string json, Action<ThermalCatalog> check)
{
    var path = Path.Combine(Path.GetTempPath(), "thermal-catalog-" + Guid.NewGuid() + ".json");
    try { File.WriteAllText(path, json); check(ThermalCatalog.Read(path)); }
    finally { File.Delete(path); }
}
string one = "{\"componentId\":\"test\",\"displayName\":\"Test\",\"availableFrom\":\"1977.01.01\",\"sensorType\":\"thermal\"}";
string V2(string model) => "{\"version\":2,\"defaultProfileId\":\"test\",\"models\":[" + model + "]}";
ReadText(V2(one), c => Check(c.Version == 2, "read v2"));
foreach (var invalid in new[] {"1977.02.29", "1977.13.01", "1977.00.01", "1977.01.00", "1977.1.01", "0000.01.01"})
    Reject(() => ReadText(V2(one.Replace("1977.01.01", invalid)), _ => {}), "invalid calendar " + invalid);
ReadText(V2(one.Replace("1977.01.01", "2000.02.29")), c => Check(c.Models[0].AvailableDate.Day == 29, "leap day"));
Reject(() => ReadText(V2(one.Replace("\"availableFrom\":\"1977.01.01\",", "")), _ => {}), "v2 requires date");
Reject(() => ReadText(V2(one.Replace("\"sensorType\":\"thermal\"", "\"sensorType\":\"thermal\",\"blackHot\":false")), _ => {}), "v2 rejects blackHot");
Reject(() => ReadText(V2(one.Replace("thermal", "other")), _ => {}), "invalid sensor type");
Reject(() => ReadText("{\"version\":2,\"models\":null}", _ => {}), "null models");
Reject(() => ReadText(V2("null"), _ => {}), "null model");
Reject(() => new ThermalCatalog { Models = null! }.Validate(), "null models validation");
Reject(() => ReadText(V2(one.Replace("thermal", "activeIR")), _ => {}), "active IR beam fields required");
foreach (var field in new[] { "beamRangeM", "beamAngleDeg" })
{
    var model = one.Replace("thermal", "activeIR").Replace("}", ",\"beamRangeM\":800,\"beamAngleDeg\":6}");
    ReadText(V2(model), c => Check(c.Models[0].SensorType == "activeIR", "IR schema"));
    foreach (var invalid in field == "beamRangeM" ? new[] {99,2001} : new[] {0,31})
        Reject(() => ReadText(V2(model.Replace(field + "\":" + (field == "beamRangeM" ? "800" : "6"), field + "\":" + invalid)), _ => {}), "beam bounds");
}
ReadText("{\"version\":1,\"defaultProfileId\":\"test\",\"models\":[{\"componentId\":\"test\",\"displayName\":\"Test\",\"blackHot\":true}]}", c => {
    Check(c.Version == 2 && c.Models[0].AvailableFrom == "1977.01.01" && c.Models[0].SensorType == "thermal", "v1 defaults migrated");
    Check(c.Models[0].Palette == "blackHot" && !c.Models[0].BlackHot, "v1 polarity migrated");
});
var introduction = new DateTime(1977,1,1);
Check(!ThermalEraPolicy.Allows(introduction.AddDays(-1), vanilla, new DateTime(1950,1,1), introduction), "profile date delays part");
Check(ThermalEraPolicy.Allows(introduction, vanilla, new DateTime(1950,1,1), introduction), "profile boundary inclusive");
Check(!ThermalEraPolicy.Allows(introduction, vanilla, new DateTime(1980,1,1), introduction), "part delays profile");
Check(ThermalEraPolicy.Allows(DateTime.MaxValue, vanilla, new DateTime(1980,1,1), new DateTime(9999,12,30)), "MaxValue opens all finite dates");
ThermalProfile? Pick(int year) => ProfileSelectionPolicy.Default(catalog.Models, catalog.DefaultProfileId, m => m.AvailableDate.Year <= year);
Check(Pick(1949) == null, "no usable default");
Check(Pick(1955)?.ComponentId == "irActiveL1", "early default IR");
Check(Pick(1965)?.ComponentId == "iiGen1", "default newest II");
Check(Pick(1974)?.ComponentId == "iiGen2", "newest available fallback");
Check(Pick(1999)?.ComponentId == "tiGen1", "configured default takes priority");
// The final era stays open; its sentinel must not become a finite technology horizon.
var eraStarts = new[] { new DateTime(1900,1,1), new DateTime(1940,1,1), new DateTime(1960,1,1), new DateTime(1975,1,1) };
var sentinel = new DateTime(9999,12,31);
var tiGen2 = catalog.Resolve("tiGen2"); var tiGen1 = catalog.Resolve("tiGen1");
Check(ThermalEraPolicy.Allows(sentinel, eraStarts, new DateTime(1950,1,1), tiGen2.AvailableDate), "final-era sentinel uses 1992 profile");
Check(ProfileSelectionPolicy.Default(catalog.Models, "tiGen2", m => ThermalEraPolicy.Allows(sentinel, eraStarts, new DateTime(1950,1,1), m.AvailableDate))?.ComponentId == "tiGen2", "final-era sentinel selects 1992 profile");
Check(!ThermalEraPolicy.Allows(new DateTime(1976,12,31), eraStarts, new DateTime(1950,1,1), tiGen1.AvailableDate), "1977 profile excluded on 1976.12.31");
Check(ThermalEraPolicy.Allows(new DateTime(1977,1,1), eraStarts, new DateTime(1950,1,1), tiGen1.AvailableDate), "1977 profile included on 1977.01.01");
Check(ProfileSelectionPolicy.Default(catalog.Models, "tiGen1", m => ThermalEraPolicy.Allows(new DateTime(1976,12,31), eraStarts, new DateTime(1950,1,1), m.AvailableDate))?.ComponentId != "tiGen1", "1977 default not offered on 1976.12.31");
Console.WriteLine($"PASS: {checks} checks including v1/v2 validation, profile dates, final-era sentinel and default selection.");

var ii = new ThermalProfile { ComponentId = "ii", DisplayName = "II", SensorType = "imageIntensifier", Palette = "greenHot", Width = 80, Height = 60, Contrast = 1, Noise = 0, Blur = 0, GrayLevels = 256 };
var dim = new byte[count * 4]; var emptyMask = new byte[count * 4]; var fullMask = new byte[count * 4];
for (int i = 0; i < count; i++)
{
    dim[i * 4] = dim[i * 4 + 1] = dim[i * 4 + 2] = 25;
    fullMask[i * 4] = fullMask[i * 4 + 1] = fullMask[i * 4 + 2] = 255;
}
var amplified = SensorProcessor.Process(dim, emptyMask, ii, 42);
Check(amplified[1] > 100 && amplified[1] > amplified[0], "II automatic gain and green palette");
Check(amplified.SequenceEqual(SensorProcessor.Process(dim, fullMask, ii, 42)), "II ignores vehicle mask");
int spot = (20 * 80 + 20) * 4;
dim[spot] = dim[spot + 1] = dim[spot + 2] = 255;
var bloom = SensorProcessor.Process(dim, emptyMask, ii, 42);
Check(bloom[spot + 5] > bloom[1], "II highlight bloom spreads beyond bright pixel with blur disabled");
var workspace = new SensorWorkspace(ii);
workspace.Process(dim, emptyMask, 1);
long before = GC.GetAllocatedBytesForCurrentThread();
for (uint i = 0; i < 8; i++) workspace.Process(dim, emptyMask, i);
Check(GC.GetAllocatedBytesForCurrentThread() == before, "II reuses all processing buffers");
ii.Noise = .02f;
for (int i = 0; i < count * 4; i++) dim[i] = 2;
var darkNoise = SensorProcessor.Process(dim, emptyMask, ii, 42);
for (int i = 0; i < count * 4; i++) dim[i] = 128;
var lightNoise = SensorProcessor.Process(dim, emptyMask, ii, 42);
int Spread(byte[] bytes) { var green = bytes.Where((_, i) => i % 4 == 1); return green.Max() - green.Min(); }
Check(Spread(darkNoise) > Spread(lightNoise) * 2, "II noise increases with gain");
ii.Noise = 0; ii.Palette = "whiteHot";
for (int i = 0; i < count * 4; i++) dim[i] = 200;
var daylight = SensorProcessor.Process(dim, emptyMask, ii, 42);
// 0.5.4: the tube saturates softly, so a bright scene lands just under the 0.45 target instead of exactly on it.
var probe = new SensorWorkspace(ii); probe.Process(dim, emptyMask, 42);
Console.WriteLine($"II daylight: {daylight[0]} (target 0.45*255 = 115); gain {probe.LastGain:F2}");
Check(daylight[0] > .35 * 255 && daylight[0] < .46 * 255, "II gain also reduces bright scenes toward target mean");

// 0.5.4: the catalogue object is shared with the later profile checks, so this one works on a copy.
var ir = new ThermalProfile { ComponentId = "ir", DisplayName = "IR", SensorType = "activeIR", Palette = "greenHot", Width = 80, Height = 60, Noise = .04f, Blur = 0, Contrast = 1, GrayLevels = 256, ExposureK = 3 };
var missingLamp = SensorProcessor.Process(dim, fullMask, ir, 42);
double greenMean = missingLamp.Where((_, i) => i % 4 == 1).Average(v => (double)v);
Check(greenMean > 0 && greenMean < 30, "missing IR lamp produces dark visible noise");
ir.Noise = 0; ir.Blur = 0;
var irWork = new SensorWorkspace(ir); var beam = Enumerable.Repeat(1f, count).ToArray();
var lit = irWork.Process(dim, emptyMask, 42, beam).ToArray();
Array.Clear(beam, 0, beam.Length);
var unlit = irWork.Process(dim, fullMask, 42, beam).ToArray();
Check(lit[1] > unlit[1] + 60, "active IR only amplifies illuminated scene");
Check(unlit.SequenceEqual(irWork.Process(dim, emptyMask, 42, beam)), "active IR ignores thermal vehicle mask");
before = GC.GetAllocatedBytesForCurrentThread();
for (uint i = 0; i < 8; i++) irWork.Process(dim, emptyMask, i, beam);
Check(GC.GetAllocatedBytesForCurrentThread() == before, "IR reuses processing buffers");
Check(IrBeamPolicy.Illumination(0, 0, 100, 0, 0, 1, 800, 6) > IrBeamPolicy.Illumination(0, 0, 600, 0, 0, 1, 800, 6), "IR distance attenuation");
Check(IrBeamPolicy.Illumination(0, 0, 801, 0, 0, 1, 800, 6) == 0, "IR range limit");
Check(IrBeamPolicy.Illumination(0, 0, -100, 0, 0, 1, 800, 6) == 0, "IR rear hemisphere dark");
Check(IrBeamPolicy.Illumination(10, 0, 100, 0, 0, 1, 800, 6) == 0, "IR cone excludes off-axis surface");
Check(IrBeamPolicy.Illumination(100, 0, 0, 1, 0, 0, 800, 6) > 0, "IR follows rotated searchlight");
Check(IrBeamPolicy.Illumination(float.NaN, 0, 0, 1, 0, 0, 800, 6) == 0, "IR rejects invalid geometry");
Check(IrBeamPolicy.PixelWeight(0, 0, 1, 0, 0, 1, 6) == 1 && IrBeamPolicy.PixelWeight(1, 0, 0, 0, 0, 1, 6) == 0, "0.5.4 per-pixel cone");

var adapter = new LoadedShellAdapter(typeof(FakeSelection), typeof(FakeAmmoApi));
var cannon = new FakeCannon { Mix = true, Initial = new FakeProfile("missile", "atgm_gun"), State = new("ap", true, false, "missile") };
Check(!adapter.MustSuspend(cannon), "loaded AP wins over initial ATGM and requested ATGM");
cannon.Initial = new("ap", "ap"); cannon.State = new("missile", true, false, "ap");
Check(adapter.MustSuspend(cannon), "loaded ATGM wins over initial AP and requested AP");
cannon.State = new("ap", false, false, "ap"); Check(adapter.MustSuspend(cannon), "unconfirmed chamber suspends automatic correction");
cannon.State = new("ap", true, true, "ap"); Check(adapter.MustSuspend(cannon), "frozen mix suspends correction");
cannon.State = null; Check(adapter.MustSuspend(cannon), "missing mix snapshot never falls back to InitialProfile");
cannon.State = new("vanilla", true, false, "missile"); Check(!adapter.MustSuspend(cannon), "confirmed stock round is unguided");
cannon.State = new("removed", true, false, "ap"); Check(adapter.MustSuspend(cannon), "unknown chamber identity suspends correction");
cannon.State = null; cannon.Mix = false; Check(!adapter.MustSuspend(cannon), "single unguided profile fallback");
cannon.Initial = new("missile", "atgm_gun"); Check(adapter.MustSuspend(cannon), "single guided profile remains excluded");
Check(new LoadedShellAdapter(typeof(FakeSelection), null).MustSuspend(cannon), "missing API fails closed");
Console.WriteLine($"PASS: {checks} checks including II gain/bloom/noise, IR cone/no-lamp behavior, allocation reuse and loaded-shell reflection.");

// 0.3.3: an empty, reloading or just-switched gun must not drop the gravity
// correction when every carried type is unguided (apds_late + heat case).
cannon.Mix = true;
FakeRound[] apHeat = { new("ap"), new("heat") }, apMissile = { new("ap"), new("missile") };
cannon.State = new(null, false, false, "heat", apHeat);
Check(adapter.SuspendReason(cannon) == null, "unconfirmed chamber with all-unguided mix keeps correction");
cannon.State = new(null, false, false, null, apHeat);
Check(!adapter.MustSuspend(cannon), "unguided mix without next-load preference keeps correction");
cannon.State = new(null, false, false, "ap", apMissile);
Check(adapter.SuspendReason(cannon) == "chamber-unconfirmed", "unconfirmed chamber with a guided type in the mix suspends");
cannon.State = new(null, false, false, "missile", new FakeRound[] { new("ap") });
Check(adapter.MustSuspend(cannon), "guided next-load request suspends an unconfirmed chamber");
cannon.State = new(null, false, false, "ap", new FakeRound[] { new("ap"), new("removed") });
Check(adapter.MustSuspend(cannon), "unknown carried type fails closed");
cannon.State = new(null, false, false, "ap", Array.Empty<FakeRound>());
Check(adapter.MustSuspend(cannon), "empty carried list fails closed");
cannon.State = new(null, false, false, "ap", new FakeRound[] { new("vanilla"), new("ap") });
Check(!adapter.MustSuspend(cannon), "stock rounds count as unguided");
cannon.State = new(null, false, true, "ap", apHeat);
Check(adapter.SuspendReason(cannon) == "mix-frozen", "frozen mix still suspends before the carried-list check");
cannon.State = new("missile", true, false, "ap", apMissile);
Check(adapter.SuspendReason(cannon) == "guided:missile", "guided reason names the chambered profile");
cannon.State = new("removed", true, false, "ap", apHeat);
Check(adapter.SuspendReason(cannon) == "chamber-unknown:removed", "unknown chambered reason names the id");
cannon.State = null;
Check(adapter.SuspendReason(cannon) == "mix-without-snapshot", "mix without snapshot reason");
Check(new LoadedShellAdapter(typeof(FakeSelection), null).SuspendReason(cannon) == "ammo-api-missing", "missing API reason");

// 0.3.4: the drop correction uses the chambered round's muzzle speed (EB 0.22.17+).
cannon.State = new FakeSpeedState("heat", true, false, "ap", apHeat, 1150);
Check(adapter.SuspendReason(cannon) == null && adapter.ChamberedSpeed == 1150, "chambered HEAT speed is used");
cannon.State = new FakeSpeedState("vanilla", true, false, "ap", apHeat, 780);
Check(adapter.SuspendReason(cannon) == null && adapter.ChamberedSpeed == 780, "chambered stock round speed is used");
cannon.State = new FakeAmmoState("heat", true, false, "ap", apHeat);
Check(adapter.SuspendReason(cannon) == null && adapter.ChamberedSpeed == null, "older EB without the field falls back to the cannon");
cannon.State = new FakeSpeedState("heat", true, false, "ap", apHeat, 1150);
Check(adapter.SuspendReason(cannon) == null && adapter.ChamberedSpeed == 1150, "field lookup follows a changed state type");
foreach (var bad in new double?[] { null, 0, -900, 49.9, double.NaN, double.PositiveInfinity })
{
    cannon.State = new FakeSpeedState("heat", true, false, "ap", apHeat, bad);
    Check(adapter.SuspendReason(cannon) == null && adapter.ChamberedSpeed == null, $"invalid chambered speed {bad} falls back to the cannon");
}
cannon.State = new FakeSpeedState("heat", true, false, "ap", apHeat, 50);
Check(adapter.SuspendReason(cannon) == null && adapter.ChamberedSpeed == 50, "50 m/s is the inclusive lower bound");
cannon.State = new FakeSpeedState(null, false, false, "heat", apHeat, 1150);
Check(adapter.SuspendReason(cannon) == null && adapter.ChamberedSpeed == null, "unconfirmed chamber keeps correction but falls back to the cannon");
cannon.State = new FakeSpeedState("heat", true, false, "ap", apHeat, 1150);
adapter.SuspendReason(cannon);
cannon.State = new FakeSpeedState("heat", true, true, "ap", apHeat, 1150);
Check(adapter.SuspendReason(cannon) == "mix-frozen" && adapter.ChamberedSpeed == null, "frozen mix clears a previous chambered speed");
cannon.State = null;
Check(adapter.SuspendReason(cannon) == "mix-without-snapshot" && adapter.ChamberedSpeed == null, "missing snapshot clears the chambered speed");
cannon.Mix = false; cannon.Initial = new("ap", "ap");
Check(adapter.SuspendReason(cannon) == null && adapter.ChamberedSpeed == null, "no EB mix: cannon speed");
cannon.Mix = true;
cannon.State = new FakeSpeedState("missile", true, false, "ap", apMissile, 300);
Check(adapter.SuspendReason(cannon) == "guided:missile", "guided chambered round still suspends with a speed present");
Console.WriteLine($"PASS: {checks} checks including 0.3.4 chambered muzzle speed.");

// Diagnostic refusal names the first failed condition and agrees with CanUse.
var refusalNames = new[] { "not-ready", "disabled", "measurement-failed", "device-not-automatic", "not-owned",
    "no-linked-cannon", "cannon-unhealthy", "guided", "native-distance-changed" };
for (int i = 0; i < refusalNames.Length; i++)
{
    bool On(int k) => k != i;
    var refusal = RangefinderZeroingPolicy.Refusal(On(0), On(1), !On(2), On(3), On(4), On(5), On(6), !On(7), On(8));
    Check(refusal == refusalNames[i], "refusal names " + refusalNames[i]);
    Check(!RangefinderZeroingPolicy.CanUse(On(0), On(1), !On(2), On(3), On(4), On(5), On(6), !On(7), On(8)), "CanUse rejects " + refusalNames[i]);
}
Check(RangefinderZeroingPolicy.Refusal(true, true, false, true, true, true, true, false, true) == null
    && RangefinderZeroingPolicy.CanUse(true, true, false, true, true, true, true, false, true), "usable zero has no refusal");
Check(RangefinderZeroingPolicy.Refusal(false, false, true, false, false, false, false, true, false) == "not-ready", "first failure wins");
Check(RangefinderZeroingPolicy.IsCurrent(new(1, 2, 3, 500, 1), 1, 2, 3, 500f)
    && !RangefinderZeroingPolicy.IsCurrent(new(1, 2, 3, 500, 1), 1, 2, 3, 250f), "native read-back must match the retained zero");
Console.WriteLine($"PASS: {checks} checks including all-unguided mix reload/switch and FCS refusal reasons.");

void RejectData(string json, string fragment)
{
    bool correct = false;
    try { ReadText(json, _ => {}); }
    catch (InvalidDataException ex) { correct = ex.Message.Contains(fragment); }
    Check(correct, "targeted validation: " + fragment);
}
RejectData("{\"version\":2,\"models\":null}", "models must be a non-null array");
RejectData(V2("null"), "null/non-object model");
RejectData(V2(one.Replace("1977.01.01", "1977.02.30")), "availableFrom");
RejectData(V2(one.Replace("\"displayName\":", "\"componentId\":\"duplicate\",\"displayName\":")), "Duplicate setting");
foreach (var bounds in new[] { (100, 1), (2000, 30) })
    ReadText(V2(one.Replace("thermal", "activeIR").Replace("}", $",\"beamRangeM\":{bounds.Item1},\"beamAngleDeg\":{bounds.Item2}}}")), c => Check(c.Models[0].BeamRangeM == bounds.Item1, "inclusive IR beam bounds"));
var savedLate = catalog.Resolve("tiGen2");
Check(!ThermalEraPolicy.Allows(new DateTime(1955, 1, 1), vanilla, new DateTime(1950, 1, 1), savedLate.AvailableDate)
    && catalog.Resolve("tiGen2").ComponentId == "tiGen2", "early saved choice keeps identity");
var thermalWork = new SensorWorkspace(p);
thermalWork.Process(scene, mask, 1);
before = GC.GetAllocatedBytesForCurrentThread();
for (uint i = 0; i < 8; i++) thermalWork.Process(scene, mask, i);
Check(GC.GetAllocatedBytesForCurrentThread() == before, "thermal reuses processing buffers with blur disabled");
string migrationPath = Path.Combine(Path.GetTempPath(), "thermal-migration-" + Guid.NewGuid() + ".json");
try
{
    const string original = "{\"version\":1,\"defaultProfileId\":\"old\",\"models\":[{\"componentId\":\"old\",\"displayName\":\"Old\",\"blackHot\":true}]}";
    File.WriteAllText(migrationPath, original); var logs = new List<string>();
    var migrated = ThermalCatalog.Read(migrationPath, logs.Add);
    Check(logs.Count == 1 && File.ReadAllText(migrationPath) == original, "v1 migration logs once and leaves file unchanged");
    string serialized = JsonSerializer.Serialize(migrated, ThermalCatalog.JsonOptions);
    Check(!serialized.Contains("blackHot\":"), "v2 serialization omits legacy blackHot property");
    File.WriteAllText(migrationPath, serialized);
    Check(ThermalCatalog.Read(migrationPath).Models[0].Palette == "blackHot", "migrated v2 roundtrip");
}
finally { File.Delete(migrationPath); }

// 0.3.5 mount-chain matching: same mechanism, ancestor, descendant, foreign
// cannon/vehicle and two layers on one cannon, all as pure pointer tables.
var mount = new Dictionary<long, long>
{
    [2] = 1, [10] = 2, [11] = 10, [12] = 11, [13] = 12, [14] = 13, [20] = 14,
    [21] = 20, [30] = 14, [31] = 30, [40] = 2, [101] = 100
};
IntPtr P(long value) => new(value);
IntPtr MountParent(IntPtr node) => mount.TryGetValue(node.ToInt64(), out var parent) ? P(parent) : IntPtr.Zero;
IntPtr yaw = P(10), pitch = P(12), gun = P(20), barrel = P(21), noCannon = IntPtr.Zero;
Check(AimChain.Controls(P(500), IntPtr.Zero, gun, P(500), yaw, P(501), pitch, gun, gun, MountParent), "same horizontal mechanism");
Check(AimChain.Controls(P(501), IntPtr.Zero, gun, P(500), yaw, P(501), pitch, gun, gun, MountParent), "same vertical mechanism");
Check(AimChain.Controls(P(600), yaw, gun, P(500), yaw, P(501), pitch, noCannon, gun, MountParent), "same mechanism transform");
Check(AimChain.Controls(P(600), pitch, gun, P(500), yaw, P(501), pitch, noCannon, gun, MountParent), "pitch cradle is an ancestor");
Check(AimChain.Controls(P(600), barrel, gun, P(500), yaw, P(501), pitch, gun, gun, MountParent), "barrel is a descendant");
Check(!AimChain.Controls(P(600), P(30), gun, P(500), yaw, P(501), pitch, P(30), gun, MountParent), "foreign cannon under the same mantlet");
Check(!AimChain.Controls(P(600), P(31), gun, P(500), yaw, P(501), pitch, P(30), gun, MountParent), "foreign cannon barrel under the same mantlet");
Check(!AimChain.Controls(P(600), P(40), gun, P(500), yaw, P(501), pitch, noCannon, gun, MountParent), "unrelated branch on the same vehicle");
Check(!AimChain.Controls(P(600), P(101), gun, P(500), yaw, P(501), pitch, P(102), gun, MountParent), "foreign vehicle");
Check(AimChain.Controls(P(700), yaw, gun, P(500), yaw, P(501), pitch, noCannon, gun, MountParent)
    && AimChain.Controls(P(701), pitch, gun, P(500), yaw, P(501), pitch, noCannon, gun, MountParent), "two layers on one cannon");
Check(AimChain.Controls(P(700), yaw, gun, P(500), yaw, P(501), pitch, noCannon, gun, MountParent)
    && AimChain.Controls(P(700), yaw, gun, P(500), yaw, P(501), pitch, noCannon, gun, MountParent), "layer match is stateless");
Check(!AimChain.Controls(IntPtr.Zero, yaw, gun, P(500), yaw, P(501), pitch, noCannon, gun, MountParent), "missing layer target");
var cyclic = new Dictionary<long, long> { [50] = 51, [51] = 50 };
IntPtr CycleParent(IntPtr node) => cyclic.TryGetValue(node.ToInt64(), out var parent) ? P(parent) : IntPtr.Zero;
Check(!AimChain.Controls(P(900), P(50), gun, P(500), yaw, P(501), pitch, noCannon, gun, CycleParent), "parent cycle terminates");
var deep = new Dictionary<long, long>();
for (long i = 1001; i <= 1200; i++) deep[i] = i - 1;
IntPtr DeepParent(IntPtr node) => deep.TryGetValue(node.ToInt64(), out var parent) ? P(parent) : IntPtr.Zero;
Check(!AimChain.Controls(P(900), P(1000), P(1200), P(500), yaw, P(501), pitch, noCannon, gun, DeepParent), "depth cap rejects an overlong chain");
Console.WriteLine($"PASS: {checks} checks including 0.3.5 cannon mount-chain matching.");
Console.WriteLine($"PASS: {checks} total assertions; pure managed checks only, native gameplay acceptance pending.");

BoxSightChecks.Run(Check);
Console.WriteLine($"PASS: {checks} total assertions including 0.3.6 box sight geometry, part, policy and converter checks; native gameplay pending.");
CamoChecks.Run(Check);
Console.WriteLine($"PASS: {checks} total assertions including 0.3.7 UV readers, surface contracts and package hashes; native camo pending.");
HeatChecks.Run(Check, Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../PERF-0.4.0.txt")));
Console.WriteLine($"PASS: {checks} total assertions including 0.4.0 heat model, projection, mask sum and CPU measurement; native heat sources pending.");
HeatFixChecks.Run(Check);
Console.WriteLine($"PASS: {checks} total assertions including 0.4.3 camera-matrix projection, far-side weight, mask diagnostics and per-frame shot counter; native confirmation pending.");
HudChecks.Run(Check, Reject, catalog);
Console.WriteLine($"PASS: {checks} total assertions including 0.5.0 HUD helpers, cooldown, Gen 3 profiles, sharpen and parallel HD processing; in-game HUD pending.");
HudCanvasChecks.Run(Check, catalog);
Console.WriteLine($"PASS: {checks} total assertions including 0.5.1 once-per-session cooling, canvas HUD raster, HD image after cooling and Gen 3 contrast; in-game canvas pending.");
ProfileChecks.Run(Check, Reject, catalog, Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../")));
Console.WriteLine($"PASS: {checks} total assertions including 0.5.4 explicit catalogue fields, legacy aliases, IR exposure and beam, image targets and the polarity switch; native gameplay pending.");
