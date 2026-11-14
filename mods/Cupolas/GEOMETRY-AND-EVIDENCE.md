# Geometry and evidence for prepared 0.1.0

The supplied assets were copied unchanged. No modelling, re-export or icon regeneration was performed. SHA256 values are recorded in `Assets/approved-sha256.json` and `artifact-hashes.json` (written by `Tools/report_artifacts.py`). The approved contact sheet and the 1200×800 smooth render made from the exact OBJ positions/normals were visually inspected. `Assets/approved/view-t90a-cupola.scad` imports the corresponding STLs and shows thin yellow bounds. Authoring poses in the original asset previews do not describe a game feature.

Independent OBJ measurements in metres, game frame (+Y up, +Z forward):

| Export | Vertices (v//vn) | Triangles | Minimum | Maximum |
|---|---:|---:|---|---|
| Base | 1698 | 1744 | (-0.335,-0.025,-0.330) | (0.335,0.245,0.440) |
| Lid, hinge-local | 763 | 796 | (-0.265,0,-0.016) | (0.265,0.0892855072,0.535) |

The SCAD hinge H=(0,-270,240) mm maps through the approved export's x,z,y conversion to `hatchObject.localPosition=[0,0.24,-0.27]`. This places the closed lid underside on the ring lip, with the lid disc centred at approximately XZ=(0,0). Its rendered maximum height is approximately 0.329286 m after adding H.

`cameraTrackPosition=[0,0.4,0]`, `objectiveLensPosition=[0,0.149716,0]` and `eyepiecePosition=[0,0.149716,0]` copy the measured Matilda II reference heights. All are horizontally centred within the 0.500 m ring opening; the camera track reference is above the lid. These are gameplay reference positions, not a claim about historical optical equipment or verified native occlusion. `entrancePosition=[0,0.24,0]` centres the native exit at the measured ring lip. `entranceDirection=[0,1,0]`, `entranceSize=0.65` and `maxPressure=20000` retain stock semantics. No hinge angle or control is introduced.

`iconOffset=[0,0]`, `iconRotation=[]`, `iconScale=1` centre the supplied full 256×256 sprite without a further crop or rotation. The existing Mast API replaces the native card sprite directly; it does not use those model-thumbnail fields. No localization aliases are needed for a new canonical `t90aCupola` filename/name key; XML elements remain lowercase and the file has a UTF-16 LE BOM.

Root schema 0.3 uses the complete working `smokeLauncherBankPart.json` key set. The native constructor evidence in the supplied `CUPOLA-MAP.log` (RVA 0x1E2F720, local GameAssembly SHA256 3E58082804D97CF32FFD400E7427E767EF9576D23DB71B2BE96FBB7F825FC17B, rehashed for this build) reads order from minor 2, date from minor 2, and icon fields from minor 3. Both model-info blocks stay at 0.4, with the exact stock key set, associated IDs, material/mesh GUIDs and null durableMaterialID. No mass field is added. All stock transform permissions, mirror/snap behaviour and 0.5–2 scale bounds are retained except the required dynamic=true.

The embedded data retains exactly one vertex per OBJ v//vn pair and OBJ triangle order. Runtime cylindrical and planar charts duplicate render vertices at seams (2033 base / 866 lid) while preserving every triangle corner's position, authored normal and winding. The density is one UV per metre, not a per-face normalized atlas. Tangents are generated from these UVs. CPU validation rejects nonfinite data, invalid normals/indices, zero-area triangles, inward winding/volume, wrong bounds/units, degenerate UVs/tangents and a mismatched embedded binary hash. Tests also read the actual production DLL's static mesh fields and independently parse the two OBJs.

Replacement requires the two exact native model wrappers under one installed own root, matching commander associations and Hatch.DoorModel, and the original two-object topology. An inspectable per-vehicle RendererMergerAssemblyStage must show no atlas user referencing either mesh/renderer. Missing stages or unsafe/ambiguous state keep stock geometry. This deliberate conservative gate can retain the stock appearance in designer or Play; availability of those native stages has not been demonstrated live. Snapshot rollback changes meshes only, preserving native visibility and lid pose. Shared meshes live for the driver lifetime and are destroyed after safe restoration on unload; failure to restore retains them to protect native references.

All offline checks passed for the final production bytes. Game installation, startup, atlas transitions, storage/reload, distant LODs, actual mass/cost deltas, collision/interaction, native commander/viewport behaviour, cook-off and visible selector icon are unverified. Virtual/generic native dispatch remains undecidable offline even with zero static interop failures. No new Harmony hook was built or required.
