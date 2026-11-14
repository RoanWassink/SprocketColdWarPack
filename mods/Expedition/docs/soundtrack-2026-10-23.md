# Expedition soundtrack — 23 October 2026

The user supplied three MP3s and states they hold rights through Suno for use in
the mod. Originals remain unchanged. Local stereo PCM16/44100 Hz WAV conversions
retain the whole recordings, with no level normalization or remixing:

| Track | Use | Duration |
| --- | --- | --- |
| Mechanical Tension | Expedition submenu, editor, loading and combat background loop | 157.57 s |
| Triumph Complete | Accepted victory, one playback | 34.89 s |
| Mechanical Fadeout | Accepted defeat, one playback | 65.20 s |

Triumph returns to background when finished. Fadeout finishes into silence.
Continuing into the designer or a new battle restores background. A duplicate
result cannot replay the cue; technical failure/cancellation is not a defeat cue.
The accepted encounter callback and existing session-generation check own results;
music has no authority over game outcomes, rewards or saves. As before, defeat
means the friendly formation is destroyed, including any surviving allies.

Playback is scoped to the Expedition submenu/session. Returning to ordinary game
modes stops owned audio and restores the prior mute state of native music sources.
Music uses two owned persistent 2D AudioSources and 0.8-second unscaled crossfades.
Native Music periodic players are muted during soundtrack ownership; effects and
ambience are left alone. Gain follows the native final Master/Music mix, multiplied
by the new config section `Expedition Music`, `Volume` (default 0.35, range 0–1).
`Enabled=false` restores ordinary music. Existing config values are preserved at
install; new config entries are added by BepInEx when the game next loads.

Only WAV reading/PCM decoding runs on a managed background task. Native uploads
and playback run on Update. The installed Unity interop signatures were inspected:
Construct_Internal/CreateUserSound_Injected/SetData_Injected, ManagedSpanWrapper,
AudioMix/GlobalAudioSettings and PeriodicAudioPlayer. The upload checks the span
layout and native sample metadata; clips retain HideAndDontSave across scenes.
Session cleanup stops/destroys owned sources/clips, restores music and discards
pending decode results. Optional audio errors stop the soundtrack and restore
native music without throwing into campaign Update.

Files are copied under the plugin's `Audio` directory. The installer validates the
reviewed manifest and each asset hash, backs up any replaced assets and verifies
the copied files. `MUSIC-NOTICE.md` records supplied-track provenance separately
from the code's MIT third-party notice. No game binaries, saves or personal tanks
are included in the local output package.

Validation: campaign and Core-free proof builds compile with zero warnings/errors.
CampaignContent passes cue lifecycle, duplicate-result suppression, bounded PCM16
format/truncation/channel rejection and stereo sample scaling, plus existing
catalogue/map/spawn geometry checks. Controller integration passes new run,
resume, victory, loss and technical retry. The test projects retain their existing
net6 support warning. Converted file formats and durations were inspected locally.
These checks do not establish audible native playback or perceived loudness.

Installed with Sprocket closed, backup and unchanged config:
`artifacts/install-campaign-20261023-212137/journal.json`.

- Game SHA256: `04AE908A4261DFABB4B088DC31978B5C0E14040E2488F5B588B92F4D8831CB3D`
- Core SHA256: `B22D7F532B3EBADC037CFBBC2C8683F062EB5A7AAD4E103497DC6B290268AC4A`
- Music manifest SHA256: `CAD9BDEC06FFD7F62368220A4BC7E3D730FC35CA289895B7E047DD05DABE2885`

User test pending: open Expedition, build/play, hear a win and a defeat, confirm
only the intended track plays, assess loudness, then return to the main menu and
check ordinary audio resumes. Codex does not launch or control the game.

User feedback after this build: "Muziek is prima". Soundtrack playback is user
accepted; this does not claim individual coverage of every listed lifecycle case.
