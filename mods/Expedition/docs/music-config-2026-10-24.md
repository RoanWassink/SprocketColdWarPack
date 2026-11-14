# Configurable music and rotation - 24 October 2026

Adds a configuration layer over the six soundtrack phases so the user can add or
replace music without rebuilding. Game DLL only; Core unchanged.

## Configuration

`[Expedition Music]` in `sprocket.expedition.cfg` now has one key per
phase: `Background`, `Build`, `Combat`, `Boss`, `Victory`, `Defeat`. Each value is a
comma/semicolon separated list of WAV files relative to the plugin `Audio` folder,
for example:

```
[Expedition Music]
Background = Mechanical Tension.wav
Build = Minimal Focus Loop.wav, Local\Build Alternate.wav
Combat = Heavy Industry.wav, Local\Combat A.wav, Local\Combat B.wav
Boss = The Final Blow.wav
Victory = Triumph Complete.wav
Defeat = Skill Issue.wav, Local\Defeat Alternate.wav
```

- Empty value or a value with no usable entries falls back to the built-in track.
- Entries keep their configured order; duplicates (case-insensitive) are dropped.
- Absolute paths, `..` traversal and non-`.wav` names are rejected with a log warning.
- Missing or unreadable files are skipped with a log warning; the next configured
  entry and finally the built-in track act as fallbacks.
- File names are resolved under the plugin `Audio` folder (for example
  `BepInEx/plugins/SprocketExpedition/Audio/Local/...`). The resolver
  proves the final path stays inside that folder.
- No config reload is attempted while the game runs; restart the game after editing.

## Rotation

Selection is deterministic per session seed: the first track per phase depends on the
session, later selections advance through the configured list, cycle through every
track and never repeat the immediately previous one. Picks happen per phase entry:
each returning refit for Build, each normal encounter for Combat, each boss fight for
Boss, each accepted outcome for Victory/Defeat and each session start for Background.
A single configured track stays selected.

## Runtime behaviour

- Example WAV requirements are enforced by the existing bounded decoder: RIFF PCM16,
  mono or stereo, 8000-96000 Hz (44100 Hz recommended), at most 64 MB per file.
- Decoding still happens on a background task; playback, upload and crossfades stay
  on Update. A newly used track can add a short decode delay before it fades in.
- Only the playing and outgoing clips are kept after a crossfade; other decoded clips
  are released, so a larger configured library does not accumulate in memory.
- One bad or missing custom file no longer disables the whole soundtrack for the
  session; it is skipped and the next candidate is tried.
- Installer behaviour is unchanged: it still validates and reinstalls only the six
  built-in recordings, the notice and the manifest. Custom files under `Audio/Local`
  are user-owned and are neither validated nor overwritten by the installer.

## Evidence

- Proof and campaign builds: 0 warnings, 0 errors.
- Managed suites remain green: run 30/30, encounters 106/106, persistence 30/30,
  integration 6, presentation 19/19, campaign content 14, player control 1/1,
  adventure 16/16; all six real WAV assets still decode.
- New pure checks cover default coverage, list parsing/ordering/deduplication,
  invalid-entry reporting, empty/invalid fallback, path safety and deterministic
  rotation (coverage, no immediate repeat, cycling, single-track and empty cases).
- Installed with Sprocket closed and backup:
  `artifacts/install-campaign-20261024-205541/journal.json`.
  Plugin `773278871569E1411C6FEE637EEFD396293547D88103CD63F232026AC79F5826`;
  Core unchanged `F64D33901C33254607D66BB2D0158ED35061BF832059B59233622D3B3AAA790E`;
  music manifest unchanged `841CE9243D27763DA42C7E3A321C69857EE68A552B8D5A824827C994A6278A91`.
- Build/check evidence: `artifacts/music-config-20261024-205432/`.

## User checks

1. Start the game once so BepInEx writes the six new `[Expedition Music]` keys.
2. Copy one or two test WAVs into the plugin `Audio/Local` folder, list them for a
   phase and restart. The phase should use them in rotation and fall back cleanly
   when a name is removed.
3. Confirm an invalid entry produces a log warning and does not silence the game.
4. Confirm the installer still installs only the built-in soundtrack and leaves
   `Audio/Local` untouched.

## Per-era combat pools - 24 October evening

Combat music can also be set per native era. New keys in `[Expedition Music]`:

```
CombatWWI =
CombatInterwar =
CombatEarlywar =
CombatMidwar = Local\Hostile Takeover.wav
CombatLatewar = Local\Hostile Takeover.wav
CombatColdwar = Local\Hostile Takeover.wav
```

- The locked encounter's native era selects the matching cue (`WWI`, `Interwar`,
  `Earlywar`, `Midwar`, `Latewar`, `Coldwar`; case-insensitive).
- An empty era key inherits the shared `Combat` list. An era key with only invalid
  entries also falls back to the shared list, and finally to the built-in Heavy
  Industry track.
- Each era pool rotates on its own cursor, so early and late eras keep independent
  sequences. Boss, build, background, victory and defeat are unchanged.
- Implementation: `CampaignMusicCue` gains the six combat cues,
  `CampaignMusicOptions.CombatCueForEra`/`CombatTracks`/`Loops` hold the pure policy,
  `CampaignGameAdapter` passes `request.Plan.Era.NativeId` into
  `CampaignMusic.BeginCombat`.
- Checks: era-name mapping (case-insensitive), unknown/missing era fallback, empty era
  pool inheriting Combat, era pool winning over Combat, invalid era pool falling back,
  and the loop policy for every combat cue.
- Installed with Sprocket closed and backup:
  `artifacts/install-campaign-20261024-212556/journal.json`.
  Plugin `33F68C01CD4605185CEB2884981A032F06A4A284C7A666DF88CC905A723DEAC5`;
  Core unchanged `F64D33901C33254607D66BB2D0158ED35061BF832059B59233622D3B3AAA790E`;
  music manifest unchanged `841CE9243D27763DA42C7E3A321C69857EE68A552B8D5A824827C994A6278A91`.
- Build/check evidence: `artifacts/era-music-20261024-211839/`.
