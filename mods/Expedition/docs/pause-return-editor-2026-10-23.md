# Native pause menu: Return to Editor — 23 October 2026

**Initial native integration rejected:** the user reported a crash immediately
on Escape, before clicking anything. The option-array/WaitForAccept detour below
is historical and has been removed. See [crash repair](pause-menu-crash-repair-2026-10-23.md)
for the current installed implementation. Core refit checks remain valid.

User requests a fair way to adjust an untested/non-working tank during an
Expedition encounter. The native pause menu now gets a `Return to Editor` option
while a live, unresolved Expedition battle is active. Native menu input, layout,
dismissal and unpausing remain native. No keybind or separate pause overlay is
introduced; unrelated modes and resolved result screens do not get this option.

The option captures the current attempt/session identity. Its callback requests
cancellation; Update waits until the active controller command and native adapter
have unwound before persisting a separate return-to-editing transition. The
existing pre-battle tank bytes travel back through the proven editor scene path.
Combat damage and temporary battlefield state are not exported as the design.
The round, era, budget, supply, scores, original offers and chosen offer remain.
There is no campaign outcome, reward, defeat or consumed offer. Native Play
recaptures/reprices the edited tank and starts the same stored encounter with a
fresh generation and fresh vehicles. Existing affordability and era checks remain.

RunDirector.ReturnToEditor validates the exact active attempt, clears its live
attempt/lock and returns to ChoosingEncounter without regenerating offers.
GenerationCounter is retained; the next Begin increments it. Late old-generation
results cannot resolve the replay. Save schema and field identities are unchanged;
the persisted refit checkpoint resumes into the existing selected-offer editor
flow. A resolved victory/loss cannot be undone with this action.

The controller rejects a result after cancellation even if an adapter returns
one anyway. The native outcome callback ignores cancelled attempts. During exit,
a no-op owner suppresses native result screens, observation stops, and native
mission cleanup is awaited through the existing return pipeline. Its native Fail
task is cleanup only; it is not delivered to the campaign or soundtrack as a
defeat. The guard restores on the next battle. The scene request is issued only
after the refit checkpoint is saved. Save failure reports an error without
claiming a successful return.

Inspected installed interop: Sprocket.Core.UI.PauseMenu.WaitForAccept(option array,
native cancellation token), PauseMenuOption(string,bool enabled,Func<result>),
HideMenuOnExecute, PauseMenuExecutionResult and the existing Mission.Fail task.
The pause hook has an optional independent Harmony owner; a hook failure logs and
does not disable the campaign. Callbacks do not perform scene/save operations.

Validation:

- Run: 30/30, including refit economics/selection, stale generation, edited tank,
  snapshot restore and rejection after actual loss.
- Integration: cancellation -> persisted refit -> reload -> edited design ->
  same encounter, with a deliberately late adapter victory suppressed; existing
  new-run/resume/victory/loss/technical retry checks pass.
- Persistence 30/30, encounters 106/106, presentation 18/18 and CampaignContent
  map/cover/music checks pass. Test projects retain the existing net6 warning.
- Campaign and Core-free proof builds: zero warnings/errors.

Installed with Sprocket closed, backup and preserved config/music:
`artifacts/install-campaign-20261023-220938/journal.json`.

- Game SHA256: `DA7014914B698760EB11D992AAF79F42CC4EE4AFB094CD67B9D4B2D5FF1104CD`
- Core SHA256: `5A2C1AB786479C538D8977FCAB5385F63F003C096F8C2D7B62E85208A76BE21C`
- Music manifest: `CAD9BDEC06FFD7F62368220A4BC7E3D730FC35CA289895B7E047DD05DABE2885`

Native acceptance is pending. User test: during an Expedition fight open the
native pause menu, select Return to Editor, verify the intact pre-battle tank and
same round/budget, change something and press Play. Confirm the same offer returns
with fresh vehicles and that pause/resume works on another visit. Music and the
three-map spawn/cover changes remain included. Codex does not launch/control the
game for this test.
