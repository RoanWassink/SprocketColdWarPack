using Il2CppInterop.Runtime;
using Sprocket.Gameplay;
using Sprocket.VehicleDesigner.Access;
using UnityEngine;

namespace SprocketExpedition.Game;

// Native event handoff: see THIRD-PARTY-NOTICES.md. Preserve photo mode and
// restore the previous subscriber when this Expedition editor visit ends.
internal static class CampaignDesignerPlayHook
{
    private static VehicleDesignerPlayerState? state;
    private static Il2CppSystem.EventHandler<VehicleEditorStateChangeRequestEventArgs>? original, wrapper;
    private static bool forwarding;

    internal static void Tick(bool editing)
    {
        if (!editing) { Release(); return; }
        var found = UnityEngine.Object.FindObjectOfType<VehicleDesignerPlayerState>();
        if (found == null) return;
        if (state != null && state.Pointer != found.Pointer) Release();
        if (state != null && wrapper != null && found.StateChangeRequested?.Pointer == wrapper.Pointer) return;
        wrapper ??= DelegateSupport.ConvertDelegate<Il2CppSystem.EventHandler<VehicleEditorStateChangeRequestEventArgs>>(
            new Action<Il2CppSystem.Object, VehicleEditorStateChangeRequestEventArgs>(Requested))!;
        state = found;
        original = found.StateChangeRequested;
        found.StateChangeRequested = wrapper;
        Plugin.Logger.LogInfo("Expedition: native designer Play is connected to the Custom Battle campaign.");
    }

    private static void Requested(Il2CppSystem.Object sender, VehicleEditorStateChangeRequestEventArgs args)
    {
        if (forwarding) return;
        if (CampaignNativeFlow.OwnsSession && args.Request == VehicleEditorStateChangeRequest.Exit)
        {
            CampaignControllerHost.PlayFromDesigner();
            return;
        }
        forwarding = true;
        try { original?.Invoke(sender, args); }
        finally { forwarding = false; }
    }

    internal static void Release()
    {
        if (state != null && wrapper != null && state.StateChangeRequested?.Pointer == wrapper.Pointer)
            state.StateChangeRequested = original;
        state = null;
        original = null;
        wrapper = null;
    }
}
