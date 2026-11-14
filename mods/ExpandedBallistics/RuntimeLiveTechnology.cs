using HarmonyLib;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using Sprocket.TechTrees;
namespace SprocketExpandedBallistics;

/// <summary>
/// 0.22.14 fix for "an own cassette only works after a restart". "Reload armour materials" (and the composer's live
/// registration, which uses it) replaced the technology lookup of the designer vehicle only. Play, and the designer again
/// afterwards, build a fresh vehicle frame through <see cref="TechTreeLoader.GetTechFrameAtDate"/> from the list the game
/// loaded at startup, so the new cassette was "Technology file missing or not loaded" there and every hit stayed native.
/// The reloaded list is now published to the native loader, and every later frame is built from it; the vehicle date
/// still selects what is in the frame (the 9999.12.31 sentinel allows everything).
/// </summary>
[HarmonyPatch]
internal static class RuntimeLiveTechnology
{
    private static Il2CppReferenceArray<Tech>? live;
    private static readonly HashSet<string> registered=new(StringComparer.Ordinal);
    /// <summary>The frame hook is installed: a live registration also reaches Play and later designer sessions.</summary>
    internal static bool Patched;
    internal static void Publish(Il2CppReferenceArray<Tech> technologies)
    {
        live=technologies;
        try{if(ITechFrameFactory.Instance?.TryCast<TechTreeLoader>() is {} loader)loader.technologies=technologies;}
        catch(Exception ex){Plugin.ModLog.LogWarning("[Cassettes] Native technology loader not updated directly ("+ex.Message+"); the frame hook still applies.");}
        if(ReleaseDiagnostics.Enabled)Plugin.ModLog.LogInfo($"[Cassettes] Live technology list published ({technologies.Length} entries); frame hook {(Patched?"on":"off")}.");
    }
    /// <summary>The composer registered this material live (saved during this session, not loaded at startup).</summary>
    internal static void Registered(string materialId)=>registered.Add(materialId);
    /// <summary>
    /// Whether a live-registered material is also known in Play. Materials loaded at startup always are; a live one only with the hook.
    /// </summary>
    internal static bool ReachesPlay(string materialId)=>CassetteAvailability.LiveReachesPlay(registered.Contains(materialId),Patched);
    [HarmonyPrefix,HarmonyPatch(typeof(TechTreeLoader),nameof(TechTreeLoader.GetTechFrameAtDate),new[]{typeof(TechDate)})]
    private static void BeforeFrame(TechTreeLoader __instance)
    {
        var list=live;if(list==null)return;
        try{if(__instance.technologies==null||__instance.technologies.Pointer!=list.Pointer)__instance.technologies=list;}
        catch(Exception){}
    }
    internal static void Clear(){live=null;registered.Clear();}
}
