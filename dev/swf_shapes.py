"""Parse DefineShape1-4 fill styles and rescale bitmap fills without touching geometry."""
import struct

SHAPE_CODES = {2: 1, 22: 2, 32: 3, 83: 4}

class Bits:
    def __init__(self, data, pos=0): self.data, self.bit = data, pos*8
    def u(self, n):
        value = 0
        for _ in range(n):
            value = (value << 1) | ((self.data[self.bit >> 3] >> (7 - (self.bit & 7))) & 1); self.bit += 1
        return value
    def s(self, n):
        value = self.u(n)
        return value - (1 << n) if n and value & (1 << (n-1)) else value
    def align(self): self.bit = (self.bit + 7) & ~7
    @property
    def pos(self): return self.bit >> 3

class Writer:
    def __init__(self): self.out, self.acc, self.n = bytearray(), 0, 0
    def u(self, value, n):
        for i in range(n-1, -1, -1):
            self.acc = (self.acc << 1) | ((value >> i) & 1); self.n += 1
            if self.n == 8: self.out.append(self.acc); self.acc = self.n = 0
    def flush(self):
        if self.n: self.out.append(self.acc << (8 - self.n)); self.acc = self.n = 0
        return bytes(self.out)

def sbits(*values):
    need = 1
    for v in values:
        while not (-(1 << (need-1)) <= v < (1 << (need-1))): need += 1
    return need

def read_matrix(data, pos):
    b = Bits(data, pos); sx = sy = 1 << 16; r0 = r1 = 0
    if b.u(1): n = b.u(5); sx, sy = b.s(n), b.s(n)
    if b.u(1): n = b.u(5); r0, r1 = b.s(n), b.s(n)
    n = b.u(5); tx, ty = b.s(n), b.s(n)
    b.align()
    return [sx, sy, r0, r1, tx, ty], b.pos

def write_matrix(m):
    sx, sy, r0, r1, tx, ty = m; w = Writer()
    if (sx, sy) != (1 << 16, 1 << 16):
        n = sbits(sx, sy); w.u(1, 1); w.u(n, 5); w.u(sx & ((1 << n)-1), n); w.u(sy & ((1 << n)-1), n)
    else: w.u(0, 1)
    if (r0, r1) != (0, 0):
        n = sbits(r0, r1); w.u(1, 1); w.u(n, 5); w.u(r0 & ((1 << n)-1), n); w.u(r1 & ((1 << n)-1), n)
    else: w.u(0, 1)
    n = sbits(tx, ty) if (tx, ty) != (0, 0) else 0
    w.u(n, 5)
    if n: w.u(tx & ((1 << n)-1), n); w.u(ty & ((1 << n)-1), n)
    return w.flush()

class Shape:
    """Records byte ranges of every fill style; rewriting splices whole bytes."""
    def __init__(self, code, body):
        self.code, self.body, self.version = code, body, SHAPE_CODES[code]
        self.id = struct.unpack_from('<H', body)[0]
        b = Bits(body, 2); n = b.u(5); b.bit += 4*n; b.align()
        if self.version == 4:
            n = b.u(5); b.bit += 4*n; b.align(); b.bit += 8
        self.fills = []  # (type_offset, matrix_offset, matrix_end, bitmap_id)
        pos = self.styles(b.pos)
        b = Bits(body, pos); fill_bits, line_bits = b.u(4), b.u(4)
        while True:
            if b.u(1):  # edge
                straight = b.u(1); n = b.u(4) + 2
                if straight:
                    if b.u(1): b.bit += 2*n
                    else: b.bit += 1 + n
                else: b.bit += 4*n
                continue
            flags = b.u(5)
            if not flags: break
            if flags & 1: n = b.u(5); b.bit += 2*n
            if flags & 2: b.bit += fill_bits
            if flags & 4: b.bit += fill_bits
            if flags & 8: b.bit += line_bits
            if flags & 16:
                b.align(); pos = self.styles(b.pos); b = Bits(body, pos)
                fill_bits, line_bits = b.u(4), b.u(4)

    def styles(self, pos):
        body = self.body
        count = body[pos]; pos += 1
        if count == 0xFF and self.version >= 2: count = struct.unpack_from('<H', body, pos)[0]; pos += 2
        for _ in range(count): pos = self.fill(pos)
        count = body[pos]; pos += 1
        if count == 0xFF: count = struct.unpack_from('<H', body, pos)[0]; pos += 2
        for _ in range(count):
            pos += 2
            if self.version == 4:
                flags = struct.unpack_from('<H', body, pos)[0]; pos += 2
                if (flags >> 4) & 3 == 2: pos += 2
                if flags & 0x0800: pos = self.fill(pos)
                else: pos += 4
            else: pos += 4 if self.version == 3 else 3
        return pos

    def fill(self, pos):
        body = self.body; kind = body[pos]; start = pos; pos += 1
        color = 4 if self.version >= 3 else 3
        if kind == 0: return pos + color
        if kind in (0x10, 0x12, 0x13):
            _, pos = read_matrix(body, pos)
            n = body[pos] & 15; pos += 1 + n*(1 + color)
            return pos + (2 if kind == 0x13 else 0)
        if kind in (0x40, 0x41, 0x42, 0x43):
            bitmap = struct.unpack_from('<H', body, pos)[0]; pos += 2
            _, end = read_matrix(body, pos)
            self.fills.append((start, pos, end, bitmap))
            return end
        raise ValueError('Unknown fill style %#x in shape %d' % (kind, self.id))

    def bitmaps(self): return {f[3] for f in self.fills}

    def rescale(self, factors, smooth=True):
        """Divide bitmap fill matrices by factor[bitmap]; returns the new tag body."""
        out = bytearray(); last = 0
        for start, mpos, mend, bitmap in self.fills:
            if bitmap not in factors: continue
            k = factors[bitmap]; m, _ = read_matrix(self.body, mpos)
            m = [round(v / k) for v in m[:4]] + m[4:]
            kind = self.body[start]
            if smooth and kind in (0x41, 0x43): kind -= 1  # smoothed variants
            out += self.body[last:start] + bytes([kind]) + self.body[start+1:mpos] + write_matrix(m)
            last = mend
        return bytes(out + self.body[last:])
