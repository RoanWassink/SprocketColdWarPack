import pathlib,struct,json
folder=pathlib.Path(__file__).resolve().parents[1]/'tests/fixtures';folder.mkdir(exist_ok=True)
items=[]
# Logical bottom row red, green; top row blue, white.
rgba=bytes.fromhex('ff0000ff00ff00ff0000ffffffffffff')
def make(bits=24,header=40,compression=0,top=False,alpha=False):
    stride=((2*bits+31)//32)*4
    b=bytearray(header);struct.pack_into('<IiiHHIIiiII',b,0,header,2,-2 if top else 2,1,bits,compression,stride*2,0,0,0,0)
    masks=struct.pack('<III',0xff0000,0xff00,0xff)
    if compression==3:
        if header==40:b.extend(masks)
        else:b[40:52]=masks
    if alpha:struct.pack_into('<IIII',b,40,0xff0000,0xff00,0xff,0xff000000)
    if header>=108:struct.pack_into('<I',b,56,0x73524742)
    pixels=[[(0,0,255),(0,255,0)],[(255,0,0),(255,255,255)]]
    if top:pixels.reverse()
    for row in pixels:
        raw=b''.join(bytes(c)+(b'\xff' if alpha else b'\0') if bits==32 else bytes(c) for c in row)
        b.extend(raw+b'\0'*(stride-len(raw)))
    return b
def add(name,b,valid=True,expected=rgba):
    (folder/name).write_bytes(b);items.append({'file':name,'valid':valid,**({'rgba':expected.hex()} if valid else {})})
for bits in (24,32):
    for top in (False,True):
        for compression in (0,3):
            for header in ((40,) if compression==0 else (40,52,56,108,124)):
                add(f'{bits}-{header}-{compression}-'+('top' if top else 'bottom')+'.dib',make(bits,header,compression,top))
for comp in (0,3):
    add(f'v5-alpha-{comp}.dib',make(32,124,comp,False,True))
b=make(32,56,3)
struct.pack_into('<III',b,40,0x3ff00000,0x000ffc00,0x000003ff)
for i,v in enumerate((0x3ff00000,0x000ffc00,0x000003ff,0x3fffffff)):struct.pack_into('<I',b,56+i*4,v)
add('bitfields-10bit-normalized.dib',b)
alpha=make(32,124,3,False,True);alpha[124+3]=0
add('v5-alpha-flatten.dib',alpha,True,bytes([48,48,48,255])+rgba[4:])
b=make();add('short.dib',b[:25],False)
for name,offset,fmt,value in [('header',0,'I',41),('planes',12,'H',2),('width',4,'i',-2),('height-overflow',8,'i',-2147483648),('compression',16,'I',4),('palette',32,'I',1),('oversize',4,'i',9000),('sizeimage',20,'I',1)]:
    b=make();struct.pack_into('<'+fmt,b,offset,value);add('bad-'+name+'.dib',b,False)
b=make();add('truncated-pixels.dib',b[:-2],False)
b=make(32,124,3);struct.pack_into('<I',b,44,0xff0000);add('overlap-masks.dib',b,False)
b=make(32,124,3);struct.pack_into('<I',b,40,0xf0f0);add('noncontiguous-mask.dib',b,False)
b=make(32,124,3);struct.pack_into('<II',b,112,124,10);add('profile.dib',b,False)
b=make(32,108,3);struct.pack_into('<I',b,56,0x4d424544);add('exotic-space.dib',b,False)
(folder/'dib-expected.json').write_text(json.dumps(items,indent=2)+'\n')
print(f'Wrote {len(items)} DIB fixtures.')
