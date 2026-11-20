using System.Collections;

namespace SprocketThermalSight;

// Pure reader of the optional EB snapshot. Stock-list availability and confirmed chamber identity are separate.
public static class AmmoSnapshotReader
{
    public static string? Read(object? state, List<(string Code, int Count, bool Chambered)> rounds, out string status)
    {
        rounds.Clear(); status = "no-snapshot";
        if (state == null) return null;
        if (Get(state, "Frozen") is true) { status = "frozen"; return null; }
        bool confirmed = Get(state, "ChamberedConfirmed") is true;
        string? chambered = null;
        if (Get(state, "Rounds") is IEnumerable list)
            foreach (var round in list)
            {
                if (round == null) continue;
                string? name = Name(Get(round, "Label")) ?? Name(Get(round, "Code"));
                if (name == null || Get(round, "Rounds") is not int count || count < 0) continue;
                bool inGun = confirmed && Get(round, "Chambered") is true;
                rounds.Add((name, count, inGun));
                if (inGun) chambered = name;
            }
        status = confirmed ? "chamber-confirmed" : "chamber-unconfirmed";
        return confirmed ? chambered ?? Name(Get(state, "ChamberedLabel")) ?? Name(Get(state, "ChamberedCode")) : null;
    }
    private static object? Get(object source, string property) => source.GetType().GetProperty(property)?.GetValue(source);
    private static string? Name(object? value) => value is string s && !string.IsNullOrWhiteSpace(s) ? s.Trim().Replace('\r', ' ').Replace('\n', ' ') : null;
}
