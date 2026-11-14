using SprocketExpedition.Core.Presentation;

namespace SprocketExpedition.Game;

// Minimal output-only presenter for the first controller-driven loop. The hub
// reads CurrentView on the Unity main thread and renders it; no native object or
// path crosses into the presenter contract. Reset suspends stale controller tasks
// after a scene return so they cannot overwrite the new slot view.
internal sealed class CampaignPresenter : IExpeditionPresenter
{
    private bool suspended;

    internal object? CurrentView { get; private set; }

    internal void Reset()
    {
        suspended = true;
        CurrentView = null;
    }

    internal void Activate() => suspended = false;

    public void ShowStart(StartView view) => Set(view);
    public void ShowArmory(ArmoryView view) => Set(view);
    public void ShowBuilding(BuildingView view) => Set(view);
    public void ShowOffers(OffersView view)
    {
        CampaignControllerHost.LogDangerousOnlyOffers(view);
        Set(view);
    }
    public void ShowEncounter(EncounterView view) => Set(view);
    public void ShowResult(ResultView view) => Set(view);
    public void ShowRunFinished(RunFinishedView view) => Set(view);
    public void ShowEndlessChoice(EndlessChoiceView view) => Set(view);
    public void ShowResumeBlocked(BlockedView view) => Set(view);
    public void ShowError(ErrorView view) => Set(view);

    private void Set(object view)
    {
        if (!suspended) CurrentView = view;
    }
}
