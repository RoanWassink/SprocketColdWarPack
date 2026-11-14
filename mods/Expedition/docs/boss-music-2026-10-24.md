# Boss soundtrack â€” 24 October 2026

The user supplied The Final Blow.mp3 and requested it for boss battles. The original
Downloads file is unchanged; the local mod asset is a 198.37-second stereo PCM16
WAV at 44100 Hz, without normalization or musical edits. Source/output hashes are
recorded in assets/audio/manifest.json; authorization/provenance is recorded in
assets/audio/MUSIC-NOTICE.md. No public release or external upload was performed.

Confirmed native combat start selects BossBattle when the locked encounter has a
BossBlueprintId (including existing boss runs). The Final Blow loops with the
existing soundtrack gain/crossfade controls. Victory and defeat interrupt it with
Triumph Complete and Mechanical Fadeout. Victory then returns to normal background;
defeat ends in silence. Editor/refit/loading and normal encounters use Mechanical
Tension. Reset/session cleanup clears boss selection; generation setup preserves
an explicitly requested boss phase. Failed precombat loads do not select boss music.

CampaignContent music tests cover boss selection, looping cue, single accepted
outcome, victory/background, defeat/silence, normal/refit and reset transitions.
All four actual WAV assets pass the mod's own PCM decoder. Existing content, AI,
archive and lifecycle tests pass. Campaign/proof builds: zero warnings/errors.
Audible native behavior remains a user test; no game process was launched.

The installer validates and backs up all four recordings, the notice and the
manifest. Installed with Sprocket closed:

- Receipt: artifacts/install-campaign-20261024-193953/journal.json
- Game SHA256: `02ADB85B954A80AE498EB8E7A37D30A3526D57E6C5F84A6C024E9906688CBCD6`
- Core SHA256 unchanged: `4C01CB3EE81B4536CD3986B12CC1DA67E74224EE0E9000DDA0627F3C54C3CD14`
- Music manifest SHA256: `51DEB1A702696F96BF748444A0234D9D75E0C6BC87A1F5EC7572887853304A5D`

Config preserved byte-for-byte; root ACL unchanged; executable still inherits Medium
integrity. Previous combat-role, return-guard and item fixes remain included.

User check: enter a boss battle and listen for The Final Blow when combat starts.
Confirm victory/defeat switches tracks and returning to the editor restores normal
background music.

## Amendment - 24 October evening

The soundtrack was extended after this build: build mode uses Minimal Focus Loop,
normal combat uses Heavy Industry and defeat uses Skill Issue; the boss track
(The Final Blow), background and victory cues are unchanged. See
[music expansion](music-expansion-2026-10-24.md) and
`artifacts/install-campaign-20261024-204821/journal.json`.
