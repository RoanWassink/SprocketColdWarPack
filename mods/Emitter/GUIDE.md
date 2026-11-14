# Sprocket Emitter

Prepared development build, author Nero. No installation or native acceptance test has been performed.
The placeable emitter is an installed, dynamic, selection-only stock adapter, in Crew / Stowage.
The 120 mm arrow points along local +Z. Scale range 0.5–3 gives a 60–360 mm arrow and matching interaction mesh.
Native mirroring is disabled because the dynamic native transform origin breaks repositioned twins. Place independent copies.
Ghosts deliberately keep the stock box. The stock adapter material is retained. The arrow is hidden in Play with SetVisible.
First-frame hiding, selection, native rebuilding and return to designer require live confirmation.

Connect the Emitter output in Mast I/O Flow (Eject / Stop). A complete IO-API revision 1 is required for managed output and icons.
Without Mast, or with revision 0 / an incomplete revision 1 facade, the emitter is inert and does not write storage.
The first callback is a baseline and never emits, including a saved on-state. A subsequent rising Boolean edge emits one batch.
Mast supplies only on/off: Pulse / Open / Toggle cannot be distinguished. Overlapping pulses can merge.
With continuous enabled, repetition begins after a rising edge and stops on false. False leaves existing objects in place.
Paused edges are discarded; neither object age nor repeat timers advance while paused. There is no lag catch-up burst.
Without revision 2 (Mast before 0.9.9) the cfg section Defaults supplies all values. With revision 2 the Settings section of the
selected output node in the Mast I/O inspector edits them per part; Mast freezes the schema defaults into the part data, so the
0.1.0 defaults are fixed constants (casing, 30 mm, 3 m/s, 8 deg, 180 deg/s, 1, off, 0.5 s, 10 s, inherit on) and the cfg no longer applies.
Settings use data key sprocket.emitter, are read on each rising edge and after onChanged, clamped and defaulted like Mast does.
Apply calls outside Play (designer return, release) never become the baseline. Mast owns persistence; save/reload is unverified.

Objects are inert renderers on Ignore Raycast, outside the vehicle hierarchy, without colliders, rigid bodies, damage or sound.
Already-loaded rack LOD0 meshes are cloned once per vehicle/item with non-generic Object.Instantiate; native assets are never destroyed.
No asset load request is made. 0.1.1: the stock meshes are harvested from any ammunition rack that has them loaded, in the designer as well as in Play,
every 3 s while an Emitter exists, and kept as owned clones that survive the designer-to-Play scene change; a `stock probe` log line says what was found.
Without a stock mesh nothing is thrown (one warning). The embedded Carousel Charge8 / Projectile8 shapes are used only when Meshes.AllowEmbeddedFallback=true; they are a closed propellant-case proxy, not a demonstrated empty spent case.
Shapes scale generically, without historical case profiles. Bounds and longest axis are measured on first use. Source and scale are logged. Stock bounds are not measured offline.
Brass and grey use shared HDRP/Unlit materials; no metallic lighting or paint registration.

Motion uses bounded semi-implicit integration and RaycastNonAlloc, mask -5, triggers ignored, nearest suitable hit.
This is a point-path approximation: a long rotating case can intersect corners. No native physics force is applied.
Resting objects follow their collider using local points and relative rotation, without SetParent. Steep supports cause sliding.
Only moving collider transforms are followed. Rebuilt, disabled or destroyed colliders release their objects.
Pool capacity includes resting objects. Cast budget exhaustion freezes the affected integration step rather than skipping collision.
At a full 128-hit query buffer, the optional simulation clears safely and emits a warning; it never assumes truncated results are globally nearest.
Scene change, designer return, owner release, lifetime and OnDestroy clear owned objects. No quality or frame-time claim is made.

Run Verify.ps1 with PowerShell from the source directory. It builds, runs the offline checks (contract, mesh, Logic, Eligibility, interop scan) and writes only to its work folder (-WorkDir).
It reads the game folder and never writes to it; -BuildGameDir points the build at a reference copy of BepInEx/core and interop.
Installation (Sprocket closed): copy BepInEx/plugins/SprocketEmitter (DLL and icon) and the part definition and localization under Sprocket_Data/StreamingAssets (Parts, Localization/en-UK/Parts) into the game folder.
The configuration file BepInEx/config/sprocket.emitter.cfg is written by the plugin on first start; the file in this source folder only lists the defaults.
The Mast is found by plugin GUID sprocket.telescopicmast (soft dependency) and its facade type SprocketTelescopicMast.Io.IoApi; Logic and check-contract.py test both names.

Schema: the stock boltOnBoxStowage keysets plus the root key date = 1930.01.01 (the same root v0.0 key 57 stock parts use).
Model info v0.2 has no mass or price key; native mass is read only from model info v0.5, which would change the material contract.
Mass and price are therefore the native values of the stock bolt-on box adapter; they are not measured offline.
The outlet is 120 mm along +Z times the part scale (node scale if the transform carries it, else ModelScale.z); logged once per part.
