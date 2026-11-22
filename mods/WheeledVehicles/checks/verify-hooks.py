import json
import os
import re
import sys
from collections import defaultdict

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
DUMP_PATH = r"c:\Users\Roanj\Documents\Codex\2026-11-18\native-dump-26757044\all-native-methods.json"

with open(DUMP_PATH, "r", encoding="utf-8-sig") as f:
    methods = json.load(f)

by_rva = defaultdict(list)
for m in methods:
    by_rva[m["RVA"]].append(m["Name"])

patch_re = re.compile(r'HarmonyPatch\(\s*typeof\(([^)]+)\)\s*,\s*nameof\(([^)]+)\)\s*\)')
# Map simple C# class names in our mod to full native prefixes
type_map = {
    "TrackBlueprint": "Sprocket.Vehicles.Tracks.TrackBlueprint",
    "Blueprint": "Sprocket.Blueprints.Blueprint",
    "TrackEditor": "Sprocket.Vehicles.Tracks.Editor.TrackEditor",
    "TrackBelt": "Sprocket.Vehicles.Tracks.Belts.TrackBelt",
    "TrackAssembly": "Sprocket.Vehicles.Tracks.TrackAssembly",
    "TrackBehaviour": "Sprocket.ContinuousTracks.TrackBehaviour",
    "VehiclePowertrainBehaviourModule": "Sprocket.Vehicles.Powertrains.VehiclePowertrainBehaviourModule",
    "VehicleMovementRegister": "Sprocket.Vehicles.Powertrains.VehicleMovementRegister",
    "PowertrainBehaviour": "Sprocket.Vehicles.Powertrains.PowertrainBehaviour",
    "PartDefinitionCardFactory": "Sprocket.PartImporting.PartDefinitionCardFactory",
    "TrackAudioController": "Sprocket.Vehicles.Tracks.TrackAudioController",
    "VehicleController": "Sprocket.Gameplay.VehicleControl.VehicleController",
    "ProjectileImpactEffect": "Sprocket.Vehicles.Weapons.ProjectileImpactEffect",
}

found = []
for fn in sorted(os.listdir(ROOT)):
    if not fn.endswith(".cs"):
        continue
    text = open(os.path.join(ROOT, fn), "r", encoding="utf-8").read()
    # Also check for ref struct parameters on Harmony hooks
    for m in patch_re.finditer(text):
        cls = m.group(1).strip()
        member = m.group(2).strip().split(".")[-1]
        found.append((fn, cls, member))

errors = 0
print(f"Auditing {len(found)} Harmony hooks against {DUMP_PATH}...")
for fn, cls, member in found:
    full_cls = type_map.get(cls, cls)
    needle = f"{full_cls}::{member}("
    hits = [x for x in methods if needle in x["Name"]]
    if not hits:
        print(f"  FAIL [NOT FOUND]: {fn} -> {needle}")
        errors += 1
        continue
    for h in hits:
        rva = h["RVA"]
        ln = int(h["Length"], 16)
        shared = by_rva[rva]
        if len(shared) != 1:
            print(f"  FAIL [ICF SHARED {len(shared)}x]: {fn} -> {h['Name']} @ {rva} (len 0x{ln:X})")
            errors += 1
        elif ln < 0x10:
            print(f"  WARN [SHORT 0x{ln:X}]: {fn} -> {h['Name']} @ {rva}")
        else:
            print(f"  OK   [UNIQUE, 0x{ln:X}B]: {fn} -> {h['Name']} @ {rva}")

if errors:
    print(f"\nHOOK AUDIT FAILED: {errors} error(s).")
    sys.exit(1)
print(f"\nHOOK AUDIT PASSED: {len(found)} hooks verified, 0 ICF collisions.")
