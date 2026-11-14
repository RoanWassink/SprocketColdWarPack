# Changelog

## 0.7.3 - 2026-11-01

- Both Technology records (`SprocketStabilization` and `SprocketStabilizationTraverseMotor`) are dated 1948.01.01.
- The runtime now checks the native technology frame date against the same 1948.01.01 floor before accepting the checkbox.
- A too-early saved checkbox state keeps its identity, is not applied, and logs one warning.
- The exact final-era sentinel 9999.12.31 remains available.
