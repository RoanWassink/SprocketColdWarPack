import json, math
from collections import Counter, defaultdict
from export_game_mesh import *
meta=json.loads((P/'mesh-hashes.json').read_text()); failed=[]
def check(cond,msg):
 print(('PASS ' if cond else 'FAIL ')+msg)
 if not cond: failed.append(msg)
for name in NAMES:
 stl=P/f't90a_cupola_{name}.stl'; obj=P/f't90a_cupola_{name}.obj'; ts=read_stl(stl); edges=Counter(); directions=Counter(); vertex_faces=defaultdict(list)
 for i,t in enumerate(ts):
  for j in range(3):
   a,b=t[j],t[(j+1)%3]; edges[tuple(sorted((a,b)))]+=1; directions[(a,b)]+=1
   vertex_faces[a].append(i)
 check(all(v==2 for v in edges.values()) and all(directions[(a,b)]==directions[(b,a)] for a,b in edges),name+' watertight, consistently oriented edges')
 neighbors=defaultdict(set)
 for fs in vertex_faces.values():
  for f in fs: neighbors[f].update(fs)
 seen={0}; todo=[0]
 while todo:
  for n in neighbors[todo.pop()]-seen: seen.add(n); todo.append(n)
 check(len(seen)==len(ts),name+' one connected body')
 volume=sum(dot(a,cross(b,c)) for a,b,c in ts)/6
 check(volume>0,name+' positive outward signed volume')
 check(len(ts)<=(1900 if name=='base' else 800),name+' triangle budget (base <=1900, door <=800)')
 pairs={corner for line in obj.read_text().splitlines() if line.startswith('f ') for corner in line.split()[1:]}
 gameverts=len(pairs)
 check((1300<=gameverts<=1700) if name=='base' else (700<=gameverts<=850),name+' game vertex budget: '+str(gameverts))
 check(sha(stl.read_bytes())==meta[name]['stl_sha256'] and sha(obj.read_bytes())==meta[name]['obj_sha256'] and obj.read_bytes()==obj_data(ts),name+' current STL, OBJ and hashes match')
 pts=[p for t in ts for p in t]; lo=[min(p[i] for p in pts) for i in range(3)]; hi=[max(p[i] for p in pts) for i in range(3)]
 if name=='base': check(abs(lo[2]+25)<.01 and abs(hi[2]-245)<.01 and 440<=hi[1]<441 and abs(lo[1]+330)<.01 and abs(lo[0]+335)<.01 and abs(hi[0]-335)<.01,'base bounds: x -335..335, y -330..440.2, z -25..245 mm')
 else: check(abs(lo[1]+16)<.01 and abs(hi[1]-535)<.01 and lo[0]<0<hi[0] and lo[2]==0,'door local origin at hinge; disc near y=270, bottom z=0')
 gamebounds=[[min((p[0]/1000,p[2]/1000,p[1]/1000)[i] for p in pts) for i in range(3)],[max((p[0]/1000,p[2]/1000,p[1]/1000)[i] for p in pts) for i in range(3)]]
 check(gamebounds==meta[name]['bounds_m'],name+' reported game bounds')
 print(f'{name}: game_vertices={gameverts} triangles={len(ts)} bounds_m={gamebounds} volume_cm3={volume/1000:.2f}')
# The sight head starts at y=280 mm; maximize each actual door vertex's
# y coordinate over the continuous 0..100 degree rotation about H.
door_pts=[p for t in read_stl(P/'t90a_cupola_door.stl') for p in t]
furthest=-float('inf')
for x,y,z in door_pts:
 candidates=[0,math.radians(100)]
 root=math.atan2(-z,y)
 for k in range(-2,3):
  a=root+k*math.pi
  if 0<=a<=math.radians(100): candidates.append(a)
 furthest=max(furthest,*(-270+y*math.cos(a)-z*math.sin(a) for a in candidates))
check(furthest<280,'door remains clear of sight head throughout 0..100 degrees (minimum y clearance %.2f mm)'%(280-furthest))
if failed: raise SystemExit(1)
print('PASS all cupola checks')




