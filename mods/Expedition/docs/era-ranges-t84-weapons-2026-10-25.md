# Era spawn distances and T-84 weapon investigation

Status: installed with backup while Sprocket was closed. Native gameplay
verification is pending. Receipt: `artifacts/install-campaign-20261025-234005/journal.json`.
Installed hashes, original configuration, root/executable ACLs and Medium
integrity labels were verified after installation.

The prior installed `2A28EA07...` build applied 350–500 m in every era. The last
session confirmed a 197 m friendly deployment shift on Defence, and actual initial
player coordinates followed the relocated markers. Thus variation was active,
but the era-specific enemy distance policy had not been connected.

## Spacing changes

| Native era schedule entry | Enemy distance from every selected friendly anchor |
| --- | --- |
| WWI | 350–500 m |
| Interwar | 450–600 m |
| Earlywar | 600–750 m |
| Midwar | 800–1,000 m |
| Latewar | 1,000–1,200 m |
| Coldwar | 1,200–1,500 m |

The later approved WWI 350–500 m front-arc trial is retained. The earlier
era progression is now threaded through ordinary, covered/open and special
formation placement on all supported maps. No historic dates or era playability
are changed. Terrain, bounds, spacing, probe limits and complete-layout checks
remain mandatory. Failing a layout retains the native map layout with an explicit
status; it does not silently reduce the requested era distance.

Combat ranges now also scale with the encounter era. Coldwar Assault profiles
hold at 450–600 m, Line at 700–900 m and FireSupport at 1,000–1,200 m, when a
natively detected contact and a clear firing line exist. Roles, reaction skill
and native gun controls are preserved. These are policy distances, not guaranteed
engagement ranges: ridges, native detection and approaching enemies can still
produce a close encounter.

## T-84 allies: unresolved weapon gate

The user clarified that the allied T-84 copies do not fire. Native logs show
attack orders and clear-shot holds at short ranges, but the previous build did
not record native gun state. Local inspection of the exact locked battle
blueprint confirms the gunner/turret assignments and enabled assigned bustle
autoloader settings survived capture. This does not prove runtime loader fit,
ammunition, crew control or aiming succeeds.

Read-only weapon samples now accompany the existing 15-second AI samples while
attacking. They report native gunner count/task, Functioning, CanFire,
ReloadFraction, gunner/loader enable fractions, sight, task instructions,
shot-buffer head and each aiming mechanism's target/achievability/angular error.
No firing permission, shell, autoloader setting, ammo reserve or crew assignment
is overridden. Native/personal material stays local. No other plugin is changed.

## Checks and package

- Campaign and proof builds: zero warnings/errors against the installed interop.
- CampaignContent checks pass. New checks exercise 144 era/seed combinations,
  ordinary open fallback and all three special patterns, every-friendly range
  constraints, spacing, deterministic retries and era-aware combat roles.
- Core and music sources are unchanged; the Core DLL and music manifest match
  the previous installed package.
- Before-source snapshot: `artifacts/era-range-gunner-before-20261025-233703`.
- Builds: `bin/EraRangeGunner`, `bin/EraRangeGunnerProof`.
- Package: `artifacts/era-range-gunner-package.json`.

User test: begin a Coldwar encounter with the T-84 design and at least one AI
ally. Check the initial nearest-enemy distance, then allow the allies to engage
for about 30 seconds. Report whether they shoot. If they remain idle, the next
local log should identify which native weapon gate is blocking them. The T-84
firing issue is not yet claimed fixed.
