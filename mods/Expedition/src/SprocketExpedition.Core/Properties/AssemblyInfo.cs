using System.Runtime.CompilerServices;

// Focused pure tests exercise the internal thematic planner directly so the
// checked-price/role rules are pinned without widening the public runtime contract.
[assembly: InternalsVisibleTo("EncountersTests")]
