using System.Reflection;
using System.Security.Cryptography;
using BepInEx.Unity.IL2CPP;
namespace SprocketCupolas;

// Existing Mast IO-API registration only. No output, flow, storage or hook is created.
internal static class IconClient
{
    internal static bool Bind(Type? facade, string path)
    {
        try {
            if (facade == null || Convert.ToInt32(facade.GetField("ApiVersion")?.GetRawConstantValue()) != 1) return false;
            var rev = facade.GetField("ApiRevision");
            if (rev == null || Convert.ToInt32(rev.IsLiteral ? rev.GetRawConstantValue() : rev.GetValue(null)) < 1) return false;
            var method = facade.GetMethod("RegisterPartIcon", BindingFlags.Public | BindingFlags.Static, null, new[] { typeof(string), typeof(string) }, null);
            if (method?.ReturnType != typeof(bool)) return false;
            return method.CreateDelegate<Func<string, string, bool>>()(CupolaPolicy.PartGuid, path);
        } catch { return false; }
    }
    internal static void Register()
    {
        bool ok = false;
        try {
            string path = Path.Combine(Path.GetDirectoryName(typeof(Plugin).Assembly.Location)!, "t90a-cupola-icon.png");
            if (Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))) == CupolaMeshData.IconSha256 &&
                IL2CPPChainloader.Instance.Plugins.TryGetValue("sprocket.telescopicmast", out var info))
                ok = Bind(info.Instance?.GetType().Assembly.GetType("SprocketTelescopicMast.Io.IoApi", false), path);
        } catch { }
        Plugin.Instance.Log.LogInfo("[Cupola] ICON " + (ok ? "registered via optional Mast IO-API revision 1" : "native fallback; optional Mast icon API or verified icon unavailable"));
    }
}
