#!/usr/bin/env python3
"""Inventory candidate VPORT and VIEWPORT tags; this is evidence, not a schema gate.

Requires ezdxf. Reads raw tags without typed entity projection. Codes 331, 341,
and 441 are recorded with their actual section/subclass/application/XData scope;
their presence is not interpreted as proof of frozen-layer semantics.
"""
from pathlib import Path
from collections import Counter
import argparse
import gzip
import hashlib
import io
import json
from ezdxf.lldxf.tagger import ascii_tags_loader, binary_tags_loader
from ezdxf.lldxf.tagwriter import BinaryTagWriter

def scan(path,base):
    data=path.read_bytes(); compressed=path.suffix=='.gz'
    if compressed: data=gzip.decompress(data)
    result = scan_data(data)
    return {'file':str(path.relative_to(base)),'sha256':hashlib.sha256(data).hexdigest(),
            'bytes':len(data),'compressed':compressed, **result}

def scan_data(data):
    binary=data.startswith(b'AutoCAD Binary DXF')
    tags=list(binary_tags_loader(data) if binary else ascii_tags_loader(io.StringIO(data.decode('latin1'), newline=None)))
    rows=[]; row=[]
    for t in tags:
        code,value=t.code,t.value
        if code==0:
            if row: rows.append(row)
            row=[]
        row.append((code,value))
    if row: rows.append(row)
    section=None; vports=[]; viewports=[]; layers={}; profile=None
    def first(row,c): return next((v for k,v in row if k==c),None)
    for row in rows:
        if row[0]==(0,'SECTION'): section=first(row,2)
        elif row[0]==(0,'ENDSEC'): section=None
        if row[0]==(0,'SECTION') and section=='HEADER':
            profile=next((row[i+1][1] for i,t in enumerate(row[:-1]) if t==(9,'$ACADVER')),None)
        if row[0]==(0,'LAYER') and section=='TABLES':
            h=first(row,5)
            if h: layers[int(h,16)]=first(row,2)
        if row[0][1] in ('VPORT','VIEWPORT'):
            fields=[]; subclass=None; appdepth=0; xdata=False
            for code,value in row:
                if code==1001: xdata=True
                if code==100: subclass=value
                if code==102:
                    if str(value).startswith('{'): appdepth+=1
                    elif value=='}': appdepth=max(0,appdepth-1)
                if code in (331,341,441):
                    fields.append({'code':code,'value':value,'subclass':subclass,'applicationDepth':appdepth,'xdata':xdata})
            r={'section':section,'handle':first(row,5),'name':first(row,2),'fields':fields}
            (vports if row[0][1]=='VPORT' else viewports).append(r)
    for r in vports+viewports:
        for f in r['fields']:
            if f['code'] in (331,341):
                try:f['layerName']=layers.get(int(f['value'],16))
                except (ValueError,TypeError):f['layerName']=None
    return {'binary':binary,'profile':profile,'vports':vports,'viewports':viewports}

def inventory(base,paths):
    files=[];failures=[]
    for path in paths:
        try: files.append(scan(path,base))
        except Exception as ex:failures.append({'file':str(path.relative_to(base)),'error':type(ex).__name__+': '+str(ex)})
    def n(kind):return sum(len(f[kind]) for f in files)
    def fields(kind):return sum(len(r['fields']) for f in files for r in f[kind])
    return {'summary':{'candidates':len(paths),'parsed':len(files),'failures':failures,'vports':n('vports'),'viewports':n('viewports'),'vportCandidateFields':fields('vports'),'viewportCandidateFields':fields('viewports'),'profiles':dict(Counter(f['profile'] for f in files))},'files':files}

def controls():
    tags = [(0, 'SECTION'), (2, 'HEADER'), (9, '$ACADVER'), (1, 'AC1032'),
            (0, 'ENDSEC'), (0, 'SECTION'), (2, 'TABLES'),
            (0, 'LAYER'), (5, 'AB'), (2, 'Control'),
            (0, 'VPORT'), (5, 'BC'), (100, 'AcDbViewportTableRecord'),
            (2, '*ACTIVE'), (331, 'AB'), (331, 'AB'), (341, 'AB'), (441, 171),
            (102, '{CONTROL'), (331, 'AB'), (102, '}'),
            (100, 'PrivateSubclass'), (331, 'AB'), (1001, 'CONTROL'), (1000, 'AB'),
            (0, 'ENDSEC'), (0, 'SECTION'), (2, 'ENTITIES'),
            (0, 'VIEWPORT'), (5, 'CD'), (100, 'AcDbViewport'), (331, 'AB'),
            (0, 'ENDSEC'), (0, 'EOF')]
    encodings = []
    for newline in ('\n', '\r\n'):
        encodings.append(''.join(str(c)+newline+str(v)+newline for c,v in tags).encode('ascii'))
    stream = io.BytesIO()
    writer = BinaryTagWriter(stream)
    writer.write_signature()
    for c,v in tags: writer.write_tag2(c,v)
    encodings.append(stream.getvalue())
    for data in encodings:
        result = scan_data(data)
        assert result['profile'] == 'AC1032'
        assert len(result['vports']) == len(result['viewports']) == 1
        vport = result['vports'][0]
        assert vport['section'] == 'TABLES' and vport['handle'] == 'BC'
        fields = vport['fields']
        assert [f['code'] for f in fields] == [331,331,341,441,331,331]
        assert [f['layerName'] for f in fields if f['code'] != 441] == ['Control']*5
        assert fields[4]['applicationDepth'] == 1
        assert fields[5]['subclass'] == 'PrivateSubclass'
        assert result['viewports'][0]['section'] == 'ENTITIES'
        # A populated VIEWPORT alone cannot count as a VPORT relationship.
        filtered = [(c,v) for c,v in tags]
        first = filtered.index((0,'VPORT'))
        end = filtered.index((0,'ENDSEC'), first)
        del filtered[first:end]
        text = ''.join(str(c)+'\n'+str(v)+'\n' for c,v in filtered).encode('ascii')
        separate = scan_data(text)
        assert not separate['vports'] and len(separate['viewports'][0]['fields']) == 1
    return 6

def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('directory', type=Path)
    parser.add_argument('--output', required=True, type=Path)
    args = parser.parse_args()
    if not args.directory.is_dir(): parser.error('directory must exist')
    count = controls()
    paths = sorted(p for p in args.directory.rglob('*')
                   if p.is_file() and (p.name.endswith('.dxf') or p.name.endswith('.dxf.gz')))
    if not paths: parser.error('directory contains no DXF candidates')
    result = inventory(args.directory, paths)
    result['scannerControls'] = count
    args.output.parent.mkdir(parents=True, exist_ok=True)
    args.output.write_text(json.dumps(result, indent=2)+'\n')
    print(json.dumps(result['summary'], indent=2))
    print('PASS ' + str(count) + ' scanner controls (synthetic, not interoperability evidence)')
    if result['summary']['failures']: raise SystemExit(1)

if __name__ == '__main__': main()
