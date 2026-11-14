"""One-time stock clone authoring. Game files are read only."""
from pathlib import Path
import json, re, hashlib
ROOT=Path(__file__).resolve().parents[1]
GAME=Path(r'C:\Program Files (x86)\Steam\steamapps\common\Sprocket')
def stock(name):
    return json.loads(re.sub(r',\s*([}\]])',r'\1',(GAME/'Sprocket_Data/StreamingAssets/Parts'/name).read_text(encoding='utf-8-sig')))

p=stock('germanCupolaPart.json')
p.update(v='0.3',guid='b56e5c50-f099-4bf0-b031-93f19212b862',name='t90aCupola',order=69,date='1992.01.01',iconOffset=[0,0],iconRotation=[],iconScale=1)
p['tags']=['cupola','commander','ussr']
p['transform']['dynamic']=True
p['components'][0]['info'].update(cameraTrackPosition=[0,.4,0],objectiveLensPosition=[0,.149716,0],eyepiecePosition=[0,.149716,0])
p['components'][1]['info'].update(entrancePosition=[0,.24,0])
p['transform']['objects'][0]['localPosition']=[0,.24,-.27]
(ROOT/'parts/t90aCupolaPart.json').write_text(json.dumps(p,indent=2)+'\n',encoding='utf-8',newline='\n')
xml='<?xml version="1.0" encoding="utf-16"?>\n<Part><name>Cupola T-90A</name><description>Commander\'s cupola in the style of the T-90A. The lid is a static model.</description></Part>\n'
(ROOT/'localization/t90aCupola.xml').write_text(xml,encoding='utf-16')
hashes={f.name:hashlib.sha256(f.read_bytes()).hexdigest().upper() for f in (ROOT/'Assets/approved').iterdir() if f.is_file()}
(ROOT/'Assets/approved-sha256.json').write_text(json.dumps(hashes,indent=2)+'\n')
print('Authored stock clone, UTF-16 localization and approved asset hashes.')
