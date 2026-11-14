namespace SprocketAIOrders;

// Pure order geometry; engine references stay in OrdersHost snapshots.
public static class OrderPolicy
{
    public static bool FollowNeedsMove(float distance, bool moving) => float.IsFinite(distance) && distance > (moving ? 10f : 20f);
    public static bool RefreshMove(float seconds, float shifted) => seconds >= 1f && shifted >= 8f;
    public static bool RetryEndedMove(float seconds, bool nativeMoving) => seconds >= 4f && !nativeMoving;
    public static (float X, float Z) BlendHeading(float x, float z, float wantedX, float wantedZ)
    {
        var length = MathF.Sqrt(wantedX * wantedX + wantedZ * wantedZ);
        if (!float.IsFinite(length) || length < .001f) return (x, z);
        wantedX /= length; wantedZ /= length;
        // Opposite vectors otherwise normalize back to the original direction forever.
        if (x * wantedX + z * wantedZ < -.5f) return (wantedX, wantedZ);
        var blendedX = x * .65f + wantedX * .35f;
        var blendedZ = z * .65f + wantedZ * .35f;
        var blendedLength = MathF.Sqrt(blendedX * blendedX + blendedZ * blendedZ);
        return blendedLength > .001f ? (blendedX / blendedLength, blendedZ / blendedLength) : (wantedX, wantedZ);
    }

    // Fallback follow slot behind the leader: column (one behind the other) or
    // wedge/line (rows of three). The slot is led by the leader's velocity so
    // followers aim where the leader will be instead of where it was.
    public static (float X, float Z) FollowSlot(float leaderX, float leaderZ, float forwardX, float forwardZ,
        float velocityX, float velocityZ, int slot, bool column)
    {
        var rightX = forwardZ; var rightZ = -forwardX;
        float back, side;
        if (column) { back = 30f + slot * 25f; side = 0f; }
        else { back = 30f + slot / 3 * 20f; side = (slot % 3 - 1) * 25f; }
        var lead = MathF.Min(MathF.Sqrt(velocityX * velocityX + velocityZ * velocityZ), 20f) * 1.5f;
        var speed = MathF.Sqrt(velocityX * velocityX + velocityZ * velocityZ);
        var leadX = speed > .5f ? velocityX / speed * lead : 0f;
        var leadZ = speed > .5f ? velocityZ / speed * lead : 0f;
        return (leaderX - forwardX * back + rightX * side + leadX, leaderZ - forwardZ * back + rightZ * side + leadZ);
    }

    // Destinations for a group move: a line abreast perpendicular to the
    // approach direction, centred on the clicked point.
    public static (float X, float Z)[] SpreadLine(float goalX, float goalZ, float fromX, float fromZ, int count, float spacing = 25f)
    {
        var result = new (float X, float Z)[Math.Max(0, count)];
        if (count <= 0) return result;
        var dx = goalX - fromX; var dz = goalZ - fromZ;
        var length = MathF.Sqrt(dx * dx + dz * dz);
        if (!float.IsFinite(length) || length < 1f) { dx = 0; dz = 1; length = 1; }
        dx /= length; dz /= length;
        var rightX = dz; var rightZ = -dx;
        for (var i = 0; i < count; i++)
        {
            var offset = (i - (count - 1) / 2f) * spacing;
            result[i] = (goalX + rightX * offset, goalZ + rightZ * offset);
        }
        return result;
    }

    public static (float X, float Z) Retreat(float x, float z, float threatX, float threatZ)
    {
        var dx = x - threatX; var dz = z - threatZ;
        var length = MathF.Sqrt(dx * dx + dz * dz);
        if (!float.IsFinite(length) || length < 1f) throw new InvalidOperationException("Threat direction is unavailable.");
        return (x + dx / length * 150f, z + dz / length * 150f);
    }
}

