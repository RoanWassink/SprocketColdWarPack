# Music expansion - 24 October 2026

Builds on the freeze-fix campaign build installed at 20:18 (plugin 477FD52E...).
This increment only changes soundtrack phases and audio assets; Core is unchanged.

## Phase mapping

| Phase | Cue | Asset |
|---|---|---|
| Expedition menu / loading | Background | Mechanical Tension |
| Editor / build mode | Build | Minimal Focus Loop |
| Normal encounter | Combat | Heavy Industry |
| Boss encounter | BossBattle | The Final Blow |
| Victory | Victory | Triumph Complete |
| Defeat | Defeat | Skill Issue (replaces Mechanical Fadeout) |

## Implementation

- `CampaignMusicCue` gains `Build` and `Combat`; `CampaignMusicState.BeginPhase(cue)`
  and `BeginCombat(boss)` replace the old boolean boss flag.
- `CampaignNativeFlow`: New Game, opened saved designer and ReturnToDesigner select
  Build; battle loading selects Background; `CampaignGameAdapter` selects Combat or
  BossBattle at confirmed native combat start.
- Loop policy: Background/Build/Combat/BossBattle loop; Victory/Defeat stay one-shot.
  Victory returns to Background, defeat ends in Silence. Generation resets preserve
  the requested phase.
- Audio: the three new tracks were supplied by the user as MP3s on 24 October 2026
  and converted to stereo PCM16 44.1 kHz WAV without normalization, remixing or
  edits. Provenance is recorded in `assets/audio/MUSIC-NOTICE.md`; source and output
  hashes in `assets/audio/manifest.json`.
- `Mechanical Fadeout` is retired: moved to `assets/audio/retired/`, removed from the
  manifest and installer allow-list. The previously installed copy was removed with
  a backup during this install.
- The installer now validates six tracks plus the notice.

## Evidence

- Proof and campaign builds: 0 warnings, 0 errors.
- Managed suites: run 30/30, encounters 106/106, persistence 30/30, integration (see
  artifact log), presentation 19/19, campaign content (see log), player control 1/1,
  adventure 16/16. Music asset verification decodes all six real WAVs.
- Cue lifecycle checks cover background, build, combat, boss, victory, one-shot
  defeat, refit/return and reset.
- Installed with Sprocket closed and backup:
  `artifacts/install-campaign-20261024-204821/journal.json`.
  Plugin `B0561FD249B12FBBC11FF30823AD88FE09C0DAA5D52D1E65262DD57ED7E01D0C`;
  Core unchanged `F64D33901C33254607D66BB2D0158ED35061BF832059B59233622D3B3AAA790E`;
  music manifest `841CE9243D27763DA42C7E3A321C69857EE68A552B8D5A824827C994A6278A91`.
- Build/check evidence: `artifacts/music-expansion-20261024-204750/`.

## User checks (audible native behavior; the agent launched no game)

1. Editor/build mode plays Minimal Focus Loop.
2. A normal encounter starts Heavy Industry; a boss encounter keeps The Final Blow.
3. Defeat plays Skill Issue once and then goes silent; victory plays Triumph
   Complete and returns to build/background music.
4. Menu and loading keep Mechanical Tension; leaving the session restores the
   vanilla music state.
5. Volume follows `[Expedition Music] Volume` in `sprocket.expedition.cfg`.
