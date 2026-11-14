using System.Runtime.InteropServices;
using UnityEngine;

namespace SprocketReferenceImages;

internal static class ClipboardImport
{
    [DllImport("user32.dll",CharSet=CharSet.Unicode)] private static extern uint RegisterClipboardFormatW(string name);
    [DllImport("user32.dll")] private static extern bool OpenClipboard(IntPtr owner);
    [DllImport("user32.dll")] private static extern bool CloseClipboard();
    [DllImport("user32.dll")] private static extern bool IsClipboardFormatAvailable(uint format);
    [DllImport("user32.dll")] private static extern IntPtr GetClipboardData(uint format);
    [DllImport("kernel32.dll")] private static extern UIntPtr GlobalSize(IntPtr handle);
    [DllImport("kernel32.dll")] private static extern IntPtr GlobalLock(IntPtr handle);
    [DllImport("kernel32.dll")] private static extern bool GlobalUnlock(IntPtr handle);
    internal static string Paste()
    {
        uint png=RegisterClipboardFormatW("PNG");
        if(png==0)throw new InvalidOperationException("Cannot register PNG clipboard format.");
        if(!OpenClipboard(IntPtr.Zero))throw new IOException("Clipboard is busy. Click Paste again.");
        var copies=new List<(uint Format,byte[] Bytes)>();
        try
        {
            foreach(uint f in new[]{png,17u,8u})
            {
                if(!IsClipboardFormatAvailable(f))continue;
                IntPtr handle=GetClipboardData(f); if(handle==IntPtr.Zero)continue;
                ulong size=GlobalSize(handle).ToUInt64(); if(size<1||size>160_000_000)continue;
                IntPtr data=GlobalLock(handle); if(data==IntPtr.Zero)continue;
                try { var bytes=new byte[(int)size]; Marshal.Copy(data,bytes,0,bytes.Length); copies.Add((f,bytes)); }
                finally { GlobalUnlock(handle); }
            }
        }
        finally { CloseClipboard(); }
        // No Unity calls or file operations while the OS clipboard is locked.
        var errors=new List<string>();
        foreach(var copy in copies)
        {
            Texture2D? texture=null;
            try
            {
                string format;
                if(copy.Format==png) { texture=ImageLibrary.Decode(copy.Bytes,"clipboard"); format="PNG"; }
                else
                {
                    var dib=DibParser.Parse(copy.Bytes,SystemInfo.maxTextureSize);
                    texture=new Texture2D(dib.Width,dib.Height,TextureFormat.RGBA32,false,false);
                    var pixels=new Color32[dib.Width*dib.Height];
                    for(int i=0;i<pixels.Length;i++) pixels[i]=new Color32(dib.Rgba[i*4],dib.Rgba[i*4+1],dib.Rgba[i*4+2],255);
                    texture.SetPixels32(pixels); texture.Apply(false,false); format=copy.Format==17 ? "DIBV5" : "DIB";
                }
                // Re-encode accepted clipboard images with Unity, including registered PNG.
                byte[] encoded=ImageConversion.EncodeToPNG(texture);
                string name=ImageLibrary.SavePng(encoded);
                if(Plugin.Detailed)Plugin.Info($"paste: {format}, {texture.width} x {texture.height} px -> {name}");
                return name;
            }
            catch(Exception ex) { errors.Add(ex.Message); }
            finally { if(texture!=null)UnityEngine.Object.Destroy(texture); }
        }
        throw new InvalidDataException(copies.Count==0 ? "Clipboard contains no PNG or supported DIB image." : string.Join("; ",errors));
    }
}
