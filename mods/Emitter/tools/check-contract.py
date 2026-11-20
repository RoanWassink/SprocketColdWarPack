"""Emitter 0.1.3 static contract: part JSON schema/GUID, UTF-16 localization, source rules, patch count.
Usage: py -I tools/check-contract.py <game dir> <built dll>
Reads the game folder only (stock template and other part GUIDs). Writes nothing outside stdout.
"""
import copy, json, re, sys, uuid
from pathlib import Path

R = Path(__file__).resolve().parents[1]
GAME = Path(sys.argv[1]); DLL = Path(sys.argv[2])
PARTS = GAME / 'Sprocket_Data/StreamingAssets/Parts'
checks = 0

def check(ok, msg):
    global checks
    checks += 1
    if not ok:
        print('FAIL', msg); sys.exit(1)
    print('PASS', msg)

runtime = (R / 'EmitterRuntime.cs').read_text(encoding='utf-8')
GUID = re.search(r'PartGuid="([0-9a-f-]{36})"', runtime)[1]
COMPONENT = re.search(r'ComponentId="([A-Za-z]+)"', runtime)[1]
template = json.loads((PARTS / 'boltOnBoxStowagePart.json').read_text(encoding='utf-8'))
TEMPLATE_BYTES = (PARTS / 'boltOnBoxStowagePart.json').stat().st_size

def part_errors(p):
    """Return the list of violated rules for a candidate part definition (empty = valid)."""
    e = []
    if set(p) != set(template) | {'date'}: e.append('root keyset != template + date')
    if p.get('v') != '0.0': e.append('root v')
    if p.get('date') != '1930.01.01': e.append('date gate')
    if p.get('guid') != GUID: e.append('guid not the fixed source GUID')
    if p.get('name') != 'emitterPart': e.append('name')
    if p.get('tags') != ['sprocketEmitter', 'stowage']: e.append('tags')
    if any(t in (p.get('tags') or []) for t in ('structure', 'crewEquipment')): e.append('forbidden tag')
    comps = p.get('components') or []
    if len(comps) != 1: e.append('component count')
    else:
        c, t = comps[0], template['components'][0]
        if set(c) != set(t): e.append('component keyset')
        if c.get('fileID') != COMPONENT or c.get('type') != 'model': e.append('fileID/type')
        info = c.get('info') or {}
        if set(info) != set(t['info']): e.append('model info keyset')
        if info != t['info']: e.append('model info values differ from template')
        if info.get('v') != '0.2' or info.get('damageable') is not False or info.get('selectable') is not True or info.get('placementSurface') is not False: e.append('info flags')
    tr, tt = p.get('transform') or {}, template['transform']
    if set(tr) != set(tt): e.append('transform keyset')
    if tr.get('dynamic') is not True: e.append('dynamic must be true')
    if tr.get('mirroring') is not False: e.append('mirroring must be false')
    if tr.get('placementMode') != 'forwardFaceNormal': e.append('placementMode')
    if tr.get('scale') != [1.0] * 3 or tr.get('minScale') != [0.5] * 3 or tr.get('maxScale') != [3.0] * 3: e.append('scale limits')
    for k in ('duplication', 'pickup', 'snapToCentre', 'autoRotate', 'airPlacement', 'translation', 'rotation', 'scaling', 'objects'):
        if tr.get(k) != tt.get(k): e.append('transform.' + k + ' differs from template')
    return e

ours = json.loads((R / 'parts/emitterPart.json').read_text(encoding='utf-8'))
check(TEMPLATE_BYTES == 1072, f'stock template boltOnBoxStowagePart.json is the mapped 1072-byte file ({TEMPLATE_BYTES})')
errs = part_errors(ours)
check(not errs, 'emitterPart.json schema: ' + (', '.join(errs) or 'template keysets + date, dynamic, no mirroring, forwardFaceNormal, tags, scale 0.5-3'))
check(str(uuid.UUID(GUID)) == GUID and GUID != template['guid'], 'fixed GUID is a canonical UUID distinct from the template')
others = []
# Our own installed copy (emitterPart.json, present once 0.1.0 is installed) shares the GUID by design; skip it by name.
stock_files = [f for f in PARTS.glob('*.json') if f.name != 'emitterPart.json']
for f in stock_files:
    m = re.search(r'"guid"\s*:\s*"([^"]+)"', f.read_text(encoding='utf-8', errors='replace'))
    if m and m[1].lower() == GUID: others.append(f.name)
check(not others, f'GUID unused by {len(stock_files)} stock part files (own installed emitterPart.json excluded)')
for name in ('EmitterEligibility.cs',):
    check(('EmitterRuntime.PartGuid' in (R / name).read_text(encoding='utf-8')), name + ' gates on the same PartGuid constant')
check(GUID in (R / 'checks/Eligibility/Program.cs').read_text(encoding='utf-8'), 'eligibility test uses the same GUID')
# Negative fixtures: the schema check must reject each mutation.
mutations = [
    ('dynamic false', lambda p: p['transform'].__setitem__('dynamic', False)),
    ('dynamic missing', lambda p: p['transform'].pop('dynamic')),
    ('mirroring true', lambda p: p['transform'].__setitem__('mirroring', True)),
    ('structure tag', lambda p: p['tags'].append('structure')),
    ('crewEquipment tag', lambda p: p['tags'].append('crewEquipment')),
    ('upFaceNormal', lambda p: p['transform'].__setitem__('placementMode', 'upFaceNormal')),
    ('extra root key', lambda p: p.__setitem__('mass', 1)),
    ('missing date', lambda p: p.pop('date')),
    ('era date name', lambda p: p.__setitem__('date', 'interwar')),
    ('other guid', lambda p: p.__setitem__('guid', str(uuid.uuid4()))),
    ('info mass key', lambda p: p['components'][0]['info'].__setitem__('mass', 1)),
    ('damageable true', lambda p: p['components'][0]['info'].__setitem__('damageable', True)),
]
for why, mutate in mutations:
    bad = copy.deepcopy(ours); mutate(bad)
    check(bool(part_errors(bad)), 'schema check rejects ' + why)

xml = (R / 'localization/emitterPart.xml').read_bytes()
check(xml[:2] == b'\xff\xfe', 'localization is UTF-16 LE with BOM')
text = xml.decode('utf-16')
m = re.fullmatch(r'﻿?<\?xml version="1.0" encoding="utf-16"\?>\s*<Part><name>([^<]*)</name><description>([^<]*)</description></Part>\s*', text)
check(bool(m), 'localization root <Part> with name and description')
check(m[1] == 'emitter' and m[2] == m[2].lower() and m[2], 'lowercase name/description: ' + m[2])
check(not re.search(r'(?i)interwar|cold war|ww2|era|key|press', m[2]), 'no era name and no key promise')

# Source rules (code only: comments and strings stripped).
def code(path):
    s = path.read_text(encoding='utf-8')
    s = re.sub(r'//[^\n]*', '', s); s = re.sub(r'/\*.*?\*/', '', s, flags=re.S)
    return re.sub(r'"(?:[^"\\]|\\.)*"', '""', s)
sources = [p for p in R.glob('*.cs')]
allcode = {p.name: code(p) for p in sources}
patch = sum(len(re.findall(r'\.Patch\(', (p.read_text(encoding='utf-8')))) for p in sources)
attr = sum(len(re.findall(r'\[\s*HarmonyPatch', (p.read_text(encoding='utf-8')))) for p in sources)
check(patch == 0 and attr == 0, f'.Patch( = {patch}, [HarmonyPatch] = {attr} in {len(sources)} sources')
dll = DLL.read_bytes()
check(b'HarmonyLib' not in dll and b'0Harmony' not in dll, 'built DLL references no Harmony assembly or namespace')
for token in ('SetCollisionMesh', 'RequestLoad', 'SetParent', 'Rigidbody>', 'Collider>', 'FindObjectsOfType', 'RaycastAll', 'SphereCastAll', 'AddForce', 'RequestModelLoad'):
    hits = [n for n, c in allcode.items() if token in c and not (token == 'Collider>' and False)]
    if token in ('Rigidbody>', 'Collider>'):
        hits = [n for n, c in allcode.items() if re.search(r'AddComponent<\w*' + token, c)]
    check(not hits, f'no {token} in code ({hits})')
for name in ('EmitterRuntime.cs', 'Simulation.cs', 'MeshAssets.cs'):
    check(not re.search(r'\.(Where|Select|OrderBy|ToList|ToArray|Any|First|Sum|Count)\(', allcode[name]), 'no LINQ in ' + name)
rt = allcode['EmitterRuntime.cs']
check('RaycastNonAlloc' in rt and ',-5,QueryTriggerInteraction.Ignore)' in rt, 'casts: RaycastNonAlloc, mask -5, triggers ignored')
check('layer=2' in rt and 'HideAndDontSave' in rt, 'pooled objects on layer 2, not saved')
raw = (R / 'EmitterRuntime.cs').read_text(encoding='utf-8')
reg = raw[raw.index('internal void RegisterIo()'):raw.index('private void SettingsChanged')]
check(reg.index('RegisterManagedOutput') < reg.index('RegisterPartSettings') and 'if(!registered)return;' in reg, 'RegisterPartSettings only after a successful RegisterManagedOutput')
check('"Eject","Stop"' in reg and 'DataKey="sprocket.emitter"' in raw and 'ComponentId,DataKey,Settings.Schema(false)' in reg, 'labels Eject/Stop, dataKey sprocket.emitter, 0.1.0 schema')
check(re.search(r'if\(native\.InteractionMesh!=arrow\)', raw) and 'void Update()' in raw and 'Adopt(' not in raw[raw.index('public void Update()'):raw.index('private void RetireScene')], 'mesh swap and SetVisible not called from Update')
plugin = (R / 'Plugin.cs').read_text(encoding='utf-8'); proj = (R / 'SprocketEmitter.csproj').read_text(encoding='utf-8')
check('Version="0.1.3"' in plugin.replace(' ', '') and '<Version>0.1.3</Version>' in proj, 'version 0.1.3 in Plugin and csproj')
check('"Meshes","AllowEmbeddedFallback",false' in plugin.replace(' ', ''), 'embedded fallback shapes are off by default')
check('"Meshes","UseNativeMaterial",true' in plugin.replace(' ', ''), 'native material on by default')
check('settings part=' not in raw, 'onChanged does not consume the scene trace budget')
check('Plugin.Instance.Log.LogInfo(FormattableString.Invariant($"[EMITTER] eject ' in raw, 'eject diagnostics bypass Trace')
check('"DetailedLogging",false' in plugin.replace(' ', '') and 'DetailedLogging = false' in (R / 'sprocket.emitter.cfg').read_text(), 'DetailedLogging off by default')
check('SoftDependency' in plugin and '"sprocket.emitter"' in plugin, 'plugin id sprocket.emitter, Mast soft dependency')
# Lookup by name (RENAME-MAP section 4): Mast GUID and facade type. The soft dependency, the lookup constant and the contract agree.
client = (R / 'IoApiClient.cs').read_text(encoding='utf-8')
mast = re.search(r'MastGuid\s*=\s*"([^"]+)"', client)
dep = re.search(r'\[BepInDependency\("([^"]+)"\s*,\s*BepInDependency\.DependencyFlags\.SoftDependency\)\]', plugin)
check(bool(mast) and mast[1] == 'sprocket.telescopicmast', 'IoApiClient.MastGuid is sprocket.telescopicmast')
check(bool(dep) and dep[1] == mast[1], 'BepInDependency (soft) names the same Mast GUID as IoApiClient.MastGuid')
check('GetType("SprocketTelescopicMast.Io.IoApi"' in client, 'facade is resolved as SprocketTelescopicMast.Io.IoApi')
stray = sorted({m for p in R.glob('*.cs') for m in re.findall(r'"((?:nl|com|local)\.[a-z0-9.]+)"', p.read_text(encoding='utf-8'))})
check(not stray, f'no reverse-domain plugin GUIDs left in the sources ({stray})')
cfg = (R / 'sprocket.emitter.cfg').read_text()
binds = re.findall(r'Config\.Bind\("(\w+)","(\w+)"', plugin)
section, keys = None, set()
for line in cfg.splitlines():
    if line.startswith('['): section = line.strip('[]')
    elif '=' in line: keys.add((section, line.split('=')[0].strip()))
check(set(binds) == keys, f'cfg keys match Config.Bind ({len(keys)})')
settings = (R / 'Settings.cs').read_text(encoding='utf-8')
schema = settings[settings.index('internal static string Schema('):]
base_schema = schema[:schema.index('if(effects)')]
base_count = 1 + len(re.findall(r'\b[NB]\("', base_schema))
ext_count = 1 + len(re.findall(r'\b[NB]\("', schema))
check((base_count, ext_count) == (11, 13), f'schema counts without/with effects: {base_count}/{ext_count} (runtime schema bytes validated in logic)')
check('FollowCaliber=B("follow_caliber",true)' in settings and 'FollowCaliber=true' in settings, 'missing follow_caliber uses the fixed true default')
check('float diameter=size.Diameter' in raw and 'diameter*.5f+.002f' in raw and 's.Spin,diameter*.5f,life' in raw, 'effective diameter used for outlet and landing radius')
check('follow={(s.FollowCaliber?' in raw and 'source={size.Source}' in raw and 'caliberMm={size.CaliberMm}' in raw and 'propellantMm={size.PropellantMm}' in raw, 'eject includes follow state, dimension source and millimeter values')
check('FollowCaliber=false,Item=item.Value' in plugin, 'pre-revision-2 cfg route remains manual as mapped')
# The owner-side comparison with the pre-0.1.3 source copy is not part of the fresh release (no such copy exists there).
check('follow_caliber' not in (R / 'sprocket.emitter.cfg').read_text(), 'follow_caliber is a per-part setting and adds no cfg key')
print(f'SUMMARY contract checks={checks} fail=0 patches={patch}')
