namespace SprocketExpedition.Game;

internal enum CampaignMusicCue { Silence, Background, Build, Combat, CombatWWI, CombatInterwar, CombatEarlywar, CombatMidwar, CombatLatewar, CombatColdwar, Victory, Defeat, BossBattle }

// Presentation only: never produces a gameplay outcome or writes a checkpoint.
internal sealed class CampaignMusicState
{
    private bool outcomeLatched;
    internal CampaignMusicCue Cue { get; private set; }
    internal void BeginPhase(CampaignMusicCue phase) { outcomeLatched = false; Cue = phase; }
    internal void BeginCombat(bool boss) => BeginPhase(boss ? CampaignMusicCue.BossBattle : CampaignMusicCue.Combat);
    internal void Outcome(bool victory)
    {
        if (outcomeLatched) return;
        outcomeLatched = true;
        Cue = victory ? CampaignMusicCue.Victory : CampaignMusicCue.Defeat;
    }
    internal void Finished()
    {
        if (Cue == CampaignMusicCue.Victory) Cue = CampaignMusicCue.Background;
        else if (Cue == CampaignMusicCue.Defeat) Cue = CampaignMusicCue.Silence;
    }
    internal void Reset() { outcomeLatched = false; Cue = CampaignMusicCue.Silence; }
}
