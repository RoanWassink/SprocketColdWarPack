# Armour sandbox and combat reports

Local test development. Native gameplay acceptance is separate from managed checks.

Armour still pays its native density, resistance, spall and cost. Additional material reactions act on the remaining projectile state, once per reached interaction. There is no whole-array material loss ceiling: later reached layers can continue acting on what remains.

| Role | Construction and tradeoff |
| --- | --- |
| High-hardness steel | Thin strike face against full-calibre projectiles and shorter rods; retains its native spall and mass properties. |
| Advanced armour steel | Modest additional kinetic resistance across a wider thickness range; premium native material cost. |
| Ceramic | Requires reached steel backing for the added disruption effect; repeat hits in the affected local cell reduce that effect. |
| Heavy alloy | Compact rod protection with a substantial density penalty. |
| Textolite | Added response requires a reached steel–filler–steel sequence. |
| Elastomer | Angled steel–elastomer–steel sandwich; a nearby steel plate alone does not prove a valid sandwich. |
| Aramid | Acts on secondary fragments that actually reach the liner, subject to successful optional native energy hooks. It is not primary dart armour. |

Unknown geometry or unsupported native facts retain native behaviour. Normalization and ricochet retain the game's behaviour; this build does not invent additional values for either.

## Combat report

Configure **Expanded Ballistics / Combat report** in the shared mod keybind settings. The factory binding is unbound. The cannon panel also provides a report action. The read-only menu shows the newest ten observed impacts, including incoming, outgoing and other-vehicle shots. Each impact lists ordered armour interactions and observed outcomes. Missing identity or measurements are marked unknown.

Closing the report restores the framework's normal input state. The short damage HUD is optional and does not control report collection. At the end of Play, bounded history is saved under `BepInEx/config/sprocket.expandedballistics.combat-report/last-session.json` and `last-session.html`. These are session reports, not vehicle blueprint settings.

## Adding response recipes

`BepInEx/config/sprocket.expandedballistics.material.reactions.json` overrides the embedded defaults. An explicitly disabled catalogue remains disabled. Invalid edits are rejected on reload, preserving the previous complete armour snapshot; invalid initial optional content retains the established native/legacy route. In-flight custom shots retain their captured response snapshot.

Recipes define a mechanism, threat tags, thickness/angle gates and calibrated additional response. Bindings associate material IDs with recipes and their native passive fingerprints. A changed density/resistance/spall fingerprint must be deliberately updated in the binding; the runtime does not guess which recipe a custom material intended to use. New recipe IDs become available through the material editor's behaviour choices.

The mechanisms are `direct`, `backed`, `sandwich` and `fragmentAbsorber`. Copy a complete valid entry from the supplied defaults rather than omitting required fields. `maximumInteractionLoss` bounds each reached primary interaction, not the complete armour array. `specificAbsorptionJPerKg` is an energy capacity for fragment absorption, not a penetration percentage. Native spall factors are coefficients, not percentages.

Future shell profiles may declare optional `armourThreatTags`, for example `"armourThreatTags": ["futureThreat", "longRod"]`. Matching recipes can then opt into that trait without a new material-specific runtime switch. Tags do not create a new projectile simulation or change its physical dimensions. The tag `fragment` is reserved for actual native secondary fragments. Existing profiles need no new fields.

## Material ERA

Eligibility uses the declared thickness of the exact struck face where the native layout can be resolved. Adjacent backing must not inherit the face's ERA type. Activation uses finite local cells and the material ledger rearms on returning to Edit.

Sequential stacks inside one native merged block still require a safe native stage-entry implementation. Optional stack metadata is not evidence that multiple charges are operational. Do not advertise an unverified stack or count every geometric rear intersection as a reached charge.
