using System.Reflection;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using UnityEngine;

namespace SprocketCarouselAutoloader;

internal static class BeltIcon
{
    private static Sprite? icon;
    private static Texture2D? texture;
    internal static Sprite Icon()
    {
        if (icon != null) return icon;
        using var input = Assembly.GetExecutingAssembly().GetManifestResourceStream("Autoloaders.BeltIcon.png")
            ?? throw new InvalidOperationException("Belt icon resource missing");
        using var bytes = new MemoryStream();
        input.CopyTo(bytes);
        var created = new Texture2D(256,256,TextureFormat.RGBA32,false);
        try
        {
            // LoadImage is a verified callable interop wrapper on Unity 6000.3.21f1;
            // PNG bytes are embedded, so cards do not depend on the installer's file location.
            if (!ImageConversion.LoadImage(created,new Il2CppStructArray<byte>(bytes.ToArray()),false))
                throw new InvalidOperationException("Belt icon PNG could not be decoded");
            created.name = "Belt feed box icon";
            created.hideFlags = HideFlags.DontUnloadUnusedAsset;
            icon = Sprite.Create(created,new Rect(0,0,256,256),new Vector2(.5f,.5f),256);
            icon.name = created.name; icon.hideFlags = HideFlags.DontUnloadUnusedAsset;
            texture = created;
            return icon;
        }
        catch { UnityEngine.Object.Destroy(created); throw; }
    }
}
