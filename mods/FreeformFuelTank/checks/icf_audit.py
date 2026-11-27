import json
import sys
from collections import defaultdict

DUMP_PATH = r"c:\Users\Roanj\Documents\Codex\2026-11-18\native-dump-26757044\all-native-methods.json"

HOOKS = [
    "Sprocket.Vehicles.FuelTanks.FuelTank::Initiate(",
    "Sprocket.Vehicles.FuelTanks.FuelTank::Build(",
    "Sprocket.Vehicles.FuelTanks.FuelTank::BuildDamageModelColliders(",
    "Sprocket.Vehicles.FuelTanks.FuelTank::CalculateCenterOfMass_LocalSpace(",
    "Sprocket.Vehicles.PlateStructures.PlateStructure::Build(",
    "Sprocket.Vehicles.PlateStructures.PlateStructure::BuildDamageModelColliders(",
    "Sprocket.PartImporting.PartDefinitionCardFactory::CreateCard(",
]

with open(DUMP_PATH, "r", encoding="utf-8-sig") as f:
    data = json.load(f)

methods = data.get("methods", data) if isinstance(data, dict) else data
by_rva = defaultdict(list)
for m in methods:
    rva = m.get("RVA") or m.get("rva")
    name = m.get("Name") or m.get("name") or ""
    if rva is not None:
        by_rva[rva].append(name)

failed = 0
print("=== ICF Audit (Sprocket 0.2.56.3, buildid 26757044) ===")
for h in HOOKS:
    matches = [m for m in methods if h in (m.get("Name") or m.get("name") or "")]
    if not matches:
        print(f"  FAIL: NOT FOUND: {h}")
        failed += 1
        continue
    for m in matches:
        name = m.get("Name") or m.get("name")
        rva = m.get("RVA") or m.get("rva")
        length = m.get("Length") or m.get("length") or "?"
        shared = len(by_rva[rva])
        status = "PASS" if shared == 1 else "FAIL (ICF SHARED)"
        if shared != 1:
            failed += 1
        print(f"  {status}: {name} | RVA={rva} | Len={length} | Shared={shared}")

print(f"Audit result: {'ALL PASS' if failed == 0 else f'{failed} FAILED'}")
sys.exit(0 if failed == 0 else 1)
