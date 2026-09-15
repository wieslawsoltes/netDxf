#!/usr/bin/env python3
"""Verify DATATABLE packets, actual ownership and two exact native R2004 subgraphs.

Ezdxf has no DATATABLE typed model. Its tag reader, generic object database and
ancillary audit are used; column values and semantics are independently checked
here. This does not certify native CAD evaluation or the ambiguous point/vector
terminology in the published DXF table.
"""
from pathlib import Path
import argparse, copy, gzip, hashlib, io, json, struct
import ezdxf
from ezdxf.lldxf.encoding import decode_dxf_unicode
from ezdxf.lldxf.tagger import ascii_tags_loader, binary_tags_loader, tag_compiler
ROOT = Path(__file__).resolve().parents[1]
YEARS = {2004:'AC1018',2007:'AC1021',2010:'AC1024',2013:'AC1027',2018:'AC1032'}
TYPE_CODE = {1:93,2:40,3:3,4:10,5:331,6:360,7:350,8:340,9:330,10:71,11:11}

def check(condition, message):
    if not condition: raise ValueError(message)

def records(data, encoding='utf-8-sig'):
    loader = binary_tags_loader(data) if data.startswith(b'AutoCAD Binary DXF') else ascii_tags_loader(io.StringIO(data.decode(encoding), newline=None))
    result = {}; current = []
    def flush():
        if not current: return
        prefix = []
        for tag in current[1:]:
            if tag.code in (100,1001): break
            if tag.code in (5,105): prefix.append(tag.value)
        if not prefix: return
        check(len(prefix)==1, 'Duplicate physical identity')
        handle = prefix[0].upper(); check(handle not in result, 'Duplicate physical handle')
        result[handle] = [(tag.code, tag.value) for tag in current]
    for tag in tag_compiler(loader):
        if tag.code == 0: flush(); current = []
        current.append(tag)
    flush(); return result

def text(value): return decode_dxf_unicode(value).encode('utf-16-le','surrogatepass').decode('utf-16-le')

def first(tags, code): return next(value for tag,value in tags if tag==code)
def body(tags):
    start = next(i for i,t in enumerate(tags) if t[0]==100)
    end = next((i for i in range(start,len(tags)) if tags[i][0]==1001),len(tags))
    return tags[start:end]
def packet(tags):
    values = body(tags); index = 0
    def take(code):
        nonlocal index
        check(index < len(values) and values[index][0]==code, f'Expected DATATABLE group {code} at {index}')
        value=values[index][1];index+=1;return value
    check(take(100)=='AcDbDataTable','Subclass differs');check(take(70)==2,'Stored version differs')
    count,rows,name=take(90),take(91),text(take(1))
    check(0<=count<=1048576 and 0<=rows<=1048576 and count*rows<=1048576,'Dimensions invalid')
    columns=[]
    for column in range(count):
        kind=take(92);check(kind in TYPE_CODE,'Unknown stored cell type');title=text(take(2));cells=[]
        for row in range(rows):
            value=take(TYPE_CODE[kind])
            if kind==3:value=text(value)
            if kind in (4,11):check(len(value)==3,'All coordinates required');value=tuple(value)
            if kind==10:check(value in (0,1),'Boolean outside public range')
            cells.append(value)
        columns.append((kind,title,cells))
    check(index==len(values),'Trailing/repeated public fields')
    return rows,name,columns

def audit(doc):
    result=doc.audit();check(not result.errors and not result.fixes,'Ancillary audit changed objects: '+str(result.errors+result.fixes))

def read(path, year, binary):
    data=path.read_bytes();check(data.startswith(b'AutoCAD Binary DXF')==binary,'Transport differs')
    doc=ezdxf.readfile(path);check(doc.dxfversion==YEARS[year],'Profile differs')
    return doc,records(data)

def authored(path,year,binary,clone=False):
    doc,wire=read(path,year,binary);graph=doc.rootdict['DATA_TABLES'];main=graph['TABLE'];handle=main.dxf.handle
    check(main is graph['ALIAS'],'Alias identity differs');check(main.dxf.owner==graph.dxf.handle,'Table owner differs')
    check(set(graph.keys())=={'TABLE','ALIAS','POINTER','EMPTY','ZERO_ROWS','ZERO_COLUMNS'},'Dictionary inventory differs')
    rows,name,columns=packet(wire[handle]);check((rows,name)==(3,'Ω table \\U+0041 🧪'),'Table dimensions or name differ')
    pointer=graph['POINTER'].dxf.handle;hard=columns[5][2][0];soft=columns[6][2][1]
    expected=[(1,'',[-2147483648,0,2147483647]),(2,'Duplicate',[-2.5,0.0,1.25e30]),(3,'Duplicate',['','青 🧪',r'Literal\U+0042']),
      (4,'Point',[(1,2,3),(-4,5,-6),(7,8,9)]),(5,'Id',[pointer,'0',pointer]),(6,'Hard owner',[hard,'0','0']),
      (7,'Soft owner',['0',soft,'0']),(8,'Hard pointer',[pointer,hard,'0']),(9,'Soft pointer',[soft,'0',pointer]),
      (10,'Boolean',[0,1,0]),(11,'Vector',[(10,20,30),(-40,50,-60),(70,80,90)])]
    check(columns==expected,'Exact cell values/type/order or column names differ')
    check(packet(wire[graph['EMPTY'].dxf.handle])==(0,'',[]),'Empty table differs')
    check(packet(wire[graph['ZERO_COLUMNS'].dxf.handle])==(7,'',[]),'Zero-column rows lost')
    check(packet(wire[graph['ZERO_ROWS'].dxf.handle])==(0,'',[(3,'',[]),(1,'',[])]),'Zero-row columns lost')
    for owned,content in ((hard,'Hard-owned payload'),(soft,'Soft-owned payload')):
        check(doc.entitydb[owned].dxftype()=='XRECORD' and doc.entitydb[owned].dxf.owner==handle,'Actual declared ownership differs')
        check(list(doc.entitydb[owned].tags)==[(1,content)],'Owned payload differs')
    for kind,title,cells in columns:
        if 5<=kind<=9:
            for reference in cells:check(reference=='0' or reference in wire,'Unresolved physical cell reference')
    check(main.get_reactors()==[graph.dxf.handle],'Persistent reactor differs')
    check([(tag.code,text(tag.value) if tag.code==1000 else tag.value) for tag in main.get_xdata('DATATABLE_APP')]==[(1000,'Metadata 青'),(1005,pointer)],'XData differs')
    extension=main.get_extension_dict().dictionary
    check(extension.dxf.owner==handle and extension['NOTE'].dxf.value=='Metadata','Extension graph differs')
    definition=doc.classes.get('DATATABLE').dxf
    check((definition.cpp_class_name,definition.app_name,definition.flags,definition.instance_count)==('AcDbDataTable','ObjectDBX Classes',0,4),'CLASS declaration/count differs')
    check(len(list(doc.modelspace()))==(0 if clone else 1),'Following geometry inventory differs')
    if not clone:
        line=next(iter(doc.modelspace()));check(tuple(line.dxf.start)==(1,2,3) and tuple(line.dxf.end)==(4,5,6),'Following line changed')
    audit(doc)
    return doc,wire

def canonical(tags):
    def value(item):
        if isinstance(item,float):return ('double',struct.pack('>d',item).hex())
        if isinstance(item,bytes):return ('bytes',item.hex())
        if isinstance(item,(tuple,list)):return tuple(value(v) for v in item)
        return item
    return [(code,value(item)) for code,item in tags]

def native(path,handle,binary,source):
    doc,wire=read(path,2004,binary);main=doc.rootdict['NATIVE_DATA_TABLE'];check(main.dxf.handle==handle,'Native identity differs')
    expected=copy.deepcopy(source[handle]);owner=main.dxf.owner
    expected=[(code,owner if code==330 else value) for code,value in expected]
    check(canonical(wire[handle])==canonical(expected),'Native DATATABLE complete body differs')
    rows,name,columns=packet(wire[handle]);check(rows==(21 if handle=='143D' else 20) and name=='' and [c[0] for c in columns]==[2,1,1,6],'Native schema differs')
    children=columns[3][2];check(len(set(children))==rows,'Native ownership multiplicity differs')
    for child in children:
        check(canonical(wire[child])==canonical(source[child]),'Native owned XRecord body differs: '+child)
        check(doc.entitydb[child].dxf.owner==handle,'Native owner differs')
    audit(doc)

def main():
    parser=argparse.ArgumentParser(description=__doc__);parser.add_argument('artifacts',type=Path);args=parser.parse_args();count=0
    for year in YEARS:
        for binary in (False,True):
            authored(args.artifacts/f'datatable-AutoCad{year}-{binary}.dxf',year,binary);count+=1
            authored(args.artifacts/f'datatable-clone-AutoCad{year}-{binary}.dxf',year,binary,True);count+=1
            doc,wire=read(args.artifacts/f'datatable-erased-AutoCad{year}-{binary}.dxf',year,binary)
            check(not any(r[0]==(0,'DATATABLE') for r in wire.values()),'Erased DATATABLE remains')
            check(doc.classes.get('DATATABLE').dxf.instance_count==0,'Erased CLASS count stale');audit(doc);count+=1
    file='sample_AC1018_ascii.dxf';metadata=json.loads((ROOT/'tools/table_oracle/fixtures.json').read_text());expected=next(f['sha256'] for f in metadata['files'] if f['file']==file)
    data=gzip.decompress((ROOT/'tests/fixtures/table-oracle'/(file+'.gz')).read_bytes());check(hashlib.sha256(data).hexdigest()==expected,'Pinned native source hash differs');source=records(data,'cp1252')
    for handle in ('143D','145B'):
        for binary in (False,True):native(args.artifacts/f'datatable-native-{handle}-{binary}.dxf',handle,binary,source);count+=1
    # Independent negative controls prove the verifier actually checks value and dimension grammar.
    _,wire=read(args.artifacts/'datatable-AutoCad2018-False.dxf',2018,False)
    target=next(r for r in wire.values() if r[0]==(0,'DATATABLE') and packet(r)[0]==3)
    controls=0
    for code,bad in ((71,2),(90,12)):
        corrupt=copy.deepcopy(target);index=next(i for i,t in enumerate(corrupt) if t[0]==code);corrupt[index]=(code,bad)
        try:packet(corrupt)
        except ValueError:controls+=1
        else:raise ValueError('Corruption control was accepted')
    check(count==34 and controls==2,'Mandatory inventory differs')
    print(json.dumps({'outputs':count,'native_tables':2,'native_owned_xrecords':41,'negative_controls':controls,'audit_errors':0,'audit_fixes':0,'native_cad_execution':False},sort_keys=True))

if __name__=='__main__':main()
