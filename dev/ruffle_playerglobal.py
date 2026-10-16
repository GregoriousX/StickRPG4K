"""Build a compile-time playerglobal SWC for JPEXS from the bundled Ruffle runtime.

JPEXS needs flash.* type definitions to compile our ActionScript. Ruffle embeds
its own playerglobal.swf; we normalise its API-versioned namespaces and
namespace-set trait names into the plain form JPEXS indexes. The result is
only a type library for compilation and is never shipped.
"""
from pathlib import Path
import re, struct, sys, zipfile
sys.path.insert(0, str(Path(__file__).resolve().parent))
from abc_patch import ABC, swf_parts, write_swf

def extract(exe):
    data = exe.read_bytes()
    for match in re.finditer(rb'FWS[\x08-\x30]', data):
        start = match.start(); length = struct.unpack_from('<I', data, start+4)[0]
        if 100_000 < length < 5_000_000 and b'flash.display' in data[start:start+length]:
            return data[start:start+length]
    raise SystemExit('Ruffle playerglobal.swf not found in '+str(exe))

def normalise(swf):
    strip = lambda s: s[:-1] if s and 0xE000 <= ord(s[-1]) <= 0xE3FF else s
    header, tags = swf_parts(swf)
    result = []
    for code, body in tags:
        if code == 82:
            start = body.index(b'\0', 4)+1; abc = ABC(body[start:])
            for i in range(1, len(abc.namespaces)):
                kind, name = abc.namespaces[i]
                text = abc.string(name)
                if strip(text) != text: abc.namespaces[i] = (kind, abc.add_string(strip(text)))
            used = {t['name'] for group in (abc.instances, abc.classes, abc.scripts) for item in group for t in item['traits']}
            for i in used:
                kind, values = abc.names[i]
                if kind != 9: continue
                nsset = abc.sets[values[1]]
                pick = next((n for n in nsset if abc.namespaces[n][0] == 22), nsset[0])
                abc.names[i] = (7, [pick, values[0]])
            body = body[:start]+abc.serialize()
        result.append((code, body))
    return header, result

def build(exe, profile):
    work = profile.parent/'playerglobal'; work.mkdir(parents=True, exist_ok=True)
    library = work/'library.swf'
    write_swf(*normalise(extract(exe)), library)
    swc = work/'playerglobal32_0.swc'
    with zipfile.ZipFile(swc, 'w', zipfile.ZIP_DEFLATED) as z:
        z.writestr('catalog.xml', '<?xml version="1.0" encoding="utf-8"?>\n<swc xmlns="http://www.adobe.com/flash/swccatalog/9"><libraries><library path="library.swf"></library></libraries></swc>\n')
        z.write(library, 'library.swf')
    for folder in (profile/'JPEXS/FFDec/flashlib', profile/'.FFDec/flashlib'):
        folder.mkdir(parents=True, exist_ok=True)
        (folder/swc.name).write_bytes(swc.read_bytes())
    return swc

if __name__ == '__main__':
    root = Path(__file__).resolve().parents[1]
    print('Type library:', build(root/'runtime/ruffle.exe', root/'dev/local/compiled/profile'))
