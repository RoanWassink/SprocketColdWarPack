"""Find native methods that call (call/jmp rel32) one of the given RVAs. Usage: py heavy_callers.py <rva-hex> [...]
Environment: SPROCKET_NATIVE_METHODS = native-methods.json (Name/RVA/Length per native method of GameAssembly.dll; read only),
SPROCKET_DIR = game folder (default: the Steam folder), OT_PY_DEPS = optional folder with pefile and capstone (else pip-installed).
"""
import json, os, sys, pathlib
if os.environ.get('OT_PY_DEPS'): sys.path.insert(0, os.environ['OT_PY_DEPS'])
import pefile, capstone
MAP_PATH = os.environ['SPROCKET_NATIVE_METHODS']
GAME = os.path.join(os.environ.get('SPROCKET_DIR', r'C:\Program Files (x86)\Steam\steamapps\common\Sprocket'), 'GameAssembly.dll')
rows = json.loads(pathlib.Path(MAP_PATH).read_text(encoding='utf-8-sig'))
pe = pefile.PE(GAME, fast_load=True); base = pe.OPTIONAL_HEADER.ImageBase
md = capstone.Cs(capstone.CS_ARCH_X86, capstone.CS_MODE_64)
targets = {base + int(a, 16): a for a in sys.argv[1:]}
seen = set()
for r in rows:
    a, l = int(r['RVA'], 16), int(r['Length'], 16)
    if a in seen or l > 40000: continue
    seen.add(a)
    try: code = pe.get_data(a, l)
    except Exception: continue
    for ins in md.disasm(code, base + a):
        if ins.mnemonic in ('call', 'jmp') and ins.op_str.startswith('0x') and int(ins.op_str, 16) in targets:
            print(f'{targets[int(ins.op_str, 16)]} <- 0x{a:X} @0x{ins.address - base:X} {r["Name"]}')
