import math, struct, zlib
from collections import Counter
from pathlib import Path
P=Path(__file__).resolve().parent
V=[]; F=[]; TAG=[]; SOLID=[]; current_solid=0
def vert(x,y,z):
 V.append((x,y,z)); return len(V)
def face(a,b,c,group,tag="flat"):
 F.append((a,b,c,group)); TAG.append(tag); SOLID.append(current_solid)
def box(x0,x1,y0,y1,z0,z1,g='housing'):
 global current_solid
 current_solid+=1
 ids=[vert(x,y,z) for x,y,z in [(x0,y0,z0),(x1,y0,z0),(x1,y1,z0),(x0,y1,z0),(x0,y0,z1),(x1,y0,z1),(x1,y1,z1),(x0,y1,z1)]]
 for a,b,c,d in [(0,3,2,1),(4,5,6,7),(0,1,5,4),(3,7,6,2),(0,4,7,3),(1,2,6,5)]:
  face(ids[a],ids[b],ids[c],g);face(ids[a],ids[c],ids[d],g)
def revolved(profile, cy, g, n=96, smooth=()):
 global current_solid
 current_solid+=1
 # Closed profile in (radius,z); outer surface traversed rear-to-front.
 rings=[]
 for index,(r,z) in enumerate(profile):
  if index == len(profile)-1 and (r,z)==profile[0] and r != 0:
   rings.append(rings[0]); continue
  if r==0: rings.append([vert(0,cy,z)])
  else: rings.append([vert(r*math.cos(2*math.pi*i/n),cy+r*math.sin(2*math.pi*i/n),z) for i in range(n)])
 for j in range(len(rings)-1):
  a,b=rings[j],rings[j+1]
  tag=('smooth', current_solid, next(k for k,indices in enumerate(smooth) if j in indices)) if any(j in indices for indices in smooth) else ('flat',current_solid,j)
  for i in range(n):
   k=(i+1)%n
   if len(a)==1: face(a[0],b[k],b[i],g,tag)
   elif len(b)==1: face(a[i],a[k],b[0],g,tag)
   else: face(a[i],a[k],b[k],g,tag);face(a[i],b[k],b[i],g,tag)
# The housing depth includes its gently domed back; axis center height 0.25 m.
housing_start=len(V)
revolved([(0,-.125),(.07,-.121),(.12,-.114),(.15,-.105),(.15,.125),(.132,.125),(.132,.105),(0,.105)],.25,'housing',smooth=((0,1,2,3),))
housing_vertices=V[housing_start:]
# Separate annular bezel, closed by its inner wall.
revolved([(.15,.122),(.15,.130),(.133,.130),(.133,.122),(.15,.122)],.25,'housing')
# Thin enclosed lens, recessed from the lip.
revolved([(0,.120),(.131,.120),(.131,.125),(.10,.1267),(.06,.1275),(0,.128)],.25,'lens',smooth=((2,3,4),))
foot_start=len(V)
box(-.06,.06,0,.012,-.05,.05)
foot_vertices=V[foot_start:]
box(-.06,-.044,.012,.245,-.024,.024)
box(.044,.06,.012,.245,-.024,.024)
box(-.044,.044,.012,.035,-.024,.024)
# Rear gland and short cable stub (concentric closed solids).
revolved([(0,-.155),(.025,-.155),(.025,-.12),(0,-.12)],.25,'housing',32)
revolved([(0,-.18),(.012,-.18),(.012,-.151),(0,-.151)],.25,'housing',32)
# Explicit per-surface corner normals and metre-based UVs. OBJ splits hard-edge vertices.
def cross3(a,b): return (a[1]*b[2]-a[2]*b[1],a[2]*b[0]-a[0]*b[2],a[0]*b[1]-a[1]*b[0])
def unit(v):
 length=math.sqrt(sum(x*x for x in v)); return tuple(x/length for x in v)
normal_sums={}
for i,(a,b,c,g) in enumerate(F):
 A,B,C=[V[k-1] for k in (a,b,c)]; raw=cross3(tuple(B[j]-A[j] for j in range(3)),tuple(C[j]-A[j] for j in range(3)))
 # Box triangles are coplanar in pairs, but their different sides stay hard.
 tag=TAG[i] if TAG[i]!='flat' else ('box',SOLID[i],tuple(round(v,6) for v in unit(raw)))
 TAG[i]=tag
 for v in (a,b,c):
  key=(v,tag); old=normal_sums.get(key,(0,0,0)); normal_sums[key]=tuple(old[j]+raw[j] for j in range(3))
newv=[]; newn=[]; newuv=[]; mapping={}; outfaces=[]
for i,(a,b,c,g) in enumerate(F):
 tag=TAG[i]; normal=unit(normal_sums[(a,tag)]); cylindrical=tag[0]=='smooth' and SOLID[i]!=3
 # Unwrap each cylindrical triangle over the seam; UV coordinates are in metres.
 angles=[math.atan2(V[k-1][1]-.25,V[k-1][0])%(2*math.pi) for k in (a,b,c)]
 if max(angles)-min(angles)>math.pi: angles=[t+2*math.pi if t<math.pi else t for t in angles]
 result=[]
 for j,k in enumerate((a,b,c)):
  x,y,z=V[k-1]; n=unit(normal_sums[(k,tag)])
  if cylindrical: uv=(angles[j]*max(math.hypot(x,y-.25),.001),z)
  else:
   ax=max(range(3),key=lambda t:abs(n[t])); uv=((y,z) if ax==0 else ((x,z) if ax==1 else (x,y)))
  key=(k,tag,round(uv[0],8),round(uv[1],8))
  if key not in mapping:
   mapping[key]=len(newv)+1;newv.append((x,y,z));newn.append(n);newuv.append(uv)
  result.append(mapping[key])
 outfaces.append((*result,g))
with (P/'ir-searchlight.obj').open('w',newline='\n') as f:
 f.write('# Metres; +Y up, +Z forward; origin at centre under foot.\n')
 for x,y,z in newv: f.write(f'v {x:.8f} {y:.8f} {z:.8f}\n')
 for u,v in newuv: f.write(f'vt {u:.8f} {v:.8f}\n')
 for x,y,z in newn: f.write(f'vn {x:.8f} {y:.8f} {z:.8f}\n')
 for group in ('housing','lens'):
  f.write(f'o {group}\n')
  for a,b,c,g in outfaces:
   if g==group: f.write(f'f {a}/{a}/{a} {b}/{b}/{b} {c}/{c}/{c}\n')
# Topological checks use vertex indices and include each closed solid separately.
edges=Counter(); faces=Counter(); deg=0
for a,b,c,g in F:
 faces[tuple(sorted((a,b,c)))]+=1
 for i,j in ((a,b),(b,c),(c,a)): edges[tuple(sorted((i,j)))]+=1
 A,B,C=[V[k-1] for k in (a,b,c)]; u=[B[i]-A[i] for i in range(3)];v=[C[i]-A[i] for i in range(3)]; cross=(u[1]*v[2]-u[2]*v[1],u[2]*v[0]-u[0]*v[2],u[0]*v[1]-u[1]*v[0]);deg+=sum(t*t for t in cross)<1e-20
bbox=[(min(v[i] for v in V),max(v[i] for v in V)) for i in range(3)]
span=lambda vs,axis: max(v[axis] for v in vs)-min(v[axis] for v in vs)
checks=[('Housing diameter',span(housing_vertices,0),.30),('Housing depth including dome',span(housing_vertices,2),.25),('Foot width',span(foot_vertices,0),.12),('Foot depth',span(foot_vertices,2),.10),('Total height',bbox[1][1]-bbox[1][0],.40)]
report=['IR searchlight validation (m)','Bounding box:']+[f'  {"XYZ"[i]}: {a:.6f} .. {b:.6f} (span {b-a:.6f})' for i,(a,b) in enumerate(bbox)]+[f'Topological vertices before hard-edge/UV splits: {len(V)}',f'Triangles: {len(F)}',f'Closed (each indexed edge incident on exactly two faces): {all(n==2 for n in edges.values())}',f'Boundary/nonmanifold edges: {sum(n!=2 for n in edges.values())}',f'Duplicate faces: {sum(n-1 for n in faces.values() if n>1)}',f'Degenerate faces: {deg}',f'Exported v/vt/vn: {len(newv)}/{len(newuv)}/{len(newn)}', f'All face v/vt/vn references in range: {all(1 <= k <= len(newv) and k <= len(newuv) and k <= len(newn) for a,b,c,g in outfaces for k in (a,b,c))}', *[f'{group} closed per original solid topology: {all(n==2 for n in Counter((SOLID[i],tuple(sorted(edge))) for i,(a,b,c,g) in enumerate(F) if g==group for edge in ((a,b),(b,c),(c,a))).values())}' for group in ('housing','lens')], 'Target dimensions:']+[f'  {n}: {v:.4f} m; target {t:.4f} m; deviation {(v/t-1)*100:+.2f}%' for n,v,t in checks]+['Note: the mesh comprises intersecting closed solids, as do reference assets; it is not a boolean-unioned shell.']
# Parse the written OBJ independently of the generator's in-memory indices.
counts={'v':0,'vt':0,'vn':0}; parsed_faces=0; bad=0; groups=set()
for line in (P/'ir-searchlight.obj').read_text().splitlines():
 parts=line.split()
 if not parts: continue
 if parts[0] in counts: counts[parts[0]]+=1
 elif parts[0]=='o': groups.add(parts[1])
 elif parts[0]=='f':
  parsed_faces+=1
  for corner in parts[1:]:
   fields=corner.split('/')
   if len(fields)!=3 or any(not value.isdigit() or not 1<=int(value)<=counts[kind] for value,kind in zip(fields,('v','vt','vn'))): bad+=1
report += [f'OBJ reread: {counts}, faces {parsed_faces}, groups {sorted(groups)}', f'Invalid face-corner references in written OBJ: {bad}']
assert bad==0 and parsed_faces==len(F) and groups=={'housing','lens'}
(P/'check.txt').write_text('\n'.join(report)+'\n')
# Orthographic software rendering, transparent background, flat lighting, 2x antialiasing.
S=1024; pix=bytearray(S*S*4); depth=[-1e9]*(S*S)
def norm(v):
 l=math.sqrt(sum(x*x for x in v));return tuple(x/l for x in v)
def dot(a,b): return sum(x*y for x,y in zip(a,b))
def cross(a,b): return (a[1]*b[2]-a[2]*b[1],a[2]*b[0]-a[0]*b[2],a[0]*b[1]-a[1]*b[0])
cam=norm((4,3,6)); right=norm((cam[2],0,-cam[0])); up=cross(cam,right); light=norm((-3,7,6)); scale=1740
proj=[(S/2+scale*dot((x,y-.20,z),right),S/2-scale*dot((x,y-.20,z),up),dot((x,y,z),cam)) for x,y,z in V]
for a,b,c,g in F:
 va,vb,vc=[V[i-1] for i in (a,b,c)]; n=norm(cross(tuple(vb[i]-va[i] for i in range(3)),tuple(vc[i]-va[i] for i in range(3))))
 if dot(n,cam)<=0: continue
 p,q,r=[proj[i-1] for i in (a,b,c)]; area=(q[0]-p[0])*(r[1]-p[1])-(q[1]-p[1])*(r[0]-p[0]);
 if abs(area)<1e-9: continue
 col=(11,28,31) if g=='lens' else (96,113,74); shade=.55+.45*max(0,dot(n,light)); color=tuple(int(v*shade) for v in col)+(255,)
 for y in range(max(0,int(min(p[1],q[1],r[1]))),min(S-1,int(max(p[1],q[1],r[1])))+1):
  for x in range(max(0,int(min(p[0],q[0],r[0]))),min(S-1,int(max(p[0],q[0],r[0])))+1):
   w1=((x-p[0])*(r[1]-p[1])-(y-p[1])*(r[0]-p[0]))/area; w2=((q[0]-p[0])*(y-p[1])-(q[1]-p[1])*(x-p[0]))/area; w0=1-w1-w2
   if min(w0,w1,w2)>=0:
    z=w0*p[2]+w1*q[2]+w2*r[2]; idx=y*S+x
    if z>depth[idx]: depth[idx]=z;pix[idx*4:idx*4+4]=bytes(color)
# Downsample using premultiplied alpha.
raw=bytearray()
for y in range(512):
 raw.append(0)
 for x in range(512):
  samples=[pix[((2*y+dy)*S+2*x+dx)*4:((2*y+dy)*S+2*x+dx)*4+4] for dy in range(2) for dx in range(2)]; alpha=sum(s[3] for s in samples);raw.extend([sum(s[k]*s[3] for s in samples)//alpha if alpha else 0 for k in range(3)]+[alpha//4])
def chunk(t,d): return struct.pack('!I',len(d))+t+d+struct.pack('!I',zlib.crc32(t+d)&0xffffffff)
(P/'ir-searchlight-icon.png').write_bytes(b'\x89PNG\r\n\x1a\n'+chunk(b'IHDR',struct.pack('!IIBBBBB',512,512,8,6,0,0,0))+chunk(b'IDAT',zlib.compress(raw,9))+chunk(b'IEND',b''))
print('\n'.join(report))
