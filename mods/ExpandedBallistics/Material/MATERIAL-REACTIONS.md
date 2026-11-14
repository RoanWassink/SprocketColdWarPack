# Differentiated material reactions

The material editor now offers data-driven response roles in its Behaviour dropdown. These augment native armour resistance; native density, passive resistance, spall factor, mass and cost remain native values. Existing material IDs and legacy ERA routes remain compatible. Magnesium remains available as a passive material.

| Role | Additional response | Required geometry |
|---|---|---|
| High-hardness steel | Full-calibre AP/APHE and short-rod response | Actual reached plate within thickness and angle range |
| Advanced armour steel | Smaller full-calibre response, also eligible against long rods | Actual reached plate |
| Heavy-alloy insert | Short/long rod response | Actual reached dense insert |
| Ceramic strike face | Backed insert response; repeated hits degrade the local 0.15 m cell | Actually reached ceramic followed by steel backing, at least 3 mm thick, gap at most 25 mm |
| Glass/textolite | Sandwich response | Actually reached steel / textolite / steel; steel at least 3 mm; gaps at most 15 mm |
| Elastomer NERA | Oblique sandwich response, ramps from 10 to 30 degrees | Actually reached steel / elastomer / steel; steel at least 3 mm; gaps at most 5 mm |
| Aramid fragment liner | Finite incoming fragment kinetic-energy absorption | Actual secondary fragment crossing the liner; verified optional continuation hooks required |

These are editable gameplay calibrations, not measured historical protection specifications. No normalization, ricochet or yaw change is claimed. The system does not silently add a fixed percentage to all ammunition.

## Editing and refreshing

Open the material editor, expand a material, and select the role above its numeric values. Recipe fields show their accepted ranges and units. Threat tags identify supported incoming threats. Save validates and atomically backs up the native material, labels and both reaction catalogues, then refreshes in game. Editing one material's shared recipe creates its own recipe instead of changing every material using the original. Unavailable material bindings are preserved. Existing custom native material values are not automatically overwritten.

The additional reaction sidecar is `BepInEx/config/sprocket.expandedballistics.material.reactions.json`. If absent, embedded defaults are used. It has `schemaVersion`, `enabled`, `maximumInteractionLoss`, `steelMaterialIds`, `recipes`, and `bindings`. A reload rejects malformed JSON as a whole and retains the previous valid snapshot; already launched projectiles retain their launch-time snapshot. Back up this file before manual edits.

To bind an existing custom material to a provided role, append a binding with its actual native passive fingerprint:

```json
{
  "materialId": "myDenseInsert",
  "recipeId": "heavyAlloy",
  "density": 17500,
  "rhaFactor": 1.5,
  "spallFactor": 0.00035
}
```

That material must exist in native technology data. A fingerprint mismatch disables its additional reaction rather than stacking incompatible defaults. The material editor updates the fingerprint when deliberately saving changed native values. Native `rha` and `sheetMetal` must be duplicated before assigning a special response.

New recipes use the generic `direct`, `backed`, `sandwich`, or `fragmentAbsorber` mechanisms. Reuse a provided recipe as a complete schema example, use unique IDs, and bind it declaratively. Primary built-in tags are `kineticFullBore`, `shortRod`, `longRod`, and `chemicalJet`. A shell can add arbitrary identifiers through `armourThreatTags`; `fragment` is reserved for actual native secondary fragments and cannot grant original ammunition fragment-liner treatment.

`maximumInteractionLoss` limits each individual primary recipe interaction, not the total shot or armour array. Every subsequently reached layer can reduce the remaining penetration. A ceramic strike face and reactive steel backing each keep their own response. Fragment absorption instead uses crossed liner mass × `specificAbsorptionJPerKg`, bounded by the fragment's actual remaining kinetic energy and recipe limit. It does not reduce upstream fragment creation. Default liner capacity is 250,000 J/kg.

## Native integration and validation limits

Reached-block facts are supplied by the shared armour hook for original and secondary fragments. Exact native target VUID, declared face thickness and entry/exit ownership are required. Mixed/merged native blocks, unresolved face markers, edges/rivets and unknown thickness fail closed to native armour. A backing which native merges away cannot be advertised as an actual reached sandwich. No inferred chord thickness or nearby unhit backing is substituted.

Primary loss uses the native inverse penetration-speed conversion and verifies the resulting native base penetration before committing traversal or local ceramic damage. Native passive resistance is paid once by the game's original simulation. Only the exact validated reached layer supersedes a legacy non-ERA role; unrelated layers and failed conversions retain their existing path.

Secondary absorption is enabled only when the native continuation hooks are verified. Copied continuations must receive the same reduction, and a fully absorbed fragment must not produce a cached-energy continuation or a downstream spall burst. Tooltips show native-only/unavailable when these hooks are absent. In-game checks are still required for actual effectiveness, native merged-layer configurations, liner continuations, repeated ceramic cell hits and Play/Edit reset. Pure tests alone do not establish live acceptance.

## Test handoff

Build succeeded with 0 errors and the existing MSB3246 native-reference warning. The combined shell suite passes 7,672 checks, including 49 material cases: distinct roles, actual backing/sandwich gaps, local degradation, multiple layers beyond the per-interaction bound, fragment finite energy, transaction isolation, invalid schemas/fingerprints and readiness-aware tooltips. No game files were changed by this implementation task.
