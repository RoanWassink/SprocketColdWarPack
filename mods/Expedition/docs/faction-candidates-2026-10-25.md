# Personal faction Coldwar audit — 25 October 2026

User asked to inspect other factions, naming Nederland's T-80BVM. This turn was
read-only for factions/game data. No imports, game installs, header/date changes,
native load or game control occurred.

Tool: `tools/audit-faction-blueprints.py`. All nested Factions folders are scanned:
138 vehicle-path files, comprising 107 modern-header vehicles, 30 legacy vehicles
without the current date/cost header, and one freeform component (`t62m.blueprint`).
No parse failures. Native era names/starts/playability/mass thresholds are read
from the currently installed `Sprocket_Data/StreamingAssets/Eras/*.json`, allowing
their trailing commas. No historical era dates are embedded in the tool.

There are 25 Coldwar-range source files / 24 exact or canonical JSON identities,
including autosaves, tests, near-duplicate families and one exact existing pool
entry. Header date 9999.12.31 is retained as the native final-era sentinel; it is
also present on the Oplot already classified Coldwar by the latest native pool.
Finite-date candidates follow the installed era definitions. Runtime card loading,
part completeness, actual operation and native cost recalculation remain untested.

Prepared shortlist of nine new candidates: T-80BVM, T-80B v2, Leopard 2a6-AMAP,
T-84 Oplot-S, C42 Falcon-S, VRRC-155, PzH6000, Variant13, and the named Stormgaupe
Modell 4 from downloads. This favors weight/weapon/price variety over filling slots
with Oplot repairs or test copies. Mass and costs are saved header data. Calibers
are cannon definitions, not proof that each is mounted or operational.

Excluded from the initial shortlist: autosaves, named isolation/test/FR005/ERA-fix
variants, Variant16 with no cannon component, the 218.98 t MBT No.24 outlier, and
the future-dated MBT-27A9E2M (optional separate review). Default's Centurion/T95
are legacy-format conversion candidates rather than current verified imports.
Many additional Leopard/T-80/T-84/VRRC files have 1945.01.01 or 1945.09.02 dates,
which place them in the installed native Latewar era. Names do not override dates.

Reports, all-faction and Coldwar CSVs, full audit and prepared import manifest:
the original development workspace (not part of this release).
The nine original file hashes were rechecked after planning. Importer PlanOnly
accepts the manifest (9 vehicles, 3,927,571 bytes); no destination writes occurred.
`verification.json` records the exact reviewed manifest hash. A later authorized
import must recheck sources and game closure, preserve originals and immutable
existing run catalogues, and distinguish pending native validation from verified
playability. Raw personal blueprints and local data were not shared externally.
