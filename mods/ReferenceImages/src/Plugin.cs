using BepInEx;
using BepInEx.Configuration;
using BepInEx.Unity.IL2CPP;
using HarmonyLib;
using SprocketKeybinds;
using SprocketUiKit;
using UnityEngine;

namespace SprocketReferenceImages;

[BepInPlugin(Guid, "Sprocket Reference Images", Version)]
[BepInDependency(UiKit.PluginGuid, ">=0.1.3 <0.2.0")]
[BepInDependency(Keybinds.PluginGuid, ">=0.1.3 <0.2.0")]
public sealed class Plugin : BasePlugin
{
    public const string Guid = "sprocket.referenceimages", Version = "0.1.2";
    internal static Plugin Instance = null!;
    private ConfigEntry<bool> detailed = null!;
    private Harmony? harmony;
    internal static bool Detailed => Instance.detailed.Value;
    internal static void Info(string s) => Instance.Log.LogInfo("[Sprocket Reference Images] " + s);
    internal static void Error(string s) => Instance.Log.LogError("[Sprocket Reference Images] " + s);
    internal static void Diag(string s) { if (Detailed) Info(s); }
    private static readonly HashSet<string> warned = new();
    internal static void WarnOnce(string s) { if (warned.Add(s)) Instance.Log.LogWarning("[Sprocket Reference Images] " + s); }
    public override void Load()
    {
        Instance = this;
        detailed = Config.Bind("Diagnostics", "DetailedLogging", true, "Verbose trace lines for testing. Warnings and errors are always logged.");
        harmony = new Harmony(Guid);
        try { IconHooks.Install(harmony); } // Exactly H3; no PatchAll and no save/load callbacks.
        catch(Exception ex) { Error("hook failed: "+ex.Message);harmony.UnpatchSelf();throw; }
        ReferencePanel.Initialize();
        AddComponent<BoardDriver>();
        Info("0.1.2 loaded; author Nero; hook OK: H3 CreateCard only.");
    }
    public override bool Unload()
    {
        harmony?.UnpatchSelf(); BoardRuntime.Clear(); ReferencePanel.Close();
        Keybinds.Unregister(Guid,"panel"); Keybinds.Unregister(Guid,"paste");
        ImageLibrary.Clear(); BoardMeshes.Clear(); IconHooks.Clear();
        return true;
    }
}

public sealed class BoardDriver : MonoBehaviour
{
    public BoardDriver(IntPtr pointer) : base(pointer) { }
    private float next;
    public void Update()
    {
        try
        {
            ReferencePanel.Keys();
            if (Time.unscaledTime < next) return;
            next = Time.unscaledTime + .2f;
            BoardRuntime.Poll();
        }
        catch(Exception ex) { Plugin.WarnOnce("poll error: " + ex.Message); }
    }
    public void LateUpdate()
    {
        try { BoardRuntime.UpdateKnown(); }
        catch(Exception ex) { Plugin.WarnOnce("visual update error: "+ex.Message); }
    }
}
