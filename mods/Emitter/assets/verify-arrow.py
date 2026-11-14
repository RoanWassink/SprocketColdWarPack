import re, pathlib, collections, subprocess, tempfile, hashlib, sys
p=pathlib.Path(__file__).resolve().parent
f=p/'emitter_arrow.stl'
def read(path):
 s=path.read_text(); vertices=[tuple(map(float,v)) for v in re.findall(r'vertex\s+([-\d.eE+]+)\s+([-\d.eE+]+)\s+([-\d.eE+]+)',s)]; return vertices
v=read(f); edges=collections.Counter(tuple(sorted((tri[i],tri[(i+1)%3]))) for j in range(0,len(v),3) for tri in [v[j:j+3]] for i in range(3))
print('triangles:',len(v)//3,'bounds:',[(min(a),max(a)) for a in zip(*v)],'watertight:',all(n==2 for n in edges.values()))
assert len(v)//3 < 500 and all(n==2 for n in edges.values())
assert [(min(a),max(a)) for a in zip(*v)] == [(-30,30),(-30,30),(0,120)]
# Scratch folder: first argument (a folder under the work directory), else the system temp folder; never the source tree.
work=pathlib.Path(sys.argv[1]) if len(sys.argv)>1 else None
if work: work.mkdir(parents=True,exist_ok=True)
with tempfile.TemporaryDirectory(dir=work) as d:
 out=pathlib.Path(d)/'new.stl'; subprocess.run([r'C:\Program Files\OpenSCAD\openscad.com','-o',str(out),str(p/'emitter_arrow.scad')],check=True,capture_output=True)
 assert collections.Counter(tuple(sorted(v[i:i+3])) for i in range(0,len(v),3)) == collections.Counter(tuple(sorted(w[i:i+3])) for w in [read(out)] for i in range(0,len(w),3))
print('PASS: current STL matches regenerated SCAD export')

