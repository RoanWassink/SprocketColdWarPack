"""Create inspectable OpenSCAD views from the shared C# geometry test snapshots.
Run from the Carousel root after the final tests. No source assets are modified.
"""
import json
from pathlib import Path

root = Path.cwd()
folder = root / 'logs/ingame-0.3.2'
colors = [[43/255,55/255,57/255],[112/255,134/255,132/255],
          [186/255,203/255,192/255],[204/255,167/255,83/255],
          [147/255,62/255,52/255],[.62,.54,.37]]
for path in folder.glob('*.json'):
    data = json.loads(path.read_text())
    spec = data['spec']
    w,h,l = (spec[k]*1000 for k in ('X','Y','Z'))
    out = ['// Exact C# mesh snapshot; cutaway is inspection only. Runtime box is complete.',
           '// Toggle cutaway=false for the complete installed geometry.', 'cutaway=true;',
           'module bounds(){',
           f'for(y=[{-h/2},{h/2}],z=[{-l/2},{l/2}]) translate([{-w/2},y,z]) rotate([0,90,0]) cylinder(h={w},r=.65,$fn=8);',
           f'for(x=[{-w/2},{w/2}],z=[{-l/2},{l/2}]) translate([x,{-h/2},z]) rotate([-90,0,0]) cylinder(h={h},r=.65,$fn=8);',
           f'for(x=[{-w/2},{w/2}],y=[{-h/2},{h/2}]) translate([x,y,{-l/2}]) cylinder(h={l},r=.65,$fn=8);', '}',
           'rotate([90,0,0]){']
    for item in data['items']:
        v=item['vertices']; idx=item['indices']; mats=item['materials']
        points=[[round(v[i+j]*1000,6) for j in range(3)] for i in range(0,len(v),3)]
        for mat in sorted(set(mats)):
            faces=[idx[i*3:i*3+3] for i,m in enumerate(mats) if m==mat]
            out += [f'color({colors[mat]})']
            if item['Name']=='Belt box': out += ['difference(){']
            # STL and Unity use opposite winding conventions for OpenSCAD polyhedra.
            out += ['polyhedron(points='+json.dumps(points,separators=(',',':'))+',faces='+
                    json.dumps([f[::-1] for f in faces],separators=(',',':'))+',convexity=12);']
            if item['Name']=='Belt box': out += [f'if(cutaway) translate([{ -w/2-5},{-h/2+.283333*h},{-l/2-5}]) cube([{w+10},{h+10},{l+10}]);','}']
    out += ['color("#ffe02c") bounds();','}']
    (folder/('view-'+path.stem+'.scad')).write_text('\n'.join(out)+'\n')
    print(path.stem, 'stock', data['state']['Stored'], 'top',data['state']['TopCount'], 'fill mm',data['state']['FillHeight']*1000)
