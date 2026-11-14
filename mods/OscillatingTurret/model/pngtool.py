"""Minimal PNG read/write (no packages) and the icon step for the Heavy Trunnion Mount.

py -I pngtool.py icon <render.png> <out.png> [size]   render -> square RGBA icon, background made transparent
py -I pngtool.py info <file.png>                      size, mode, corner and centre pixels
"""
import struct, sys, zlib


def read_png(path):
    data = open(path, 'rb').read()
    assert data[:8] == b'\x89PNG\r\n\x1a\n', path
    pos, idat, plte, trns = 8, b'', None, None
    while pos < len(data):
        n, kind = struct.unpack('>I4s', data[pos:pos + 8]); body = data[pos + 8:pos + 8 + n]; pos += 12 + n
        if kind == b'IHDR': w, h, depth, ctype, _, _, inter = struct.unpack('>IIBBBBB', body)
        elif kind == b'PLTE': plte = body
        elif kind == b'tRNS': trns = body
        elif kind == b'IDAT': idat += body
    assert depth == 8 and inter == 0, 'only 8-bit non-interlaced PNG'
    ch = {0: 1, 2: 3, 3: 1, 4: 2, 6: 4}[ctype]
    raw, stride, rows, prev = zlib.decompress(idat), w * ch, [], bytearray(w * ch)
    for y in range(h):
        f = raw[y * (stride + 1)]; line = bytearray(raw[y * (stride + 1) + 1:(y + 1) * (stride + 1)])
        for i in range(stride):
            a = line[i - ch] if i >= ch else 0; b = prev[i]; c = prev[i - ch] if i >= ch else 0
            if f == 1: line[i] = (line[i] + a) & 255
            elif f == 2: line[i] = (line[i] + b) & 255
            elif f == 3: line[i] = (line[i] + (a + b) // 2) & 255
            elif f == 4:
                p = a + b - c; pa, pb, pc = abs(p - a), abs(p - b), abs(p - c)
                line[i] = (line[i] + (a if pa <= pb and pa <= pc else b if pb <= pc else c)) & 255
        rows.append(line); prev = line
    px = []
    for line in rows:
        r = []
        for x in range(w):
            v = line[x * ch:(x + 1) * ch]
            if ctype == 6: r.append(tuple(v))
            elif ctype == 2: r.append((v[0], v[1], v[2], 255))
            elif ctype == 0: r.append((v[0], v[0], v[0], 255))
            elif ctype == 4: r.append((v[0], v[0], v[0], v[1]))
            else:
                i = v[0]; r.append(tuple(plte[3 * i:3 * i + 3]) + ((trns[i] if trns and i < len(trns) else 255),))
        px.append(r)
    return w, h, px


def write_png(path, w, h, px):
    raw = b''.join(b'\x00' + bytes(c for p in row for c in p) for row in px)
    chunk = lambda k, b: struct.pack('>I', len(b)) + k + b + struct.pack('>I', zlib.crc32(k + b) & 0xffffffff)
    open(path, 'wb').write(b'\x89PNG\r\n\x1a\n' + chunk(b'IHDR', struct.pack('>IIBBBBB', w, h, 8, 6, 0, 0, 0))
                           + chunk(b'IDAT', zlib.compress(raw, 9)) + chunk(b'IEND', b''))


def icon(src, dst, size=128):
    w, h, px = read_png(src)
    bg = px[0][0][:3]
    # crop to the model (non-background) plus a small margin, square
    xs = [x for y in range(h) for x in range(w) if px[y][x][:3] != bg]
    ys = [y for y in range(h) for x in range(w) if px[y][x][:3] != bg]
    x0, x1, y0, y1 = min(xs), max(xs), min(ys), max(ys)
    side = int(max(x1 - x0, y1 - y0) * 1.06) + 1
    cx, cy = (x0 + x1) // 2, (y0 + y1) // 2
    sx, sy = cx - side // 2, cy - side // 2
    out = []
    for oy in range(size):
        row = []
        for ox in range(size):
            acc, n, cov = [0, 0, 0], 0, 0
            for yy in range(sy + oy * side // size, sy + (oy + 1) * side // size):
                for xx in range(sx + ox * side // size, sx + (ox + 1) * side // size):
                    n += 1
                    if 0 <= yy < h and 0 <= xx < w and px[yy][xx][:3] != bg:
                        cov += 1
                        for i in range(3): acc[i] += px[yy][xx][i]
            row.append((acc[0] // cov, acc[1] // cov, acc[2] // cov, 255 * cov // n) if cov else (0, 0, 0, 0))
        out.append(row)
    write_png(dst, size, size, out)


if __name__ == '__main__':
    if sys.argv[1] == 'icon':
        icon(sys.argv[2], sys.argv[3], int(sys.argv[4]) if len(sys.argv) > 4 else 128)
    else:
        w, h, px = read_png(sys.argv[2])
        print(w, h, 'corner', px[0][0], 'centre', px[h // 2][w // 2])
