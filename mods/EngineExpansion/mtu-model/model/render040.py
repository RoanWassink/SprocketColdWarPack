"""Generate inspectable OpenSCAD viewers and PNGs of the exact 0.4.0 STL geometry.
The bridge sample comes from the SAME pure geometry function called by the plugin (Checks --bridge-json).
"""
from pathlib import Path
import sys,json,subprocess,concurrent.futures
sys.path.insert(0,str(Path(__file__).resolve().parent))
from stlcheck import read_stl,stats
ROOT=Path(__file__).resolve().parent
SCAD=r'C:\Program Files\OpenSCAD\openscad.com'
FRAME='''module frame(lo,hi,t=2) {
 for(i=[0:2]) for(u=[lo[(i+1)%3],hi[(i+1)%3]],v=[lo[(i+2)%3],hi[(i+2)%3]]) {
  p=[for(j=[0:2]) j==i?lo[i]:(j==(i+1)%3?u:v)];
  translate(p-[t/2,t/2,t/2]) cube([for(j=[0:2]) j==i?hi[j]-lo[j]+t:t]);
 }
}
'''
jobs=[]
for part in ['start','pair','output','turbo','turbo-outboard']:
 lo,hi,_=stats(read_stl(ROOT/'parts'/f'{part}-hull.stl'))
 viewer=f'view-{part}-040.scad'
 source='// Exact DLL LOD0; set collision=true to inspect the 100-triangle interaction mesh.\ncollision=false;\n'+FRAME
 source+=f'color([.74,.77,.79]) import(collision?"{part}-hull.stl":"{part}-lod0.stl",convexity=10);\ncolor([1,.85,0]) frame({lo},{hi});\n'
 (ROOT/'parts'/viewer).write_text(source,encoding='utf-8')
 for collision in (False,True):
  out=f'shots/040-{part}-'+('hull' if collision else 'lod0')+'.png'
  jobs.append((["-o",out,"--imgsize=1200,800","--camera=-1.3,-1,0.85,0,0,0","--viewall","--autocenter","--colorscheme=Tomorrow","-D",f'collision={str(collision).lower()}',f'parts/{viewer}'],out))

# Sample bridge: exact output from the production C# pure geometry, Unity metres -> OpenSCAD mm.
bridge=json.loads((ROOT/'logs/bridge040.json').read_text(encoding='utf-8-sig'))
vs=[[v[0]*1000,v[2]*1000,v[1]*1000] for v in bridge['Vertices']];ids=bridge['Triangles'];faces=[ids[i:i+3] for i in range(0,len(ids),3)]
# The y/z reflection reverses standard triangle winding; OpenSCAD's clockwise convention reverses it back.
(ROOT/'parts/bridge040-sample.scad').write_text('// Exported from PowerpackBridgeGeometry.Build(.55,.50,.70,0,.20).\npolyhedron(points='+str(vs)+',faces='+str(faces)+',convexity=10);\n')
subprocess.run([SCAD,'-o',str(ROOT/'parts/bridge040-sample.stl'),str(ROOT/'parts/bridge040-sample.scad')],check=True,capture_output=True)
bridge_view=FRAME+'color([.60,.63,.57]) import("bridge040-sample.stl",convexity=10);\ncolor([1,.85,0]) frame([-275,0,0],[275,700,540]);\n'
(ROOT/'parts/view-bridge040.scad').write_text(bridge_view)
jobs.append((["-o","shots/040-bridge.png","--imgsize=1200,800","--camera=-1.3,-1,0.85,0,0,0","--viewall","--autocenter","--colorscheme=Tomorrow","parts/view-bridge040.scad"],'shots/040-bridge.png'))

viewer='''// Exact STL engine assembly as M2+ places it. Open in OpenSCAD, change N/d/lod/collision/cfg.
N=12; d=3.97; SizeFactor=1; LengthFactor=.9; HeightFactor=1; TurboLayout="Compact";
lod=0; collision=false; show_bounds=true;
k=pow(d,1/3); n=N/2;
'''+FRAME+'''
module part(name) {import(str("parts/",name,collision?"-hull.stl":str("-lod",lod,".stl")),convexity=10);}
color([.76,.79,.81]) scale([k*SizeFactor,k*SizeFactor*LengthFactor,k*SizeFactor*HeightFactor]) translate([0,-n*60,0]) {
 part("start");for(i=[0:n-1])translate([0,i*120,0])part("pair");
 translate([0,n*120,0]){part("output");part(TurboLayout=="Compact"?"turbo":"turbo-outboard");}
}
if(show_bounds) color([1,.85,0]) scale([k*SizeFactor,k*SizeFactor*LengthFactor,k*SizeFactor*HeightFactor])
 frame([TurboLayout=="Compact"?-350:-625,-n*60-160,0],[TurboLayout=="Compact"?350:625,n*60+245,620]);
'''
(ROOT/'view-mtu040.scad').write_text(viewer)
for name,N,d,lod,col in [('mb873',12,3.97,0,False),('mb873-lod1',12,3.97,1,False),('mb873-collision',12,3.97,0,True),('v8',8,4.07,0,False)]:
 out=f'shots/040-{name}.png';jobs.append((["-o",out,"--imgsize=1200,800","--camera=-1.3,-1,0.85,0,0,0","--viewall","--autocenter","--colorscheme=Tomorrow","-D",f'N={N}',"-D",f'd={d}',"-D",f'lod={lod}',"-D",f'collision={str(col).lower()}',"view-mtu040.scad"],out))
def render(job):
 args,out=job;r=subprocess.run([SCAD]+args,cwd=ROOT,capture_output=True,text=True)
 if r.returncode:raise RuntimeError(r.stderr)
 return f'{out}\n{r.stdout}{r.stderr}'
with concurrent.futures.ThreadPoolExecutor(max_workers=3) as pool:
 results=list(pool.map(render,jobs))
(ROOT/'logs/render040.txt').write_text('\n'.join(results))
print(f'PASS {len(jobs)} OpenSCAD PNGs at 1200x800')
