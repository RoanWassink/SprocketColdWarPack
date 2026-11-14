namespace SprocketExpedition.Core.Presentation;

// Presenter/controller boundary for Phase 4. Output-only view models; no Unity,
// BepInEx, native pointers, file paths or credentials cross this boundary.
// The UI host calls controller commands on the Unity main thread and marshals
// these presenter calls back to the main thread.
public sealed record RunRecordSummary(
    string RunId,
    RunPhase Phase,
    int Victories,
    int Defeats,
    EraReference? Era)
{
    public decimal Score { get; init; }
    public bool Ironman { get; init; }
    public bool TestPool { get; init; }
}

public sealed record StartView(
    bool HasActiveRun,
    bool CanResume,
    IReadOnlyList<RunRecordSummary> Records);

public sealed record BuildingView(
    RunPhase Phase,
    int Round,
    int BlockIndex,
    EraReference Era,
    ScenarioValue AvailableBudget,
    DesignQuote? Quote,
    int FriendlyCount,
    string? RejectionReason)
{
    public bool CanRetire { get; init; }
}

public sealed record OffersView(
    int Round,
    EraReference Era,
    ScenarioValue AvailableBudget,
    IReadOnlyList<EncounterPlan> Offers,
    string? SelectedOfferId)
{
    public bool CanRetire { get; init; }
    public string? EraBoss { get; init; }
    public IReadOnlyDictionary<string, decimal>? EnemyCosts { get; init; }
}

public sealed record EncounterView(
    AttemptIdentity Attempt,
    EncounterPlan Plan,
    int FriendlyCount,
    int EnemyCount,
    bool CombatStarted);

public sealed record ResultView(
    EncounterResult Result,
    RunPhase NextPhase,
    ScenarioValue? NextBudget,
    int PendingSupplyPoints,
    bool IsTerminal,
    bool IsLoss)
{
    public bool CanRetryLoss { get; init; }
    public int RoundAttempts { get; init; }
    public decimal? ScoreGained { get; init; }
    public string? ScoreBreakdown { get; init; }
}

public sealed record RunFinishedView(
    int Victories,
    int Defeats,
    RunPhase Phase,
    bool IsLoss);

public sealed record EndlessChoiceView(
    int CompletedRounds,
    ScenarioValue NextBudget,
    int PendingSupplyPoints);

public sealed record BlockedView(
    string Reason,
    bool CanRecover);

public sealed record ErrorView(
    string Message,
    bool CanRetry)
{
    public string? Title { get; init; } = "Unexpected error";
}

public sealed record ArmoryView(Run.RewardState Rewards, int Round, ScenarioValue Budget,
    RunPhase Phase, bool Recovery = false)
{
    public string? Notice { get; init; }
}

public interface IExpeditionPresenter
{
    void ShowArmory(ArmoryView view) { }
    void ShowStart(StartView view);
    void ShowBuilding(BuildingView view);
    void ShowOffers(OffersView view);
    void ShowEncounter(EncounterView view);
    void ShowResult(ResultView view);
    void ShowRunFinished(RunFinishedView view);
    void ShowEndlessChoice(EndlessChoiceView view);
    void ShowResumeBlocked(BlockedView view);
    void ShowError(ErrorView view);
}
