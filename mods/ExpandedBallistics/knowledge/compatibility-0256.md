# Compatibility, native hooks and update recovery

## Exact build qualification

| Game | GameAssembly SHA256 | Status/evidence |
|---|---|---|
| 0.2.56.1 Experimental | `FCF745C40A02406A4D757A859A8FFCEF67D82835C98BE557312861E843B04FFC` | Loader startup and module-specific investigations performed October10-12. |
| 0.2.56.2 Experimental | `3E58082804D97CF32FFD400E7427E767EF9576D23DB71B2BE96FBB7F825FC17B` | Current October13-15 native impact/report work. |

Both reported Unity6000.3.21f1. Same Unity version does not imply unchanged native addresses or managed API layouts. Method names, RVAs and flags below must be reverified on another hash.

## Loader before gameplay modules

An update regenerated interop successfully yet failed chainloading before gameplay plugins: the private Sprocket-specific Il2CppInterop profile rejected the new game hash. A missing-mod symptom therefore was not evidence that all module patches broke.

For .56.2 the privately repaired profile resolved ClassInit `0x4E47B0`, FieldDefault `0x494C30`, GenericMethod `0x4CFDD0`. Unique masked matches, export traversal and complete pdata-bounded bodies (103/341/2338bytes) corroborated the new locations with no nonrelocation differences. Hash and prologue guards remained enabled. Never accept a changed game merely by replacing its hash string or disabling fail-closed checks.

The exact unpublished local profile source/provenance was incomplete. This was a private local recovery, not a redistributable loader. Do not package game-derived interop/cache/native files or promise a stock BepInEx substitution. Public bootstrap needs upstream source/provenance and local cache generation from the user's vanilla game.

Separate checks: loader startup; explicit Harmony target resolution; dynamic/generic targets; correct calling conventions; editor operations; actual impact; Play reset; save/load. Hundreds of resolving declarations do not prove native execution.

## Engine API migration

In .56.1 the engine editor classes moved from `Sprocket.EngineDesigner` to `Sprocket.EngineDesigner.Editor`. Native `EngineLayout` is None/V/Inline/Radial, not arbitrary JSON strings. `ICombustionEngineModelGenerator`, `EngineModelOutputMeshes`, `layoutModelGuids`, output/COM and Built event replace older model interfaces. Existing definitions support native generator reuse; a new fourth layout is not proved JSON-only.

The .56.2 engine repair verifies the exact hash and float update signatures. Prefer native blueprint/model/generator and event paths over unrelated physics or per-frame synchronization hooks.

## Byref-return and empty-stub hazards

- The experimental secondary-energy/NewFragment byref-return hook caused native crashes. It remains disabled. A managed build or signature resolver is not permission to turn it back on.
- `VehicleComponent.EnableBehaviour` / `BuildDamageModelColliders` can resolve to the same empty shared stub. Do not globally patch an inherited empty native stub to implement one part. Carousel damage instead uses actual `VehicleObject.EnablePhysics` and `VehicleDamageModelGeneration.CreateStructures` paths.
- Public CreateHealthPool can inline its Internal method; hooking both needs exact pool-pointer deduplication.
- IL2CPP structs are boxed copies: update the actual fragment array after validated edits. A modified wrapper or report value is not proof of physics writeback.

## Cached-energy ordering

`CompoundStructure.SimulateFragment` caches incoming kinetic energy before `GetNextMaterialBlock`. Changing parent speed during block lookup does not automatically change a continuation calculated from that cache. The .56.2 analysis identifies the cache before the lookup; use the current checked disassembly rather than stale addresses.

The accepted correction route stores one aggregate speed ratio relative to the scope's original speed. Chemical gap loss now notifies that route just as ERA does. Notifications replace the aggregate ratio; independently multiplying gap and ERA again double-charges. Apply to exact new original continuations, never restore a native stopped core. A copied-air branch can differ from a newly computed continuation: do not change its energy policy without proof. Current narrow handoff repair observes already reduced copies without writing native energy.

## Native fragment transition evidence (.56.2)

- Verified code branches produce original deflected/embedded `0x31` children and original ricochet/spall `0x101` children.
- The copied-air branch (`0x1D78E09` in the reviewed hash) clones the current parent then adds precomputed flag `0x80`, giving original `0x81`.
- Exact parent/profile/mass/diameter, unique candidate, finite non-increasing budget and native flags establish a bounded handoff; screen-space path proximity does not.
- Native fragment indices in a report are not parent links. The saved report did not prove which exact edge led to the old fragment209 reset. Preserve that uncertainty.

## Tooling and validation lessons

Use the actual installed SemanticVersioning library for dependency checks. Numeric ranges rejected helper prerelease strings even though APIs compiled. Use numeric plugin versions for installed dependency contracts and keep test identity in build artifacts.

When local sandbox Temp ACLs prevent Roslyn response files, use a task-local writable TEMP/TMP directory. Do not interpret this as a source error or modify unrelated system settings. Prefer already restored projects with `--no-restore`; a minimal local-source restore can avoid an unrelated NuGet lock when all required packages are available.

Exact install preflight, backup hash, touched-file rollback and preserved-file hashes distinguish a prepared package from a verified installation. A previous one-off user instruction to terminate one identified hidden process is not a general process-killing permission.

Evidence pointers: `outputs/beta-compatibility-20261010/STATUS.md`, `outputs/beta-2562-compatibility/REVIEW.md`, `outputs/native-block-audit/FINDINGS.md`, `outputs/tow-impact-crash-20261013`, `outputs/dependency-version-repair-20261012/REVIEW.md`.

## October22-24 adapter findings

BepInEx soft dependencies are load-order edges; reciprocal DynamicSight/Ballistics edges caused chainloader abort. Generated interop callability is separate from signature existence: Graphics.DrawTexture compiled but failed at runtime because the native method was stripped. Top-level native part v0.2 requires `order`; component-model schema versions are separate. Native importer errors may appear only in Unity Player.log. Read [DynamicSight and WW sights](dynamic-sights.md) for exact evidence and the uninstalled icon-repair candidate.
