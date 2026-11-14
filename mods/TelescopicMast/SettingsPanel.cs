using System.Text.Json.Nodes;
using SprocketTelescopicMast.Io;
using SprocketUiKit;
namespace SprocketTelescopicMast;

internal static class SettingsPanel
{
    // Getter bindings follow part reassignments, context changes and twin wiring while open.
    internal static void Build(Container parent, Func<SettingsBinding?> binding, Func<bool> editable)
    {
        var initial = binding(); if (initial == null) return;
        var section = parent.Stack(); section.VisibleWhen(() => binding() != null);
        section.Label("Settings", TextStyle.Section);
        section.Label("Settings are saved immediately and are outside flow undo.", TextStyle.Faint).Wrap();
        bool CanEdit() => editable() && binding() != null;
        foreach (var f in initial.Registration.Fields)
        {
            var row = section.Stack(); row.VisibleWhen(() => binding() is { } b && b.Registration.Visible(f,b.Values));
            JsonNode Read() => binding()?.Values[f.Key] ?? f.Default;
            void Write(JsonNode value)
            {
                if (!CanEdit() || binding() is not { } b) return;
                PartSettings.Change(b.Registration,b.Targets,f.Key,value,true,Plugin.Detailed ? Plugin.Trace : null);
            }
            if (f.Type == "bool") row.Labeled(f.Label).Toggle(() => Read().GetValue<bool>(), v => Write(JsonValue.Create(v)!)).EnabledWhen(CanEdit);
            else if (f.Type == "enum") row.Labeled(f.Label).Dropdown(() => f.Options.Select(o => o.Label).ToArray(),
                () => Array.FindIndex(f.Options,o => o.Value == Read().GetValue<string>()),
                i => { if (i >= 0 && i < f.Options.Length) Write(JsonValue.Create(f.Options[i].Value)!); }).EnabledWhen(CanEdit);
            else row.Labeled(f.Label).Stepper(() => Read().GetValue<double>(), v => Write(JsonValue.Create(v)!),f.Min,f.Max,f.Step,f.Unit).EnabledWhen(CanEdit);
        }
    }
}
