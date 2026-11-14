using HarmonyLib;
using Sprocket.Engines.Editor;
using Sprocket.Vehicles.Engines;
using Sprocket.Vehicles.Engines.Editor;
using SprocketEngineExpansion.Native;
namespace SprocketEngineExpansion.Editor;

[HarmonyPatch]
internal static class EditorScope
{
    [HarmonyPrefix,HarmonyPatch(typeof(CombustionEngineComponentEditor),nameof(CombustionEngineComponentEditor.OnGUI))]
    private static void Enter(CombustionEngineComponentEditor __instance,out CombustionEngine? __state)
    {__state=AssemblyStatus.EditorContext;AssemblyStatus.EditorContext=__instance.Component;}
    [HarmonyFinalizer,HarmonyPatch(typeof(CombustionEngineComponentEditor),nameof(CombustionEngineComponentEditor.OnGUI))]
    private static Exception? Leave(CombustionEngine? __state,Exception? __exception)
    {AssemblyStatus.EditorContext=__state;return __exception;}
    [HarmonyPrefix,HarmonyPatch(typeof(EngineProperties),nameof(EngineProperties.SyncDependentValues))]
    private static void EnterSync(EngineProperties __instance,out CombustionEngine? __state)
    {__state=AssemblyStatus.EditorContext;AssemblyStatus.EditorContext=EngineEditor.ForProperties(__instance)??__state;}
    [HarmonyFinalizer,HarmonyPatch(typeof(EngineProperties),nameof(EngineProperties.SyncDependentValues))]
    private static Exception? LeaveSync(CombustionEngine? __state,Exception? __exception)
    {AssemblyStatus.EditorContext=__state;return __exception;}
}
