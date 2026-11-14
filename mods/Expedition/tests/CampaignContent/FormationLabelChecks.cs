using SprocketExpedition.Core.Encounters;
using SprocketExpedition.Game;

internal static class FormationLabelChecks
{
    internal static void Run()
    {
        static void Check(bool ok, string what) { if (!ok) throw new Exception(what); }

        Check(CampaignFormationLabels.Role("M48 Patton") == EnemyRole.Line,
            "MBT names remain centre designs");
        Check(CampaignFormationLabels.Role("M24 Chaffee") == EnemyRole.Recon,
            "M24 remains a light reconnaissance design");
        Check(CampaignFormationLabels.Role("Stuart") == EnemyRole.Recon,
            "Stuart remains a light reconnaissance design");
        Check(CampaignFormationLabels.Role("BT-7") == EnemyRole.Recon,
            "BT- prefixed light designs remain reconnaissance");
        Check(CampaignFormationLabels.Family("Leopard 2A7") == "Leopard",
            "known family names remain stable");
        Check(CampaignFormationLabels.Family("M48 Patton") == "M48 Patton",
            "unknown content keeps its exact family label");

        // Recognisable real designs are classified by name before mass.
        Check(CampaignFormationLabels.Role("AEC Mk2", 7_000d, "Midwar") == EnemyRole.Recon,
            "AEC Mk2 is a recon/armoured-car flanker by name");
        Check(CampaignFormationLabels.Role("AEC Mk II", 9_000d, "Latewar") == EnemyRole.Recon,
            "AEC Mk II spelling is recognised");
        Check(CampaignFormationLabels.Role("Tiger I", 3_000d, "Latewar") == EnemyRole.Heavy,
            "a recognised heavy name wins over a fictional light mass");
        Check(CampaignFormationLabels.Role("Sherman", 60_000d, "Latewar") == EnemyRole.Line,
            "a recognised medium design is not promoted to recon by mass");
        Check(CampaignFormationLabels.Role("M41 Walker Bulldog", 23_000d, "Earlywar") == EnemyRole.Recon,
            "M41 remains a light reconnaissance design");
        Check(CampaignFormationLabels.Role("M410 Fiction", 23_000d, "Earlywar") == EnemyRole.Line,
            "M41 token matching must not match the fictional M410");
        Check(CampaignFormationLabels.Role("Fictional A", 5_000d, "Midwar") == EnemyRole.Recon,
            "a fictional light design is classified by measured mass");
        Check(CampaignFormationLabels.Role("Fictional A", 25_000d, "Midwar") == EnemyRole.Line,
            "a fictional medium design remains centre Line");
        Check(CampaignFormationLabels.Role("Fictional A", 40_000d, "Midwar") == EnemyRole.Heavy,
            "a fictional heavy design is classified Heavy by era-aware mass");
        Check(CampaignFormationLabels.Role("Fictional A", 40_000d, "Earlywar") == EnemyRole.Heavy,
            "the early-war heavy threshold is authored and era-aware");
        Check(CampaignFormationLabels.Role("Fictional A", 14_000d, "WWI") == EnemyRole.Line,
            "WWI light threshold does not misclassify a real WWI medium");
        Check(CampaignFormationLabels.Role("Fictional A", null, "Latewar") == EnemyRole.Line,
            "missing mass stays neutral Line, never Recon");
        Check(CampaignFormationLabels.Role("Fictional A", -5_000d, "Latewar") == EnemyRole.Line,
            "negative mass is unknown and stays neutral");
        Check(CampaignFormationLabels.Role("Fictional A", double.NaN, "Latewar") == EnemyRole.Line,
            "nonfinite mass is unknown and stays neutral");
        Check(CampaignFormationLabels.Role("T-54", 36_000d, "Coldwar") == EnemyRole.Line,
            "T-54 is a known medium/MBT and must not be labelled Heavy by number matching");
        Check(CampaignFormationLabels.Role("T-55", 36_000d, "Coldwar") == EnemyRole.Line,
            "T-55 is a known medium/MBT Line design");
        Check(CampaignFormationLabels.Role("T-72", 41_000d, "Coldwar") == EnemyRole.Line,
            "T-72 is a modern MBT line design, not a fictional heavy");
        Check(CampaignFormationLabels.Role("M1 Abrams", 55_000d, "Coldwar") == EnemyRole.Line,
            "M1 Abrams name precedence keeps the MBT centre in the Line family");
        Check(CampaignFormationLabels.Role("T-10", 52_000d, "Coldwar") == EnemyRole.Heavy,
            "T-10 remains a recognised heavy tank");
        Check(CampaignFormationLabels.Role("Fictional Heavy", 55_000d, "Coldwar") == EnemyRole.Heavy,
            "fictional unknown content still uses the era-aware measured mass class");

        var overlong = new string('x', ContentSafety.MaxReferenceLength + 40);
        var safeFamily = CampaignFormationLabels.Family(overlong);
        Check(safeFamily.Length <= ContentSafety.MaxReferenceLength,
            "runtime family labels cannot exceed the catalogue validator bound");
        Check(!CampaignFormationLabels.Family("Tank\u0001Name").Any(char.IsControl),
            "runtime family labels cannot retain control characters");
        Check(CampaignFormationLabels.Family("\u0001\u0002").Length > 0,
            "a fully unsafe name still yields a non-empty local family");
        Console.WriteLine("PASS conservative formation roles, stable families and validator-safe family bounds");
    }
}
