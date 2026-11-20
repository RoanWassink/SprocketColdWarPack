# Changelog

## 0.3.4 - 2026-11-20
- Mixed ammunition autoloader fixes (with Expanded Ballistics 0.22.99):
  - `BeltRuntime` and `MagazineRefill`: Belt feed boxes now load and refill mixed shell types via EB's typed stores instead of rejecting non-vanilla request IDs.
  - `AmmoStorageApi` v2: adds `GetRackCapacityForGeometry` (including two-piece, revolver drum geometry, and native rack spacing), `IsReadyMagazine` (ready magazine sorted before hull reserve), and `GetReachableStorage` so EB stays aware when a carousel/bustle has no crew loader or the ready magazine is full.
  - `MagazineRefill`: prevents deadlocks in `KeepMagazineRoute` / `Resume` when switching to a reserve type while a mixed bustle is full or has no healthy crew loader.
  - `CarouselRuntime`: `RefreshNativeLoaders` and `Speed` call `AmmoLoadoutBridge.Prepare` before `SelectSupply` so EB stocks/steers `Request.TypeID` first; carousel fallback to hull reserve when a mixed type runs out in the carousel is permitted when a crew loader is present (`SemiAutoloader.HasCrew`).
  - Zero new Harmony targets (stays at 41).

## 0.3.3 - 2026-11-16
- Belt HUD: count and coloured status on the heading row, cached rpm on a small second row, and a thin stock bar in both ring and bar modes. Height includes both extra elements for placement below AMMO. Bustle/revolver text and behaviour, loading, visuals, configuration defaults and all 41 patch targets are unchanged.

## 0.3.2 - 2026-11-16
- Reserve selection uses native ProjectileSizeID, requested projectile type and finite stock, independent of shell-slot blueprint numbers. Belt, bustle, semi and revolver share one transfer/refund path; managed mixes remain with Expanded Ballistics typed storage.
- Duplicate slots may also have distinct generated projectile types. An empty unmanaged box may adopt the actual reserve type only without a real round loading/chambered; original wildcard requests stay distinct from the mechanical supply pin. Partial stock and occupied rounds retain their identity. Refusal restores both stock and box type.
- Belt retries its native crew refill point after delayed box initialization. Refill status distinguishes missing/unhealthy crew, native reach validation and a missing native loading task; idle assigned loaders still count.
- Own Belt and bustle round layouts include native per-axis spacing/padding (Bradley 248 x 221 x 308 mm, 25 x 212 mm, 10 mm gaps: 42 slots). Visual construction reads built component capacity and retries transient initialization instead of permanently refusing a zero-capacity preview.
- Bustle still starts crew refill only when empty. No new Harmony targets; CarouselVisualGeometry and approved STL assets unchanged. Build default follows the relocated game folder.

## 0.3.1 - 2026-11-15
- Belt feed box: approved OpenSCAD box, 228-triangle links and chute, true-size complete rounds, packed top layer and stock-driven fill block. Empty stock shows only the box and chute.
- Visual budget: omit links first, then use a ribbed top block; one DetailedLogging notice per rack VUID. Native ammunition, loading, damage, capacity, mass and all 41 patch targets are unchanged.
- Dedicated 256 px Belt icon embedded and copied to installation output. Mesh hashes, packer/layer/prefix/fill/budget/allocation checks and four mesh-derived cutaway renders.

## 0.3.0 - 2026-11-15

Belt feed box: a fourth loader kind using a real native ammunition rack, available from 1965-01-01. Complete rounds only, no lower calibre limit, configurable 57 mm upper limit and 1.5 m feed reach. Normal native packing; mechanism mass 25 kg + 0.1 kg per capacity slot, excluding native ammunition mass.
- Config [Belt] sets the calibre/rpm table, a hard 1200/min ceiling, mass, reach and stationary crew refill. Firing needs no crew; replenishment needs a healthy assigned loader in reach and three seconds below 0.5 m/s, transferring one finite reserve round at a time with refund on rejection. Moving discards partial work.
- Existing Update hook: one native call per frame where possible, first three phases take one call each and ram receives the remaining work. Rounding credit crosses cycles. Only the minimum uniform substeps needed to fit the short frame-rounded cycle are used (1200/min: four calls at 30 fps, two at 60, one at 144); unused final-frame time is charged to the rounding credit. No additional hooks or TakeShell accounting.
- One native ammunition type and a cached source; no per-round LINQ list or sorting in Belt source selection. Existing save fields; saved TwoPiece is ignored. Native rack rendering/damage and temporary bustle icon; one 10 Hz HUD line and Belt fit readout.
- CarouselVisualGeometry, CarouselRuntime, CarouselGeometry, CarouselNativeDamage and the three older part definitions remain byte-identical to 0.2.34. Build/offline tests do not establish live cadence, interop allocation cost, ammunition lifecycle or native blueprint persistence.

## 0.2.34 - 2026-11-08

The lift stuck far above the carousel at basket depths 1193 to 1215 mm (user, blueprint "T-80BVM (proporties)": basket 2230 mm wide, 125 mm cannon, mixed loadout with a 900 mm charge). Visual only: no new hook, no new patch target, no gameplay or damage change; CarouselVisualGeometry is byte-identical.
- Cause: the lift is stretched or squeezed to the height of the cannon's load point, but only within 0.3 to 1.5 x its reference height. Outside those limits 0.2.32/0.2.33 used the default height (about 1.1 m x calibre scale above the cassettes). With this tall T-80 cassette (a 900 mm upright charge, so the cassettes reach 1.17 m) the load point is only about 0.31 to 0.33 m above the cassette tops in the window, which is a factor just under 0.3, so the lift got the default height and stuck far above the breech; from 1216 mm the factor reaches 0.3 and the lift was right. (1193 mm is where the carousel itself starts to fit: required depth = 150 + 18 + calibre + charge.)
- Fix: a load point outside the limits now gets the nearest limit (0.3 or 1.5) instead of the default height; only an unknown load point still gives the default. The ram then ends at most a few centimetres above the breech in the window.
- Capacity 0 (basket too shallow): no ring parts exist, so the proof line says "path None" and the "ring meshes baked by the game" Info line is no longer written.
- Proof line (DetailedLogging) extended with: capacity reason, model level, "lift top <m> / basket top <m>".

## 0.2.33 - 2026-11-08

The ring turns in Play too (0.2.32 test: "ik zie hem niet fysiek draaien"; the log showed "ring children 0" in Play). Visual only: no new hook, no new patch target, no gameplay or damage change; CarouselVisualGeometry is byte-identical; nothing of the game's own objects is changed or re-parented.
- Cause: at the start of Play the game takes the cassette, projectile and charge objects out from under "Carousel ring" (the Ring object lived on, with 0 children) while our meshes are still drawn (fired rounds still disappear). Which step does it (native model bake, a move or a clone) cannot be read from the interop; the fix handles moved and cloned parts, and reports baked ones.
- Ring parts are looked up (not per frame): at the first stock change after a (re)binding and again when the set no longer fits (destroyed, or the Ring child count differs). All under Ring: the 0.2.32 path (Ring.localRotation). Otherwise the MeshFilters that use one of our ring meshes are found (under the generator, then under the vehicle root) and each part's pose in the Root frame is kept with the applied angle removed; turning sets each part to that pose turned about the Y axis of Root (position through Root.TransformPoint, rotation Root.rotation * yaw * base), which is what the ring transform would give, also with the non-uniform 1/scale of Root. Nothing found: one Info line "ring meshes baked by the game; ring cannot turn in Play".
- The existing Tick sets the poses of those few parts, only during a turn. Remove also destroys our parts the game moved away; the game's clones are left alone (their mesh is destroyed anyway).
- Lift slot after a rebuild in Play: when the load point is not valid at that moment (rebuild at the start of Play), the last valid target of the same basket (VUID) is used; one Info line with DetailedLogging.
- Proof line (DetailedLogging) extended: ring parts k/m under ring, path, moved, clones, parents of the first part (4 levels), lift target fresh|cached|none, and the first part's world yaw when parts are turned one by one.

## 0.2.32 - 2026-11-05

Ring fix and realistic loading (user test of 0.2.31: "de round onder de loader is altijd leeg ... Hij draait niet echt"). Visual only: no new hook, no new patch target, no gameplay or damage change; CarouselVisualGeometry is byte-identical.
- Rule changed (user: "Leeg, zoals echt"): the ring now turns f - 1 slots (n = capacity, s = stock, f = n - s, L = slot under the lift at a full stock). The slot used last, (L + f - 1) mod n, stands EMPTY under the lift between two shots; at f = 0 the ring is in its base position with full slot L under the lift. Empty slots are still those with (i - L) mod n < f. Refilling turns the ring back.
- Smooth turn (user: "zo nee zichtbaar draaien"): when the stock drops by exactly 1 the ring turns from the old to the new angle in [Visuals] CarouselTurnSeconds = 0.5 s (0 = jump). Driven by the existing LoadTask.Update postfix (one call, one count check when nothing turns; no allocation); time based, with a safety net: a ring whose last tick is more than 1 s old, whose time is up, or whose stock changes during the turn goes straight to its end angle. A change of more than 1, a refill, a Play reset and loading a save jump.
- Proof for the next test (DetailedLogging = true): one Info line per stock change, "[Carousel visual] Basket <VUID> stock s/n, fired f, ring <deg> deg (set/turning, was <old>, transform now <deg>), slot under lift <i> (empty|full), ring children <k>", plus "ring turn done". The "transform now" value is read back from the ring transform, so it shows whether the rotation really was applied. The ring angle is also re-applied when the basket is shown again (Sync).
- The load log line now shows the real version from one constant (Plugin.PluginVersion) instead of "0.2.28".

## 0.2.31 - 2026-11-05

The carousel ring turns one slot per shot (user: "ik zie alleen niet dat de round precies bij de lift wordt gepakt", choice "Ring draait per schot"). Visual only: no new hook, no new patch target, no gameplay or damage change; CarouselVisualGeometry is byte-identical.
- Rule (n = capacity, s = stock, L = slot under the lift at a full stock, f = n - s): slots i with (i - L) mod n < f are empty; the ring is turned f slots on (minus f x 360/n degrees about Y, on top of the 0.2.30 base rotation), so slot (L + f) mod n, a full one while s > 0, stands under the lift. With s = 0 everything is empty and the ring stays at its base position. Refilling lowers f and turns the ring back. The stock is limited to 0..n; n = 1 works (no turn).
- Display: cassettes, projectiles and charges hang under one ring object that is turned when the native stock changes (the existing change detection); the lift, floor ring and drive trim do not turn. A jump without animation; nothing per frame, no mesh is rebuilt. The Simple and reduced models behave the same (their slot angles stay those of the 0.2.28 model and they have no lift).
- The native damage mesh does not turn with the ring (it keeps the 0.2.28 slot angles).

## 0.2.30 - 2026-11-05

Carousel lift exactly behind the cannon (user: "de lift zit niet precies achteraan ... Ja, zet hem goed"). Visual geometry only: no new hook, no new patch target, no gameplay or damage change; CarouselVisualGeometry (and so the native damage mesh) is byte-identical.
- Direction: when the basket has an assigned cannon, its load point (cannon.transform.TransformPoint(cannon.LoadLocalPosition), same pattern as the bustle feed arm) is read once when the model is built, converted to the carousel model frame (root offset and 1/scale corrected), and the lift stands in its direction atan2(x, z). No cannon or an invalid transform: local -Z as before.
- The whole ring of slots is turned so that one slot stands exactly under the lift (at most half a pitch, pi / capacity; 5.8 degrees at 31 slots, 8.2 at 22). Slot indexing and the visible-round order are unchanged; the native damage mesh keeps the unturned angles, so it can differ from the visible rounds by that angle.
- Place: the lift stands on the rim over the outer end of the slot under it, no longer outside the ring: the foot plate ends on the slot outer edge, the guides and the claw stand over the cassette. Its foot starts on top of the cassettes (top of the slot), not on the floor.
- Height: with a known load point the height (Y) of the lift is scaled so that the ram ends at the load point's height, within 0.3 to 1.5 x the reference ram height; otherwise (unknown or outside the limits) the default height x calibre scale. The ram points radially inward in the lift direction, towards the cannon.
- Config [Visuals] CarouselLift = true is unchanged (false hides the lift). The ram head's rear end (cylinder at breech height) still reaches beyond the slot outer edge.

## 0.2.29 - 2026-11-05

Carousel models (BESLUITEN 2026-11-05 "Goed, bouw ze in"): the approved T-72 and T-80 style carousels replace the procedural carousel model. Visual geometry and config only: no new hook, no new patch target; loading, capacity, damage and the native damage mesh are unchanged.
- New CarouselModelGeometry.Build (same inputs as the 0.2.28 CarouselVisualGeometry.Build): vertical-charge layout = T-80 parts (lying projectile, upright charge at the outer edge), otherwise T-72 parts (projectile below, charge lying above). Per slot the cassette, projectile and charge are turned to the slot angle with the slot origin on the 0.2.28 outer edge, local -Z to the centre. Projectile and charge cross-section scale with calibre / 0.125 m, projectile length with projectile length / 0.680 m, charge with charge length / 0.408 m (T-72 along Z, T-80 upright along Y); the cassette follows (head and T-80 charge cage per calibre, troughs stretched to the longest piece, T-80 cage height to the upright charge) and is narrowed to its real wedge (2 pi r / capacity, 1.5 mm margin per side) at the inner and outer end, so neighbouring slots never touch.
- Hub (naaf) in the centre on the 0.2.28 floor ring, scaled with the calibre but never wider than the free space inside the cassettes or taller than the basket; its ring gear uses the DriveTrim material. The old cylinder hub is gone; the procedural floor ring and rim stay.
- Cassette lift at local -Z, its foot 0.1 m x calibre scale behind the slot outer edge, ram towards +Z (centre and breech), calibre scale. The runtime has no fixed outlet position (the carousel is a virtual rack feeding the cannon's load position), so the lift stands at the rear, behind the slot nearest local -Z (exactly -Z with an even slot count, half a pitch beside it with an odd one). The slots keep the 0.2.28 angles, which the native damage mesh also uses. The lift stands outside the basket and reaches into the turret.
- Per basket fallback: a basket shallower than the detailed cassettes, slots too narrow for them, rounds wider than their wedge, or no room for the hub gives exactly the 0.2.28 model, with one Info line per basket. Above 25000 triangles (T-72 from 50 slots, T-80 from 46) the 0.2.28 cassettes and rounds are used with the detailed hub and lift.
- Visible rounds unchanged: per slot projectile_xx/charge_xx meshes, merged per material with prefix counts, so fired rounds disappear as before; materials Cassette, Projectile, Charge, Mechanism (hub, lift, floor ring) and DriveTrim.
- New config [Visuals] CarouselModel = Detailed (default) or Simple (exactly the 0.2.28 model); [Visuals] CarouselLift = true (false hides the lift).
- Mesh data: Models/carousel_t72.obj and carousel_t80.obj, converted by Models/obj_to_cs.py to CarouselModelData.cs (positions, normals and triangles per group); a test compares the embedded data with the OBJ files (SHA256 and a re-parse).

## 0.2.28 - 2026-11-03

Safety release ("clean, stable mod", user choices of 2026-11-03 on the risk inventory). No new hook and no new patch target; loading, refill, damage, visuals and audio behave as in 0.2.27 except where noted.
- Magazine route (bustle/revolver waiting for a crew refill): the native load-task fields (state, phase, source, load info, request, progress) are recorded before the route takes the task. On an error they are written back as raw fields (never through SetState, which can chamber a round), the task is released and the route stays off for that cannon (design + VUID) for the rest of the session, with one warning. A task that stays in the waiting state longer than 3 x the expected refill/fire cycle (at least 10 s) is restored and taken again by the next native request; logged once per cannon. A round taken from a reserve is put back if storing it in the magazine throws.
- Automatic-loader contributor lists (weapon and captured controller arrays): the old array is written back if the write fails, and the write stays off for that cannon for the session (one warning per VUID).
- New config [Damage] NativeCookoff = true. false: the native-damage hook group is not patched (no own hit mesh, health pool or fire control for the carousel); the basket keeps the game's own damage behaviour and the carousel keeps loading. All damage hooks are fully guarded.
- New config [Audio] Enabled = true. The own reload sounds (internal Unity audio calls) only run when enabled and on Unity 6000.3.21f1, the version they were made for; otherwise they stay off with one Info line and no internal call is made. The turret-audio update skip now applies only to vehicles with a Carousel autoloader and is guarded; the turret-audio assembly scan is guarded too.
- Every hook is guarded: an error keeps the native behaviour for that call and is logged once per hook and VUID; repeated identical errors inside existing guards are logged once.
- Per-frame early exit: LoadTask.Update/UpdateRequest/getter hooks and VehicleWeaponLoadController.Update return at once for cannons and vehicles without a bustle, revolver or Semi rack or an enabled carousel basket, from a cache keyed by the native pointer (no wrapper allocation), cleared on every topology change. A cannon without a vehicle no longer throws in the load index.
- Duplicate hooks merged with the same order of work (73 -> 57 hooks on the same 41 targets): LoadTask.Update postfixes 3 -> 1, UpdateRequest postfixes 2 -> 1, RemainingTime/LoadFraction 2 -> 1 each, Cannon.DisableBehaviour prefixes 3 -> 1 (plus the load-index prefix), Cannon.Build, TurretBasket.Build, ReleaseInternal, LoadData and ToBlueprint 2 -> 1, TurretBasket.Release 3 -> 1, AmmoRackModel renderer postfixes 5 -> 3. The four LoadTask.Update prefixes stay separate (different priorities around Expanded Ballistics' own prefixes); RapidCycle is guarded only.
- Unchanged by choice: two-piece capacity (H1, design for an own stock follows), revolver capacity clamp (M1), crew point (M7), projectile registration (M11) and the loader-enable getter (M12).

## 0.2.27 - 2026-11-02

- Autoloader HUD moved directly below the Expanded Ballistics AMMO panel: same corner and X, width 300, its top 8 px (config Gap) under the bottom of the AMMO panel. The position is read once from that plugin's config ("Ammunition HUD": Corner, Offset X, Offset Y, through BepInEx's public config API, again at most once per second) and anchored again when the number of lines changes; Y is never below 8. With the AMMO panel in a top corner, or without Expanded Ballistics, the manual position is used (one Info line).
- Visible only while something runs: a fire cycle, or a crew/stationary refill of a bustle or revolver of the player's vehicle, plus "Blocked: <reason>" for BlockedSeconds (5 s) after it appears or its reason changes. "Cycle: ready" and "Full" are never shown; only the autoloaders with something running get a card, at most 4. Same visibility rules as the AMMO panel: Play only, hidden while a kit panel is open or the game is paused, a free cursor alone does not hide it.
- Look like the AMMO panel: section label ("DRUM 5/6", "BUSTLE 12/16"), a "two-piece" chip where it applies, and the time. With UI Kit 0.1.8 or later (checked at runtime from the loaded kit plugin's version) each running action shows a ring with the remaining seconds in the centre; with an older kit (0.1.7 and before) the same texts and bars as 0.2.26. The kit stays a soft dependency without a version requirement.
- Config [Hud]: Enabled, Placement (BelowAmmo or Manual), Corner/OffsetX/OffsetY (Manual and fallback; new defaults BottomRight 24/230, an existing 0.2.26 cfg keeps its own values), Gap 8, BlockedSeconds 5.
- Loading, refill, Semi and all other gameplay unchanged. Build against the kit 0.1.8 DLL (UiKitDll property) until 0.1.8 is installed.

## 0.2.26 - 2026-11-02

- Revolver crew refill fixed: an assigned, healthy crew loader within native hand reach (also a commander with the loader role) tops the drum up whenever it is not full, also while driving, from any matching rack in the vehicle (hull included). The native loader task only works while the gun is Loading, so it is no longer required for the revolver; without such a loader the drum still refills only while stationary. The revolver crew point is the ram chamber at the feed arm. Bustle and Semi unchanged.
- Refill status as short reasons (full, crew refill, stationary refill, waiting for the vehicle to stand still, no matching reserve ammunition, crew loader cannot reach the magazine, ...): one Info log line per rack and reason change, also with DetailedLogging off.
- Autoloader HUD through the UI Kit (soft dependency, config [Hud] Enabled/Corner/OffsetX/OffsetY): per bustle/revolver of the player's vehicle the stock ("Drum 5/18"), the fire cycle and the refill with progress bars, or "Blocked: <reason>". Updated at most 10 times per second; without the kit one Info line "HUD disabled: UI Kit not installed".

## 0.2.25 - 2026-11-02

- Revolver fire cycle is 0.6 x the bustle cycle for the same ammunition (was 1.5 x); two-piece ammunition stays x 1.25 on top. Semi and bustle timing unchanged.
- Revolver refill without a crew loader stays slow and is now computed from the bustle cycle (two-piece factor included, never the revolver factor): max(10 s, 3 x bustle cycle) per round, per pair with two-piece, only while stationary. The faster fire cycle cannot speed up the refill. The inspector lines "Fire cycle" and "Refill" show the new values.
- New config in [Revolver]: CycleFactor 0.6, RefillCycleFactor 3, MinimumRefillSeconds 10. A value <= 0, NaN or infinite falls back to the default (one warning in the log).

## 0.2.24 - 2026-11-02

- Feed arm at the breech: with an assigned cannon the bustle/revolver arm and the point the game measures from move to the grid column/layer (bustle) or outer chamber (revolver) nearest the cannon's load point, via the native component transforms; the measured point is never farther than the old outlet. Without a cannon the MirrorFeedArm outlet stays. New config Bustle.MaxFeedDistance (0 = native reach); Semi unchanged.
- Two-piece ammunition checkbox for bustle and revolver (saved per rack, default off): the rack needs only the longest piece (carousel split, or the shell API pieces), holds floor(pieces / 2) complete rounds, cycle x 1.25; projectiles and charges shown as separate true-size pieces (new OpenSCAD charge and projectile parts).
- Revolver limits configurable: Revolver.MaxCaliberMm 155 and MaxRoundLengthMm 0 (no fixed limit). Fixes a revolver that would not fire with a valid drum because of the old 1100 mm limit; a refused revolver now blocks loading consistently and the inspector gives exactly one reason.
- One Info log line per rack (VUID) and reason for every refused bustle/revolver load; the revolver limit warning is keyed by VUID.

## 0.2.23 - 2026-11-02

- Bustle and revolver models are static props at true size: every round has the packer diameter and length (ShellLoaderGeometry + NativePackDimensions), parts modelled in OpenSCAD (round with rim, groove, driving band and ogive; cradle, cross clamp, feed trough, ram head, hub) and placed per rack in C#.
- Bustle: rounds on the native packer grid floor(W/d) x floor(H/d) x floor(L/len) in cradles, with cross clamps, side plates, posts and an open top; a feed trough with rammer housing and ram head reaches the gameplay outlet. Bustle and Semi gameplay unchanged.
- Revolver: one drum along the gun axis (diameter min(W,H), rack length), chambers on concentric rings (wall 10 mm, clearance 5 mm, hub max(30 mm, 0.2 d), end plates 15 mm), holed end plates, hub, axle, hoops and a small loading trough at the outlet chamber.
- Revolver capacity = min(native, drum chambers x rows) everywhere (mass follows); saved stock above it is clamped; "1 drum: N chambers x M rows = C rounds" or "Too small for a revolver drum" in the inspector. New single-drum icon.
- No animation: Visuals.Animate is gone (an old cfg line is ignored). Rounds disappear outlet-first per shot via a prebuilt index-buffer prefix; no per-frame transform work or allocation.
- Detail levels keep full racks inside the 12000-triangle budget. A low-definition model without renderers no longer cancels the own rounds; visual warnings are logged once per rack per session.

## 0.2.22 - 2026-11-02

- Approved open-chain bustle and twin-cage revolver models, animated from native stock and assigned-gun load progress.
- Rack-scoped shell-renderer suppression with native-grid fallback, stock-matched own rounds, five shared tinted materials and explicit mesh/material cleanup.
- Visuals.Animate and Visuals.OwnRounds default to true; the revolver has its own icon. Semi model, icon, loading and all gameplay policies remain unchanged.
- Native capacities remain unchanged. Decorations exceeding the fixed visual budget, and a single-chamber revolver, fall back to the native ammunition grid.
- Ram, ram rod and transit round share one feed lane limited laterally to the rack bounds; only the +z protrusion through the outlet remains. Approved meshes unchanged.
- Stock and renderer syncs keep the running load phase while the stock is unchanged.

## 0.2.21 - 2026-11-01

- New "Revolver autoloader" part (1952.01.01), a two-drum turret-bustle loader in AMX-13 style.
- Revolver limits: 105 mm calibre and 1100 mm complete round; capacity still comes from the native rack packer, with no artificial upper bound.
- Revolver fire cycle is 1.5x the bustle curve; mechanism mass is 60 kg + 5 kg per round.
- Without a working crew loader the revolver tops up one finite round at a time from normal racks only after standing still for 3 s, at max(10 s, 3x cycle) per round. Driving pauses progress.
- The native load bar and timer show the stationary refill; all other cases stay native.
- A too-early or oversized revolver keeps its saved part but does no work and logs one warning.

## 0.2.20-ai.1 - 2026-11-01

- Bustle 1952.01.01, semi-autoloader 1953.01.01, T-64/80 carousel 1967.01.01 and T-72 carousel 1973.01.01.
- The basket and rack inspectors show the applicable start date; too-early layouts/loaders cannot be selected and the saved layout is retained.
- Runtime eligibility checks the owning vehicle date before supplying ammunition, so an imported too-early loader keeps its identity but does no work and logs one warning.
- The exact final-era sentinel 9999.12.31 keeps every loader available.

