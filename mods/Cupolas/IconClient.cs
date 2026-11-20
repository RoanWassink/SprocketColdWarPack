using System.Reflection;
using BepInEx.Unity.IL2CPP;
namespace SprocketCupolas;

// Existing Mast IO-API registration only. No output, flow, storage or hook is created.
internal static class IconClient
{
    private static readonly (string Guid, string File)[] Icons = {
        (CupolaPolicy.PartGuid,       "t90a-cupola-icon.png"),
        (CupolaPolicy.AbramsGuid,     "abrams-cupola-icon.png"),
        (CupolaPolicy.LeopardGuid,    "leopard-cupola-icon.png"),
        (CupolaPolicy.ChallengerGuid,      "challenger-cupola-icon.png"),
        (CupolaPolicy.T80BvmGuid,          "t80bvm-cupola-icon.png"),
        (CupolaPolicy.T90ADriverHatchGuid, "t90a-driver-hatch-icon.png"),
    };

    internal static bool Bind(Type? facade, string guid, string path)
    {
        try {
            if (facade == null || Convert.ToInt32(facade.GetField("ApiVersion")?.GetRawConstantValue()) != 1) return false;
            var rev = facade.GetField("ApiRevision");
            if (rev == null || Convert.ToInt32(rev.IsLiteral ? rev.GetRawConstantValue() : rev.GetValue(null)) < 1) return false;
            var method = facade.GetMethod("RegisterPartIcon", BindingFlags.Public | BindingFlags.Static, null, new[] { typeof(string), typeof(string) }, null);
            if (method?.ReturnType != typeof(bool)) return false;
            return method.CreateDelegate<Func<string, string, bool>>()(guid, path);
        } catch { return false; }
    }

    internal static void Register()
    {
        int registered = 0;
        try {
            string dir = Path.GetDirectoryName(typeof(Plugin).Assembly.Location)!;
            if (IL2CPPChainloader.Instance.Plugins.TryGetValue("sprocket.telescopicmast", out var info)) {
                var facade = info.Instance?.GetType().Assembly.GetType("SprocketTelescopicMast.Io.IoApi", false);
                foreach (var (guid, file) in Icons) {
                    string path = Path.Combine(dir, file);
                    if (File.Exists(path) && Bind(facade, guid, path)) registered++;
                }
            }
        } catch { }
        Plugin.Instance.Log.LogInfo($"[Cupola] ICON registered {registered}/{Icons.Length} via optional Mast IO-API revision 1");
    }
}
