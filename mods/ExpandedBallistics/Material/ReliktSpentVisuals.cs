using HarmonyLib;
using Sprocket;
using Sprocket.PlayerControl;
using Sprocket.Vehicles;
using SprocketExpandedBallistics;

namespace SprocketExpandedBallistics.Materials;

// Ledger reset and existing lifecycle callbacks; render-only reconciliation uses no extra hooks.
[HarmonyPatch]
internal static class ReliktSpentVisuals
{
    private static readonly HashSet<string> WarningMessages=new(StringComparer.Ordinal);
    private static void Warn(string context,Exception error)
    {var message=context+error.Message;if(WarningMessages.Count<16&&WarningMessages.Add(message))MaterialFeature.ModLog.LogWarning(message);}
    private static int mainThread,eventLogs;
    internal static void Start(){mainThread=Environment.CurrentManagedThreadId;EraSpentRender.Start();}
    internal static void Stop(){EraSpentRender.Stop();WarningMessages.Clear();}

    [HarmonyPrefix,HarmonyPatch(typeof(VehicleObjectModel),nameof(VehicleObjectModel.Release))]
    private static void Released(VehicleObjectModel __instance)
    {try{EraSpentRender.Forget(__instance);}catch(Exception ex){Warn("ERA renderer release: ",ex);}finally{EraSurface.Forget(__instance);}}

    [HarmonyPostfix,HarmonyPatch(typeof(VehicleEditorScenarioGameState),nameof(VehicleEditorScenarioGameState.OnPlayerStateChanged))]
    private static void EnteredEditor(VehicleEditorScenarioGameState __instance,Il2CppSystem.Object __0,PlayerStateChangeEventArgs __1)
    {
        try
        {
            // This is the committed player-state event used by the native state
            // callback, not creation of the asynchronous Enter task or a request.
            var editor=__instance.editorPlayerState;
            var next=__1?.NewState;
            if(eventLogs++<24)if(SprocketExpandedBallistics.ReleaseDiagnostics.Enabled)MaterialFeature.ModLog.LogInfo($"[ERA state committed] thread={Environment.CurrentManagedThreadId} main={mainThread} previous={__1?.PreviousState?.Pointer} next={next?.Pointer} editor={editor?.Pointer}");
            if(Environment.CurrentManagedThreadId!=mainThread)return;
            if(editor==null||next==null||next.Pointer!=editor.Pointer)return;
            if(__0?.TryCast<Player>() is not {} player||player.CurrentState?.Pointer!=editor.Pointer)return;
            // Native OnPlayerStateChanged accepts a null previous state. A repeated
            // editor notification does not clear the ledger twice.
            if(__1?.PreviousState?.Pointer!=editor.Pointer){PlacedEra.ResetForEdit();EraSpentRender.ResetForEdit();}
        }
        catch(Exception ex){Warn("Relikt Edit reset: ",ex);}
    }
}
