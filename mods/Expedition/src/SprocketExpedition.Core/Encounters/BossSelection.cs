namespace SprocketExpedition.Core.Encounters;

// Shared by the real encounter and its preview; it never rolls offers or loot.
public static class BossSelection
{
    public const decimal Schema6EnemyFactor = 1.35m;
    public static EnemyDesignEntry Select(CatalogueDefinition definition, int eraIndex,
        ContentQualification qualification, long seed = 0, int round = 0, int rulesVersion = 6)
    {
        if (eraIndex < 0 || eraIndex >= definition.Eras.Count) throw new ArgumentOutOfRangeException(nameof(eraIndex));
        var current = definition.Eras[eraIndex];
        var next = definition.Eras[Math.Min(eraIndex + 1, definition.Eras.Count - 1)];
        var unfiltered = next.Enemies.Where(e => e.Qualification >= qualification).ToArray();
        var pool = unfiltered.Where(DangerousDesignPolicy.AllowsAsBoss).ToArray();
        // A minimum-risk design is never a boss. Only when the upcoming era had
        // candidates that were all excluded does the current era provide one.
        if (pool.Length == 0 && unfiltered.Length > 0)
            pool = current.Enemies.Where(e => e.Qualification >= qualification)
                .Where(DangerousDesignPolicy.AllowsAsBoss).ToArray();
        if (rulesVersion >= 7 && definition.BossCandidates?.TryGetValue(current.Era.NativeId, out var candidates) == true
            && candidates.Count > 1)
        {
            // A dedicated hash never consumes the offer/loot RNG or uses randomized string hashes.
            var key = FormattableString.Invariant($"boss-candidates/v1|{seed}|{eraIndex}|{current.Era.NativeId}|{round}");
            var digest = System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(key));
            var roll = System.Buffers.Binary.BinaryPrimitives.ReadUInt64LittleEndian(digest);
            var id = candidates[(int)(roll % (ulong)candidates.Count)];
            return pool.SingleOrDefault(e => e.Blueprint.LocalId == id)
                ?? throw new InvalidOperationException("The configured boss candidate is missing or has the wrong native era.");
        }
        if (definition.BossBlueprints?.TryGetValue(current.Era.NativeId, out var configured) == true)
            return pool.SingleOrDefault(e => e.Blueprint.LocalId == configured)
                ?? throw new InvalidOperationException("The configured boss is missing or has the wrong native era.");
        return pool.OrderBy(e => e.CheckedPrice.Value).ThenBy(e => e.Blueprint.LocalId, StringComparer.Ordinal).LastOrDefault()
            ?? throw new InvalidOperationException("No native placeholder boss exists in the upcoming era.");
    }
}
