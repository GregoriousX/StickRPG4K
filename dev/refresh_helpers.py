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
for file in manifest['files']:
    if file.get('copy'): continue
    installed = (args.game/file['path']).read_bytes()
    is_hq = 'soundtrack' in file and sha(installed) == file['soundtrack']['sha256']
    if sha(installed) != file['sha256'] and not is_hq:
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
    baseline_tags = list(tags)
    if is_hq:
        original_data = (args.originals/file['source']).read_bytes()
        if sha(original_data) != manifest['sources'][file['source']]['sha256']:
            raise SystemExit('Original file checksum mismatch: '+file['source'])
        _, original_tags = swf_parts(original_data)
        sounds = {struct.unpack_from('<H', d)[0]:(c,d) for c,d in original_tags if c==14}
        for replacement in file['soundtrack']['replacements']:
            baseline_tags[replacement['index']] = sounds[replacement['id']]
    baseline = serialize(header, baseline_tags)
    file['sha256'] = sha(baseline)
    file['header'] = base64.b64encode(baseline[:len(header)]).decode()
    if 'soundtrack' in file:
        old_hq_hash = file['soundtrack']['sha256']
        hq_tags = list(tags)
        if not is_hq:
            # A baseline alone cannot reconstruct album frames. Supply a known
            # HQ installation to preview/apply both variants without ambiguity.
            raise SystemExit('Use an installation with the matching Soundtrack installed for '+file['path'])
        file['soundtrack']['previous_sha256'] = old_hq_hash
        file['soundtrack']['sha256'] = sha(serialize(header, hq_tags))
    report.append({'file':file['path'], 'baseline_sha256':file['sha256'], 'hq_sha256':file.get('soundtrack',{}).get('sha256')})
if not report:
    print('No addon changes.'); raise SystemExit(0)
manifest.setdefault('upgrade_from', {})[old['version']] = {
    f['path']:[f['sha256']]+([f['soundtrack']['sha256']] if 'soundtrack' in f else []) for f in old['files']}
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
