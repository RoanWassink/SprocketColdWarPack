using System.IO.Compression;
using SprocketGunSights;

int checks = 0, failures = 0;
void Check(bool ok, string what) { checks++; if (!ok) { failures++; Console.WriteLine("FAIL: " + what); } }

// ---------------------------------------------------------------- catalogue
var ids = SightCatalogue.All.Select(v => v.Id).ToArray();
Check(ids.Distinct().Count() == ids.Length, "sight ids are unique");
foreach (var v in SightCatalogue.All)
{
    Check(SightCatalogue.Countries.Contains(v.Country), v.Id + " country is listed");
    Check(v.Magnifications.Length > 0 && v.Magnifications.All(m => m >= 1 && m <= 20), v.Id + " powers within 1x-20x");
    Check(v.Magnifications.Zip(v.Magnifications.Skip(1)).All(p => p.First < p.Second), v.Id + " powers ascend");
    Check(Reticles.Get(v.Reticle).Id == v.Reticle, v.Id + " reticle exists");
    Check(!string.IsNullOrWhiteSpace(v.Tanks) && !string.IsNullOrWhiteSpace(v.Source), v.Id + " has tanks and source");
}
foreach (var country in SightCatalogue.Countries)
    foreach (var era in new[] { SightEra.EarlyWar, SightEra.MidWar, SightEra.LateWar, SightEra.ColdWar })
        Check(SightCatalogue.ForCountry(country).Any(v => v.Era == era), $"{country} has a {era} sight");
Check(SightCatalogue.Find("uk_swingfire")!.Magnifications.Max() >= 10, "Swingfire sight reaches 10x");
Check(SightCatalogue.Find("de_tzf12a")!.Magnifications.SequenceEqual(new[] { 2.5f, 5f }), "TZF 12a dual 2.5x / 5x");
Check(SightCatalogue.Default(SightCatalogue.AtgmGuidanceSightGuid, new DateTime(1975, 1, 1)).Id == "uk_swingfire", "ATGM guidance sight defaults to Swingfire");
Check(SightCatalogue.Default(null, new DateTime(1917, 5, 1)).Era == SightEra.Ww1 && SightCatalogue.Default(null, new DateTime(1980, 1, 1)).Era == SightEra.ColdWar, "era defaults follow the design date");
Check(SightCatalogue.All.Length == 33, "all 33 sights carry a date");
foreach (var v in SightCatalogue.All)
{
    Check(v.AvailableFrom > new DateTime(1900, 1, 1), v.Id + " has a real start date");
    Check(!SightDatePolicy.Allows(v.AvailableFrom.AddDays(-1), v), v.Id + " is unavailable one day early");
    Check(SightDatePolicy.Allows(v.AvailableFrom, v), v.Id + " is available on its date");
    Check(SightDatePolicy.Allows(DateTime.MaxValue, v), v.Id + " is available at the final-era sentinel");
}
foreach (var date in new[] { new DateTime(1917, 5, 1), new DateTime(1937, 1, 1), new DateTime(1943, 7, 1),
             new DateTime(1945, 9, 2), new DateTime(1945, 9, 3), new DateTime(1948, 1, 1),
             new DateTime(1951, 6, 1), new DateTime(1975, 1, 1), new DateTime(9999, 12, 31) })
{
    var fallback = SightCatalogue.Default(null, date);
    Check(SightDatePolicy.Allows(date, fallback), "default is usable on " + date.ToString("yyyy-MM-dd"));
}
Check(SightCatalogue.EraForDate(new DateTime(1945, 9, 2)) == SightEra.LateWar, "1945.09.02 is still late war");
Check(SightCatalogue.EraForDate(new DateTime(1945, 9, 3)) == SightEra.ColdWar, "1945.09.03 is the Coldwar start");
Check(SightCatalogue.EraForYear(1945) == SightEra.LateWar && SightCatalogue.EraForYear(1946) == SightEra.ColdWar, "EraForYear no longer uses 1946 as the cut");
Check(SightCatalogue.Find("de_emes15")!.AvailableFrom == new DateTime(1979, 10, 25), "EMES 15 date from ERA-MAP");
Check(SightCatalogue.Find("us_gps")!.AvailableFrom == new DateTime(1980, 1, 1), "M1 GPS date from ERA-MAP");
Check(SightCatalogue.Find("su_1g46")!.AvailableFrom == new DateTime(1985, 1, 1), "1G46 date from ERA-MAP");
// A saved id stays resolvable even when the date refuses it; only the runtime use is gated.
Check(SightCatalogue.Find("de_emes15") != null && !SightDatePolicy.Allows(new DateTime(1970, 1, 1), SightCatalogue.Find("de_emes15")!), "too-early saved identity is retained, not deleted");
Check(Math.Abs(SightCatalogue.FieldOfView(60, 1) - 60) < .01f, "1x keeps the reference view");
Check(Math.Abs(SightCatalogue.FieldOfView(60, 4) - 16.43f) < .05f, "4x from 60 deg is ~16.4 deg (TSh-16 field 16 deg)");

// ------------------------------------------------------------------ reticles
var preview = Path.Combine(AppContext.BaseDirectory, "previews");
Directory.CreateDirectory(preview);
foreach (var id in new[] { "post", "winkel", "chevron", "rangefinder", "ladder", "cross", "nato", "atgm" })
{
    var design = Reticles.Get(id);
    foreach (var perMil in new[] { 2.5f, 12f })
    {
        var mask = ReticleRaster.Render(design, perMil, 1.6f, out var size);
        Check(mask.Length == size * size && mask.Any(a => a > 200), $"{id} at {perMil} px/mil renders");
        // Thin strokes still reach full opacity at low power.
        Check(mask.Count(a => a > 200) > size / 2, $"{id} at {perMil} px/mil keeps visible strokes");
        if (perMil == 12f) WritePng(Path.Combine(preview, id + ".png"), mask, size);
    }
    var big = ReticleRaster.Render(design, 12f, 1.6f, out var s);
    int centre = s / 2;
    bool nearAim = false;
    for (int dy = -24; dy <= 24 && !nearAim; dy++) for (int dx = -24; dx <= 24; dx++) if (big[(centre + dy) * s + centre + dx] > 128) { nearAim = true; break; }
    Check(nearAim, id + " marks the aim point within 2 mils (open centres allowed)");
}
Console.WriteLine($"Gun sights: {checks - failures}/{checks} checks passed. Previews: {preview}");
return failures == 0 ? 0 : 1;

static void WritePng(string path, byte[] alpha, int size)
{
    // Black ink on white, top row first.
    var raw = new byte[size * (size + 1)];
    for (int y = 0; y < size; y++)
    {
        raw[y * (size + 1)] = 0;
        for (int x = 0; x < size; x++) raw[y * (size + 1) + 1 + x] = (byte)(255 - alpha[(size - 1 - y) * size + x]);
    }
    using var data = new MemoryStream();
    using (var z = new ZLibStream(data, CompressionLevel.Optimal, true)) z.Write(raw);
    using var file = File.Create(path);
    file.Write(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 });
    Chunk(file, "IHDR", BE(size).Concat(BE(size)).Concat(new byte[] { 8, 0, 0, 0, 0 }).ToArray());
    Chunk(file, "IDAT", data.ToArray());
    Chunk(file, "IEND", Array.Empty<byte>());
}
static byte[] BE(int v) => new[] { (byte)(v >> 24), (byte)(v >> 16), (byte)(v >> 8), (byte)v };
static void Chunk(Stream s, string type, byte[] body)
{
    var t = System.Text.Encoding.ASCII.GetBytes(type);
    s.Write(BE(body.Length)); s.Write(t); s.Write(body);
    s.Write(BE((int)Crc(t.Concat(body).ToArray())));
}
static uint Crc(byte[] bytes)
{
    uint c = 0xffffffff;
    foreach (var b in bytes) { c ^= b; for (int k = 0; k < 8; k++) c = (c & 1) != 0 ? 0xedb88320 ^ (c >> 1) : c >> 1; }
    return c ^ 0xffffffff;
}
