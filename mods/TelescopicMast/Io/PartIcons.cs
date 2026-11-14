namespace SprocketTelescopicMast.Io;

/// <summary>
/// Registration of API part icons (IO-API.md §6 M3). Files are validated here but decoded only
/// by the existing CreateCard postfix, on the first card for that GUID. The first registration
/// per GUID wins. Unity-free so the rules are testable.
/// </summary>
internal static class PartIcons
{
    internal const int MaxBytes = 2 * 1024 * 1024;
    private sealed class Entry
    {
        internal string Path = "";
        internal bool Requested;
    }
    private static readonly object Gate = new();
    private static readonly Dictionary<string, Entry> Registered = new(StringComparer.OrdinalIgnoreCase);
    private static string? root;

    internal static void ConfigureRoot(string? pluginRoot)
    {
        lock (Gate) root = string.IsNullOrWhiteSpace(pluginRoot) ? null : System.IO.Path.GetFullPath(pluginRoot);
    }

    internal static string? Root { get { lock (Gate) return root; } }

    internal static bool Register(string partGuid, string pngPath)
    {
        if (!Guid.TryParse(partGuid, out var parsed)) { Warn("icon GUID '" + partGuid + "' is not a GUID."); return false; }
        partGuid = parsed.ToString("D");
        string? pluginRoot = Root;
        if (pluginRoot == null) { Warn("icon registration needs the BepInEx plugins folder."); return false; }
        if (string.IsNullOrWhiteSpace(pngPath) || !System.IO.Path.IsPathFullyQualified(pngPath))
        { Warn("icon path for " + partGuid + " must be absolute."); return false; }
        if (!string.Equals(System.IO.Path.GetExtension(pngPath), ".png", StringComparison.OrdinalIgnoreCase))
        { Warn("icon path for " + partGuid + " must end in .png."); return false; }
        string full;
        try { full = System.IO.Path.GetFullPath(pngPath); }
        catch (Exception ex) { Warn("icon path for " + partGuid + " is invalid: " + ex.Message); return false; }
        string relative;
        try { relative = System.IO.Path.GetRelativePath(pluginRoot, full); }
        catch { relative = ".."; }
        if (System.IO.Path.IsPathRooted(relative) || relative == ".." || relative.StartsWith(".." + System.IO.Path.DirectorySeparatorChar, StringComparison.Ordinal) || relative.StartsWith("../", StringComparison.Ordinal))
        { Warn("icon path for " + partGuid + " is outside BepInEx\\plugins."); return false; }
        long length;
        try
        {
            var file = new System.IO.FileInfo(full);
            if (!file.Exists) { Warn("icon file for " + partGuid + " does not exist."); return false; }
            length = file.Length;
        }
        catch (Exception ex) { Warn("icon file for " + partGuid + " cannot be read: " + ex.Message); return false; }
        if (length > MaxBytes) { Warn("icon file for " + partGuid + " exceeds 2 MB."); return false; }
        lock (Gate)
        {
            if (Registered.ContainsKey(partGuid)) { IoApiCore.Log?.Invoke("[IO API] Duplicate icon for " + partGuid + "; the first registration wins."); return false; }
            Registered[partGuid] = new Entry { Path = full };
        }
        return true;
    }

    /// <summary>Called only by the first card for this GUID; marks the lazy load as requested.</summary>
    internal static bool TryGet(string partGuid, out string path)
    {
        if (Guid.TryParse(partGuid, out var parsed)) partGuid = parsed.ToString("D");
        lock (Gate)
        {
            if (Registered.TryGetValue(partGuid, out var entry))
            { entry.Requested = true; path = entry.Path; return true; }
        }
        path = "";
        return false;
    }

    internal static bool Requested(string partGuid)
    {
        if (Guid.TryParse(partGuid, out var parsed)) partGuid = parsed.ToString("D");
        lock (Gate) return Registered.TryGetValue(partGuid, out var entry) && entry.Requested;
    }
    internal static int Count { get { lock (Gate) return Registered.Count; } }
    internal static void ClearForTests() { lock (Gate) { Registered.Clear(); root = null; } }
    private static void Warn(string message) => IoApiCore.Log?.Invoke("[IO API] " + message);
}
