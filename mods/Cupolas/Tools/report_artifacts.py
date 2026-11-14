"""Artifact inventory: hashes of the built DLL and approved assets, source inventory, staged-layout check.
Run: py -I Tools/report_artifacts.py --out <evidence folder> --staged <folder written by Build-Package.ps1>
Writes only below --out.
"""
from pathlib import Path
import argparse, hashlib, json
ap = argparse.ArgumentParser()
ap.add_argument('--out', required=True)
ap.add_argument('--staged', required=True)
args = ap.parse_args()
ROOT = Path(__file__).resolve().parents[1]; OUT = Path(args.out); STAGED = Path(args.staged)
files = [ROOT/'bin/Release/net6.0/SprocketCupolas.dll', ROOT/'Assets/approved/t90a_cupola_base.obj', ROOT/'Assets/approved/t90a_cupola_door.obj',
         ROOT/'Assets/approved/t90a-cupola-icon.png', ROOT/'parts/t90aCupolaPart.json', ROOT/'localization/t90aCupola.xml']
hashes = {str(p.relative_to(ROOT)): hashlib.sha256(p.read_bytes()).hexdigest().upper() for p in files}
(OUT/'artifact-hashes.json').write_text(json.dumps({'version': '0.1.0', 'sha256': hashes}, indent=2)+'\n')
source = []
for p in sorted(ROOT.rglob('*')):
    if not p.is_file() or any(s in ('bin', 'obj', '__pycache__') for s in p.relative_to(ROOT).parts): continue
    source.append(p)
lines = ['Source inventory of Sprocket Cupolas (without bin/obj).', 'Path\tBytes\tLines']
for p in source:
    try: count = len(p.read_text(encoding='utf-8').splitlines())
    except (UnicodeError, ValueError): count = 'binary'
    lines.append(f'{p.relative_to(ROOT)}\t{p.stat().st_size}\t{count}')
lines.append(f'Source inventory: {len(source)} files, {sum(p.stat().st_size for p in source)} bytes.')
(OUT/'files-and-diffstat.tsv').write_text('\n'.join(lines)+'\n')
expected = {
    'BepInEx/plugins/SprocketCupolas/SprocketCupolas.dll': ROOT/'bin/Release/net6.0/SprocketCupolas.dll',
    'BepInEx/plugins/SprocketCupolas/t90a-cupola-icon.png': ROOT/'Assets/approved/t90a-cupola-icon.png',
    'Sprocket_Data/StreamingAssets/Parts/t90aCupolaPart.json': ROOT/'parts/t90aCupolaPart.json',
    'Sprocket_Data/StreamingAssets/Localization/en-UK/Parts/t90aCupola.xml': ROOT/'localization/t90aCupola.xml',
    'BepInEx/config/sprocket.cupolas.cfg': ROOT/'sprocket.cupolas.cfg',
}
staged = sorted(p.relative_to(STAGED).as_posix() for p in STAGED.rglob('*') if p.is_file())
assert staged == sorted(expected), f'staged layout differs: {staged}'
for rel, src in expected.items():
    assert hashlib.sha256((STAGED/rel).read_bytes()).hexdigest() == hashlib.sha256(src.read_bytes()).hexdigest(), 'staged file differs from source: '+rel
assert [p for p in staged if p.lower().endswith('.dll')] == ['BepInEx/plugins/SprocketCupolas/SprocketCupolas.dll']
print(json.dumps(hashes, indent=2))
print(lines[-1]); print('PASS staged layout and hashes: only own DLL, icon, part, XML and default config.')
