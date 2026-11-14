# Changelog

## 0.2.4 - 2026-11-01

- Every one of the 33 sight variants now has its own `AvailableFrom` date from `ERA-MAP.md`.
- The picker hides too-early variants from the selection list, shows them in grey with `vanaf <date>`, and never overwrites a saved choice.
- A saved too-early sight keeps its identity, uses the stock sight and logs one warning.
- The default sight follows the vehicle's actual date; `EraForYear` now switches to Coldwar at 1945.09.03 instead of 1946.
- Sentinel `9999.12.31` keeps every sight available.
