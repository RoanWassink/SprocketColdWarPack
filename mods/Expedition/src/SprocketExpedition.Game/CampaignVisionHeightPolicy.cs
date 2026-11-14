namespace SprocketExpedition.Game;

// Pure height policy. No camera coordinates or hidden target positions retained.
internal sealed class CampaignVisionHeightPolicy
{
    internal float Height { get; private set; } = 6f;
    internal bool Sampled { get; private set; }
    internal void Observe(float cameraY, float playerHullY)
    {
        var height = cameraY - playerHullY;
        if (!float.IsFinite(cameraY) || !float.IsFinite(playerHullY)
            || !float.IsFinite(height) || height <= 0) return;
        Height = height;
        Sampled = true;
    }
    internal float ObserverY(float nativeEyeY, float hullY) =>
        float.IsFinite(nativeEyeY) && float.IsFinite(hullY) && float.IsFinite(hullY + Height)
            ? Math.Max(nativeEyeY, hullY + Height) : nativeEyeY;
    internal void Reset() { Height = 6f; Sampled = false; }
}
