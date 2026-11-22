import json
import os
import re

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
part_path = os.path.join(ROOT, "assets", "wheeledSuspensionPart.json")
xml_path = os.path.join(ROOT, "assets", "wheeledSuspension.xml")
ref_path = r"E:\SteamLibrary\steamapps\common\Sprocket\Sprocket_Data\StreamingAssets\Parts\christieSuspensionPart.json"
game_parts = r"E:\SteamLibrary\steamapps\common\Sprocket\Sprocket_Data\StreamingAssets\Parts"

p = json.load(open(part_path, "r", encoding="utf-8"))
r = json.load(open(ref_path, "r", encoding="utf-8"))

assert set(p.keys()) == set(r.keys()), f"Root keys differ: {set(p.keys()) ^ set(r.keys())}"
assert len(p["components"]) == len(r["components"])
for cp, cr in zip(p["components"], r["components"]):
    assert set(cp.keys()) == set(cr.keys())
    assert set(cp["info"].keys()) == set(cr["info"].keys()), f"Comp info keys differ for {cp['fileID']}"
for op, or_ in zip(p["transform"]["objects"], r["transform"]["objects"]):
    assert set(op.keys()) == set(or_.keys())
    for cp, cr in zip(op["components"], or_["components"]):
        assert set(cp["info"].keys()) == set(cr["info"].keys())

guid = p["guid"].lower()
guid_re = re.compile(r'"guid"\s*:\s*"([^"]+)"', re.I)
for fn in os.listdir(game_parts):
    if not fn.endswith(".json"):
        continue
    if fn.lower() == "wheeledsuspensionpart.json":
        continue
    txt = open(os.path.join(game_parts, fn), "r", encoding="utf-8", errors="ignore").read()
    m = guid_re.search(txt)
    if m and m.group(1).lower() == guid:
        raise SystemExit(f"GUID collision with {fn}!")

raw = open(xml_path, "rb").read()
assert raw[:2] == b"\xff\xfe", "XML must be UTF-16 LE with BOM"
print(f"PART SCHEMA + GUID + UTF-16 XML CHECK PASSED: {guid}")
