"""Deterministic approved STL -> indexed mesh, area-weighted crease normals, metric UV atlas."""
from pathlib import Path
from collections import defaultdict
import argparse, base64, hashlib, math, struct
ROOT=Path(__file__).resolve().parents[1]
SOURCE=ROOT/'rotator-assets'
CONVERTER_VERSION='0.9.7-native-1uv-per-metre'
NATIVE_UV_PER_METRE=1.0
CREASE=40
WELD=1e-6
GAP=.0041

def sub(a,b): return tuple(x-y for x,y in zip(a,b))
def dot(a,b): return sum(x*y for x,y in zip(a,b))
def cross(a,b): return (a[1]*b[2]-a[2]*b[1],a[2]*b[0]-a[0]*b[2],a[0]*b[1]-a[1]*b[0])
def length(a): return math.sqrt(dot(a,a))
def unit(a): return tuple(x/length(a) for x in a)

def triangles(path):
    data=path.read_bytes()
    count=struct.unpack_from('<I',data,80)[0] if len(data)>=84 else -1
    if 84+50*count==len(data):
        return [tuple(struct.unpack_from('<3f',data,84+50*i+12+12*j)) for i in range(count) for j in range(3)]
    return [tuple(map(float,line.split()[1:4])) for line in data.decode('ascii').splitlines() if line.strip().startswith('vertex ')]

def build(path,offset,uv_scale=None):
    raw=[(x/1000,z/1000+offset,-y/1000) for x,y,z in triangles(path)]
    positions,cells,flat=[],defaultdict(list),[]
    for p in raw:
        cell=tuple(math.floor(x/WELD) for x in p)
        candidates=[i for x in (-1,0,1) for y in (-1,0,1) for z in (-1,0,1)
                    for i in cells[(cell[0]+x,cell[1]+y,cell[2]+z)] if length(sub(p,positions[i]))<=WELD]
        if candidates: i=min(candidates)
        else: i=len(positions);positions.append(p);cells[cell].append(i)
        flat.append(i)
    faces=[tuple(flat[i:i+3]) for i in range(0,len(flat),3)]
    weighted,normals,incident,edges=[],[],defaultdict(list),defaultdict(list)
    for f,tri in enumerate(faces):
        a,b,c=[positions[i] for i in tri];n=cross(sub(b,a),sub(c,a))
        if length(n)<1e-16: raise ValueError('Degenerate STL triangle')
        weighted.append(n);normals.append(unit(n))
        for i in tri: incident[i].append(f)
        for i,j in zip(tri,tri[1:]+tri[:1]): edges[tuple(sorted((i,j)))].append(f)
    adjacency=defaultdict(list);threshold=math.cos(math.radians(CREASE))
    for fs in edges.values():
        if len(fs)!=2: raise ValueError('STL not closed two-manifold')
        a,b=fs
        if dot(normals[a],normals[b])>=threshold: adjacency[a].append(b);adjacency[b].append(a)
    groups,chart_of=[],{}
    for f in range(len(faces)):
        if f in chart_of: continue
        group,stack=[],[f];chart=len(groups);chart_of[f]=chart
        while stack:
            a=stack.pop();group.append(a)
            for b in sorted(adjacency[a]):
                if b not in chart_of: chart_of[b]=chart;stack.append(b)
        groups.append(sorted(group))
    charts=[]
    for group in groups:
        ids=sorted({i for f in group for i in faces[f]});n=normals[group[0]]
        planar=all(dot(n,normals[f])>1-1e-7 for f in group)
        levels=defaultdict(list)
        for i in ids: levels[round(positions[i][1],8)].append(i)
        ring=max(levels.values(),key=len)
        cx=sum(positions[i][0] for i in ring)/len(ring);cz=sum(positions[i][2] for i in ring)/len(ring)
        # Fit an open/notched circular ring without triangulation-centroid bias.
        if not planar and len(ring)>=8:
            a,b,c=[positions[ring[j]] for j in (0,len(ring)//3,2*len(ring)//3)]
            d=2*(a[0]*(b[2]-c[2])+b[0]*(c[2]-a[2])+c[0]*(a[2]-b[2]))
            if abs(d)>1e-12:
                aa,bb,cc=[p[0]**2+p[2]**2 for p in (a,b,c)]
                cx=(aa*(b[2]-c[2])+bb*(c[2]-a[2])+cc*(a[2]-b[2]))/d
                cz=(aa*(c[0]-b[0])+bb*(a[0]-c[0])+cc*(b[0]-a[0]))/d
        radii={i:math.hypot(positions[i][0]-cx,positions[i][2]-cz) for i in ids}
        band=not planar and all(abs(normals[f][1])<.999 for f in group)
        local={}
        if band:
            y0,y1=min(levels),max(levels)
            r0=sum(radii[i] for i in levels[y0])/len(levels[y0]);r1=sum(radii[i] for i in levels[y1])/len(levels[y1])
            rmean=(r0+r1)/2;slope=math.hypot(y1-y0,r1-r0)/(y1-y0)
            for f in group:
                angles=[(math.atan2(positions[i][2]-cz,positions[i][0]-cx)+math.pi)%(2*math.pi) for i in faces[f]]
                if max(angles)-min(angles)>math.pi: angles=[a+2*math.pi if a<math.pi else a for a in angles]
                for corner,(i,a) in enumerate(zip(faces[f],angles)): local[f,corner]=(a*rmean,(positions[i][1]-y0)*slope)
        else:
            axis=min(((1,0,0),(0,1,0),(0,0,1)),key=lambda v:abs(dot(v,n)))
            u=unit(cross(axis,n));v=cross(n,u)
            for f in group:
                for corner,i in enumerate(faces[f]): local[f,corner]=(dot(positions[i],u),dot(positions[i],v))
        mins=tuple(min(uv[k] for uv in local.values()) for k in range(2))
        sizes=tuple(max(uv[k] for uv in local.values())-mins[k] for k in range(2))
        charts.append(dict(faces=group,local=local,mins=mins,size=sizes,kind='band' if band else 'plane',center=(cx,cz),normal=unit(tuple(sum(weighted[f][k] for f in group) for k in range(3))) if planar else None))
    # Shelf atlas: exactly one metric scale shared by all charts and both axes.
    order=sorted(range(len(charts)),key=lambda i:(-charts[i]['size'][1],-charts[i]['size'][0],i))
    def pack(scale):
        x,y,row=GAP,GAP,0;placed={}
        for i in order:
            w,h=[s*scale for s in charts[i]['size']]
            if x+w>1-GAP: x=GAP;y+=row+GAP;row=0
            if y+h>1-GAP or w>1-2*GAP: return None
            placed[i]=(x,y);x+=w+GAP;row=max(row,h)
        return placed
    lo,hi=0,1/max(max(c['size']) for c in charts)
    for _ in range(60):
        mid=(lo+hi)/2
        if pack(mid) is None: hi=mid
        else: lo=mid
    scale=uv_scale if uv_scale is not None else lo*(1-1e-6);placed=pack(scale)
    output,indices,lookup,chart_vertices=[],[],{},defaultdict(set)
    for f,tri in enumerate(faces):
        chart=chart_of[f];c=charts[chart]
        for corner,i in enumerate(tri):
            neighbors=[j for j in incident[i] if chart_of[j]==chart and dot(normals[f],normals[j])>=threshold]
            normal=unit(tuple(sum(weighted[j][k] for j in neighbors) for k in range(3)))
            if c['normal'] is not None: normal=c['normal']
            uv=tuple(placed[chart][k]+(c['local'][f,corner][k]-c['mins'][k])*scale for k in range(2))
            vertex=struct.pack('<8f',*(positions[i]+normal+uv));key=(i,chart,vertex)
            if key not in lookup: lookup[key]=len(output);output.append(vertex)
            index=lookup[key];indices.append(index);chart_vertices[chart].add(index)
    volume=abs(sum(dot(raw[i],cross(raw[i+1],raw[i+2]))/6 for i in range(0,len(raw),3)))
    return dict(vertices=b''.join(output),indices=struct.pack('<'+'I'*len(indices),*indices),volume=volume,count=len(output),
                index_count=len(indices),charts=charts,chart_vertices=chart_vertices,scale=scale,raw=raw,welded=len(positions))

def meshes():
    specs=[('Base',0),('Plate',.06)]
    scale=min(build(SOURCE/('rotator_'+kind.lower()+'.stl'),offset)['scale'] for kind,offset in specs)
    items=[]
    for kind,offset in specs:
        m=build(SOURCE/('rotator_'+kind.lower()+'.stl'),offset,scale)
        # Preserve the original vertex/index order and all 24 position/normal bytes.
        # Uniformly scale the original atlas, including chart translations; Repeat allows >1.
        result=bytearray(m['vertices'])
        for at in range(0,len(result),32):
            u,v=struct.unpack_from('<2f',result,at+24)
            struct.pack_into('<2f',result,at+24,u*NATIVE_UV_PER_METRE/scale,v*NATIVE_UV_PER_METRE/scale)
        m['vertices']=bytes(result);m['scale']=NATIVE_UV_PER_METRE;items.append((kind,m))
    return items

def generate(items):
    lines=['// Generated by tools/stl_to_mesh.py; do not edit. Approved STL geometry only.','namespace SprocketTelescopicMast;',
           'internal static class RotatorMeshData','{',f'    internal const string ConverterVersion = "{CONVERTER_VERSION}";']
    for kind,m in items:
        path=SOURCE/('rotator_'+kind.lower()+'.stl');data=m['vertices']
        lines += [f'    internal const string {kind}SourceSHA256 = "{hashlib.sha256(path.read_bytes()).hexdigest()}";',
                  f'    internal const string {kind}DataSHA256 = "{hashlib.sha256(data).hexdigest()}";',
                  f'    internal const string {kind}IndexSHA256 = "{hashlib.sha256(m["indices"]).hexdigest()}";',
                  f'    internal const float {kind}Volume = {m["volume"]:.12g}f;',f'    internal const float {kind}UvScale = {m["scale"]:.12g}f;',
                  f'    internal const int {kind}VertexCount = {m["count"]};',f'    internal const int {kind}IndexCount = {m["index_count"]};']
        for name,blob in [(kind,data),(kind+'Indices',m['indices'])]:
            encoded=base64.b64encode(blob).decode();chunks=[encoded[i:i+120] for i in range(0,len(encoded),120)]
            lines += [f'    internal static byte[] {name}() => System.Convert.FromBase64String(']
            lines += ['        "'+chunk+'"'+(' +' if i<len(chunks)-1 else ');') for i,chunk in enumerate(chunks)]
    return '\n'.join(lines+['}',''])

def export_obj(items,directory):
    directory.mkdir(parents=True,exist_ok=True)
    for kind,m in items:
        vertices=list(struct.iter_unpack('<8f',m['vertices']));indices=[i[0] for i in struct.iter_unpack('<I',m['indices'])]
        lines=['# Exact embedded float32 mesh, Y-up, authored normals; UV/m '+str(m['scale'])]
        for v in vertices: lines.append('v '+' '.join(format(x,'.9g') for x in v[:3]))
        for v in vertices: lines.append('vn '+' '.join(format(x,'.9g') for x in v[3:6]))
        for v in vertices: lines.append('vt '+' '.join(format(x,'.9g') for x in v[6:]))
        for i in range(0,len(indices),3): lines.append('f '+' '.join(f'{j+1}/{j+1}/{j+1}' for j in indices[i:i+3]))
        (directory/('rotator_'+kind.lower()+'.obj')).write_text('\n'.join(lines)+'\n',encoding='utf-8')

if __name__=='__main__':
    parser=argparse.ArgumentParser();parser.add_argument('--check',action='store_true');parser.add_argument('--obj',type=Path);args=parser.parse_args()
    items=meshes();expected=generate(items);target=ROOT/'RotatorMeshData.cs'
    if args.check:
        assert target.read_text(encoding='utf-8')==expected,'Embedded STL data stale; run py tools/stl_to_mesh.py'
        print('PASS reproducible positions, crease normals, UVs, indices, original volumes and source hashes')
    else: target.write_text(expected,encoding='utf-8');print('Generated',target)
    if args.obj: export_obj(items,args.obj)
    for kind,m in items: print(kind,'vertices',m['index_count'],'->',m['count'],'indices',m['index_count'],'charts',len(m['charts']),'UV/m',m['scale'])

