"""Update owned ABC addons and matching baseline/HQ recipes after compilation."""
from pathlib import Path
import argparse, base64, copy, hashlib, json, struct
from abc_patch import swf_parts

root = Path(__file__).resolve().parents[1]
p = argparse.ArgumentParser(description=__doc__)
p.add_argument('--game', type=Path, default=root/'game')
p.add_argument('--originals', type=Path, default=root/'dev/local/original')
p.add_argument('--compiled', type=Path, default=root/'dev/local/compiled')
p.add_argument('--version', required=True, help='A NEW data version, e.g. wide4k-next-2026-10-17')
p.add_argument('--apply', action='store_true', help='Update patches/ after reviewing the default preview.')
args = p.parse_args()
manifest_path = root/'patches/manifest.json'
old = json.loads(manifest_path.read_text())
if args.version == old['version']:
    p.error('Use a new data version so existing installations can upgrade safely.')
manifest = copy.deepcopy(old)
sha = lambda data: hashlib.sha256(data).hexdigest()
def serialize(header, tags):
    body = header[8:] + b''.join((struct.pack('<H', c<<6|len(d)) if len(d)<63 else struct.pack('<HI', c<<6|63, len(d)))+d for c,d in tags)
    return b'FWS'+header[3:4]+struct.pack('<I', len(body)+8)+body
payloads = {}
report = []
def asset(step):
    with open(root/step['asset'], 'rb') as f:
        f.seek(step['offset']); return (step['code'], f.read(step['length']))
for file in manifest['files']:
    if file.get('copy'): continue
    installed = (args.game/file['path']).read_bytes()
    plain = file.get('variants', {}).get('no-upscale')
    hq_hashes = [h for h in (file.get('soundtrack', {}).get('sha256'), plain and plain.get('soundtrack_sha256')) if h]
    base_hashes = [file['sha256']] + ([plain['sha256']] if plain else [])
    is_hq = sha(installed) in hq_hashes
    if sha(installed) not in base_hashes and not is_hq:
        raise SystemExit('Unrecognized generated file: '+file['path'])
    header, tags = swf_parts(installed)
    changed = False
    for index, step in enumerate(file['tags']):
        if not isinstance(step, dict) or 'addon' not in step: continue
        name = {'patches/SRPG2_HDNativeMusic.abc':'NativeMusic', 'patches/SRPG2_HDPreferences.abc':'Preferences', 'patches/SRPG2_HDSmoothWide.abc':'SmoothWide'}.get(step['addon'])
        if name is None: continue
        abc = (args.compiled/(name+'.abc')).read_bytes()
        if tags[index][1] == base64.b64decode(step['prefix'])+abc: continue
        tags[index] = (82, base64.b64decode(step['prefix'])+abc)
        payloads[step['addon']] = abc
        changed = True
    if not changed: continue
    if 'soundtrack' in file and not is_hq:
        # A baseline alone cannot reconstruct album frames. Supply a known
        # HQ installation to preview/apply both variants without ambiguity.
        raise SystemExit('Use an installation with the matching Soundtrack installed for '+file['path'])
    original_data = (args.originals/file['source']).read_bytes()
    if sha(original_data) != manifest['sources'][file['source']]['sha256']:
        raise SystemExit('Original file checksum mismatch: '+file['source'])
    _, original_tags = swf_parts(original_data)
    sounds = {struct.unpack_from('<H', d)[0]:(c,d) for c,d in original_tags if c==14}
    def hashes(hq_tags):
        baseline_tags = list(hq_tags)
        for replacement in file.get('soundtrack', {}).get('replacements', []):
            baseline_tags[replacement['index']] = sounds[replacement['id']]
        baseline = serialize(header, baseline_tags)
        return sha(baseline), base64.b64encode(baseline[:len(header)]).decode(), sha(serialize(header, hq_tags))
    # Optional upscale steps: rebuild both variants from whichever one is installed.
    up_tags, plain_tags = list(tags), list(tags)
    for index, step in enumerate(file['tags']):
        if isinstance(step, dict) and step.get('option') == 'upscale':
            up_tags[index] = asset(step); plain_tags[index] = original_tags[step['original']]
    file['sha256'], file['header'], hq = hashes(up_tags)
    if 'soundtrack' in file:
        file['soundtrack']['previous_sha256'] = file['soundtrack']['sha256']
        file['soundtrack']['sha256'] = hq
    if plain:
        plain['sha256'], plain['header'], plain_hq = hashes(plain_tags)
        if 'soundtrack' in file: plain['soundtrack_sha256'] = plain_hq
    report.append({'file':file['path'], 'baseline_sha256':file['sha256'], 'hq_sha256':file.get('soundtrack',{}).get('sha256')})
if not report:
    print('No addon changes.'); raise SystemExit(0)
def known(f):
    values = [f['sha256']] + ([f['soundtrack']['sha256']] if 'soundtrack' in f else [])
    for v in f.get('variants', {}).values():
        values += [v['sha256']] + ([v['soundtrack_sha256']] if 'soundtrack_sha256' in v else [])
    return values
manifest.setdefault('upgrade_from', {})[old['version']] = {f['path']: known(f) for f in old['files']}
manifest['version'] = args.version
for path, data in payloads.items(): manifest['payloads'][path] = sha(data)
preview = root/'dev/local/manifest-preview.json'
preview.parent.mkdir(parents=True, exist_ok=True)
preview.write_text(json.dumps(manifest, separators=(',',':')), encoding='utf8')
print(json.dumps(report, indent=2))
print('Manifest preview:', preview)
if args.apply:
    for path, data in payloads.items(): (root/path).write_bytes(data)
    manifest_path.write_bytes(preview.read_bytes())
    print('Updated patches. Run config.exe --install to rebuild; keep your current saves.')
