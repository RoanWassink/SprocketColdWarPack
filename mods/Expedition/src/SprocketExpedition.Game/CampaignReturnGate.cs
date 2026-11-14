namespace SprocketExpedition.Game;

// Keep result/reset suppression alive until the actual native unload completes.
internal sealed class CampaignReturnGate
{
    internal bool Pending { get; private set; }
    internal bool SceneRequested { get; private set; }
    internal bool Request()
    {
        if (Pending) return false;
        Pending = true;
        return true;
    }
    internal void SceneStarted() { if (Pending) SceneRequested = true; }
    internal bool AllowsCleanup(string operation, bool nativeExiting) =>
        operation == "ResetGame" && (SceneRequested || nativeExiting);
    internal bool Complete(bool mainMenuStarted, bool gameControllerPresent)
    {
        if (!Pending || !SceneRequested || !mainMenuStarted || gameControllerPresent) return false;
        Reset();
        return true;
    }
    internal void Reset() { Pending = SceneRequested = false; }
}
