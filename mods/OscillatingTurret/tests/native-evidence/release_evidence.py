"""Read-only native release investigation; outputs disassembly and direct callers, not a test run.
Usage: py release_evidence.py <icf-metadata.json written by tests/Export-IcfMetadata.ps1>
Environment: SPROCKET_DIR = game folder (default: the Steam folder), OT_PY_DEPS = optional folder with pefile and capstone.
"""
import bisect
import hashlib
import json
import pathlib
import struct
import sys

import os
if os.environ.get('OT_PY_DEPS'): sys.path.insert(0, os.environ['OT_PY_DEPS'])
import capstone
import pefile

GAME = pathlib.Path(os.environ.get('SPROCKET_DIR', r'C:\Program Files (x86)\Steam\steamapps\common\Sprocket'))
data = json.loads(pathlib.Path(sys.argv[1]).read_text(encoding='utf-8-sig'))
rows = data['native']
types = {t['name']: t for t in data['dummyTypes']}
by_addr = {}
for r in rows:
    by_addr.setdefault(int(r['RVA'], 16), []).append(r)
starts = sorted(by_addr)
pe = pefile.PE(str(GAME / 'GameAssembly.dll'), fast_load=True)
base = pe.OPTIONAL_HEADER.ImageBase
md = capstone.Cs(capstone.CS_ARCH_X86, capstone.CS_MODE_64)
print('GameAssembly SHA256', hashlib.sha256((GAME / 'GameAssembly.dll').read_bytes()).hexdigest().upper())
print('Cecil: all', len(rows), 'AddressAttribute records from local Cpp2IL output; current interop exported separately.')


print('\nAUTHORITATIVE VIRTUAL SLOTS from global-metadata.dat (Cecil method order is not a native slot table):')
metadata = (GAME / 'Sprocket_Data/il2cpp_data/Metadata/global-metadata.dat').read_bytes()
assert struct.unpack_from('<II', metadata) == (0xFAB11BAF, 39)
string_base = struct.unpack_from('<I', metadata, 32)[0]
method_offset, method_size, method_count = struct.unpack_from('<III', metadata, 68)
type_offset, type_size, type_count = struct.unpack_from('<III', metadata, 236)
assert method_size == 30 * method_count and type_size == 76 * type_count


def metadata_string(index):
    start = string_base + index
    return metadata[start:metadata.index(b'\0', start)].decode()


for i in range(method_count):
    offset = method_offset + i * 30
    name_index, declaring_index = struct.unpack_from('<IH', metadata, offset)
    name = metadata_string(name_index)
    if name not in ('Release', 'ReleaseInternal'):
        continue
    type_name_index, namespace_index = struct.unpack_from('<II', metadata, type_offset + declaring_index * 76)
    declaring = metadata_string(namespace_index) + '.' + metadata_string(type_name_index)
    if declaring != 'Sprocket.Vehicles.VehicleComponent':
        continue
    token, flags, iflags, slot, parameter_count = struct.unpack_from('<IHHHH', metadata, offset + 18)
    print(f'METADATA @{offset:#x} {declaring}::{name} token={token:#x} flags={flags:#x} slot={slot} parameters={parameter_count}')
    print('  record:', metadata[offset:offset + 30].hex(' '))
    if name == 'Release':
        assert token == 0x0600026C and slot == 13 and parameter_count == 0
        print('  Il2CppClass vtable base 0x138 + slot 13 * VirtualInvokeData size 16 = 0x208; MethodInfo at 0x210.')

print('\nCECIL INHERITANCE (no Release override on any tracked component type):')
for name in ['Sprocket.Vehicles.VehicleComponent', 'Sprocket.Vehicles.Turrets.TurretRing',
             'Sprocket.Vehicles.Turrets.TraverseMotor', 'Sprocket.Vehicles.AttachedBehaviours.Mantlet',
             'Sprocket.Vehicles.Weapons.LayingDrive']:
    t = types[name]
    print('TYPE', name, 'BASE', t['base'])
    print('  Declared Release methods:', [m['signature'] for m in t['methods'] if m['name'] == 'Release'])


def dump(addr):
    records = by_addr[addr]
    length = max(int(r['Length'], 16) for r in records)
    print('\nMETHOD', hex(addr), 'length', hex(length), ' | '.join(r['Name'] for r in records))
    for ins in md.disasm(pe.get_data(addr, length), base + addr):
        annotation = ''
        if ins.mnemonic in ('call', 'jmp') and ins.op_str.startswith('0x'):
            annotation = ' | '.join(r['Name'] for r in by_addr.get(int(ins.op_str, 16) - base, []))
        print(f'{ins.address - base:08X} {ins.mnemonic:8} {ins.op_str:48} {annotation}')


def callers(target):
    found = set()
    # Candidate rel32 references are checked against instruction boundaries within their mapped method.
    for section in pe.sections:
        if not section.Characteristics & 0x20000000:
            continue
        raw = section.get_data()
        for opcode in (b'\xe8', b'\xe9'):
            pos = raw.find(opcode)
            while pos >= 0:
                if pos + 5 <= len(raw):
                    site = section.VirtualAddress + pos
                    dest = site + 5 + struct.unpack_from('<i', raw, pos + 1)[0]
                    if dest == target:
                        idx = bisect.bisect_right(starts, site) - 1
                        if idx >= 0:
                            owner = starts[idx]
                            length = max(int(r['Length'], 16) for r in by_addr[owner])
                            if owner <= site < owner + length:
                                for ins in md.disasm(pe.get_data(owner, length), base + owner):
                                    if ins.address == base + site and ins.mnemonic in ('call', 'jmp'):
                                        found.add((owner, site))
                                        break
                pos = raw.find(opcode, pos + 1)
    return sorted(found)


targets = [0x1FD0FA0, 0x1FD7680]
for target in targets:
    calls = callers(target)
    print('\nDIRECT CALL/JMP REFERENCES TO', hex(target), 'count', len(calls))
    for owner, site in calls:
        print(hex(site), '<-', hex(owner), ' | '.join(r['Name'] for r in by_addr[owner]))
    dump(target)
    for owner in sorted({owner for owner, _ in calls}):
        if owner not in targets:
            dump(owner)
