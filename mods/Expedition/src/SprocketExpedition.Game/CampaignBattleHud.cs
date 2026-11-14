#if CAMPAIGN
using SprocketUiKit;
using UnityEngine;

namespace SprocketExpedition.Game;

// Uses the same native crew roster as the outcome observer. Never invents deaths
// from despawning/unreadable vehicles. Drawn as a non-interactive Sprocket UI Kit
// HUD: no pointer capture or input handling.
internal static class CampaignBattleHud
{
    private static HudPanel? hud;
    private static bool hudFailed;
    private static bool active;
    private static string roundLine = "";
    private static string countsLine = "";
    private static float nextRefresh;

    internal static void Tick()
    {
        var roster = CustomBattleHostSpike.CurrentRoster;
        if (!CampaignNativeFlow.OwnsSession || roster == null)
        { Release(); return; }
        if (Time.unscaledTime < nextRefresh) return;
        nextRefresh = Time.unscaledTime + 0.2f;
        try
        {
            if (!EnsureHud()) return;
            var counts = roster.Enemies < 0 ? "Enemies alive: checking… · Friendly tanks: checking…"
                : $"Enemies alive: {roster.Enemies} / {roster.ExpectedEnemies} · Friendly tanks: {roster.Friendlies} / {roster.ExpectedFriendlies} (incl. you)";
            var note = roster.Unknown > 0 ? $" · {roster.Unknown} vehicle(s) unverified" : "";
            var run = CampaignControllerHost.RunSnapshot;
            var battlesPerEra = run?.Rewards == null ? 3 : 4;
            var finiteRounds = run == null ? 0 : run.EraSchedule.Count * battlesPerEra;
            var round = run == null ? "EXPEDITION"
                : (run.BlockIndex >= run.EraSchedule.Count
                    ? $"ENDLESS · ROUND {run.Round}"
                    : $"ROUND {run.Round} / {finiteRounds}")
                + $" · {run.EraSchedule[run.EraIndex].NativeId}";
            if (run?.LockedPlan?.Extras?.BossCharacter is { } boss) round += " · BOSS: " + boss;
            // Battle HUD shows only round/era/boss and live roster counts. Spawn
            // status, hunt status, nearest-enemy distance and formation intent
            // remain available internally for diagnostics but are not revealed.
            roundLine = round;
            countsLine = counts + note;
            if (!active)
            {
                active = true;
                hud!.Show();
                if (Plugin.Detailed) Plugin.Trace("[Expedition HUD] battle counter shown.");
            }
        }
        catch (Exception ex)
        {
            Release();
            nextRefresh = Time.unscaledTime + 5f;
            Plugin.Logger.LogWarning("Expedition optional battle counter: " + ex.Message);
        }
    }

    private static bool EnsureHud()
    {
        if (hud != null) return true;
        if (hudFailed) return false;
        try
        {
            var created = UiKit.CreateHud("sprocket.expedition", "battle", 930)
                .Anchor(HudCorner.TopCentre, 0, 44);
            // Same visibility as before: whenever the campaign owns the battle roster and
            // the Expedition menu is closed, also while the cursor is free.
            created.AllowedContexts = UiContexts.All;
            created.HideWhenCursorFree = false;
            created.Visible(() => active && CampaignNativeFlow.OwnsSession
                && CustomBattleHostSpike.CurrentRoster != null && !CampaignNativeMenu.IsOpen);
            created.Body.Label(() => roundLine, TextStyle.Value).Centre().Outline();
            created.Body.Label(() => countsLine, TextStyle.Value).Centre().Outline();
            created.Hide();
            hud = created;
            return true;
        }
        catch (Exception ex)
        {
            hudFailed = true;
            Plugin.Logger.LogWarning("Expedition optional battle counter: UI Kit HUD unavailable. " + ex.Message);
            return false;
        }
    }

    internal static void Release()
    {
        if (!active) return;
        active = false;
        hud?.Hide();
        roundLine = countsLine = "";
        if (Plugin.Detailed) Plugin.Trace("[Expedition HUD] battle counter hidden.");
    }
}
#endif
