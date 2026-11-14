using System.Globalization;
using BepInEx.Configuration;
using Sprocket.Vehicles.PlateStructures.Design;
using SprocketUiKit;

namespace SprocketExpandedBallistics.Materials;

internal sealed record MaterialEditorCassetteRow(string Id, string Label, string Subtitle);

/// <summary>
/// Material editor on a Sprocket UI Kit panel: materials grouped on the left, checked fields in the
/// middle and live balance numbers, recipe and reaction summary on the right. The Cassettes tab lists
/// the composer's catalogue and opens the composer. Save uses the existing
/// <see cref="MaterialEditorSession"/> transaction. The JSON Editor session remains behind
/// <c>[Material editor] Use UI Kit = false</c>.
/// </summary>
internal static class MaterialEditorPanel
{
    private static ConfigEntry<bool>? useKit;
    internal static bool UseKit => useKit?.Value != false;
    internal static void Configure(ConfigFile config) => useKit = config.Bind("Material editor", "Use UI Kit", true,
        "Open the material editor as a UI Kit panel. false = the older JSON Editor session (kept as a fallback).");

    private static Panel? panel;
    private static MaterialEditorSession? session;
    private static MaterialEditorModel? model;
    private static PlateStructureEditor? editor;
    private static Action? refresh;
    private static int tab;
    private static string search = "";
    private static IReadOnlyList<MaterialEditorRow> filtered = Array.Empty<MaterialEditorRow>();
    private static readonly List<MaterialEditorCassetteRow> cassettes = new();
    private static string? selectedCassette;

    internal static bool TryOpen(PlateStructureEditor source, Action refreshAction)
    {
        try
        {
            Ensure();
            var p = panel!;
            if (p.IsOpen && model != null)
            {
                editor = source;
                refresh = refreshAction;
                p.Open();
                p.SetStatus("Editing the open material draft. Save or close it first to reload from disk.", StatusKind.Info);
                return true;
            }
            Load(source, refreshAction);
            if (!p.Open()) return false;
            p.SetStatus($"{model!.Rows.Count} materials in {MaterialEditorView.GroupOrder.Length - 1} groups. Changes stay in the editor until you save.", StatusKind.Info);
            return true;
        }
        catch (Exception ex)
        {
            SprocketExpandedBallistics.Plugin.ModLog.LogWarning("[Material editor] UI Kit panel unavailable; using the JSON Editor session: " + ex);
            return false;
        }
    }

    /// <summary>"Edit materials" and any future entry point use exactly this route.</summary>
    internal static bool OpenOrFallback(PlateStructureEditor source, Action refreshAction)
    {
        if (MaterialEditorRoute.Choose(UseKit, true) == MaterialEditorTarget.UiKit && TryOpen(source, refreshAction))
            return true;
        SprocketJsonEditor.JsonEditor.Open(new MaterialEditorSession(refreshAction));
        return false;
    }

    private static void Load(PlateStructureEditor source, Action refreshAction)
    {
        editor = source;
        refresh = refreshAction;
        tab = 0;
        search = "";
        selectedCassette = null;
        session = new MaterialEditorSession(() =>
        {
            refreshAction();
            try { source.RequestRedraw(); } catch (Exception) { }
        });
        model = new MaterialEditorModel(session.Draft);
        ReadCassettes();
        UpdateFilter();
    }

    private static void ReadCassettes()
    {
        cassettes.Clear();
        try
        {
            var models = SprocketExpandedBallistics.RuntimeArmourResponses.Snapshot.Cassettes;
            if (models == null) return;
            foreach (var pair in models)
                cassettes.Add(new(pair.Key, pair.Value.Preset.Label,
                    $"{pair.Value.DepthMm:0.#} mm - {pair.Value.Preset.Layers.Count(l => l.Material != "air")} layers"));
            cassettes.Sort((a, b) => string.Compare(a.Label, b.Label, StringComparison.OrdinalIgnoreCase));
        }
        catch (Exception ex)
        {
            SprocketExpandedBallistics.Plugin.ModLog.LogWarning("[Material editor] Cassette catalogue unavailable: " + ex.Message);
        }
    }

    private static void UpdateFilter()
    {
        if (model == null) { filtered = Array.Empty<MaterialEditorRow>(); return; }
        filtered = string.IsNullOrWhiteSpace(search)
            ? model.Rows
            : model.Rows.Where(r => r.Label.Contains(search, StringComparison.OrdinalIgnoreCase) ||
                                    r.Id.Contains(search, StringComparison.OrdinalIgnoreCase) ||
                                    r.Group.Contains(search, StringComparison.OrdinalIgnoreCase)).ToArray();
    }

    // ---------------- construction (once) ----------------
    private static void Ensure()
    {
        if (panel != null) return;
        var p = UiKit.CreatePanel(SprocketExpandedBallistics.BallisticsKeybindMetadata.Owner, "materials", "Material editor", "Expanded Ballistics", 1600, 900);
        panel = p;
        p.AllowedContexts = UiContexts.Designer;
        p.StayOpenWhilePaused = true;
        p.OnClose += () => { session = null; model = null; filtered = Array.Empty<MaterialEditorRow>(); cassettes.Clear(); selectedCassette = null; };
        p.AddShortcut("Ctrl+S", Save, "save");
        p.AddKeyHint("Enter", "save");
        var cols = p.Columns(430, 0, 430);
        BuildList(cols[0]);
        BuildForm(cols[1]);
        BuildLive(cols[2]);
        SprocketExpandedBallistics.Plugin.ModLog.LogInfo("[Material editor] UI Kit panel created.");
    }

    private static void BuildList(Column c)
    {
        var head = c.Section("Materials", () => model == null ? "" : $"{filtered.Count} of {model.Rows.Count}");
        head.Tabs(new[] { "Materials", "Cassettes" }, () => tab, i =>
        {
            tab = i;
            if (i == 0) panel?.SetStatus("Materials grouped as Steel, Ceramic, Composite and ERA.", StatusKind.Info);
            else panel?.SetStatus(cassettes.Count == 0 ? "The cassette catalogue is unavailable." : "Select a cassette or open the composer.", cassettes.Count == 0 ? StatusKind.Warning : StatusKind.Info);
        });
        head.TextField(() => search, v => { search = v ?? ""; UpdateFilter(); }, "Search material, ID or group")
            .OnChange(v => { search = v ?? ""; UpdateFilter(); }).MaxChars(40).VisibleWhen(() => tab == 0);
        c.List<MaterialEditorRow>(() => filtered, (r, row) =>
            {
                row.Title = r.Label;
                row.Subtitle = r.Group;
                row.Tag = r.Group;
                row.TagChip = false;
                row.Selected = model != null && r.Id == model.SelectedId;
            })
            .OnSelect(r => { model?.Select(r.Id); panel?.SetStatus($"{r.Label} - {r.Group}", StatusKind.Info); })
            .Empty("No material matches this search.").VisibleWhen(() => tab == 0).Fill();
        c.List<MaterialEditorCassetteRow>(() => cassettes, (r, row) =>
            {
                row.Title = r.Label;
                row.Subtitle = r.Subtitle;
                row.Tag = "preset";
                row.TagChip = true;
                row.Selected = r.Id == selectedCassette;
            })
            .OnSelect(r => { selectedCassette = r.Id; panel?.SetStatus($"{r.Label}: {r.Subtitle}", StatusKind.Info); })
            .Empty("No cassette catalogue loaded. The composer can still create one.").VisibleWhen(() => tab == 1).Fill();
        var foot = c.Footer("Actions");
        foot.Button("Open cassette composer", OpenComposer, ButtonStyle.Primary)
            .EnabledWhen(() => editor != null)
            .Tooltip("Open the cassette composer for the selected plate (or the last inspected plate).");
        foot.Button("Reload materials", Reload, ButtonStyle.Quiet)
            .Tooltip("Re-read the Technology files and the cassette catalogue. Unsaved field text is discarded.");
    }

    private static void BuildForm(Column c)
    {
        var form = c.Section("Fields", () => model?.SelectedLabel ?? "No material selected");
        form.SectionVisibleWhen(() => tab == 0);
        form.Label(() => model?.SelectedGroup ?? "", TextStyle.Small);
        form.Label("Every value is checked before it reaches the existing save transaction.", TextStyle.Small).Wrap();
        Field(form, "label", "Name");
        Field(form, "date", "Date");
        Field(form, "rhaFactor", "RHA factor");
        Field(form, "density", "Density (kg/m3)");
        Field(form, "spallFactor", "Spall factor");
        Field(form, "costMultiplier", "Cost multiplier");
        form.Label("The MaterialBalance floor is shown live on the right; a lower requested cost is raised by the game.",
            TextStyle.Small).Wrap();

        var info = c.Section("Cassette", () => selectedCassette == null ? "Cassettes" : cassettes.FirstOrDefault(r => r.Id == selectedCassette)?.Label ?? "Cassette");
        info.SectionVisibleWhen(() => tab == 1);
        info.Label(() => selectedCassette == null
            ? "Pick a cassette on the left, or open the composer to build or edit one. Composition editing stays versioned in the composer."
            : cassettes.FirstOrDefault(r => r.Id == selectedCassette)?.Subtitle ?? "", TextStyle.Body).Wrap();
        info.Label("The composer saves a new immutable version; plates keep the version they use. New Technology may need a Sprocket restart.",
            TextStyle.Small).Wrap();
        info.Button("Open cassette composer", OpenComposer, ButtonStyle.Primary).EnabledWhen(() => editor != null);
    }

    private static void Field(Stack body, string field, string label)
    {
        var row = body.Labeled(label);
        var fieldControl = row.TextField(() => model?.Text(field) ?? "", v =>
            {
                model?.Edit(field, v ?? "");
                model?.Commit(field);
            }, field == "date" ? "yyyy.MM.dd" : field == "label" ? "Material name" : "0")
            .MaxChars(field == "label" ? 80 : 16)
            .OnChange(v => model?.Edit(field, v ?? ""))
            .Error(() => model?.Error(field));
        if (field is not "label" and not "date") fieldControl.Numeric();
        fieldControl.Tooltip(field switch
            {
                "label" => "1-80 printable characters. The saved ID never changes.",
                "date" => "Native technology date (yyyy.MM.dd).",
                "rhaFactor" => "Effective RHA per unit thickness; above zero.",
                "density" => "kg/m3; above zero. Also drives the live weight efficiency.",
                "spallFactor" => "Passive spall factor; zero or above.",
                "costMultiplier" => "Requested price. The MaterialBalance floor may raise it.",
                _ => ""
            });
    }

    private static void BuildLive(Column c)
    {
        var live = c.Section("Live", () => model?.SelectedLabel ?? "");
        live.SectionVisibleWhen(() => tab == 0);
        Row(live, "Ts (thickness)", () => model == null ? "" : model.Balance.IsValid ? model.Balance.ThicknessEfficiency.ToString("0.###", CultureInfo.InvariantCulture) : "-");
        Row(live, "Em (weight)", () => model == null ? "" : model.Balance.IsValid ? model.Balance.WeightEfficiency.ToString("0.###", CultureInfo.InvariantCulture) : "-");
        Row(live, "Requested", () => model == null ? "" : model.Balance.IsValid ? model.Balance.RequestedCostMultiplier.ToString("0.###", CultureInfo.InvariantCulture) + "x" : "-");
        Row(live, "Floor", () => model == null ? "" : model.Balance.IsValid ? model.Balance.MinimumCostMultiplier.ToString("0.###", CultureInfo.InvariantCulture) + "x" : "-");
        Row(live, "Effective", () => model == null ? "" : model.Balance.IsValid ? model.Balance.EffectiveCostMultiplier.ToString("0.###", CultureInfo.InvariantCulture) + "x" : "-");
        Row(live, "Cost / m2", () => model == null ? "" : model.Balance.IsValid ? model.CostPerSquareMetre.ToString("0", CultureInfo.InvariantCulture) + " @100 mm" : "-");
        live.Warning(() => model?.Balance.WasAdjusted == true
                ? "Requested cost is below the MaterialBalance floor; the game uses the effective value."
                : "")
            .Severity(StatusKind.Warning).VisibleWhen(() => model?.Balance.WasAdjusted == true);
        live.Label("MODIFIER / REACTION", TextStyle.Section);
        Row(live, "Recipe", () => model?.RecipeLabel ?? "");
        live.Label(() => model?.ReactionSummary ?? "", TextStyle.Small).Wrap().VisibleWhen(() => model != null);
        live.Label("Cost / m2 is one square metre at 100 mm actual thickness, in game cost-per-kg units.", TextStyle.Small).Wrap();
        var foot = c.Footer("Save");
        foot.Label(() => model?.ErrorText ?? "", TextStyle.Small).Wrap();
        var buttons = foot.Row(8);
        buttons.Button("Save", Save, ButtonStyle.Primary).EnabledWhen(() => tab == 0 && model != null && panel?.HasErrors != true)
            .Tooltip("Write Technology, responses, labels and reactions in one transaction with backup and rollback (Ctrl+S).");
        buttons.Button("Close", () => panel?.Close(), ButtonStyle.Quiet).Tooltip("Close without saving; unsaved field text is discarded (Esc).");
    }

    private static void Row(Stack body, string label, Func<string> value)
    {
        var row = body.Row(8);
        row.Label(label, TextStyle.Value).Colour("#b8ad94").Width(118);
        row.Label(value, TextStyle.Value).AlignRight().Flex();
    }

    // ---------------- actions ----------------
    private static void Save()
    {
        if (panel == null || model == null || session == null || tab != 0) return;
        if (!model.CanSave(out var reason))
        {
            panel.SetStatus(reason ?? "Correct the marked fields before saving.", StatusKind.Error);
            return;
        }
        try
        {
            model.ApplyPending();
            session.Save();
            model.Reload();
            UpdateFilter();
            panel.SetStatus($"Saved {model.SelectedLabel ?? "the material"}. The live catalogue was refreshed.", StatusKind.Success);
            SprocketExpandedBallistics.Plugin.ModLog.LogInfo("[Material editor] Saved through the existing MaterialEditorDraft transaction.");
        }
        catch (Exception ex)
        {
            panel.SetStatus("Not saved: " + ex.Message, StatusKind.Error);
            SprocketExpandedBallistics.Plugin.ModLog.LogWarning("[Material editor] Save failed; files unchanged or rolled back: " + ex.Message);
        }
    }

    private static void Reload()
    {
        if (editor == null || refresh == null || panel == null) return;
        if (model?.HasErrors == true) { panel.SetStatus("Fix or clear the marked fields first.", StatusKind.Warning); return; }
        try
        {
            Load(editor, refresh);
            panel.SetStatus($"{model!.Rows.Count} materials reloaded from disk.", StatusKind.Success);
        }
        catch (Exception ex)
        {
            panel.SetStatus("Reload failed: " + ex.Message, StatusKind.Error);
            SprocketExpandedBallistics.Plugin.ModLog.LogWarning("[Material editor] Reload failed: " + ex);
        }
    }

    private static void OpenComposer()
    {
        if (editor == null) { panel?.SetStatus("Inspect a composite plate first, then open the composer.", StatusKind.Warning); return; }
        try { SprocketExpandedBallistics.CassetteComposerPanel.Open(editor); panel?.SetStatus("Cassette composer opened.", StatusKind.Info); }
        catch (Exception ex) { panel?.SetStatus("Composer unavailable: " + ex.Message, StatusKind.Error); }
    }

    internal static void Clear()
    {
        try { if (panel != null) UiKit.DestroyPanel(panel); } catch (Exception) { }
        panel = null; session = null; model = null; editor = null; refresh = null;
        filtered = Array.Empty<MaterialEditorRow>(); cassettes.Clear(); selectedCassette = null;
    }
}
