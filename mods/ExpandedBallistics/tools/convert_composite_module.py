from pathlib import Path
import json, hashlib, collections, shutil
src=Path(__file__).resolve().parents[1]
original=src/'assets'/'source'/'module.obj'  # the checked module mesh of the asset pipeline
assets=src/'Material/files/BepInEx/plugins/SprocketExpandedBallistics/era-assets'; assets.mkdir(parents=True,exist_ok=True)
verts=[]; faces=collections.defaultdict(list); group=''
for line in original.read_text().splitlines():
 p=line.split()
 if not p: continue
 if p[0]=='v': verts.append(tuple(map(float,p[1:4])))
 if p[0]=='g': group=p[1]
 if p[0]=='f':
  ids=[int(v.split('/')[0])-1 for v in p[1:]]
  for i in range(1,len(ids)-1): faces[group].append([ids[0],ids[i],ids[i+1]])
assert set(faces)=={'housing','cover','dark'}
verts=[(-y,x+.027,z) for x,y,z in verts] # Rz(+90), det +1; approved rear bounds X=-.027 become Y=0
lo=[min(v[i] for v in verts) for i in range(3)]; hi=[max(v[i] for v in verts) for i in range(3)]
edges=collections.Counter(tuple(sorted((a,b))) for ff in faces.values() for f in ff for a,b in zip(f,f[1:]+f[:1]))
assert all(n==2 for n in edges.values()), collections.Counter(edges.values())
# The engine loader accepts triangles and bare positive indices. Preserve coordinates,
# vertex order, winding, group identity and every face; remove only unused OBJ attributes.
lines=['# Lossless checked module conversion; metres, +Y exterior, -X up, +Z front; Rz(+90)']
lines+=['v '+' '.join(format(x,'.6f') for x in v) for v in verts]
for g,ff in faces.items():
 lines+=['g '+g]+['f '+' '.join(str(i+1) for i in f) for f in ff]
gameobj=assets/'compositeSideModule.obj'; gameobj.write_text('\n'.join(lines)+'\n',encoding='utf-8')
# Closed collision box uses exactly the render bounds. No per-detail collision or extra mass.
box=[(x,y,z) for x in (lo[0],hi[0]) for y in (lo[1],hi[1]) for z in (lo[2],hi[2])]
quads=[(0,1,3,2),(4,6,7,5),(0,4,5,1),(2,3,7,6),(0,2,6,4),(1,5,7,3)]
bb=['# Closed collision envelope, identical render bounds','g mount']+['v '+' '.join(format(x,'.6f') for x in v) for v in box]
bb+=['f '+' '.join(str(i+1) for i in t) for q in quads for t in ((q[0],q[1],q[2]),(q[0],q[2],q[3]))]
(assets/'compositeSideModule-collision.obj').write_text('\n'.join(bb)+'\n')
manifest={'source':original.relative_to(src).as_posix(),'sourceSha256':hashlib.sha256(original.read_bytes()).hexdigest().upper(),'convertedSha256':hashlib.sha256(gameobj.read_bytes()).hexdigest().upper(),'vertices':len(verts),'triangles':sum(map(len,faces.values())),'nonManifoldEdges':sum(n!=2 for n in edges.values()),'min':lo,'max':hi,'size':[hi[i]-lo[i] for i in range(3)],'groups':{g:len(ff) for g,ff in faces.items()},'paintGroups':['housing','cover'],'darkGroups':['dark']}
manifest.update(rotation=[[0,-1,0],[1,0,0],[0,0,1]],determinant=1,translation=[0,.027,0])
(src/'assets/compositeSideModule-mesh.json').write_text(json.dumps(manifest,indent=2)+'\n')
config={'date':'1990.01.01','label':'Passive composite side module','layers':[{'name':'backplate','thicknessMm':40,'density':7850,'keMassEfficiency':1,'heatMassEfficiency':1},{'name':'composite core','thicknessMm':200,'density':2500,'keMassEfficiency':2.2,'heatMassEfficiency':5},{'name':'passive cover plates','thicknessMm':50,'density':7850,'keMassEfficiency':1,'heatMassEfficiency':1},{'name':'edge strip allowance','thicknessMm':10,'density':7850,'keMassEfficiency':1,'heatMassEfficiency':1}],'spallFactor':0.0008,'costMultiplier':9}
config.update(minFootprintScale=.5,maxFootprintScale=8,era={'enabled':True,'responseId':'kontakt5Active','materialId':'kontakt5Active','activeThicknessMm':13,'activeDensity':3171.788731181019,'activeRhaFactor':.15,'activeSpallFactor':.25,'housingWallMm':2,'coverDepthMm':50,'panelWidthM':.294,'panelLengthM':.292,'columns':2,'rows':3})
(src/'assets/compositeSideModule.default.json').write_text(json.dumps(config,indent=2)+'\n')
area=(hi[0]-lo[0])*(hi[2]-lo[2]); e=config['era']
panelarea=e['columns']*e['rows']*e['panelWidthM']*e['panelLengthM']
housingvolume=e['columns']*e['rows']*(e['panelWidthM']*e['panelLengthM']*e['coverDepthMm']*.001-(e['panelWidthM']-2*e['housingWallMm']*.001)*(e['panelLengthM']-2*e['housingWallMm']*.001)*(e['coverDepthMm']-2*e['housingWallMm'])*.001)
mass=area*sum(l['thicknessMm']*.001*l['density'] for l in config['layers'] if l['name']!='passive cover plates')+housingvolume*7850+panelarea*e['activeThicknessMm']*.001*e['activeDensity']
ke=sum(l['thicknessMm']*l['density']/7850*l['keMassEfficiency'] for l in config['layers'] if l['name']!='passive cover plates')+(housingvolume*1000+panelarea*e['activeThicknessMm']*e['activeRhaFactor'])/area
part=json.loads((src/'Material/files/Sprocket_Data/StreamingAssets/Parts/reliktPanelTurretV1Part.json').read_text())
part.update(guid='a95bc652-39c6-51ad-9f52-ecb022370001',name='compositeSideModule',date=config['date'])
part['components']=part['components'][:1]; c=part['components'][0]; c['fileID']='compositeSideModule'; c['info'].update(durableMaterialID='compositeSideModuleMaterial',mass=mass)
# Continuous X/Z footprint with fixed Y depth, like the other placed panels.
part['transform'].update(placementMode='upFaceNormal',minScale=[.5,1,.5],maxScale=[8,1,8])
def put(rel,value):
 p=src/'Material/files'/rel;p.parent.mkdir(parents=True,exist_ok=True);p.write_text(json.dumps(value,indent=2)+'\n')
put('Sprocket_Data/StreamingAssets/Parts/compositeSideModulePart.json',part)
put('Sprocket_Data/StreamingAssets/Technology/compositeSideModuleMaterial.json',{'v':'0.0','id':'','type':'compositeSideModuleMaterial','date':config['date'],'properties':{'density':mass/(area*(hi[1]-lo[1])),'rhaFactor':ke/((hi[1]-lo[1])*1000),'spallFactor':config['spallFactor'],'costMultiplier':config['costMultiplier']}})
put('BepInEx/config/sprocket.expandedballistics.composite-module.json',config)
p=src/'Material/files/Sprocket_Data/StreamingAssets/Localization/en-UK/Parts/compositeSideModule.xml';p.parent.mkdir(parents=True,exist_ok=True)
p.write_text('<?xml version="1.0" encoding="utf-8"?>\n<Part><name>Passive composite side module</name><description>Passive composite side armour. Housing and covers follow vehicle paint; rubber and bolts stay dark. Scale X/Z from 0.5 to 8; Y is fixed thickness. Local +Y is the exterior. Includes configurable heavy ERA covers.</description></Part>\n')
print(json.dumps({'mesh':manifest,'massKg':mass},indent=2))
