# Items from the build editor — 24 October 2026

The user could not reach active items while building, making removal of Last Stand
before Play impractical. The existing fleet overlay now includes ITEMS / BONUSES
directly below its budget. It is available for an existing item-enabled run while
the editor is settled and no controller command is in flight. A fresh session
without a committed run/reward inventory cannot activate it yet.

The visual uGUI button uses the campaign menu's existing direct pointer dispatch
from Update. Clicks are sampled every frame, independently of the label's 0.2-second
refresh. The button has a raycast target, no injected click delegate or extra keybind.
It follows the overlay's native editor/session lifetime and hides under the armory.

Opening queues ShowArmory without Play or design capture. MANAGE ACTIVE ITEMS can
discard Last Stand through the existing saved reward command. BACK TO BUILDING
(and the menu's Close button) return to the same native editor and unsaved design,
without opening another scene or reloading an old blueprint. Reward changes refresh
budget, fleet and affordability through the existing rules; the overlay then reads
the live editor tank price again. The normal challenge-menu item route is retained.
Session reset clears the editor-origin flag. Unresolved loot/recovery still cannot
be silently skipped through Close.

Campaign/proof builds pass with zero warnings/errors. Existing integration scenarios,
including item management, full capacity, saved replacement and recovery, pass.
Native button appearance/input and preservation of editor changes remain a user
test; no game process was launched or controlled by Codex.

Installed after the user saved and closed Sprocket, with backup and verified hashes:

- Receipt: `artifacts/install-campaign-20261024-200249/journal.json`
- Game SHA256: `4383D442FC2F1076EEF4BF3DDB7CC96A39BAD8D0933F4975479663821A6A5226`
- Core unchanged: `F64D33901C33254607D66BB2D0158ED35061BF832059B59233622D3B3AAA790E`
- Music manifest unchanged: `51DEB1A702696F96BF748444A0234D9D75E0C6BC87A1F5EC7572887853304A5D`

Config preserved byte-for-byte, root ACL unchanged, executable still inherited
Medium. Build/integration/ACL evidence: `artifacts/editor-items-*.txt`.

User check: resume the builder, open ITEMS / BONUSES, discard Last Stand through
MANAGE ACTIVE ITEMS, then BACK TO BUILDING. Confirm the current tank remains as
edited and the fleet readout permits allies again (with the Last Stand budget
bonus removed).
