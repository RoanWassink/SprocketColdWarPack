"""Independent OBJ reader, strict schema and compiled-data export checks."""
from pathlib import Path
import argparse, json, re, hashlib, math, copy, struct, xml.etree.ElementTree as ET
ap=argparse.ArgumentParser(description='Contract checks. The game folder is only read.')
ap.add_argument('--out',required=True,help='folder with <name>-embedded.json written by checks/Checks.csproj')
ap.add_argument('--game',default=r'C:\Program Files (x86)\Steam\steamapps\common\Sprocket',help='Sprocket folder (read only: stock part JSON)')
args=ap.parse_args()
ROOT=Path(__file__).resolve().parents[1]
GAME=Path(args.game)
OUT=Path(args.out)
def check(ok,text):
    assert ok,text
    print('PASS '+text)
def read_stock(name):
    return json.loads(re.sub(r',\s*([}\]])',r'\1',(GAME/'Sprocket_Data/StreamingAssets/Parts'/name).read_text(encoding='utf-8-sig')))
stock=read_stock('germanCupolaPart.json'); v3=read_stock('smokeLauncherBankPart.json')
part=json.loads((ROOT/'parts/t90aCupolaPart.json').read_text())
def validate(p):
    assert set(p)==set(v3)
    assert p['v']=='0.3' and p['date']=='1992.01.01' and type(p['order']) is int
    assert p['guid']=='b56e5c50-f099-4bf0-b031-93f19212b862' and p['name']=='t90aCupola'
    assert p['tags']==['cupola','commander','ussr']
    assert p['iconOffset']==[0,0] and p['iconRotation']==[] and p['iconScale']==1
    expected=copy.deepcopy(stock)
    expected.update({k:p[k] for k in ('v','guid','name','tags','order','date','iconOffset','iconRotation','iconScale')})
    expected['transform']['dynamic']=True
    expected['components'][0]['info'].update(cameraTrackPosition=[0,.4,0],objectiveLensPosition=[0,.149716,0],eyepiecePosition=[0,.149716,0])
    expected['components'][1]['info']['entrancePosition']=[0,.24,0]
    expected['transform']['objects'][0]['localPosition']=[0,.24,-.27]
    assert p==expected,'changed stock field beyond explicit contract'
    for m in (p['components'][2],p['transform']['objects'][0]['components'][0]):
        assert m['info']['v']=='0.4' and 'mass' not in m['info'] and m['info']['durableMaterialID'] is None
        assert set(m['info'])==set(stock['components'][2]['info'])
validate(part);check(True,'exact root v0.3/native model v0.4 schema, IDs, stock fallback and permissions')
mutations=[('root 0.0 + date',lambda p:p.update(v='0.0')),
 ('model 0.4 + mass',lambda p:p['components'][2]['info'].update(mass=10)),
 ('missing tag',lambda p:p['tags'].remove('commander')),
 ('structure tag',lambda p:p['tags'].append('structure')),
 ('wrong GUID',lambda p:p.update(guid='foreign')),
 ('static root',lambda p:p['transform'].update(dynamic=False)),
 ('missing icon field',lambda p:p.pop('iconOffset')),
 ('foreign mesh',lambda p:p['components'][2]['info'].update(meshGuid='invented'))]
for text,mutate in mutations:
    bad=copy.deepcopy(part);mutate(bad)
    try:validate(bad)
    except AssertionError:check(True,'negative schema: '+text)
    else:raise AssertionError('negative case accepted: '+text)
xml=(ROOT/'localization/t90aCupola.xml').read_bytes()
check(xml[:2]==b'\xff\xfe','real UTF-16 LE BOM')
tree=ET.fromstring(xml)
check([c.tag for c in tree]==['name','description'] and tree.findtext('name')=='Cupola T-90A' and tree.findtext('description')=="Commander's cupola in the style of the T-90A. The lid is a static model.",'localization filename key and lowercase elements')
hashes=json.loads((ROOT/'Assets/approved-sha256.json').read_text())
for filename,expected in hashes.items():
    f=ROOT/'Assets/approved'/filename
    check(hashlib.sha256(f.read_bytes()).hexdigest().upper()==expected,'approved bytes '+filename)
meta=json.loads((ROOT/'Assets/approved/mesh-hashes.json').read_text())
def sub(a,b):return tuple(x-y for x,y in zip(a,b))
def cross(a,b):return (a[1]*b[2]-a[2]*b[1],a[2]*b[0]-a[0]*b[2],a[0]*b[1]-a[1]*b[0])
def dot(a,b):return sum(x*y for x,y in zip(a,b))
for name,count,tris in [('base',1698,1744),('door',763,796)]:
    path=ROOT/f'Assets/approved/t90a_cupola_{name}.obj'
    vs=[];ns=[];faces=[]
    for line in path.read_text().splitlines():
        fields=line.split()
        if not fields:continue
        if fields[0]=='v':vs.append(tuple(float(x) for x in fields[1:]))
        elif fields[0]=='vn':ns.append(tuple(float(x) for x in fields[1:]))
        elif fields[0]=='f':
            assert len(fields)==4
            faces.append([tuple(int(x)-1 for x in f.split('//')) for f in fields[1:]])
    pairs=list(dict.fromkeys(p for face in faces for p in face));index={p:i for i,p in enumerate(pairs)}
    check(len(pairs)==count and len(faces)==tris,name+' approved v//vn counts')
    embedded=json.loads((OUT/f'{name}-embedded.json').read_text())
    def f32(x):return struct.unpack('<f',struct.pack('<f',x))[0]
    pos=[f32(x) for i,j in pairs for x in vs[i]];norm=[f32(x) for i,j in pairs for x in ns[j]]
    indices=[index[p] for face in faces for p in face]
    check(all(a==f32(b) for a,b in zip(pos,embedded['Positions'])) and len(pos)==len(embedded['Positions']),name+' compiled positions match independent OBJ parse')
    check(all(a==f32(b) for a,b in zip(norm,embedded['Normals'])) and len(norm)==len(embedded['Normals']) and indices==embedded['Indices'],name+' compiled normals, indices and winding match independent parse')
    check(embedded['ObjSha256'].lower()==hashlib.sha256(path.read_bytes()).hexdigest()==meta[name]['obj_sha256'],name+' source, approved and embedded SHA256')
    check(all(math.isfinite(x) for v in vs+ns for x in v),name+' finite coordinates')
    volume=0
    for face in faces:
        (a,na),(b,nb),(c,nc)=face
        normal=cross(sub(vs[b],vs[a]),sub(vs[c],vs[a]))
        assert dot(normal,normal)>1e-20
        assert all(dot(normal,ns[ni])>0 for vi,ni in face)
        volume+=dot(vs[a],cross(vs[b],vs[c]))/6
    check(volume>0,name+' nonzero triangles, outward winding, positive volume')
    bounds=[[min(v[i] for v in vs) for i in range(3)],[max(v[i] for v in vs) for i in range(3)]]
    check(all(abs(x-y)<1e-7 for a,b in zip(bounds,meta[name]['bounds_m']) for x,y in zip(a,b)),name+' measured metre bounds')
check(part['transform']['objects'][0]['localPosition']==[0,.24,-.27],'hinge H converted x,z,y from SCAD mm')
# Static mutation budget: no forbidden runtime write or hook in own code.
source='\n'.join(p.read_text(encoding='utf-8-sig') for p in ROOT.glob('*.cs'))
for banned in (r'\.Patch\s*\(',r'HarmonyPatch',r'SetCollisionMesh\s*\(',r'ForceRebuild\s*\(',r'SetMass\s*\(',r'SetVisible\s*\(',r'SetMaterials?\s*\(',r'\.cachedMass\s*=',r'\.ModelLocalPosition\s*='):
    check(not re.search(banned,source),'no forbidden runtime write/hook '+banned)
own_hooks=len(re.findall(r'\.Patch\s*\(',source))
check(own_hooks==0,f'Harmony .Patch calls in own code: {own_hooks}, new cupola hooks=0')
png=(ROOT/'Assets/approved/t90a-cupola-icon.png').read_bytes()
check(struct.unpack('>II',png[16:24])==(256,256),'icon 256x256')
check(hashlib.sha256(png).hexdigest().upper() in (ROOT/'CupolaMeshData.cs').read_text(),'icon hash embedded')
check('File.ReadLines' not in source and '.obj"' not in (ROOT/'CupolaRuntime.cs').read_text(),'no runtime OBJ disk loader')
print('PASS all independent contract checks; no live-game assertion.')
