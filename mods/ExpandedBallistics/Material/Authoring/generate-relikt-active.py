from pathlib import Path
import json,hashlib,math,copy,argparse
authoring=Path(__file__).resolve().parent
parser=argparse.ArgumentParser(description='Regenerate reviewed Relikt active proxies from immutable authored geometry, never game files.')
parser.add_argument('--output',required=True);parser.add_argument('--report',required=True);parser.add_argument('--mirror-canonical',action='store_true');args=parser.parse_args()
settings=json.loads((authoring/'relikt-active-layouts.json').read_text())
assert settings['schemaVersion']==1 and settings['model']=='directional-single-slab'
root=Path(args.report).resolve();root.mkdir(parents=True,exist_ok=True);assets=Path(args.output).resolve();assets.mkdir(parents=True,exist_ok=True)
canonical=authoring.parent/'files/BepInEx/plugins/SprocketExpandedBallistics/era-assets';before=authoring/'baselines-relikt-flat';families={k:v['cells'] for k,v in settings['families'].items()}
def read(path,group='cassette'):
 v=[];f=[];g=''
 for l in path.read_text().splitlines():
  a=l.split()
  if not a:continue
  if a[0]=='g':g=a[1]
  if a[0]=='v':v.append(tuple(map(float,a[1:4])))
  if a[0]=='f' and g==group:f.append(tuple(v[int(x.split('/')[0])-1] for x in a[1:]))
 return f
def sub(a,b):return tuple(a[i]-b[i] for i in range(3))
def dot(a,b):return sum(x*y for x,y in zip(a,b))
def cross(a,b):return(a[1]*b[2]-a[2]*b[1],a[2]*b[0]-a[0]*b[2],a[0]*b[1]-a[1]*b[0])
def volume(tris):return sum(dot(t[0],cross(t[1],t[2]))/6 for t in tris)
def edge(tris):return min(math.sqrt(dot(sub(t[i],t[(i+1)%3]),sub(t[i],t[(i+1)%3]))) for t in tris for i in range(3))
def prism(poly,t):
 v=[(x,y,z) for y in [-t/2,t/2] for x,z in poly];n=len(poly);f=[]
 for i in range(1,n-1):f.extend([(v[0],v[i],v[i+1]),(v[n],v[n+i+1],v[n+i])])
 for i in range(n):j=(i+1)%n;f.extend([(v[i],v[n+j],v[j]),(v[i],v[n+i],v[n+j])])
 assert volume(f)>0
 return f
def inclined(old,family,cell):
 points=[p for tr in old for p in tr];lo=[min(p[i] for p in points) for i in range(3)];hi=[max(p[i] for p in points) for i in range(3)];centre=[(lo[i]+hi[i])/2 for i in range(3)];t=hi[1]-lo[1]
 cfg=dict(settings['families'][family]);cfg.update(cfg.get('cellOverrides',{}).get(str(cell),{}));axis={'X':0,'Z':2}[cfg['riseAxis']];sign=cfg['riseSign'];alpha=math.radians(cfg['angleDegrees']);co=math.cos(alpha);si=math.sin(alpha);span=hi[axis]-lo[axis]
 other=2 if axis==0 else 0
 assert span>=hi[other]-lo[other]-1e-9, f'Relikt inclination must follow each active cell length: {family}/{cell}'
 centre[1]=settings['lidBottomM']-settings['lidClearanceM']-span/2*si-t/2*co
 def rotate(p):
  q=list(p);u=p[axis]-centre[axis];y=p[1]-(lo[1]+hi[1])/2;q[axis]=centre[axis]+u*co-sign*y*si;q[1]=centre[1]+sign*u*si+y*co;return tuple(q)
 out=[tuple(rotate(p) for p in tr) for tr in old]
 assert abs(volume(out)-volume(old))<1e-10 and abs(edge(out)-t)<1e-9
 assert min(p[1] for tr in out for p in tr)>settings['mountClearanceM'] and max(p[1] for tr in out for p in tr)<settings['lidBottomM']
 assert all(lo[i]-1e-9<=p[i]<=hi[i]+1e-9 for tr in out for p in tr for i in [0,2])
 return out,{'riseAxis':cfg['riseAxis'],'riseSign':sign,'tiltDegrees':cfg['angleDegrees'],'centreY':centre[1],'parts':1,'volumeM3':volume(out),'shortestEdgeMm':edge(out)*1000,'cellBounds':[lo,hi],'incomingTravelAxis':cfg['riseAxis'],'incomingTravelSign':sign}
def hit(tris,o,v):
 for tri in tris:
  e1=sub(tri[1],tri[0]);e2=sub(tri[2],tri[0]);p=cross(v,e2);det=dot(e1,p)
  if abs(det)<1e-12:continue
  q=sub(o,tri[0]);u=dot(q,p)/det
  if u<-1e-9 or u>1+1e-9:continue
  c=cross(q,e1);z=dot(v,c)/det
  if z<-1e-9 or u+z>1+1e-9:continue
  distance=dot(e2,c)/det
  if distance>=-1e-9:return True
 return False
def write(path,tris):
 assert len(tris)*3<=2000
 lines=['# Sprocket directional ERA proxy; one inclined paid-volume-preserving closed slab.','g cassette']
 for tr in tris:
  for p in tr:lines.append('v '+' '.join(f'{x:.9f}' for x in p))
 lines += [f'f {3*i+1} {3*i+2} {3*i+3}' for i in range(len(tris))];path.write_text('\n'.join(lines)+'\n')
report=[];plan=[]
for family,cells in families.items():
 for cell in range(1,cells+1):
  collision=f'{family}-active-stage-1-cell-{cell}.obj';visual=f'{family}Stage1Cell{cell}-physical-intact.obj'
  old=read(before/collision);new,meta=inclined(old,family,cell);render=read(before/visual)
  # Remove exactly the old active solid, preserving every existing cover/detail triangle.
  oldset={tuple(sorted(tr)) for tr in old};lid=[tr for tr in render if tuple(sorted(tr)) not in oldset]
  assert len(render)-len(lid)==len(old)
  write(assets/collision,new);write(assets/visual,new+lid)
  checks=[];lo,hi=meta['cellBounds'];cx=(lo[0]+hi[0])/2;cz=(lo[2]+hi[2])/2
  for angle in [0,60,70,80,85]:
   for azimuth in [0,45,90,135,180,225,270,315]:
    a=math.radians(angle);z=math.radians(azimuth);v=(math.sin(a)*math.cos(z),-math.cos(a),math.sin(a)*math.sin(z));oldhit=hit(old,(cx,.13,cz),v);newhit=hit(new,(cx,.13,cz),v);checks.append({'angle':angle,'azimuth':azimuth,'oldHit':oldhit,'newHit':newhit})
  report.append({'family':family,'cell':cell,**meta,'centreRays':checks})
  for name in [collision,visual]:
   staged=assets/name
   if args.mirror_canonical:canonical.mkdir(parents=True,exist_ok=True);(canonical/name).write_bytes(staged.read_bytes())
   plan.append({'Source':str(staged),'TargetRelative':'BepInEx/plugins/SprocketExpandedBallistics/era-assets/'+name,'BeforeSHA256':hashlib.sha256((before/name).read_bytes()).hexdigest().upper(),'AfterSHA256':hashlib.sha256(staged.read_bytes()).hexdigest().upper()})
(root/'PLAN.json').write_text(json.dumps({'Files':plan,'Retire':[],'assetOnly':True},indent=2));(root/'GEOMETRY-CHECKS.json').write_text(json.dumps(report,indent=2));print([(r['family'],r['cell'],sum(x['newHit'] for x in r['centreRays']),sum(x['oldHit'] for x in r['centreRays'])) for r in report])
