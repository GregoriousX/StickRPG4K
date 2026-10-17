"""Decode and encode SWF bitmap tags (DefineBits*, DefineBitsLossless*) with Pillow."""
import io, struct, zlib

BITMAP_CODES = {6: 'JPEG', 21: 'JPEG2', 35: 'JPEG3', 90: 'JPEG4', 20: 'Lossless', 36: 'Lossless2'}

def _jpeg(data, tables=None):
    from PIL import Image
    if data.startswith(b'\xff\xd9\xff\xd8'): data = data[4:]
    if tables:
        if tables.startswith(b'\xff\xd9\xff\xd8'): tables = tables[4:]
        data = tables[:-2] + data[2:] if tables.endswith(b'\xff\xd9') else tables + data[2:]
    image = Image.open(io.BytesIO(data)); image.load()
    return image

def _unpremultiply(pixels):
    out = bytearray(pixels)
    for i in range(0, len(out), 4):
        a = out[i+3]
        if 0 < a < 255:
            for j in range(3): out[i+j] = min(255, out[i+j]*255 // a)
    return bytes(out)

def decode(code, body, tables=None):
    """Return an RGBA Pillow image (straight alpha)."""
    from PIL import Image
    if code in (6, 21):
        return _jpeg(body[2:], tables if code == 6 else None).convert('RGBA')
    if code in (35, 90):
        alpha_offset = struct.unpack_from('<I', body, 2)[0]; start = 6 if code == 35 else 8
        image = _jpeg(body[start:start+alpha_offset]).convert('RGB')
        alpha = zlib.decompress(body[start+alpha_offset:])
        if not alpha:
            image.putalpha(Image.new('L', image.size, 255)); return image
        # JPEG3 colour is stored premultiplied by its alpha channel.
        rgba = image.tobytes()
        pixels = bytearray(len(alpha)*4)
        for i, a in enumerate(alpha): pixels[i*4:i*4+4] = bytes((rgba[i*3], rgba[i*3+1], rgba[i*3+2], a))
        return Image.frombytes('RGBA', image.size, _unpremultiply(bytes(pixels)))
    if code in (20, 36):
        fmt, width, height = struct.unpack_from('<BHH', body, 2); pos = 7
        alpha = code == 36
        if fmt == 3:
            count = body[pos] + 1; pos += 1
            raw = zlib.decompress(body[pos:]); entry = 4 if alpha else 3
            table = raw[:count*entry]; stride = (width + 3) & ~3; pixels = bytearray()
            for y in range(height):
                for x in range(width):
                    i = raw[count*entry + y*stride + x]*entry
                    pixels += table[i:i+3] + (table[i+3:i+4] if alpha else b'\xff')
            pixels = bytes(pixels)
        elif fmt == 5:
            raw = zlib.decompress(body[pos:]); pixels = bytearray()
            for i in range(0, width*height*4, 4):
                a = raw[i] if alpha else 255
                pixels += bytes((raw[i+1], raw[i+2], raw[i+3], a))
            pixels = bytes(pixels)
        else:
            raise ValueError('Unsupported lossless format %d' % fmt)
        if alpha: pixels = _unpremultiply(pixels)
        return Image.frombytes('RGBA', (width, height), pixels)
    raise ValueError('Not a bitmap tag: %d' % code)

def encode(code, cid, image, quality=95):
    """Re-encode in the same family: JPEG* -> JPEG2/JPEG3, Lossless* -> Lossless2.
    Returns (new_code, body)."""
    image = image.convert('RGBA')
    if code in (6, 21, 35, 90):
        alpha = image.getchannel('A')
        if code in (6, 21) and alpha.getextrema() == (255, 255):
            rgb = io.BytesIO(); image.convert('RGB').save(rgb, 'JPEG', quality=quality, subsampling=0, optimize=True)
            return 21, struct.pack('<H', cid) + rgb.getvalue()
        # Store JPEG3 colour premultiplied, as Flash expects.
        from PIL import Image, ImageChops
        premultiplied = Image.merge('RGB', [ImageChops.multiply(channel, alpha) for channel in image.split()[:3]])
        rgb = io.BytesIO(); premultiplied.save(rgb, 'JPEG', quality=quality, subsampling=0, optimize=True)
        jpeg = rgb.getvalue()
        return 35, struct.pack('<HI', cid, len(jpeg)) + jpeg + zlib.compress(alpha.tobytes(), 9)
    width, height = image.size; raw = bytearray()
    for r, g, b, a in image.getdata():
        raw += bytes((a, r*a // 255, g*a // 255, b*a // 255))
    return 36, struct.pack('<HBHH', cid, 5, width, height) + zlib.compress(bytes(raw), 9)
