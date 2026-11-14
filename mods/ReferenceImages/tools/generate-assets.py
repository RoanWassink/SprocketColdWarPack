"""Rebuild only derived assets; approved model/ is read-only."""
import pathlib, re, hashlib, json, struct, zlib
root=pathlib.Path(__file__).resolve().parents[1]
model=root/'model'
out=['// Generated from the approved STL files by tools/generate-assets.py.','namespace SprocketReferenceImages;','internal static class BoardMeshData','{']
manifest={}
for kind in ('frame','image'):
    path=model/f'reference-board-{kind}.stl'
    data=path.read_bytes(); text=data.decode('ascii')
    verts=[tuple(map(float,x)) for x in re.findall(r'vertex\s+(\S+)\s+(\S+)\s+(\S+)',text)]
    norms=[tuple(map(float,x)) for x in re.findall(r'facet normal\s+(\S+)\s+(\S+)\s+(\S+)',text)]
    mapped=[(x,z,-y) for x,y,z in verts]
    directions=[(x,z,-y) for n in norms for x,y,z in [n]*3]
    def arr(name,values):
        out.append(f'    internal static readonly float[] {kind.title()}{name} = new float[] {{')
        for i in range(0,len(values),9):out.append('        '+', '.join(format(x,'.9g')+'f' for x in values[i:i+9])+',')
        out.append('    };')
    out.append(f'    internal const string {kind.title()}Sha256 = "{hashlib.sha256(data).hexdigest().upper()}";')
    arr('Vertices',[v for xyz in mapped for v in xyz]); arr('Normals',[v for xyz in directions for v in xyz])
    manifest[kind]={'sha256':hashlib.sha256(data).hexdigest().upper(),'triangles':len(norms)}
out.append('}')
(root/'src'/'BoardMeshData.cs').write_text('\n'.join(out)+'\n',encoding='utf-8')
(root/'assets').mkdir(exist_ok=True)
(root/'assets'/'mesh-manifest.json').write_text(json.dumps(manifest,indent=2)+'\n')
# 2D placeholder, hand-defined pixel lettering. No change to approved 3D assets.
w=h=256; pixels=bytearray([48,48,48,255])*(w*h)
font={'N':['10001','11001','10101','10011','10001'],'O':['01110','10001','10001','10001','01110'],'I':['11111','00100','00100','00100','11111'],'M':['10001','11011','10101','10001','10001'],'A':['01110','10001','11111','10001','10001'],'G':['01111','10000','10111','10001','01111'],'E':['11111','10000','11110','10000','11111']}
def pixel(x,y,c):pixels[(y*w+x)*4:(y*w+x+1)*4]=bytes(c)
for y in range(h):
    for x in range(w):
        if (x//16+y//16)%2:pixel(x,y,(55,55,55,255))
for word,y0 in [('NO',72),('IMAGE',128)]:
    scale=6; x0=(256-(len(word)*6-1)*scale)//2
    for n,ch in enumerate(word):
        for y,row in enumerate(font[ch]):
            for x,c in enumerate(row):
                if c=='1':
                    for dy in range(scale):
                        for dx in range(scale):pixel(x0+n*36+x*scale+dx,y0+y*scale+dy,(230,201,116,255))
def chunk(tag,b):return struct.pack('>I',len(b))+tag+b+struct.pack('>I',zlib.crc32(tag+b)&0xffffffff)
raw=b''.join(b'\0'+pixels[y*w*4:(y+1)*w*4] for y in range(h))
png=b'\x89PNG\r\n\x1a\n'+chunk(b'IHDR',struct.pack('>IIBBBBB',w,h,8,6,0,0,0))+chunk(b'IDAT',zlib.compress(raw))+chunk(b'IEND',b'')
(root/'assets'/'no-image.png').write_bytes(png)
print('Generated mesh arrays, manifest and NO IMAGE placeholder.')
