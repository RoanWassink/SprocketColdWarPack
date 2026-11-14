"""Source/asset contract for Sprocket Visual Effects 0.4.5. Pure file checks; the game folder is only read.

Usage: py -I checks/check_contract.py [--game <Sprocket folder>] [--native-methods <all-native-methods.json>]

--game            Sprocket folder, read only. Needed for the stock lamp part JSON and the stock mesh bundle hashes.
--native-methods  Optional dump of all native methods (RVA, Name, Length); when given, the unique-RVA/length check of
                  VehicleObjectEditor.OnGUI (the inspector hook) runs, otherwise that one check is skipped.
"""
from pathlib import Path
import argparse, copy, hashlib, json, re, struct, sys, xml.etree.ElementTree as ET

ap = argparse.ArgumentParser()
ap.add_argument('--game', default=r'C:\Program Files (x86)\Steam\steamapps\common\Sprocket')
ap.add_argument('--native-methods', default=None)
opts = ap.parse_args()

mod = Path(__file__).resolve().parents[1]
game = Path(opts.game)
partsdir = game / 'Sprocket_Data/StreamingAssets/Parts'
bundles = game / 'Sprocket_Data/StreamingAssets/aa/StandaloneWindows64'


def check(ok, label):
    if not ok:
        raise AssertionError(label)
    print('PASS ' + label)


def h(p):
    return hashlib.sha256(p.read_bytes()).hexdigest()


def sources(d):
    return list(d.glob('*.cs'))


def count(d, pattern):
    return sum(len(re.findall(pattern, p.read_text(encoding='utf-8'))) for p in sources(d))


forms = ['round', 'rect', 'marker']
guids = ['7b696e26-98bd-4e6d-ad6a-9009814b0c01', '7b696e26-98bd-4e6d-ad6a-9009814b0c02', '7b696e26-98bd-4e6d-ad6a-9009814b0c03']

# 1. Part JSON = stock lamp, except the agreed fields (GUID, name, +vfxLamp tag, componentId; marker default scale).
for i, form in enumerate(forms):
    part = json.loads((mod / f'parts/vfxLamp{form.title()}Part.json').read_text())
    template = json.loads((partsdir / ('headlightPart.json' if form == 'round' else 'tailLightPart.json')).read_text(encoding='utf-8-sig'))
    expected = copy.deepcopy(template)
    expected['guid'] = guids[i]
    expected['name'] = 'Lamp ' + form
    expected['tags'] = template['tags'] + ['vfxLamp']
    expected['components'][0]['fileID'] = 'vfxLamp'
    if form == 'marker':
        expected['transform']['scale'] = [0.6, 0.6, 0.6]
    check(part == expected, form + ' equals stock ' + ('headlightPart' if form == 'round' else 'tailLightPart') + ' except guid/name/tag vfxLamp/componentId' + (' and default scale 0.6' if form == 'marker' else ''))
    t = part['transform']
    check(t['dynamic'] is False and t['mirroring'] is True and t['placementMode'] == ('forwards' if form == 'round' else 'backwards') and t['minScale'] == template['transform']['minScale'] and t['maxScale'] == template['transform']['maxScale'], form + ' dynamic:false, native mirroring, stock placementMode and scale limits')
    check(part['components'][0]['info'] == template['components'][0]['info'], form + ' stock meshGuid/materialGuid and model info (autoFlip/flipping) unchanged')
    for tags in [['hatch'], ['cupola'], ['vent', 'engine'], ['exhaust', 'engine'], ['structure'], ['trackPanel'], ['fender']]:
        check(set(part['tags']).isdisjoint(tags), form + ' no tag of variant list ' + repr(tags))
    xml = (mod / f'localization/vfxLamp{form.title()}.xml').read_bytes()
    doc = ET.fromstring(xml)
    d = doc.findtext('description')
    check(xml[:2] == b'\xff\xfe' and doc.findtext('name') == 'LAMP ' + form.upper() and 'Right-click' in d and 'stock' in d and 'flush' not in d, form + ' UTF-16 description: stock model + right-click section')
    check((mod / f'localization/Lamp {form}.xml').read_bytes() == xml, form + ' readable-name XML equals the vfxLamp XML')
scaled = [n for n in ['shortTravelLockPart.json', 'squareMantletPart.json', 'ammoRackPart.json'] if (json.loads((partsdir / n).read_text(encoding='utf-8-sig'))['transform']['scale'] != [1.0, 1.0, 1.0])]
check(len(scaled) == 3, 'stock parts use transform.scale as a non-1 default scale (shortTravelLock 1.5, mantlets/ammo rack 0.5): marker 0.6 follows that pattern')

# 2. Static, no native setter.
exec((Path(__file__).resolve().parent / 'check-static-parts.py').read_text())
for name in ['LampMesh.cs', 'ExteriorPaint.cs']:
    check(not (mod / name).exists(), 'no runtime file ' + name)
assets = sorted(p.name for p in (mod / 'lamp-assets').iterdir())
check(assets == ['lamp-halo.png', 'lamp-marker-icon.png', 'lamp-rect-icon.png', 'lamp-round-icon.png'], 'lamp-assets = halo + three icons (no own lamp meshes)')
for n in assets:
    data = (mod / 'lamp-assets' / n).read_bytes()
    w, hgt = struct.unpack('>II', data[16:24])
    check(data[:8] == b'\x89PNG\r\n\x1a\n' and w > 0 and hgt > 0, n + ' is a PNG ' + str(w) + 'x' + str(hgt))
halo = (mod / 'lamp-assets/lamp-halo.png').read_bytes()
check(struct.unpack('>II', halo[16:24]) == (128, 128), 'lamp-halo.png is 128x128 (LampRuntime.PrepareHalo requires it)')

# 3. Own objects are never under the vehicle; the native merger cannot pick them up.
rt = (mod / 'LampRuntime.cs').read_text(encoding='utf-8')
check(rt.count('new GameObject(') == 2 and rt.count('SetParent(') == 2 and rt.count('go.transform.SetParent(transform,false)') == 1 and rt.count('Root.transform.SetParent(parent,false)') == 1
      and len(re.findall(r'new Glow\(', rt)) == 2 and len(re.findall(r'new Glow\("[^"]+",transform,', rt)) == 2, 'every VE lamp GameObject (lens/halo Glow, pooled light) is parented to the LampRuntime object only')
check('.parent=' not in rt.replace(' ', '') and 'VehicleTransform' not in rt.split('class Lamp', 1)[1].split('Alive', 1)[0], 'no reparenting to a vehicle transform')
plugin = (mod / 'Plugin.cs').read_text(encoding='utf-8')
check('Lamps=AddComponent<LampRuntime>()' in plugin, 'LampRuntime lives on the BepInEx plugin object, not on a vehicle')
code_only = re.sub(r'//[^\n]*', '', rt)
check(set(re.findall(r'\w*Register\w*', code_only)) <= {'RegisterIo', 'RegisterManagedOutput', 'RegisterPartIcon', 'RegisterOutput', 'outputRegistered'}, 'no native renderer registration in code (only the Mast IO registrations); DLL allowlist in checks/Program.cs proves the same on the binary')

# 4. Lens geometry: constants = measurement of the installed stock meshes (checks/lens/l9-lens.json); disc inside the bezel, in front of the glass.
m = json.loads((mod / 'checks/lens/l9-lens.json').read_text())
for f in ('round', 'rect'):
    check(h(bundles / m['forms'][f]['bundle']) == m['forms'][f]['bundle_sha256'], f + ' stock mesh measurement belongs to the installed stock mesh bundle (SHA256)')
code = (mod / 'LampLens.cs').read_text(encoding='utf-8')
cs = [[float(x.rstrip('f')) for x in v.split(',')] for v in re.findall(r'new\(([^)]*)\)', code.split('Centre=', 1)[1].split(';', 1)[0])]
rs = [float(x.rstrip('f')) for x in re.search(r'Radius=\{([^}]*)\}', code).group(1).split(',')]
for i, f in enumerate(forms):
    g = m['forms'][f]
    check(all(abs(a - b) < 6e-5 for a, b in zip(cs[i], g['centre'])) and abs(rs[i] - g['radius']) < 6e-5, f + ' LampLens centre/radius = measured stock lens ' + repr(g['centre']) + ' r=' + str(g['radius']))
    check(g['fit_max_residual'] < 1e-3 and g['ring_vertices'] >= 12, f + ' lens ring is an exact circle (' + str(g['ring_vertices']) + ' vertices)')
    check(g['radius'] < g['bezel_inner_radius'] and g['centre'][2] > g['lens_front_z'], f + ' glow disc inside the bezel and in front of the glass')
check('ModelPosition' in rt and 'ModelRotation' in rt and 'GetModelScale()' in rt and 'LampLens.TryWorld' in rt, 'lens pose from the native model chain incl. signed scale')

# 5. Hooks: exactly two registrations (muzzle observer, inspector postfix).
check(count(mod, r'\.Patch\s*\(') == 2, 'Harmony .Patch registrations = 2 (muzzle observer + inspector OnGUI postfix)')
for pattern in [r'\[HarmonyPatch', r'\.PatchAll\s*\(', r'HarmonyPrefix', r'HarmonyTranspiler', r'__result']:
    check(count(mod, pattern) == 0, 'no other hook mechanism ' + pattern)
if opts.native_methods:
    rows = json.loads(Path(opts.native_methods).read_text(encoding='utf-8-sig'))
    same = [r for r in rows if r['RVA'] == '0x1F92E50']
    target = [r for r in rows if r['Name'].endswith('VehicleObjectEditor::OnGUI(Sprocket.UI.IGUILayout)')]
    check(len(target) == 1 and target[0]['Length'] == '0x36D' and len(same) == 1, 'OnGUI RVA 0x1F92E50 length 0x36D, unique (no ICF)')
else:
    print('SKIP OnGUI RVA uniqueness (no --native-methods given)')

# 6. Light/glow behaviour.
check('SetFloat(n,v)' in rt and '("_EmissiveExposureWeight",0)' in rt and 'LampLook.Emission(' in rt, 'lens disc: exposure weight 0 + capped LampLook emission (red stays red)')
check('MaxOnLogs=40' in rt and 'Lamp ON part=' in rt and 'light={light}' in rt, 'bounded Lamp ON log line with real-light assignment')
check('MaxHalos=64' in rt and 'MaxLenses=128' in rt and 'Math.Clamp(p.MaxLamps.Value,0,128)' in rt, 'bounded halos, lens discs and real-light budget')
check('Infrared' in rt and 'glowing.Add(lamp)' in rt, 'IR / off lamps draw no glow (stock lens shows)')
find = rt.split('internal Lamp? Find(', 1)[1].split('internal bool IsLampObject', 1)[0]
check('Ensure(' not in find and 'lamps.Add' not in find, 'inspector lookup never creates cache entries')
ensure = rt.split('private Lamp? Ensure(', 1)[1].split('private void Scan()', 1)[0]
check(ensure.index('LampEligibility.Check(model)') < ensure.index('lamps.TryGetValue'), 'Ensure: eligibility before cache')

# 7. Plumbing: UTF-8 sources, the Mast as a reflection-only soft dependency under its fresh name.
for p in sources(mod):
    check('\ufffd' not in p.read_text(encoding='utf-8'), 'UTF-8 source ' + p.name)
client = (mod / 'IoApiClient.cs').read_text(encoding='utf-8')
check('[BepInDependency("sprocket.telescopicmast",BepInDependency.DependencyFlags.SoftDependency)]' in plugin
      and 'public const string MastGuid = "sprocket.telescopicmast";' in client
      and 'GetType("SprocketTelescopicMast.Io.IoApi", false)' in client
      and 'SprocketTelescopicMast.dll' not in (mod / 'SprocketVisualEffects.csproj').read_text(), 'Mast stays a reflection-only soft dependency: GUID sprocket.telescopicmast in Plugin.cs and IoApiClient.cs, facade type SprocketTelescopicMast.Io.IoApi')
check('[BepInPlugin("sprocket.visualeffects","Sprocket Visual Effects","0.4.5")]' in plugin and 'new Harmony("sprocket.visualeffects.inspector")' in plugin and 'new Harmony("sprocket.visualeffects.observer")' in plugin, 'plugin GUID and both Harmony ids start with sprocket.visualeffects')
print('PASS Visual Effects contract')
