namespace SprocketExpedition.Game;

// Pure identity/reward-roster decisions. The native observer provides valid
// entity keys, current control flags and crew readings from the owned battle.
internal static class CampaignPlayerControlPolicy
{
    internal static bool IsOwnDesign(string? expectedName, string? observedName, bool sameTeam,
        bool validEntity, int livingCrew, bool crewReadable) => sameTeam && validEntity && crewReadable
        && livingCrew > 0 && !string.IsNullOrWhiteSpace(expectedName)
        && string.Equals(expectedName, observedName, StringComparison.Ordinal);

    internal readonly record struct Ally(int EntityKey, bool SameTeam, bool AiControlled,
        bool PlayerControlled, int LivingCrew, bool CrewReadable);

    internal static int CountLivingAiAllies(IEnumerable<Ally> roster, int? controlledEntityKey)
    {
        var counted = new HashSet<int>();
        foreach (var tank in roster)
        {
            if (!tank.SameTeam || !tank.AiControlled || tank.PlayerControlled || !tank.CrewReadable
                || tank.LivingCrew <= 0 || tank.EntityKey == controlledEntityKey) continue;
            counted.Add(tank.EntityKey);
        }
        return counted.Count;
    }
}
