using System.Numerics;

namespace SprocketExpedition.Game;

// Native WorldBounds dimensions in its own transform space. Terrain existing
// at a point is not evidence that the point belongs to the playable rectangle.
internal readonly record struct CampaignAreaFootprint(Vector2 Center, Vector2 Size)
{
    internal bool Valid => Finite(Center) && Finite(Size) && Size.X > 0 && Size.Y > 0;

    internal bool Contains(Vector2 localPoint, float margin)
    {
        if (!Valid || !Finite(localPoint) || !float.IsFinite(margin) || margin < 0) return false;
        var half = Size * .5f - new Vector2(margin);
        return half.X > 0 && half.Y > 0
            && MathF.Abs(localPoint.X - Center.X) <= half.X
            && MathF.Abs(localPoint.Y - Center.Y) <= half.Y;
    }

    private static bool Finite(Vector2 value) => float.IsFinite(value.X) && float.IsFinite(value.Y);
}
