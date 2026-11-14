using System.Text.Json;
using SprocketExpandedBallistics.Materials;

namespace SprocketExpandedBallistics;

internal static class Fix0226Tests
{
    internal static void Run(Action<bool, string> check)
    {
        var root = AppContext.BaseDirectory;

        // --- composite identity: prefab name route and icon GUID mapping ---
        var partPath = Path.Combine(root, "CompositePart/compositeArmourPlatePart.json");
        using var part = JsonDocument.Parse(File.ReadAllText(partPath));
        var prefab = part.RootElement;
        var mesh = prefab.GetProperty("components")[0].GetProperty("info").GetProperty("mesh");
        check(mesh.GetProperty("name").GetString() == CompositePlate.Name,
            "fix0226: composite prefab carries the native identity name");
        check(Guid.Parse(prefab.GetProperty("guid").GetString()!) == Guid.Parse(CompositePlate.PrefabGuid) &&
              CompositePlate.IconMap.Count == 1 &&
              CompositePlate.IconMap[CompositePlate.PrefabGuid] == CompositePlate.IconFile,
            "fix0226: composite prefab GUID maps to its icon file");

        var iconPath = Path.Combine(root, "CompositePart", CompositePlate.IconFile);
        check(File.Exists(iconPath), "fix0226: composite icon file exists");
        var png = File.ReadAllBytes(iconPath);
        check(png.Length > 33 && png[0] == 0x89 && png[1] == 0x50 && png[2] == 0x4E && png[3] == 0x47 &&
              png[12] == 0x49 && png[13] == 0x48 && png[14] == 0x44 && png[15] == 0x52,
            "fix0226: composite icon is a PNG");
        int Be32(int index) => (png[index] << 24) | (png[index + 1] << 16) | (png[index + 2] << 8) | png[index + 3];
        check(Be32(16) == 256 && Be32(20) == 256, "fix0226: composite icon is 256x256");
        check(png[25] == 6, "fix0226: composite icon has an alpha channel (transparent background)");
        check(JsonNodeString(mesh, "name") == CompositePlate.Name,
            "fix0226: the prefab name is the only identity source; FromJson stays name-free");

        // --- material editor view model: grouping, field validation and the balance floor ---
        check(MaterialEditorView.GroupOf("rha", "Rolled Homogeneous Armour", "passive") == "Steel" &&
              MaterialEditorView.GroupOf("ceramicArray", "Ceramic Array", "passive") == "Ceramic" &&
              MaterialEditorView.GroupOf("passiveComposite", "Passive composite", "passiveComposite") == "Composite" &&
              MaterialEditorView.GroupOf("cwepGlassTextolite", "glass Textolite", "glassTextolite") == "Composite" &&
              MaterialEditorView.GroupOf("kontakt1Active", "Kontakt-1 active", "lightEra") == "ERA" &&
              MaterialEditorView.GroupOf("ebCassette_t64k", "T-64 combination K", "passive") == "Cassettes",
            "fix0226: material editor groups steel, ceramic, composite, ERA and cassettes");
        var rows = new[]
        {
            new MaterialEditorRow("kontakt1Active", "Kontakt-1 active", "ERA", "lightEra", 0),
            new MaterialEditorRow("rha", "Rolled Homogeneous Armour", "Steel", "passive", 1),
            new MaterialEditorRow("ceramicArray", "Ceramic Array", "Ceramic", "passive", 2),
            new MaterialEditorRow("passiveComposite", "Passive composite", "Composite", "passiveComposite", 3)
        };
        var sorted = MaterialEditorView.Sorted(rows);
        check(sorted.Select(r => r.Group).SequenceEqual(new[] { "Steel", "Ceramic", "Composite", "ERA" }),
            "fix0226: material editor list follows the group order");

        check(!MaterialEditorView.CheckField("rhaFactor", "0").Ok &&
              !MaterialEditorView.CheckField("rhaFactor", "-1").Ok &&
              !MaterialEditorView.CheckField("rhaFactor", "abc").Ok &&
              MaterialEditorView.CheckField("rhaFactor", "2.5").Ok &&
              !MaterialEditorView.CheckField("density", "0").Ok &&
              !MaterialEditorView.CheckField("spallFactor", "-0.1").Ok &&
              !MaterialEditorView.CheckField("costMultiplier", "-1").Ok &&
              MaterialEditorView.CheckField("costMultiplier", "0").Ok,
            "fix0226: material editor rejects non-finite, zero/negative and negative spall/cost values");
        check(!MaterialEditorView.CheckField("label", "").Ok &&
              !MaterialEditorView.CheckField("label", new string('x', 81)).Ok &&
              MaterialEditorView.CheckField("label", new string('x', 80)).Ok,
            "fix0226: material editor name length is checked");
        check(!MaterialEditorView.CheckField("date", "2021.02.29").Ok &&
              !MaterialEditorView.CheckField("date", "2021-02-01").Ok &&
              MaterialEditorView.CheckField("date", "2020.02.29").Ok,
            "fix0226: material editor date is a real yyyy.MM.dd date");
        check(MaterialEditorView.CheckMaterial("Steel", "2020.01.01", "1", "7850", "0.0005", "2") == null,
            "fix0226: complete material values pass the shared checks");
        check(MaterialEditorView.CheckMaterial("Steel", "2020.01.01", "0", "7850", "0.0005", "2") != null,
            "fix0226: cross-field check catches an invalid RHA factor");
        var floor = MaterialBalance.Calculate(1, 7850, 0.5f);
        check(floor.IsValid && floor.WasAdjusted && floor.EffectiveCostMultiplier > 0.5f,
            "fix0226: live values keep the existing MaterialBalance cost floor");
        check(Math.Abs(MaterialEditorView.CostPerSquareMetre(7850, 2) - 1570) < 1e-9,
            "fix0226: cost per square metre uses density and the effective multiplier");

        // --- "Edit materials" routing ---
        check(MaterialEditorRoute.Choose(true, true) == MaterialEditorTarget.UiKit,
            "fix0226: Edit materials routes to the UI Kit panel");
        check(MaterialEditorRoute.Choose(true, false) == MaterialEditorTarget.JsonEditor &&
              MaterialEditorRoute.Choose(false, true) == MaterialEditorTarget.JsonEditor,
            "fix0226: the JSON Editor is only the fallback");
        Console.WriteLine("FIX 0.22.6 composite identity/name route, icon GUID mapping, material editor view model and Kit routing PASS.");
    }

    private static string? JsonNodeString(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) ? value.GetString() : null;
}
