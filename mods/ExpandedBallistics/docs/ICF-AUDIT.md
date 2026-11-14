# 0.22.47 validation (prepared only)

Current source re-audit: 290 Harmony annotations, 139 native targets, three documented shared annotations and the shared-address negative control pass. Compiled comparison against 0.22.46: all 649 Harmony attributes unchanged; no new hook. GameAssembly SHA256 verified unchanged by Audit-Module.ps1. Historical rationale follows; it is not live acceptance of the new factor arithmetic.

# 0.22.38: no new hook; compiled attributes equal installed 0.22.37. Current ICF audit follows.
# 0.22.37: no new patch or target; compiled inventory equals installed 0.22.36. Current source addresses re-audited below.
# 0.22.36 validation

No hook or target changed from installed 0.22.35. This task reran the ICF inventory and negative shared-address control. The following sections retain the historical rationale, not new live validation.

# 0.22.35 validation (historical)

No target or hook kind changed from the installed 0.22.34. Current source audit: 290 annotations / 139 native methods; shared-address negative control passes. Live GameAssembly SHA256 is unchanged. The retained historical rationale follows.

# 0.22.32 validation

No patch target or hook kind changed from src-0.22.31. This task reran the source-extracted ICF audit; 290 annotations / 139 native targets and the negative shared-address control pass. The historical rationale below remains in force.

# 0.22.31 carry-forward validation

All patch targets are unchanged from 0.22.29. The 0.22.31 source-extracted audit revalidated 290 annotations / 139 native targets; 3 shared mesh annotations retain the documented no-argument exception. The address/risk inventory below is carried forward, with the shared mesh rationale updated for authorized reversible visibility restoration and is not a claim of new in-game validation.

# ICF audit â€” Expanded Ballistics 0.22.29

290 source patch annotations resolve to 139 distinct native methods. Three additional optional managed targets are listed below. All targets are potential registrations, including optional/disabled patch owners.

GameAssembly SHA256: 3E58082804D97CF32FFD400E7427E767EF9576D23DB71B2BE96FBB7F825FC17B (read from the live file).
Primary dump (EB_NATIVE_METHODS): `all-native-methods.json`; SHA256 `B1D673AA2DD9EDF3E15BE9EA0F311AD3E336FD1C07D6D1F0660D95E8C2519C8F`.
The primary dump covers only Sprocket*.dll. Cross-assembly audit therefore also reads all 116,538 AddressAttribute records from the local Cpp2IL output via `Export-NativeAudit.ps1`; SHA256 `2FBAE24157ACE6F79E758BA0EDD66A737455FDAA65238195635BDDFB773D5CB1`.
No native dump or game DLL is part of the install payload. AudioSource and UI targets omitted from the primary Sprocket-only dump are included in the cross-assembly audit.

## Findings and fixes

- Baseline 0.22.28 has all three postfixes on OnAssignedMeshModified at 0x1E7D0A0 (14 bytes). All three now select RequestMeshRefresh(), with an explicit null guard and only PlateStructure __instance in the managed callback. Both folded methods belong to that exact class.
- Additional unsafe baseline target found: RuntimeAtgmGimbalPreset.ReleaseMotorModel patched VehicleComponent.Release(), RVA 0x54D0D0, 3 bytes, shared with 787 other records (788 records total). Changed to the unique VehicleComponent.ReleaseInternal() caller. Callback only removes exact-pointer entries from two managed dictionaries.
- Final audit: one distinct shared target / three annotations, explicitly accepted as requested below; no undocumented shared target.
- No new high-risk gameplay hook or risk-alternative implementation was added.

## Native targets

| Exact native signature | RVA | Length (hex bytes) | Source owners | Other methods at same address / safety |
|---|---|---|---|---|
| `Sprocket.AssetManagement.IAssetInfo[] Sprocket.PartImporting.PartDefinitionCardFactory::GetAllWithTags(System.String[])` | 0x200FB90 | 0x131 | Material/EraMenuPresentation.cs | None (unique) |
| `Sprocket.CameraInputProccessingFlags Sprocket.ScopeCameraController::ProcessInputs(System.Single,Sprocket.CameraInputProccessingFlags)` | 0x1CCAAB0 | 0x302 | RuntimeAtgmGuidanceSight.cs | None (unique) |
| `Sprocket.DamageModelling.DamageModelHitResult Sprocket.DamageModelling.ArmourPiercingProjectileFunction::HitDamageModel(Sprocket.DamageModelling.ProjectileInstance&,Sprocket.DamageModelling.ProjectileDamageModelHit&)` | 0x1EE02D0 | 0xCC1 | RuntimeDamageFeed.cs, RuntimeShellSelection.cs, RuntimeTandem.cs | None (unique) |
| `Sprocket.DamageModelling.DamageModelHitResult Sprocket.DamageModelling.ExplosiveProjectileFunction::HitDamageModel(Sprocket.DamageModelling.ProjectileInstance&,Sprocket.DamageModelling.ProjectileDamageModelHit&)` | 0x1EE1C80 | 0x11B | RuntimeDamageFeed.cs, RuntimeShellSelection.cs | None (unique) |
| `Sprocket.DamageModelling.IPenetrationSimulation Sprocket.DamageModelling.CompoundStructure::Penetrate(Sprocket.DamageModelling.PenetratorInfo&,Sprocket.DamageModelling.PenetrationSimulationOptions)` | 0x1D75550 | 0x7A4 | RuntimeArmourComparisonCapture.cs, RuntimeArmourComparisonParity.cs, RuntimeArmourSimulator.cs, RuntimeSpall.cs, RuntimeTandem.cs | None (unique) |
| `Sprocket.DamageModelling.MaterialBlock Sprocket.DamageModelling.StructureIntersection::GetNextMaterialBlock(Sprocket.DamageModelling.StructureIntersection[],System.Int32,System.Byte[],System.Int16,Sprocket.DamageModelling.ObjectInfo[],Sprocket.DamageModelling.MaterialInfo[])` | 0x1D81A40 | 0x58E | RuntimeArmourComparisonCapture.cs, RuntimeArmourResponses.cs, RuntimeDamageFeed.cs, RuntimeSpall.cs | None (unique) |
| `Sprocket.DamageModelling.PhysicsObjectHitResult Sprocket.DamageModelling.ExplosiveProjectileFunction::HitPhysicsObject(Sprocket.DamageModelling.ProjectileInstance&,UnityEngine.RaycastHit&)` | 0x1EE1DA0 | 0x4A | RuntimeShellSelection.cs | None (unique) |
| `Sprocket.IDurable Sprocket.Vehicles.VehicleHealthRegister::CreateHealthPoolInternal(Sprocket.Vehicles.VehicleComponent,System.Single)` | 0x1FCE810 | 0x17C | RuntimeDamageFeed.cs | None (unique) |
| `Sprocket.PartImporting.PartDisplayCard Sprocket.PartImporting.PartDefinitionCardFactory::CreateCard(Sprocket.PartImporting.PartDefinition)` | 0x200FA10 | 0x171 | AtgmAmmoBoxVisuals.cs, AtgmGuidanceSightVisuals.cs, Material/EraMenuPresentation.cs, Material/EraPartsTab.cs, Material/ReliktIcon.cs, RuntimeAtgmLauncher.cs, SightUiIntegration.cs | None (unique) |
| `Sprocket.PointerOperators.PointerInputHandleResult Sprocket.VehicleDesigner.ArmourEditing.ArmourOverlayPointerOperator::Update(Sprocket.PointerOperators.IPointerOperatorContext,Sprocket.PointerOperators.PointerInputHandleResult)` | 0x1F90FC0 | 0x94C | RuntimeArmourSimulator.cs | None (unique) |
| `Sprocket.PointerOperators.PointerInputHandleResult Sprocket.VehicleDesigner.VehicleEditorOverlayControlPointerOperator::Update(Sprocket.PointerOperators.IPointerOperatorContext,Sprocket.PointerOperators.PointerInputHandleResult)` | 0x1F8C8D0 | 0xDE | RuntimeShellEra.cs | None (unique) |
| `Sprocket.TechTrees.ITechFrame Sprocket.TechTrees.TechTreeLoader::GetTechFrameAtDate(Sprocket.TechTrees.TechDate)` | 0x1F1E7B0 | 0x78 | RuntimeLiveTechnology.cs | None (unique) |
| `Sprocket.VehicleDesigner.UI.OverlayInstanceConfig Sprocket.VehicleDesigner.UI.ArmourOverlayConfigFactory::Create(Sprocket.Vehicles.VehicleOverlay,Sprocket.VehicleDesigner.Access.IVehicleEditor)` | 0x1FA55D0 | 0x13B | RuntimeShellEra.cs | None (unique) |
| `Sprocket.Vehicles.ArmourOverlaySampleSettings Sprocket.VehicleDesigner.ArmourEditing.ArmourOverlay::get_SampleSettings()` | 0x1F90C60 | 0xC5 | RuntimeArmourSimulator.cs | None (unique) |
| `Sprocket.Vehicles.PlateStructures.PlateStructure Sprocket.Vehicles.PlateStructures.Design.PlateStructureDesigner::NewStructure(System.String,System.Boolean,System.Int32)` | 0x1E8AC00 | 0x254 | RuntimeCompositePlate.cs | None (unique) |
| `Sprocket.Vehicles.Serialization.VehicleObjectBlueprint Sprocket.Vehicles.VehicleObjectSerialization::ToBlueprint(Sprocket.Vehicles.VehicleObject,Sprocket.StateContext)` | 0x1FEC120 | 0x5BC | RuntimeAtgmAmmoBox.cs | None (unique) |
| `Sprocket.Vehicles.VehicleDirtyFlags Sprocket.Vehicles.Vehicle::Build()` | 0x2090E70 | 0xA46 | Material/MaterialFeature.cs | None (unique) |
| `Sprocket.Vehicles.VehicleObject Sprocket.VehicleDesigner.Operations.VehicleOperations::InstantiateAndBeginPlacement(System.String,System.Int32)` | 0x1FB5B90 | 0x2C9 | Material/EraPlacementDefaults.cs | None (unique) |
| `Sprocket.Vehicles.VehicleObject Sprocket.Vehicles.VehicleObject::Duplicate(Sprocket.Vehicles.VehicleObject,Sprocket.Vehicles.DuplicateOptions)` | 0x1FD8970 | 0x1F5 | Material/ReliktPrototype.cs | None (unique) |
| `Sprocket.WeaponFramework.WeaponFireInfo Sprocket.Vehicles.Weapons.Cannons.CannonBehaviour::FireInternal(Sprocket.DamageModelling.ProjectileTypeID)` | 0x210E320 | 0x6EC | RuntimeAmmoFireGuard.cs, RuntimeLaunchAudio.cs | None (unique) |
| `System.Boolean Sprocket.Vehicles.AmmunitionStorage.AmmoRackBehaviour::TakeShell()` | 0x1C98710 | 0x90 | RuntimeMixedAmmoStorage.cs | None (unique) |
| `System.Boolean Sprocket.Vehicles.Weapons.AmmoRequest::IsMatch(Sprocket.WeaponFramework.IAmmoSource)` | 0x210CF40 | 0xFE | RuntimeMixedAmmoStorage.cs | None (unique) |
| `System.Boolean Sprocket.WeaponFramework.WeaponBehaviour::get_CanFire()` | 0x210C3D0 | 0x10D | RuntimeAmmoFireGuard.cs, RuntimeAtgmLauncher.cs | None (unique) |
| `System.Object Sprocket.Vehicles.Cannons.CannonBlueprint::Clone()` | 0x2036CE0 | 0x182 | RuntimeAmmoLoadout.cs, RuntimeShellSelection.cs | None (unique) |
| `System.Single Sprocket.ProjectilePhysicsSystems.ShellProperties::GetDamage(System.Single,System.Single)` | 0x1D82F10 | 0x73 | RuntimeShellSelection.cs | None (unique) |
| `System.Single Sprocket.WeaponFramework.WeaponBehaviour::get_LoaderEnableFraction()` | 0x210C250 | 0x14A | RuntimeAtgmAmmoBox.cs | None (unique) |
| `System.Threading.Tasks.Task Sprocket.VehicleDesigner.VehicleComponentSelectionDisplayEditorPlugin::Release()` | 0x1FA2AF0 | 0x4A | Material/EraPartsTab.cs | None (unique) |
| `System.UInt16 Sprocket.Vehicles.AmmunitionStorage.AmmoRackBehaviour::LoadAmmo(System.UInt16)` | 0x1C987A0 | 0xF2 | RuntimeMixedAmmoStorage.cs | None (unique) |
| `System.Void DesignerFramework.UI.SelectionPanel::ClearAll()` | 0x1F9DEE0 | 0xA0 | Material/EraPartsTab.cs | None (unique) |
| `System.Void DesignerFramework.UI.SelectionPanel::OnClickSelection(System.Int32)` | 0x1F9F1E0 | 0x6B | Material/EraPartsTab.cs | None (unique) |
| `System.Void DesignerFramework.UI.SelectionPanel::OnClickTab(System.Int32)` | 0x1F9F1A0 | 0x31 | Material/EraPartsTab.cs | None (unique) |
| `System.Void DesignerFramework.UI.SelectionPanel::Release()` | 0x1F9DEB0 | 0x27 | Material/EraPartsTab.cs | None (unique) |
| `System.Void DesignerFramework.UI.SelectionPanel::SetDisplayOptions(Sprocket.Selection.ISelectionGroup[],System.Int32)` | 0x1F9E1E0 | 0x6EC | Material/EraPartsTab.cs, SightUiIntegration.cs | None (unique) |
| `System.Void DynamicGUI.DynamicDropdown::SetOptions(System.Collections.Generic.IEnumerable`1<System.String>,System.Int32)` | 0x6AFD60 | 0x48B | RuntimeSimulatorScroll.cs | None (unique) |
| `System.Void DynamicGUI.PropertyFieldSlider::FinishEdit()` | 0x6B6660 | 0x2B | RuntimeAmmoLoadout.cs | None (unique) |
| `System.Void DynamicGUI.PropertyFieldSlider::OnSliderBeginDrag()` | 0x6B5CB0 | 0xD9 | RuntimeAmmoLoadout.cs | None (unique) |
| `System.Void DynamicGUI.PropertyFieldSlider::OnSliderEndDrag()` | 0x6B5D90 | 0x82 | RuntimeAmmoLoadout.cs | None (unique) |
| `System.Void DynamicGUI.PropertyFieldSlider::StartEdit(DynamicGUI.PropertyFieldSlider/EditMode)` | 0x6B6610 | 0x43 | RuntimeAmmoLoadout.cs | None (unique) |
| `System.Void Sprocket.ArtificialIntelligence.GunLayerAI::Step(UnityEngine.Vector3,System.Single)` | 0x201FF10 | 0x495 | RuntimeAtgmAi.cs | None (unique) |
| `System.Void Sprocket.DamageModelling.CompoundStructure::CreateSpallBurst(Sprocket.DamageModelling.PenetrationSimulation,Sprocket.DamageModelling.CompoundStructure/SpallSpawnBurst&)` | 0x1D78FC0 | 0x2AD | RuntimeSpall.cs | None (unique) |
| `System.Void Sprocket.DamageModelling.CompoundStructure::SimulateFragment(Sprocket.DamageModelling.PenetrationSimulation,System.Int16)` | 0x1D777A0 | 0x1813 | RuntimeArmourComparisonCapture.cs, RuntimeArmourResponses.cs, RuntimeDamageFeed.cs, RuntimeOriginalTrajectory.cs, RuntimeSpall.cs | None (unique) |
| `System.Void Sprocket.DamageModelling.ProjectileRegister::DestroyAll()` | 0x1EE6340 | 0x209 | RuntimeAtgm.cs, RuntimeAtgmTrail.cs, RuntimeShellSelection.cs, RuntimeTandem.cs | None (unique) |
| `System.Void Sprocket.DamageModelling.ProjectileRegister::FixedUpdate()` | 0x1EE5CB0 | 0x687 | RuntimeAtgm.cs, RuntimeAtgmTrail.cs | None (unique) |
| `System.Void Sprocket.DamageModelling.ProjectileRegister::Launch(System.Object,Sprocket.DamageModelling.ProjectileLaunchState&,Sprocket.DamageModelling.ProjectileLauncherInfo&,Sprocket.DamageModelling.ProjectileTypeID,Sprocket.DamageModelling.ProjectileFireInfo&)` | 0x1EE52D0 | 0x744 | RuntimeAtgm.cs, RuntimeDamageFeed.cs, RuntimeShellSelection.cs | None (unique) |
| `System.Void Sprocket.DamageModelling.ProjectileRegister::Release(System.Int32)` | 0x1EE5A40 | 0x262 | RuntimeAtgm.cs, RuntimeAtgmTrail.cs, RuntimeTandem.cs | None (unique) |
| `System.Void Sprocket.Gameplay.VehicleControl.VehicleController::UpdateControl(Sprocket.GameTime,UnityEngine.Ray,Sprocket.Gameplay.VehicleControl.IVehicleControlInputs,UnityEngine.Camera)` | 0x1F5C4F0 | 0x890 | RuntimeAmmoLoadout.cs, RuntimeAtgm.cs, RuntimeAtgmGuidanceSight.cs, RuntimeDamageFeed.cs, RuntimeSightIntegration.cs | None (unique) |
| `System.Void Sprocket.PlayerControl.Player::GameLateUpdate(Sprocket.GameTime)` | 0x1E9E050 | 0x101 | Material/ReliktPrototype.cs, Material/ReliktSpentVisuals.cs, RuntimeCassettePlateVisual.cs | None (unique) |
| `System.Void Sprocket.VehicleDesigner.Operations.Place.PlaceOperation::UpdateStart(System.Boolean)` | 0x1FBEA90 | 0x28E | Material/EraPlacementDefaults.cs | None (unique) |
| `System.Void Sprocket.VehicleDesigner.UI.ArmourOverlayConfig::Draw(Sprocket.UI.IGUILayout)` | 0x1FA66B0 | 0x316 | RuntimeArmourSimulator.cs, RuntimeCassettePlateVisual.cs | None (unique) |
| `System.Void Sprocket.VehicleDesigner.UI.OverlayConfigPlugin::Draw(Sprocket.VehicleDesigner.UI.OverlayInstanceConfig)` | 0x1FABC00 | 0x9F | RuntimeSimulatorScroll.cs | None (unique) |
| `System.Void Sprocket.VehicleDesigner.UI.OverlayConfigPlugin::LateUpdate(Sprocket.GameTime,Sprocket.VehicleDesigner.Access.EditorLateUpdateContext)` | 0x1FABBB0 | 0x4C | RuntimeShellEra.cs | None (unique) |
| `System.Void Sprocket.VehicleDesigner.VehicleComponentSelectionDisplay::CreatePlugins(Sprocket.VehicleDesigner.Access.IVehicleEditorPluginLoader,Sprocket.VehicleDesigner.Access.EditorPluginLoadContext)` | 0x1FA2630 | 0x295 | Material/EraPartsTab.cs | None (unique) |
| `System.Void Sprocket.VehicleDesigner.VehicleEditor::ApplyOverlay(Sprocket.Vehicles.VehicleOverlay)` | 0x1F87BF0 | 0x36 | RuntimeCassettePlateVisual.cs, RuntimeShellEra.cs | None (unique) |
| `System.Void Sprocket.VehicleDesigner.VehicleEditor::Detach()` | 0x1F89890 | 0x247 | Material/EraPartsTab.cs, RuntimeShellEra.cs | None (unique) |
| `System.Void Sprocket.VehicleEditorScenarioGameState::OnPlayerStateChanged(System.Object,Sprocket.PlayerControl.PlayerStateChangeEventArgs)` | 0x1EF6510 | 0x19D | Material/ReliktSpentVisuals.cs, RuntimeCassettePlateVisual.cs, RuntimeDamageFeed.cs | None (unique) |
| `System.Void Sprocket.Vehicles.AllOperablesValidCriterion::Validate(Sprocket.Validation.IValidationIssueCollection)` | 0x1F781C0 | 0x360 | RuntimeAtgmAmmoBox.cs, RuntimeAtgmInitialRound.cs | None (unique) |
| `System.Void Sprocket.Vehicles.AmmunitionStorage.AmmoRack::Build()` | 0x1C96D40 | 0xD5A | AtgmAmmoBoxVisuals.cs, RuntimeAtgmAmmoBox.cs | None (unique) |
| `System.Void Sprocket.Vehicles.AmmunitionStorage.AmmoRackBehaviour::ApplyDamage(System.Single,System.Single)` | 0x1C98930 | 0x13F | RuntimeMixedAmmoStorage.cs | None (unique) |
| `System.Void Sprocket.Vehicles.AmmunitionStorage.AmmoRackEditor::OnGUI(Sprocket.UI.IGUILayout)` | 0x1C9F7A0 | 0x1E | RuntimeAtgmAmmoBox.cs | None (unique) |
| `System.Void Sprocket.Vehicles.AmmunitionStorage.AmmoRackModel::ApplyRendererSettings()` | 0x1C9A0D0 | 0x498 | AtgmAmmoBoxVisuals.cs | None (unique) |
| `System.Void Sprocket.Vehicles.AmmunitionStorage.AmmoRackModel::ApplyRenderers(System.Int32,System.Single,System.Single)` | 0x1C9AB60 | 0x47D | AtgmAmmoBoxVisuals.cs | None (unique) |
| `System.Void Sprocket.Vehicles.Cannons.Cannon::Build()` | 0x2030C50 | 0x497 | RuntimeAtgmAi.cs, RuntimeAtgmAmmoBox.cs, RuntimeAtgmGuidanceSight.cs, RuntimeAtgmInitialRound.cs, RuntimeAtgmLauncher.cs, RuntimeShellSelection.cs | None (unique) |
| `System.Void Sprocket.Vehicles.Cannons.Cannon::DisableBehaviour()` | 0x2030B30 | 0x110 | RuntimeAtgmAi.cs, RuntimeAtgmAmmoBox.cs | None (unique) |
| `System.Void Sprocket.Vehicles.Cannons.Cannon::EnableBehaviour(Sprocket.Vehicles.IBehaviourEnableContext)` | 0x20301F0 | 0x871 | RuntimeAtgmAmmoBox.cs | None (unique) |
| `System.Void Sprocket.Vehicles.Cannons.Cannon::OnVehicleBuilt(Sprocket.Vehicles.VehicleDirtyFlags)` | 0x20310F0 | 0x6F | RuntimeAtgmGuidanceSight.cs, RuntimeAtgmLauncher.cs | None (unique) |
| `System.Void Sprocket.Vehicles.Cannons.Cannon::Release()` | 0x202FF60 | 0x12F | RuntimeAtgmGuidanceSight.cs, RuntimeAtgmInitialRound.cs, RuntimeAtgmLauncher.cs | None (unique) |
| `System.Void Sprocket.Vehicles.Cannons.Cannon::UpdateAssociatedSight(Sprocket.Vehicles.Cannons.CannonInstanceBlueprint)` | 0x2031160 | 0xAB4 | RuntimeAtgmGuidanceSight.cs | None (unique) |
| `System.Void Sprocket.Vehicles.Cannons.CannonBlueprint::Load(Sprocket.Blueprints.IBlueprintDeserializer,Sprocket.FileVersion,Sprocket.Blueprints.BlueprintLoadContext)` | 0x20374A0 | 0x30 | RuntimeAmmoLoadout.cs, RuntimeShellSelection.cs | None (unique) |
| `System.Void Sprocket.Vehicles.Cannons.CannonBlueprint::Save(Sprocket.Blueprints.IBlueprintSerializer,Sprocket.Blueprints.BlueprintSaveContext)` | 0x20374D0 | 0x223 | RuntimeAmmoLoadout.cs, RuntimeShellSelection.cs | None (unique) |
| `System.Void Sprocket.Vehicles.Cannons.Editor.CannonEditor::OnGUI(Sprocket.UI.IGUILayout)` | 0x20418E0 | 0xB31 | RuntimeShellSelection.cs | None (unique) |
| `System.Void Sprocket.Vehicles.Cannons.Editor.CannonProperties::OnRebuilt(Sprocket.Vehicles.VehicleComponent)` | 0x2046C30 | 0x62C | RuntimeShellSelection.cs | None (unique) |
| `System.Void Sprocket.Vehicles.CrewSystems.GunLayer::AimAtDirection(UnityEngine.Vector3,Sprocket.VehicleControl.GunLayerAimFlags)` | 0x1D5F9E0 | 0x18 | RuntimeAtgmGuidanceSight.cs | None (unique) |
| `System.Void Sprocket.Vehicles.CrewSystems.GunLayer::AimAtPosition(UnityEngine.Vector3,Sprocket.VehicleControl.GunLayerAimFlags)` | 0x1D5FA00 | 0x18 | RuntimeAtgmGuidanceSight.cs | None (unique) |
| `System.Void Sprocket.Vehicles.CrewSystems.GunLayer::ExecuteUpdateEnd(Sprocket.Vehicles.CrewSystems.SharedTaskResources&)` | 0x1D5FA60 | 0x2DD | RuntimeAtgmGuidanceSight.cs | None (unique) |
| `System.Void Sprocket.Vehicles.Fires.MuzzleFlashEffect::Setup(Sprocket.WeaponFramework.CannonMuzzleFlashEffectInfo,UnityEngine.Transform,UnityEngine.Vector3,UnityEngine.Quaternion,System.Boolean)` | 0x2104760 | 0xAF3 | RuntimeLaunchAudio.cs | None (unique) |
| `System.Void Sprocket.Vehicles.PlateStructures.Design.PlateStructureDesigner::GetSelection(Sprocket.Selection.ISelectionBuffer)` | 0x1E8B510 | 0x5D | RuntimeAtgmGimbalPreset.cs, RuntimeCompositePlate.cs | None (unique) |
| `System.Void Sprocket.Vehicles.PlateStructures.Design.PlateStructureDesigner::LoadPrefabPlateStructure(System.String)` | 0x1E8A610 | 0x185 | RuntimeCompositePlate.cs | None (unique) |
| `System.Void Sprocket.Vehicles.PlateStructures.Design.PlateStructureDesigner::Release()` | 0x1E8A5E0 | 0x26 | RuntimeAtgmGimbalPreset.cs | None (unique) |
| `System.Void Sprocket.Vehicles.PlateStructures.Design.PlateStructureEditor::OnGUI(Sprocket.UI.IGUILayout)` | 0x1E8F5B0 | 0x20B | Material/MaterialFeature.cs, RuntimeCompositePlate.cs, RuntimeIndependentStructureUi.cs, RuntimePlateStackUi.cs | None (unique) |
| `System.Void Sprocket.Vehicles.PlateStructures.PlateStructure::Build()` | 0x1E7D0B0 | 0x4C7 | Material/RuntimeMaterialBalance.cs, RuntimeCassetteComposer.cs, RuntimeCassettePlateVisual.cs | None (unique) |
| `System.Void Sprocket.Vehicles.PlateStructures.PlateStructure::Release()` | 0x1E7CB10 | 0x33B | RuntimeArmourResponses.cs, RuntimeCassetteComposer.cs, RuntimeCassettePlateVisual.cs | None (unique) |
| `System.Void Sprocket.Vehicles.PlateStructures.PlateStructure::RequestMeshRefresh()` | 0x1E7D0A0 | 0xE | NativeCassetteGeometry.cs, RuntimeCassettePlateVisual.cs, RuntimeCompositePlate.cs | System.Void Sprocket.Vehicles.PlateStructures.PlateStructure::OnAssignedMeshModified(System.Object,System.EventArgs)<br>ALLOW-ICF-MESH: only the no-argument this trampoline; all three callbacks accept PlateStructure __instance only. Both callers have this exact class; instance/null guards restrict cache invalidation, queued refresh and restoration of tracked renderer visibility. No event arguments or game-data writes; 0.22.31 permits only reversible forceRenderingOff writes. |
| `System.Void Sprocket.Vehicles.PlateStructures.PlateStructure::SetArmourMaterial(System.String)` | 0x1E7D580 | 0x384 | Material/RuntimeMaterialBalance.cs, RuntimeCassetteComposer.cs | None (unique) |
| `System.Void Sprocket.Vehicles.PlateStructures.PlateStructure::SyncWithArmourTech(Sprocket.TechTrees.IReadOnlyTech)` | 0x1E7D910 | 0x150 | Material/RuntimeMaterialBalance.cs | None (unique) |
| `System.Void Sprocket.Vehicles.PlateStructures.PlateStructureMesh::FromJson(System.String)` | 0x1E82E30 | 0x9F | NativeCassetteGeometry.cs, RuntimeCompositePlate.cs | None (unique) |
| `System.Void Sprocket.Vehicles.PlateStructures.PlateStructureMesh::LoadBlueprint(Sprocket.Vehicles.MeshGeneration.PlateStructureMeshBlueprint)` | 0x1E829D0 | 0x28A | NativeCassetteGeometry.cs, RuntimeCompositePlate.cs | None (unique) |
| `System.Void Sprocket.Vehicles.ScopeController::ActivateSight(Sprocket.WeaponFramework.IGunnerSight)` | 0x1F59200 | 0x2C9 | RuntimeAtgmGuidanceSight.cs, RuntimeAtgmLauncherOptics.cs | None (unique) |
| `System.Void Sprocket.Vehicles.ScopeController::Dispose()` | 0x1F59990 | 0xBF | RuntimeAtgm.cs, RuntimeAtgmGuidanceSight.cs | None (unique) |
| `System.Void Sprocket.Vehicles.ScopeController::SetAimPoint(UnityEngine.Vector3,UnityEngine.Vector3)` | 0x1F595B0 | 0x56 | RuntimeAtgm.cs, RuntimeAtgmGuidanceSight.cs | None (unique) |
| `System.Void Sprocket.Vehicles.ScopeController::SyncToSight(Sprocket.WeaponFramework.IGunnerSight)` | 0x1F597C0 | 0x18F | RuntimeAtgmGuidanceSight.cs | None (unique) |
| `System.Void Sprocket.Vehicles.ScopeController::Update()` | 0x1F594D0 | 0xC7 | RuntimeAtgm.cs, RuntimeAtgmGuidanceSight.cs | None (unique) |
| `System.Void Sprocket.Vehicles.Turrets.TraverseMotor::DisableBehaviour()` | 0x1E18390 | 0x5A | RuntimeAtgmGuidanceSight.cs | None (unique) |
| `System.Void Sprocket.Vehicles.Turrets.TraverseMotor::EnableBehaviour(Sprocket.Vehicles.IBehaviourEnableContext)` | 0x1E17D50 | 0x564 | RuntimeAtgmGuidanceSight.cs | None (unique) |
| `System.Void Sprocket.Vehicles.Turrets.TraverseMotorModelComponent::Build(Sprocket.Vehicles.Turrets.TraverseMotorModelInfo&)` | 0x1E1CD20 | 0xF1B | RuntimeAtgmGimbalPreset.cs | None (unique) |
| `System.Void Sprocket.Vehicles.Turrets.TurretRing::Build()` | 0x1F44590 | 0xDE0 | RuntimeAtgmGimbalPreset.cs | None (unique) |
| `System.Void Sprocket.Vehicles.Turrets.TurretRing::ValidateMotorPosition(Sprocket.Vehicles.Turrets.TraverseMotor)` | 0x1F456E0 | 0x259 | RuntimeAtgmGimbalPreset.cs | None (unique) |
| `System.Void Sprocket.Vehicles.VehicleBehaviour::Release()` | 0x1FCCD40 | 0x1E9 | RuntimeAmmoLoadout.cs, RuntimeAtgmInitialRound.cs | None (unique) |
| `System.Void Sprocket.Vehicles.VehicleComponent::LoadData(System.Runtime.Serialization.SerializationInfo,Sprocket.StateContext)` | 0x1FD1820 | 0x75 | Material/ReliktPrototype.cs, RuntimeAtgmAmmoBox.cs | None (unique) |
| `System.Void Sprocket.Vehicles.VehicleComponent::ReleaseInternal()` | 0x1FD0FA0 | 0x313 | AtgmAmmoBoxVisuals.cs, RuntimeAtgmAmmoBox.cs, RuntimeAtgmGimbalPreset.cs | None (unique) |
| `System.Void Sprocket.Vehicles.VehicleHealthRegister::LinkHealthPool(Sprocket.IDurable,System.Int32)` | 0x1FCEB30 | 0x26 | RuntimeDamageFeed.cs | None (unique) |
| `System.Void Sprocket.Vehicles.VehicleHealthRegister::OnHealthChanged(Sprocket.IDurable,Sprocket.HealthChangeInfo)` | 0x1FCE990 | 0x16 | RuntimeDamageFeed.cs | None (unique) |
| `System.Void Sprocket.Vehicles.VehicleHealthRegister::Release()` | 0x1FCE9B0 | 0x17D | RuntimeDamageFeed.cs | None (unique) |
| `System.Void Sprocket.Vehicles.VehicleHealthRegister::Sprocket.DamageModelling.IDamageApplier.HealthDelta(System.Int32,System.Single,Sprocket.DamageModelling.DamageModelDamageModifiers)` | 0x1FCED20 | 0x186 | RuntimeDamageFeed.cs | None (unique) |
| `System.Void Sprocket.Vehicles.VehicleHealthRegister::Sprocket.DamageModelling.IDamageApplier.HealthDelta(System.Single,Sprocket.DamageModelling.DamageModelDamageModifiers)` | 0x1FCEEB0 | 0x187 | RuntimeDamageFeed.cs | None (unique) |
| `System.Void Sprocket.Vehicles.VehicleHealthRegister::Update()` | 0x1FCEB60 | 0x1B5 | RuntimeDamageFeed.cs | None (unique) |
| `System.Void Sprocket.Vehicles.VehicleObject::Release()` | 0x1FD7680 | 0x8C | RuntimeArmourResponses.cs | None (unique) |
| `System.Void Sprocket.Vehicles.VehicleObjectModel::Build()` | 0x209DE90 | 0x9A8 | AtgmGuidanceSightVisuals.cs, Material/ReliktPrototype.cs, RuntimeCassettePlateVisual.cs | None (unique) |
| `System.Void Sprocket.Vehicles.VehicleObjectModel::BuildDamageModelColliders(Sprocket.Vehicles.Colliders.IVehicleComponentDamageModelBuilder)` | 0x209EC30 | 0x2D | Material/ReliktPrototype.cs | None (unique) |
| `System.Void Sprocket.Vehicles.VehicleObjectModel::ForceRebuild()` | 0x209E920 | 0x7 | Material/ReliktPrototype.cs | None (unique) |
| `System.Void Sprocket.Vehicles.VehicleObjectModel::OnModelLoaded(System.Object,Sprocket.AssetManagement.AssetLoadCompleteEventArgs`1<Sprocket.Vehicles.IVehicleModel>)` | 0x209D760 | 0x17A | AtgmGuidanceSightVisuals.cs, Material/ReliktPrototype.cs, Material/ReliktSpentVisuals.cs | None (unique) |
| `System.Void Sprocket.Vehicles.VehicleObjectModel::Release()` | 0x209E930 | 0x52 | AtgmGuidanceSightVisuals.cs, Material/ReliktPrototype.cs, Material/ReliktSpentVisuals.cs, RuntimeArmourResponses.cs | None (unique) |
| `System.Void Sprocket.Vehicles.VehicleObjectModel::SetVisible(System.Boolean)` | 0x209EB40 | 0x69 | AtgmGuidanceSightVisuals.cs, Material/ReliktSpentVisuals.cs | None (unique) |
| `System.Void Sprocket.Vehicles.VehicleObjectModel::set_ModelScale(UnityEngine.Vector3)` | 0x209C120 | 0x1A6 | Material/ReliktPrototype.cs | None (unique) |
| `System.Void Sprocket.Vehicles.VehicleTransform::CopyFrom(Sprocket.Vehicles.VehicleTransform)` | 0x1FE6CD0 | 0xB9 | Material/ReliktPrototype.cs | None (unique) |
| `System.Void Sprocket.Vehicles.VehicleTransform::LoadState(System.Runtime.Serialization.SerializationInfo,Sprocket.StateContext)` | 0x1FE5600 | 0x2E9 | Material/ReliktPrototype.cs | None (unique) |
| `System.Void Sprocket.Vehicles.VehicleTransform::OnChildTransformChanged(Sprocket.Vehicles.TransformChangeType,Sprocket.Vehicles.VehicleTransform)` | 0x1FE3280 | 0x136 | Material/ReliktPrototype.cs | None (unique) |
| `System.Void Sprocket.Vehicles.VehicleTransform::OnDuplicate(Sprocket.Vehicles.VehicleTransform)` | 0x1FE5380 | 0x19 | Material/ReliktPrototype.cs | None (unique) |
| `System.Void Sprocket.Vehicles.VehicleTransform::OnParentTransformChanged(Sprocket.Vehicles.TransformChangeType)` | 0x1FE3010 | 0x266 | Material/ReliktPrototype.cs | None (unique) |
| `System.Void Sprocket.Vehicles.VehicleTransform::OnTransformChanged(Sprocket.Vehicles.TransformChangeType)` | 0x1FE2D20 | 0xF0 | Material/ReliktPrototype.cs | None (unique) |
| `System.Void Sprocket.Vehicles.VehicleTransform::ReclampScale()` | 0x1FE51E0 | 0x193 | Material/ReliktPrototype.cs | None (unique) |
| `System.Void Sprocket.Vehicles.VehicleTransform::SetMirrors(Sprocket.Vehicles.VehicleTransform,Sprocket.Vehicles.VehicleTransform)` | 0x1FE59E0 | 0x3D2 | Material/ReliktPrototype.cs | None (unique) |
| `System.Void Sprocket.Vehicles.VehicleTransform::set_Scale(UnityEngine.Vector3)` | 0x1FE2340 | 0x229 | Material/ReliktPrototype.cs | None (unique) |
| `System.Void Sprocket.Vehicles.Weapons.LoadTask::Cancel()` | 0x210D8A0 | 0x66 | RuntimeAmmoFireGuard.cs, RuntimeMixedAmmoStorage.cs | None (unique) |
| `System.Void Sprocket.Vehicles.Weapons.LoadTask::LoadInstantly()` | 0x210D840 | 0x51 | RuntimeAmmoLoadout.cs, RuntimeAtgmInitialRound.cs, RuntimeMixedAmmoStorage.cs | None (unique) |
| `System.Void Sprocket.Vehicles.Weapons.LoadTask::SetState(Sprocket.WeaponFramework.LoadState)` | 0x210D980 | 0x1D0 | RuntimeMixedAmmoStorage.cs | None (unique) |
| `System.Void Sprocket.Vehicles.Weapons.LoadTask::Unload()` | 0x210D910 | 0x66 | RuntimeAmmoFireGuard.cs, RuntimeMixedAmmoStorage.cs | None (unique) |
| `System.Void Sprocket.Vehicles.Weapons.LoadTask::Update(System.Single,System.Single,System.Single,System.Single)` | 0x210D730 | 0x104 | RuntimeAmmoLoadout.cs, RuntimeAtgmAmmoBox.cs, RuntimeMixedAmmoStorage.cs | None (unique) |
| `System.Void Sprocket.Vehicles.Weapons.LoadTask::UpdateRequest()` | 0x210DB90 | 0x3D5 | RuntimeAtgmAmmoBox.cs, RuntimeMixedAmmoStorage.cs | None (unique) |
| `System.Void Sprocket.Vehicles.Weapons.ProjectileEffectConfig::PlayEffect(Sprocket.DamageModelling.ProjectileEffectInfo)` | 0x21015E0 | 0x2F3 | RuntimeShellEffects.cs | None (unique) |
| `System.Void Sprocket.Vehicles.Weapons.ProjectileImpactEffect::PlayEffect(Sprocket.DamageModelling.ProjectileEffectInfo)` | 0x21018E0 | 0x70C | RuntimeShellEffects.cs | None (unique) |
| `System.Void Sprocket.Vehicles.Weapons.VehicleWeaponLoadController::Update(Sprocket.Vehicles.VehicleFlags,System.Int32)` | 0x2103290 | 0x3A3 | RuntimeAtgmAmmoBox.cs | None (unique) |
| `System.Void Sprocket.WeaponFramework.WeaponBehaviour::Release()` | 0x210C4E0 | 0x79 | RuntimeAtgmLauncher.cs | None (unique) |
| `System.Void TMPro.TMP_Dropdown::Show()` | 0x2ECE370 | 0x1D43 | RuntimeSimulatorScroll.cs | None (unique) |
| `System.Void UnityEngine.AudioSource::Play()` | 0x3015880 | 0xBF | RuntimeLaunchAudio.cs | None (unique) |
| `System.Void UnityEngine.AudioSource::Play(System.Double)` | 0x3014FA0 | 0xA8 | RuntimeLaunchAudio.cs | None (unique) |
| `System.Void VehicleDesigner.AmmunitionStorage.WeaponLoadControllerAssemblyStage::Execute()` | 0x1C949D0 | 0x67E | RuntimeAtgmAmmoBox.cs, RuntimeAtgmInitialRound.cs | None (unique) |
| `UnityEngine.Sprite Sprocket.GameIcons::GetOrDefault(System.String)` | 0x1D3A800 | 0xED | AtgmAmmoBoxVisuals.cs, RuntimeAtgmGuidanceSight.cs, RuntimeAtgmLauncher.cs, SightUiIntegration.cs | None (unique) |
| `UnityEngine.Vector3 Sprocket.DamageModelling.CompoundStructure::GetFragmentDirection(UnityEngine.Vector3,System.Single)` | 0x1D79E70 | 0x2E1 | RuntimeSpall.cs | None (unique) |
| `UnityEngine.Vector3 Sprocket.PhysicsRules.Ballistics::CalculateInterceptPoint(UnityEngine.Vector3,UnityEngine.Vector3,UnityEngine.Vector3,System.Single,System.Int32)` | 0x1CE50C0 | 0x29B | RuntimeAtgmAi.cs | None (unique) |

## Optional managed targets

| Target (RuntimeSightIntegration reflection) | RVA / length | Sharing / disposition |
|---|---|---|
| SprocketDynamicSight.Game.GameReader.Fill(SightFrame, SightSettings, float) | N/A: optional assembly/type absent | Managed-mod bridge; not registered when types are absent. Two alternative postfix registrations address the same logical method. |
| SprocketDynamicSight.Game.ShellSelectorBridge.ForCannon(Cannon) | N/A: optional assembly/type absent | Same absent managed-mod bridge; not a GameAssembly hook. |
| SprocketDynamicSight.Game.Zoom.Configure(SightSettings) | N/A: optional assembly/type absent | Same absent managed-mod bridge; not a GameAssembly hook. |

Installed SprocketGunSights.dll exposes SprocketGunSights.AmmoBridge, not these legacy SprocketDynamicSight types. Their numeric RVA/length cannot honestly be supplied. This is an audit limitation for an inactive optional bridge, not an unchecked native registration.

## Five highest remaining hook risks (alternatives NOT built)

| Rank | Hook and source | Risk in plain words | Safer alternative for a future user decision |
|---|---|---|---|
| 1 | ArmourPiercingProjectileFunction.HitDamageModel: RuntimeShellSelection and RuntimeTandem | Native hit path passes projectile/hit/result wrappers by ref; bad aliasing or an exception can corrupt a hit or the process. Tandem also launches a precursor in the hit lifecycle. | Put stage/profile data on separate native projectile definitions at launch; observe a unique non-ref result/event after native completes. Requires native lifecycle research. |
| 2 | CompoundStructure.Penetrate: RuntimeSpall.ExperimentalNormalization / RuntimeTandem.Precursor | Replaces ref PenetratorInfo (density, normalization, carrier geometry) and nests precursor work. Wrong ownership changes native energy or contaminates another penetration. | Define correct core/density/normalization at projectile construction; model tandem as separate native shots. Preserve selected/live parity. |
| 3 | CompoundStructure.CreateSpallBurst / GetFragmentDirection: RuntimeSpall | Ref SpallSpawnBurst is temporarily rewritten; direction override skips native random direction. Re-entry or failed restoration can affect unrelated fragments. | Native projectile/material spall configuration and native direction generation; keep observation only. First prove required tunings can be expressed in data. |
| 4 | StructureIntersection.GetNextMaterialBlock + CompoundStructure.SimulateFragment finalizer: RuntimeArmourResponses / NativeMaterialReactions / RuntimeSpall | Temporarily overwrites shared native MaterialInfo.rhaFactor and sometimes fragment state. An unexpected block order, nested sim or missed restore can leak a factor or residual speed to the next block. | Give each block an immutable material entry with precomputed factor, or use a unique native before/after block event that owns material state. Needs native API research; retain ERA/spall. |
| 5 | PlateStructureMesh.LoadBlueprint / FromJson and PlateStructure.RequestMeshRefresh: NativeCassetteGeometry, RuntimeCompositePlate, RuntimeCassettePlateVisual | Callbacks read/cache mesh identity and queue visuals while native mesh edits/load are underway. Even unique addresses cannot protect an invalid native pointer; shared mesh trampoline still runs on both callers. Existing snapshot/quiet-period guards remain. | Subscribe to a proved native mesh-completed event, or poll managed snapshots/content signatures only after editor operations settle. Never read mesh in an in-flight mutation. |

All five remain as in 0.22.28 except the explicitly requested mesh target/guard change. Alternatives require the user to decide; none was built. Numeric uniqueness is not proof of ABI, lifetime, re-entry, save/load or impact correctness.

## Reproduce

`py src-0.22.29/tests/icf_audit.py` extracts attributes and checks every resolved address against both dumps; it fails on unknown target syntax, ambiguity, new dynamic targets, undocumented sharing or missing exception documentation. A negative control injects the old VehicleComponent.Release target and must fail. `Fix02229Tests` executes this gate. Full per-annotation data: ICF-TARGETS.json.
