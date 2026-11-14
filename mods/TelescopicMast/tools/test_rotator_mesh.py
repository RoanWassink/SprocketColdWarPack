"""Acceptance checks against embedded float32 payload and unchanged approved STL."""
import hashlib, json, math, re, statistics, struct
import stl_to_mesh as cv

def metrics(tris):
    return (sum(cv.length(cv.cross(cv.sub(b,a),cv.sub(c,a)))/2 for a,b,c in tris),
            abs(sum(cv.dot(a,cv.cross(b,c))/6 for a,b,c in tris)),
            [[fn(p[k] for tri in tris for p in tri) for k in range(3)] for fn in (min,max)])

def main():
    report={};checks=0
    def check(ok,why):
        nonlocal checks
        checks+=1
        if not ok: raise AssertionError(why)
    text=(cv.ROOT/'RotatorMeshData.cs').read_text(encoding='utf-8')
    items=cv.meshes()
    check(text==cv.generate(items),'Exact embedded bytes differ from converter')
    check(items[0][1]['scale']==items[1][1]['scale'],'One shared UV scale for Base and Plate')
    for kind,m in items:
        sha=hashlib.sha256((cv.SOURCE/('rotator_'+kind.lower()+'.stl')).read_bytes()).hexdigest()
        check(f'{kind}SourceSHA256 = "{sha}"' in text,'Approved STL changed')
        v=list(struct.iter_unpack('<8f',m['vertices']));idx=[p[0] for p in struct.iter_unpack('<I',m['indices'])]
        check(len(idx)==len(m['raw']) and len(v)<len(idx),'Indexed topology/count')
        check(all(0<=i<len(v) for i in idx),'Index range')
        for p in v:
            check(abs(cv.length(p[3:6])-1)<1e-6,'Normal length')
            check(all(math.isfinite(t) for t in p[6:8]),'Finite repeating metric UV')
        triangles=[[v[i][:3] for i in idx[j:j+3]] for j in range(0,len(idx),3)]
        original=[m['raw'][j:j+3] for j in range(0,len(idx),3)]
        for tri,src in zip(triangles,original):
            check(all(cv.length(cv.sub(p,q))<1e-7 for p,q in zip(tri,src)),'Same original triangle/winding')
        actual,expected=metrics(triangles),metrics(original)
        volume=float(re.search(kind+r'Volume = ([0-9.eE+-]+)f',text)[1])
        check(abs(volume-expected[1])/expected[1]<=1e-6,'Mass volume constant differs from the approved STL volume')
        for a,b in zip(actual[:2],expected[:2]): check(abs(a-b)/b<=1e-6,'Surface/volume preservation')
        for a,b in zip(sum(actual[2],[]),sum(expected[2],[])): check(abs(a-b)<=max(abs(b),.001)*1e-6,'Bounds preservation')
        boxes=[]
        for chart,ids in m['chart_vertices'].items():
            box=[min(v[i][k] for i in ids) for k in (6,7)]+[max(v[i][k] for i in ids) for k in (6,7)]
            for other in boxes:
                check(box[0]-other[2]>=.004 or other[0]-box[2]>=.004 or box[1]-other[3]>=.004 or other[1]-box[3]>=.004,'Chart bbox overlap/padding')
            boxes.append(box)
        ratios=[];max_wall_angle=0;wall_points={}
        for j,(a,b,c) in enumerate(triangles):
            n=cv.unit(cv.cross(cv.sub(b,a),cv.sub(c,a)))
            verts=[v[i] for i in idx[j*3:j*3+3]]
            if n[1]>1-1e-8:
                for p in verts: check(p[4]>math.cos(math.radians(.1)),'Top normal +Y within .1 degrees')
            if kind=='Base' and max(p[1] for p in (a,b,c))<=.05700001 and min(math.hypot(p[0],p[2]) for p in (a,b,c))>.1999 and abs(n[1])<1e-5:
                for p in verts:
                    check(abs(p[4])<1e-6,'Drum wall normal horizontal')
                    wall_points[round(math.atan2(p[2],p[0]),6)]=p[3:6]
            au,bu,cu=[p[6:8] for p in verts]
            uvarea=abs((bu[0]-au[0])*(cu[1]-au[1])-(bu[1]-au[1])*(cu[0]-au[0]))/2
            area=cv.length(cv.cross(cv.sub(b,a),cv.sub(c,a)))/2
            check(uvarea>1e-14,'Nonzero UV triangle')
            ratios.append(uvarea/area)
        median=statistics.median(ratios)
        for ratio in ratios: check(abs(ratio/median-1)<=.15,'UV/3D area density within 15 percent')
        if wall_points:
            ns=[n for _,n in sorted(wall_points.items())]
            for a,b in zip(ns,ns[1:]+ns[:1]):
                angle=math.degrees(math.acos(max(-1,min(1,cv.dot(a,b)))))
                max_wall_angle=max(max_wall_angle,angle);check(angle<3,'Drum adjacent normals under 3 degrees')
        report[kind]=dict(before_vertices=len(idx),after_vertices=len(v),before_indices=len(idx),after_indices=len(idx),
                          charts=len(boxes),bands=sum(c['kind']=='band' for c in m['charts']),source_sha256=sha,
                          original_area=expected[0],mesh_area=actual[0],original_volume=expected[1],mesh_volume=actual[1],bounds=actual[2],
                          uv_area_ratio_min=min(ratios),uv_area_ratio_median=median,uv_area_ratio_max=max(ratios),max_wall_neighbor_degrees=max_wall_angle)
    report['checks']=checks;report['converter']=cv.CONVERTER_VERSION
    (cv.ROOT/'logs').mkdir(exist_ok=True)
    (cv.ROOT/'logs/mesh-acceptance.json').write_text(json.dumps(report,indent=2)+'\n',encoding='utf-8')
    print('PASS',checks,'mesh/normal/UV/STL preservation checks');print(json.dumps(report,indent=2))

if __name__=='__main__': main()
