using SprocketExpedition.Core.Presentation;
using SprocketExpedition.Core.Run;

namespace SprocketExpedition.Core.Integration;

public sealed partial class ExpeditionController
{
    private void PresentArmory(string? notice = null)
    {
        var snapshot = RequireEngine().Snapshot!;
        if (snapshot.Rewards == null) { _presenter.ShowError(new ErrorView("This legacy run uses the original rules; start a new run for items and bosses.", false)); return; }
        _presenter.ShowArmory(new ArmoryView(snapshot.Rewards, snapshot.Round, snapshot.AvailableBudget,
            snapshot.Phase, snapshot.Phase == RunPhase.AwaitingRecovery) { Notice = notice });
    }

    public Task ShowArmoryAsync(CancellationToken token = default) => RunExclusiveAsync(() =>
    { PresentArmory(); return Task.CompletedTask; }, token);

    public Task RewardCommandAsync(string command, string? id, CancellationToken token = default)
        => RunExclusiveAsync(async () =>
        {
            var engine = RequireEngine();
            var action = command switch
            {
                "activate" => engine.ActivateReward(id ?? ""),
                "cash" => engine.ActivateReward(id ?? "", true),
                "discard" => engine.DiscardReward(id ?? ""),
                "discard-active" => engine.DiscardActiveReward(id ?? ""),
                "claim" => engine.ClaimReward(id ?? ""),
                "skip" => engine.SkipRewardChoice(),
                _ => new RunActionResult(RunActionStatus.Rejected, "Unknown inventory action.")
            };
            if (!action.IsAccepted) { PresentArmory(action.Reason); return; }
            if (!await SaveAsync("reward inventory", token)) return;
            PresentArmory(engine.Snapshot?.SelectedOfferId != null
                ? "Selected challenge kept. Budget and ally bonuses update your fleet; enemy-budget changes apply to future offers." : null);
        }, token);

    public Task LeaveArmoryAsync(CancellationToken token = default) => RunExclusiveAsync(async () =>
    {
        var snapshot = RequireEngine().Snapshot!;
        if (snapshot.Rewards?.Choices.Count > 0 || snapshot.Phase == RunPhase.AwaitingRecovery)
        { PresentArmory(); return; }
        if (snapshot.Phase is RunPhase.Building or RunPhase.ChoosingEncounter)
        {
            if (snapshot.DesignConfirmedForBlock || RequireEngine().CanChooseEncounterBeforeRefit) await EnsureOffersAsync(token);
            else PresentBuilding();
        }
        else PresentCurrent();
    }, token);

    public Task UseRecoveryAsync(bool acceptDefeat = false, CancellationToken token = default)
        => RunExclusiveAsync(async () =>
        {
            var engine = RequireEngine();
            var result = acceptDefeat ? engine.AcceptPermanentLoss() : engine.UseRecovery();
            if (!result.IsAccepted) { _presenter.ShowError(new ErrorView(result.Reason, false)); return; }
            if (!await SaveAsync(acceptDefeat ? "accept defeat" : "consume emergency recovery", token)) return;
            if (!acceptDefeat) await _adapter.CleanupAsync(token);
            PresentCurrent();
        }, token);
}
