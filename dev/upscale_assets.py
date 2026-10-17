"""Upscale the original game's bitmaps (never earlier AI output) in place.

The recipe keeps only rebane2001's hand-made art (assets/<swf>-hd.bin); every other
bitmap drawn through shapes comes from the original SWF and is upscaled here.
Categories:
  icons  dat.swf `*_icon` item icons (65x65), 4x with Remacri. Ids/classes stay;
         inventory timeline placements are scaled by 1/4 here and code-created
         Bitmaps at runtime by Preferences (ICON_SIZE/ICON_SCALE).
  ui     bitmaps under 400 px (buttons, panels, frames), 4x with
         realesrgan-x4plus-anime.
  scene  bitmaps of 400 px or more (backgrounds, panoramas) with Remacri.
Factors are capped so no bitmap exceeds 4096 px; fill matrices are divided accordingly.

Hand-made replacements: dev/overrides/<swf stem>_<bitmap id>.png (4x the original size,
RGBA) replace the model output for that bitmap. `override` applies them to files whose
upscale steps already exist, writing assets/<swf>-04.bin.

Stages (all intermediate files stay in dev/local/upscale):
  extract   decode originals to PNG and write plan.json
  upscale   run Real-ESRGAN (dev/toolchain, never committed)
  override  apply dev/overrides to already upscaled files (needs --version)
  build     write assets/<swf>-03.bin and update recipes/hashes in
            patches/manifest.json (preview unless --apply)
build needs the current HQ installation in game/ matching patches/manifest.json.
Requires Pillow (dev/local/venv) and verified originals in dev/local/original.
"""
from pathlib import Path
import argparse, base64, copy, hashlib, json, shutil, struct, subprocess, sys
sys.path.insert(0, str(Path(__file__).resolve().parent))
from abc_patch import swf_parts
from swf_shapes import Shape, SHAPE_CODES, read_matrix, write_matrix
from swf_bitmaps import BITMAP_CODES, decode, encode

root = Path(__file__).resolve().parents[1]
work = root/'dev/local/upscale'
FILES = ['dat.swf', 'Minigames.swf', 'sa_ballistick_beta_insecure.swf']
# Painted game textures: Remacri for every bitmap regardless of size (combat arena).
REMACRI_FILES = {'sa_ballistick_beta_insecure.swf'}
ICON_FACTOR = 4
MODELS = {'icons': ('models-extra', 'remacri-4x'), 'ui': ('realesrgan/models', 'realesrgan-x4plus-anime'),
          'scene': ('models-extra', 'remacri-4x')}
SCENE_SIZE = 400  # bitmaps this large (backgrounds, panoramas) are painted art
sha = lambda data: hashlib.sha256(data).hexdigest()

def tag_bytes(code, body):
    head = struct.pack('<H', code << 6 | len(body)) if len(body) < 63 else struct.pack('<HI', code << 6 | 63, len(body))
    return head + body

def serialize(header, tags):
    body = header[8:] + b''.join(tag_bytes(c, d) for c, d in tags)
    return b'FWS' + header[3:4] + struct.pack('<I', len(body) + 8) + body

def known_hashes(entry):
    """Every accepted installed hash of a manifest file: base/HQ, for each variant."""
    values = [entry['sha256']] + ([entry['soundtrack']['sha256']] if 'soundtrack' in entry else [])
    for variant in entry.get('variants', {}).values():
        values += [variant['sha256']] + ([variant['soundtrack_sha256']] if 'soundtrack_sha256' in variant else [])
    return values

def symbols(tags):
    names = {}
    for code, body in tags:
        if code == 76:
            count = struct.unpack_from('<H', body)[0]; pos = 2
            for _ in range(count):
                cid = struct.unpack_from('<H', body, pos)[0]; end = body.index(b'\0', pos+2)
                names[cid] = body[pos+2:end].decode(); pos = end + 1
    return names

def sprite_tags(body):
    pos = 4
    while pos < len(body):
        start = pos; word = struct.unpack_from('<H', body, pos)[0]; pos += 2
        code, length = word >> 6, word & 63
        if length == 63: length = struct.unpack_from('<I', body, pos)[0]; pos += 4
        yield start, pos, code, body[pos:pos+length]; pos += length
        if code == 0: break

def placed_bitmaps(tags):
    placed = set()
    for code, body in tags:
        if code == 39:
            for _, _, c, t in sprite_tags(body):
                if c == 70 and t[0] & 2 and not t[1] & 8: placed.add(struct.unpack_from('<H', t, 4)[0])
    return placed

def rescale_placements(body, factors):
    out = bytearray(body[:4]); changed = 0
    for start, pos, code, tag in sprite_tags(body):
        if code == 70 and tag[0] & 2 and tag[0] & 4 and not tag[1] & 8 and struct.unpack_from('<H', tag, 4)[0] in factors:
            k = factors[struct.unpack_from('<H', tag, 4)[0]]
            matrix, end = read_matrix(tag, 6); matrix[:4] = [round(v / k) for v in matrix[:4]]
            out += tag_bytes(70, tag[:6] + write_matrix(matrix) + tag[end:]); changed += 1
        else: out += body[start:pos+len(tag)]
    return bytes(out), changed

def load(manifest, name):
    entry = next(f for f in manifest['files'] if f['path'] == name)
    data = (root/'dev/local/original'/entry['source']).read_bytes()
    if sha(data) != manifest['sources'][entry['source']]['sha256']: raise SystemExit('Original mismatch: '+name)
    _, tags = swf_parts(data)
    return entry, tags, next((d for c, d in tags if c == 8), None)

def upscaled(entry):
    return any(isinstance(s, dict) and s.get('option') == 'upscale' for s in entry.get('tags', []))

def plan_file(manifest, name):
    entry, tags, tables = load(manifest, name)
    # Files shipped as plain copies are rebuilt tag by tag once upscaled.
    kept = set(range(len(tags))) if entry.get('copy') else {s for s in entry['tags'] if isinstance(s, int)}
    names, placed = symbols(tags), placed_bitmaps(tags)
    users = {}
    for index, (code, body) in enumerate(tags):
        if code in SHAPE_CODES and index in kept:
            for b in Shape(code, body).bitmaps(): users.setdefault(b, []).append(index)
    items = []
    for index, (code, body) in enumerate(tags):
        if code not in BITMAP_CODES or index not in kept: continue
        cid = struct.unpack_from('<H', body)[0]; image = decode(code, body, tables); w, h = image.size
        if code == 35 and names.get(cid, '').endswith('_icon'):
            items.append({'id': cid, 'index': index, 'category': 'icons', 'factor': ICON_FACTOR, 'size': [w, h]})
        elif cid in users and cid not in names and cid not in placed and min(w, h) >= 4:
            k = min(4, 4096 // max(w, h))
            category = 'scene' if max(w, h) >= SCENE_SIZE or name in REMACRI_FILES else 'ui'
            if k >= 2: items.append({'id': cid, 'index': index, 'category': category, 'factor': k, 'size': [w, h]})
    return items, tags, tables

def bleed(image):
    """Fill transparent pixels with nearby opaque colours so upscaling adds no dark fringes."""
    import numpy as np
    from PIL import Image, ImageFilter
    rgba = np.asarray(image, dtype=np.float32)/255; alpha = rgba[..., 3:4]
    premultiplied = Image.fromarray((rgba[..., :3]*alpha*255).astype('uint8'))
    weight = Image.fromarray((alpha[..., 0]*255).astype('uint8'))
    result = rgba[..., :3].copy()
    for radius in (1, 2, 4, 8, 16):
        p = np.asarray(premultiplied.filter(ImageFilter.GaussianBlur(radius)), dtype=np.float32)/255
        w = np.asarray(weight.filter(ImageFilter.GaussianBlur(radius)), dtype=np.float32)[..., None]/255
        fill = (alpha < 0.999) & (w > 0.001)
        result = np.where(fill, np.clip(p/np.maximum(w, 1e-4), 0, 1)*(1-alpha) + result*alpha, result)
    return Image.fromarray((result*255+0.5).astype('uint8'), 'RGB')

def extract():
    manifest = json.loads((root/'patches/manifest.json').read_text())
    plan_path = work/'plan.json'
    plan = json.loads(plan_path.read_text()) if plan_path.exists() else {}
    for name in FILES:
        if upscaled(next(f for f in manifest['files'] if f['path'] == name)):
            print(name, 'already upscaled in the manifest; keeping its plan'); continue
        for old in (work/'src').glob('*/%s_*.png' % Path(name).stem): old.unlink()
        items, tags, tables = plan_file(manifest, name); plan[name] = items
        for item in items:
            target = work/'src'/item['category']; target.mkdir(parents=True, exist_ok=True)
            code, body = tags[item['index']]; image = decode(code, body, tables)
            base = target/('%s_%d' % (Path(name).stem, item['id']))
            if item['category'] != 'icons':
                # The anime model can erase mostly-transparent alpha; run colour and alpha separately.
                if image.getchannel('A').getextrema() == (255, 255): image.convert('RGB').save(str(base)+'.png'); continue
                bleed(image).save(str(base)+'.png'); image.getchannel('A').convert('RGB').save(str(base)+'.alpha.png')
            else: image.save(str(base)+'.png')
        print(name, {c: sum(1 for i in items if i['category'] == c) for c in MODELS})
    (work/'plan.json').write_text(json.dumps(plan))

def upscale():
    import numpy as np
    from PIL import Image
    exe = root/'dev/toolchain/realesrgan/realesrgan-ncnn-vulkan.exe'
    mean = lambda path: np.asarray(Image.open(path).convert('RGB'), dtype=np.float32).mean()
    for category, (folder, model) in MODELS.items():
        source, target = work/'src'/category, work/'x4'/category
        target.mkdir(parents=True, exist_ok=True)
        def chroma(image):
            rgb = np.asarray(image.convert('RGB'), dtype=np.float32); return (rgb.max(2) - rgb.min(2)).mean()
        def ok(path, out):
            a, b = mean(path), mean(out)
            if abs(a-b) > max(8, 0.25*a): return False
            source = Image.open(path); result = Image.open(out)
            # The model invents coloured texture on thin strips (e.g. shelves).
            return chroma(result) - chroma(source.resize(result.size, Image.BICUBIC)) <= 20
        def lanczos(path, out):
            image = Image.open(path); image.resize((image.width*4, image.height*4), Image.LANCZOS).save(out)
            fallback.append(path.name)
        fallback = []
        # One process per image: directory mode corrupts mixed-size batches.
        for path in sorted(source.glob('*.png')):
            out = target/path.name
            if min(Image.open(path).size) < 10: lanczos(path, out); continue  # too thin for the model
            if out.exists() and ok(path, out): continue  # resume
            for attempt in range(3):
                subprocess.run([str(exe), '-i', str(path), '-o', str(out), '-m', str(root/'dev/toolchain'/folder),
                                '-n', model, '-s', '4', '-f', 'png'], check=True, stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL)
                if ok(path, out): break
            else: lanczos(path, out)
        print(category, model, len(list(target.glob('*.png'))), 'files verified; Lanczos fallback:', fallback)

def build(version, apply):
    from PIL import Image
    manifest_path = root/'patches/manifest.json'
    old = json.loads(manifest_path.read_text()); manifest = copy.deepcopy(old)
    if version == old['version']: raise SystemExit('Use a new data version.')
    plan = json.loads((work/'plan.json').read_text())
    for name in FILES:
        entry, original, tables = load(manifest, name)
        if upscaled(entry): print(name, 'already upscaled; unchanged'); continue
        if name not in plan or not plan[name]: print(name, 'nothing planned'); continue
        stem = Path(name).stem; pack_path = 'assets/%s-03.bin' % stem
        replacements = {}; factors = {}
        for item in plan[name]:
            code, body = original[item['index']]
            base = work/'x4'/item['category']/('%s_%d' % (stem, item['id']))
            override = root/'dev/overrides'/('%s_%d.png' % (stem, item['id']))
            image = Image.open(str(override if override.exists() else str(base)+'.png')).convert('RGBA')
            if not override.exists() and (work/'src'/item['category']/('%s_%d.alpha.png' % (stem, item['id']))).exists():
                image.putalpha(Image.open(str(base)+'.alpha.png').convert('L'))
            w, h = item['size']; k = item['factor']
            if image.size != (w*4, h*4): raise SystemExit('Unexpected upscale size for %s %d' % (name, item['id']))
            if k != 4: image = image.resize((w*k, h*k), Image.LANCZOS)
            replacements[item['index']] = encode(code, item['id'], image); factors[item['id']] = k
        ui = {i['id']: i['factor'] for i in plan[name] if i['category'] != 'icons'}
        icons = {i['id']: i['factor'] for i in plan[name] if i['category'] == 'icons'}
        shapes = sprites = 0
        for index, (code, body) in enumerate(original):
            if code in SHAPE_CODES and ui:
                shape = Shape(code, body)
                if shape.bitmaps() & set(ui): replacements[index] = (code, shape.rescale(ui)); shapes += 1
            elif code == 39 and icons:
                new, changed = rescale_placements(body, icons)
                if changed: replacements[index] = (39, new); sprites += changed
        pack = bytearray(); recipe = {}
        for index in sorted(replacements):
            code, body = replacements[index]
            # Optional: Install.ps1 -NoUpscale uses the original tag instead.
            recipe[index] = {'asset': pack_path, 'offset': len(pack), 'length': len(body), 'code': code,
                             'option': 'upscale', 'original': index}; pack += body
        if entry.get('copy'):
            # Plain copy -> recipe of the original tags; its no-upscale variant is the original.
            header, _ = swf_parts((root/'dev/local/original'/entry['source']).read_bytes())
            plain = serialize(header, original)
            tags = list(original)
            for index in recipe: tags[index] = replacements[index]
            built = serialize(header, tags)
            del entry['copy']
            entry['tags'] = [recipe.get(i, i) for i in range(len(original))]
            entry['sha256'], entry['header'] = sha(built), base64.b64encode(built[:len(header)]).decode()
            entry['variants'] = {'no-upscale': {'sha256': sha(plain), 'header': base64.b64encode(plain[:len(header)]).decode()}}
            manifest['payloads'][pack_path] = sha(bytes(pack))
            if apply: (root/pack_path).write_bytes(bytes(pack))
            print('%s: %d bitmaps, %d shapes, %s %.1f MB' % (name, len(ui), shapes, pack_path, len(pack)/2**20))
            continue
        positions = {p: s for p, s in enumerate(entry['tags']) if isinstance(s, int) and s in recipe}
        if len(positions) != len(recipe): raise SystemExit('Some replaced tags are not plain copies in '+name)
        installed = (root/'game'/name).read_bytes()
        if not ('soundtrack' in entry and sha(installed) == entry['soundtrack']['sha256']):
            raise SystemExit('Install the current HQ build (with Soundtrack) before building: '+name)
        header, tags = swf_parts(installed)
        sounds = {struct.unpack_from('<H', d)[0]: (c, d) for c, d in original if c == 14}
        def hashes(hq_tags):
            baseline_tags = list(hq_tags)
            for item in entry['soundtrack']['replacements']: baseline_tags[item['index']] = sounds[item['id']]
            baseline = serialize(header, baseline_tags)
            return sha(baseline), base64.b64encode(baseline[:len(header)]).decode(), sha(serialize(header, hq_tags))
        # The installed build has none of these optional steps: it is the no-upscale variant.
        plain_sha, plain_header, plain_hq = hashes(tags)
        for position, index in positions.items():
            entry['tags'][position] = recipe[index]; tags[position] = replacements[index]
        previous_hq = entry['soundtrack']['sha256']
        entry['sha256'], entry['header'], entry['soundtrack']['sha256'] = hashes(tags)
        entry['soundtrack']['previous_sha256'] = previous_hq
        entry['variants'] = {'no-upscale': {'sha256': plain_sha, 'header': plain_header, 'soundtrack_sha256': plain_hq}}
        manifest['payloads'][pack_path] = sha(bytes(pack))
        if apply: (root/pack_path).write_bytes(bytes(pack))
        print('%s: %d icons, %d ui bitmaps, %d shapes, %d placements, %s %.1f MB' % (
            name, len(icons), len(ui), shapes, sprites, pack_path, len(pack)/2**20))
    manifest.setdefault('upgrade_from', {})[old['version']] = {f['path']: known_hashes(f) for f in old['files']}
    manifest['version'] = version
    preview = root/'dev/local/manifest-preview.json'; preview.write_text(json.dumps(manifest, separators=(',', ':')), encoding='utf8')
    if apply:
        manifest_path.write_bytes(preview.read_bytes()); print('Updated packs and patches/manifest.json. Run config.exe --install.')
    else: print('Preview:', preview)

def override(version, apply):
    """Swap hand-made images into existing optional upscale steps and rehash."""
    from PIL import Image
    manifest_path = root/'patches/manifest.json'
    old = json.loads(manifest_path.read_text()); manifest = copy.deepcopy(old)
    if version == old['version']: raise SystemExit('Use a new data version.')
    changed_files = 0
    for name in FILES:
        entry, original, tables = load(manifest, name)
        stem = Path(name).stem
        files = sorted((root/'dev/overrides').glob('%s_*.png' % stem))
        if not files or not upscaled(entry): continue
        pack_path = 'assets/%s-04.bin' % stem; pack = bytearray(); swaps = {}
        for path in files:
            cid = int(path.stem.rsplit('_', 1)[1])
            index = next(i for i, (c, d) in enumerate(original) if c in BITMAP_CODES and struct.unpack_from('<H', d)[0] == cid)
            code, body = original[index]; w, h = decode(code, body, tables).size
            image = Image.open(path).convert('RGBA')
            if image.size != (w*4, h*4): raise SystemExit('%s must be %dx%d' % (path.name, w*4, h*4))
            step = next(s for s in entry['tags'] if isinstance(s, dict) and s.get('option') == 'upscale' and s['original'] == index)
            new_code, new_body = encode(code, cid, image)
            swaps[index] = (step, new_code, new_body)
        installed = (root/'game'/name).read_bytes()
        if sha(installed) != entry['soundtrack']['sha256']:
            raise SystemExit('Install the current upscaled HQ build before overriding: '+name)
        header, tags = swf_parts(installed)
        sounds = {struct.unpack_from('<H', d)[0]: (c, d) for c, d in original if c == 14}
        for position, step in enumerate(entry['tags']):
            if isinstance(step, dict) and step.get('option') == 'upscale' and step['original'] in swaps:
                _, new_code, new_body = swaps[step['original']]
                step.update({'asset': pack_path, 'offset': len(pack), 'length': len(new_body), 'code': new_code})
                pack += new_body; tags[position] = (new_code, new_body)
        baseline_tags = list(tags)
        for item in entry['soundtrack']['replacements']: baseline_tags[item['index']] = sounds[item['id']]
        baseline = serialize(header, baseline_tags)
        entry['sha256'], entry['header'] = sha(baseline), base64.b64encode(baseline[:len(header)]).decode()
        entry['soundtrack']['previous_sha256'] = entry['soundtrack']['sha256']
        entry['soundtrack']['sha256'] = sha(serialize(header, tags))
        manifest['payloads'][pack_path] = sha(bytes(pack))
        if apply: (root/pack_path).write_bytes(bytes(pack))
        print('%s: %d overrides -> %s' % (name, len(swaps), pack_path)); changed_files += 1
    if not changed_files: print('No overrides to apply.'); return
    manifest.setdefault('upgrade_from', {})[old['version']] = {f['path']: known_hashes(f) for f in old['files']}
    manifest['version'] = version
    preview = root/'dev/local/manifest-preview.json'; preview.write_text(json.dumps(manifest, separators=(',', ':')), encoding='utf8')
    if apply: manifest_path.write_bytes(preview.read_bytes()); print('Updated patches/manifest.json. Run config.exe --install.')

if __name__ == '__main__':
    p = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    p.add_argument('stage', choices=['extract', 'upscale', 'build', 'override'])
    p.add_argument('--version'); p.add_argument('--apply', action='store_true')
    a = p.parse_args()
    if a.stage in ('build', 'override'):
        if not a.version: p.error(a.stage+' needs --version')
        (build if a.stage == 'build' else override)(a.version, a.apply)
    else: globals()[a.stage]()
