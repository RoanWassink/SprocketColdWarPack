using System.Globalization;
using System.Text.Json.Nodes;

namespace SprocketExpandedBallistics.Materials;

/// <summary>
/// Live state for the UI Kit material editor. The draft and its transaction stay authoritative;
/// this class only holds the selected row, uncommitted field text and the derived right-hand values.
/// </summary>
internal sealed class MaterialEditorModel
{
    internal static readonly string[] Fields = { "label", "date", "rhaFactor", "density", "spallFactor", "costMultiplier" };
    private readonly MaterialEditorDraft draft;
    private readonly Dictionary<string, string> edits = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string?> errors = new(StringComparer.Ordinal);
    private readonly List<MaterialEditorRow> rows = new();
    private string? selectedId;
    private int selected = -1;

    internal MaterialEditorModel(MaterialEditorDraft draft)
    {
        this.draft = draft;
        Rebuild();
    }

    internal IReadOnlyList<MaterialEditorRow> Rows => rows;
    internal int SelectedIndex => selected;
    internal string? SelectedId => selectedId;
    internal bool HasErrors => errors.Values.Any(error => error != null);
    internal string? FirstError => errors.Values.FirstOrDefault(error => error != null);
    internal string? SelectedGroup => selected >= 0 && selected < rows.Count ? rows[selected].Group : null;
    internal string? SelectedLabel => selected >= 0 && selected < rows.Count ? rows[selected].Label : null;

    private JsonObject? Item => selected >= 0 && selected < rows.Count && rows[selected].Index >= 0 && rows[selected].Index < draft.Items.Count
        ? draft.Items[rows[selected].Index]!.AsObject() : null;

    internal void Select(string id)
    {
        selectedId = id;
        Rebuild();
        edits.Clear();
        errors.Clear();
    }

    internal void Reload()
    {
        Rebuild();
        edits.Clear();
        errors.Clear();
    }

    private void Rebuild()
    {
        rows.Clear();
        for (var i = 0; i < draft.Items.Count; i++)
        {
            var item = draft.Items[i]!.AsObject();
            var id = item["id"]!.GetValue<string>();
            var label = item["label"]?.GetValue<string>() ?? id;
            var behavior = item["behavior"]?.GetValue<string>() ?? "passive";
            rows.Add(new(id, label, MaterialEditorView.GroupOf(id, label, behavior), behavior, i));
        }
        var sorted = MaterialEditorView.Sorted(rows);
        rows.Clear();
        rows.AddRange(sorted);
        selected = rows.FindIndex(r => r.Id == selectedId);
        if (selected < 0)
        {
            selected = rows.Count == 0 ? -1 : 0;
            selectedId = selected < 0 ? null : rows[selected].Id;
        }
    }

    internal string Text(string field)
    {
        if (edits.TryGetValue(field, out var edited)) return edited;
        var item = Item;
        if (item == null) return "";
        var node = item[field];
        if (node == null) return "";
        return field switch
        {
            "label" or "date" => node.GetValue<string>(),
            _ => node.GetValue<double>().ToString("0.########", CultureInfo.InvariantCulture)
        };
    }

    internal string? Error(string field) => errors.TryGetValue(field, out var error) ? error : null;
    internal string ErrorText => FirstError ?? "";

    /// <summary>Keeps typed text stable, applies valid values to the draft immediately and marks invalid ones.</summary>
    internal void Edit(string field, string text)
    {
        if (Item == null) return;
        edits[field] = text;
        var check = MaterialEditorView.CheckField(field, text);
        if (!check.Ok)
        {
            errors[field] = check.Error;
            return;
        }
        errors.Remove(field);
        if (field is "rhaFactor" or "density" or "spallFactor" or "costMultiplier")
            draft.Set(rows[selected].Index, field, check.Value.ToString(CultureInfo.InvariantCulture));
        else
            draft.Set(rows[selected].Index, field, text);
    }

    internal void Commit(string field)
    {
        if (!edits.TryGetValue(field, out var text)) return;
        Edit(field, text);
        if (errors.TryGetValue(field, out var error) && error != null) return;
        edits.Remove(field);
        if (field == "label")
        {
            selectedId = Item?["id"]?.GetValue<string>() ?? selectedId;
            Rebuild();
        }
    }

    internal bool CanSave(out string? reason)
    {
        foreach (var field in Fields)
        {
            var text = Text(field);
            var check = MaterialEditorView.CheckField(field, text);
            if (!check.Ok)
            {
                errors[field] = check.Error;
                reason = check.Error;
                return false;
            }
        }
        reason = MaterialEditorView.CheckMaterial(Text("label"), Text("date"), Text("rhaFactor"), Text("density"), Text("spallFactor"), Text("costMultiplier"));
        return reason == null;
    }

    /// <summary>Writes every valid typed value to the draft before the existing transaction runs.</summary>
    internal void ApplyPending()
    {
        foreach (var field in Fields)
        {
            var text = Text(field);
            var check = MaterialEditorView.CheckField(field, text);
            if (!check.Ok) throw new FormatException(check.Error);
            draft.Set(rows[selected].Index, field, field is "rhaFactor" or "density" or "spallFactor" or "costMultiplier"
                ? check.Value.ToString(CultureInfo.InvariantCulture) : text);
        }
        edits.Clear();
    }

    internal MaterialBalanceResult Balance
    {
        get
        {
            var rha = MaterialEditorView.CheckField("rhaFactor", Text("rhaFactor")).Value;
            var density = MaterialEditorView.CheckField("density", Text("density")).Value;
            var cost = MaterialEditorView.CheckField("costMultiplier", Text("costMultiplier")).Value;
            return MaterialBalance.Calculate((float)rha, (float)density, (float)cost);
        }
    }

    internal double CostPerSquareMetre => MaterialEditorView.CostPerSquareMetre(
        MaterialEditorView.CheckField("density", Text("density")).Value,
        MaterialEditorView.CheckField("costMultiplier", Text("costMultiplier")).Value);

    internal string RecipeLabel
    {
        get
        {
            var role = Item?["reactionRecipe"]?.GetValue<string>();
            if (string.IsNullOrEmpty(role) || role == "none") return "None";
            return draft.ReactionChoices.FirstOrDefault(choice => choice.Id == role).Label ?? role;
        }
    }

    internal string ReactionSummary
    {
        get
        {
            var item = Item;
            if (item == null) return "";
            var behavior = item["behavior"]?.GetValue<string>() ?? "passive";
            var response = item["response"] as JsonObject;
            if (response == null) return "Passive material: native RHA, density and spall only.";
            var id = response["responseId"]?.GetValue<string>() ?? "editor";
            var calibration = response["calibration"] as JsonObject;
            var heat = calibration?["heatRetention"]?.GetValue<double>();
            var loss = calibration?["lossPerReference"]?.GetValue<double>();
            var parts = new List<string> { behavior, id };
            if (heat is { } h) parts.Add($"HEAT x{h:0.###}");
            if (loss is { } l) parts.Add($"loss {l:0.###}");
            return string.Join(" · ", parts);
        }
    }

    internal string CostText => Balance.IsValid
        ? $"{Balance.EffectiveCostMultiplier:0.000}x effective · {CostPerSquareMetre:0} /m²"
        : "Invalid values";
}
