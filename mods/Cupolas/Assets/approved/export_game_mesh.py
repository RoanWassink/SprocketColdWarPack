import math, json, hashlib, struct
from pathlib import Path
from collections import defaultdict, deque
P=Path(__file__).resolve().parent
NAMES=('base','door')
def sha(b): return hashlib.sha256(b).hexdigest()
def read_stl(path):
 b=path.read_bytes()
 if len(b)>=84 and len(b)==84+50*struct.unpack_from('<I',b,80)[0]:
  return [tuple(tuple(struct.unpack_from('<3f',b,84+i*50+12+j*12)) for j in range(3)) for i in range(struct.unpack_from('<I',b,80)[0])]
 vals=[tuple(map(float,l.split()[1:4])) for l in b.decode('ascii').splitlines() if l.strip().startswith('vertex ')]
 return [tuple(vals[i:i+3]) for i in range(0,len(vals),3)]
def cross(a,b): return (a[1]*b[2]-a[2]*b[1],a[2]*b[0]-a[0]*b[2],a[0]*b[1]-a[1]*b[0])
def sub(a,b): return tuple(x-y for x,y in zip(a,b))
def dot(a,b): return sum(x*y for x,y in zip(a,b))
def unit(a):
 d=math.sqrt(dot(a,a)); return tuple(x/d for x in a)
def obj_data(tris):
 # Swap y/z and reverse winding, preserving outward orientation.
 faces=[tuple((p[0]/1000,p[2]/1000,p[1]/1000) for p in (t[0],t[2],t[1])) for t in tris]
 verts=list(dict.fromkeys(p for f in faces for p in f)); ids={v:i for i,v in enumerate(verts)}
 faces=[tuple(ids[p] for p in f) for f in faces]
 normals=[unit(cross(sub(verts[b],verts[a]),sub(verts[c],verts[a]))) for a,b,c in faces]
 edge=defaultdict(list); incident=defaultdict(list)
 for i,f in enumerate(faces):
  for v in f: incident[v].append(i)
  for j in range(3): edge[tuple(sorted((f[j],f[(j+1)%3])))].append(i)
 # Connected smooth fans, split at edges over the crease angle.
 adj=defaultdict(set); threshold=math.cos(math.radians(50))
 for (a,b),fs in edge.items():
  if len(fs)==2 and dot(normals[fs[0]],normals[fs[1]])>=threshold-1e-9:
   for v in (a,b): adj[(v,fs[0])].add(fs[1]); adj[(v,fs[1])].add(fs[0])
 outnorm=[]; corners={}
 for v,fs in incident.items():
  pending=set(fs)
  while pending:
   q=[pending.pop()]; group=[]
   while q:
    f=q.pop(); group.append(f)
    for nxt in adj[(v,f)] & pending: pending.remove(nxt); q.append(nxt)
   n=unit(tuple(sum(normals[f][k] for f in group) for k in range(3))); outnorm.append(n)
   for f in group: corners[(v,f)]=len(outnorm)
 lines=['# OpenSCAD mm to game m; Y/Z swapped; winding reversed; 50 degree smooth fans']
 lines += ['v %.9g %.9g %.9g'%v for v in verts]
 lines += ['vn %.9g %.9g %.9g'%n for n in outnorm]
 lines += ['f '+' '.join('%d//%d'%(v+1,corners[(v,i)]) for v in f) for i,f in enumerate(faces)]
 return ('\n'.join(lines)+'\n').encode()
def main():
 result={}
 for name in NAMES:
  stl=P/f't90a_cupola_{name}.stl'; data=obj_data(read_stl(stl)); obj=P/f't90a_cupola_{name}.obj'; obj.write_bytes(data)
  ts=read_stl(stl); ps=[(p[0]/1000,p[2]/1000,p[1]/1000) for t in ts for p in t]
  result[name]={'stl_sha256':sha(stl.read_bytes()),'obj_sha256':sha(data),'bounds_m':[[min(p[i] for p in ps) for i in range(3)],[max(p[i] for p in ps) for i in range(3)]]}
 (P/'mesh-hashes.json').write_text(json.dumps(result,indent=2)+'\n')
if __name__=='__main__': main()

