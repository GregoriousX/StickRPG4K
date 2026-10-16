"""Historical reference for the 1.4.0 method edits. Importing/executing is disabled.
The paths below describe the old workstation and are not build inputs.
"""
raise SystemExit("Reference only: use current installed files and docs/DEVELOPMENT.md.")
from pathlib import Path
import json,struct
from abc_patch import ABC,Assembler,Reader,u30,swf_parts,write_swf
root=Path(__file__).resolve().parents[1]
out=root/'analysis/offline-ui';out.mkdir(exist_ok=True)
source=root/'analysis/soundtrack-release-test-02/Moved game/game'
report=[]
def suffix(abc,method,extra):
    body=abc.body(method);assert body['code'][-1:]==b'\x47'
    r=Reader(abc.data[body['start']:body['end']])
    for _ in range(5):r.vu()
    r.take(r.vu());tail=r.take(len(r.data)-r.pos)
    code=body['code'][:-1]+extra+b'\x47'
    abc.patches[method]=b''.join(u30(v) for v in (method,max(10,body['stack']),body['regs'],body['scope'],body['maxscope'],len(code)))+code+tail
def route(abc,classname,original,replacement,args):
    a=Assembler().emit(0xd0).emit(0x30).emit(0x60,abc.add_qname('com.srpg2.hd','Preferences')).emit(0xd0)
    for local in range(1,args+1):a.emit(0x62,local)
    a.emit(0x4f,abc.add_qname('',replacement),args+1).emit(0x47)
    abc.set_code(classname,original,a.finish(),stack=8)
    report.append(classname+'.'+original)
for filename in ['dat.swf','Minigames.swf']:
    header,tags=swf_parts((source/filename).read_bytes())
    for index,(code,data) in enumerate(tags):
        if code!=82:continue
        start=data.index(b'\0',4)+1;name=data[4:start-1]
        if name==b'SRPG2_HDNativeMusic':
            tags[index]=(82,data[:start]+(out/'NativeMusic.abc').read_bytes());continue
        abc=ABC(data[start:]);q=lambda n:abc.add_qname('',n)
        classes={abc.qname(i['name']) for i in abc.instances}
        if 'com.srpg2.SoundManager' in classes:
            cls='com.srpg2.SoundManager'
            for original,args in [('playSound',3),('playInterruptableSound',3),('playInterruptableLoop',4)]:route(abc,cls,original,original,args)
            a=Assembler().emit(0xd0).emit(0x30).emit(0x60,abc.add_qname('com.srpg2.hd','Preferences')).emit(0x4f,q('interruptSound'),0).emit(0x47)
            abc.set_code(cls,'interruptSound',a.finish())
            a=Assembler().emit(0xd0).emit(0x30).emit(0xd0).emit(0xd1).emit(0x61,abc.member_name(cls,'_volume'))
            a.emit(0x60,abc.add_qname('com.srpg2.hd','Preferences')).emit(0x4f,q('refreshSfx'),0).emit(0x47)
            abc.set_code(cls,'set:volume',a.finish())
        if 'com.srpg2.SRPG2_Main' in classes:
            a=Assembler().emit(0x60,abc.add_qname('com.srpg2.hd','Preferences')).emit(0xd0).emit(0x4f,q('attach'),1)
            abc.prepend_simple(abc.find_method('com.srpg2.SRPG2_Main','onStage'),a.finish())
            report.append('SRPG2_Main.onStage menu listener')
        if 'com.srpg2.XPhone' in classes:
            cls='com.srpg2.XPhone'
            a=Assembler().emit(0xd0).emit(0x2c,abc.add_string('OFFLINE')).emit(0x61,q('username'))
            a.emit(0xd0).emit(0x20).emit(0x61,q('password')).emit(0xd0).emit(0x24,3).emit(0x61,q('quality'))
            a.emit(0xd0)
            for i in range(5):a.emit(0x24,i)
            a.emit(0x56,5).emit(0x61,q('owned_cheats'))
            suffix(abc,abc.find_method(cls,'<cinit>'),a.finish())
            a=Assembler().emit(0x60,abc.add_qname('com.srpg2.hd','Preferences')).emit(0xd0)
            for method in ['updateQuality','toggleFullscreen']:a.emit(0xd0).emit(0x66,abc.member_name(cls,method))
            a.emit(0x4f,q('configurePhone'),3)
            suffix(abc,abc.find_method(cls,'<init>'),a.finish())
            route(abc,cls,'changeScreen','changeScreen',1)
            for method in ['loginClick','registerClick','buyXCash','updateXCash','purchaseCallback','priceListLoaded','getOwnedItems']:
                abc.set_code(cls,method,Assembler().emit(0xd0).emit(0x30).emit(0x47).finish())
                report.append(cls+'.'+method+' offline no-op')
            for method in ['doSave','doLoad','populateSaveSlots']:
                a=Assembler().emit(0x60,abc.add_qname('com.srpg2','XPhone')).emit(0x2c,abc.add_string('OFFLINE')).emit(0x61,q('username'))
                # Prepending shifts the complete method uniformly, including its
                # internal branches; preserve activation traits and exceptions.
                body=abc.body(abc.find_method(cls,method));r=Reader(abc.data[body['start']:body['end']])
                for _ in range(5):r.vu()
                r.take(r.vu());tail=r.take(len(r.data)-r.pos)
                assert tail[0]==0,'Unexpected exception table'
                new=a.finish()+body['code'];mid=body['method']
                abc.patches[mid]=b''.join(u30(v) for v in (mid,max(8,body['stack']),body['regs'],body['scope'],body['maxscope'],len(new)))+new+tail
            report.append('XPhone: OFFLINE local saves, Ultra default and options panel')
        if abc.patches:tags[index]=(82,data[:start]+abc.serialize())
    if filename=='dat.swf':
        first=next(i for i,(c,d) in enumerate(tags) if c==82)
        tags.insert(first,(82,struct.pack('<I',1)+b'SRPG2_HDPreferences\0'+(out/'Preferences.abc').read_bytes()))
    write_swf(header,tags,out/filename)
(out/'code-patch-report.json').write_text(json.dumps(report,indent=2))
print(json.dumps(report,indent=2))
