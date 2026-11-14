# Leopard/T-84 bustle loading investigation

Status: autoloader 0.2.19-ai.1 testbuild installed with Sprocket closed and backup.
The local `install-20261026-204659/journal.json` records installation and verification
of 299 other protected files, original ACLs and Medium integrity. Native firing
acceptance is pending. No Expedition source or DLL was rebuilt by this repair task.

Latest live observations show Leopard 2a7 native weapon crew functioning and
FireAtWill/FireWhenAimed orders, but repeatedly ReloadFraction=0, CanFire=False
and no gunner shot-buffer entries. This identifies an unloaded/unready weapon;
it does not yet establish why it failed to load or prove both reported designs
share the exact same failure.

The existing autoloader attaches during build. The native controller captures
its contributor array during assembly. A guarded late-attachment path now checks
each controller, invokes the existing valid-feed initializer when a binding is
missing and adds only that weapon's advertised automatic-loader contributor if
absent from the capture. It preserves crew, does not duplicate the contributor,
and never loads/free-fills ammo, bypasses fit, accelerates reload or forces Fire.

Bounded loader-state traces also report the assigned bustle's capacity, remaining
stock, native type, eligibility and the actual LoadTask state/request/progress.
They distinguish a repaired capture from a different empty/invalid feed problem.

Installed 0.2.18 baseline SHA256:
`F23398A4B7964510699D387DEEF699E2F7A42B3A8A37D9E6477F120696DEC012`.
An isolated source rebuild matches all 758 installed method bodies. Candidate
comparison retains 754; changed existing methods are static initialization,
cleanup, version/log text and the controller-aware update-hook signature. Existing
supply, mixed-stock, timing, persistence, audio and cookoff bodies remain intact.

Candidate compilation passes with zero warnings/errors; existing 85,611 pure
checks and 106 actual capture-policy checks pass. Native late attachment and
Leopard/T-84 firing still require the user's test. This is a conditional repair,
not a proven root-cause conclusion.

Full source, frozen local battle log, method comparisons, tests, review and
one-DLL backup/hash-guarded installer:
the original development workspace (not part of this release).
Candidate SHA256:
`13BC7620DA3D3A1B7A30EAD5B0906153D000E30B46A7B57627C756611DAC56CF`.

User test: let T-84 allies or Leopard 2a7 enemies engage, and watch for at least
two shots. Then repeat Play. If silent, inspect the local `[Autoloaders load trace]`
and `[Autoloaders] Repaired captured loader` messages before another behavior edit.
