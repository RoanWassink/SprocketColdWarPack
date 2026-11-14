# Escape crash: pause integration replacement — 23 October 2026

The user reports a crash as soon as Escape opens the pause menu. BepInEx's last
line is the new option-added message after PauseMenu scene load, before a refit
click. Player.log provides no useful crash stack. This rejects the first native
integration; the exact native fault instruction is not established.

The entire WaitForAccept Harmony detour, byref option-array replacement and native
Func<PauseMenuExecutionResult> bridge are removed. There is no optional pause
Harmony owner installed now. Native pause options, button arrays and async task
arguments are untouched.

Update observes an already-open native PauseMenu only while refit is allowed.
After original buttons exist, it clones the native Tab prefab under native
content. Its own UnityEvent has a single UnityAction; native Clicked subscriptions
are not cloned from a running button. LayoutGroup handles placement when present;
otherwise the added row sits below native buttons and the temporary content size
restores on cleanup. The original menu's native Resume button dismisses/unpauses
the game normally after the refit request is accepted. The owned visual Tab is
not included in the native WaitForAny/options machinery. Unknown resume labels
or managed UI errors log and leave the native menu alone.

The Core refit transition and cancellation/late-result guards are unchanged:
original pre-battle tank, selected offer, round and budget survive without an
outcome/reward. The previous Core run/integration/persistence tests remain valid;
controller integration was rerun and passes. Campaign and proof builds compile
with zero warnings/errors. Source inspection confirms no remaining WaitForAccept
patch or CampaignPauseMenu.Install call. These checks do not establish native
pause stability or clickable layout; the user performs that acceptance.

Installed with Sprocket closed and backup:
`artifacts/install-campaign-20261023-221546/journal.json`.

- Game SHA256: `CFB4FE4E4EA5BA4F5F43D3E128EC61C3C94BF4BB043911E5EB9755DCB01E55A3`
- Core SHA256: `5A2C1AB786479C538D8977FCAB5385F63F003C096F8C2D7B62E85208A76BE21C`
- Music manifest: `CAD9BDEC06FFD7F62368220A4BC7E3D730FC35CA289895B7E047DD05DABE2885`

The immediately preceding backup is the rejected pause build; preserve it as
diagnostic evidence, not an accepted rollback target. Earlier map-cover and music
packages are retained separately. No saves or personal tank files were rewritten
for this repair. A run interrupted by the crash can use the existing explicit
Load Game/retry flow.

User test: first open Escape and use native Resume; repeat once. If stable, open
Escape again and try Return to Editor, adjust the tank and press Play. Report a
missing button or scene-return failure separately from Escape stability. Codex
does not launch/control the game.
