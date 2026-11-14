namespace SprocketExpedition.Game.UI;

// Coordinator-owned, pure UI contract. Commands are dispatched by the native
// host on Update; this layer never touches scenes, input, storage or game objects.
internal enum ScreenCommand
{
    Resume, NewGame, ChooseOffer, ConfirmDesign, Continue, Retry,
    ContinueEndless, Finish, Slots, ReturnToMenu, ReturnToDesigner
    , ChooseAnotherBattle, Retire
}

internal sealed record ScreenAction(
    string Label, ScreenCommand Command, bool Enabled = true,
    string? Argument = null, bool Primary = false);

internal sealed record ScreenCard(
    string Title, IReadOnlyList<string> Lines, ScreenAction? Action = null);

internal sealed record ScreenModel(
    string Title, IReadOnlyList<string> Lines,
    IReadOnlyList<ScreenCard> Cards, IReadOnlyList<ScreenAction> Actions)
{
    // Optional structured variants (offers header, result banner); null when not used.
    public IReadOnlyList<ScreenFact>? Facts { get; init; }
    public string? Hint { get; init; }
    public ResultSummary? Result { get; init; }
    // 0.3.6: visible titled blocks on the (defeat) result screen.
    public IReadOnlyList<ResultBlock> Blocks { get; init; } = System.Array.Empty<ResultBlock>();
}

internal sealed record ScreenContext(
    bool Busy, bool OwnedSession, bool Editing, int Slot,
    string ActualMap, bool CanRetry = false);

// Structured, additive screen data (2026-10-31). Lines keep carrying the full text;
// a richer view (the Kit menu) may show these facts instead of the matching lines.
internal sealed record ScreenFact(string Text, bool Emphasis = false, string? Tooltip = null);

internal enum ResultTone { Victory, Defeat, Neutral }

internal sealed record ResultTile(string Caption, string Value);

// A titled block of plain lines on the result screen (0.3.6: Losses, Battle stats).
internal sealed record ResultBlock(string Title, IReadOnlyList<string> Lines);

// The Victory/Defeat banner and the numbers of one result. Details holds the
// lines that are not shown elsewhere on the result screen.
internal sealed record ResultSummary(
    ResultTone Tone, string Headline, string Subline, string? OutcomeText, string? Message,
    IReadOnlyList<ResultTile> Tiles, IReadOnlyList<string> Details);
