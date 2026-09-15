#!/usr/bin/env python3
"""Independently compare all native TABLECONTENT packets and exposed identities.

Two whole-native ezdxf examples and three scoped ACadSharp carriers are distinct
qualification modes. This verifies stored packets, not cell or solar evaluation.
"""
import argparse
import copy
import gzip
import hashlib
import io
import json
from pathlib import Path
import struct
import sys
from ezdxf.lldxf.tagger import ascii_tags_loader, tag_compiler

NAMES = ['AcDbLinkedData','AcDbLinkedTableData','AcDbFormattedTableData','AcDbTableContent']
FILES = ['acad_table_simple.dxf','acad_table_with_blk_ref.dxf','sample_AC1018_ascii.dxf','sample_AC1021_ascii.dxf','sample_AC1024_ascii.dxf']


def check(test,message):
    if not test: raise ValueError(message)


def exact(value):
    if isinstance(value,float): return struct.pack('>d',value)
    if isinstance(value,(list,tuple)): return tuple(exact(v) for v in value)
    return value


def payload(record):
    start=next(i for i,t in enumerate(record) if t[0]==100)
    end=next((i for i in range(start,len(record)) if record[i][0]==1001),len(record))
    return [(code,exact(value)) for code,value in record[start:end]]


def source(repo,file):
    entry=next(f for f in json.loads((repo/'tools/table_oracle/fixtures.json').read_text())['files'] if f['file']==file)
    data=gzip.decompress((repo/'tests/fixtures/table-oracle'/(file+'.gz')).read_bytes())
    check(hashlib.sha256(data).hexdigest()==entry['sha256'],'Native TABLECONTENT source SHA256 differs')
    tags=tag_compiler(ascii_tags_loader(io.StringIO(data.decode('utf8' if entry['profile']>='AC1021' else 'cp1252'),newline=None)))
    records,classes,current={},[],[]
    def finish():
        if not current:return
        if current[0]==(0,'CLASS'): classes.append(list(current));return
        start=next((i for i,t in enumerate(current) if t[0] in (100,1001)),len(current))
        handle=next((v for c,v in current[:start] if c in (5,105)),None)
        if handle is not None:records[handle]=list(current)
    for tag in tags:
        if tag.code==0:finish();current=[]
        current.append((tag.code,tag.value))
    finish()
    year={'AC1018':2004,'AC1021':2007,'AC1024':2010,'AC1027':2013,'AC1032':2018}[entry['profile']]
    return records,classes,year


def plain_records(data,profile):
    rows={};current=[]
    def finish():
        identity=next((v for c,v in current if c in (5,105)),None)
        if identity is not None: rows[identity]=list(current)
    for tag in tag_compiler(ascii_tags_loader(io.StringIO(data.decode('utf8' if profile>='AC1021' else 'cp1252'),newline=None))):
        if tag.code==0: finish();current=[]
        current.append((tag.code,tag.value))
    finish();return rows


def verify_extraction(repo,entry):
    original=gzip.decompress((repo/'tests/fixtures/table-oracle'/(entry['file']+'.gz')).read_bytes())
    check(hashlib.sha256(original).hexdigest()==entry['source_sha256'],'Extraction original SHA differs')
    if entry['file'].startswith('acad_'):
        check('sha256' not in entry and 'records' not in entry,'Whole source unexpectedly transformed')
        check((repo/'tests/fixtures/table-content'/entry['fixture']).resolve()==(repo/'tests/fixtures/table-oracle'/(entry['file']+'.gz')).resolve(),'Whole source fixture path differs')
        return 0
    fixture=(repo/'tests/fixtures/table-content'/entry['fixture']).read_bytes()
    check(hashlib.sha256(fixture).hexdigest()==entry['sha256'],'Extracted fixture SHA differs')
    before=plain_records(original,entry['profile']);after=plain_records(fixture,entry['profile'])
    selected=entry['records'];expected_count=136 if entry['profile']=='AC1018' else 90
    check(len(selected)==expected_count and len({r['handle'] for r in selected})==expected_count,'Native selected-record inventory differs')
    for row in selected:
        h=row['handle'];expected=list(before[h]);boundary=next((i for i,t in enumerate(expected) if t[0]==100),len(expected))
        check(expected[0]==(0,row['type']),'Manifest source type differs')
        check(len({c['index'] for c in row['changes']})==len(row['changes']),'Manifest repeats change index')
        for c in row['changes']:
            i=c['index'];check(c['code']==330 and i<boundary and expected[i]==(330,c['before']),'Only explicit common-owner/reactor transformations are allowed')
            check(c['before']!=c['after'],'Manifest lists an identity transformation')
            expected[i]=(330,c['after'])
        check(exact(after[h])==exact(expected),'Extracted native packet differs outside its explicit mapping: '+h)
    for row in selected:
        for code,target in after[row['handle']]:
            if 330<=code<=369 or 390<=code<=399 or code in (480,481,1005):
                if int(target,16)!=0: check(target in after,'Retained native dependency lacks a physical record: '+target)
    check(set(entry['content_handles'])=={h for h,r in before.items() if r[0]==(0,'TABLECONTENT')},'Manifest content inventory differs')
    check(all(not r['changes'] for r in selected if r['type']=='TABLECONTENT'),'Native TABLECONTENT payload or owner was transformed')
    return len(selected)


def opaque_expected(variant):
    body=[(100,'AcDbLinkedData'),(1,'Stored name'),(300,'Stored description'),(100,'AcDbLinkedTableData'),(90,0),(91,0),(92,0),(100,'AcDbFormattedTableData'),(100,'AcDbTableContent'),(340,'0')]
    prefix=[]
    if variant==0: body[0]=(100,'PrivateLinkedData')
    if variant==1: body.append((100,'PrivateContent'))
    if variant==2: prefix=[(102,'{PRIVATE'),(1000,'header private'),(102,'}')]
    if variant==4: body[1:1]=[(102,'{PRIVATE'),(1000,'payload private'),(102,'}')]
    if variant==5: body.extend([(100,'PrivateContent'),(1000,'subclass private')])
    if variant==6: prefix=[(300,'private common value')]
    if variant==7: body.extend([(100,'PrivateContent'),(340,'EEEEEEEE')])
    return prefix,body


def verify_opaque(records,variant,metadata,dictionary_edges):
    contents=[r for r in records.values() if r[0]==(0,'TABLECONTENT')];check(len(contents)==1,'Opaque TABLECONTENT inventory differs')
    row=contents[0];start=next(i for i,t in enumerate(row) if t[0]==100);prefix,body=opaque_expected(variant)
    actual_prefix=[t for t in row[:start] if t[0] not in (0,5,330)]
    check(actual_prefix==prefix and row[start:]==body,'Whole opaque TABLECONTENT packet differs')
    owner=next(v for c,v in row[:start] if c==330);identity=next(v for c,v in row if c==5)
    check(owner in records and ('CONTENT',360,identity) in dictionary_edges(records[owner]),'Opaque owning dictionary identity differs')


def semantic_refs(record):
    start=next(i for i,t in enumerate(record) if t[0]==100)
    end=next((i for i in range(start,len(record)) if record[i][0]==1001),len(record))
    return [(c,v) for c,v in record[start:end] if (330<=c<=369 or 390<=c<=399 or c in (480,481)) and int(v,16)!=0]


def exposed_refs(record):
    """Include common owners, reactors and XData in physical-closure checks."""
    return [(code, target) for code, target in record
            if (330 <= code <= 369 or 390 <= code <= 399 or code in (480, 481, 1005))
            and int(target, 16) != 0]


def verify_carrier_graph(records, carrier, selected):
    """Check retained source identities without claiming complete carrier equality.

    Ordinary typed resource writers can normalize optional source fields. Every
    selected record and every original exposed dependency must still exist with
    its actual source type, and no emitted selected record may dangle.
    """
    for row in selected:
        handle = row['handle']
        check(handle in records, 'Retained carrier record is missing: ' + handle)
        check(records[handle][0] == carrier[handle][0],
              'Retained carrier record type differs: ' + handle)
        for _, target in exposed_refs(carrier[handle]):
            check(target in records, 'Original carrier dependency is missing: ' + target)
            check(records[target][0] == carrier[target][0],
                  'Original carrier dependency type differs: ' + target)
        for _, target in exposed_refs(records[handle]):
            check(target in records, 'Emitted carrier dependency is missing: ' + target)


def symbol_name(record):
    code=3 if record[0]==(0,'TABLESTYLE') else 2
    return next((v for c,v in record if c==code),None)


def verify_native(records,classes,original,file,metadata,dictionary_edges):
    contents={h:r for h,r in original.items() if r[0]==(0,'TABLECONTENT')}
    check(contents,'Native source contains no TABLECONTENT')
    check({h for h,r in records.items() if r[0]==(0,'TABLECONTENT')}==set(contents),'Native TABLECONTENT identity inventory differs')
    for handle,before in contents.items():
        after=records[handle]
        check([v for c,v in after if c==100]==NAMES,'TABLECONTENT subclass order differs')
        check(payload(after)==payload(before),'Exact TABLECONTENT payload differs: '+handle)
        wrapper=metadata(before)[0]
        check(metadata(after)[0]==wrapper,'TABLECONTENT common owner differs: '+handle)
        check(wrapper in records and records[wrapper][0]==(0,'XRECORD'),'TABLECONTENT wrapper identity/type differs')
        check(payload(records[wrapper])==payload(original[wrapper]),'Native wrapper payload differs')
        owner,reactors,_=metadata(records[wrapper])
        if file.startswith('acad_'):
            check(metadata(records[wrapper])==metadata(original[wrapper]),'Whole-native wrapper metadata differs')
        else:
            check(owner in records and records[owner][0]==(0,'DICTIONARY'),'Scoped wrapper owner is not a retained dictionary')
            check(reactors==[owner],'Scoped wrapper reactor mapping differs')
        check(any(h==wrapper for _,_,h in dictionary_edges(records[owner])),'Wrapper lacks an owning dictionary entry')
        refs=semantic_refs(before)
        check(semantic_refs(after)==refs,'Ordered TABLECONTENT exposed references differ')
        for code,target in refs:
            check(target in records,'TABLECONTENT dependency is missing: '+target)
            check(target in original,'Native TABLECONTENT dependency lacks a source record: '+target)
            check(records[target][0]==original[target][0],'TABLECONTENT source target type differs: '+target)
            if original[target][0][1] in ('STYLE','LTYPE','BLOCK_RECORD','TABLESTYLE'):
                check(symbol_name(records[target])==symbol_name(original[target]),'TABLECONTENT source resource name differs: '+target)
        terminal=next(i for i,t in enumerate(after) if t==(100,'AcDbTableContent'))
        check(after[terminal:terminal+2]==[(100,'AcDbTableContent'),(340,refs[-1][1])],'Terminal TABLESTYLE slot differs')
        check(records[refs[-1][1]][0]==(0,'TABLESTYLE'),'Terminal TABLECONTENT target is not TABLESTYLE')
    class_rows=[r for r in classes if (1,'TABLECONTENT') in r]
    check(len(class_rows)==1,'Expected one TABLECONTENT CLASS')
    cls=class_rows[0]
    for tag in [(2,'AcDbTableContent'),(3,'ObjectDBX Classes'),(90,1152),(91,len(contents)),(280,0),(281,0)]:
        check(tag in cls,'TABLECONTENT CLASS metadata differs: '+repr(tag))


def replace(rows,code,value,last=False):
    indices=[i for i,t in enumerate(rows) if t[0]==code];check(indices,'Corruption target code is absent')
    rows[indices[-1] if last else indices[0]]=(code,value)


def main():
    parser=argparse.ArgumentParser(description=__doc__);parser.add_argument('directory',type=Path)
    parser.add_argument('--repository',type=Path,default=Path(__file__).resolve().parents[1]);args=parser.parse_args()
    sys.path.insert(0,str(args.repository/'tools'))
    from verify_fourth_mixed_modules import load,metadata,dictionary_edges
    expected={f'table-content-native-{file}-{binary}.dxf' for file in FILES for binary in (False,True)}
    check({p.name for p in args.directory.glob('table-content-native-*.dxf')}==expected,'All ten native TABLECONTENT outputs are mandatory')
    manifest=json.loads((args.repository/'tests/fixtures/table-content/manifest.json').read_text())
    check(manifest['schema']==1 and manifest['original_transformations']==[],'Fixture manifest schema/source transformations differ')
    check({r['file'] for r in manifest['files']}==set(FILES),'All five source fixture manifests are mandatory')
    selected=sum(verify_extraction(args.repository,entry) for entry in manifest['files'])
    check(selected==316,'Native selected source record count differs')
    opaque={f'table-content-opaque-{variant}-{binary}.dxf' for variant in range(8) for binary in (False,True)}
    check({p.name for p in args.directory.glob('table-content-opaque-*.dxf')}==opaque,'All sixteen opaque TABLECONTENT outputs are mandatory')
    controls=0;packet_count=0;carrier_controls=0;retained_records=0
    for file in FILES:
        original,source_classes,year=source(args.repository,file)
        entry=next(row for row in manifest['files'] if row['file']==file)
        selected_records=entry.get('records',[])
        carrier=plain_records((args.repository/'tests/fixtures/table-content'/entry['fixture']).read_bytes(),entry['profile']) if selected_records else {}
        for binary in (False,True):
            name=f'table-content-native-{file}-{binary}.dxf';doc,records,classes=load(args.directory/name,year,binary)
            audit=doc.audit();check(not audit.errors and not audit.fixes,'Native output audit errors or repairs: '+name)
            verify_native(records,classes,original,file,metadata,dictionary_edges)
            verify_carrier_graph(records,carrier,selected_records)
            retained_records+=len(selected_records)
            if selected_records:
                indirect=next(row['handle'] for row in selected_records if row['type']=='INSERT')
                for fault in range(3):
                    bad=copy.deepcopy(records)
                    if fault==0:bad.pop(indirect)
                    elif fault==1:bad[indirect][0]=(0,'DICTIONARY')
                    else:bad[indirect].append((1005,'FFFFFFFFFFFFFFFE'))
                    try:verify_carrier_graph(bad,carrier,selected_records)
                    except (ValueError,KeyError,StopIteration):carrier_controls+=1
                    else:raise ValueError('Indirect carrier corruption control accepted: '+str(fault))
            contents=[h for h,r in original.items() if r[0]==(0,'TABLECONTENT')];packet_count+=len(contents)
            for variant in range(8):
                bad=copy.deepcopy(records);bad_classes=copy.deepcopy(classes);content=contents[0];style=semantic_refs(original[content])[-1][1];wrapper=metadata(original[content])[0]
                if variant==0:replace(bad[content],90,987654)
                if variant==1:replace(bad[content],1,'altered stored text')
                if variant==2:replace(bad[content],330,'0')
                if variant==3:bad.pop(style)
                if variant==4:bad[style][0]=(0,'DICTIONARY')
                if variant==5:replace(bad[content],340,'0',True)
                if variant==6:replace(next(r for r in bad_classes if (1,'TABLECONTENT') in r),90,1025)
                if variant==7:replace(bad[wrapper],90,987654)
                try:verify_native(bad,bad_classes,original,file,metadata,dictionary_edges)
                except (ValueError,KeyError,StopIteration):controls+=1
                else:raise ValueError('TABLECONTENT corruption control accepted: '+str(variant))
            print('PASS '+name)
    check(packet_count==16 and controls==80,'TABLECONTENT packet/control inventory differs')
    for variant in range(8):
        for binary in (False,True):
            name=f'table-content-opaque-{variant}-{binary}.dxf'
            doc,records,_=load(args.directory/name,2000 if variant==3 else 2018,binary)
            audit=doc.audit();check(not audit.errors and not audit.fixes,'Opaque output audit errors or repairs: '+name)
            verify_opaque(records,variant,metadata,dictionary_edges)
            for fault in (0,1):
                bad=copy.deepcopy(records);row=next(r for r in bad.values() if r[0]==(0,'TABLECONTENT'))
                replace(row,1,'changed private text') if fault==0 else replace(row,340,'1',True)
                try:verify_opaque(bad,variant,metadata,dictionary_edges)
                except (ValueError,KeyError,StopIteration):controls+=1
                else:raise ValueError('Opaque TABLECONTENT corruption accepted')
            print('PASS '+name)
    check(controls==112 and carrier_controls==18,'Complete TABLECONTENT corruption inventory differs')
    check(retained_records==632,'Both transports must retain all 316 selected source identities')
    print('PASS 26 outputs, 16 exact native packets, 316 manifest-qualified source records retained in both transports, exact opaque variants, and 130 corruption controls')


if __name__=='__main__':main()
