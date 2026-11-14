using Sprocket;
using Sprocket.VehicleDesigner;
using UnityEngine;

namespace SprocketExpedition.Game;

// Native scene detection used by CampaignControllerHost.Tick.
internal static class CampaignControllerHub
{
    private static bool contextAvailable;
    private static float nextContextCheck;

    internal static bool InCampaignScene()
    {
        var now = Time.realtimeSinceStartup;
        if (now < nextContextCheck) return contextAvailable;
        nextContextCheck = now + 0.5f;
        try
        {
            if (UnityEngine.Object.FindObjectOfType<DeathmatchGameMode>() != null
                || UnityEngine.Object.FindObjectOfType<VehicleDesignerCore>() != null)
            {
                contextAvailable = true;
                return true;
            }

            var modes = UnityEngine.Object.FindObjectsOfType<ScenarioGameMode>();
            contextAvailable = modes.Length == 1
                && modes[0] != null
                && modes[0].gameObject.scene.name == "Defence";
            return contextAvailable;
        }
        catch
        {
            contextAvailable = false;
            return false;
        }
    }
}
