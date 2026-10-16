from pathlib import Path
from collections import Counter
import re
import struct
import zlib

ROOT = Path(__file__).resolve().parents[1]
FOLDER = ROOT / "dev" / "local" / "original"

def expand(data):
    if data[:3] == b"CWS":
        return b"FWS" + data[3:8] + zlib.decompress(data[8:])
    if data[:3] != b"FWS":
        raise ValueError("Unsupported SWF signature")
    return data

def uint(data, bit, count):
    value = 0
    for index in range(count):
        value = (value << 1) | ((data[(bit+index)//8] >> (7-(bit+index)%8)) & 1)
    return value, bit+count

def info(path):
    raw = path.read_bytes()
    data = expand(raw)
    n, bit = uint(data, 64, 5)
    coords = []
    for _ in range(4):
        value, bit = uint(data, bit, n)
        if value & (1 << (n-1)):
            value -= 1 << n
        coords.append(value)
    start = (bit+7)//8
    rate, frames = struct.unpack_from("<HH", data, start)
    width, height = (coords[1]-coords[0])/20, (coords[3]-coords[2])/20
    print(path.name, "signature", raw[:3].decode(), "version", data[3], "size", len(data), "stage", [width,height], "fps", rate/256, "frames", frames)
    pos = start+4
    counts = Counter()
    while pos+2 <= len(data):
        word = struct.unpack_from("<H", data, pos)[0]
        pos += 2
        code, length = word >> 6, word & 63
        if length == 63:
            length = struct.unpack_from("<I", data, pos)[0]
            pos += 4
        if pos+length > len(data):
            raise ValueError("Truncated SWF tag")
        counts[code] += 1
        pos += length
        if code == 0:
            break
    print("tags", dict(sorted(counts.items())))
    output = ROOT / "dev" / "local" / "inspection"
    output.mkdir(parents=True, exist_ok=True)
    target = output / (path.stem.replace(" ","_")+"_expanded.swf")
    target.write_bytes(data)
    strings = [s.decode("ascii") for s in re.findall(rb"[\x20-\x7e]{4,}", data)]
    (output / (path.stem.replace(" ","_")+"_strings.txt")).write_text("\n".join(strings), encoding="utf-8")
    interesting = [s for s in strings if re.search(r"fullScreen|stageWidth|stageHeight|scaleMode|resize|640|480|559|566|quality|BitmapData|displayState|cacheAsBitmap|mapVersion|Minigames|zinc|mdm", s, re.I)]
    print("interesting strings", [s[:180] for s in interesting[:100]])

if __name__ == "__main__":
    for path in sorted(FOLDER.glob("*.swf")):
        info(path)
