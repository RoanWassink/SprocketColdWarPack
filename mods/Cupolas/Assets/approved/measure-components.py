import re,subprocess
from pathlib import Path
P=Path(__file__).resolve().parent
openscad=r'C:\Program Files\OpenSCAD\openscad.com'
text=(P/'export_game_mesh.py').read_text()
exporters={}
for ver in ('v4','v5'):
 ns={'__file__':str(P/'export_game_mesh.py')}; exec(text.replace('math.radians(50)','math.radians(40)') if ver=='v4' else text,ns); exporters[ver]=ns

def measure(source,part,ver):
 path=P/'_component_probe.scad'; stl=P/'_component_probe.stl'
 path.write_text(source.replace('assembled(door_angle);',part+'();'))
 run=subprocess.run([openscad,'-o',str(stl),str(path)],capture_output=True,text=True)
 if run.returncode: raise RuntimeError(run.stderr)
 ns=exporters[ver]; tris=ns['read_stl'](stl); lines=ns['obj_data'](tris).decode().splitlines()
 return len({p for line in lines if line.startswith('f ') for p in line.split()[1:]})
def remove(source,parts):
 for p in parts:
  if p=='head': source=source.replace('  sight_head();','')
  if p=='viewers': source=source.replace('   rotate([0,0,-a]) viewer();','')
  if p=='lift': source=re.sub(r'  for\(s=\[-1,1\]\).*lift_eye\(\);','',source)
  if p=='bands': source=source.replace('   for(y=[-150,-70,70,150]) band(y);','')
  if p=='rivets': source=re.sub(r' for\(x=\[.*?\]\) translate\(\[x,y,62\.5\]\) cylinder\(.*?;', '', source)
  if p=='handle': source=re.sub(r'   for\(x=\[-45,45\]\).*?;','',source).replace('   translate([-45,0,80]) rotate([0,90,0]) cylinder(h=90,d=20,$fn=8);','')
  if p=='hinge':
   source=source.replace('  translate([-35,-12,0]) cube([70,50,22]);','')
   source=re.sub(r'  for\(x=\[-28,18\]\).*?;','',source).replace('  translate([-36,-4,12]) rotate([0,90,0]) cylinder(h=72,d=10,$fn=8);','')
 return source
out={}
for ver in ('v4','v5'):
 source=(P/('t90a_cupola_v4.scad' if ver=='v4' else 't90a_cupola.scad')).read_text()
 fullbase=measure(source,'base',ver); fulldoor=measure(source,'door',ver)
 n={}
 for p in ('head','viewers','lift'):
  n[p]=fullbase-measure(remove(source,[p]),'base',ver)
 n['rings']=measure(remove(source,['head','viewers','lift']),'base',ver)
 no_riv=measure(remove(source,['rivets']),'door',ver); no_bands=measure(remove(source,['bands']),'door',ver)
 n['rivets']=fulldoor-no_riv; n['bands']=no_riv-no_bands
 n['handle']=fulldoor-measure(remove(source,['handle']),'door',ver)
 n['hinge']=fulldoor-measure(remove(source,['hinge']),'door',ver)
 n['disk']=measure(remove(source,['bands','handle','hinge']),'door',ver)
 n['base_total']=fullbase; n['door_total']=fulldoor; out[ver]=n
 print(ver,n,flush=True)
for f in ('_component_probe.scad','_component_probe.stl'): (P/f).unlink(missing_ok=True)
labels=[('Trommel + bovenring inclusief basis-scharnier','rings'),('Kijkerkop inclusief bouten','head'),('Zeven kijkers','viewers'),('Tilogen','lift'),('Deksel-schijf','disk'),('Vier banden zonder klinknagels','bands'),('Klinknagels','rivets'),('Beugel','handle'),('Deksel-scharnier','hinge')]
lines=['# Vertexmetingen per functie','','Aantallen zijn unieke OBJ v//vn-paren. Bij losse details is het verschil gemeten met/zonder het detail in de volledige union; deze waarden zijn niet optelbaar omdat driehoeken en normals bij een union veranderen. Trommel en bovenring delen een profiel en zijn samen gemeten. De schijf is zonder banden, beugel en scharnier gemeten.','','| Functie | Ronde 4 | Ronde 5 voor dekseloptimalisatie |','|---|---:|---:|']
lines += [f'| {label} | {out["v4"][key]} | {out["v5"][key]} |' for label,key in labels]
lines += ['',f'Volledige basis: {out["v4"]["base_total"]} -> {out["v5"]["base_total"]}.',f'Volledig deksel voor de extra optimalisatie: {out["v4"]["door_total"]} -> {out["v5"]["door_total"]}; na optimalisatie: 763. De optimalisatie grijpt in op het geheel: een eerlijke uitsplitsing achteraf per detail is niet optelbaar.']
(P/'vertex-budget.md').write_text('\n'.join(lines)+'\n',encoding='utf-8')

