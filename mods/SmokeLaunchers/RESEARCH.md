# 0.2.4 audio implementation and current acceptance

User directly confirmed0.2.3 no crashes and stopping/restarting Play reloads banks. Thus crash repair is available/limited-live-accepted. Before integration actual local installed Smoke DLL was checked twice and hashes to accepted rollback0.2.1 (76CD256...C26B4E), not the tested0.2.3 candidate. No install here.

0.2.4 reuses native injected AudioClip upload and AudioSource members, with two deterministic own mono clips. Launch gains2.8/1.5=1.8667 amplitude over0.2.1 (+5.42dB), measured peak0.775026/RMS0.213539, near-distance6m/max65m. Distinct0.48s burst on BeginSmoke, with one gate per launched grenade/effect slot and prewarmed48 burst voices. Burst world position is actual hit/expiry, not cloud2.2m lift. Existing muted/custom source volume unchanged. No War Thunder assets; feel reference only.

Native0.2.3 observer and plain icon unchanged; no async Task/CancellationToken hooks or GPU icon calls reintroduced. Body checks are still not native sound-quality or ABI proof;0.2.4 needs live sound/mix/pause test. Historical crash evidence below remains intact.

---
# 0.2.3 crash evidence and repair

Read frozen REVIEW7-CRASH Player/LogOutput logs and matching WER dump35340. Dump access violation0xc0000005 CoreCLR, managed System.ExecutionEngineException on35700, PInvoke(IntPtr,IntPtr), repeated ReversePInvoke(Int64,Int64,Int64). Native raw stack addresses map to ScenarioGameMode.EnterState/SetState state machines. Raw stack scan is not a full unwind. Last Player line enters Vehicle Editor; no new icon/session success log. Strong suspect is async native entry trampoline; icon GPU cause is not established.

Removed ALL six Task/cancellation-token lifecycle patches and ALL Graphics.Blit/ReadPixels/RenderTexture icon work. Replacement SceneUpdate is void/noargs with reference instance; reads existing native gamemode.State, scenario.activeState/mission/Active or deathmatch.missionRunning. This follows main-thread state rather than detouring async methods. Plain native borrowed icon only. Verify.ps1 rejects removed async target names and GPU references; no by-ref callbacks. Native and managed live validation remains required; do not repeat the0.2.2 claim that body audit establishes bridge safety.

Historical0.2.2 research follows for traceability, with its async hooks/GPU badge superseded and removed.

---
# 0.2.2 lifecycle and icon evidence

Exact native contracts verified: MissionScenarioGameState.Enter(CancellationToken)/Exit, VehicleEditorScenarioGameState.Enter(CancellationToken), DeathmatchGameMode.SetupGame/ResetGame, GameMode.ExitGame and PartDefinitionCardFactory.CreateCard(PartDefinition). No by-ref Harmony callback is introduced. Native MissionScenarioGameState/<Enter>d__10 MoveNext RVA0x1EEB9E0 starts Mission.StartMission (0x1E04DA0), awaits setup and invokes ResetMission on failure. Player control/photo camera states have a separate Enter/Exit path; UI-scene transitions are not used for ammunition reset. Prefix begins ledger before native async setup and state loads; editor entry/mission exit ends it. This is method/body evidence, not live ordering proof.

PartDefinitionCardFactory.CreateCard RVA0x200A330 passes definition.name to GameIcons.GetOrDefault RVA0x1D36F00. Own part name has no native icon. Optional card postfix requests germanSmokeLauncher and composites authored normalized smoke circles with an outline into owned128x128 texture via temporary RenderTexture/Graphics.Blit, sprite textureRect and CPU pixels. It assigns only own GUID card and preserves plain native fallback. No game sprite/texture is exported. The145 directly/transitively reached Unity entries (including9 native delegate endpoints) are audited for unstripping-failure stubs, including Blit/ReadPixels/Sprite/native upload paths.

Session ledger is game-independent regression tested; previous-session values and legacy spent designs reload at fresh native combat, editor saves use preparation1:0, same-session ticket state is monotonic/shared across copies.1024 total tickets per combat bounds missing/legacy-state spawn abuse without recycling released tickets. No serialization flag marshaling/hook is needed: native state boundary is authoritative and existing component SaveData/LoadData hooks retain the key.

Historical notes follow; earlier permanent-spent-across-Play behavior is superseded in0.2.2.

---
# 0.2.1 native mirroring comparison

Standard native germanSmokeLauncherPart.json has model.scaling=true/flipping=true and transform.dynamic=false, scaling=true, airPlacement=true, minScale0.5/maxScale1.25. Own0.2.0 differed in scaling/limits/airPlacement/dynamic and forced MarkDynamic at each preparation. These differences are removed, with verification of EVERY native transform property and five model placement flags. This is a native parity fix; live root-cause confirmation remains pending.

Pinned native GameAssembly research: VehicleModelFactory.Load RVA0x2099FC0 copies definition.scaling (offset0x28) to model.autoApplyScale (0x14b) and definition.flipping (0x29) to autoFlipModel (0xe1). GetModelScale RVA0x2099140 uses native transform scale when autoApplyScale is true, then the native Flipped/requestedFlip/FlippingDisabled handling. Standard renderer constructor is static; MarkDynamic RVA0x2099490 clears staticRenderer and the model Static bit. Own code now follows native model flags and removes forced MarkDynamic; no custom sign branch is introduced. Only own endpoint constants and pure authored audio are shipped, no game data/bundles.

0.2.0 is user-confirmed live for smoke; candidate0.2.1 mirror/audio retest pending. Historical research follows and may describe superseded versions.

---
# Current 0.2.0 findings

Native germanSmokeLauncherPart.json identifies mesh 4155910536a20744f92326d954d869fe, material 244fb3087922ee7428d51ec7ebe33800 and crewEquipment category. Original part GUID d0ef8659-cc00-4d3c-8c51-a8be09a4daba is not modified. Own GUID and all persisted IDs remain stable.

Private read-only LOD0 analysis fitted three 32-point mouth rings (outer radius .05m, inner .04m) and their normals; only minimal endpoint/axis constants are in source. No original mesh vertices, bundles, textures or audio are packaged. Axis constants are in TriLauncherGeometry.cs. Native ModelRotation RVA 0x2096CC0, ModelPosition 0x20973A0, GetModelScale 0x2099140 (includes negative mirror scale), MarkDynamic 0x2099490 on the pinned GameAssembly baseline. Own components use dynamic native placement rather than replacement geometry.

Audio uses native AudioClip.Construct_Internal and CreateUserSound_Injected/SetData_Injected with checked x64 ManagedSpanWrapper layout and pinned own float PCM; m_CachedPtr is used instead of wrapper pointer. High-level span uploads are avoided. The actual reached generated bodies are audited, terminating at explicit native icall delegates. Runtime sound quality remains pending.

The user confirmed 0.1.1 smoke works live. Outgoing separate-thread balance contact remains blocked by automatic review; received advice is preserved. Historical 0.1.1 research follows for traceability and may describe superseded six-tube geometry.

---
# Research evidence and boundaries

Inspection on 2026-10-04 used the local Sprocket 0.2.55.5 research baseline, Unity 6000.3.21f1, BepInEx 6 IL2CPP be.788. GameAssembly SHA256: `18A9A15B5E5F11898ED4DC34FC3E2D4C12950C3B37AC1FA499E8B00592DEDD56`. No game files were modified.

- Enumerated canonical generated Sprocket types/methods for Smoke/Obscur/Visibility/Target/Detection/Exhaust. No native smoke-grenade system was found in those exposed APIs; absence from this search is not proof that no hidden asset exists.
- ExhaustEffect exposes `UnityEngine.VFX.VisualEffect` with rate/richness/wind properties; ProjectileImpactEffect also uses native effects. These are engine-exhaust/impact adapters, not evidence of a smoke-screen density or AI-occlusion service. The first build uses independent procedural puffs, retaining no native effect objects or distributed assets.
- Native `DetectionRegister.TryGetLOSTargetPoint` at RVA `0x1D919E0` (length `0x64E`) calls `UnityEngine.Physics.Linecast(Vector3,Vector3,int)` at `0x1D91D8E` and `0x1D91E3E`. Generated signature includes by-ref DetectedObject and TargetLOSDetectionInfo. Transparent particles without colliders do not change these physics queries. No Harmony patch is applied to AI detection or target memory.
- Thermal source inspected from the current local SprocketThermalSight project: `DrawRenderersCustomPass` at BeforePostProcess, vehicle renderer mask and separate SensorCapture. Ordinary transparent scene smoke is not sensor-mask integration. This mod does not rebuild or change thermal code.
- Native `VehicleObjectModel.Build` at RVA `0x2098770` calls SetMass at `0x2098D72` with MassType=1 (Armour), and SetCost at `0x2098D97` with Armour/Material and multiplier `0.2429958582`. This informed explicit clearing of the bank's adapter resource before setting its loaded 18 kg / 120 price. SetMass changes resources; cachedMass is explicitly synchronized to the total resource mass to keep COM numerator/denominator consistent.
- Correction from the 0.1.0 live failure: exported ParticleSystem properties are not sufficient evidence of callability. The used playOnAwake, loop, startLifetime, startSpeed, maxParticles, simulationSpace and enableEmission setters contain `Method unstripping failed` throw-stubs. v0.1.1 removes the whole ParticleSystem dependency and renders preallocated billboard meshes through actual Mesh/Renderer/Transform interop. An automated recursive body audit checks 88 reached Unity members and rejects the failed backend. This evidence removes the reported failing path; it does not prove the repaired visuals in game.
- Generated HDRP exports include `HDMaterial.SetSurfaceType(Material,bool)` and `ValidateMaterial(Material)`. Runtime validates a native HDRP/Unlit material with a procedural alpha texture. The [official HDRP Unlit source](https://github.com/Unity-Technologies/Graphics/blob/master/Packages/com.unity.render-pipelines.high-definition/Runtime/Material/Unlit/Unlit.shader) establishes the corresponding surface/texture properties. Available compiled transparent shader variants and final particle appearance remain a live check.
- Keybinds 0.1.5 source, INTEGRATION.md, Packman contract and grouping amendment inspected. RegisterButton's existing six-argument signature and non-consuming press reads are retained. Metadata registration supports Nero / Smoke Launchers; no automatic pack membership is declared by this standalone consumer.

Internal disassembly/interop transcripts stay under this chat's work directory and are excluded from packages. Compilation, signature checks, state/capacity tests and native gameplay evidence are separate levels. All native live checks in TEST-PLAN.md are pending.

