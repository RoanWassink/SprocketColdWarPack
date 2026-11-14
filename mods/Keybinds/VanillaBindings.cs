using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;

namespace SprocketKeybinds;

internal static class VanillaBindings
{
    // The menu's targets cover disabled maps as well. Enabled actions supplement controls created at runtime.
    // Effective paths include live overrides; do not substitute factory paths or the persisted RebindMap alone.
    internal static (bool Known, string[] Paths) Read()
    {
        try
        {
            var menu = VanillaMenu.Menu;
            var targets = menu?.keymappingMenu?.rebindTargets;
            var rebindMap = menu?.keymappingMenu?.rebindMap;
            if (targets == null || targets.Length == 0 || rebindMap == null || Keyboard.current == null) return (false, Array.Empty<string>());
            var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var assets = new HashSet<IntPtr>();
            var live = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var liveActions = new List<InputAction>();
            foreach (var row in menu!.keymappingGUI.GetComponentsInChildren<Sprocket.SettingConfiguration.RebindActionUI>(true))
            {
                if (VanillaMenu.Rows.Any(r => r.UI.Pointer == row.Pointer)) continue;
                var action = row.ActionReference?.action;
                if (action == null || InputRuntime.IsManaged(action)) continue;
                liveActions.Add(action);
                for (int i = 0; i < action.bindings.Count; i++)
                {
                    var binding = action.bindings[i];
                    string id = binding.id.ToString(), effective = binding.effectivePath ?? "";
                    if (live.TryGetValue(id, out var previous) && previous != effective)
                        throw new InvalidOperationException("Ambiguous vanilla working copies.");
                    live[id] = effective;
                }
            }
            void AddAction(InputAction action, Dictionary<string, string?>? saved = null)
            {
                var bindings = Enumerable.Range(0, action.bindings.Count).Select(i =>
                {
                    var binding = action.bindings[i];
                    string id = binding.id.ToString();
                    // Working UI paths win over persisted choices, including explicit unbind/reset edits.
                    string path = live.TryGetValue(id, out var working) ? working : saved != null && saved.TryGetValue(id, out var stored)
                        ? stored ?? binding.path ?? "" : binding.effectivePath ?? "";
                    return new FreeKeys.NativeBinding(path, binding.isComposite, binding.isPartOfComposite);
                });
                foreach (var path in FreeKeys.SinglePaths(bindings))
                {
                    var controls = InputSystem.FindControls(path);
                    try
                    {
                        if (controls.Count == 0 && (path.Contains("keyboard", StringComparison.OrdinalIgnoreCase) || !path.StartsWith('<')))
                            throw new InvalidOperationException("Unresolved keyboard binding.");
                        for (int i = 0; i < controls.Count; i++)
                        {
                            var control = controls[i];
                            if (control.device.TryCast<Keyboard>() == null) continue;
                            if (control.name == "anyKey")
                                paths.UnionWith(FreeKeys.Groups.SelectMany(g => g).Select(k => "<Keyboard>/" + k));
                            else paths.Add("<Keyboard>/" + control.name);
                        }
                    }
                    finally { controls.Dispose(); }
                }
            }
            void AddAsset(InputActionAsset asset, Dictionary<string, string?>? saved = null)
            {
                if (!assets.Add(asset.Pointer)) return;
                for (int m = 0; m < asset.actionMaps.Count; m++)
                    for (int a = 0; a < asset.actionMaps[m].actions.Count; a++) AddAction(asset.actionMaps[m].actions[a], saved);
            }
            for (int i = 0; i < targets.Length; i++)
            {
                if (targets[i].Asset == null) return (false, Array.Empty<string>());
                Dictionary<string, string?>? saved = null;
                if (string.IsNullOrEmpty(targets[i].SaveID)) return (false, Array.Empty<string>());
                if (rebindMap.TryGetRebind(targets[i].SaveID, out var json)) saved = FreeKeys.ReadNativeOverrides(json);
                AddAsset(targets[i].Asset, saved);
            }
            // Vanilla rows can refer to a working copy while a profile is being edited.
            foreach (var action in liveActions) AddAction(action);
            var enabled = InputSystem.ListEnabledActions();
            for (int i = 0; i < enabled.Count; i++)
            {
                var action = enabled[i];
                if (InputRuntime.IsManaged(action)) continue;
                var asset = action.actionMap?.asset;
                if (asset != null) AddAsset(asset); else AddAction(action);
            }
            return (true, paths.ToArray());
        }
        catch { return (false, Array.Empty<string>()); }
    }
}
