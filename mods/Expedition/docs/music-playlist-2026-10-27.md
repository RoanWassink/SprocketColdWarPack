# Music playlist within a phase - 27 October 2026

Status: implemented in `src/SprocketExpedition.Game/CampaignMusic.cs`, built, tested and installed
with Sprocket closed. It is the only source difference against the installed round38 build.

## Behaviour

- New config key `[Expedition Music] Playlist` (default `true`). The selected track no longer loops
  for the whole phase: when it ends naturally the next configured track for that phase is loaded
  with the existing 0.8 s crossfade. When the list is exhausted the rotation continues with a fresh
  start; a one-track list replays that track.
- Applies to Background, Build, every combat/era pool and boss cues. Victory and defeat stay
  one-shot with their existing finish/silence logic.
- `Playlist = false` restores the old repeat-selected-track behaviour. BepInEx creates the key on
  the next game start; the installer preserved the existing config.
- Natural end versus engine-stopped source is distinguished by the recorded clip end time. Engine
  stops still use the 2 s watchdog from the 26 October audio fix (priority 0, clip reload, resume).
- Log line for an advance: `Expedition music: track finished; continuing the <cue> playlist.`

## Evidence

- Proof and campaign builds: 0 warnings, 0 errors.
- Suites: 33/33 tests passed, 139/139 tests passed, 35/35 tests passed, 6 integration scenarios, 20 passed, 0 failed, 20 total, 23 campaign
  content groups, 1 player-control, 19/19 adventure scenarios passed, 6 music assets -> 0 failures.
- Installed with Sprocket closed: `artifacts/install-campaign-20261027-204001/journal.json`.
  Game `BE7DE85CBBA90AB030D9AD5BE98E7F8A9BECFC17AF7D2F009016973E5424A4B9`; Core `70E3420E624B4C822FA9DE8965574A06BC5703EC7E3F556D8892068602955FD0`; music manifest unchanged
  `841CE9243D27763DA42C7E3A321C69857EE68A552B8D5A824827C994A6278A91`.
- Config preserved byte-for-byte (`2A1BEE46A0F05366BB747C3F68FAC17F4853CA3B8F5B3D574BE002D5D2C06574`); root and executable remain Medium integrity.
- Build/test evidence: `artifacts/music-playlist-20261027-203423/`.

## Verification (user, audible)

Play a battle whose era pool has several tracks; across a track boundary the next track should start
instead of the same one repeating. Check `LogOutput.log` for
`Expedition music: track finished; continuing the <cue> playlist.` Boss has a single built-in track
and will replay it. `Playlist = false` in `[Expedition Music]` disables the behaviour.
