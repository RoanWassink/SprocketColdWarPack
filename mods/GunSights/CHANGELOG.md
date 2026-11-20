# Changelog

## 0.3.0 - 2026-11-16

- New checkbox per sight, "Sight follows gun", in the inspector section "Gun sight" (native `ToggleField`).
  - On: the game's own view, aimed with the gun (up and down); the native aim circle stays visible.
  - Off ("stays static"): the view stays along the sight part, as the old global option did, for that sight only.
  - Works for every gunner sight, including the Thermal Sight parts. The ATGM guidance sight keeps its free view and shows no checkbox.
- The global `[View] ScopeFollowsTurret` is no longer read. New: `[View] DefaultSightFollowsGun = true`, the default for sights without a saved choice.
- The reticle variant and the checkbox are saved in the blueprint object state under `sprocketGunSightsV1`
  (postfixes on `VehicleObjectSerialization.ToBlueprint` and `VehicleObjectRegister.InstantiateStructured`, as Thermal Sight).
  Other keys, including Thermal's `sprocketFcsLinksV1` and `sprocketThermalProfilesV1`, are kept in either postfix order.
  VUIDs are remapped on duplicate/instantiate; broken or duplicate rows are refused. The old `sprocketGunSightVariant` component key is still read as a fallback and still written.
- Log lines: one per sight activation (VUID, follows gun / stays static, saved or default; max 40), one per save and per load with the row count.
- Harmony patches: 8 -> 10 (only the two persistence postfixes).

## 0.2.4 - 2026-11-01

- Every one of the 33 sight variants now has its own `AvailableFrom` date from `ERA-MAP.md`.
- The picker hides too-early variants from the selection list, shows them in grey with `vanaf <date>`, and never overwrites a saved choice.
- A saved too-early sight keeps its identity, uses the stock sight and logs one warning.
- The default sight follows the vehicle's actual date; `EraForYear` now switches to Coldwar at 1945.09.03 instead of 1946.
- Sentinel `9999.12.31` keeps every sight available.
