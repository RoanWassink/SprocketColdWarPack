namespace SprocketFloppyParts;

internal static class GameBuildGateCore
{
    internal const string KnownGameAssemblySha256 =
        "77FC601748991DC2101435C6B72C7A5A4B3DC3C68429B349C2EFEE711F6D5388";

    internal static (bool Allowed, string Reason) Evaluate(string? sha256, bool trustUnknown)
    {
        if (!string.IsNullOrWhiteSpace(sha256) &&
            string.Equals(sha256.Trim(), KnownGameAssemblySha256, StringComparison.OrdinalIgnoreCase))
        {
            return (true, $"Verified GameAssembly {sha256.Trim()[..8]}.");
        }

        if (trustUnknown)
        {
            string tag = string.IsNullOrWhiteSpace(sha256) ? "unknown" : sha256.Trim()[..Math.Min(8, sha256.Trim().Length)];
            return (true, $"Unknown GameAssembly ({tag}); trusted via Compatibility.TrustUnknownGameBuild=true.");
        }

        return (false, $"Unverified GameAssembly ({(string.IsNullOrWhiteSpace(sha256) ? "unreadable" : sha256.Trim())}); hooks disabled.");
    }
}
