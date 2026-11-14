# Seven approved community additions — 26 October 2026

Imported with Sprocket closed, using the existing hash-guarded importer.
Receipt: `artifacts/import-community-20261026-130139/journal.json`.
Native spawning/part compatibility remains pending the user's gameplay test.

| Design | Requested era | Saved scalar price | Expedition copy |
| --- | --- | ---: | --- |
| Bmp-2D | Coldwar | 25,658 | Exact original bytes; date already in Coldwar |
| M40A1 «Ridgway» | Coldwar | 35,226 | Header date moved to native Coldwar start |
| NTZ - M.48 (Spatha) | Coldwar | 32,881 | Header date moved to native Coldwar start |
| Enforcer | Coldwar | 86,923 | Header date moved to native Coldwar start |
| Sdkfz 422 ``Daisy`` | Midwar | 26,030 | Header date moved to native Midwar start |
| ZZ-2 | Midwar | 69,874 | Exact original bytes; author reports cheats required |
| St.Pz.Kpf.Wg. VIAR-Škoda | WWI | 31,906 | Exact original bytes; era already correct |

The user explicitly assigned these eras, including BMP-2D as Coldwar. Current
installed native Era definitions supplied the start values for the four necessary
copy-only adjustments. Downloads originals were copied and hash-checked, never
edited or hardlinked. The only changed bytes in the four adapted copies are the
header creationDate value. Names, Unicode, component data, unknown mod settings,
mesh numbers, dimensions and saved costs remain intact. No global era dates,
playability, cheat settings, DLLs, audio, boss overrides or saves were changed.

All seven are modern vehicle-format blueprints within the size limit, with
positive saved prices/mass and cannon, cannon-instance, engine, transmission,
track and crew-seat components. The runtime stationary-name filter rejects none.
These checks do not prove engine physics, ammunition, crew operation or assembly
success. Saved scalar prices remain the native card metadata used by the pool;
they were not replaced with estimates.

ZZ-2 is 64.315 t with 120 mm and 75 mm cannon definitions. Its file does not state
which cheat is required. The user-supplied author warning is preserved in the
review manifest; no game setting or build-validation bypass is enabled. It is
included as requested, with native spawning specifically pending a user test.

## Preservation and verification

- Seven exact immutable imported hashes, seven original source hashes and all
  115 previously active community hashes verified after import.
- Active community markers: 122; reserve unchanged at 515; total 637 identities.
- 165 protected JSON/config/DLL/music-manifest files remained byte-identical.
- All eight slot/current-or-pre-encounter catalogues/offers verify read-only,
  including legacy slot 4. Existing runs retain their pinned content.
- Combined 147-design pool passes 1,536 cost-based round/seed requests, exact
  offer restoration and the existing next-era maximum-cost boss policy.
- Game root/executable ACLs remain unchanged; imported content inherits Medium
  integrity. The game was not launched, controlled or terminated by the agent.

Expected new-run totals before any additional veteran unlocks: WWI 23, Interwar 27,
Earlywar 25, Midwar 27, Latewar 25 and Coldwar 20. Native card acceptance is still
verified by the game's next catalogue build; these counts are not a live spawn
acceptance claim.

Frozen originals, reviewed copies, manifest, pre/post verification and the
combined read-only pool plan are in
the original development workspace (not part of this release).
Manifest SHA256: `2fea04847882a5dd218602aa570b422a6aeab6707721cbada77e302cf3fc2eb7`.

For a future deliberate recuration, the merged mandatory additions list is
`outputs/community-additions-2026-10-26/required-designs.json` under the continuation
workspace. It retains the earlier approved faction designs plus these seven;
the older historical required-designs/curation receipts are left untouched.

User check: start a new Expedition run in the relevant era and test the new
models, especially ZZ-2. Existing campaigns intentionally do not gain new IDs.
