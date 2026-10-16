"""Preserve an ABC file and edit selected method bodies, without recompiling classes."""
from pathlib import Path
import struct
import zlib
from swf_info import expand, uint

def u30(value):
    result=bytearray()
    while value>127:
        result.append((value&127)|128)
        value>>=7
    result.append(value)
    return bytes(result)

class Reader:
    def __init__(self,data): self.data,self.pos=data,0
    def byte(self):
        value=self.data[self.pos]; self.pos+=1; return value
    def vu(self):
        result=0
        for i in range(5):
            value=self.byte(); result|=(value&127)<<(i*7)
            if not value&128: return result
        return result
    def take(self,n):
        value=self.data[self.pos:self.pos+n]; self.pos+=n
        assert len(value)==n
        return value

class ABC:
    def __init__(self,data):
        self.data=data; r=Reader(data); r.take(4)
        for kind in ('int','uint','double'):
            for _ in range(max(0,r.vu()-1)):
                r.take(8) if kind=='double' else r.vu()
        self.pool_start=r.pos
        self.strings=[b'']
        for _ in range(max(0,r.vu()-1)): self.strings.append(r.take(r.vu()))
        self.namespaces=[None]
        for _ in range(max(0,r.vu()-1)): self.namespaces.append((r.byte(),r.vu()))
        self.sets=[None]
        for _ in range(max(0,r.vu()-1)): self.sets.append([r.vu() for x in range(r.vu())])
        self.names=[None]
        for _ in range(max(0,r.vu()-1)):
            kind=r.byte()
            if kind in (7,13,9,14): values=[r.vu(),r.vu()]
            elif kind in (15,16,27,28): values=[r.vu()]
            elif kind in (17,18): values=[]
            elif kind==29: values=[r.vu()]; values += [r.vu() for x in range(r.vu())]
            else: raise ValueError('Unknown multiname '+str(kind))
            self.names.append((kind,values))
        self.pool_end=r.pos
        self.method_count_pos=r.pos
        method_count=r.vu()
        self.method_info_start=r.pos
        self.methods=[]
        for _ in range(method_count):
            count=r.vu(); ret=r.vu(); params=[r.vu() for x in range(count)]; name=r.vu(); flags=r.byte()
            if flags&8:
                for x in range(r.vu()): r.vu(); r.byte()
            if flags&128:
                for x in range(count): r.vu()
            self.methods.append({'params':params,'returns':ret,'name':name,'flags':flags})
        self.method_info_end=r.pos
        self.extra_method_info=[]
        self.extra_bodies=[]
        for _ in range(r.vu()):
            r.vu()
            for x in range(r.vu()*2): r.vu()
        count=r.vu(); self.instances=[]; self.classes=[]
        for _ in range(count):
            name=r.vu(); parent=r.vu(); flags=r.byte()
            if flags&8:r.vu()
            for x in range(r.vu()): r.vu()
            init=r.vu()
            self.instances.append({'name':name,'parent':parent,'init':init,'traits':self.read_traits(r)})
        for _ in range(count): self.classes.append({'init':r.vu(),'traits':self.read_traits(r)})
        self.scripts=[]
        for _ in range(r.vu()): self.scripts.append({'init':r.vu(),'traits':self.read_traits(r)})
        self.bodies_count_pos=r.pos
        self.bodies=[]
        for _ in range(r.vu()):
            start=r.pos
            method,stack,regs,scope,maxscope=(r.vu() for x in range(5))
            code=r.take(r.vu())
            for x in range(r.vu()):
                for y in range(5):r.vu()
            self.read_traits(r)
            self.bodies.append({'start':start,'end':r.pos,'method':method,'stack':stack,'regs':regs,'scope':scope,'maxscope':maxscope,'code':code})
        assert r.pos==len(data), (r.pos,len(data))
        self.patches={}

    @staticmethod
    def read_traits(r):
        traits=[]
        for _ in range(r.vu()):
            name=r.vu(); flags=r.byte(); kind=flags&15
            if kind in (0,6):
                slot,typ,value=r.vu(),r.vu(),r.vu()
                if value:r.byte()
                trait={'name':name,'kind':kind,'slot':slot,'type':typ,'value':value}
            elif kind in (1,2,3,4,5): trait={'name':name,'kind':kind,'slot':r.vu(),'method':r.vu()}
            else: raise ValueError('Unknown trait '+str(kind))
            if flags&64:
                for x in range(r.vu()):r.vu()
            traits.append(trait)
        return traits

    def string(self,index): return self.strings[index].decode('utf-8','replace')
    def qname(self,index):
        if not index:return '*'
        kind,values=self.names[index]
        if kind in (7,13):
            namespace=self.string(self.namespaces[values[0]][1]) if values[0] else ''
            return (namespace+'.' if namespace else '')+self.string(values[1])
        if kind in (9,14,15,16):return self.string(values[0])
        return '<multiname>'

    def find_method(self,classname,methodname):
        wanted_kind=None
        if methodname.startswith('set:'):wanted_kind=3;methodname=methodname[4:]
        if methodname.startswith('get:'):wanted_kind=2;methodname=methodname[4:]
        for index,instance in enumerate(self.instances):
            if self.qname(instance['name'])==classname:
                if methodname=='<init>':return instance['init']
                if methodname=='<cinit>':return self.classes[index]['init']
                matches=[t['method'] for t in instance['traits']+self.classes[index]['traits'] if t['kind'] in (1,2,3) and (wanted_kind is None or t['kind']==wanted_kind) and self.qname(t['name']).split('.')[-1]==methodname]
                if len(matches)==1:return matches[0]
        raise KeyError((classname,methodname))
    def member_name(self,classname,name):
        for instance in self.instances:
            if self.qname(instance['name'])==classname:
                matches=[trait['name'] for trait in instance['traits'] if self.qname(trait['name']).split('.')[-1]==name]
                if len(matches)==1:return matches[0]
        raise KeyError((classname,name))

    def body(self,method):return next(b for b in self.bodies if b['method']==method)
    def add_string(self,value):
        value=value.encode('utf-8') if isinstance(value,str) else value
        if value in self.strings:return self.strings.index(value)
        self.strings.append(value);return len(self.strings)-1
    def add_qname(self,namespace,name):
        ns=(22,self.add_string(namespace))
        if ns not in self.namespaces:self.namespaces.append(ns)
        qn=(7,[self.namespaces.index(ns),self.add_string(name)])
        if qn not in self.names:self.names.append(qn)
        return self.names.index(qn)
    def existing_name(self,name):
        candidates=[i for i in range(1,len(self.names)) if self.qname(i)==name]
        if len(candidates)!=1:raise ValueError((name,candidates))
        return candidates[0]
    def set_code(self,classname,methodname,code,stack=8,regs=None):
        method=self.find_method(classname,methodname);body=self.body(method)
        if regs is None:regs=max(body['regs'],len(self.methods[method]['params'])+1)
        self.patches[method]=b''.join(u30(v) for v in (method,stack,regs,body['scope'],max(body['maxscope'],body['scope']+1),len(code)))+code+b'\0\0'
        return method
    def add_function(self,name,code,scope,stack=6,regs=4):
        existing=[index for index,method in enumerate(self.methods) if method['name'] and self.string(method['name'])==name]
        if existing:
            assert len(existing)==1
            method=existing[0]
            self.patches[method]=b''.join(u30(v) for v in (method,stack,regs,scope,scope+1,len(code)))+code+b'\0\0'
            return method
        method=len(self.methods)
        name_index=self.add_string(name)
        self.methods.append({'params':[0],'returns':0,'name':name_index,'flags':0})
        self.extra_method_info.append(u30(1)+u30(0)+u30(0)+u30(name_index)+b'\0')
        self.extra_bodies.append(b''.join(u30(v) for v in (method,stack,regs,scope,scope+1,len(code)))+code+b'\0\0')
        return method
    def prepend_simple(self,method,prefix,stack=8):
        body=self.body(method)
        if body['code'].startswith(prefix):return
        r=Reader(self.data[body['start']:body['end']])
        for _ in range(5):r.vu()
        r.take(r.vu())
        assert r.take(len(r.data)-r.pos)==b'\0\0', 'Prefix requires a method without exceptions or traits'
        code=prefix+body['code']
        self.patches[method]=b''.join(u30(v) for v in (method,max(stack,body['stack']),body['regs'],body['scope'],body['maxscope'],len(code)))+code+b'\0\0'
    def serialize(self):
        pool=u30(len(self.strings))+b''.join(u30(len(s))+s for s in self.strings[1:])
        pool+=u30(len(self.namespaces))+b''.join(bytes([k])+u30(n) for k,n in self.namespaces[1:])
        pool+=u30(len(self.sets))+b''.join(u30(len(s))+b''.join(u30(i) for i in s) for s in self.sets[1:])
        pool+=u30(len(self.names))
        for kind,values in self.names[1:]:
            pool+=bytes([kind])
            if kind==29:pool+=u30(values[0])+u30(len(values)-1)+b''.join(u30(v) for v in values[1:])
            else:pool+=b''.join(u30(v) for v in values)
        middle=u30(len(self.methods))+self.data[self.method_info_start:self.method_info_end]+b''.join(self.extra_method_info)+self.data[self.method_info_end:self.bodies_count_pos]
        bodies=u30(len(self.bodies)+len(self.extra_bodies))+b''.join(self.patches.get(b['method'],self.data[b['start']:b['end']]) for b in self.bodies)+b''.join(self.extra_bodies)
        return self.data[:self.pool_start]+pool+middle+bodies

class Assembler:
    def __init__(self):self.code=bytearray();self.labels={};self.branches=[]
    def emit(self,opcode,*operands):
        self.code.append(opcode)
        for value in operands:self.code.extend(u30(value))
        return self
    def label(self,name):self.labels[name]=len(self.code);return self
    def branch(self,opcode,label):
        self.code.append(opcode);self.branches.append((len(self.code),label));self.code.extend(b'\0\0\0');return self
    def finish(self):
        for pos,label in self.branches:
            offset=self.labels[label]-(pos+3)
            self.code[pos:pos+3]=(offset&0xffffff).to_bytes(3,'little')
        return bytes(self.code)

def swf_parts(data):
    data=expand(data);n,bit=uint(data,64,5);pos=(bit+4*n+7)//8+4
    header=data[:pos];tags=[]
    while pos<len(data):
        word=struct.unpack_from('<H',data,pos)[0];pos+=2;code,length=word>>6,word&63
        if length==63:length=struct.unpack_from('<I',data,pos)[0];pos+=4
        body=data[pos:pos+length];pos+=length;tags.append((code,body))
        if code==0:break
    assert pos==len(data)
    return header,tags

def write_swf(header,tags,path):
    payload=bytearray(header[8:])
    for code,body in tags:
        if len(body)<63:payload.extend(struct.pack('<H',(code<<6)|len(body)))
        else:payload.extend(struct.pack('<HI',(code<<6)|63,len(body)))
        payload.extend(body)
    path.write_bytes(b'CWS'+header[3:4]+struct.pack('<I',len(payload)+8)+zlib.compress(payload,6))

def patch_swf(source,dest,callback):
    header,tags=swf_parts(source.read_bytes());changes=[]
    for index,(code,body) in enumerate(tags):
        if code==82:
            start=body.index(b'\0',4)+1
            abc=ABC(body[start:]);assert abc.serialize()==body[start:], 'ABC roundtrip changed original data'
            result=callback(abc)
            if result:
                tags[index]=(code,body[:start]+abc.serialize());changes.extend(result)
    write_swf(header,tags,dest)
    return changes

if __name__=='__main__':
    import sys
    _,tags=swf_parts(Path(sys.argv[1]).read_bytes())
    for code,body in tags:
        if code==82:
            start=body.index(b'\0',4)+1;abc=ABC(body[start:])
            assert abc.serialize()==body[start:]
            for instance in abc.instances:
                name=abc.qname(instance['name'])
                if 'XPhone' in name or 'SRPG2_Main' in name or 'internalApplication' in name or 'MainTimeline' in name:
                    print(name,instance['init'])
                    for trait in instance['traits']:
                        if trait['kind'] in (1,2,3):print(' ',abc.qname(trait['name']),trait['method'])
