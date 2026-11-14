using SprocketExpedition.Game;
using Ally = SprocketExpedition.Game.CampaignPlayerControlPolicy.Ally;

static void Check(bool value, string message) { if (!value) throw new Exception(message); }
const string ownName = "NTL light tank";
Check(CampaignPlayerControlPolicy.IsOwnDesign(ownName, ownName, true, true, 2, true),
    "the alive own design on the player team qualifies for initial control");
Check(!CampaignPlayerControlPolicy.IsOwnDesign(ownName, "Expedition support 0123abcd | " + ownName, true, true, 2, true)
    && !CampaignPlayerControlPolicy.IsOwnDesign(ownName, ownName, false, true, 2, true)
    && !CampaignPlayerControlPolicy.IsOwnDesign(ownName, ownName, true, false, 2, true)
    && !CampaignPlayerControlPolicy.IsOwnDesign(ownName, ownName, true, true, 0, true)
    && !CampaignPlayerControlPolicy.IsOwnDesign(ownName, ownName, true, true, 2, false),
    "support aliases, enemy hulls, invalid native entities, dead crew and unreadable crew cannot qualify");
Check(!CampaignPlayerControlPolicy.IsOwnDesign(ownName, ownName.ToUpperInvariant(), true, true, 2, true)
    && !CampaignPlayerControlPolicy.IsOwnDesign(ownName, null, true, true, 2, true)
    && !CampaignPlayerControlPolicy.IsOwnDesign("", ownName, true, true, 2, true),
    "initial control uses exact native names without fuzzy fallback");
var roster = new[] {
    new Ally(1, true, false, true, 3, true),  // current player
    new Ally(2, true, true, false, 3, true),  // own design AI copy
    new Ally(3, true, true, false, 2, true),  // borrowed/captured support
    new Ally(4, false, true, false, 3, true), // enemy
    new Ally(5, true, true, false, 0, true),  // confirmed dead
    new Ally(6, true, true, false, 2, false), // unreadable: no manufactured bonus
    new Ally(3, true, true, false, 2, true),  // duplicate native spawn reference
    new Ally(7, true, true, false, 2, true)   // controller still points here during flag transition
};
Check(CampaignPlayerControlPolicy.CountLivingAiAllies(roster, 7) == 2,
    "victory counts living AI allies once and excludes current player/controller, dead, unknown and enemy entities");
var afterManualSwitch = new[] {
    new Ally(1, true, true, false, 3, true),
    new Ally(2, true, true, false, 3, true),
    new Ally(3, true, true, false, 2, true)
};
Check(CampaignPlayerControlPolicy.CountLivingAiAllies(afterManualSwitch, 3) == 2,
    "after manual switching the actual controlled ally is excluded and the former own hull can count as AI");
Check(CampaignPlayerControlPolicy.CountLivingAiAllies(Array.Empty<Ally>(), null) == 0,
    "missing battle roster cannot create a Veteran Crew reward");
Console.WriteLine("PASS initial own-design identity and living AI ally roster: support exclusion, native entity validity, player handoff, crew uncertainty and deduplication");
