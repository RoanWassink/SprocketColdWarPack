# Music audio-stop fix - 26 October 2026

Symptom: combat music stopped when many tanks spawned; the cue state stayed "playing" and no error
was logged. Likely cause: native engine/gun AudioSources saturate Unity real voices; the music
sources (default priority 128) are virtualised or stopped by the engine.

Fix in `src/SprocketExpedition.Game/CampaignMusic.cs` (only file changed): music sources use
`priority = 0` and bypass effects/listener effects/reverb zones; a throttled watchdog (2 s) reloads
a lost clip or resumes a stopped looping source and logs a warning.

Status: source patched; campaign build and all managed suites pass (see handover evidence). The
Core-free proof build currently fails on the unrelated in-progress `CampaignFormationLabels.cs`
Core reference. Nothing installed; include the fix in the next merged build.

Handover bundle: newest `artifacts/handover-music-audiofix-*` (before/patched files, optional git
patch, evidence logs, verification hints, integrity receipt).

Integrity: the new DSH session had lowered the Sprocket install root to Low again; repaired (root/exe Medium). Do not use the Sprocket install folder
as an agent/DSH workspace; restore the label before playing after any such session.
