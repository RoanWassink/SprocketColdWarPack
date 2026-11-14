"""Heavy Trunnion 0.3.0 native evidence: disassemble named GameAssembly methods.
Usage: py heavy_disasm.py <substring> [...]   (matches the method's full name)
Map: native-methods.json (RVA + length per method) that matches GameAssembly 3E58082804D9...;
the script prints the GameAssembly hash so the match is checked.
Environment: SPROCKET_NATIVE_METHODS = native-methods.json (Name/RVA/Length per native method of GameAssembly.dll; read only),
SPROCKET_DIR = game folder (default: the Steam folder), OT_PY_DEPS = optional folder with pefile and capstone (else pip-installed).
"""
import hashlib, json, os, sys, pathlib
if os.environ.get('OT_PY_DEPS'): sys.path.insert(0, os.environ['OT_PY_DEPS'])
import pefile, capstone

MAP_PATH = os.environ['SPROCKET_NATIVE_METHODS']
GAME = os.path.join(os.environ.get('SPROCKET_DIR', r'C:\Program Files (x86)\Steam\steamapps\common\Sprocket'), 'GameAssembly.dll')

rows = json.loads(pathlib.Path(MAP_PATH).read_text(encoding='utf-8-sig'))
entries = [(int(r['RVA'], 16), int(r['Length'], 16), r['Name']) for r in rows]
names = {}
for a, l, n in entries:
    names.setdefault(a, n)
print('GameAssembly SHA256', hashlib.sha256(open(GAME, 'rb').read()).hexdigest().upper())
pe = pefile.PE(GAME, fast_load=True)
base = pe.OPTIONAL_HEADER.ImageBase
md = capstone.Cs(capstone.CS_ARCH_X86, capstone.CS_MODE_64)
for arg in sys.argv[1:]:
    print('===', arg)
    for addr, length, name in sorted(e for e in entries if arg in e[2]):
        print(f'METHOD 0x{addr:X} len=0x{length:X} {name}')
        for ins in md.disasm(pe.get_data(addr, length), base + addr):
            note = ''
            if ins.mnemonic in ('call', 'jmp') and ins.op_str.startswith('0x'):
                note = names.get(int(ins.op_str, 16) - base, '')
            print(f'{ins.address - base:08X} {ins.mnemonic:8} {ins.op_str} {note}')
        print()
