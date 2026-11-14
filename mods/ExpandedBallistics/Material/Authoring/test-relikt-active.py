from pathlib import Path
from collections import Counter
import argparse,json,math,sys
p=argparse.ArgumentParser();p.add_argument('--assets',required=True);p.add_argument('--report',required=True);a=p.parse_args();base=Path(__file__).resolve().parent;sys.argv=[str(base/'generate-relikt-active.py'),'--output',a.assets,'--report',a.report];scope={'__file__':str(base/'generate-relikt-active.py')};exec((base/'generate-relikt-active.py').read_text().split('report=[];plan=[]')[0],scope)
read,hit,edge,volume=[scope[k] for k in ['read','hit','edge','volume']];result=[]
def first_surface(tris,o,v):
 sub,dot,cross=[scope[k] for k in ['sub','dot','cross']];found=[]
 for tr in tris:
  e1=sub(tr[1],tr[0]);e2=sub(tr[2],tr[0]);p=cross(v,e2);det=dot(e1,p)
  if abs(det)<1e-12:continue
  q=sub(o,tr[0]);u=dot(q,p)/det;c=cross(q,e1);z=dot(v,c)/det;distance=dot(e2,c)/det
  if u>=-1e-9 and z>=-1e-9 and u+z<=1+1e-9 and distance>=-1e-9:
   n=cross(e1,e2);size=math.sqrt(dot(n,n));normal=tuple(x/size for x in n);angle=math.degrees(math.acos(min(1,abs(dot(normal,v)))));found.append((distance,normal,angle))
 return min(found,key=lambda x:x[0])

for family,cells in scope['families'].items():
 for cell in range(1,cells+1):
  name=f'{family}-active-stage-1-cell-{cell}.obj';old=read(scope['before']/name);new=read(Path(a.assets)/name);assert len(new)==12
  counts=Counter(tuple(sorted((tr[i],tr[(i+1)%3]))) for tr in new for i in range(3));assert all(v==2 for v in counts.values()) and volume(new)>0
  assert abs(volume(new)-volume(old))<2e-10 and abs(edge(new)-.013)<2e-9
  points=[p for tr in old for p in tr];cx=(min(p[0] for p in points)+max(p[0] for p in points))/2;cz=(min(p[2] for p in points)+max(p[2] for p in points))/2;origin=(cx,.13,cz);rows=[]
  cfg=dict(scope['settings']['families'][family]);cfg.update(cfg.get('cellOverrides',{}).get(str(cell),{}));axis={'X':0,'Z':2}[cfg['riseAxis']];other=2 if axis==0 else 0;sign=cfg['riseSign']
  spans=[max(p[i] for p in points)-min(p[i] for p in points) for i in range(3)]
  assert spans[axis]>=spans[other]-1e-9,f'Inclination is across the short axis: {family}/{cell}'
  for angle in [0,60,70,80,85]:
   t=math.radians(angle);v=[0,-math.cos(t),0];v[axis]=sign*math.sin(t);opposite=list(v);opposite[axis]*=-1;perpendicular=[0,-math.cos(t),0];perpendicular[other]=math.sin(t)
   assert hit(new,origin,v),f'Intended lengthwise centre ray missed {family}/{cell}/{angle}'
   rows.append({'angle':angle,'incomingAxis':cfg['riseAxis'],'incomingSign':sign,'intendedLengthwiseHit':True,'actualFirstSurfaceNormal':first_surface(new,origin,v)[1],'actualImpactDegrees':first_surface(new,origin,v)[2],'oppositeHit':hit(new,origin,opposite),'perpendicularHit':hit(new,origin,perpendicular)})
  assert not rows[-1]['oppositeHit'] and not rows[-1]['perpendicularHit']
  render=read(Path(a.assets)/f'{family}Stage1Cell{cell}-physical-intact.obj');oldrender=read(scope['before']/f'{family}Stage1Cell{cell}-physical-intact.obj');oldset={tuple(sorted(t)) for t in old};newset={tuple(sorted(t)) for t in new};assert [t for t in oldrender if tuple(sorted(t)) not in oldset]==[t for t in render if tuple(sorted(t)) not in newset]
  result.append({'family':family,'cell':cell,'closed':True,'volumeConserved':True,'sourceRenderDetailsPreserved':True,'directionalRays':rows})
Path(a.report,'DIRECTIONAL-CHECKS.json').write_text(json.dumps(result,indent=2));print(f'PASS {len(result)} cells: length-axis inclination, single solid, conserved volume/depth, preserved details, intended rays; opposite/perpendicular grazing misses')
