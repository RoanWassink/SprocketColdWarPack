using System.Diagnostics;
using System.Reflection;
using UnityEngine;

namespace SprocketReferenceImages;

internal static class ImageLibrary
{
    internal static string Folder => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),"My Games","Sprocket","Reference Images");
    internal sealed class Entry { internal Texture2D Texture=null!; internal string Key=""; internal int Users; }
    private static readonly Dictionary<string, Entry> cache = new(StringComparer.OrdinalIgnoreCase);
    private static Texture2D? placeholder;
    internal static Texture2D Placeholder => placeholder ??= Decode(Resource("no-image.png"), "NO IMAGE");
    internal static IReadOnlyList<string> Files { get; private set; } = Array.Empty<string>();
    internal static void Refresh()
    {
        Directory.CreateDirectory(Folder);
        Files = Directory.EnumerateFiles(Folder).Select(Path.GetFileName).Where(n=>BoardPolicy.SafeName(n)).Select(n=>n!).OrderBy(n=>n,StringComparer.OrdinalIgnoreCase).ToArray();
    }
    internal static byte[] Resource(string name)
    {
        using var s=Assembly.GetExecutingAssembly().GetManifestResourceStream(name) ?? throw new FileNotFoundException(name);
        using var m=new MemoryStream(); s.CopyTo(m); return m.ToArray();
    }
    internal static Texture2D Decode(byte[] bytes, string label)
    {
        var (w,h)=ImageHeaders.Read(bytes); BoardPolicy.Dimensions(w,h,SystemInfo.maxTextureSize);
        var texture=new Texture2D(2,2,TextureFormat.RGBA32,true,false) { name="Reference Images " + label, hideFlags=HideFlags.HideAndDontSave, filterMode=FilterMode.Trilinear, wrapMode=TextureWrapMode.Clamp };
        try
        {
            if(!ImageConversion.LoadImage(texture,bytes,false) || texture.width!=w || texture.height!=h) throw new InvalidDataException("Image decode failed.");
            return texture;
        }
        catch { UnityEngine.Object.Destroy(texture); throw; }
    }
    internal static string? Signature(string name)
    {
        if(!BoardPolicy.SafeName(name))return null;
        var f=new FileInfo(Path.Combine(Folder,name));
        return f.Exists ? name+"|"+f.LastWriteTimeUtc.Ticks+"|"+f.Length : null;
    }
    internal static Entry Acquire(string name)
    {
        if(!BoardPolicy.SafeName(name))throw new InvalidDataException("Unsafe file name.");
        string key=Signature(name) ?? throw new FileNotFoundException("Image file is missing: " + name);
        if(cache.TryGetValue(key,out var old)) { old.Users++; return old; }
        var f=new FileInfo(Path.Combine(Folder,name));
        if(f.Length>160_000_000)throw new InvalidDataException("Encoded image exceeds 160 MB.");
        var timer=Stopwatch.StartNew();
        var texture=Decode(File.ReadAllBytes(f.FullName),name);
        var e=new Entry {Texture=texture,Key=key,Users=1}; cache.Add(key,e);
        if(Plugin.Detailed)Plugin.Info($"image loaded: {name}, {texture.width} x {texture.height} px, {timer.ElapsedMilliseconds} ms");
        return e;
    }
    internal static void Release(Entry? entry)
    {
        if(entry==null || --entry.Users>0)return;
        cache.Remove(entry.Key); UnityEngine.Object.Destroy(entry.Texture);
    }
    internal static string SavePng(byte[] bytes)
    {
        var (w,h)=ImageHeaders.Read(bytes); BoardPolicy.Dimensions(w,h,SystemInfo.maxTextureSize);
        Directory.CreateDirectory(Folder);
        string name="Clipboard-"+DateTime.Now.ToString("yyyyMMdd-HHmmss-fff")+"-"+System.Guid.NewGuid().ToString("N")[..8]+".png";
        string path=Path.Combine(Folder,name), temp=path+".tmp";
        try { File.WriteAllBytes(temp,bytes); File.Move(temp,path); }
        finally { if(File.Exists(temp))File.Delete(temp); }
        Refresh(); return name;
    }
    internal static void Clear()
    {
        foreach(var e in cache.Values)UnityEngine.Object.Destroy(e.Texture); cache.Clear();
        if(placeholder!=null)UnityEngine.Object.Destroy(placeholder); placeholder=null;
    }
}
