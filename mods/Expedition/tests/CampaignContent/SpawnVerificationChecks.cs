using System.Numerics;
using SprocketExpedition.Game;

internal static class SpawnVerificationChecks
{
    internal static void Run()
    {
        static void Check(bool ok, string message) { if (!ok) throw new Exception(message); }
        var roles = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["Leopard"] = "Center",
            ["Light"] = "Flank"
        };
        var planned = new[]
        {
            new SpawnPlanSlot(new Vector2(10, 20), "Leopard", "Center"),
            new SpawnPlanSlot(new Vector2(30, 40), "Light", "Flank")
        };
        var observed = new[]
        {
            new SpawnObservation(new Vector2(12, 22), "Leopard"),
            new SpawnObservation(new Vector2(31, 39), "Light")
        };

        Check(CampaignSpawnVerification.Verify(planned, observed, roles, out var error),
            "matching position/design/role passes: " + error);
        Check(!CampaignSpawnVerification.Verify(planned,
                new[] { observed[0], new SpawnObservation(new Vector2(31, 39), "Leopard") },
                roles, out error) && error.Contains("expected design", StringComparison.Ordinal),
            "wrong design at the planned location fails");
        Check(!CampaignSpawnVerification.Verify(planned,
                new[] { observed[0] }, roles, out error)
            && error.Contains("expected 2 spawned enemies", StringComparison.Ordinal),
            "count mismatch fails");
        Check(!CampaignSpawnVerification.Verify(planned,
                new[] { observed[0], new SpawnObservation(new Vector2(31, 39), "unknown") },
                roles, out error) && error.Contains("no readable spawned design", StringComparison.Ordinal),
            "unknown spawned identity fails");
        Check(!CampaignSpawnVerification.Verify(new[]
                {
                    new SpawnPlanSlot(new Vector2(10, 20), "Leopard", "Center"),
                    new SpawnPlanSlot(new Vector2(30, 40), "Light", "Center")
                }, observed, roles, out error)
            && error.Contains("expected role", StringComparison.Ordinal),
            "role mismatch fails even when positions and designs match");
        Check(!CampaignSpawnVerification.Verify(planned,
                new[] { new SpawnObservation(new Vector2(80, 20), "Leopard"), observed[1] },
                roles, out error) && error.Contains("from the planned marker", StringComparison.Ordinal),
            "a moved/incorrect spawn position fails");
        Check(!CampaignSpawnVerification.Verify(Array.Empty<SpawnPlanSlot>(), observed,
                roles, out error), "no planned verification data cannot pass");
        Console.WriteLine("PASS actual spawn verification: exact match, wrong design, count, unknown identity, role mismatch, position and absent-plan rejection");
    }
}
