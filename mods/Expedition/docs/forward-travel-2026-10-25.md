# Forward travel trial — 25 October 2026

The user observed a PZ4 veteran driving backwards. The captured Dunes log identifies
an ordinary enemy `PZ4`, with a 477 m advance, a 44 m recovery leg, another advance
and finally a stationary attack/FaceTargetTask at about 173 m. It does not record
velocity or heading, so the exact moment and cause are unproven. No Battery retreat
was issued for this unit. Its exact veteran snapshot identity is not established.

Local matching-binary inspection confirms DriverAI.Step supports automatic reverse
through reverseAllowed, separately from explicit ForceReverse flags. The verified
signature is `(float, DriverUpdateFlags)`; native value-type reference arguments
are not patched. GameAssembly SHA256 remains
`3E58082804D97CF32FFD400E7427E767EF9576D23DB71B2BE96FBB7F825FC17B`.

This trial disables automatic reverse only inside a native Step when the current
Expedition owns the driver, it has not yielded to manual/player control, and an
active, unforced MoveToPositionTask matches the owned Advance/Search/Recover goal
more than 18 horizontal metres away. The movement cancellation token must be live.
A Harmony finalizer restores the original field after successful and exceptional
Steps. The 18 m threshold is gameplay policy, not a native tank specification.

Short arrival manoeuvres, explicit reverse, Battery retreats, native combat pursuit
and stationary aiming retain their previous behavior. Native steering, pathfinding,
collision avoidance and controls still generate the actual drive solution. No
throttle, gear or rigidbody-motion writes are added.

Existing 15-second samples now include signed horizontal hull speed, angle to the
owned goal, reverse settings, task ForceReverse and policy eligibility. Negative
speed helps distinguish actual reverse movement from simply facing backwards.

CampaignContent checks pass, including long/short/invalid range boundaries and the
existing content, startup, map, music and AI checks. Campaign/proof builds have zero
warnings/errors. Native hook startup and actual driving remain user tests. No game
process was launched or controlled.

## Installed with backup

The user initially deferred installation, then explicitly requested it. Installed
with Sprocket closed after rechecking the prepared bytes, unchanged installed music
baseline and source. Receipt: `artifacts/install-campaign-20261025-004138/journal.json`.
Configuration was preserved byte-for-byte; root/executable ACLs remain unchanged
with inherited Medium integrity. User-owned Audio/Local was not an install target.
Verification: `artifacts/forward-travel-install-verification.json`.

- Campaign output: `bin/ForwardTravel`, relative to the project root.
- Game DLL: `E4E4413EEE3BA05328AC70C1B4BA51FF49598319783992BFDEC7E1E562C8D172`.
- Core unchanged: `F64D33901C33254607D66BB2D0158ED35061BF832059B59233622D3B3AAA790E`.
- Music manifest: `841CE9243D27763DA42C7E3A321C69857EE68A552B8D5A824827C994A6278A91`.
- Proof output: `bin/ForwardTravelProof`.
- Evidence: `artifacts/forward-travel-*.txt`.
- Private before-source/log snapshot:
  `artifacts/reverse-travel-before-20261025-003845`.
- Private native inspection: `artifacts/reverse-driver-native.local.txt` and
  `artifacts/su100-driver-native.local.txt`.

The baseline DLL matches the latest installed per-era music build:
`33F68C01CD4605185CEB2884981A032F06A4A284C7A666DF88CC905A723DEAC5`, receipt
`artifacts/install-campaign-20261024-212556/journal.json`. Compiled IL comparison
finds all 70 music/PCM method bodies unchanged; changed/added method bodies are
confined to CampaignCombatPolicy, CampaignEnemyHunt (including compiler-generated
closures) and the new CampaignForwardTravelPatch. Music manifest hashes also match.
Evidence: `artifacts/forward-travel-il-comparison.json`.

For later installation, freshly check the game is closed and preserve intervening
work/config/music. The normal installer resolves BuildOutput relative to
`src/SprocketExpedition.Game`, so this package uses `../../bin/ForwardTravel`.
Back up the then-current install; do not overwrite later music work with this
package if it has changed. User-owned Audio/Local files are never installer targets.

User check after installation: observe a tank with a long waypoint behind its hull.
It should turn/advance instead of reversing over the full approach; short obstacle
manoeuvres and stopping/aiming on contact should still work. If it recurs, note the
tank name and phase so the new diagnostics can distinguish movement sources.
