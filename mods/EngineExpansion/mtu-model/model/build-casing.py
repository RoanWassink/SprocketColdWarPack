"""Geometry/visual reproducer for the EPP casing.

OpenSCAD 2021.01 may reorder facets when it re-exports the same CSG. The stored
parts/casing-lod0/1.stl are the canonical embedded assets; re-running this script
overwrites them and can make embed_meshes.py --check report a new STL hash.
Restore the canonical STLs from the snapshot before embedding.
"""
from pathlib import Path
import subprocess,hashlib,json,sys
ROOT=Path(__file__).resolve().parent
sys.path.insert(0,str(ROOT))
from stlcheck import read_stl,stats
SCAD=r"C:\Program Files\OpenSCAD\openscad.com"
for folder in ('parts','shots','logs'):(ROOT/folder).mkdir(exist_ok=True)
records=[];logs=[]
for lod in (0,1):
 out=ROOT/'parts'/f'casing-lod{lod}.stl'
 result=subprocess.run([SCAD,'-o',str(out),'-D',f'lod={lod}',str(ROOT/'casing.scad')],capture_output=True,text=True)
 logs.append(result.stdout+result.stderr);assert result.returncode==0
 tris=read_stl(out);lo,hi,bad=stats(tris)
 assert bad==0,(out,bad)
 assert all(lo[i]>=v-1e-5 for i,v in enumerate([-.5,0,.045]))
 assert all(hi[i]<=v+1e-5 for i,v in enumerate([.5,1,1]))
 records.append(dict(lod=lod,triangles=len(tris),bounds=[lo,hi],nonmanifold=bad,sha256=hashlib.sha256(out.read_bytes()).hexdigest()))
for name,defines,source in [
 ('casing-unit',[], 'view-casing.scad'),
 ('casing-v12-renk',[], 'view-casing-pack.scad'),
 ('casing-twin',['twin=true'], 'view-casing-pack.scad'),
 ('casing-v8-small',['N=8','d=4.07','small=true'], 'view-casing-pack.scad')]:
 args=[SCAD,'-o',str(ROOT/'shots'/f'{name}.png'),'--imgsize=1200,800','--camera=-1.3,-1,0.85,0,0,0','--viewall','--autocenter','--colorscheme=Tomorrow']
 for define in defines:args+=['-D',define]
 result=subprocess.run(args+[str(ROOT/source)],capture_output=True,text=True)
 logs.append(name+'\n'+result.stdout+result.stderr);assert result.returncode==0
(ROOT/'logs/casing-manifest.json').write_text(json.dumps(records,indent=2))
(ROOT/'logs/casing-build.txt').write_text('\n'.join(logs))
print(json.dumps(records,indent=2))
print('PASS casing LOD0/LOD1 bounds/manifold + 4 OpenSCAD PNGs')
