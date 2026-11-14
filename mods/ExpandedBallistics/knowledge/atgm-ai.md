# ATGM AI aiming and guidance

## October16-24: locked top attack

The current locked top-attack path adds identity-bound acquisition, excludes the exact own vehicle, consumes one ready lock at launch and keeps already launched top-attack flights independent. This supersedes the historical newest-missile-only limit **for this path**, not every SACLOS/MCLOS/AI-ray mode. AI uses one physical acquisition; modular topattack validation and wire rejection remain explicit.

Locking a visible front collider can produce a frontal hit despite a steep path. The reviewed repair performs one downward native ray at launch over colliders attached to the exact acquired rigidbody, retaining a real roof surface in body-local coordinates. Reject foreign/trigger/disabled/oversized/nonfinite hierarchies; static scenery or missing roof keeps the original actual point. Avoid ClosestPoint on nonconvex meshes, weak-spot/health lookup, per-frame target searches or forced impacts.

Plan turns from actual height/pitch and physical turn radius. Begin climb roundout before reaching the apex and descent early enough to accommodate pitch-over. Topattack alone permits the bounded forward-horizontal turn from climb into dive; retain SACLOS/MCLOS guards and actual angular limit. At final convergence use the tracked real point, and do not loop horizontally back after passing the target. Estimate target motion from successive observations of the same transform, reject discontinuities, smooth and bound lead; destroyed targets freeze the last point without reacquisition.

The October20 reviewed stock guidance is90 m altitude/45-degree climb/70-degree requested dive,200 m/s and90 degrees/s turn limit. Conservative flat minimum was428.255 m with50 m/s initial launch;200/300 m explicitly use direct guidance,500 m uses full loft. These are gameplay settings and numerical evidence, not a validated real Spike or guaranteed roof strike. Pure trajectory misses do not prove native collision/drag integration. Earlier387 m feasibility and60/25/55 settings remain historical/custom evidence.

Migrate only the exact old stock ID+guidance tuple; skip customized guidance and modular-flight overrides. Preserve warhead, user precursor, motor and all other fields. Flight-phase HUD is read-only; lock status/direct-range notice takes priority. Validate real hull/turret roof impacts, moving/elevated targets, target/launcher destruction and simultaneous independent flights.

For intermittent tiny penetration/deflection, read [chemical impact lifecycle](chemical-impact-lifecycle.md) before changing guidance or damage. Evidence: `outputs/opencode-independent-review-20261017/REVIEW-NL.md`, `outputs/lofting-review-20261020/HANDOFF.md`, `outputs/lofting-review-20261020/REPORT-FINDINGS.md` in the original integration workspace.

## October6-15 follow-up

The separate ATGM launcher recovered native editability by preserving the supplied native NoBreech-style part structure instead of replacing blueprint ownership or overriding generic component picking. Keep actual cannon/loading/readiness/finite ammunition paths; special visual tubes do not justify global collision, sight, fire-group or reserve filters. Invalid native JSON part schema was encountered during a Play crash; restoration of the accepted baseline preceded isolated optional automatic-ammo work. A narrow missing-sight requirement may belong to the dedicated launcher, not every cannon.

Native sight aiming reference and native gunner fire-group selection are separate. A forward reference cannon in a different fire group sharing the sight was a no-code off-axis test proposal; it was not automatically proved persistent or implemented as loft/top-attack guidance. SACLOS/MCLOS and native ejection remain separate from that aiming reference.

Early MCLOS missile destruction could leave the player's tank input captured before guidance became active. Cleanup must release control for pre-activation destruction, explosion, supersession, reset and scene teardown, not just an active guidance loop exit. The user confirmed the subsequent MCLOS recovery on their test. Do not add another keyboard capture or change accepted steering merely to solve lifecycle cleanup.

TOW impact crashes later motivated the disabled byref-return secondary hook boundary. Read [compatibility notes](compatibility-0256.md) before re-enabling any NewFragment hook. Current tandem precursor/main shared snapshot and finite ERA are documented in [ERA packages](era-packages.md); earlier config names below are historical, not a new player experimental opt-in requirement.

> Historical specialist notes, originally recorded through October5. Read [current state](current-state.md) first. Version-specific recipes, limits and identifiers below are not automatic current contracts.

## Evidence and scope (2026-10-04)

Sprocket 0.2.55.5, Unity 6000.3.21f1, BepInEx 6 IL2CPP. Expanded Ballistics
v0.11.0-flight.2 AI behavior was accepted by the user after beta testing with
one missile in flight per vehicle. v0.11.0-rc.1 retains that AI logic unchanged;
its audio changes have a separate validation status. 317 standalone managed
checks passed. Recheck interop signatures and native call paths on game updates.
Native addresses below belong to GameAssembly SHA256
18A9A15B5E5F11898ED4DC34FC3E2D4C12950C3B37AC1FA499E8B00592DEDD56.

## Why low-speed missiles were aimed too high

Native gun AI applies ballistic shell-drop compensation. A soft-launch ATGM
at about 50 m/s, later accelerating to 200 m/s, was therefore elevated like a
mortar. Changing actual launch speed to cruise would sacrifice the ejection
model. Keep actual firing velocity and AI aiming speed separate.

In Cannon.Build, use configured cruise speed for ATGM aiming muzzle velocity
only when the flight module is ready/enabled. Keep live launch velocity from
the selected fixed/cannon launch settings; keep displayed preview truthful.
Cruise aiming speed alone is not the complete correction: override the native
intercept calculator in a narrowly identified ATGM AI context.

## Hook the native method actually called

- GunLayerAI.Step(Vector3, float), RVA 0x201a830, updates its native AI task.
- FindAndAimAtTargetGunLayerTask.Update(ref GunLayerTaskInfo,
  ref GunLayerInstructions, float), RVA 0x20207b0, calls
  Ballistics.CalculateInterceptPoint directly (observed call at 0x2020de8).
- Ballistics.CalculateInterceptPoint(Vector3 targetPosition,
  Vector3 targetVelocity, Vector3 launchPosition, float projectileSpeed,
  int maxIterations), RVA 0x1ce17c0, is the effective hook.
- GunLayerAITask.EstimateCorrectedTargetPoint (0x201af00) is a convenience
  wrapper but is inlined in the relevant task. Patching only it misses AI aiming.

RuntimeAtgmAi uses a separate optional Harmony owner `.atgmai`. Track built
Cannons; remove them on Cannon.DisableBehaviour. In a GunLayerAI.Step prefix,
match Layer.ControlTarget.Pointer to the cannon's horizontal or vertical aiming
mechanism pointer. Require a selected atgm/atgm_gun profile with guidance,
healthy cannon and active flight module; exclude the currently controlled player
vehicle. Set thread-local cannon/profile context and restore it in a finalizer.
The intercept prefix replaces only calls within that context. Other ammunition
and native AI acquisition remain unchanged. Optional AI patch failure must not
unpatch the flight/impact modules.

Use the AI-provided estimated target position/velocity, not an omniscient entity
lookup. The accepted gameplay lead performs three iterations:

    point = estimatedTargetPosition
    repeat 3 times:
        time = distance(point, launchPosition) / cruiseSpeed
        point = estimatedTargetPosition + estimatedTargetVelocity * time

No extra gravity elevation is added. Reject nonfinite inputs/outputs and fall
back to native behavior. This is cruise-speed lead, not a calibrated accelerating
intercept solution. Native visibility, acquisition, aim errors and target
estimates remain in use. Shared aiming mechanisms with mixed cannons were not
comprehensively validated; do not claim arbitrary launcher combinations work.

## Reuse AI aim for in-flight guidance

Cache the calculated launch origin and normalized intercept direction against
Cannon.Behaviour.Pointer, timestamped with game Time.time. Before native
ProjectileRegister.FixedUpdate, use a fresh cached ray for a healthy AI launcher
(freshness window 0.3 s). Convert it to a look-ahead steering target with the same
AimAlongRay/turn-limited Step helpers as SACLOS; use sight-mode steering for AI
regardless of whether its profile is sight or keyboard. Player MCLOS remains
WASD and player input lock remains player-only. This is ray guidance, not target
lock, seeker, top attack or fire-and-forget.

ProjectileRegister.pool[index] returns a boxed ProjectileInstance copy: write
modified velocity back into pool[index] before native movement/collisions.
Retain native damage/HEAT impact and collision processing. Clear cached aims on
projectile reset, but retain live cannon registry entries until DisableBehaviour;
resetting projectiles does not destroy cannon assemblies.

## Crucial ownership limit and log interpretation

The accepted implementation guides only the newest missile per vehicle.
Launching another missile supersedes older missiles from that vehicle, even if
another launcher fired it. Superseded missiles retain their last heading and
continue flying. Rapid launches can therefore miss despite working AI guidance.
The user requested retaining the accepted behavior after confirming one missile
at a time worked well. Do not change ownership or accepted steering merely to
explain these logs; multi-missile guidance would be a separately scoped feature.

Historical plugin configuration settings (resolve the actual current file from installed plugin metadata; this is not a literal legacy filename):

    [ATGM Experimental]
    DiagnosticLogging = true

Restart after editing. Read `BepInEx/LogOutput.log`:

- `[ATGM AI] Scoped native aiming hooks loaded`: patch installation, not proof
  that any shot used the override.
- `[ATGM AI] Matched`: native gun-layer/cannon association, once per cannon.
- `Native intercept replaced without shell drop`: override executed and target
  point, once per cannon. It is not continuous target telemetry.
- `[ATGM] LAUNCH` and `MOTOR`: launch velocity and actual motor-start state.
- `COMMAND ... guidance=ai-ray`: active AI steering, once per second with
  diagnostics enabled. `reason=arming` is still before steering starts.
- `reason=superseded`: newer launch took ownership; not an AI tracking error.
- `[ATGM] NATIVE`: actual speed/movement after native tick. headingDot near 1
  proves commanded velocity survived the tick, not that aiming is accurate.

A pasted fragment containing only arming and superseded lines cannot establish
active guidance or the cause of a high miss. Request the full startup/first-shot
log and first test a single launcher with reload interval longer than flight to
impact. Compare stationary/moving targets and ordinary AP. No AI-ray entry in a
partial excerpt does not prove the hook never ran. Do not retune blindly from
screenshots or from superseded missiles.

## Flight-phase interaction

Before motorDelay, leave native velocity alone so gravity/drag accumulate; the
old per-tick heading restoration erased ejection drop. At ignition capture
actual speed and heading. MotorBurnTime=0 and CoastDeceleration=0 preserve old
sustained-power semantics; optional finite burn/coast use game tuning. Guided
powered flight can legitimately be almost straight after launch; no artificial
oscillation was added. The US Army TOW history describes a trajectory close to
line of sight: https://history.redstone.army.mil/miss-tow.html . No complete
verified Konkurs speed-time curve was recovered; .12/.08 s delay, 4 s burn and
1 m/s² coast are example gameplay values, not measured historical specifications.

## Source map

In the Expanded Ballistics project: RuntimeAtgmAi.cs scopes AI calls and caches rays;
RuntimeAtgm.cs tracks flight, player/AI ownership and logs; AtgmGuidance.cs
contains pure lead/steering/motor math; RuntimeShellSelection.cs sets cannon
preview/launch/aiming values. ATGM.md and CUSTOM-SHELLS.md document public
settings. Consult current source before extending old experiments.
