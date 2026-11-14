# Preferred Oplot replacement — 26 October 2026

Status: applied with Sprocket closed. Receipt:
`artifacts/oplot-replacement-20261026-142104/journal.json`.
Native gameplay validation of the preferred version is still pending.

The user selected `T-84 Oplot-M Duplet` as the best version and explicitly
confirmed it replaces both active older models: `T-84 Oplot-S` and
`Sprocket Oplot-rep`.

Preferred source: Nederland/Blueprints/Vehicles/T-84 Oplot-M Duplet.blueprint.
Exact final SHA256: `e468274eb0cd364770e80003df224353912fdbb34e322011820546fb831350c2`.
The file changed during the user's editor session. After normal closure, the
latest saved bytes were reread, frozen and rechecked. The earlier prepared
version and plan are retained in a previous-review folder; they were not imported.
Native modern vehicle format, Coldwar date, saved scalar cost 76,771 and mass
56.426 t. Engine, transmission, tracks, cannon, cannon instance and crew seats
are present. No source bytes, dates, component data, price or personal file are
rewritten. Native spawning/weapon/ERA behavior is not established by this review.

## Concrete replacement

- Import one immutable community copy of the preferred version.
- Reserve the old T-84 Oplot-S community marker, preserving its immutable blob.
- Reserve both Expedition Spawns root copies named Sprocket Oplot-rep: the named
  blueprint and Autosave.blueprint. Both are scanned by the native candidate
  loader, so leaving either root copy could reintroduce the older model.
- The old faction copies move into the continuation workspace's
  `outputs/oplot-duplet-replacement-2026-10-26/reserved-faction-blueprints/`.
  No Nederland file or profile is moved. No matching native stock or veteran
  entry was found, so no stock or veteran change is necessary.
- Existing pinned campaign/catalogue content stays unchanged. This replacement
  affects new runs only. No DLL/music/config/cheat/boss override is changed.

The guarded application script validates absolute parent directories, exact
allowed filenames, every old/new source hash, destination collisions, reparse
points, pool snapshots and game closure. It journals every individual move; no
recursive move/delete is used. Source and destination copies remain recoverable.

## Verification

The 146-design combined pool passes 1,536 cost-based round/seed requests, exact
offer restore and the existing next-era maximum-cost boss policy. The runtime
stationary-name filter accepts the preferred design. The application dry run
validates all hashes/paths and performs no mutation. Gameplay remains untested.

Expected new-run totals before further veteran unlocks: WWI 23, Interwar 27,
Earlywar 25, Midwar 27, Latewar 25, Coldwar 19. Replacing two different designs
with one reduces the Coldwar count by one, as the user explicitly requested.

Reviewed copies, replacement plan, before-state hashes, combined verification
and the updated merged mandatory-designs list live under the continuation
workspace `outputs/oplot-duplet-replacement-2026-10-26/`.
Plan SHA256: `41098a80431ebc8c2294e3a566b4e587001cc6f70e89a6fb37bd903d844edae9`.
Manifest SHA256: `177e6d1378896c45184d0b66081b6691328f583f7e214d938fefa20433690a45`.

Post-application checks confirm zero older-model entries in the scanned community
and Expedition Spawns roots, all three retired copies preserved, 122 active
community markers and 516 reserved markers. The other 121 active community hashes
and all 184 protected personal/config/save/DLL/music files remain unchanged.
All eight current/pre-encounter slot catalogues/offers verify, including legacy
slot 4. Root/executable ACLs are unchanged and imported content has Medium
integrity. The agent did not launch, control or terminate Sprocket.

Use this folder's `required-designs.json` for future deliberate recuration; it
retains all previous explicit additions, replaces the retired Oplot-S requirement
and adds Duplet. Historical import/curation receipts remain untouched.
