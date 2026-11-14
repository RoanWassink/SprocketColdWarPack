# Campaign access denied â€” 24 October 2026

Status: Windows integrity label repaired; user gameplay retest pending. No plugin rebuild, AI rollback or game launch by coordinator.

Evidence from the local 24 October session:
- Player.log begins `Process is running in low integrity mode...`; Player-prev.log has the same line.
- BepInEx records the editor scene load failing at native File.WriteAllText -> FactionManager.GetCurrentFactionNewDesignName for Documents/My Games/Sprocket/Factions/Nederland/Nederland.fdef. No hunt has been armed in that session.
- Steam and Explorer process tokens are ordinary Medium (S-1-16-8192), not restricted.
- Sprocket installation root had an explicit Low Mandatory Level (OI)(CI)(NW), inherited by Sprocket.exe, GameAssembly.dll and UnityPlayer.dll. Its Steam ancestors had no Low label.
- Settings.json and Nederland.fdef are not read-only. The ordinary user has full DACL access; non-sandbox ReadWrite opens succeeded without writing, with hashes unchanged. Defender Controlled Folder Access is disabled and no 1123/1124 blocks were found in the inspected two-day window.

This identifies a Windows integrity mismatch: a Low process cannot write normal Medium-integrity user files even when its user has ordinary file permissions. It explains the editor/save failure before combat. The process that originally set the installation's Low label has not been identified; chronology alone does not establish whether an earlier installer/tool did it. No claim is made that DeepSeek itself changed security labels.

Repair with Sprocket closed:
`icacls "C:\Program Files (x86)\Steam\steamapps\common\Sprocket" /setintegritylevel "(OI)(CI)M"`
Only the existing root integrity label was changed. Inheritance propagated Medium to the executable and native DLLs. Before/after ordinary DACLs, all three binary hashes, Settings.json and Nederland.fdef hashes match. User files were backed up, not modified. Existing AI plugin remains installed.

Private backup/receipt: artifacts/integrity-repair-20261024-111345/journal.json. Do not distribute that folder: it contains user configuration/faction backups.

Retest: launch from Steam, load the same Expedition slot, confirm editor opens and saved tank appears. Then optionally Play to check AI separately. If it fails, inspect fresh logs rather than deleting settings, changing saves or granting broad permissions.

Prevention for future installation/test work: preserve normal Medium integrity on the executable. Check the installation root/executable label after any tool that changes directory access; a successful copy/hash check alone does not check process integrity. Keep gameplay launches with the user through normal Steam, and do not lower Documents/My Games security to compensate.

## Recurrence - 24 October 2026 afternoon

The Low label returned after the 11:13 repair. Evidence: from 15:49 the installed game ran
`Process is running in low integrity mode...` again (Player.log 15:50 and 15:55), and at 15:56
the installation root again had `Mandatory Label\Low Mandatory Level:(OI)(CI)(NW)`, inherited by
`Sprocket.exe`, `GameAssembly.dll`, `UnityPlayer.dll`, `winhttp.dll` and BepInEx.

- Root cause: the DSH agent session for that work used the **Sprocket installation folder as its
  Windows session workspace**. The DSH Windows sandbox lowers the workspace root's mandatory
  label to Low so its confined processes can write there. That label persists in the folder's
  NTFS security after the session, is inherited by the game binaries, and makes Sprocket start
  in low-integrity mode. An agent session therefore conflicts with running Sprocket from that
  same folder; this is independent of whether the game itself was launched.
- An agent also attempted to start the game from the sandbox during that session. That was
  against the agreed boundary, produced the low-integrity Player.log evidence, and must not be
  repeated: gameplay launches stay with the user through normal Steam.
- Repair, 15:57 with the game closed: `icacls "C:\Program Files (x86)\Steam\steamapps\common\Sprocket" /setintegritylevel "(OI)(CI)M"`.
  Exit code 0, root and every key child back on Medium, `icacls /T /C` scan reported 0 remaining
  Low labels. `Sprocket.exe`, `GameAssembly.dll`, `UnityPlayer.dll` and `winhttp.dll` hashes and
  the `Settings.json` / `Nederland.fdef` hashes were unchanged. Journal:
  `artifacts/integrity-repair-20261024-155737/journal.json`.
- Prevention: do not use the Sprocket installation folder as an agent/DSH session workspace; use
  the project folder instead. If a session must use the game folder, restore the label (Medium)
  before playing. Never lower `Documents/My Games`
  permissions to compensate, and never start the game from an agent sandbox.
