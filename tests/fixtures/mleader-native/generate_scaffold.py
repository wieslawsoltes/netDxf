#!/usr/bin/env python3
"""Extract exact R2007 MLEADER packets into a deterministic independent carrier."""
from pathlib import Path
import gzip, hashlib, io, json
import ezdxf
ROOT = Path(__file__).resolve().parents[3]
DEST = Path(__file__).resolve().parent

def pairs(text):
    lines=text.splitlines();return [(int(lines[i]),lines[i+1]) for i in range(0,len(lines),2)]
def chunks(tags):
    result=[]
    for tag in tags:
        if tag[0]==0:result.append([])
        result[-1].append(tag)
    return result

def ident(record):return next((value for code,value in record if code in (5,105)),None)
def field(record,code):return next((v for c,v in record if c==code),None)
source_path=ROOT/'tests/fixtures/table-oracle/sample_AC1021_ascii.dxf.gz'
source=gzip.decompress(source_path.read_bytes())
original=chunks(pairs(source.decode('utf-8-sig')))
by_handle={ident(r):r for r in original if ident(r)}
leaders=[r for r in original if r[0]==(0,'MULTILEADER')]
assert len(leaders)==15 and all((270,'     2') not in r and not any(c==270 for c,v in r) for r in leaders)
doc=ezdxf.new('R2007');doc.header['$TDCREATE']=2451545.;doc.header['$TDUPDATE']=2451545.
stream=io.StringIO();doc.write(stream);carrier=chunks(pairs(stream.getvalue()))
ids={ident(r) for r in carrier if ident(r)}
map_ids={handle:f'{0xF0000+int(handle,16):X}' for handle in ids}
for r in carrier:
    handle=ident(r)
    if r[0]==(0,'TABLE') and field(r,2) in ('STYLE','LTYPE','APPID'):
        map_ids[handle]={'STYLE':'3','LTYPE':'5','APPID':'9'}[field(r,2)]
    if r[0]==(0,'STYLE') and field(r,2)=='Standard':map_ids[handle]='11'
    if r[0]==(0,'LTYPE') and field(r,2).lower()=='byblock':map_ids[handle]='14'
    if r[0]==(0,'BLOCK_RECORD') and field(r,2)=='*Model_Space':map_ids[handle]='1F'
map_ids[doc.rootdict.dxf.handle]='C'
for record in carrier:
    for i,(code,value) in enumerate(record):
        if code in (5,105,1005) or 320<=code<=369 or 390<=code<=399 or code in (480,481):
            record[i]=(code,map_ids.get(value,value))
        if code==9 and value=='$HANDSEED':record[i+1]=(5,'F10000')
        if code==9 and value in ('$FINGERPRINTGUID','$VERSIONGUID'):record[i+1]=(2,'{00000000-0000-0000-0000-000000000001}')
        if code==9 and value in ('$TDCREATE','$TDUPDATE','$TDUCREATE','$TDUUPDATE'):record[i+1]=(40,'2451545.0')
for i,r in enumerate(carrier):
    if ident(r) in ('11','14'):carrier[i]=by_handle[ident(r)]
root=next(r for r in carrier if ident(r)=='C')
removed=set()
for name in ('ACAD_MLEADERSTYLE','EZDXF_META'):
    index=root.index((3,name));removed.add(root[index+1][1]);del root[index:index+2]
while True:
    children={ident(r) for r in carrier if field(r,330) in removed and ident(r)}
    if children<=removed:break
    removed.update(children)
carrier=[r for r in carrier if ident(r) not in removed and not (r[0]==(0,'CLASS') and field(r,1) in ('MULTILEADER','MLEADERSTYLE'))]
root.extend([(3,'ACAD_MLEADERSTYLE'),(350,'D7')])
# ezdxf derives its generated CLASS inventory from a set. Stabilize only those
# carrier records; the original native CLASS packets appended below stay exact.
class_indices=[i for i,r in enumerate(carrier) if r[0]==(0,'CLASS')]
class_records=sorted((carrier[i] for i in class_indices),key=lambda r:field(r,1))
for index,record in zip(class_indices,class_records):carrier[index]=record
section=None;table=None;result=[]
for r in carrier:
    if r[0]==(0,'SECTION'):section=field(r,2)
    if r[0]==(0,'TABLE'):table=field(r,2)
    if r[0]==(0,'ENDTAB') and table=='APPID':result.append(by_handle['107']);table=None
    if r[0]==(0,'ENDSEC'):
        if section=='ENTITIES':result.extend(leaders)
        if section=='OBJECTS':result.extend(by_handle[h] for h in ('D7','D8','E5','13CD','13CE'))
        if section=='CLASSES':result.extend(x for x in original if x[0]==(0,'CLASS') and field(x,1) in ('MULTILEADER','MLEADERSTYLE'))
        section=None
    result.append(r)
text=''.join(f'{code:3d}\n{value}\n' for record in result for code,value in record).encode('utf8')
output=DEST/'native-mleader-R2007.dxf.gz';output.write_bytes(gzip.compress(text,mtime=0))
manifest={'producer':f'ezdxf {ezdxf.__version__} minimal carrier; exact AutoCAD-source records from pinned ACadSharp corpus',
 'source_file':str(source_path.relative_to(ROOT)),'source_sha256':hashlib.sha256(source).hexdigest(),
 'fixture':output.name,'decoded_sha256':hashlib.sha256(text).hexdigest(),
 'entity_handles':[ident(r) for r in leaders],'style_handles':['D8','E5'],
 'exact_source_handles':['11','14','107','D7','D8','E5','13CD','13CE']+[ident(r) for r in leaders],
 'carrier_transformations':['New empty ezdxf R2007 carrier, deterministic dates/GUIDs, allocated handles rebased into F0000 range; generated CLASS records sorted by name.',
 'Carrier model-space BLOCK_RECORD, root DICTIONARY, STYLE/LTYPE/APPID table handles use original identities 1F/C/3/5/9.',
 'Default STYLE and ByBlock LTYPE replaced with exact original records 11 and 14; APPID 107 and style dictionary D7 with D8/E5 added.',
 'All 15 original MULTILEADER packets, extension dictionary 13CD and XRECORD 13CE copied without changing any tag or owner/reference value.',
 'Original MULTILEADER and MLEADERSTYLE CLASS records copied; unrelated original tables, entities, OBJECTS and HEADER variables excluded.']}
(DEST/'manifest.json').write_text(json.dumps(manifest,indent=2)+'\n')
print(json.dumps({'fixture':str(output),'bytes':len(text),'entities':len(leaders),'digest':manifest['decoded_sha256']}))
