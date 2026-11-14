# Verified existing API facts and scope

Coordinator inspected current generated interop with Mono.Cecil on the exact installed .56.2 hash. Existing RuntimeAtgm already patches ScopeController.Update and the two-Vector3 SetAimPoint, and has access to that controller. Verified getters on Sprocket.Vehicles.ScopeController:
- `float GunConverganceDistance { get; }` (spelling exactly as shown)
- `UnityEngine.Vector3 ScopeAimPoint { get; }`
- ControlledScope, Scoped, HasActive are already used by the supplied baseline.

These are scope convergence/aim data, NOT evidence of a separate native FCS or laser-rangefinder range dial. A fresh scoped sample can carry the finite positive GunConverganceDistance as a launch snapshot, explicitly documented as sight-convergence range rather than precise GHPC FCS equivalence. Reject implausible/nonfinite distances and stale scope samples. PlayerAim currently has no range: extend the existing AimPoint record with optional range and correlate only the exact weapon sight, never a global most-recent scope. Third-person/no fresh exact scope: use safe direct fallback with a concise diagnostic for a hump profile.

RuntimeAtgmAi computes an intercept direction from a native AI-supplied estimated target point. Its cache may retain `direction.magnitude` as launch range with an additive TryAimRange helper. Keep existing TryAim signature usable and no entity lookup. Snapshot once at launch; do not update range from hidden current entity positions. Zero/unavailable range gives safe direct fallback.

Read the supplied native RuntimeAtgm and RuntimeAtgmAi for existing exact method hooks; no additional Harmony hooks required. For hump mode let a profile configure its threshold and terminal distance. Short lockout and initial forced elevation may use the snapshotted sample, but avoid calling an unknown FCS API or modifying the actual native cannon angle. This is an independently moving missile after launch; independent physical launcher ownership remains deferred.

Tracer rendering may use the same GameObject primitives, Mesh, Material, Texture2D, Renderer, Camera.main and Transform operations already used by RuntimeAtgmTrail. Do not depend on undocumented ParticleSystem or new native entry points. AtgmGuidance.Vector3 means System.Numerics; runtime uses UnityEngine and explicitly converts them.

Tests add `AtgmFlightChecks.Run(Action<bool,string>)`; coordinator merges it into the existing main test executable and links newly introduced pure files. Do not fabricate a whole missing production/test framework.
