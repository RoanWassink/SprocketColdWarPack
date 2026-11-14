import struct, sys, pathlib

def inspect(path):
    data=path.read_bytes()
    count=struct.unpack_from('<I',data,80)[0] if len(data)>=84 else -1
    if 84+50*count==len(data):
        points=[struct.unpack_from('<3f',data,84+50*i+12+12*j) for i in range(count) for j in range(3)]
    else:
        lines=data.decode('ascii').splitlines()
        points=[tuple(map(float,line.split()[1:4])) for line in lines if line.strip().startswith('vertex ')]
        count=len(points)//3
    lo=[min(p[i] for p in points) for i in range(3)]
    hi=[max(p[i] for p in points) for i in range(3)]
    print(f'{path.name}: triangles={count}; min={lo}; max={hi}; size={[round(hi[i]-lo[i],4) for i in range(3)]} mm')
for name in sys.argv[1:] or ['rotator_base.stl','rotator_plate.stl']:
    inspect(pathlib.Path(name))
