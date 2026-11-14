from pathlib import Path
import re, struct, hashlib, json, sys, collections
R=Path(__file__).resolve().parents[1]
def stl(path):
 b=path.read_bytes()
 if len(b)>=84 and len(b)==84+50*struct.unpack_from('<I',b,80)[0]:
  return [struct.unpack_from('<3f',b,84+50*i+12+12*j) for i in range(struct.unpack_from('<I',b,80)[0]) for j in range(3)]
 return [tuple(map(float,p)) for p in re.findall(rb'vertex\s+([-\d.eE+]+)\s+([-\d.eE+]+)\s+([-\d.eE+]+)',b)]
def arrow():
 v=stl(R/'assets/emitter_arrow.stl'); flat=[x*.001 for p in v for x in p]
 def f(x):return format(x,'.9g')+'f'
 return 'namespace SprocketEmitter;\ninternal static class ArrowMeshData {\n internal static readonly UnitMesh Arrow=new("arrow","'+hashlib.sha256((R/'assets/emitter_arrow.stl').read_bytes()).hexdigest().upper()+'",'+str(len(v)//3)+',new float[]{'+','.join(map(f,flat))+'},new int[]{'+','.join(map(str,range(len(v))))+'});\n}\n'
if '--check' not in sys.argv:(R/'ArrowMeshData.cs').write_text(arrow())
assert (R/'ArrowMeshData.cs').read_text()==arrow(),'arrow arrays differ from STL'
print('PASS arrow hash, triangles and exact arrays (mm to m)')
data=(R/'CasingMeshData.cs').read_text()
results={}
for name,field in [('charge8','Charge8'),('projectile8','Projectile8')]:
 p=R/'assets'/f'{name}.stl'; v=stl(p);h=hashlib.sha256(p.read_bytes()).hexdigest().upper()
 m=re.search(field+r' = new\("[^\"]+", "([A-F0-9]+)", (\d+),\s*new float\[\] \{(.*?)\},\s*new int\[\] \{(.*?)\}',data,re.S);assert m
 assert m[1]==h and int(m[2])==len(v)//3
 nums=[float(x.strip().rstrip('f')) for x in m[3].split(',')]; verts=[tuple(nums[i:i+3]) for i in range(0,len(nums),3)];inds=list(map(int,m[4].split(',')))
 def canonical(v):return collections.Counter(tuple(sorted(tuple(round(x,6) for x in p) for p in v[i:i+3])) for i in range(0,len(v),3))
 assert canonical(v)==canonical([verts[i] for i in inds]),name+' geometry mismatch'
 results[name]={'sha256':h,'triangles':len(v)//3,'bounds':[(min(a),max(a)) for a in zip(*v)]}
 print('PASS',name,'STL hash, triangle count and full geometry')
if '--evidence' in sys.argv:
 e=Path(sys.argv[sys.argv.index('--evidence')+1]);e.parent.mkdir(parents=True,exist_ok=True);e.write_text(json.dumps(results,indent=2))
