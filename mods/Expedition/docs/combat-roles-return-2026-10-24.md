# Varied combat and menu-return trial — 24 October 2026

Follow-up: the return gate below blocked necessary native ResetGame cleanup and
could yield a null Task. This is corrected by the later
[SU-100Y/inventory/return fix](su100-inventory-return-2026-10-24.md), which also lets
stationary vehicles face their targets before the strict brake task.

## Combat behavior

Previously, ordinary units used Aggressive approach with a 280/340 m stop boundary.
More importantly, matching native binary inspection shows AttackOrder dispatch goes
directly to CommanderAI.Attack, bypassing GetMoveOrderCancellationToken. A waypoint
and attack can therefore remain active on separate native cancellation channels.
Logs show both tokens active while the driver still runs MoveToPositionTask.

The adapter now cancels its one-off movement token and clears the synchronous
native driver-task stack before installing an attack. Stationary firing uses Passive
approach, StopToFire and a native HaltTask at top priority, bound to the attack token.
Gun laying, ammunition, weapon accuracy and firing remain native. Losing contact
after 1.5 seconds cancels that attack/hold before new pursuit; native player control,
manual ally orders, controller fallback and session release relinquish owned holds.
No per-frame throttle, rigidbody override or vehicle teleport is used.

Ordinary AI mixes three stable profiles per encounter and slot:

| Role | Stops when a visible target is within | Resumes closing beyond |
| --- | --- | --- |
| Assault | 100–160 m | its stop range +60 m |
| Line | 230–300 m | its stop range +60 m |
| Fire Support | 600–700 m | its stop range +60 m |

Cheap enemy designs favor Assault; expensive ones favor Fire Support. Every price
band can receive any role. Price percentile uses the saved measured native prices
in the current era's pinned catalogue. A native enemy name must resolve to one
unambiguous price. Friendly, unmeasured or ambiguous hulls keep neutral reaction
skill; no live editor price is inferred from its design name.

A genuinely visible selected target starts one acquisition timer, rather than
rerolling or restarting every scan. Reactions span 1–5 seconds: cheapest designs
4–5, most expensive 1–2, neutral profiles 2.5–3.5. Identified bosses use 1–2 seconds
and retain their character engagement ranges and bounded Battery retreat. Scans
run every 0.25 seconds; pausing does not advance this game-time timer. Short detector
flicker retains acquisition; a changed target or longer loss restarts it. Profile
variation is deterministic for encounter/slot/team, so retrying does not reroll it.

Private live-player boss pursuit remains active without entering either team's
sighting memory. A boss prioritizes a genuinely visible player but can engage a
visible ally while the player is hidden, rather than ignoring that immediate threat.
Without a visible opponent it follows its private player objective as before.

## Return-to-menu crash investigation

The 18:50 Windows event confirms Sprocket crashed with `0xc0000005` in `coreclr.dll`.
The game log ends after requesting Fields → MainMenu and entering LoadingScreen.
Only Report.wer remains in the Windows archive; no crash stack/dump was available.
This establishes the phase/module, not the exact native faulting cause.

Review found result ownership was released immediately after requesting a scene
change, while old missions/game-controller cleanup could still send callbacks.
A return gate now coalesces requests, stops the outcome observer and suppresses
late result/reset callbacks for that exact old mode until the main menu has started
and its old GameController is gone. Editor/battle transfers wait for that gate
before creating the next game controller. Native mission completion is still polled
through its actual task. No scene unload is faked or forced. A failed scene request
releases the gate to permit another attempt/native exit.

The owned pause button explicitly removes its managed UnityAction listener before
the native button is destroyed and the delegate root is released. EndSession does
this while the old UI is still alive. Closed campaign menus clear retained click
actions. These address concrete lifecycle risks; native crash resolution still
requires a user reproduction test.

## Evidence and installation

- CampaignContent checks pass: three roles, reproducibility/order independence,
  measured-price reactions, neutral/small-pool cases, hysteresis, acquisition and
  lost/changed targets, boss timing, return coalescing and native-unload gate lifecycle.
  Existing spawn, map bounds, archive, boss-tracking, audio and veteran checks pass.
- All seven live current/pre-encounter checkpoints and pinned catalogues were
  verified read-only. Slot 1 is schema 2, round 11; no recovery/save rewrite needed.
- Campaign and proof builds: zero warnings/errors. Native signatures and the
  matching GameAssembly hash were inspected locally. Native materials were not
  sent externally or included in a package. No game launch/termination by Codex.
- Installed closed with DLL/config/audio backups and hash verification:
  `artifacts/install-campaign-20261024-190249/journal.json`.
- Game SHA256: `4641E6DD4766B83C47977CE32257FC37938BABA3160F89DDCD006830E1807FA4`.
- Core SHA256 unchanged: `4C01CB3EE81B4536CD3986B12CC1DA67E74224EE0E9000DDA0627F3C54C3CD14`.
- Config preserved byte-for-byte; root ACL unchanged; Sprocket.exe remains inherited
  Medium. Build/check/ACL evidence: `artifacts/combat-roles-*.txt`; slot evidence:
  `artifacts/combat-return-slot-check.txt`. Local crash evidence is private in
  `artifacts/return-crash-20261024-185058/`.

User checks: watch one normal battle for early stationary fire support alongside
units that close further before stopping; break sight and confirm pursuit resumes.
After victory use Return to Menu, then resume. Repeat editor → battle → menu once
to check both native unload and the next encounter still work.
