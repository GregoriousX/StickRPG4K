"""Compile our ActionScript modules; generated files stay under dev/local/."""
from pathlib import Path
import argparse, os, subprocess

root = Path(__file__).resolve().parents[1]
p = argparse.ArgumentParser(description=__doc__)
p.add_argument('--toolchain', type=Path, default=root/'dev/toolchain')
p.add_argument('--shell', type=Path, default=root/'game/Zinc Shell Mac.swf')
p.add_argument('--output', type=Path, default=root/'dev/local/compiled')
args = p.parse_args()
toolchain = args.toolchain.resolve()
java = next((toolchain/'java').glob('*/bin/java.exe'), None)
ecj = next((toolchain/'ecj').glob('ecj-*.jar'), None)
if not java or not ecj or not args.shell.is_file():
    p.error('Restore dev/toolchain and install your original game locally first. See docs/DEVELOPMENT.md.')
output = args.output.resolve()
output.mkdir(parents=True, exist_ok=True)
classes = output/'classes'
classes.mkdir(exist_ok=True)
libs = [toolchain/'ffdec/ffdec.jar'] + sorted((toolchain/'ffdec/lib').glob('*.jar'))
env = os.environ.copy()
env['APPDATA'] = str(output/'profile')
subprocess.run([str(java), '-jar', str(ecj), '-17', '-cp', str(toolchain/'ffdec/lib/ffdec_lib.jar'),
                '-d', str(classes), str(root/'dev/CompileAs.java')], env=env, check=True)
for name in ['NativeMusic', 'Preferences', 'SmoothWide']:
    subprocess.run([str(java), '-Xmx2g', '-Djava.awt.headless=true', '-Duser.home='+str(output/'profile'),
                    '-cp', os.pathsep.join(map(str, [classes]+libs)), 'CompileAs', str(args.shell.resolve()),
                    str(root/'src'/(name+'.as')), str(output/(name+'.abc'))], env=env, check=True)
print('Compiled modules:', output)
